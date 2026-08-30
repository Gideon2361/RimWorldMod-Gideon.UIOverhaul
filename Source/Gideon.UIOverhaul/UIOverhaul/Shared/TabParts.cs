using System;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;
using RimWorld;
using Verse.Sound;

namespace Gideon.UIOverhaul.Shared
{
    /// <summary>
    /// The small drawing vocabulary this mod's main tabs and their dialogs share.
    ///
    /// <b>Here rather than copied into each window, because a third caller is what earns a control.</b> Section
    /// headings, explanatory notes, segmented choices, plain buttons and status pills are drawn by the hospital
    /// tab, its two dialogs, the corpses tab and its grave picker, and near-identical copies of each is how the
    /// work grid's buttons ended up being written three times before they were pulled out.
    ///
    /// <b>It began as HospitalParts and moved here when the corpses tab became the fourth caller.</b> The name
    /// was the only thing wrong with it: nothing in here has ever known what a patient is.
    /// </summary>
    internal static class TabParts
    {
        internal const float RowGap = 4f;

        internal const float BlockGap = 10f;

        /// <summary>
        /// How much smaller a pill sets its word than the Tiny it is measured at.
        ///
        /// Tiny is the smallest GameFont RimWorld has, and a chip wants to be smaller still: it is a label on
        /// a label, and at the same size as the row it sits beside it competes with the thing it is annotating.
        /// A bundled face is drawn at a point size rather than chosen from a fixed set, so it can go below Tiny
        /// where the game font cannot -- which is why this only applies when a face is given.
        /// </summary>
        private const float ChipScale = 0.76f;

        /// <summary>
        /// The point size a chip sets at: the caller's if it named one, and otherwise the size the old
        /// Tiny-times-a-fraction rule worked out to.
        ///
        /// Keeping the old rule as the default is what lets the chips on twenty other tabs stay exactly where
        /// they are while a converted screen names a real size. It resolves against the game font rather than
        /// the face on purpose: it is standing in for a number nobody chose, so it should not also vary by face.
        /// </summary>
        private static float ChipPoints(float points)
        {
            return points > 0f ? points : UIFonts.PointsOf(GameFont.Tiny) * ChipScale;
        }

        /// <summary>
        /// Air either side of a chip's word, for a chip set in a bundled face.
        ///
        /// The game-font path adds six and that is right for it, because UIRichText measures with the thirteen
        /// pixel ellipsis reserve already in the figure and that reserve reads as padding once drawn. A bundled
        /// face is measured through the style that draws it, bare, so the same six became three pixels a side --
        /// less than half the mockup's seven -- and the word crowded the border hard enough to read as oversized
        /// whatever point size it was really set at.
        ///
        /// Past the mockup's seven, at ten, because the mockup's chip has no rounding on it and ours does: a
        /// rounded end eats into the corner the word would otherwise have had, so matching the flat design's
        /// padding leaves less air than the flat design has.
        /// </summary>
        private const float ChipPad = 20f;

        /// <summary>
        /// A section heading: a hairline, then a small dim caption under it.
        ///
        /// The same shape the hunting bill dialog uses, so a player moving between the two windows is reading the
        /// same furniture rather than learning a second convention.
        ///
        /// <paramref name="rule"/> draws the hairline and is on by default, because the line is what separates one
        /// section from the one above it. Pass false for the first heading inside a panel, where there is nothing
        /// above to separate from and the line only doubles up on the panel's own edge.
        /// </summary>
        /// <paramref name="face"/> defaults to the game's own, so existing callers are unchanged.
        internal static float Heading(Rect rect, float y, string text, UIColorPaletteDef palette, bool rule = true,
            UIFace face = UIFace.Game)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                GUI.color = palette.Border;

                if (rule)
                    Widgets.DrawLineHorizontal(rect.x, y, rect.width);

                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                Rect caption = new Rect(rect.x, y + 4f, rect.width, UIFonts.LineHeightOf(GameFont.Tiny));

