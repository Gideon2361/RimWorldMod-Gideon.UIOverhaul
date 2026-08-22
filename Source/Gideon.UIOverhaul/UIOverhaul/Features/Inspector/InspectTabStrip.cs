using System.Collections;
using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// The row of tab chips along the top of the inspect pane.
    ///
    /// <b>This is vanilla's <c>DoTabs</c> rebuilt, not decorated.</b> That method draws each tab with
    /// <c>Widgets.ButtonText</c>, which means the open tab looks exactly like the five closed ones and the only
    /// hint of which is open is a dark strip drawn to the left of the row. Chips can carry the state, so they do:
    /// the open one is filled in the accent and the rest are outlined.
    ///
    /// <b>Where the chips sit is vanilla's decision and stays vanilla's.</b> Every ITab positions its own window
    /// at <c>PaneTopY - 30 - size.y</c>, so the strip is what an open tab is stacked on top of. The mockup drew
    /// the chips along the bottom of the pane, which would have put them at the very bottom of the screen and
    /// left a thirty pixel gap under every ITab window in the game, including modded ones this rebuild never
    /// touches. The chips are the same shape the mockup asked for, in the position the rest of the game needs
    /// them.
    ///
    /// <b>No chip opens a window.</b> Health, Gear, Social, Needs, Bio and Log are rebuilt in the pane, and
    /// everything else -- a workbench's bills, an animal's training, a prisoner's settings, whatever a mod has
    /// added -- is drawn in the pane too by <see cref="InspectForeignTab"/>, with the pane grown to the size that
    /// tab asks for. Half the row rendering in place and half popping out was the worst of both.
    /// </summary>
    internal static class InspectTabStrip
    {
        /// <summary>The slot each chip is laid into. Vanilla's tab width, so the pane's own width math still holds.</summary>
        internal const float SlotWidth = InspectPaneMetrics.VanillaTabWidth;

        /// <summary>Room left around a chip inside its slot.</summary>
        private const float SlotPadX = 2f;

        private const float SlotPadY = 3f;

        /// <summary>Reused so a frame of drawing does not allocate a list per pane.</summary>
        private static readonly List<InspectTabBase> Tabs = new List<InspectTabBase>();

        /// <summary>
        /// A second list, for the questions asked while the first one is being drawn from.
        ///
        /// <b>Two lists because one of them was being pulled out from under the draw.</b> <see cref="Draw"/>
        /// collects the tabs and then reads <c>pane.PaneTopY</c> to place the row -- and that property is
        /// patched, and its patch asks <see cref="OpenForeign"/> how tall the pane should be, which collected
        /// into the same list and cleared it on the way out. The result was a chip row that vanished the moment
        /// a tab was open, which is the only time <c>OpenForeign</c> gets as far as collecting anything.
        /// </summary>
        private static readonly List<InspectTabBase> Scratch = new List<InspectTabBase>();

        /// <summary>
        /// Whether one of our six bodies applies to this tab.
        ///
        /// <b>The selection has to be a live pawn as well as the tab being the right type,</b> which is not the
        /// same test. A corpse carries the health, gear and social tabs -- they read the pawn inside it -- and the
        /// pane's body reads <c>SingleSelectedThing</c>, which for a corpse is the corpse. Without this the chip
        /// would light up and the pane would go on showing the corpse's overview underneath it.
        /// </summary>
        private static bool Ours(InspectTabBase tab, out InspectBody body)
        {
            body = InspectBody.Overview;

            return UIGuard.Try("Inspector.TabSubject", () => InspectBodies.PawnOf(Find.Selector.SingleSelectedThing) != null, false,
                null) && InspectPaneState.Replaces(tab, out body);
        }

        /// <summary>
        /// How wide the strip needs the pane to be: one slot per visible tab, plus one for the overview chip when
        /// there is one.
        ///
        /// Asked by the width patch rather than assumed, because the chips are laid out from the pane's right
        /// edge leftwards and a strip wider than its pane would run off the screen.
        /// </summary>
        internal static float WidthNeeded(IInspectPane pane)
        {
            Collect(pane, Scratch);

            int slots = 0;
            bool anyOurs = false;

            for (int i = 0; i < Scratch.Count; i++)
            {
                InspectTabBase tab = Scratch[i];

                if (!tab.IsVisible || tab.Hidden)
                    continue;

                slots++;

                InspectBody body;

                if (Ours(tab, out body))
                    anyOurs = true;
            }

            if (anyOurs)
                slots++;

            Scratch.Clear();

            return slots * SlotWidth;
        }

        /// <summary>
        /// Whether this selection has a chip for the given body.
        ///
        /// <b>Answered from the tab list rather than from a copy of RimWorld's visibility rules.</b> Each of the
        /// six tabs decides for itself whether it applies -- the gear tab refuses babies and anomalies, the needs
        /// tab refuses wild animals and other factions' insects, the character tab wants a backstory -- and
        /// restating those tests here would be four rules to keep in step with a game that changes them. Asking
        /// whether the chip is there answers the same question and cannot drift.
        /// </summary>
        internal static bool Offers(IInspectPane pane, InspectBody body)
        {
            if (body == InspectBody.Overview)
                return true;

            Collect(pane, Scratch);

            bool found = false;

            for (int i = 0; i < Scratch.Count; i++)
            {
                InspectTabBase tab = Scratch[i];

                if (!tab.IsVisible || tab.Hidden)
                    continue;

                InspectBody offered;

                if (Ours(tab, out offered) && offered == body)
                {
                    found = true;

                    break;
                }
            }

            Scratch.Clear();

            return found;
        }

        /// <summary>
        /// The open tab that the pane has to draw itself, or null.
        ///
        /// Null both when nothing is open and when what is open is one of our six, since those have a body of
        /// their own. Asked by the frame, to know what to put in the body, and by the metrics, to know how big
        /// the pane has to be for it.
        /// </summary>
        internal static InspectTabBase OpenForeign(IInspectPane pane)
        {
            if (pane == null || pane.OpenTabType == null)
                return null;

            Collect(pane, Scratch);

            InspectTabBase found = null;

            for (int i = 0; i < Scratch.Count; i++)
            {
                InspectTabBase tab = Scratch[i];

                if (!tab.IsVisible || tab.GetType() != pane.OpenTabType)
                    continue;

                InspectBody body;

                if (!Ours(tab, out body))
                    found = tab;

                break;
            }

            Scratch.Clear();

            return found;
        }

        /// <summary>
        /// Draws the strip and runs whatever the click meant.
        ///
        /// Returns false when it did nothing, which is the caller's cue to let vanilla draw its own tabs: the
        /// world map's inspect pane comes through the same method and is not ours to rebuild.
        /// </summary>
        internal static bool Draw(IInspectPane pane, UIColorPaletteDef palette, float paneWidth)
        {
            Collect(pane, Tabs);

            float top = pane.PaneTopY - InspectPaneMetrics.TabStripHeight;
            float x = paneWidth - SlotWidth;
            float leftEdge = paneWidth;
            // Kept only so the overview chip knows where the row ended.

            bool anyOurs = false;

            for (int i = 0; i < Tabs.Count; i++)
            {
                InspectTabBase tab = Tabs[i];

                if (!tab.IsVisible)
                    continue;

                bool open = tab.GetType() == pane.OpenTabType;

                InspectBody body;
                bool ours = Ours(tab, out body);

                if (ours)
                    anyOurs = true;

                if (!tab.Hidden)
                {
                    Rect slot = new Rect(x, top, SlotWidth, InspectPaneMetrics.TabStripHeight);

                    // Our six read as selected from the pane's own state; everybody else's from whether their
                    // window is open, which is the only state they have.
                    bool selected = ours
                        ? pane.OpenTabType == null && InspectPaneState.Selected == body
                        : open;

                    if (Chip(slot, tab.labelKey.Translate(), selected, palette))
                    {
                        if (ours)
                            Choose(pane, body);
                        else
                            Toggle(tab, pane, open);
                    }

                    if (!selected && !tab.TutorHighlightTagClosed.NullOrEmpty())
                        UIHighlighter.HighlightOpportunity(slot, tab.TutorHighlightTagClosed);

                    leftEdge = x;
                    x -= SlotWidth;
                }

                // Nothing opens a window any more. A tab of ours is drawn by the pane's body; anybody else's is
                // drawn there too, by InspectForeignTab, so the chip row behaves the same way all the way along
                // it. DoTabGUI is what is deliberately not called here.
                if (open && ours)
                    pane.CloseOpenTab();
            }

            if (anyOurs)
            {
                Rect slot = new Rect(x, top, SlotWidth, InspectPaneMetrics.TabStripHeight);

                bool selected = pane.OpenTabType == null
                                && InspectPaneState.Selected == InspectBody.Overview;

                if (Chip(slot, InspectPaneState.LabelOf(InspectBody.Overview), selected, palette))
                    Choose(pane, InspectBody.Overview);

                leftEdge = x;
            }

            // Vanilla's connector strip is gone with the windows it connected: there is nothing above the chips
            // any more, so a bar joining the row to a window would be joining it to the map.
            Tabs.Clear();

            return true;
        }

        /// <summary>Switches the pane to one of our bodies, closing whatever window was over it.</summary>
        private static void Choose(IInspectPane pane, InspectBody body)
        {
            if (pane.OpenTabType != null)
                pane.CloseOpenTab();

            InspectPaneState.Select(body);

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Vanilla's own open and close, reproduced because both halves of it are private.
        ///
        /// The tutorial gate is kept: <c>InterfaceToggleTab</c> refuses to open a tab during a tutorial the
        /// tutorial has not asked for, and a rebuilt strip that ignored it would let a player click past a lesson
        /// the game is trying to teach.
        /// </summary>
        private static void Toggle(InspectTabBase tab, IInspectPane pane, bool open)
        {
            if (TutorSystem.TutorialMode && !open && !TutorSystem.AllowAction("ITab-" + tab.tutorTag + "-Open"))
                return;

            if (open)
            {
                pane.OpenTabType = null;

                SoundDefOf.TabClose.PlayOneShotOnCamera();

                return;
            }

            tab.OnOpen();

            pane.OpenTabType = tab.GetType();

            // Back to the overview underneath, because a window is now covering the pane and the body it was
            // showing has no lit chip any more. Leaving it would mean the pane was drawing one tab's content
            // while a different tab was the one visibly open.
            InspectPaneState.Select(InspectBody.Overview);

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
        }

        /// <summary>One chip. Filled when it is what the pane is showing, outlined when it is not.</summary>
        private static bool Chip(Rect slot, string label, bool selected, UIColorPaletteDef palette)
        {
            Rect chip = new Rect(slot.x + SlotPadX, slot.y + SlotPadY, slot.width - SlotPadX * 2f,
                slot.height - SlotPadY * 2f);

            bool over = Mouse.IsOver(chip);

            if (selected)
                UIElementPainter.FillRounded(chip, palette.AccentMuted);
            else
                UIElementPainter.OutlineRounded(chip, over ? palette.Accent : palette.Border,
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
                GUI.color = selected ? palette.Accent : over ? palette.TextPrimary : palette.TextSecondary;

                UIRichText.Label(chip, label ?? string.Empty);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            MouseoverSounds.DoRegion(chip);

            return Widgets.ButtonInvisible(chip);
        }

        /// <summary>
        /// The pane's tabs as a list.
        ///
        /// <c>CurTabs</c> is an <c>IEnumerable</c> that may or may not be a list, and vanilla's own code checks
        /// for <c>IList</c> before enumerating for that reason: a property that builds an iterator on every read
        /// is walked three times in one frame otherwise. It is also arbitrary code from whichever mod supplied
        /// the thing, so it is read once, into our list, behind a guard.
        /// </summary>
        private static void Collect(IInspectPane pane, List<InspectTabBase> into)
        {
            into.Clear();

            UIGuard.Try("Inspector.ReadTabs", () =>
            {
                IEnumerable<InspectTabBase> tabs = pane.CurTabs;

                if (tabs == null)
                    return;

                IList list = tabs as IList;

                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        InspectTabBase tab = list[i] as InspectTabBase;

                        if (tab != null)
                            into.Add(tab);
                    }

                    return;
                }

                foreach (InspectTabBase tab in tabs)
                {
                    if (tab != null)
                        into.Add(tab);
                }
            }, "The inspect pane shows no tabs for this selection.");
        }
    }
}
