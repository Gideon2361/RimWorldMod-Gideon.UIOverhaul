using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// Every typeface a control may be told to draw in.
    ///
    /// <b>This enum is the whole registry.</b> A control names a face here and never learns where the glyphs
    /// came from, which is what lets one be swapped for another without touching the control. Adding a face is
    /// two steps and no more: bake it into <c>Fonts/</c>, then add a member here and its two lines in
    /// <see cref="UIFaces"/>. Everything that picks a face from a list is generated from this enum, so a new
    /// member appears in the options picker on its own.
    ///
    /// <b>One member per family, and weight is not a member.</b> It was, briefly: a sheet held one weight at one
    /// slant, so bold and italic had to be faces in their own right. They are not any more, because a sheet now
    /// carries regular, bold, italic and both together, tagged per glyph -- so a caller asks for Barlow Condensed
    /// and, separately, for bold. That is what lets a rich text tag switch weight mid-sentence, which a face
    /// chosen for the whole label never could.
    ///
    /// <b>Thin is the exception and has to be.</b> <c>FontStyle</c> offers bold and italic and nothing else, so
    /// a weight lighter than regular cannot be asked for by style and must be asked for by name.
    ///
    /// <b><see cref="Game"/> is a real option and the default.</b> It means RimWorld's own text, drawn by
    /// <c>Widgets.Label</c> as it always was. Keeping it in the enum rather than treating a null face as vanilla
    /// means a control can offer the game's font as one choice among several, and means an unavailable sheet has
    /// somewhere to fall back to that the caller already understands.
    ///
    /// The name is <c>UIFace</c> rather than <c>UIFont</c> because <see cref="UIFonts"/> already answers a
    /// different question -- how tall a line of RimWorld's text will be -- and two types a letter apart is how
    /// the wrong one gets called.
    /// </summary>
    internal enum UIFace
    {
        /// <summary>RimWorld's own interface font, at whatever size was asked for.</summary>
        Game,

        /// <summary>Barlow Condensed. Narrow, so a long label fits where the game's font would not.</summary>
        BarlowCondensed,

        /// <summary>
        /// Barlow Condensed Thin. Very light, and the one face here that needs judgement about size.
        ///
        /// A thin condensed stem is well under a pixel at interface sizes, so it survives as a grey suggestion
        /// of a letter rather than a letter. It reads at Medium and above and is worth looking at before being
        /// used anywhere smaller.
        ///
        /// <b>Its own face rather than a weight of the one above,</b> because no markup or style flag reaches
        /// Thin -- <c>FontStyle</c> has bold and italic and nothing else. A weight that cannot be asked for by
        /// style has to be asked for by name.
        /// </summary>
        BarlowCondensedThin,

        /// <summary>
        /// Cascadia Mono. Fixed width, so digits and anything tabular line up in a column.
        ///
        /// The widest coverage of anything shipped here by a long way -- 2,424 glyphs, which is most of a
        /// megabyte of sheet before compression. Worth knowing before it is used for one label.
        /// </summary>
        CascadiaMono,

        /// <summary>
        /// Hammersmith One. Wide and geometric, for a heading that wants to be a heading.
        ///
        /// Its own sheet rather than the floor labels', which is the same typeface baked at 64 for a mesh drawn
        /// across a room. This one is baked for text at interface size.
        /// </summary>
        HammersmithOne,

        /// <summary>IBM Plex Mono. Fixed width, and quieter than Cascadia at the same size.</summary>
        IBMPlexMono,

        /// <summary>
        /// Oswald. Condensed and tall.
        ///
        /// <b>It draws smaller than the others at the same <c>GameFont</c>, and that is arithmetic rather than a
        /// fault.</b> Every face is scaled so one line occupies RimWorld's line height, and Oswald's own line
        /// box is 1.48 ems against Barlow's 1.20 -- so fitting it into the same height leaves the letters about
        /// a fifth smaller. Reach for a larger <c>GameFont</c> with this one.
        /// </summary>
        Oswald
    }

    /// <summary>
    /// What each <see cref="UIFace"/> is made of, and what to call it.
    ///
    /// <b>The sheets are loaded once and kept.</b> Each atlas reads its PNG and metrics table on first use and
    /// holds both for the session, so a face costs its files the first time a control asks for it and nothing
    /// afterwards. A face nobody draws in is never read off disk at all, which is why the four Barlow weights
    /// are separate atlases rather than one object holding all four.
    ///
    /// <b>To add a face:</b> bake it (see <c>ThirdParty/Fonts/README-Gideon.md</c>), add a member to
    /// <see cref="UIFace"/>, add its atlas field and its <see cref="AtlasFor"/> case here, and give it a display
    /// name in <see cref="Named"/>. Nothing else needs touching.
    /// </summary>
    internal static class UIFaces
    {
        /// <summary>
        /// Every sheet read so far, by file name. A face may have several.
        /// </summary>
        private static readonly Dictionary<string, UITypefaceAtlas> Sheets =
            new Dictionary<string, UITypefaceAtlas>();

        /// <summary>
        /// The size a sheet is baked at for each interface size, so it is drawn one texel to one pixel.
        ///
        /// <b>This is the whole sharpness fix, and it is a bake decision rather than a drawing one.</b> A sheet
        /// baked at one size and drawn at another is resampled, and bilinear at a fractional ratio lands each
        /// letter's ink at a different subpixel phase -- which is why the same letter came out looking heavier,
        /// lighter or lower depending on where it fell, and why no amount of rounding at draw time fixed it. A
        /// monospaced face still looked unevenly spaced, and that was the proof: its advances are provably
        /// identical, so only the resampling could differ between letters.
        ///
        /// <b>The numbers come from RimWorld, measured on screen 2026-08-29.</b> Its line heights are computed
        /// at run time and were Tiny 18, Small 22, Medium 29 at UI scale 1. Divided by the 1.2 line ratio those
        /// want ems of 15, 18.33 and 24.17 -- so 15, 18 and 24.
        ///
        /// <b>Two of the three are a rounding, and that is the trade.</b> A face at these sizes no longer
        /// occupies exactly RimWorld's line height: 18 gives 21.6 against 22, and 24 gives 28.8 against 29. Four
        /// tenths of a pixel is invisible; the resampling was not.
        ///
        /// At a UI scale of two the wanted em doubles and these sheets are drawn at 2:1, which is the cleanest
        /// ratio bilinear has after 1:1 -- every output pixel is exactly four texels.
        /// </summary>
        private static int BakedFor(UITypefaceAtlas known, GameFont size)
        {
            if (known == null || !known.Available || known.LineRatio <= 0f)
                return 0;

            return Mathf.RoundToInt(UIFonts.LineHeightOf(size) / known.LineRatio);
        }

        /// <summary>
        /// The base sheet name for a face, which is also the stem every sized sheet is named from.
        ///
        /// A sized sheet is the stem with the em appended -- <c>BarlowCondensedRegular18</c>. Only the faces the
        /// interface actually draws in have been baked that way; the rest have their one sheet and are resampled,
        /// which is fine for a face nothing is set in yet and is what makes all twelve selectable without baking
        /// thirty-six.
        /// </summary>
        private static string FileOf(UIFace face)
        {
            switch (face)
            {
                case UIFace.BarlowCondensed: return "BarlowCondensedRegular";
                case UIFace.BarlowCondensedThin: return "BarlowCondensedThin";
                case UIFace.CascadiaMono: return "CascadiaMonoRegular";
                case UIFace.HammersmithOne: return "HammersmithOneRegular";
                case UIFace.IBMPlexMono: return "IBMPlexMonoRegular";
                case UIFace.Oswald: return "OswaldRegular";
                default: return null;
            }
        }

        /// <summary>One sheet by file name, read on first ask and kept. A missing file is cached as broken.</summary>
        private static UITypefaceAtlas Sheet(string name)
        {
            UITypefaceAtlas existing;

            if (Sheets.TryGetValue(name, out existing))
                return existing;

            existing = new UITypefaceAtlas(name);
            Sheets[name] = existing;

            return existing;
        }

        /// <summary>
        /// The sheet to draw this face at this size: the one baked for it, or the general one if there is none.
        /// </summary>
        internal static UITypefaceAtlas AtlasFor(UIFace face, GameFont size)
        {
            string file = FileOf(face);

            if (file == null)
                return null;

            UITypefaceAtlas general = Sheet(file);

            // Asked of the face rather than assumed, because the em a size wants depends on the face's own line
            // ratio and those differ: Barlow is 1.20, IBM Plex 1.30, Oswald 1.48. Baking every face at the same
            // numbers put two of the three back to being resampled -- the very thing the sizes exist to avoid.
            int em = BakedFor(general, size);

            if (em <= 0)
                return general;

            UITypefaceAtlas sized = Sheet(file + em);

            return sized.Available ? sized : general;
        }

        /// <summary>
        /// The sheet behind a face, or null for <see cref="UIFace.Game"/>, which has no sheet.
        ///
        /// Null is the answer for the game's own font rather than an error, because that face is drawn by
        /// <c>Widgets.Label</c> and never by a glyph loop. A caller that gets null draws the vanilla way.
        /// </summary>
        internal static UITypefaceAtlas AtlasFor(UIFace face)
        {
            string file = FileOf(face);

            return file == null ? null : Sheet(file);
        }

        /// <summary>What to call a face in the interface. Not the enum name, which is a file name.</summary>
        internal static string Named(UIFace face)
        {
            switch (face)
            {
                case UIFace.BarlowCondensed: return "Barlow Condensed";
                case UIFace.BarlowCondensedThin: return "Barlow Condensed Thin";
                case UIFace.CascadiaMono: return "Cascadia Mono";
                case UIFace.HammersmithOne: return "Hammersmith One";
                case UIFace.IBMPlexMono: return "IBM Plex Mono";
                case UIFace.Oswald: return "Oswald";
                default: return "RimWorld";
            }
        }

        /// <summary>
        /// Whether a face can actually be drawn.
        ///
        /// <see cref="UIFace.Game"/> is always available, which is what makes it the fallback. Any other face is
        /// available only if both its files are beside the assembly and parsed, so a deleted or truncated sheet
        /// costs the look rather than the text.
        /// </summary>
        internal static bool Available(UIFace face)
        {
            UITypefaceAtlas atlas = AtlasFor(face);

            return atlas == null || atlas.Available;
        }

        /// <summary>
        /// A face from its saved name, falling back to <see cref="UIFace.Game"/>.
        ///
        /// Unrecognized rather than invalid: a settings file written by a later version can name a face this one
        /// has never heard of, and the game's own font is the one answer that is always right.
        /// </summary>
        internal static UIFace Parse(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                foreach (UIFace face in (UIFace[]) System.Enum.GetValues(typeof(UIFace)))
                {
                    if (face.ToString() == name)
                        return face;
                }
            }

            return UIFace.Game;
        }
    }
}
