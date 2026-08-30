using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// Draws a line of text in any <see cref="UIFace"/>, and is the only thing that does.
    ///
    /// <b>A drop-in for <c>Widgets.Label</c>, on purpose.</b> It reads <c>Text.Anchor</c> and <c>GUI.color</c>
    /// the same way, sizes a line to exactly the height RimWorld's own font would have taken at the same
    /// <c>GameFont</c>, and clips to the rect when the text overruns. So a caller can change which face a label
    /// is drawn in without moving anything around it, and a layout tuned against vanilla text still fits.
    ///
    /// <b>Every face falls back to the game's own text rather than failing.</b> Four things send a draw down the
    /// vanilla path: <see cref="UIFace.Game"/> itself, a sheet whose files are missing, text carrying markup or
    /// newlines, and text using a character the sheet was not baked over. The last is what makes this safe in a
    /// language the face does not cover -- the label reads in RimWorld's font instead of coming out as a row of
    /// blanks.
    ///
    /// <b>One glyph is one draw call, and that is a property of this approach rather than of IMGUI.</b> A
    /// texture draw is one quad and nothing batches quads, so a twenty character label is twenty calls -- which
    /// is why this belongs on headings, values and buttons rather than on a wall of body text.
    /// <c>ResearchScriptAtlas</c> draws single marks the same way and has been on screen since 2026-08-23.
    ///
    /// IMGUI's text path does batch, though: <c>GUI.Label</c> builds one mesh for a whole string.
    /// <see cref="UIRuntimeFont"/> reaches it by pouring the same baked sheet into a <c>UnityEngine.Font</c>, and
    /// if that proves out on screen it supersedes the loop here for anything longer than a word.
    /// </summary>
    internal static class UITextControl
    {
        /// <summary>Whether text asked for in this face will really be drawn in it.</summary>
        internal static bool Available(UIFace face)
        {
            return UIFaces.Available(face);
        }

        /// <summary>
        /// How tall one line is.
        ///
        /// <b>Identical to RimWorld's line height at the same size, by construction.</b> The em is chosen so
        /// that it comes out that way, because the alternative is that switching a face reflows every layout it
        /// appears in. It is exposed anyway so a caller has one thing to ask rather than having to know that.
        /// </summary>
        internal static float LineHeight(GameFont size)
        {
            return UIFonts.LineHeightOf(size);
        }

        /// <summary>
        /// How wide the text will draw, in the face it will really be drawn in.
        ///
        /// Falls through to <c>Text.CalcSize</c> whenever the draw would, so a caller measuring to lay something
        /// out gets the width of what will actually appear rather than of what was asked for.
        /// </summary>
        internal static float Width(string text, UIFace face, GameFont size, FontStyle weight = FontStyle.Normal)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;

            // In the same order Paint chooses, because a width measured off one path and drawn with another
            // puts every centred and right-aligned label off its own box by the difference.
            UITypefaceAtlas atlas = UIFaces.AtlasFor(face, size);
            float measured;

            if (atlas != null && atlas.Available && Settable(text)
                && TryMeasure(atlas, text, Scale(atlas, size), (int) weight, out measured))
                return measured;

            Font font = UIRuntimeFont.For(face, size);

            if (font != null && Covers(font, text))
            {
                GUIStyle style = UIRuntimeFont.StyleFor(face, size, TextAnchor.UpperLeft, false, weight);

                if (style != null)
                    return style.CalcSize(new GUIContent(text)).x;
            }

            GameFont previous = Text.Font;

            Text.Font = size;

            float vanilla = Text.CalcSize(text).x;

            Text.Font = previous;

            return vanilla;
        }

        /// <summary>One line of text in the given face, overflow clipped to the rect.</summary>
        internal static void Label(Rect rect, string text, UIFace face, GameFont size,
            FontStyle weight = FontStyle.Normal)
        {
            Paint(rect, text, face, size, false, weight);
        }

        /// <summary>One line of text, cut short with an ellipsis rather than clipped when it will not fit.</summary>
        internal static void LabelEllipses(Rect rect, string text, UIFace face, GameFont size,
            FontStyle weight = FontStyle.Normal)
        {
            Paint(rect, text, face, size, true, weight);
        }

        /// <summary>
        /// Lays the glyphs out and draws them, or hands the whole label to RimWorld.
        ///
        /// The decision is made once, up front, for the whole string. Drawing part of a label in one face and
        /// part in another was never on the table: a single unbaked character would show as a change of typeface
        /// mid-word, which reads as a rendering fault rather than as a fallback.
        /// </summary>
        private static void Paint(Rect rect, string text, UIFace face, GameFont size, bool ellipses,
            FontStyle weight)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // <b>Each path where it is better, rather than one path everywhere.</b> Plain text goes through the
            // glyph loop, which spaces letters exactly as the typeface designed because it can accumulate the
            // advance and round only where it draws. Unity cannot: its accumulator takes an int advance, so it
            // quantizes the tracking. Text carrying markup or a newline goes to the font instead, which is the
            // only one of the two that can set a colour tag or wrap a line.
            //
            // So the split is by what the string needs, and the common case gets the better spacing.
            UITypefaceAtlas atlas = UIFaces.AtlasFor(face, size);

            float scale = atlas == null ? 0f : Scale(atlas, size);
            float width = 0f;

            bool loop = atlas != null && atlas.Available && Settable(text) && scale > 0f
                        && TryMeasure(atlas, text, scale, (int) weight, out width);

            if (!loop)
            {
                if (Meshed(rect, text, face, size, ellipses, weight))
                    return;

                Vanilla(rect, text, size, ellipses);

                return;
            }

            string shown = text;

            if (ellipses && width > rect.width)
                shown = Trimmed(atlas, text, scale, (int) weight, rect.width, out width);

            UIGuard.Try("UIText.Draw", () => Glyphs(rect, shown, atlas, scale, size, width, (int) weight), null);
        }

        /// <summary>
        /// Draws through a real <c>UnityEngine.Font</c>, and reports whether it could.
        ///
        /// <b>What this buys over the glyph loop.</b> Unity's text generator handles rich text tags, word wrap
        /// and clipping, and builds one mesh for the whole string. The loop below hands markup and newlines
        /// straight to RimWorld's font, so any label carrying a colour tag silently lost its typeface -- which
        /// is most of the reason the faces could not be used everywhere.
        ///
        /// <b>Coverage is still checked, and markup is stepped over while checking.</b> A font renders a
        /// character it does not have as nothing, so a Russian label would come out blank rather than falling
        /// back. The scan skips anything between angle brackets: the tag is Unity's to read, and its letters are
        /// not going to be drawn.
        /// </summary>
        private static bool Meshed(Rect rect, string text, UIFace face, GameFont size, bool ellipses,
            FontStyle weight)
        {
            Font font = UIRuntimeFont.For(face, size);

            if (font == null || !Covers(font, text))
                return false;

            // <b>An ellipsed label never wraps, whatever the caller left WordWrap set to.</b> RimWorld leaves it
            // true by default, and a wrapping label that is also being cut short is two answers to the same
            // question -- the second line would be clipped by the rect rather than shown. This asked for
            // Text.WordWrap here at first, which quietly turned every LabelEllipses call that had not switched
            // wrapping off into a clipped two-liner.
            //
            // <b>The style is always top aligned and the vertical offset is ours.</b> Unity cannot align text
            // vertically in a font whose lineHeight is zero -- which every runtime built font's is -- so it
            // centres a box of no height and the contentOffset correction then pushes the text a full ascent
            // below where it belongs. On a middle aligned label that put the rail's names straight through the
            // progress track under them, which read as a strikethrough. Seen 2026-08-29.
            TextAnchor anchor = Text.Anchor;

            GUIStyle style = UIRuntimeFont.StyleFor(face, size, Horizontal(anchor), !ellipses && Text.WordWrap,
                weight);

            if (style == null)
                return false;

            float line = UIFonts.LineHeightOf(size);

            float top = Middle(anchor)
                ? (rect.height - line) * 0.5f
                : Lower(anchor) ? rect.height - line : 0f;

            // Rounded for the same reason the glyph metrics are: the label as a whole has to start on the
            // pixel grid, or every letter on it inherits the same fraction of a pixel of offset.
            Rect placed = new Rect(Mathf.Round(rect.x), Mathf.Round(rect.y + top), rect.width,
                Mathf.Max(line, rect.height - top));

            return UIGuard.Try("UIText.Font", () =>
            {
                string shown = text;

                // Unity clips; it does not ellipse. So the cut is made here, by measuring, which is the same
                // thing Widgets.LabelEllipses does with the game's own font.
                if (ellipses)
                    shown = Shortened(style, text, rect.width);

                GUI.Label(placed, shown, style);
            });
        }

        /// <summary>
        /// The horizontal half of an anchor, with the vertical half flattened to the top.
        ///
        /// Horizontal alignment works because the content's width is measured normally. Vertical alignment does
        /// not, for the reason above, so it is taken away from Unity here and done by moving the rect instead.
        /// </summary>
        private static TextAnchor Horizontal(TextAnchor anchor)
        {
            if (Centered(anchor))
                return TextAnchor.UpperCenter;

            return RightAligned(anchor) ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
        }

        /// <summary>
        /// Whether the font has every character that will actually be drawn.
        ///
        /// Anything between <c>&lt;</c> and <c>&gt;</c> is skipped: that is a tag, Unity consumes it, and
        /// requiring the face to contain its letters would refuse a perfectly drawable label because the sheet
        /// has no <c>=</c> inside a colour code.
        /// </summary>
        private static bool Covers(Font font, string text)
        {
            bool tag = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '<')
                {
                    tag = true;

                    continue;
                }

                if (c == '>')
                {
                    tag = false;

                    continue;
                }

                if (tag || c == '\n' || c == '\r' || c == ' ')
                    continue;

                if (!font.HasCharacter(c))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// The longest prefix that fits, with an ellipsis on the end.
        ///
        /// Measured through the style that will draw it, so the cut lands where the letters actually end. Walks
        /// forward a character at a time rather than bisecting: a label is short, and a tag cut in half would be
        /// drawn as literal text, so the simple walk is also the one that is easy to see is correct.
        /// </summary>
        private static string Shortened(GUIStyle style, string text, float available)
        {
            if (style.CalcSize(new GUIContent(text)).x <= available)
                return text;

            const string Ellipsis = "...";

            int kept = 0;

            for (int i = 1; i <= text.Length; i++)
            {
                if (style.CalcSize(new GUIContent(text.Substring(0, i) + Ellipsis)).x > available)
                    break;

                kept = i;
            }

            return text.Substring(0, kept) + Ellipsis;
        }

        /// <summary>
        /// The glyph loop.
        ///
        /// <b>Offsets are worked out inside the rect and only then moved onto it,</b> because the clipping path
        /// puts a group over the rect and a group translates the origin. Computing in local space means the two
        /// paths differ by one addition rather than by a second copy of the anchor arithmetic.
        ///
        /// <b>Only the label's own origin is rounded. Every glyph inside it is placed exactly.</b> That is the
        /// third answer this method has had, and the first two were both wrong in the same way, so the reasoning
        /// is worth keeping.
        ///
        /// Rounding a glyph's position was supposed to make it crisp. It cannot. The sheet is baked at 32 and
        /// drawn near 18, a scale of 0.573, and at any non-integer scale the source texels land at arbitrary
        /// phases regardless of where the destination rect is put. Snapping therefore buys no sharpness at all
        /// -- it only moves each letter away from where the typeface says it goes.
        ///
        /// And what it cost was visible twice over. Snapping the position while advancing the pen in exact
        /// fractions made the gap between letters vary by up to a pixel: text that would not sit still. Snapping
        /// the advance as well fixed the gaps and quantized the tracking instead, so letters sat evenly but not
        /// where they belonged. Snapping the vertical offset pushed letters that rest on the baseline a third of
        /// a pixel under it, which lit the row below and read as r and s hanging like descenders.
        ///
        /// Placed exactly, the spacing is the spacing Barlow was drawn with and the baseline is the baseline.
        /// What remains is that a letter is filtered differently depending on where it falls, so two copies of
        /// the same letter differ slightly in softness. That reads as texture rather than as misalignment, which
        /// is the right way round.
        ///
        /// <b>The label origin and the baseline are still whole numbers,</b> so a block of text sits on the
        /// pixel grid even though the letters within it are free to fall where they fall.
        ///
        /// <b>Crisp text needs a clean scale, not clever rounding.</b> Baking at an em that divides RimWorld's
        /// line height exactly -- 16 against a 22 pixel line, say -- would make every texel map to a whole
        /// number of pixels and the question would not arise. It would also break the rule that a face occupies
        /// exactly the game's line height, so it has not been done.
        /// </summary>
        private static void Glyphs(Rect rect, string text, UITypefaceAtlas atlas, float scale, GameFont size,
            float width, int style)
        {
            float em = atlas.Em * scale;
            float lineHeight = em * atlas.LineRatio;

            TextAnchor anchor = Text.Anchor;

            float left = Centered(anchor)
                ? (rect.width - width) * 0.5f
                : RightAligned(anchor) ? rect.width - width : 0f;

            float top = Middle(anchor)
                ? (rect.height - lineHeight) * 0.5f
                : Lower(anchor) ? rect.height - lineHeight : 0f;

            float baseline = top + em * atlas.AscentRatio;

            bool clip = width > rect.width + 0.5f;

            if (clip)
                GUI.BeginGroup(rect);

            float offsetX = clip ? 0f : rect.x;
            float offsetY = clip ? 0f : rect.y;

            Texture sheet = atlas.Texture;

            // Rounded once, here, rather than per glyph. Everything below is a whole number added to a whole
            // number, so no glyph can land between pixels however long the string is.
            float originX = Mathf.Round(offsetX + left);
            float baselineY = Mathf.Round(offsetY + baseline);

            float pen = 0f;

            for (int i = 0; i < text.Length; i++)
            {
                UITypefaceGlyph glyph;

                if (!atlas.TryGlyph(text[i], style, out glyph))
                    continue;

                if (glyph.Drawable)
                {
                    // <b>The pen carries the exact advance; only the drawn position is rounded.</b> That is the
                    // difference between this and what a font can do, and it is the whole reason this path still
                    // exists. Rounding each advance -- which is all Unity's own accumulator can be given, since
                    // CharacterInfo.advance is an int -- makes every letter land on a pixel but drags the
                    // spacing with it: Barlow's o advances 7.47 and becomes 7, its d advances 7.614 and becomes
                    // 8, so "Food" pulls tight across the two o's and pushes out at the d. At a seven pixel
                    // advance half a pixel is seven percent, and it shows. Reported 2026-08-29.
                    //
                    // Accumulating exactly and rounding at the point of use keeps the error from ever adding up:
                    // every glyph is within half a pixel of where the typeface puts it, and no letter inherits
                    // the drift of the ones before it.
                    Rect ink = new Rect(
                        originX + Mathf.Round(pen + glyph.Bearing * scale),
                        baselineY - Mathf.Round(glyph.MaxY * scale),
                        Mathf.Round(glyph.InkWidth * scale),
                        Mathf.Round(glyph.InkHeight * scale));

                    GUI.DrawTextureWithTexCoords(ink, sheet, glyph.Uv);
                }

                pen += glyph.Advance * scale;
            }

            if (clip)
                GUI.EndGroup();
        }

        /// <summary>
        /// The scale from atlas pixels to screen, chosen so a line comes out exactly RimWorld's line height.
        ///
        /// Matching the line box rather than the point size is what keeps a swapped face from reflowing a
        /// layout, and it is the only definition of "the same size" that survives <c>Text.Font</c> substituting
        /// Small for Tiny -- which it does for several languages, on the Steam Deck, and during any long event.
        /// <see cref="UIFonts"/> is asked rather than <c>Text.LineHeight</c> so this can be called before a font
        /// has been set.
        /// </summary>
        private static float Scale(UITypefaceAtlas atlas, GameFont size)
        {
            float ratio = atlas.LineRatio;

            if (ratio <= 0f || atlas.Em <= 0f)
                return 0f;

            float scale = UIFonts.LineHeightOf(size) / ratio / atlas.Em;

            // <b>Snapped to exactly one when the sheet was baked for this size, and this is the whole fix.</b>
            // A sheet drawn at any ratio other than 1 is resampled, and bilinear at a fractional ratio puts every
            // letter's ink at a different subpixel phase -- which is what made the same letter look heavier,
            // lighter or lower depending on where it fell. The sizes are baked so this lands within about two
            // percent; taking that last two percent is what turns "nearly one texel per pixel" into exactly one.
            //
            // What it costs is that a line is up to four tenths of a pixel shorter than RimWorld's, since 18 x
            // 1.2 is 21.6 against a line height of 22. That was the parity this control was built to keep, and
            // it is worth giving up: nobody can see four tenths of a pixel and everybody could see the other
            // thing.
            return Mathf.Abs(scale - 1f) < 0.05f ? 1f : scale;
        }

        /// <summary>
        /// Total advance, and whether every character was baked.
        ///
        /// False means the whole label goes to RimWorld's font, so the width it reports in that case is not used
        /// and is not completed.
        /// </summary>
        private static bool TryMeasure(UITypefaceAtlas atlas, string text, float scale, int style, out float width)
        {
            width = 0f;

            if (scale <= 0f)
                return false;

            for (int i = 0; i < text.Length; i++)
            {
                UITypefaceGlyph glyph;

                if (!atlas.TryGlyph(text[i], style, out glyph))
                    return false;

                // Exact, as the glyph loop advances. The two must agree or a centred label sits off centre by
                // however much they disagreed.
                width += glyph.Advance * scale;
            }

            return true;
        }

        /// <summary>
        /// The longest prefix that fits once the ellipsis is allowed for.
        ///
        /// Character by character from the end, which is what a proportional face needs: the advances differ, so
        /// there is no count of characters that can be dropped without measuring. The ellipsis is three periods
        /// rather than U+2026, matching <c>Widgets.LabelEllipses</c>, so a truncated label of ours and one of
        /// RimWorld's do not end differently in the same list.
        /// </summary>
        private static string Trimmed(UITypefaceAtlas atlas, string text, float scale, int style, float available,
            out float width)
        {
            const string Ellipsis = "...";

            float ellipsisWidth;

            if (!TryMeasure(atlas, Ellipsis, scale, style, out ellipsisWidth))
            {
                TryMeasure(atlas, text, scale, style, out width);

                return text;
            }

            float room = available - ellipsisWidth;
            float used = 0f;
            int kept = 0;

            while (kept < text.Length)
            {
                UITypefaceGlyph glyph;

                if (!atlas.TryGlyph(text[kept], style, out glyph))
                    break;

                float advance = glyph.Advance * scale;

                if (used + advance > room)
                    break;

                used += advance;
                kept++;
            }

            width = used + ellipsisWidth;

            return text.Substring(0, kept) + Ellipsis;
        }

        /// <summary>
        /// Whether this text can be set glyph by glyph at all.
        ///
        /// <b>Markup and newlines both go to RimWorld,</b> and both would otherwise be drawn literally. A
        /// RimWorld label carries colour and size tags often enough that ignoring them is not an option, and
        /// this control sets one line: a string with a newline in it wants wrapping, which is a paragraph
        /// engine's job and not this one's.
        /// </summary>
        private static bool Settable(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '<' || c == '\n' || c == '\r')
                    return false;
            }

            return true;
        }

        /// <summary>
        /// The vanilla path, with the font set and put back.
        ///
        /// The font is set here rather than left to the caller so both paths behave the same: our own path takes
        /// its size from the <c>GameFont</c> argument and never reads <c>Text.Font</c>, so a caller that had not
        /// set it would otherwise get one size from us and another from the fallback.
        /// </summary>
        private static void Vanilla(Rect rect, string text, GameFont size, bool ellipses)
        {
            GameFont previous = Text.Font;

            Text.Font = size;

            if (ellipses)
                Widgets.LabelEllipses(rect, text);
            else
                Widgets.Label(rect, text);

            Text.Font = previous;
        }

        private static bool Centered(TextAnchor anchor)
        {
            return anchor == TextAnchor.UpperCenter || anchor == TextAnchor.MiddleCenter
                || anchor == TextAnchor.LowerCenter;
        }

        private static bool RightAligned(TextAnchor anchor)
        {
            return anchor == TextAnchor.UpperRight || anchor == TextAnchor.MiddleRight
                || anchor == TextAnchor.LowerRight;
        }

        private static bool Middle(TextAnchor anchor)
        {
            return anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.MiddleCenter
                || anchor == TextAnchor.MiddleRight;
        }

        private static bool Lower(TextAnchor anchor)
        {
            return anchor == TextAnchor.LowerLeft || anchor == TextAnchor.LowerCenter
                || anchor == TextAnchor.LowerRight;
        }
    }
}
