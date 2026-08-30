using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Ideoligions
{
    /// <summary>
    /// The four typefaces the ideoligion screens are set in, and what each one is for.
    ///
    /// <b>Taken from the mockup rather than chosen again here.</b> The design named a display face, a condensed
    /// face, a body face and a monospace, and the whole point of a mockup carrying its typefaces is that the
    /// screen built from it uses them. All four are already in the mod's font bundle -- Barlow was added to it
    /// in 14203 for exactly this -- so nothing here needs sourcing or baking.
    ///
    /// <b>Named once, in one place, because the mapping is the decision.</b> A face picked at each call site
    /// drifts: the third role row gets the body face because somebody was reading a different line when they
    /// wrote it, and nobody notices until the column edges stop lining up. The rule the mockup set is:
    ///
    /// <list type="bullet">
    /// <item><b>Display</b> is the ideoligion's own name and nothing else. It is a headline face and it is
    /// wasted, and slightly hard to read, at row size.</item>
    /// <item><b>Condensed</b> is for names in a column: faiths in the rail, roles, rituals, believers, memes.
    /// It fits more characters in the same width, which is the entire reason those columns kept truncating.</item>
    /// <item><b>Body</b> is prose: descriptions, stances, the consequence lines, anything read as a sentence.</item>
    /// <item><b>Mono</b> is anything that lines up as a column of figures, plus the small-caps labels and the
    /// chips. Tabular numerals are the point: a percentage that shifts sideways between rows is unreadable as a
    /// column even when every value is correct.</item>
    /// </list>
    ///
    /// <b>Nothing falls back to the game font by accident.</b> <c>UITextControl</c> already returns to RimWorld's
    /// own face when a bundled one is missing, so an install whose asset bundle failed to load gets a plain
    /// screen rather than a blank one; that is its behaviour, not something arranged here.
    /// </summary>
    internal static class IdeoFaces
    {
        /// <summary>Oswald. The faith's name, and only that.</summary>
        internal const UIFace Display = UIFace.Oswald;

        /// <summary>Barlow Condensed. Names in a column, where width is the scarce thing.</summary>
        internal const UIFace Condensed = UIFace.BarlowCondensed;

        /// <summary>Barlow. Sentences.</summary>
        internal const UIFace Body = UIFace.Barlow;

        /// <summary>IBM Plex Mono. Figures, small-caps labels and chips.</summary>
        internal const UIFace Mono = UIFace.IBMPlexMono;


        /// <summary>
        /// The sizes the mockup sets each of those faces at, in points.
        ///
        /// <b>These are the mockup's own numbers, typed in as written.</b> The design is specified in CSS pixels
        /// on a canvas 1140 wide and the window is 1215, near enough one to one that a size can be carried over
        /// rather than re-derived -- and re-deriving it is what went wrong for a week. Every size on this screen
        /// used to be a GameFont, which means "fill the line box RimWorld fills at that font", so a face with
        /// generous vertical metrics came out visibly larger than the game font at the same nominal size and
        /// larger again than a condensed face beside it. Three rounds of "make that a bit smaller" were spent
        /// on scale factors before it was clear that the sizes were never comparable in the first place.
        ///
        /// A point size is comparable. Eleven is eleven in Barlow, in Barlow Condensed and in IBM Plex Mono,
        /// which is what lets a row hold two faces and read as one row.
        /// </summary>
        internal static class Size
        {
            /// <summary>The faith's name in the header.</summary>
            internal const float Title = 21f;

            /// <summary>The line under it: memes, or the classic-mode note.</summary>
            internal const float Subtitle = 14f;

            /// <summary>A header readout's figure, and the caption under it.</summary>
            internal const float Readout = 17f;

            internal const float Caption = 10f;

            /// <summary>A block's own heading, and the suffix on the right of it.</summary>
            internal const float BlockHead = 10.5f;

            /// <summary>A name in a column: a believer, a role, a ritual, a faith in the rail.</summary>
            internal const float Name = 15f;

            /// <summary>A sentence: a demand, a stance, a holder's name.</summary>
            internal const float Body = 14f;

            /// <summary>A figure that lines up down the screen.</summary>
            internal const float Figure = 12f;

            /// <summary>The quieter second figure on a row: a drift rate, a requirement, a count.</summary>
            internal const float Small = 11f;

            /// <summary>Ritual timing, which is the longest of the small figures and gets a half point.</summary>
            internal const float When = 11.5f;

            /// <summary>The issue column on the doctrine list, set in small caps.</summary>
            internal const float Issue = 10.5f;

            /// <summary>A chip.</summary>
            internal const float Chip = 10f;
        }
        /// <summary>
        /// Upper case, for the labels the mockup sets as small caps: block suffixes, chips, and the issue
        /// column on the doctrine list.
        ///
        /// <b>This is what makes the mono columns work, and leaving it out is why they looked wrong.</b> Mono at
        /// a dim brightness reads as a deliberate label when it is short and upper case, and as a mistake when
        /// it is mixed case -- the eye takes it for body text that has been set in the wrong font. Every one of
        /// those columns in the mockup is upper case; ours were not, which is the whole of the difference.
        ///
        /// <b>Invariant rather than culture-aware, with one known cost.</b> A Turkish or Azeri locale wants a
        /// dotted capital I from a dotted lower case i, and this will produce an undotted one. Culture-aware
        /// upper-casing would need RimWorld's active language mapped onto a CultureInfo, which the game does not
        /// hand out; and every other language this mod is likely to see -- including German, whose nouns are the
        /// usual worry -- upper-cases identically either way. RimWorld's own UI takes the same shortcut.
        /// </summary>
        internal static string Caps(string text)
        {
            return text.NullOrEmpty() ? text : text.ToUpperInvariant();
        }
    }
}
