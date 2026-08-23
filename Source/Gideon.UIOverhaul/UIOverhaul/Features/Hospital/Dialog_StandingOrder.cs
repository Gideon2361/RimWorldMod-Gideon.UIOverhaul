using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>
    /// One standing drug order: give what, to whom, how often, only while, and who carries it.
    ///
    /// <b>Two columns because there are two questions.</b> The left is the order itself and the right is the gate
    /// on it. The same arrangement the hunting bill uses, for the same reason: what a standing instruction does
    /// and what stops it doing that are read separately.
    ///
    /// <b>Nothing is applied on a Save button.</b> Every control writes straight to the order, the way a bill's
    /// own settings do, and closing is just closing. There is no draft state to lose and no half-saved order.
    ///
    /// <b>The condition list is the hunting bill's species list, for hediffs.</b> Searchable, grouped, and with
    /// the live count on the map beside each name, so ticking "gunshot" is a decision made against what is
    /// actually true of the colony rather than against a memory of it.
    /// </summary>
    internal class Dialog_StandingOrder : Window
    {
        private const float ColumnGap = 16f;

        private const float FooterHeight = 40f;

        private const float CountWidth = 62f;

        private static readonly UITextBoxControl Every = new UITextBoxControl
        {
            Placeholder = "12",
            MaxLength = 4,
            ShowClearButton = false
        };

        private static readonly UITextBoxControl ConditionSearch = new UITextBoxControl
        {
            Placeholder = "Search conditions",
            Icon = TexButton.Search,
            MaxLength = 30
        };

        private readonly StandingDrugOrder order;

        private readonly Map map;

        private readonly List<HediffDef> matches = new List<HediffDef>();

        private readonly HashSet<string> collapsed = new HashSet<string>();

        private Vector2 conditionScroll;

        private Vector2 leftScroll;

        /// <summary>Left column height from the last draw. Remembered rather than predicted.</summary>
        private float measuredLeft;

        internal Dialog_StandingOrder(StandingDrugOrder order, Map map)
        {
            this.order = order;
            this.map = map;

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(760f, 660f); }
        }

        public override void PostOpen()
        {
            base.PostOpen();

            if (order == null)
                return;

            Every.Text = order.every.ToString();

            ConditionSearch.Clear();
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Hospital.OrderDialog", inRect, () => Contents(inRect),
                "This window failed to draw. The order is unchanged and can be paused from the hospital tab.");
        }

        private void Contents(Rect inRect)
        {
            if (order == null)
                return;

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Medium;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 40f, 32f), "Standing order");

                // Back to the body font immediately: a Medium left behind here is inherited by every control
                // below that does not set its own, which is a fault this mod has already shipped once.
                Text.Font = GameFont.Small;

                Rect body = new Rect(inRect.x, inRect.y + 38f, inRect.width,
                    Mathf.Max(0f, inRect.height - 38f - FooterHeight));

                float left = Mathf.Round((body.width - ColumnGap) * 0.5f);

                Left(new Rect(body.x, body.y, left, body.height), palette);

                Gate(new Rect(body.x + left + ColumnGap, body.y, body.width - left - ColumnGap, body.height),
                    palette);

                Footer(new Rect(inRect.x, inRect.yMax - FooterHeight + 6f, inRect.width, FooterHeight - 6f),
                    palette);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        // ---------------------------------------------------------------------------------------
        // Left: the order
        // ---------------------------------------------------------------------------------------

        private void Left(Rect rect, UIColorPaletteDef palette)
        {
            Rect view = new Rect(0f, 0f, rect.width - 18f, measuredLeft > 0f ? measuredLeft : rect.height);

            Widgets.BeginScrollView(rect, ref leftScroll, view);

            Rect column = new Rect(0f, 0f, view.width, view.height);
            float y = 0f;

            y = Give(column, y, palette);
            y = To(column, y, palette);
            y = HowOften(column, y, palette);
            y = Nurse(column, y, palette);
            y = Safeguards(column, y, palette);

            measuredLeft = y + 8f;

            Widgets.EndScrollView();
        }

        /// <summary>
        /// The drug, chosen from every drug in the game that has an administer recipe.
        ///
        /// <b>Not a list anyone maintains.</b> RimWorld generates <c>Administer_&lt;drug&gt;</c> for every drug
        /// that exists, so a mod's painkiller is offered here the day it is installed.
        /// </summary>
        private float Give(Rect column, float y, UIColorPaletteDef palette)
        {
            y = HospitalParts.Heading(column, y, "GIVE", palette);

            Rect button = new Rect(column.x, y, column.width, 28f);

            if (HospitalParts.Button(button, order.drug != null ? order.drug.LabelCap.ToString() : "Choose a drug",
                    palette))
                Find.WindowStack.Add(new FloatMenu(DrugOptions()));

            y = button.yMax + HospitalParts.RowGap;

            if (order.drug != null && order.Recipe == null)
                y = HospitalParts.Note(column, y,
                    "The game has no administer recipe for " + order.drug.label
                    + ", so this order cannot fire. Choose another drug.", palette, GameFont.Tiny,
                    palette.Danger);

            return y + HospitalParts.BlockGap;
        }

        /// <summary>
        /// Every drug with an administer recipe, as a float menu.
        ///
        /// <b>The one float menu in this window, and it earns it:</b> the list is however many drugs are
        /// installed, it is chosen once when the order is created and never looked at again, and it has no state
        /// worth showing while closed. That is the case the mod's rule against float menus explicitly leaves open.
        /// </summary>
        private List<FloatMenuOption> DrugOptions()
        {
            List<FloatMenuOption> found = new List<FloatMenuOption>();

            UIGuard.Try("Hospital.DrugList", () =>
            {
                List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
                List<ThingDef> drugs = new List<ThingDef>();

                for (int i = 0; i < all.Count; i++)
                {
                    ThingDef def = all[i];

                    if (def == null || !def.IsDrug)
                        continue;

                    if (DefDatabase<RecipeDef>.GetNamedSilentFail("Administer_" + def.defName) == null)
                        continue;

                    drugs.Add(def);
                }

                drugs.SortBy(def => def.label);

                for (int i = 0; i < drugs.Count; i++)
                {
                    ThingDef def = drugs[i];
                    int stock = HospitalSurgery.Stock(map, def);

                    found.Add(new FloatMenuOption(
                        def.LabelCap + (stock > 0 ? "  (" + stock + " in stock)" : "  (none in stock)"),
                        () =>
                        {
                            order.drug = def;

                            HospitalRoster.Invalidate();
                        }, def));
                }
            }, null);

            if (found.Count == 0)
                found.Add(new FloatMenuOption("No drug in this install can be administered.", null));

            return found;
        }

        private float To(Rect column, float y, UIColorPaletteDef palette)
        {
            y = HospitalParts.Heading(column, y, "TO", palette);

            float width = Mathf.Floor((column.width - 8f) / 3f);

            HospitalParts.Segment(new Rect(column.x, y, width, 24f), "One patient",
                order.target == StandingOrderTarget.OnePatient, palette,
                () => order.target = StandingOrderTarget.OnePatient);

            HospitalParts.Segment(new Rect(column.x + width + 4f, y, width, 24f), "In a medical bed",
                order.target == StandingOrderTarget.MedicalBed, palette,
                () => order.target = StandingOrderTarget.MedicalBed);

            HospitalParts.Segment(new Rect(column.x + width * 2f + 8f, y, column.xMax - column.x - width * 2f - 8f,
                    24f), "Everyone",
                order.target == StandingOrderTarget.Everyone, palette,
                () => order.target = StandingOrderTarget.Everyone);

            y += 26f + HospitalParts.RowGap;

            if (order.target == StandingOrderTarget.OnePatient)
            {
                Rect button = new Rect(column.x, y, column.width, 26f);

                if (HospitalParts.Button(button,
                        order.patient != null ? order.patient.LabelShortCap.ToString() : "Choose a patient",
                        palette))
                    Dialog_PickColonist.For(map, "Who is this order for?", chosen =>
                    {
                        order.patient = chosen;

                        HospitalRoster.Invalidate();
                    }, order.patient);

                y = button.yMax + HospitalParts.RowGap;
            }
            else
            {
                y = HospitalParts.Note(column, y,
                    order.target == StandingOrderTarget.MedicalBed
                        ? "Anybody in the colony lying in a bed marked medical, as they come and go."
                        : "Every colonist, prisoner and slave on this map. Penoxycyline before a toxic fallout, "
                          + "and very little else.", palette);
            }

            return y + HospitalParts.BlockGap;
        }

        private float HowOften(Rect column, float y, UIColorPaletteDef palette)
        {
            y = HospitalParts.Heading(column, y, "HOW OFTEN", palette);

            Rect box = new Rect(column.x, y, 72f, 26f);

            if (Every.Draw(box, palette))
                order.every = HospitalParts.ParseCount(Every.Text, order.every, 1, 999);

            HospitalParts.Segment(new Rect(box.xMax + 6f, y, 72f, 26f), "hours",
                order.period == StandingOrderPeriod.Hours, palette,
                () => order.period = StandingOrderPeriod.Hours);

            HospitalParts.Segment(new Rect(box.xMax + 84f, y, 72f, 26f), "days",
                order.period == StandingOrderPeriod.Days, palette,
                () => order.period = StandingOrderPeriod.Days);

            y = box.yMax + HospitalParts.RowGap;

            y = HospitalParts.Note(column, y,
                "Each patient has their own clock, started when they were last dosed by this order. A dose that "
                + "is skipped because the condition is not met does not bank: the clock keeps running.", palette);

            return y + HospitalParts.BlockGap;
        }

        private float Nurse(Rect column, float y, UIColorPaletteDef palette)
        {
            y = HospitalParts.Heading(column, y, order.nurse != null ? "NURSE: ASSIGNED" : "NURSE", palette);

            Rect button = new Rect(column.x, y, column.width, 26f);

            if (HospitalParts.Button(button, order.NurseLabel, palette))
                Dialog_PickColonist.For(map, "Who delivers this?", chosen => order.nurse = chosen, order.nurse,
                    true);

            y = button.yMax + HospitalParts.RowGap;

            y = HospitalParts.Note(column, y,
                order.nurse != null
                    ? "Nobody else will pick it up. " + order.nurse.LabelShortCap
                      + " is not made to drop what they are doing; the dose waits for them."
                    : "Whoever is free and on doctoring takes it.", palette);

            return y + HospitalParts.BlockGap;
        }

        /// <summary>
        /// The two safeguards, and the one drug they must not stop.
        ///
        /// <b>Both on by default, because auto-dosing is how you kill somebody.</b> A dose is skipped when the
        /// drug would tip the patient into overdose, and the order holds itself when they pick up an addiction.
        /// The hold says so on the row rather than stopping quietly, because an order that silently does nothing
        /// is worse than one that refuses out loud.
        /// </summary>
        private float Safeguards(Rect column, float y, UIColorPaletteDef palette)
        {
            y = HospitalParts.Heading(column, y, "SAFEGUARDS", palette);

            bool overdose = order.skipOnOverdose;

            if (UICheckboxControl.Draw(new Rect(column.x, y, column.width, 26f), ref overdose, palette,
                    "Skip a dose that would overdose them"))
                order.skipOnOverdose = overdose;

            y += 28f;

            bool addiction = order.holdOnAddiction;

            if (UICheckboxControl.Draw(new Rect(column.x, y, column.width, 26f), ref addiction, palette,
                    "Hold the order if they become addicted"))
                order.holdOnAddiction = addiction;

            y += 28f;

            y = HospitalParts.Note(column, y, SafeguardNote(), palette);

            return y + HospitalParts.BlockGap;
        }

        private string SafeguardNote()
        {
            return UIGuard.Try<string>("Hospital.SafeguardNote", () =>
            {
                if (order.drug == null)
                    return null;

                CompProperties_Drug props = order.drug.GetCompProperties<CompProperties_Drug>();

                if (props == null)
                    return null;

                if (props.chemical != null && props.chemical.addictionHediff != null)
                {
                    List<HediffStage> stages = props.chemical.addictionHediff.stages;

                    if (stages != null)
                    {
                        for (int i = 0; i < stages.Count; i++)
                        {
                            if (stages[i] == null || !stages[i].lifeThreatening)
                                continue;

                            return "Withdrawal from " + order.drug.label
                                   + " is fatal, so the addiction hold does not apply to it. That is the point "
                                   + "of the order rather than a failure of it, and a missed dose is an "
                                   + "emergency.";
                        }
                    }

                    return order.drug.LabelCap + " is addictive. At " + order.every
                           + (order.period == StandingOrderPeriod.Days ? " days" : " hours")
                           + " apart this patient will build tolerance.";
                }

                return props.CanCauseOverdose
                    ? order.drug.LabelCap + " can be overdosed on."
                    : order.drug.LabelCap + " is neither addictive nor overdosable.";
            }, null, null);
        }

        // ---------------------------------------------------------------------------------------
        // Right: only while
        // ---------------------------------------------------------------------------------------

        private void Gate(Rect rect, UIColorPaletteDef palette)
        {
            HospitalConditionGate gate = order.gate;

            float y = HospitalParts.Heading(rect, rect.y, "ONLY WHILE: " + gate.Summary.ToUpperInvariant(),
                palette);

            float half = Mathf.Floor((rect.width - 4f) / 2f);

            HospitalParts.Segment(new Rect(rect.x, y, half, 24f), "Always", gate.always, palette,
                () => gate.always = true);

            HospitalParts.Segment(new Rect(rect.x + half + 4f, y, rect.xMax - rect.x - half - 4f, 24f),
                "Any of these", !gate.always, palette, () => gate.always = false);

            y += 28f;

            if (gate.always)
            {
                HospitalParts.Note(rect, y,
                    "The clock alone decides. This is what penoxycyline and luciferium want: a dose on a "
                    + "schedule regardless of how the patient looks today.", palette);

                return;
            }

            ConditionSearch.Draw(new Rect(rect.x, y, rect.width, 26f), palette);

            y += 30f;

            Rect list = new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y - 34f));

            Conditions(list, gate, palette);

            HospitalParts.Note(rect, list.yMax + 4f,
                gate.Count == 0
                    ? "Nothing is ticked, so this order will never fire. Tick a condition, or set it to always."
                    : "While none of them is true the dose is skipped and the clock keeps running.", palette,
                GameFont.Tiny, gate.Count == 0 ? palette.Danger : palette.TextDisabled);
        }

        /// <summary>
        /// The grouped, searchable condition list.
        ///
        /// <b>The state group sits above the hediffs because the reason you reach for a painkiller is not a
        /// hediff.</b> Pain is a degree rather than a presence, so it carries its own threshold inside the row;
        /// bleeding, downed and in a medical bed are about the patient's situation rather than about anything
        /// named on their health tab.
        /// </summary>
        private void Conditions(Rect rect, HospitalConditionGate gate, UIColorPaletteDef palette)
        {
            Match();

            float height = 4f;

            // Measured before the scroll view opens, because the view rect has to be right the first frame: a
            // list whose height is discovered while drawing scrolls to the wrong place on the frame it changes.
            height += 26f + (Open("State") ? 4f * 26f : 0f);

            HospitalConditionGroup previous = HospitalConditionGroup.State;

            for (int i = 0; i < matches.Count; i++)
            {
                HospitalConditionGroup group = HospitalConditions.GroupOf(matches[i]);

                if (group != previous)
                {
                    height += 26f;
                    previous = group;
                }

                if (Open(HospitalConditions.LabelOf(group)))
                    height += 26f;
            }

            Rect view = new Rect(0f, 0f, rect.width - 18f, height);

            Widgets.BeginScrollView(rect, ref conditionScroll, view);

            float y = 0f;

            y = Drawer(view, y, "State", palette);

            if (Open("State"))
            {
                y = Pain(view, y, gate, palette);
                y = State(view, y, "Bleeding", "bleeding", gate.bleeding, palette, on => gate.bleeding = on);
                y = State(view, y, "Downed", "downed", gate.downed, palette, on => gate.downed = on);

                y = State(view, y, "In a medical bed", "bed", gate.inMedicalBed, palette,
                    on => gate.inMedicalBed = on);
            }

            previous = HospitalConditionGroup.State;

            for (int i = 0; i < matches.Count; i++)
            {
                HediffDef def = matches[i];
                HospitalConditionGroup group = HospitalConditions.GroupOf(def);

                if (group != previous)
                {
                    y = Drawer(view, y, HospitalConditions.LabelOf(group), palette);
                    previous = group;
                }

                if (!Open(HospitalConditions.LabelOf(group)))
                    continue;

                y = Condition(view, y, def, gate, palette);
            }

            Widgets.EndScrollView();
        }

        private void Match()
        {
            matches.Clear();

            List<HediffDef> all = HospitalConditions.Catalogue;

            for (int i = 0; i < all.Count; i++)
            {
                if (ConditionSearch.IsEmpty || ConditionSearch.Matches(all[i].label))
                    matches.Add(all[i]);
            }
        }

        private bool Open(string group)
        {
            // A search is a request to see what matched, so a fold is ignored while one is typed. The same rule
            // the animals tab applies to its sections.
            return !ConditionSearch.IsEmpty || !collapsed.Contains(group);
        }

        private float Drawer(Rect view, float y, string label, UIColorPaletteDef palette)
        {
            Rect row = new Rect(0f, y, view.width, 24f);

            bool open = Open(label);

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextSecondary;

                Widgets.Label(new Rect(row.x + 4f, row.y + 4f, row.width - 8f, 20f),
                    (open ? "v  " : ">  ") + label.ToUpperInvariant());
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }

            if (Widgets.ButtonInvisible(row) && ConditionSearch.IsEmpty)
            {
                if (!collapsed.Remove(label))
                    collapsed.Add(label);
            }

            return y + 26f;
        }

        /// <summary>
        /// Pain, which is the only condition with a degree rather than a presence.
        ///
        /// The threshold lives inside the row because it belongs to this condition alone: putting it anywhere else
        /// would make it look like a setting on the order.
        /// </summary>
        private float Pain(Rect view, float y, HospitalConditionGate gate, UIColorPaletteDef palette)
        {
            Rect row = new Rect(0f, y, view.width, 24f);

            bool on = gate.painAbove >= 0f;

            if (UICheckboxControl.Draw(new Rect(row.x, row.y, row.width - 130f, row.height), ref on, palette,
                    "In pain above"))
                gate.painAbove = on ? 0.3f : -1f;

            if (on)
            {
                float value = gate.painAbove;

                Rect slider = new Rect(row.xMax - 126f, row.y + 4f, 80f, 16f);

                value = Widgets.HorizontalSlider(slider, value, 0.05f, 0.95f);

                gate.painAbove = Mathf.Round(value * 20f) / 20f;

                Small(new Rect(row.xMax - CountWidth + 20f, row.y, CountWidth - 22f, row.height),
                    Mathf.RoundToInt(gate.painAbove * 100f) + "%", palette.TextSecondary);
            }
            else
            {
                Small(new Rect(row.xMax - CountWidth, row.y, CountWidth - 2f, row.height),
                    Count(HospitalConditions.CountInState(map, "pain", 0.3f)), palette.TextDisabled);
            }

            return y + 26f;
        }

        private float State(Rect view, float y, string label, string key, bool on, UIColorPaletteDef palette,
            Action<bool> set)
        {
            Rect row = new Rect(0f, y, view.width, 24f);

            bool value = on;

            if (UICheckboxControl.Draw(new Rect(row.x, row.y, row.width - CountWidth - 4f, row.height),
                    ref value, palette, label))
                set(value);

            int here = HospitalConditions.CountInState(map, key, 0f);

            Small(new Rect(row.xMax - CountWidth, row.y, CountWidth - 2f, row.height), Count(here),
                here > 0 ? palette.TextSecondary : palette.TextDisabled);

            return y + 26f;
        }

        private float Condition(Rect view, float y, HediffDef def, HospitalConditionGate gate,
            UIColorPaletteDef palette)
        {
            Rect row = new Rect(0f, y, view.width, 24f);

            bool on = gate.hediffs.Contains(def);

            if (UICheckboxControl.Draw(new Rect(row.x + 12f, row.y, row.width - CountWidth - 16f, row.height),
                    ref on, palette, def.LabelCap))
            {
                if (on)
                    gate.hediffs.Add(def);
                else
                    gate.hediffs.Remove(def);
            }

            int here = HospitalConditions.CountOnMap(map, def);

            Small(new Rect(row.xMax - CountWidth, row.y, CountWidth - 2f, row.height), Count(here),
                here > 0 ? palette.TextSecondary : palette.TextDisabled);

            return y + 26f;
        }

        private static string Count(int here)
        {
            return here > 0 ? here + " now" : "none";
        }

        /// <summary>A right-aligned tiny label with wrapping off, which is what the lane is too narrow for.</summary>
        private static void Small(Rect rect, string text, Color color)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                Text.WordWrap = false;
                GUI.color = color;

                Widgets.Label(rect, text);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private void Footer(Rect rect, UIColorPaletteDef palette)
        {
            bool paused = order.suspended;

            if (UICheckboxControl.Draw(new Rect(rect.x, rect.y, 160f, 30f), ref paused, palette, "Paused"))
                order.suspended = paused;

            if (HospitalParts.Button(new Rect(rect.xMax - 260f, rect.y, 120f, 30f), "Delete", palette))
            {
                MapComponent_StandingOrders component = MapComponent_StandingOrders.For(map);

                if (component != null)
                    component.Remove(order);

                Close();
            }

            if (HospitalParts.Button(new Rect(rect.xMax - 130f, rect.y, 130f, 30f), "Done", palette, true, true))
                Close();
        }
    }
}