                if (face == UIFace.Game)
                    Widgets.Label(caption, text);
                else
                    UITextControl.LabelEllipses(caption, text, face, GameFont.Tiny);
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

        /// <summary>
        /// One line of text at a given weight, ellipsed rather than wrapped.
        /// </summary>
        /// <param name="face">
        /// Which typeface to set it in. Defaults to the game's own, so every existing caller is unchanged and a
        /// caller opts in by naming a face, exactly as <see cref="RowLabel"/> does.
        /// </param>
        internal static float Line(Rect rect, float y, string text, Color color, GameFont font = GameFont.Small,
            UIFace face = UIFace.Game)
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

                if (face == UIFace.Game)
                    UIRichText.Label(new Rect(rect.x, y, rect.width, height), text);
                else
                    UITextControl.LabelEllipses(new Rect(rect.x, y, rect.width, height), text, face, font);
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

            SegmentFrame(rect, on, over, palette);

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
        /// The frame a segment is drawn in, filled when it is the current one.
        ///
        /// Shared by the worded segments and the icon ones so the two cannot drift into looking like different
        /// controls, which they are not: only what sits inside them differs.
        /// </summary>
        private static void SegmentFrame(Rect rect, bool on, bool over, UIColorPaletteDef palette)
        {
            if (on)
                UIElementPainter.FillRounded(rect, palette.Accent);
            else
                UIElementPainter.OutlineRounded(rect, palette.Border,
                    over ? palette.SurfaceRaised : palette.PanelBackground);
        }

