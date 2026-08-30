using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// Draws a line of text in any <see cref="UIFace"/>, and is the only thing that does.
    ///
    /// <b>A drop-in for <c>Widgets.Label</c>, on purpose.</b> It reads <c>Text.Anchor</c> and <c>GUI.color</c>
    /// the same way, sizes the face so a line occupies RimWorld's line height at the same <c>GameFont</c>, and
    /// falls back to the game's own text whenever a face cannot be served -- so a caller can change which face
    /// a label is drawn in without moving anything around it.
    ///
    /// <b>The faces are real dynamic fonts out of the mod's AssetBundle,</b> which is why this file is short.
    /// Three earlier renderers lived here -- a glyph-by-glyph loop over baked sheets, a Font object assembled
    /// from those sheets at runtime, and an OS-registered TTF -- and every one of them was a partial
    /// reimplementation of a font engine, each with its own artifacts. A bundled font is the engine's own:
    /// FreeType hints each size, rich text tags work, wrapping and measurement work, and characters a face
    /// lacks fall back per glyph to system fonts instead of blanking. Settled with Aaron on 2026-08-30.
    /// </summary>
    internal static class UITextControl
    {
        private static readonly Dictionary<long, GUIStyle> Styles = new Dictionary<long, GUIStyle>();

        /// <summary>Whether text asked for in this face will really be drawn in it.</summary>
        internal static bool Available(UIFace face)
        {
            return UIFaces.Available(face);
        }

        /// <summary>How tall one line is: RimWorld's own line height, which every face is sized to fill.</summary>
        internal static float LineHeight(GameFont size)
        {
            return UIFonts.LineHeightOf(size);
        }

        /// <summary>
        /// How wide the text will draw, measured through the same style that will draw it. Falls through to
        /// <c>Text.CalcSize</c> whenever the draw would.
        /// </summary>
        internal static float Width(string text, UIFace face, GameFont size,
            FontStyle weight = FontStyle.Normal)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;

            GUIStyle style = StyleFor(face, size, TextAnchor.UpperLeft, false, weight);

            if (style != null)
                return style.CalcSize(new GUIContent(text)).x;

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

        /// <summary>One line, cut short with an ellipsis rather than clipped when it will not fit.</summary>
        internal static void LabelEllipses(Rect rect, string text, UIFace face, GameFont size,
            FontStyle weight = FontStyle.Normal)
        {
            Paint(rect, text, face, size, true, weight);
        }

        private static void Paint(Rect rect, string text, UIFace face, GameFont size, bool ellipses,
            FontStyle weight)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // An ellipsed label never wraps, whatever the caller left WordWrap set to: a label being cut short
            // and a label growing a second line are two answers to the same question.
            GUIStyle style = StyleFor(face, size, Text.Anchor, !ellipses && Text.WordWrap, weight);

            if (style == null)
            {
                Vanilla(rect, text, size, ellipses);

                return;
            }

            UIGuard.Try("UIText.Draw", () =>
            {
                string shown = ellipses ? Shortened(style, text, rect.width) : text;

                GUI.Label(rect, shown, style);
            });
        }

        /// <summary>
        /// A style drawing this face at this size, cached by everything that is state on a style.
        ///
        /// <b>The colour is left to <c>GUI.color</c>,</b> which is where <c>Widgets.Label</c> takes it from
        /// too; white here means unmodified, because IMGUI multiplies the two. The point size comes from the
        /// font's own metrics so each face fills one RimWorld line exactly.
        /// </summary>
        private static GUIStyle StyleFor(UIFace face, GameFont size, TextAnchor anchor, bool wrap,
            FontStyle weight)
        {
            if (face == UIFace.Game)
                return null;

            FontStyle synthesize;
            Font font = UIFaces.FontFor(face, weight, out synthesize);

            if (font == null)
                return null;

            long key = (long) face | ((long) size << 8) | ((long) anchor << 16) | ((long) weight << 24)
                       | ((wrap ? 1L : 0L) << 32);

            GUIStyle style;

            if (Styles.TryGetValue(key, out style))
                return style;

            style = new GUIStyle
            {
                font = font,
                fontSize = UIFaces.PointSizeFor(font, size),
                fontStyle = synthesize,
                alignment = anchor,
                wordWrap = wrap,
                richText = true,
                clipping = TextClipping.Clip
            };

            style.normal.textColor = Color.white;

            Styles[key] = style;

            return style;
        }

        /// <summary>
        /// The longest prefix that fits, with an ellipsis on the end, measured through the style that draws
        /// it. Unity clips; it does not ellipse; so the cut is made here, the same thing
        /// <c>Widgets.LabelEllipses</c> does with the game's own font.
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
        /// The vanilla path, with the font set and put back, so both paths take their size from the argument
        /// rather than one of them reading whatever <c>Text.Font</c> happened to be.
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
    }
}
