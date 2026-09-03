using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Pawns;
using Gideon.UIOverhaul.Features.Pawns.Templates;
using Gideon.UIOverhaul.Features.Work;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Mechs
{
    /// <summary>
    /// The right hand column: one mech, in the three part shape the hospital and research panes use.
    ///
    /// <b>Work priorities are the first section, and they only exist for mechs that can be given work.</b>
    /// A combat mech has an empty <c>mechEnabledWorkTypes</c> and <c>RaceProps.IsWorkMech</c> is false, so
    /// there is nothing to prioritise and no section for it.
    ///
    /// <b>Days to empty is arithmetic nobody should have to do.</b> <c>Need_MechEnergy.FallPerDay</c> is 10
    /// while active and 3 while idle, modified by <c>MechEnergyUsageFactor</c>. The game has the number; its
    /// interface has never divided by it.
    /// </summary>
    internal static class MechDetailPane
    {
        private const float CardHeight = 38f;

        private const float CardGap = 4f;

        private static readonly List<WorkTypeDef> works = new List<WorkTypeDef>();

        internal static void Draw(Rect rect, Pawn mech, ref Vector2 scroll, UIColorPaletteDef palette)
        {
            if (mech == null)
                return;

            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            Rect inner = rect.ContractedBy(12f);
            Rect canvas = new Rect(0f, 0f, inner.width - 18f, Height(mech));

            Widgets.BeginScrollView(inner, ref scroll, canvas);

            float y = 0f;

            y = Identity(canvas, y, mech, palette);
            y = Work(canvas, y, mech, palette);
            y = Energy(canvas, y, mech, palette);
            y = Condition(canvas, y, mech, palette);
            y = Orders(canvas, y, mech, palette);

            Buttons(canvas, y, mech, palette);

            Widgets.EndScrollView();
        }

        /// <summary>A heading costs ten of lead-in and twenty of its own row. See <see cref="Heading"/>.</summary>
        private const float HeadingHeight = 30f;

        /// <summary>One label and value pair. See <see cref="Line"/>.</summary>
        private const float LineHeight = 20f;

        /// <summary>
        /// Walked the same way it is drawn, so the scroll view agrees with the content.
        ///
        /// Every term here is the return of the method that draws that part rather than a number that looked
        /// about right: a pane measured one way and drawn another either clips its last button or trails a
        /// band of empty scroll under it, and which of the two depends on the mech.
        /// </summary>
        private static float Height(Pawn mech)
        {
            float y = 56f;

            if (MechFacts.IsWorkMech(mech))
            {
                Fill(mech);

                y += HeadingHeight + works.Count * (CardHeight + CardGap);
            }

            // Energy: charge, fall rate, empty in, shutdown at.
            y += HeadingHeight + 4f * LineHeight;

            MechFacts.DamagedParts(mech, MechsPanel.DamagedScratch);

            // Condition: integrity, one line per damaged part, auto repair.
            y += HeadingHeight + (2 + MechsPanel.DamagedScratch.Count) * LineHeight;

            // Orders: work mode, area, current job, tag.
            y += HeadingHeight + 4f * LineHeight;

            // The button row, which sits ten below the last line and is twenty six tall.
            return y + 36f;
        }

        // -------------------------------------------------------------------------------------------
        // Sections
        // -------------------------------------------------------------------------------------------

        private static float Identity(Rect view, float y, Pawn mech, UIColorPaletteDef palette)
        {
            Rect portrait = new Rect(view.x, y, 52f, 52f);

            PawnPortraitCell.Draw(portrait, mech, palette, palette.SurfaceSunken);

            float x = portrait.xMax + 10f;
            float width = Mathf.Max(0f, view.xMax - x);

            TabParts.RowLabel(new Rect(x, y, width, 20f), mech.LabelShortCap, palette.TextPrimary,
                MechsFaces.Condensed, MechsFaces.Size.Detail);

            string weight = MechFacts.WeightClass(mech);
            MechanitorControlGroup group = mech.GetMechControlGroup();

            string meta = (weight.NullOrEmpty() ? string.Empty : weight.ToUpperInvariant() + "  -  ")
                          + MechFacts.BandwidthCost(mech) + " BW"
                          + (group == null ? string.Empty : "  -  GROUP " + group.Index);

            TabParts.RowLabel(new Rect(x, y + 20f, width, 16f), meta, palette.TextDisabled, MechsFaces.Mono,
                MechsFaces.Size.Meta);

            Pawn overseer = mech.GetOverseer();

            TabParts.RowLabel(new Rect(x, y + 35f, width, 16f),
                overseer == null ? "no overseer" : "overseen by " + overseer.LabelShortCap,
                overseer == null ? palette.Danger : palette.TextSecondary, MechsFaces.Mono,
                MechsFaces.Size.Meta);

            return y + 56f;
        }

        /// <summary>
        /// The work priority cards, and the tools that copy them.
        ///
        /// <b>The number box, never the on/off checkbox.</b> <c>PawnWorkGrid</c> swaps the box for a checkbox
        /// when the player has manual priorities switched off, because for a colonist the number then
        /// controls nothing: <c>Pawn_WorkSettings.GetPriority</c> flattens it to 3. That guard is
        /// <c>RaceProps.Humanlike</c>, so for a mech the number always controls everything, and drawing the
        /// checkbox here would let one click write a 3 over a 7 and lose it silently.
        /// </summary>
        private static float Work(Rect view, float y, Pawn mech, UIColorPaletteDef palette)
        {
            if (!MechFacts.IsWorkMech(mech))
                return y;

            Fill(mech);

            bool live = MechFacts.PrioritiesLive(mech);

            y = Heading(view, y, "Work priorities", palette, live ? null : "IDLE",
                live ? (Color?) null : palette.Warning);

            float tools = PawnTools.WidthFor(PawnTemplateScope.Priorities);

            if (tools > 0f && tools < view.width - 40f)
            {
                PawnTools.Row(new Rect(view.xMax - tools, y - 24f, tools, 20f), mech,
                    PawnTemplateScope.Priorities, palette);
            }

            for (int i = 0; i < works.Count; i++)
                y = Card(view, y, mech, works[i], palette, live);

            return y;
        }

        private static float Card(Rect view, float y, Pawn mech, WorkTypeDef work, UIColorPaletteDef palette,
            bool live)
        {
            Rect card = new Rect(view.x, y, view.width, CardHeight);
            int priority = MechFacts.PriorityOf(mech, work);

            Color accent = priority <= 0
                ? palette.ControlBackgroundFaded
                : WorkPanel.ColorOfPriority(priority, palette);

            UIElementPainter.OutlineRounded(card, palette.Border, palette.SurfaceSunken);
            Widgets.DrawBoxSolid(new Rect(card.x, card.y, 3f, card.height), accent);

            Rect box = new Rect(card.xMax - MechsPanel.PriorityBoxSize - 7f,
                card.y + (card.height - MechsPanel.PriorityBoxSize) * 0.5f, MechsPanel.PriorityBoxSize,
                MechsPanel.PriorityBoxSize);

            float textX = card.x + 12f;
            float textWidth = Mathf.Max(0f, box.x - textX - 6f);

            TabParts.RowLabel(new Rect(textX, card.y + 3f, textWidth, 18f), WorkPanel.LabelOf(work),
                priority > 0 ? palette.TextPrimary : palette.TextSecondary, MechsFaces.Condensed,
                MechsFaces.Size.RailName);

            // Every work mech Biotech ships is skill 10 at everything it does. WorkPanel.SkillColor answers
            // with an empty label for any pawn whose skills is null, which is every mech, so this is what the
            // subtitle says instead.
            TabParts.RowLabel(new Rect(textX, card.y + 20f, textWidth, 14f),
                "skill " + MechFacts.FixedSkill(mech), palette.TextDisabled, MechsFaces.Mono,
                MechsFaces.Size.Meta);

            Box(box, card, mech, work, priority, palette, live);

            return y + CardHeight + CardGap;
        }

        private static void Box(Rect box, Rect card, Pawn mech, WorkTypeDef work, int priority,
            UIColorPaletteDef palette, bool live)
        {
            bool over = Mouse.IsOver(box);

            Widgets.DrawBoxSolid(box, priority == 0 ? palette.SurfaceRaised : palette.PanelBackground);

            if (over)
                Widgets.DrawBoxSolid(box, palette.HoverOverlay);

            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = priority <= 0 ? palette.TextDisabled : WorkPanel.ColorOfPriority(priority, palette);

            UITextControl.Label(box, priority.ToString(), MechsFaces.Mono, MechsFaces.Size.Priority);

            Text.Anchor = anchor;
            GUI.color = color;

            if (Mouse.IsOver(card))
            {
                TooltipHandler.TipRegion(card, (TipSignal) (work.gerundLabel.CapitalizeFirst()
                    + "\n\n" + work.description
                    + "\n\nLeft click raises the priority, right click lowers it."
                    + (live
                        ? string.Empty
                        : "\n\nThis is idle right now: the group is not in work mode, so the mech's think "
                          + "tree never reaches the work giver.")));
            }

            if (Event.current.type != EventType.MouseDown || !over)
                return;

            int step = Event.current.button == 1 ? -1 : 1;
            int next = priority + step;

            // Wraps at both ends, so a full circuit is possible from either button. The same range and the
            // same wrap the pawns tab uses, because WorkPriorityRange already widened SetPriority's bound to
            // 9 and mechs go through that same method.
            if (next > WorkPriorityRange.Lowest)
                next = 0;
            else if (next < 0)
                next = WorkPriorityRange.Lowest;

            MechFacts.SetPriority(mech, work, next);

            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            Event.current.Use();
        }

        private static float Energy(Rect view, float y, Pawn mech, UIColorPaletteDef palette)
        {
            y = Heading(view, y, "Energy", palette, null, null);

            float charge = MechFacts.Charge(mech);

            y = Line(view, y, "Charge", charge < 0f ? "-" : Mathf.RoundToInt(charge * 100f) + "% of 100",
                palette, charge >= 0f && charge * 100f <= MechFacts.ShutdownAt ? palette.Warning : (Color?) null);

            float fall = MechFacts.FallPerDay(mech);

            y = Line(view, y, fall > 0f ? "Falling" : "Gaining", Mathf.Abs(fall).ToString("0.0") + " / day",
                palette, null);

            float days = MechFacts.DaysToEmpty(mech);

            y = Line(view, y, "Empty in", days < 0f ? "-" : days.ToString("0.0") + " days", palette,
                days >= 0f && days < MechFacts.ShortOnCharge ? palette.Warning : (Color?) null);

            return Line(view, y, "Shuts down at", Mathf.RoundToInt(MechFacts.ShutdownAt) + "%", palette, null);
        }

        private static float Condition(Rect view, float y, Pawn mech, UIColorPaletteDef palette)
        {
            y = Heading(view, y, "Condition", palette, null, null);

            float integrity = MechFacts.Integrity(mech);

            y = Line(view, y, "Integrity", Mathf.RoundToInt(integrity * 100f) + "%", palette,
                integrity < 0.999f ? palette.Warning : (Color?) null);

            MechFacts.DamagedParts(mech, MechsPanel.DamagedScratch);

            for (int i = 0; i < MechsPanel.DamagedScratch.Count; i++)
                y = Line(view, y, MechsPanel.DamagedScratch[i], string.Empty, palette, palette.Warning);

            CompMechRepairable repairable = MechFacts.Repairable(mech);

            return Line(view, y, "Auto repair",
                repairable == null ? "-" : repairable.autoRepair ? "on" : "off", palette, null);
        }

        private static float Orders(Rect view, float y, Pawn mech, UIColorPaletteDef palette)
        {
            y = Heading(view, y, "Orders", palette, null, null);

            MechWorkModeDef mode = mech.GetMechWorkMode();

            y = Line(view, y, "Work mode",
                mode == null ? "-" : mode.LabelCap.ToString().ToLowerInvariant(), palette, null);

            Area area = mech.playerSettings == null
                ? null
                : mech.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap;

            y = Line(view, y, "Allowed area", area == null ? "Unrestricted" : area.Label, palette, null);

            string job = mech.CurJob == null || mech.jobs == null || mech.jobs.curDriver == null
                ? "nothing"
                : mech.jobs.curDriver.GetReport().TrimEnd('.');

            y = Line(view, y, "Doing", job, palette,
                MechFacts.Hibernating(mech) ? palette.Info : (Color?) null);

            string tag = MechFacts.Tag(mech);

            return Line(view, y, "Tag", tag.NullOrEmpty() ? "-" : tag, palette, null);
        }

        private static void Buttons(Rect view, float y, Pawn mech, UIColorPaletteDef palette)
        {
            float half = (view.width - 5f) * 0.5f;

            Rect draft = new Rect(view.x, y + 10f, half, 26f);
            Rect goTo = new Rect(view.x + half + 5f, y + 10f, half, 26f);

            if (TabParts.Button(draft, mech.Drafted ? "Undraft" : "Draft", palette, mech.drafter != null))
            {
                UIGuard.Try("Mechs.Draft", () => mech.drafter.Drafted = !mech.drafter.Drafted,
                    "That mech could not be drafted from here. Selecting it on the map still works.");

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            if (TabParts.Button(goTo, "Go to", palette))
            {
                PawnCameraJump.Request(mech);

                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        // -------------------------------------------------------------------------------------------
        // Parts
        // -------------------------------------------------------------------------------------------

        private static float Heading(Rect view, float y, string text, UIColorPaletteDef palette,
            string badge, Color? badgeColor)
        {
            y += 10f;

            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextDisabled;

            UITextControl.Label(new Rect(view.x, y, view.width - 60f, 14f), text.ToUpperInvariant(),
                MechsFaces.Mono, MechsFaces.Size.Caption);

            if (!badge.NullOrEmpty())
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = badgeColor ?? palette.TextDisabled;

                UITextControl.Label(new Rect(view.xMax - 60f, y, 60f, 14f), badge, MechsFaces.Mono,
                    MechsFaces.Size.Caption);
            }

            Text.Anchor = anchor;
            GUI.color = color;

            Widgets.DrawLineHorizontal(view.x, y + 16f, view.width);

            return y + 20f;
        }

        private static float Line(Rect view, float y, string label, string value, UIColorPaletteDef palette,
            Color? valueColor)
        {
            TabParts.RowLabel(new Rect(view.x, y, view.width * 0.55f, 18f), label, palette.TextSecondary,
                MechsFaces.Condensed, MechsFaces.Size.Row);

            if (!value.NullOrEmpty())
            {
                TextAnchor anchor = Text.Anchor;
                Color color = GUI.color;

                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = valueColor ?? palette.TextPrimary;

                UITextControl.LabelEllipses(new Rect(view.x + view.width * 0.42f, y, view.width * 0.58f, 18f),
                    value, MechsFaces.Mono, MechsFaces.Size.Meta);

                Text.Anchor = anchor;
                GUI.color = color;
            }

            return y + 20f;
        }

        private static void Fill(Pawn mech)
        {
            works.Clear();

            List<WorkTypeDef> enabled = MechFacts.WorkTypes(mech);

            if (enabled != null)
                works.AddRange(enabled);
        }
    }
}
