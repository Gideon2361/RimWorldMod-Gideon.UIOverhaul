using System;
using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Integrations;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// The four standing orders a colonist carries around with them: what to wear, what to eat, which drugs to
    /// take, and what to read.
    ///
    /// <b>Here because this is where the person is.</b> Vanilla files these in the Assign tab, so deciding that
    /// the colonist who keeps eating the fine meals should be on a different food policy costs a tab switch and
    /// finding the same person a second time. They sit under the schedule for the reason the schedule sits under
    /// the row: they answer questions about one pawn, and the row is already about that pawn.
    ///
    /// <b>Only what the pawn actually has is drawn.</b> An animal has no apparel policy and cannot read. Each
    /// tracker answers for itself by being absent, so no list of which pawn kinds get which policy is kept here
    /// to go stale. Whichever pickers remain share the width, so a row with two of them does not leave two gaps
    /// where the others would have been.
    ///
    /// <b>Nothing here decides what a policy means.</b> The lists come from RimWorld's own databases and Manage
    /// opens RimWorld's own dialog, so a policy added by a mod appears without this knowing anything about it.
    /// </summary>
    internal static class PolicyStrip
    {
        private const float CellHeight = 24f;
        private const float TopPad = 4f;
        private const float BottomPad = 6f;
        private const float Gap = 6f;

        /// <summary>Room either side of a chip's text.</summary>
        private const float ChipPadding = 20f;

        /// <summary>The caret's lane inside a chip.</summary>
        private const float CaretWidth = 14f;

        /// <summary>How much taller an expanded row has to be to fit this.</summary>
        internal const float Height = TopPad + CellHeight + BottomPad;

        /// <summary>
        /// The height this pawn's strip actually needs, which is nothing when they have no policies.
        ///
        /// <b>Asked before the row is sized, not only while it is drawn.</b> A guest whose food is the colony's
        /// business rather than their own, or an animal, has no pickers at all; charging their row for a band
        /// that then draws nothing would open it onto a strip of empty background, which reads as a bug rather
        /// than as an absence.
        /// </summary>
        internal static float HeightFor(Pawn pawn)
        {
            return pawn != null && Slots(pawn).Count > 0 ? Height : 0f;
        }

        /// <summary>Below this a chip cannot say anything useful, so it is left out of the line.</summary>
        private const float MinimumChipWidth = 74f;

        /// <summary>One picker: what it is called, what it currently says, and what opens its menu.</summary>
        private struct Slot
        {
            internal string Caption;
            internal string Label;
            internal Action Open;
        }

        /// <summary>
        /// The standing orders as chips, sized to what they say.
        ///
        /// <b>Chips rather than five equal buttons, from the mockup Aaron approved on 2026-08-22.</b> The old
        /// arrangement split the row's whole width into five columns whatever the words were, so "Food: Lavish"
        /// got the same 200 pixels as "Drugs: Social drugs only" and the strip read as furniture. A chip is as
        /// wide as its own text, which puts the five of them in about half the width and lets the eye read them as
        /// a sentence about the pawn rather than as a toolbar.
        ///
        /// <b>What does not fit is dropped from the right, not squeezed.</b> A chip narrower than its own name is
        /// a chip that says nothing, so on a tab dragged narrow the last ones are left out and the ones that
        /// remain are still readable.
        /// </summary>
        internal static void Draw(Rect rect, Pawn pawn, UIColorPaletteDef palette)
        {
            if (pawn == null)
                return;

            List<Slot> slots = Slots(pawn);

            if (slots.Count == 0)
                return;

            GameFont previousFont = Text.Font;

            Text.Font = GameFont.Tiny;

            float x = rect.x;

            for (int i = 0; i < slots.Count; i++)
            {
                float width = Mathf.Max(MinimumChipWidth, WidthOf(slots[i]));

                if (x + width > rect.xMax)
                    break;

                DrawChip(new Rect(x, rect.y + TopPad, width, CellHeight), slots[i], palette);

                x += width + Gap;
            }

            Text.Font = previousFont;
        }

        /// <summary>
        /// How wide a chip wants to be: its caption, its value, the caret and the padding around them.
        ///
        /// Measured at Tiny, which is what <see cref="Draw"/> has set before it asks. Measuring under a different
        /// font than the one that draws is how a label ends up ellipsised inside a chip sized for it.
        /// </summary>
        private static float WidthOf(Slot slot)
        {
            return Text.CalcSize(Caption(slot)).x + ChipPadding + CaretWidth;
        }

        private static string Caption(Slot slot)
        {
            return slot.Label.NullOrEmpty() ? slot.Caption : slot.Caption + ": " + slot.Label;
        }

        /// <summary>
        /// Which pickers this pawn gets.
        ///
        /// Ordered as vanilla's Assign tab orders them, so somebody who knows where food sits relative to drugs
        /// finds them in the same order here.
        /// </summary>
        private static List<Slot> Slots(Pawn pawn)
        {
            List<Slot> slots = new List<Slot>();

            if (pawn.outfits != null)
            {
                slots.Add(new Slot
                {
                    Caption = "APPAREL",
                    Label = NameOf(pawn.outfits.CurrentApparelPolicy),
                    Open = () => Menu(
                        Widen(Current.Game?.outfitDatabase?.AllOutfits),
                        chosen => pawn.outfits.CurrentApparelPolicy = (ApparelPolicy) chosen,
                        () => new Dialog_ManageApparelPolicies(pawn.outfits.CurrentApparelPolicy))
                });
            }

            // Configurable is vanilla's own test, and it is not the same question as whether a tracker exists:
            // it also rules out animals and anybody who is not in the colony's care. Asking it rather than
            // reproducing it means a pawn stops being editable here at the moment they stop being editable in
            // vanilla's own tab.
            if (pawn.foodRestriction != null && pawn.foodRestriction.Configurable)
            {
                slots.Add(new Slot
                {
                    Caption = "FOOD",
                    Label = NameOf(pawn.foodRestriction.CurrentFoodPolicy),
                    Open = () => Menu(
                        Widen(Current.Game?.foodRestrictionDatabase?.AllFoodRestrictions),
                        chosen => pawn.foodRestriction.CurrentFoodPolicy = (FoodPolicy) chosen,
                        () => new Dialog_ManageFoodPolicies(pawn.foodRestriction.CurrentFoodPolicy))
                });
            }

            // Taming food, from nercury's Assign Animal Food, and only when that mod is installed. Next to food
            // because it is a food policy and the mod's own table groups it with the others; before drugs so the
            // vanilla three stay in vanilla's order relative to each other.
            //
            // The picker offers RimWorld's own food policies and opens RimWorld's own manager, exactly as the FOOD
            // slot above does. Only where the choice is stored differs, and that is the integration's business.
            if (AssignAnimalFoodIntegration.Applies(pawn))
            {
                slots.Add(new Slot
                {
                    Caption = "TAMING FOOD",
                    Label = NameOf(AssignAnimalFoodIntegration.Current(pawn)),
                    Open = () => Menu(
                        Widen(Current.Game?.foodRestrictionDatabase?.AllFoodRestrictions),
                        chosen => AssignAnimalFoodIntegration.Set(pawn, (FoodPolicy) chosen),
                        () => new Dialog_ManageFoodPolicies(AssignAnimalFoodIntegration.Current(pawn)))
                });
            }

            if (pawn.drugs != null)
            {
                slots.Add(new Slot
                {
                    Caption = "DRUGS",
                    Label = NameOf(pawn.drugs.CurrentPolicy),
                    Open = () => Menu(
                        Widen(Current.Game?.drugPolicyDatabase?.AllPolicies),
                        chosen => pawn.drugs.CurrentPolicy = (DrugPolicy) chosen,
                        () => new Dialog_ManageDrugPolicies(pawn.drugs.CurrentPolicy))
                });
            }

            if (pawn.reading != null)
            {
                slots.Add(new Slot
                {
                    Caption = "READING",
                    Label = NameOf(pawn.reading.CurrentPolicy),
                    Open = () => Menu(
                        Widen(Current.Game?.readingPolicyDatabase?.AllReadingPolicies),
                        chosen => pawn.reading.CurrentPolicy = (ReadingPolicy) chosen,
                        () => new Dialog_ManageReadingPolicies(pawn.reading.CurrentPolicy))
                });
            }

            return slots;
        }

        /// <summary>
        /// Widens a database's own list to the shared base type.
        ///
        /// The four databases each return their own concrete list and nothing else about the four differs, so
        /// this is what lets one menu builder serve all of them instead of four near-identical copies.
        /// </summary>
        private static List<Policy> Widen<T>(List<T> all) where T : Policy
        {
            List<Policy> widened = new List<Policy>();

            if (all == null)
                return widened;

            foreach (T policy in all)
            {
                if (policy != null)
                    widened.Add(policy);
            }

            return widened;
        }

        private static string NameOf(Policy policy)
        {
            return policy == null || policy.label.NullOrEmpty() ? "None" : policy.label;
        }

        /// <summary>
        /// One picker: a caption, and a button that reads as a drop-down rather than as a command.
        /// </summary>
        /// <summary>
        /// One chip: an outline, the caption in dim text, the value in bright text, and a caret.
        ///
        /// Outlined rather than filled, so five of them in a line read as labels with values in them rather than
        /// as five buttons competing with the work grid underneath. The hover fill is what says they are clickable
        /// at the moment somebody is about to click.
        /// </summary>
        private static void DrawChip(Rect rect, Slot slot, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect);

            UIElementPainter.OutlineRounded(rect, over ? palette.Accent : palette.Border,
                over ? palette.SurfaceRaised : palette.PanelBackground);

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleLeft;

                float x = rect.x + 8f;
                float room = Mathf.Max(0f, rect.xMax - CaretWidth - x);

                // The caption and the value are drawn as two runs so the value can be the brighter of the two:
                // "Food" is the label of the setting and "Lavish meals" is the answer, and the answer is what
                // somebody scanning the row is looking for.
                GUI.color = palette.TextDisabled;

                string caption = slot.Caption + (slot.Label.NullOrEmpty() ? string.Empty : ": ");
                float captionWidth = Mathf.Min(room, Text.CalcSize(caption).x);

                Widgets.Label(new Rect(x, rect.y, captionWidth, rect.height), caption);

                GUI.color = palette.TextPrimary;

                Rect value = new Rect(x + captionWidth, rect.y, Mathf.Max(0f, room - captionWidth), rect.height);

                if (value.width >= 12f && !slot.Label.NullOrEmpty())
                    Widgets.LabelEllipses(value, slot.Label);

                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = over ? palette.Accent : palette.TextDisabled;

                Widgets.Label(new Rect(rect.x, rect.y, Mathf.Max(0f, rect.width - 6f), rect.height), "▾");
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }

            if (Widgets.ButtonInvisible(rect))
                slot.Open();
        }

        /// <summary>
        /// The menu of policies, with RimWorld's own manager on the end.
        ///
        /// <b>Guarded rather than trusted.</b> These run from the float menu's own OnGUI, which is outside
        /// whatever guarded panel drew the row, so an exception here would reach RimWorld itself rather than
        /// being caught and reported.
        /// </summary>
        private static void Menu(List<Policy> all, Action<Policy> apply, Func<Window> manage)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (Policy policy in all)
            {
                Policy captured = policy;

                options.Add(new FloatMenuOption(NameOf(captured),
                    UIGuard.Wrap("Pawns.SetPolicy", () => apply(captured))));
            }

            // Always offered, even when the list above is empty: a colony that has somehow lost every policy of
            // one kind is exactly the case where reaching the manager matters most.
            options.Add(new FloatMenuOption("Manage policies...",
                UIGuard.Wrap("Pawns.ManagePolicies", () => Find.WindowStack.Add(manage()))));

            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
