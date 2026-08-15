using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// How tall a line of text will actually be, as opposed to how tall the font you asked for would have been.
    ///
    /// <b>Setting a font is a request, not a result.</b> <c>Text.Font</c>'s setter substitutes <c>Small</c>
    /// whenever <c>Text.TinyFontSupported</c> is false, and that is false in more situations than it sounds:
    ///
    /// <list type="bullet">
    /// <item>a language whose <c>canBeTiny</c> is false, which covers several of them</item>
    /// <item>the "disable tiny text" accessibility preference</item>
    /// <item>the Steam Deck</item>
    /// <item>any draw that happens while a long event is running</item>
    /// </list>
    ///
    /// Small's line box is around half again Tiny's, so a row height tuned for Tiny clips the text for everyone in
    /// that list. And it clips rather than shrinks: <c>Widgets.Label</c> hands the rect to <c>GUI.Label</c> as the
    /// clip rectangle, so a line box taller than its rect loses its ascenders and descenders, and a centered
    /// anchor spends the overflow at both ends -- which is why the symptom is text with its top and bottom shaved
    /// off rather than text that overflows visibly.
    ///
    /// <b>This existed in three places before it existed here.</b> <c>UILoadingScreenControl</c> had a private
    /// version of <see cref="LineHeightOf"/>, <c>WorkPanel</c> reads <c>Text.LineHeight</c> after setting the
    /// font for the same reason, and <c>Dialog_UIOptions</c> sizes its cards for Small while asking for Tiny.
    /// Three correct answers and no shared one is how a fourth consumer gets it wrong, which is exactly what the
    /// calendar widget did.
    ///
    /// <b>When the font is already set, prefer <c>Text.LineHeight</c> directly.</b> That getter indexes by the
    /// current font, so it already reflects whatever substitution the setter made. This type is for sizing a
    /// layout <i>before</i> the font is set, which is the case a constant is usually standing in for.
    /// </summary>
    internal static class UIFonts
    {
        /// <summary>
        /// The font that will really be used if <paramref name="font"/> is requested.
        /// </summary>
        internal static GameFont Effective(GameFont font)
        {
            if (font == GameFont.Tiny && !Text.TinyFontSupported)
                return GameFont.Small;

            return font;
        }

        /// <summary>
        /// Line height for <paramref name="font"/>, accounting for the substitution.
        ///
        /// Rounded up, because a fractional row height leaves a sub-pixel of the line box outside the rect and
        /// clipping does not round in the caller's favor.
        /// </summary>
        internal static float LineHeightOf(GameFont font)
        {
            return UnityEngine.Mathf.Ceil(Text.LineHeightOf(Effective(font)));
        }

        /// <summary>
        /// A row tall enough for one line of <paramref name="font"/> plus <paramref name="padding"/> above and
        /// below, which is what most single-line rows actually want.
        /// </summary>
        internal static float RowHeight(GameFont font, float padding = 2f)
        {
            return LineHeightOf(font) + padding * 2f;
        }
    }
}
