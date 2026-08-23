using System;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;
using RimWorld;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>
    /// The small drawing vocabulary the hospital windows share.
    ///
    /// <b>Here rather than copied into each window, because a third caller is what earns a control.</b> The tab,
    /// the operation picker and the standing order editor all draw section headings, explanatory notes, segmented
    /// choices and plain buttons, and three near-identical copies is how the work grid's buttons ended up being
    /// written three times before they were pulled out.
    /// </summary>
    internal static class HospitalParts
    {
        internal const float RowGap = 4f;

        internal const float BlockGap = 10f;

        /// <summary>
        /// A section heading: a hairline, then a small dim caption under it.
        ///
        /// The same shape the hunting bill dialog uses, so a player moving between the two windows is reading the
        /// same furniture rather than learning a second convention.
        /// </summary>
        internal static float Heading(Rect rect, float y, string text, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                GUI.color = palette.Border;

                Widgets.DrawLineHorizontal(rect.x, y, rect.width);

                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(rect.x, y + 4f, rect.width, UIFonts.LineHeightOf(GameFont.Tiny)), text);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }

            return y + UIFonts.LineHeightOf(GameFont.Tiny) + 8f;
        }

        /// <summary>
        /// A paragraph of explanation, as tall as the paragraph actually is.
        ///
        /// <b>Measured rather than guessed.</b> A literal height is right until the wording grows, and then the
        /// last line and a half is simply gone with nothing to say so. <c>Text.CalcHeight</c> asks the same layout
        /// engine that is about to draw it, at the same font and the same width.
        /// </summary>
        internal static float Note(Rect rect, float y, string text, UIColorPaletteDef palette,
            GameFont font = GameFont.Tiny, Color? color = null)
        {
            if (text.NullOrEmpty())
                return y;

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = font;
                GUI.color = color ?? palette.TextDisabled;

                float height = Text.CalcHeight(text, rect.width);

                Widgets.Label(new Rect(rect.x, y, rect.width, height), text);

                return y + height;
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }
        }

        /// <summary>One line of text at a given weight, ellipsed rather than wrapped.</summary>
        internal static float Line(Rect rect, float y, string text, Color color, GameFont font = GameFont.Small)
        {
            if (text == null)
                text = string.Empty;

            float height = UIFonts.LineHeightOf(font);

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = font;
                Text.WordWrap = false;
                GUI.color = color;

                UIRichText.Label(new Rect(rect.x, y, rect.width, height), text);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Font = previousFont;
            }

            return y + height;
        }

        /// <summary>
        /// One choice in a segmented control: filled when it is the current one, outlined when it is not.
        ///
        /// A segment answers "what is this set to" without being touched, which is the whole reason these replaced
        /// float menus across the mod. The chosen callback does not fire on the segment that is already on.
        /// </summary>
        internal static void Segment(Rect rect, string label, bool on, UIColorPaletteDef palette, Action chosen)
        {
            bool over = Mouse.IsOver(rect);

            if (on)
                UIElementPainter.FillRounded(rect, palette.Accent);
            else
                UIElementPainter.OutlineRounded(rect, palette.Border,
                    over ? palette.SurfaceRaised : palette.PanelBackground);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
                GUI.color = on ? palette.WindowBackground : palette.TextPrimary;

                // Ellipsed rather than clipped. IMGUI cuts a centred label off at both ends, so a segment whose
                // label does not fit reads as the middle of a word with nothing to say a word was lost.
                UIRichText.Label(rect, label);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (!Widgets.ButtonInvisible(rect) || on)
                return;

            chosen();

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// A plain button.
        ///
        /// A disabled one is drawn rather than hidden and says why on hover, which is the rule the operation
        /// picker leans on hardest: an operation you cannot do yet is a shopping list entry, and a missing button
        /// would leave nothing to hover.
        /// </summary>
        internal static bool Button(Rect rect, string label, UIColorPaletteDef palette, bool enabled = true,
            bool primary = false, string tooltip = null)
        {
            bool over = enabled && Mouse.IsOver(rect);
            bool held = over && Input.GetMouseButton(0);

            if (primary && enabled)
                UIElementPainter.FillRounded(rect, held ? palette.AccentMuted : palette.Accent);
            else
                UIElementPainter.OutlineRounded(rect, palette.Border,
                    !enabled ? palette.PanelBackground : over ? palette.SurfaceRaised : palette.SurfaceSunken);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;

                GUI.color = !enabled
                    ? palette.TextDisabled
                    : primary
                        ? palette.WindowBackground
                        : palette.TextPrimary;

                // Ellipsed rather than clipped, which is what turned "Default care: herbal medicine or worse"
                // into "ault care: herbal medicine or wo" on the hospital strip: a centred label too wide for
                // its rect loses both ends and gives no sign that it did.
                UIRichText.Label(rect, label);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (!tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, (TipSignal) tooltip);

            if (!enabled || !Widgets.ButtonInvisible(rect))
                return false;

            SoundDefOf.Click.PlayOneShotOnCamera();

            return true;
        }

        /// <summary>
        /// How wide a pill will be, so a caller can lay one out from the right before drawing it.
        ///
        /// <b>Separate from drawing rather than measured by drawing once and again.</b> Calling the draw twice to
        /// find out where it goes paints a stray copy at the first position, which is a bug that only shows on
        /// rows where the two positions differ.
        /// </summary>
        internal static float PillWidth(string text, float ceiling = 9999f)
        {
            GameFont previousFont = Text.Font;

            try
            {
                Text.Font = GameFont.Tiny;

                // Through WidthOf rather than off CalcSize, because that is the figure the drawing side judges it
                // against: LabelEllipses holds thirteen pixels back for the dots, so a pill sized to the bare
                // text ellipses at every size however much room it has. The six on top is the visible padding.
                return Mathf.Min(ceiling, UIRichText.WidthOf(text ?? string.Empty) + 6f);
            }
            finally
            {
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// A small pill carrying a word: a status, a count, a refusal.
        ///
        /// <b>The wash is composited rather than handed over translucent.</b> An outline is painted as two fills,
        /// so a transparent inside leaves the border colour filling the whole pill: the first version of this
        /// came out as a solid block of amber with amber text on it, which was every state pill in the operation
        /// picker unreadable at once. The inside is now the pill's own colour mixed into the surface behind it,
        /// which is what a 22 percent wash was always supposed to mean.
        ///
        /// <b>And it is capped.</b> Sized purely from its text, "no analgesic regeneration injector" produced a
        /// pill wider than the row it sat in, overhanging the card and leaving the label nowhere to go.
        ///
        /// Returns the rect it took so a caller laying several across a line knows where the next one starts.
        /// </summary>
        internal static Rect Pill(Rect view, float x, float y, string text, Color color, UIColorPaletteDef palette,
            float ceiling = 9999f, Color? behind = null)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;

                float width = Mathf.Min(ceiling, UIRichText.WidthOf(text) + 6f);
                float height = UIFonts.LineHeightOf(GameFont.Tiny) + 2f;

                Rect pill = new Rect(x, y, width, height);

                Color surface = behind ?? palette.PanelBackground;

                UIElementPainter.OutlineRounded(pill, color,
                    UIElementPainter.Composite(surface, new Color(color.r, color.g, color.b, 0.22f)));

                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = color;

                // Ellipsed rather than clipped: IMGUI cuts a centred label off at both ends, so a capped pill
                // whose text does not fit would read as the middle of a word with no sign anything was lost.
                // Given the whole pill, since LabelEllipses already holds back the thirteen pixels that serve
                // as its padding; insetting on top of that reserve ellipses every pill whatever its size.
                UIRichText.Label(pill, text);

                return pill;
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>A number box's worth of text, parsed leniently and clamped.</summary>
        internal static int ParseCount(string text, int fallback, int min, int max)
        {
            int value;

            if (text.NullOrEmpty() || !int.TryParse(text.Trim(), out value))
                return fallback;

            return Mathf.Clamp(value, min, max);
        }
    }
}
