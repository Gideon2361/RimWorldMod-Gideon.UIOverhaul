using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Animals;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// The Health body: what is wrong, how it is going, and what is being done about it.
    ///
    /// <b>It lists the conditions rather than drawing a body part tree,</b> which is the one structural change
    /// from vanilla's health card. The tree answers "what is the state of the left leg", a question nobody has;
    /// the list answers "what is wrong with her and how long have I got", which is the question that made
    /// somebody open the tab.
    ///
    /// <b>Capacities and Care share the top in two columns; the conditions run the full width underneath.</b>
    /// Both blocks above are short and fixed in shape -- a handful of impaired capacities, four care rows -- so a
    /// half width column costs them nothing. A condition label is the opposite: "Scratch (shambler hand)" is
    /// ordinary, and at half width every one of them truncated to an ellipsis that hid the part of the label
    /// saying what had done the damage. Reported on 2026-08-26.
    ///
    /// <b>Healthy capacities are hidden, and Aaron chose that when he approved the mockup.</b> Only the impaired
    /// ones are listed, with a count of the rest and a way to see them; a column of twelve rows all reading 100
    /// percent is furniture, and it is the furniture that pushes the two rows that matter off the bottom.
    /// </summary>
    internal static class InspectHealthBody
    {
        /// <summary>Reused between frames so a draw does not allocate.</summary>
        private static readonly List<PawnCapacityDef> Impaired = new List<PawnCapacityDef>();

        private static readonly List<PawnCapacityDef> Whole = new List<PawnCapacityDef>();

        /// <summary>Whether the capacities list is currently expanded to include the healthy ones.</summary>
        private static bool showAllCapacities;

        /// <param name="operations">
        /// Whether to list the queued operations. The hospital tab turns this off because it draws its own
        /// operations block immediately underneath, with the buttons that queue and cancel them; two lists of
        /// the same bills, one of them inert, is worse than either.
        /// </param>
        internal static float Draw(Rect view, Pawn pawn, UIColorPaletteDef palette, bool operations = true)
        {
            if (pawn.health == null)
                return 0f;

            Rect left;
            Rect right;

            InspectBodies.Columns(view, out left, out right);

            float y;

            if (InspectBodies.Live(right))
            {
                // Both start at the top and the taller one sets the floor for what follows. Which side that is
                // varies: Capacities grows when its hidden rows are expanded, and shrinks again when they are not.
                y = Mathf.Max(Capacities(left, view.y, pawn, palette), Care(right, view.y, pawn, palette));
            }
            else
            {
                // Too narrow to divide, so they stack in the order they would have been read in.
                y = Capacities(view, view.y, pawn, palette);
                y = Care(view, y, pawn, palette);
            }

            y = Conditions(view, y, pawn, palette);

            if (operations)
                y = Operations(view, y, pawn, palette);

            return y - view.y;
        }

        /// <summary>
        /// Every visible hediff, each carrying its own progress.
        ///
        /// <b><c>Hediff.Visible</c> is the filter, not the presence of the hediff,</b> which is the same test
        /// <c>HealthCardUtility</c> uses for its own list. A stage declared <c>becomeVisible false</c> is one
        /// RimWorld has decided the player should not be reading yet, and listing it here is how the pane ends up
        /// telling somebody they are hypothermic when their health tab says nothing at all. That exact fault was
        /// fixed in the pawns tab on 2026-08-22 and this is where it would come back.
        /// </summary>
        private static float Conditions(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;

            int shown = 0;

            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] != null && hediffs[i].Visible)
                    shown++;
            }

            y = InspectPaneParts.Cap(view, y, "Injuries and conditions",
                shown == 0 ? null : shown.ToString(), palette);

            if (shown == 0)
                return InspectPaneParts.Note(view, y, "Nothing to treat.", palette) + InspectPaneParts.BlockGap;

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];

                if (hediff == null || !hediff.Visible)
                    continue;

                y = Condition(view, y, hediff, palette);
            }

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// One condition: what and where, how it is going, and the sentence that says why.
        ///
        /// The right-hand reading is chosen by urgency rather than by hediff type, so the eye finds the bleeding
        /// wound before the old scar however the list happens to be ordered.
        /// </summary>
        private static float Condition(Rect view, float y, Hediff hediff, UIColorPaletteDef palette)
        {
            string where = hediff.Part != null ? hediff.Part.LabelCap : null;
            string label = where.NullOrEmpty()
                ? hediff.LabelCap
                : hediff.LabelCap + " - " + where;

            string state;
            Color color;
            string note = null;
            float bar = -1f;
            Color barColor = palette.Accent;
            float threshold = -1f;

            HediffComp_Immunizable immunizable =
                UIGuard.Try("Inspector.HealthImmunity", hediff.TryGetComp<HediffComp_Immunizable>, null, null);

            if (hediff.Bleeding)
            {
                state = "bleeding";
                color = palette.Danger;
            }
            else if (immunizable != null)
            {
                float immunity = immunizable.Immunity;
                bool winning = immunity > hediff.Severity;

                state = winning ? "winning" : "losing";
                color = winning ? palette.Success : palette.Danger;
                bar = immunity;
                barColor = winning ? palette.Success : palette.Danger;
                threshold = hediff.Severity;

                note = "Immunity " + InspectPaneParts.Percent(immunity) + " against severity "
                       + InspectPaneParts.Percent(hediff.Severity) + ".";
            }
            else if (hediff.TendableNow())
            {
                state = "needs tending";
                color = palette.Warning;
            }
            else
            {
                HediffComp_TendDuration tended =
                    UIGuard.Try("Inspector.HealthTend", hediff.TryGetComp<HediffComp_TendDuration>, null, null);

                if (tended != null && tended.IsTended)
                {
                    state = "tended " + InspectPaneParts.Percent(tended.tendQuality);
                    color = palette.Accent;
                }
                else if (hediff.IsPermanent())
                {
                    state = "permanent";
                    color = palette.TextDisabled;
                }
                else
                {
                    state = hediff.SeverityLabel;
                    color = palette.TextSecondary;
                }
            }

            float before = y;

            y = InspectPaneParts.Entry(view, y, label, state, color, note, palette);

            if (bar >= 0f)
            {
                Rect lane = new Rect(view.x, y - 2f, view.width, InspectPaneParts.TrackHeight);

                InspectPaneParts.Track(lane, bar, barColor, palette);

                if (threshold >= 0f)
                    InspectPaneParts.Tick(lane, threshold, palette.TextPrimary, true);

                y = lane.yMax + InspectPaneParts.RowGap;
            }

            Rect row = new Rect(view.x, before, view.width, y - before);

            if (Mouse.IsOver(row))
            {
                string tip = UIGuard.Try("Inspector.HealthTip", () => hediff.TipStringExtra, null, null);

                if (!tip.NullOrEmpty())
                    TooltipHandler.TipRegion(row, (TipSignal) tip);
            }

            return y;
        }

        /// <summary>Surgeries queued on this pawn, which is the half of the health tab that is a work order.</summary>
        private static float Operations(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            BillStack bills = pawn.health.surgeryBills;

            if (bills == null || bills.Count == 0)
                return y;

            y = InspectPaneParts.Cap(view, y, "Operations", bills.Count + " queued", palette);

            for (int i = 0; i < bills.Count; i++)
            {
                Bill bill = bills[i];

                if (bill == null)
                    continue;

                y = InspectPaneParts.Entry(view, y, bill.LabelCap,
                    bill.suspended ? "suspended" : "waiting",
                    bill.suspended ? palette.TextDisabled : palette.Warning, null, palette);
            }

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// The capacities, impaired first and the rest behind a count.
        ///
        /// The count is a control rather than a note: clicking it expands the list, so the hidden rows are one
        /// click away and the player is told outright how many of them there are. Hiding without saying how much
        /// is hidden is the version of this that would be dishonest.
        /// </summary>
        private static float Capacities(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            Impaired.Clear();
            Whole.Clear();

            UIGuard.Try("Inspector.HealthCapacities", () =>
            {
                List<PawnCapacityDef> defs = DefDatabase<PawnCapacityDef>.AllDefsListForReading;

                for (int i = 0; i < defs.Count; i++)
                {
                    PawnCapacityDef def = defs[i];

                    if (def == null || !def.CanShowOnPawn(pawn))
                        continue;

                    if (pawn.health.capacities.GetLevel(def) < 0.995f)
                        Impaired.Add(def);
                    else
                        Whole.Add(def);
                }

                Impaired.SortBy(def => pawn.health.capacities.GetLevel(def));
                Whole.SortBy(def => def.listOrder);
            }, "The inspect pane cannot list this pawn's capacities.");

            y = InspectPaneParts.Cap(view, y, "Capacities",
                Impaired.Count == 0 ? "all normal" : Impaired.Count + " impaired", palette);

            for (int i = 0; i < Impaired.Count; i++)
                y = Capacity(view, y, pawn, Impaired[i], palette);

            if (showAllCapacities)
            {
                for (int i = 0; i < Whole.Count; i++)
                    y = Capacity(view, y, pawn, Whole[i], palette);
            }

            if (Whole.Count > 0)
            {
                Rect toggle = new Rect(view.x, y, view.width, UIFonts.LineHeightOf(GameFont.Tiny) + 2f);

                bool over = Mouse.IsOver(toggle);

                GameFont previousFont = Text.Font;
                Color previousColor = GUI.color;

                try
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = over ? palette.Accent : palette.TextDisabled;

                    UIRichText.Label(toggle, showAllCapacities
                        ? "Hide the " + Whole.Count + " at 100%"
                        : Whole.Count + " more at 100%. Show all");
                }
                finally
                {
                    GUI.color = previousColor;
                    Text.Font = previousFont;
                }

                if (Widgets.ButtonInvisible(toggle))
                    showAllCapacities = !showAllCapacities;

                y = toggle.yMax;
            }

            Impaired.Clear();
            Whole.Clear();

            return y + InspectPaneParts.BlockGap;
        }

        private static float Capacity(Rect view, float y, Pawn pawn, PawnCapacityDef def,
            UIColorPaletteDef palette)
        {
            float level = pawn.health.capacities.GetLevel(def);

            return InspectPaneParts.Meter(view, y, def.GetLabelFor(pawn).CapitalizeFirst(), level,
                InspectPaneParts.Level(level, palette), InspectPaneParts.Percent(level),
                level >= 0.995f ? palette.TextDisabled : InspectPaneParts.Level(level, palette), palette);
        }

        /// <summary>
        /// How this pawn is treated, and the three numbers that say whether the treatment is keeping up.
        ///
        /// The medicine chip is the same shape as every other setting chip in the mod and opens RimWorld's own
        /// five level enum, which is the one case a list rather than a control is still the right answer.
        /// </summary>
        private static float Care(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            y = InspectPaneParts.Cap(view, y, "Care", null, palette);

            if (pawn.playerSettings != null && pawn.Faction == Faction.OfPlayer)
            {
                y = AnimalPaneParts.Chip(view, y, "Medicine",
                    UIGuard.Try("Inspector.CareLabel", () => pawn.playerSettings.medCare.GetLabel(), null, null),
                    palette, () => ChooseCare(pawn));

                if (pawn.RaceProps != null && pawn.RaceProps.Humanlike)
                    y = InspectPaneParts.Fact(view, y, "Self-tend",
                        pawn.playerSettings.selfTend ? "on" : "off",
                        pawn.playerSettings.selfTend ? palette.Accent : palette.TextDisabled, palette);
            }

            float pain = UIGuard.Try("Inspector.Pain", () => pawn.health.hediffSet.PainTotal, 0f, null);

            y = InspectPaneParts.Fact(view, y, "Pain", InspectPaneParts.Percent(pain),
                pain <= 0.01f ? palette.TextDisabled : InspectPaneParts.Level(1f - pain, palette), palette);

            float bleed = UIGuard.Try("Inspector.Bleeding", () => pawn.health.hediffSet.BleedRateTotal, 0f, null);

            y = InspectPaneParts.Fact(view, y, "Bleeding",
                bleed <= 0.001f ? "none" : InspectPaneParts.Percent(bleed) + " a day",
                bleed <= 0.001f ? palette.Success : palette.Danger, palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>RimWorld's own five medical care levels, as a list.</summary>
        private static void ChooseCare(Pawn pawn)
        {
            UIGuard.Try("Inspector.ChooseCare", () =>
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();

                foreach (MedicalCareCategory category in
                         (MedicalCareCategory[]) System.Enum.GetValues(typeof(MedicalCareCategory)))
                {
                    MedicalCareCategory chosen = category;

                    options.Add(new FloatMenuOption(category.GetLabel(),
                        () => pawn.playerSettings.medCare = chosen));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }, "The medicine setting cannot be changed from the inspect pane.");
        }
    }
}
