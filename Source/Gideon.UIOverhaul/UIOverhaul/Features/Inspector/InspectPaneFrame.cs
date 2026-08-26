using System;
using System.Collections.Generic;
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
        /// Everything a pane of a given height spends on something other than its body.
        ///
        /// <b>Derived from the constants above rather than written down beside them.</b> It was a literal 112 in
        /// <see cref="InspectPaneMetrics"/> and the real figure is 153, so a foreign tab asking for 480 pixels was
        /// given 421 and drew itself into a scroll view: a workbench's bills tab came out with our scrollbar, the
        /// horizontal bar that one forced, and the tab's own list bar, three of them around a panel that was 41
        /// pixels short. A second copy of a layout is wrong the first time either copy is edited, and this one
        /// was wrong from the day the footer gained a maximum.
        ///
        /// The grip and the half margin are the window's; the rest is what <see cref="Draw"/> lays out above and
        /// below the body. The footer is counted at its maximum, since that is what a pane tall enough to matter
        /// will give it.
        ///
        /// <b>Still counted for a pawn, where the footer is no longer drawn.</b> See <see cref="ShowsFooter"/>.
        /// One figure serves every selection, and the two ways to be wrong are not symmetrical: over-counting
        /// leaves a foreign tab on a pawn seventy-eight pixels smaller than it could be, which nobody can see,
        /// while under-counting puts a scroll view around a tab that fitted, which is the fault this was written
        /// to end. If it is ever split, split it on the same test <c>ShowsFooter</c> uses and not on a second
        /// reading of it.
        /// </summary>
        internal static float Chrome
        {
            get
            {
                return InspectPaneMetrics.GripHeight + InnerMargin * 0.5f + HeaderHeight + 4f + MaxFooterHeight
                       + 6f;
            }
        }

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

            bool contents = pane.ShouldShowPaneContents || Identified(thing);

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

                Header(new Rect(0f, 0f, local.width, headerHeight), pane, thing, pawn, palette, roomForBody);

                float y = headerHeight + 4f;
                float remaining = local.height - y;

                if (remaining <= 0f)
                    return;

                bool showFooter = contents && ShowsFooter(kind, roomForBody, thing);

                float footerHeight = showFooter ? remaining : 0f;

                if (roomForBody)
                {
                    footerHeight = showFooter
                        ? Mathf.Clamp(Mathf.Min(remaining - MinBodyHeight, MaxFooterHeight), MinFooterHeight,
                            remaining)
                        : 0f;

                    Rect body = new Rect(0f, y, local.width,
                        remaining - footerHeight - (showFooter ? 6f : 0f));

                    // Somebody else's tab takes the whole body and is drawn at its own size. The pane has already
                    // been grown to fit it, so this is normally a straight draw rather than a scroll.
                    if (foreign != null)
                        InspectForeignTab.Draw(body, foreign);
                    else
                        Body(body, thing, pawn, kind, palette);
                }

                if (showFooter)
                    Footer(new Rect(0f, local.yMax - footerHeight, local.width, footerHeight), palette);
            }
            finally
            {
                Widgets.EndGroup();
            }
        }

        /// <summary>
        /// Whether an entity's body may be drawn even though its def hides inspect data.
        ///
        /// <b>This is the real reason an anomaly entity's panel was empty, and it was never the entity blocks.</b>
        /// Anomaly's entity defs declare <c>hideInspect</c> -- the noctol among them -- and vanilla's
        /// <c>ShouldShowPaneContents</c> answers false for anything that does. So <c>contents</c> was false,
        /// <c>KindOf</c> returned <c>None</c>, <c>roomForBody</c> went false with it, and the pane drew a header
        /// and stopped. No body ran at all, which is why two rounds of changes inside the bodies changed nothing.
        ///
        /// <b>Hiding it is right for something nobody has identified, and pointless once they have.</b> A noctol
        /// in the codex, or one strapped to a platform in your own base, has its Bio, Health and Entity tabs
        /// sitting in the strip full of the same information. Suppressing only the overview withheld nothing; it
        /// just left one tab blank.
        ///
        /// Two ways to be identified, and both are the game's own tests. <c>EntityCodex.Discovered</c> is
        /// literally "we know what this is", and being held on a holding platform means the colony caught it and
        /// built the thing to keep it in.
        ///
        /// Nothing else is widened. A single selection is already implied, since this is only ever asked about
        /// <c>SingleSelectedThing</c>; an unidentified entity stays hidden exactly as vanilla intends; and
        /// <c>onlyShowInspectString</c> is a different flag that <see cref="InspectBodies.KindOf"/> still obeys.
        /// </summary>
        private static bool Identified(Thing thing)
        {
            return UIGuard.Try("Inspector.EntityIdentified", () =>
            {
                if (thing == null || thing.def == null || !thing.def.hideInspect)
                    return false;

                if (!(thing is Pawn))
                    return false;

                if (thing.ParentHolder is Building_HoldingPlatform)
                    return true;

                EntityCodex codex = Find.EntityCodex;

                return codex != null && codex.Discovered(thing.def);
            }, false, null);
        }

        /// <summary>
        /// The header: who this is, what they are doing, how they are, and every button vanilla put up here.
        ///
        /// <b>The buttons come first and the label is sized around what they took,</b> which is vanilla's own
        /// order and the reason it is kept: <c>DoInspectPaneButtons</c> reports how much of the right edge it
        /// used through a ref parameter, and a label laid out before asking would run underneath the info card
        /// button on anything with a long name.
        /// </summary>
        private static void Header(Rect rect, IInspectPane pane, Thing thing, Pawn pawn, UIColorPaletteDef palette,
            bool full)
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

            // Ours goes to the left of vanilla's, and takes its own room off the name lane the same way they do.
            // Asked for 2026-08-23: an icon here rather than a button on the Bio panel, because the Bio panel
            // does not exist on a corpse and a dead pawn is exactly the one you most want the editor for.
            lineEndWidth += Editor.EditorButton.Draw(rect, lineEndWidth, thing, pawn, palette);

            // Left of the editor's icon, for a colony animal vanilla will not offer to rename. Which is very
            // nearly every animal in the game; see AnimalRenameButton for the condition that does it.
            lineEndWidth += Animals.AnimalRenameButton.Draw(rect, lineEndWidth, pawn);

            // Leftmost of the corner icons, and the only one drawn in a colour of its own. See IdeoBadge.
            lineEndWidth += IdeoBadge.Draw(rect, lineEndWidth, pawn, palette);

            float nameX = 0f;

            if (full && pawn != null)
            {
                PawnPortraitCell.Draw(new Rect(0f, rect.height - PortraitSize, PortraitSize, PortraitSize), pawn,
                    palette, palette.PanelBackground);

                nameX = PortraitSize + 8f;
            }

            float conditionWidth = ConditionWidth(pawn, palette);

            // The sex, immediately before the name, in the accent for male and the mood colour for female. Drawn
            // here rather than in the qualifier because it belongs with the name: a glyph read at the same moment
            // as who somebody is, rather than a word found afterwards in a line of small grey text.
            //
            // It takes its room off the front of the name lane, so a long name ellipses a little sooner instead
            // of running underneath it. On the compact header, where the whole pane has collapsed to a name and a
            // condition, it is left out: that form exists to say as little as possible.
            if (full)
                nameX += UIGuard.Try("Inspector.GenderGlyph",
                    () => GenderGlyphs.Draw(new Rect(nameX, 0f, GenderGlyphs.Size, CompactHeaderHeight), pawn,
                        palette), 0f, "The inspect pane's gender glyph is missing.");

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

                    // White, because every part of the qualifier now carries its own colour tag: the faction in
                    // the game's own faction colour and the age dimmed. IMGUI multiplies a tag by GUI.color, so
                    // tinting the line first would mute the one part of it that is meant to stand out.
                    GUI.color = Color.white;

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
        /// Whether vanilla's inspect string is drawn under the body.
        ///
        /// <b>Not under a pawn.</b> Asked for 2026-08-22, and the screenshot made the case: for a colonist the
        /// block was four lines of things the pane had already said in better places. Sex is the glyph before the
        /// name, age and faction are the qualifier beside it, the equipped weapon is the first row of Carrying,
        /// and the current job is the line under the name. Repeating all of it in small grey text at the bottom
        /// is the pane arguing with itself.
        ///
        /// <b>Everything that is not a pawn keeps it, and so does a pane dragged too short for a body.</b> For a
        /// rock, a plant or a modded building whose comps write their whole state into that string, it is not a
        /// duplicate of anything -- it is the only reading there is, and <see cref="InspectThingBody"/> is
        /// written on the understanding that it can fall through to it. The compact pane is the documented way to
        /// refuse this feature altogether, and refusing it has to leave RimWorld's own panel behind rather than
        /// an empty box.
        ///
        /// <b>What a pawn loses with it,</b> because none of these are shown anywhere else yet: an inspiration
        /// and its expiry, stun and stagger timers, a royal title, a trader's kind, ability cooldowns, and the
        /// inspect lines some hediffs write.
        /// </summary>
        private static bool ShowsFooter(InspectBodyKind kind, bool roomForBody, Thing thing)
        {
            if (!roomForBody)
                return true;

            if (kind == InspectBodyKind.Pawn)
                return false;

            return !ListsItsOwnContents(thing);
        }

        /// <summary>
        /// Whether a tab on this very thing already lists what its inspect string is repeating.
        ///
        /// <b>Dropped for containers on Aaron's instruction of 2026-08-23, and the reason he gave was the scroll
        /// wheel.</b> Vanilla draws the inspect string through <c>Widgets.LabelScrollable</c>, which consumes the
        /// wheel whenever the text is long enough to need a scrollbar. A crate holding forty-four stacks writes
        /// every one of them into that string, so the footer becomes a second scrolling list under the first, and
        /// the pointer only has to stray into it for the wheel to move the wrong list.
        ///
        /// <b>Losing nothing is what makes this safe.</b> The footer's whole justification is that mods write
        /// into the inspect string and this mod cannot know what they put there -- so it is kept verbatim
        /// everywhere except where the string is a list of contents and a tab on the same thing already shows
        /// that list, better, with icons and a search box. This is the second such exception; a pawn was the
        /// first, for the same reason and by the same test.
        ///
        /// <b>Keyed on the tab the thing offers, not on its class.</b> <c>Building_Storage</c> would catch
        /// shelves and crates and miss caskets, transporters, and every modded container that reached for
        /// vanilla's own contents tab. Asking whether a contents tab is offered is asking the question that
        /// actually matters, and it answers correctly for a mod nobody has written yet.
        /// </summary>
        private static bool ListsItsOwnContents(Thing thing)
        {
            return UIGuard.Try("Inspector.ContentsTabTest", () =>
            {
                List<InspectTabBase> tabs = thing?.def?.inspectorTabsResolved;

                if (tabs == null)
                    return false;

                for (int i = 0; i < tabs.Count; i++)
                {
                    if (tabs[i] is ITab_ContentsBase || tabs[i] is ITab_Storage)
                        return true;
                }

                return false;
            }, false, null);
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
