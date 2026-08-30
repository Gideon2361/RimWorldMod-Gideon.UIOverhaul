using Gideon.UIFramework.Helpers;

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
    }
}
