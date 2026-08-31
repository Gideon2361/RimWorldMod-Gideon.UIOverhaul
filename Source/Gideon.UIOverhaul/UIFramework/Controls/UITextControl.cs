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
    /// the same way and falls back to the game's own text whenever a face cannot be served -- so a caller can
    /// change which face a label is drawn in without moving anything around it.
    ///
    /// <b>Sizes come in two flavors, and the point size is the one to reach for.</b>
    ///
    /// <list type="bullet">
    /// <item>
    /// A <c>float</c> is a point size and means exactly what it says. Eleven points is eleven points in every
    /// face, so two faces sharing a row are finally comparable, and a number read off a mockup can be typed in
    /// as written. When the face is <see cref="UIFace.Game"/> -- which cannot be drawn at an arbitrary size --
    /// it falls back to the nearest of the game's three fonts.
    /// </item>
    /// <item>
    /// A <see cref="GameFont"/> means "fill the line box that GameFont fills", which is what a face has to do
    /// to drop into a layout whose row heights were built around the game font. It keeps rows from moving, and
    /// it is also why Barlow at Small looks bigger than the game font at Small: the two agree on the line box
    /// and disagree on how much of it the letters take up. For retrofitting a vanilla-shaped layout, not for
    /// building to a design.
    /// </item>
    /// </list>
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

        /// <summary>How tall one line is: RimWorld's own line height, which a GameFont-sized face fills.</summary>
        internal static float LineHeight(GameFont size)
        {
            return UIFonts.LineHeightOf(size);
        }

        /// <summary>
        /// How tall one line of <paramref name="points"/> is in <paramref name="face"/>.
        ///
        /// <b>Asked of the font rather than of RimWorld,</b> which is the whole point of sizing in points: the
        /// line box follows the face and the size, instead of the size being bent to fit a line box. Faces
        /// differ here, so a row carrying two of them wants the taller of the two.
        /// </summary>
        internal static float LineHeight(UIFace face, float points)
        {
            FontStyle ignored;
            Font font = face == UIFace.Game ? null : UIFaces.FontFor(face, FontStyle.Normal, out ignored);

            if (font == null || font.lineHeight <= 0f || font.fontSize <= 0)
                return UIFonts.LineHeightOf(UIFonts.Nearest(points));

            // Through the same points to pixels ratio the style that draws this text uses. Both sides of the
            // division have to be in Unity's pixel em or the row comes out three quarters of the height of
            // the text standing in it, and the fallback above already answers in pixels.
            return Mathf.Ceil(font.lineHeight * UIFonts.ToPixels(points) / font.fontSize);
        }

        /// <summary>
        /// How wide the text will draw, measured through the same style that will draw it. Falls through to
        /// <c>Text.CalcSize</c> whenever the draw would.
        /// </summary>
        internal static float Width(string text, UIFace face, GameFont size, FontStyle weight = FontStyle.Normal)
        {
            return Measure(text, StyleFor(face, PointsOf(face, size), TextAnchor.UpperLeft, false, weight), size);
        }

        /// <summary>How wide the text will draw at a point size.</summary>
        internal static float Width(string text, UIFace face, float points, FontStyle weight = FontStyle.Normal)
        {
            return Measure(text, StyleFor(face, points, TextAnchor.UpperLeft, false, weight),
                UIFonts.Nearest(points));
        }

        /// <summary>One line of text in the given face, overflow clipped to the rect.</summary>
        internal static void Label(Rect rect, string text, UIFace face, GameFont size,
            FontStyle weight = FontStyle.Normal)
        {
            Paint(rect, text, face, PointsOf(face, size), size, false, weight);
        }

        /// <summary>One line of text at a point size.</summary>
        internal static void Label(Rect rect, string text, UIFace face, float points,
            FontStyle weight = FontStyle.Normal)
        {
            Paint(rect, text, face, points, UIFonts.Nearest(points), false, weight);
        }

        /// <summary>One line, cut short with an ellipsis rather than clipped when it will not fit.</summary>
        internal static void LabelEllipses(Rect rect, string text, UIFace face, GameFont size,
            FontStyle weight = FontStyle.Normal)
        {
            Paint(rect, text, face, PointsOf(face, size), size, true, weight);
        }

        /// <summary>One ellipsed line at a point size.</summary>
        internal static void LabelEllipses(Rect rect, string text, UIFace face, float points,
            FontStyle weight = FontStyle.Normal)
        {
            Paint(rect, text, face, points, UIFonts.Nearest(points), true, weight);
        }

        /// <summary>
        /// How tall a wrapped block of text is at a point size, measured through the style that draws it.
        ///
        /// <b>Measured and drawn by the same style or the two disagree.</b> A paragraph sized against
        /// RimWorld's font and then drawn in Barlow is either clipped at the bottom or trailed by a band of
        /// empty space, and which of the two depends on the words.
        /// </summary>
        internal static float Height(string text, UIFace face, float points, float width)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;

            GUIStyle style = StyleFor(face, points, TextAnchor.UpperLeft, true, FontStyle.Normal);

            if (style != null)
                return style.CalcHeight(new GUIContent(text), width);

            GameFont previous = Text.Font;

            Text.Font = UIFonts.Nearest(points);

            float vanilla = Text.CalcHeight(text, width);

            Text.Font = previous;

            return vanilla;
        }

        /// <summary>
        /// A paragraph, wrapped, in a bundled face.
        ///
        /// <b>Rich text is on, as it is on every label this draws.</b> RimWorld writes colour into the strings
        /// it hands out -- faction names, thing names, dates -- and a paragraph drawn without it is a wall of
        /// one grey.
        /// </summary>
        internal static void Paragraph(Rect rect, string text, UIFace face, float points,
            FontStyle weight = FontStyle.Normal)
        {
            if (string.IsNullOrEmpty(text))
                return;

            GUIStyle style = StyleFor(face, points, TextAnchor.UpperLeft, true, weight);

            if (style == null)
            {
                GameFont previous = Text.Font;

                Text.Font = UIFonts.Nearest(points);

                Widgets.Label(rect, text);

                Text.Font = previous;

                return;
            }

            UIGuard.Try("UIText.Paragraph", () => GUI.Label(rect, text, style));
        }

        /// <summary>
        /// The point size a <see cref="GameFont"/> means for this face: the size at which the face's own line
        /// box matches the one RimWorld draws that GameFont in.
        /// </summary>
        private static float PointsOf(UIFace face, GameFont size)
        {
            if (face == UIFace.Game)
                return UIFonts.PointsOf(size);

            FontStyle ignored;
            Font font = UIFaces.FontFor(face, FontStyle.Normal, out ignored);

            return font == null ? UIFonts.PointsOf(size) : UIFaces.PointSizeFor(font, size);
        }

        private static float Measure(string text, GUIStyle style, GameFont fallback)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;

            if (style != null)
                return style.CalcSize(new GUIContent(text)).x;

            GameFont previous = Text.Font;

            Text.Font = fallback;

            float vanilla = Text.CalcSize(text).x;

            Text.Font = previous;

            return vanilla;
        }

        private static void Paint(Rect rect, string text, UIFace face, float points, GameFont fallback,
            bool ellipses, FontStyle weight)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // An ellipsed label never wraps, whatever the caller left WordWrap set to: a label being cut short
            // and a label growing a second line are two answers to the same question.
            GUIStyle style = StyleFor(face, points, Text.Anchor, !ellipses && Text.WordWrap, weight);

            if (style == null)
            {
                Vanilla(rect, text, fallback, ellipses);

                return;
            }

            UIGuard.Try("UIText.Draw", () =>
            {
                string shown = ellipses ? Shortened(style, text, rect.width) : text;

                GUI.Label(rect, shown, style);
            });
        }

        /// <summary>
        /// A style drawing this face at this point size, cached by everything that is state on a style.
        ///
        /// <b>The colour is left to <c>GUI.color</c>,</b> which is where <c>Widgets.Label</c> takes it from
        /// too; white here means unmodified, because IMGUI multiplies the two.
        /// </summary>
        private static GUIStyle StyleFor(UIFace face, float points, TextAnchor anchor, bool wrap, FontStyle weight)
        {
            if (face == UIFace.Game)
                return null;

            FontStyle synthesize;
            Font font = UIFaces.FontFor(face, weight, out synthesize);

            if (font == null)
                return null;

            // Rounded once, here, and used as the key as well as the size. Keying on the unrounded float would
            // give 10.4 and 10.6 two cache entries that draw identically, and keying on the rounded size while
            // drawing at the float would be a cache that lies about what it holds.
            // The one place points become pixels. Unity rasterizes at a pixel em size, so the HTML
            // ratio is applied here and nowhere else -- every caller above this line is in points.
            int size = Mathf.Max(1, Mathf.RoundToInt(UIFonts.ToPixels(points)));

            long key = (long) face | ((long) anchor << 8) | ((long) weight << 16)
                       | ((wrap ? 1L : 0L) << 24) | ((long) size << 32);

            GUIStyle style;

            if (Styles.TryGetValue(key, out style))
                return style;

            style = new GUIStyle
            {
                font = font,
                fontSize = size,
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
