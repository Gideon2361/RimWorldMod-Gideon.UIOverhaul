using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using Gideon.UIOverhaul.Features.Trade.Shell;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Trade.Comms
{
    /// <summary>
    /// Who you can call, as cards rather than as a list of bare text lines.
    ///
    /// <b>A float menu is one line of text and cannot hold anything else.</b> That is the whole diagnosis:
    /// vanilla answers "who can I call" with one <c>FloatMenuOption</c> per target, so there is no goodwill, no
    /// trader kind, no hint of what anybody is carrying and no clock on the orbital trader that is about to
    /// leave. The interface behind the menu was never the problem -- <c>ICommunicable</c> requires a name, a
    /// detail line and a faction of every implementer, so the details were always there with nowhere to put them.
    ///
    /// <b>Every call is the target's own, unchanged.</b> The button runs the action out of that target's
    /// <c>CommFloatMenuOption</c>, which is where the beacon check for an orbital trader lives and where a mod's
    /// own conditions live. This window decides what a card looks like and nothing else.
    ///
    /// <b>Refused targets stay visible, dimmed, with the reason.</b> Vanilla replaces the entire menu with one
    /// disabled line when the console cannot be used, so during a solar flare a player cannot even see who they
    /// would have been able to call. Here the console's own problem is stated once at the top and the directory
    /// stays readable under it.
    /// </summary>
    internal class Dialog_UIComms : Window
    {
        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search",
            Icon = TexButton.Search,
            MaxLength = 40
        };

        private readonly Building_CommsConsole console;
        private readonly Pawn negotiator;

        private readonly List<CommsTarget> targets = new List<CommsTarget>();
        private readonly List<CommsTarget> shown = new List<CommsTarget>();
        private readonly List<TradeRailEntry> rail = new List<TradeRailEntry>();

        private string group = All;

        private const string All = "all";

        private Vector2 railScroll;
        private bool railDragging;
        private float railDragOffset;

        private Vector2 listScroll;
        private bool listDragging;
        private float listDragOffset;

        private string problem;

        internal Dialog_UIComms(Building_CommsConsole console, Pawn negotiator)
        {
            this.console = console;
            this.negotiator = negotiator;

            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;
            doCloseX = true;
            soundAppear = SoundDefOf.CommsWindow_Open;
            soundClose = SoundDefOf.CommsWindow_Close;
        }

        public override Vector2 InitialSize =>
            new Vector2(Mathf.Min(900f, UI.screenWidth - 20f), Mathf.Min(700f, UI.screenHeight - 20f));

        public override void PostOpen()
        {
            base.PostOpen();

            Search.Clear();

            Refresh();
        }

        private void Refresh()
        {
            problem = CommsTargets.ConsoleProblem(console, negotiator);

            CommsTargets.All(console, negotiator, targets);
        }

        public override void DoWindowContents(Rect inRect)
        {
            TradeShell.Guarded("Comms.Window", inRect, () => Contents(inRect),
                "The comms directory failed to draw. No call has been placed. Close it and click the console "
                + "again, or switch the window off under Additional Features to use RimWorld's own menu.");
        }

        private void Contents(Rect inRect)
        {
            if (console == null || negotiator == null)
            {
                Close();

                return;
            }

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Rect headerRect;
            Rect railRect;
            Rect tableRect;
            Rect spineRect;
            Rect footerRect;

            // No spine: there is nothing being assembled here. A call is one click and then this window is gone,
            // so the third column would be a panel with nothing to put in it. The shell allows for that, which is
            // what makes it a shell rather than a template.
            TradeShell.Layout(inRect, true, false, out headerRect, out railRect, out tableRect, out spineRect,
                out footerRect);

            TradeShell.Header(headerRect, "Comms console", Detail(), palette);

            Rail(railRect, palette);
            Cards(tableRect, palette);
            Footer(footerRect, palette);
        }

        private string Detail()
        {
            int reachable = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Callable)
                    reachable++;
            }

            string signal = problem.NullOrEmpty() ? "signal clear" : problem;

            return negotiator.LabelShortCap + " calling · " + reachable + " of " + targets.Count
                   + " reachable · " + signal;
        }

        // ---------------------------------------------------------------------------------------

        private void Rail(Rect rect, UIColorPaletteDef palette)
        {
            rail.Clear();

            rail.Add(TradeRailEntry.Of(All, "Everyone", targets.Count));

            rail.Add(TradeRailEntry.Group("By kind"));

            Add(CommsTargets.GroupTraders, "Orbital traders", palette);
            Add(CommsTargets.GroupAllies, "Allies", palette);
            Add(CommsTargets.GroupNeutral, "Neutral", palette);
            Add(CommsTargets.GroupHostile, "Hostile", palette);

            string picked = TradeRail.Draw(rect, rail, group, ref railScroll, ref railDragging, ref railDragOffset,
                palette);

            if (picked == null)
                return;

            group = picked;
            listScroll = Vector2.zero;
        }

        private void Add(string key, string label, UIColorPaletteDef palette)
        {
            int count = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Group == key)
                    count++;
            }

            TradeRailEntry entry = TradeRailEntry.Of(key, label, count);

            if (key == CommsTargets.GroupHostile && count > 0)
                entry.CountColor = palette.Danger;

            rail.Add(entry);
        }

        // ---------------------------------------------------------------------------------------

        private const float CardHeight = 74f;

        private void Cards(Rect rect, UIColorPaletteDef palette)
        {
            Search.Draw(new Rect(rect.x, rect.y, Mathf.Min(320f, rect.width), 28f), palette);

            float top = rect.y + 36f;

            shown.Clear();

            for (int i = 0; i < targets.Count; i++)
            {
                CommsTarget target = targets[i];

                if (group != All && target.Group != group)
                    continue;

                if (!Search.IsEmpty && !Search.Matches(target.Label) && !Search.Matches(target.Kind))
                    continue;

                shown.Add(target);
            }

            float width = GzpPalette.ContentWidth(rect);

            Rect list = new Rect(rect.x, top, rect.width, Mathf.Max(0f, rect.yMax - top));
            Rect view = new Rect(0f, 0f, width, shown.Count * (CardHeight + 6f) + 2f);

            Widgets.BeginScrollView(list, ref listScroll, view, false);

            for (int i = 0; i < shown.Count; i++)
                Card(new Rect(0f, i * (CardHeight + 6f), view.width, CardHeight), shown[i], palette);

            Widgets.EndScrollView();

            GzpPalette.FlatScrollbar(list, view.height, ref listScroll, ref listDragging, ref listDragOffset);

            if (shown.Count > 0)
                return;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = palette.TextDisabled;

            Widgets.Label(list, targets.Count == 0
                ? "NoCommsTarget".Translate().ToString()
                : "Nobody here matches that.");

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        /// <summary>
        /// One target: who they are, how they feel about you, and the button.
        ///
        /// <b>A refused card is dimmed, not removed,</b> and its button is replaced by the reason. Both halves
        /// matter: the card is still how a player learns that this faction exists and what their goodwill is,
        /// and the reason is what tells them whether it is worth waiting.
        /// </summary>
        private void Card(Rect rect, CommsTarget target, UIColorPaletteDef palette)
        {
            bool callable = target.Callable && problem.NullOrEmpty();

            Color tone = CommsTargets.ToneFor(target, palette);

            GzpPalette.Card(rect, callable ? tone : palette.Border, Mouse.IsOver(rect) && callable);

            Rect inner = rect.ContractedBy(10f);

            inner = new Rect(inner.x + 4f, inner.y, inner.width - 4f, inner.height);

            float buttonWidth = 110f;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.WordWrap = false;

                float goodwillWidth = 0f;

                if (target.HasGoodwill)
                {
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.MiddleRight;
                    GUI.color = tone;

                    string goodwill = target.Goodwill.ToStringWithSign();

                    goodwillWidth = Text.CalcSize(goodwill).x + 14f;

                    Widgets.Label(
                        new Rect(inner.xMax - buttonWidth - goodwillWidth, inner.y, goodwillWidth - 10f,
                            inner.height), goodwill);
                }

                float labelWidth = Mathf.Max(60f, inner.width - buttonWidth - goodwillWidth - 10f);

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = callable ? palette.TextPrimary : palette.TextDisabled;

                float line = UIFonts.LineHeightOf(GameFont.Small);

                Widgets.LabelEllipses(new Rect(inner.x, inner.y, labelWidth, line), target.Label ?? "Unknown");

                Text.Font = GameFont.Tiny;
                GUI.color = callable ? palette.TextSecondary : palette.TextDisabled;

                float tiny = UIFonts.LineHeightOf(GameFont.Tiny);

                if (!target.Kind.NullOrEmpty())
                    Widgets.LabelEllipses(new Rect(inner.x, inner.y + line, labelWidth, tiny), target.Kind);

                if (!target.Detail.NullOrEmpty())
                {
                    GUI.color = palette.TextDisabled;

                    Widgets.LabelEllipses(new Rect(inner.x, inner.y + line + tiny, labelWidth, tiny),
                        target.Detail);
                }
            }
            finally
            {
                Text.WordWrap = true;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            Rect button = new Rect(inner.xMax - buttonWidth, inner.y + (inner.height - 32f) * 0.5f, buttonWidth,
                32f);

            if (!callable)
            {
                string reason = problem.NullOrEmpty() ? target.Refusal : problem;

                TradeStepper.Refused(button, "Unreachable", palette);

                if (Mouse.IsOver(rect) && !reason.NullOrEmpty())
                    TooltipHandler.TipRegion(rect, (TipSignal) reason);

                return;
            }

            // A trader is the only kind of call this window fills in: it is the one that opens a deal rather than
            // a conversation, and a directory of a dozen identical outlines gives a player nothing to aim at.
            if (!UIActionButtonControl.Draw(button, target.Verb, palette,
                    target.Group == CommsTargets.GroupTraders))
                return;

            // Run and close. The action opens whatever the target opens -- a trade window, a negotiation dialog,
            // a message saying there is no beacon -- and leaving the directory standing behind it would put two
            // windows on the stack for one decision.
            UIGuard.Try("Comms.Call", target.Call, "That call was not placed.");

            Close();
        }

        private void Footer(Rect rect, UIColorPaletteDef palette)
        {
            if (!problem.NullOrEmpty())
            {
                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.Danger;
                Text.WordWrap = false;

                Widgets.LabelEllipses(new Rect(rect.x, rect.y, rect.width - 170f, rect.height), problem);

                Text.WordWrap = true;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
            else
            {
                TradeShell.KeyHint(rect, rect.x, "Esc", "close without calling", palette);
            }

            if (UIActionButtonControl.Draw(new Rect(rect.xMax - 148f, rect.y + (rect.height - 34f) * 0.5f, 148f, 34f),
                    "CancelButton".Translate(), palette))
                Close();
        }
    }
}
