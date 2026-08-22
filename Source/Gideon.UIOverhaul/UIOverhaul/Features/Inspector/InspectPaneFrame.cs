using System;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Pawns;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Verse.Steam;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// The inspect pane itself: the grip, the header, the body, and vanilla's inspect string under all of it.
    ///
    /// <b>This replaces <c>InspectPaneUtility.InspectPaneOnGUI</c> rather than adding to it,</b> which is the
    /// only way to change the arrangement: that method is the whole layout, from the label's font to where the
    /// contents start. Everything it does that is not layout is kept and kept in the same order -- the
    /// select-next-in-cell button, the pane buttons every mod hangs its icons off, and the inspect string with
    /// its own scroll -- because those are affordances the player and other mods both rely on.
    ///
    /// <b>The header is the same reading as the rest of the mod.</b> The condition line and its badge come from
    /// <see cref="PawnHealthSummary"/>, which is what the pawns tab and the colonist bar already use, so a pawn
    /// cannot read as fine in one panel and breaking in another. The portrait is
    /// <see cref="PawnPortraitCell"/>, which already jumps the camera when it is clicked.
    ///
    /// <b>At the floor the pane is vanilla's own size, and the header shrinks to match.</b> Below the height that
    /// a body needs there is no body and no portrait: what is left is a name, a condition and the inspect string,
    /// which is what RimWorld shows. Dragging it down is meant to be a real way to refuse the feature rather than
    /// a smaller version of it.
    /// </summary>
    internal static class InspectPaneFrame
    {
        /// <summary>Room for a portrait, two lines of text and the pane buttons above them.</summary>
        private const float HeaderHeight = 56f;

        /// <summary>Room for a name and a condition, which is all a pane at the floor has space for.</summary>
        private const float CompactHeaderHeight = 30f;

        /// <summary>Vanilla's own inner margin, so the pane's contents sit where the eye expects them.</summary>
        private const float InnerMargin = InspectPaneUtility.PaneInnerMargin;

        /// <summary>Below this a body has nothing useful to say, so the inspect string takes the room instead.</summary>
        private const float MinBodyHeight = 76f;

        /// <summary>The inspect string never gets less than this, whatever the body wants.</summary>
        private const float MinFooterHeight = 34f;

        /// <summary>The inspect string never gets more than this either, so a chatty building cannot bury the body.</summary>
        private const float MaxFooterHeight = 72f;

        /// <summary>Side of the portrait's frame.</summary>
        private const float PortraitSize = PawnPortraitCell.Size;

        /// <summary>Vanilla's select-next-in-cell button, kept at its own size.</summary>
        private const float CornerButton = InspectPaneUtility.CornerButtonsSize;

        /// <summary>
        /// How tall the last body actually drew.
        ///
        /// <b>Remembered rather than predicted.</b> A formula for a section's height is wrong the first time a
        /// row is added to that section and it fails by clipping, silently. Three panels in this mod have been
        /// fixed for exactly that, so the scroll view is sized from where the previous frame's drawing ended.
        /// </summary>
        private static float lastBodyHeight;

        /// <summary>
        /// One frame of the pane.
        ///
        /// <paramref name="inRect"/> is the window at zero: <c>MainTabWindow_Inspect</c> overrides
        /// <c>Margin</c> to nothing, so this really is the whole window.
        /// </summary>
        internal static void Draw(Rect inRect, IInspectPane pane)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            pane.RecentHeight = inRect.height;

            if (!pane.AnythingSelected)
                return;

            InspectPaneMetrics.Grip(inRect, palette);

            Rect inner = new Rect(inRect.x + InnerMargin, inRect.y + InspectPaneMetrics.GripHeight,
                inRect.width - InnerMargin * 2f,
                inRect.height - InspectPaneMetrics.GripHeight - InnerMargin * 0.5f);

            if (inner.width <= 0f || inner.height <= 0f)
                return;

            Thing thing = Find.Selector.SingleSelectedThing;
            Pawn pawn = InspectBodies.PawnOf(thing);

            bool contents = pane.ShouldShowPaneContents;

            InspectBodyKind kind = contents ? InspectBodies.KindOf(thing) : InspectBodyKind.None;

            InspectPaneState.Notify(thing, InspectTabStrip.Offers(pane, InspectPaneState.Selected));

            // Everything below is drawn in the group's coordinates, which is the contract pane.DoInspectPaneButtons
            // already has: it places its icons at y zero and measures in from the group's right edge.
            Widgets.BeginGroup(inner);

            try
            {
                Rect local = inner.AtZero();

                InspectTabBase foreign = InspectTabStrip.OpenForeign(pane);

                bool roomForBody = (foreign != null || kind != InspectBodyKind.None)
                                   && local.height >= HeaderHeight + MinBodyHeight + MinFooterHeight;

                float headerHeight = roomForBody ? HeaderHeight : CompactHeaderHeight;

                Header(new Rect(0f, 0f, local.width, headerHeight), pane, pawn, palette, roomForBody);

                float y = headerHeight + 4f;
                float remaining = local.height - y;

                if (remaining <= 0f)
                    return;

                float footerHeight = remaining;

                if (roomForBody)
                {
                    footerHeight = Mathf.Clamp(Mathf.Min(remaining - MinBodyHeight, MaxFooterHeight),
                        MinFooterHeight, remaining);

                    Rect body = new Rect(0f, y, local.width, remaining - footerHeight - 6f);

                    // Somebody else's tab takes the whole body and is drawn at its own size. The pane has already
                    // been grown to fit it, so this is normally a straight draw rather than a scroll.
                    if (foreign != null)
                        InspectForeignTab.Draw(body, foreign);
                    else
                        Body(body, thing, pawn, kind, palette);
                }

                // Only when RimWorld would have shown it. A multiple selection and a thing declaring
                // hideInspect both come through here, and vanilla draws neither its contents nor its inspect
                // string for them: the second is the one that matters, since a hidden thing's inspect string is
                // hidden because the player is not supposed to be reading it yet.
                if (contents)
                    Footer(new Rect(0f, local.yMax - footerHeight, local.width, footerHeight), palette);
            }
            finally
            {
                Widgets.EndGroup();
            }
        }

        /// <summary>
        /// The header: who this is, what they are doing, how they are, and every button vanilla put up here.
        ///
        /// <b>The buttons come first and the label is sized around what they took,</b> which is vanilla's own
        /// order and the reason it is kept: <c>DoInspectPaneButtons</c> reports how much of the right edge it
        /// used through a ref parameter, and a label laid out before asking would run underneath the info card
        /// button on anything with a long name.
        /// </summary>
        private static void Header(Rect rect, IInspectPane pane, Pawn pawn, UIColorPaletteDef palette, bool full)
        {
            float lineEndWidth = 0f;

            if (pane.ShouldShowSelectNextInCellButton)
            {
                Rect next = new Rect(rect.width - CornerButton, 0f, CornerButton, CornerButton);

                MouseoverSounds.DoRegion(next);

                if (Widgets.ButtonImage(next, TexButton.SelectOverlappingNext))
                    pane.SelectNextInCell();

                lineEndWidth += CornerButton;

                if (SteamDeck.IsSteamDeckInNonKeyboardMode)
                    TooltipHandler.TipRegionByKey(next, "SelectNextInSquareTipController");
                else
                    TooltipHandler.TipRegionByKey(next, "SelectNextInSquareTip",
                        KeyBindingDefOf.SelectNextInCell.MainKeyLabel);
            }

            // Not inside a UIGuard lambda: a ref parameter cannot be captured by one. This is arbitrary code from
            // whichever mod owns the selection, so it still gets reported rather than being allowed to take the
            // pane down with it.
            try
            {
                pane.DoInspectPaneButtons(rect, ref lineEndWidth);
            }
            catch (Exception ex)
            {
                UIGuard.Report("Inspector.PaneButtons", ex,
                    "The info card and rename buttons are missing from the inspect pane for this selection.");
            }

            float nameX = 0f;

            if (full && pawn != null)
            {
                PawnPortraitCell.Draw(new Rect(0f, rect.height - PortraitSize, PortraitSize, PortraitSize), pawn,
                    palette, palette.PanelBackground);

                nameX = PortraitSize + 8f;
            }

            float conditionWidth = ConditionWidth(pawn, palette);

            // On the full header the condition has the second line to itself, so the name may run the whole
            // width. On the compact one they share a line, and the name gives way.
            Rect nameRect = new Rect(nameX, 0f,
                Mathf.Max(20f, rect.width - nameX - lineEndWidth - 6f - (full ? 0f : conditionWidth + 8f)),
                CompactHeaderHeight);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = palette.TextPrimary;

                string label = pane.GetLabel(nameRect) ?? string.Empty;

                UIRichText.Label(nameRect, label);

                // The qualifier follows the name on the same line, dim and small, and only when the name has
                // left room for it. Measured under the font that drew the name, since asking under another one
                // describes text of a different width and puts the two on top of each other.
                if (full)
                {
                    float used = Text.CalcSize(label).x + 8f;

                    Text.Font = GameFont.Tiny;
                    GUI.color = palette.TextDisabled;

                    Rect after = new Rect(nameRect.x + used, nameRect.y, nameRect.width - used, nameRect.height);

                    if (after.width > 30f)
                        UIRichText.Label(after, InspectBodies.Qualifier(pawn) ?? string.Empty);
                }

                if (full)
                {
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    GUI.color = palette.TextSecondary;

                    Rect sub = new Rect(nameX, CompactHeaderHeight,
                        Mathf.Max(20f, rect.width - nameX - conditionWidth - 8f),
                        rect.height - CompactHeaderHeight);

                    UIRichText.Label(sub, InspectBodies.Subline(pawn) ?? string.Empty);
                }
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            Rect condition = full
                ? new Rect(rect.width - conditionWidth, CompactHeaderHeight, conditionWidth,
                    rect.height - CompactHeaderHeight)
                : new Rect(rect.width - lineEndWidth - conditionWidth - 6f, 0f, conditionWidth,
                    CompactHeaderHeight);

            Condition(condition, pawn, palette);
        }

        /// <summary>
        /// How much of the header's right edge the condition line wants.
        ///
        /// Measured at the font it is drawn in, since <c>UITagControl.WidthFor</c> reads the ambient one: asking
        /// under a different font describes a badge of a different size.
        /// </summary>
        private static float ConditionWidth(Pawn pawn, UIColorPaletteDef palette)
        {
            if (pawn == null)
                return 0f;

            return UIGuard.Try("Inspector.ConditionWidth", () =>
            {
                PawnHealthSummary summary = PawnHealthSummary.For(pawn);

                GameFont previousFont = Text.Font;

                try
                {
                    Text.Font = GameFont.Tiny;

                    // UIRichText.WidthOf, not Text.CalcSize: the label goes through LabelEllipses, which keeps
                    // thirteen pixels back before it will accept a string. Sizing this lane to the bare measured
                    // width is what turned a condition reading "Healthy" into "Hea...".
                    return UITagControl.WidthFor(summary.Tag) + (summary.Tag.NullOrEmpty() ? 0f : 6f)
                                                              + UIRichText.WidthOf(summary.Label)
                                                              + 4f;
                }
                finally
                {
                    Text.Font = previousFont;
                }
            }, 0f, "The condition line is missing from the inspect pane header.");
        }

        /// <summary>The badge and the one-line condition, right of the name, with everything else in a tooltip.</summary>
        private static void Condition(Rect rect, Pawn pawn, UIColorPaletteDef palette)
        {
            if (pawn == null || rect.width <= 0f)
                return;

            UIGuard.Try("Inspector.Condition", () =>
            {
                PawnHealthSummary summary = PawnHealthSummary.For(pawn);

                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;
                bool previousWrap = Text.WordWrap;

                try
                {
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Text.WordWrap = false;

                    float x = UITagControl.DrawLeading(rect, summary.Tag, summary.TagColor(palette), palette, 6f);

                    GUI.color = summary.Color(palette);

                    UIRichText.Label(new Rect(x, rect.y, Mathf.Max(0f, rect.xMax - x), rect.height),
                        summary.Label ?? string.Empty);
                }
                finally
                {
                    Text.WordWrap = previousWrap;
                    GUI.color = previousColor;
                    Text.Anchor = previousAnchor;
                    Text.Font = previousFont;
                }

                if (Mouse.IsOver(rect) && !summary.Detail.NullOrEmpty())
                    TooltipHandler.TipRegion(rect, (TipSignal) summary.Detail);
            }, "The condition line is missing from the inspect pane header.");
        }

        /// <summary>
        /// The scrolling middle of the pane, whichever body is showing.
        ///
        /// The scrollbar's lane is taken off the content width before anything is drawn rather than after, since
        /// a column laid out at the full width and then scrolled loses its right edge under the bar.
        /// </summary>
        private static void Body(Rect rect, Thing thing, Pawn pawn, InspectBodyKind kind,
            UIColorPaletteDef palette)
        {
            if (rect.height <= 0f)
                return;

            bool scrolling = lastBodyHeight > rect.height;

            Rect view = new Rect(0f, 0f, rect.width - (scrolling ? 18f : 0f),
                Mathf.Max(rect.height, lastBodyHeight));

            Widgets.BeginScrollView(rect, ref InspectPaneState.Scroll, view);

            float drawn = 0f;

            try
            {
                drawn = InspectBodies.Draw(view, thing, pawn, kind, palette);
            }
            finally
            {
                Widgets.EndScrollView();
            }

            lastBodyHeight = drawn;
        }

        /// <summary>
        /// Vanilla's inspect string, unchanged and in its own scroll.
        ///
        /// <b>Kept verbatim on purpose.</b> It is what every modded building, plant, item and comp writes into,
        /// and there is no way to know what any of them put there. Replacing it with our own reading would break
        /// content this mod has never heard of; the rebuilt body sits above it and adds rather than substitutes.
        /// </summary>
        private static void Footer(Rect rect, UIColorPaletteDef palette)
        {
            if (rect.height <= 0f)
                return;

            GUI.color = palette.Border;

            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width);

            GUI.color = Color.white;

            Rect text = new Rect(rect.x, rect.y + 4f, rect.width, rect.height - 4f);

            UIGuard.Try("Inspector.InspectString", () =>
            {
                ISelectable selectable = Find.Selector.FirstSelectedObject as ISelectable;

                if (selectable == null)
                    return;

                GUI.color = palette.TextSecondary;

                InspectPaneFiller.DrawInspectStringFor(selectable, text);

                GUI.color = Color.white;
            }, "The inspect pane's description text is missing for this selection.");
        }
    }
}