        /// <summary>
        /// One choice in a segmented control whose choices are pictures rather than words.
        ///
        /// <b>For a set of modes the game already has art for,</b> which is the only case where dropping the words
        /// is safe: a player has seen RimWorld's three hostility-response icons on every colonist's inspect pane,
        /// so the picture is the thing they recognize and a column of them reads down the list at a glance. Three
        /// worded segments would have cost a hundred and fifty pixels in every row to say what the filled segment
        /// already says.
        ///
        /// <b>The name goes in the tooltip, and that is not optional.</b> An icon nobody can name is a puzzle, so
        /// callers pass one; there is no sensible fallback this control could invent.
        ///
        /// <b>A disabled segment is drawn, not hidden.</b> Vanilla leaves the Attack mode out of its menu for a
        /// pawn who cannot fight; a segmented control cannot do that without the remaining segments changing
        /// width and position from one row to the next, which would move the control under the pointer while the
        /// pointer is on it. Drawn dim with the reason on hover says more anyway.
        /// </summary>
        internal static void Segment(Rect rect, Texture2D icon, bool on, UIColorPaletteDef palette,
            Action chosen, string tooltip, bool disabled = false)
        {
            bool over = !disabled && Mouse.IsOver(rect);

            // Filled when it is the current choice even if it is disabled, because the control's first job is to
            // say what the setting *is*. A pawn can be set to attack and then lose the ability to fight -- a
            // trait, a hediff, a new xenotype -- and drawing that segment empty would show a row with no mode
            // selected at all rather than a mode that can no longer be obeyed.
            SegmentFrame(rect, on, over, palette);

            if (icon != null)
            {
                // Square and centered, whatever the segment's proportions are. These are shaped glyphs on
                // transparency, so stretching one to fill a wider-than-tall segment reads as a rendering fault
                // rather than as a bigger icon.
                float side = Mathf.Max(8f, Mathf.Min(rect.height, rect.width) - IconInset * 2f);
                Rect frame = new Rect(rect.center.x - side * 0.5f, rect.center.y - side * 0.5f, side, side);

                Color previous = GUI.color;

                GUI.color = on
                    ? palette.WindowBackground
                    : disabled
                        ? palette.TextDisabled
                        : over
                            ? palette.TextPrimary
                            : palette.TextSecondary;

                GUI.DrawTexture(frame, icon);

                GUI.color = previous;
            }

            if (!tooltip.NullOrEmpty() && Mouse.IsOver(rect))
                TooltipHandler.TipRegion(rect, (TipSignal) tooltip);

            // Consumed even when nothing happens, so a click on a segment that is off-limits or already chosen
            // does not fall through to whatever is underneath -- which on a table row is the row itself.
            bool clicked = Widgets.ButtonInvisible(rect);

            if (!clicked || disabled || on)
                return;

            chosen();

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>Room around an icon inside its segment.</summary>
        private const float IconInset = 4f;

        /// <summary>How wide a row of <paramref name="segments"/> icon segments comes out.</summary>
        /// <summary>
        /// A square icon control that looks exactly like a segment and behaves like a switch.
        ///
        /// <b>A separate control rather than a flag on <see cref="Segment"/>, because they answer different
        /// questions.</b> A segment is one of a set and its contract is that clicking the one already chosen does
        /// nothing -- there is no "unchoose" in a row of mutually exclusive options, and firing there would let a
        /// stray click deselect a mode that has to have some value. A toggle is a single setting with two states
        /// and its whole point is that the same click turns it off again.
        ///
        /// <b>They were the same control once and it produced a one-way switch.</b> Shuffle was drawn as a
        /// segment, so it could be turned on and never off: the click landed, the frame lit, and the callback was
        /// suppressed by the very rule that makes a segment a segment. Repeat had it too. If a control's off
        /// state is reached by pressing it again, it is this one and not that one.
        /// </summary>
        internal static void IconToggle(Rect rect, Texture2D icon, bool on, UIColorPaletteDef palette,
            Action toggled, string tooltip, bool disabled = false)
        {
            bool over = !disabled && Mouse.IsOver(rect);

            SegmentFrame(rect, on, over, palette);

            if (icon != null)
            {
                float side = Mathf.Max(8f, Mathf.Min(rect.height, rect.width) - IconInset * 2f);
                Rect frame = new Rect(rect.center.x - side * 0.5f, rect.center.y - side * 0.5f, side, side);

                Color previous = GUI.color;

                GUI.color = on
                    ? palette.WindowBackground
                    : disabled
                        ? palette.TextDisabled
                        : over
                            ? palette.TextPrimary
                            : palette.TextSecondary;

                GUI.DrawTexture(frame, icon);

                GUI.color = previous;
            }

            if (!tooltip.NullOrEmpty() && Mouse.IsOver(rect))
                TooltipHandler.TipRegion(rect, (TipSignal) tooltip);

            bool clicked = Widgets.ButtonInvisible(rect);

            if (!clicked || disabled)
                return;

            toggled();

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        internal static float IconSegmentsWidth(int segments, float height)
        {
            return segments <= 0
                ? 0f
                : segments * height + (segments - 1) * SegmentGap;
        }

        /// <summary>Between segments in a row of them.</summary>
        internal const float SegmentGap = 3f;

        /// <summary>
        /// A plain button.
        ///
        /// A disabled one is drawn rather than hidden and says why on hover, which is the rule the operation
        /// picker leans on hardest: an operation you cannot do yet is a shopping list entry, and a missing button
        /// would leave nothing to hover.
        /// </summary>
        /// <summary>
        /// The tab strips' name for the mod's button.
        ///
        /// <b>The drawing moved to <see cref="UIActionButtonControl"/> and this is the seventy-two call sites'
        /// shorthand.</b> What it drew was nearly right and wrong in one specific way: its border was
        /// <c>palette.Border</c> whatever the pointer was doing, so the corpse pane's Strip buttons lifted their
        /// fill on hover and never gained the accent edge every other button in the mod had. Reported on
        /// 2026-08-25. The ellipsing label and the tooltip came with it, so nothing was lost in the move.
        /// </summary>
        internal static bool Button(Rect rect, string label, UIColorPaletteDef palette, bool enabled = true,
            bool primary = false, string tooltip = null)
        {
            return UIActionButtonControl.Draw(rect, label, palette, primary, enabled, GameFont.Small, tooltip);
        }

        /// <summary>
        /// How wide a pill will be, so a caller can lay one out from the right before drawing it.
        ///
        /// <b>Separate from drawing rather than measured by drawing once and again.</b> Calling the draw twice to
        /// find out where it goes paints a stray copy at the first position, which is a bug that only shows on
        /// rows where the two positions differ.
        /// </summary>
        /// <summary>
        /// A one-line label filling the height of a row, centred in it, ellipsed rather than wrapped.
        ///
        /// <b>Why the height matters, and why this is a helper rather than a rect at each call site.</b> A line
        /// of Small text is about twenty two pixels tall and a label rect any shorter than that clips the bottom
        /// of the line -- so the descenders go and only the descenders go. "Depths" reads as "Deoths" and "Engi"
        /// as "Enqi", which does not look like a layout fault at all: it looks like a font problem, or like the
        /// mod is showing the wrong text. It was written five times as an eighteen or twenty pixel rect before
        /// that was understood, so the row's own height is passed in and the vertical centring happens here.
        ///
        /// Anchored middle-left, which is what makes taking the full row height safe: the text sits on the row's
        /// centre line however tall the row is.
        /// </summary>
        /// <param name="face">
        /// Which typeface to set it in. Defaults to the game's own, so all thirty-odd existing callers are
        /// unchanged and a caller opts in by naming a face rather than by everything moving at once.
        /// </param>
        /// <param name="points">
        /// An absolute point size, overriding <paramref name="font"/> for a caller that names a real size. Zero
        /// leaves the GameFont in charge, which is what every caller written before point sizes existed wants.
        /// It is ignored on the game font, which comes in three sizes and cannot honour a fourth; the row still
        /// sets in the GameFont beside it, so a screen half converted looks off rather than breaking.
        /// </param>
        internal static void RowLabel(Rect band, string text, Color color, GameFont font = GameFont.Small,
            UIFace face = UIFace.Game, float points = 0f)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = font;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = color;

                if (face == UIFace.Game)
                    UIRichText.Label(band, text);
                else if (points > 0f)
                    UITextControl.LabelEllipses(band, text, face, points);
                else
                    UITextControl.LabelEllipses(band, text, face, font);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        internal static float PillWidth(string text, float ceiling = 9999f, UIFace face = UIFace.Game,
            float points = 0f)
        {
            GameFont previousFont = Text.Font;

            try
            {
                Text.Font = GameFont.Tiny;

                // Through WidthOf rather than off CalcSize, because that is the figure the drawing side judges it
                // against: LabelEllipses holds thirteen pixels back for the dots, so a pill sized to the bare
                // text ellipses at every size however much room it has. The six on top is the visible padding.
                //
                // Measured in the same face Pill will draw it in, for the same reason.
                float measured = face == UIFace.Game
                    ? UIRichText.WidthOf(text ?? string.Empty)
                    : UITextControl.Width(text ?? string.Empty, face, ChipPoints(points));

                return Mathf.Min(ceiling, measured + (face == UIFace.Game ? 6f : ChipPad));
            }
            finally
            {
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// How wide a <see cref="Button"/> has to be for its label to fit.
        ///
        /// <b>Measured at the font the button actually draws at,</b> which is Small and not the Tiny that
        /// <see cref="PillWidth"/> uses. Through <c>UIRichText.WidthOf</c>, so the thirteen pixel ellipsis reserve
        /// is already in the figure -- a button sized off a bare <c>CalcSize</c> ellipses at every size.
        ///
        /// <b>This exists because splitting a row between two buttons on a fixed fraction is wrong.</b> The
        /// corpses tab gave each of its two action buttons half of a hundred and fifty-two pixels and "Butcher all"
        /// came out as "Butche...". Half of a row is the right width for exactly one pair of labels.
        /// </summary>
        internal static float ButtonWidth(string label, float padding = 16f)
        {
            GameFont previousFont = Text.Font;

            try
            {
                Text.Font = GameFont.Small;

                return UIRichText.WidthOf(label ?? string.Empty) + padding;
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
        /// <paramref name="face"/> sizes the pill as well as setting it: measuring in one face and drawing in
        /// another is how a pill ends up either padded with air or ellipsing a word that would have fitted.
        internal static Rect Pill(Rect view, float x, float y, string text, Color color, UIColorPaletteDef palette,
            float ceiling = 9999f, Color? behind = null, UIFace face = UIFace.Game, float points = 0f)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;

                float measured = face == UIFace.Game
                    ? UIRichText.WidthOf(text)
                    : UITextControl.Width(text, face, ChipPoints(points));

                float width = Mathf.Min(ceiling, measured + (face == UIFace.Game ? 6f : ChipPad));
                float height = (face == UIFace.Game
                    ? UIFonts.LineHeightOf(GameFont.Tiny)
                    : UITextControl.LineHeight(face, ChipPoints(points))) + 2f;

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
                if (face == UIFace.Game)
                    UIRichText.Label(pill, text);
                else
                    UITextControl.LabelEllipses(pill, text, face, ChipPoints(points));

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

        /// <summary>The caption above a value, at whatever Tiny currently means.</summary>
        internal static float CaptionHeight
        {
            get { return UIFonts.LineHeightOf(GameFont.Tiny); }
        }

        /// <summary>The value under a caption.</summary>
        internal static float ValueHeight
        {
            get { return UIFonts.LineHeightOf(GameFont.Small); }
        }

        /// <summary>
        /// A right-aligned caption over a value, laid out from the right edge inwards.
        ///
        /// Returns the x the next one should end at, so a row of them packs against the right without any of
        /// them needing to know how wide its neighbours came out.
        ///
        /// <b>Sized from its own text.</b> These carry colony figures whose width nobody can predict -- "2,410"
        /// and "herbal 14  glitter 3" sit in the same strip -- and a fixed column for them is how the hospital
        /// strip ended up truncating its own policy names.
        /// </summary>
        internal static float Readout(Rect bar, float right, string caption, string value,
            UIColorPaletteDef palette, string tip = null, Color? valueColor = null)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.WordWrap = false;
                Text.Font = GameFont.Tiny;

                float width = Mathf.Max(Text.CalcSize(caption ?? string.Empty).x,
                    Text.CalcSize(value ?? string.Empty).x) + 18f;

                Rect cell = new Rect(right - width, bar.y, width, bar.height);

                Text.Anchor = TextAnchor.UpperRight;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(cell.x, cell.y, cell.width - 4f, CaptionHeight), caption);

                Text.Font = GameFont.Small;
                GUI.color = valueColor ?? palette.TextPrimary;

                Widgets.Label(new Rect(cell.x, cell.y + CaptionHeight, cell.width - 4f, ValueHeight), value);

                if (!tip.NullOrEmpty())
                    TooltipHandler.TipRegion(cell, (TipSignal) tip);

                return cell.x;
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// A dim caption over a bright value, which is the whole cell vocabulary of a tab with no heading row.
        ///
        /// The caption says what a heading row would have said, per cell, because one column means different
        /// things on different rows: the same lane is "what we lost" over a colonist and "yield" over a muffalo.
        /// </summary>
        internal static void Labelled(Rect band, string caption, string value, Color color,
            UIColorPaletteDef palette, string tip = null)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.UpperLeft;

                Rect top = new Rect(band.x + 6f, band.y + 2f, Mathf.Max(0f, band.width - 10f), CaptionHeight);

                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                if (!caption.NullOrEmpty())
                    UIRichText.Label(top, caption);

                Text.Font = GameFont.Small;
                GUI.color = color;

                if (!value.NullOrEmpty())
                    UIRichText.Label(new Rect(top.x, top.yMax, top.width, ValueHeight), value);

                if (!tip.NullOrEmpty())
                    TooltipHandler.TipRegion(band, (TipSignal) tip);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }
    }
}
