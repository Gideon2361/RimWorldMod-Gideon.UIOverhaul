using System;
using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
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
        private const float CaptionHeight = 14f;
        private const float TopPad = 4f;
        private const float BottomPad = 6f;
        private const float Gap = 8f;

        /// <summary>How much taller an expanded row has to be to fit this.</summary>
        internal const float Height = TopPad + CaptionHeight + CellHeight + BottomPad;

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

        /// <summary>Below this there is not enough width for a policy name, so nothing is drawn.</summary>
        private const float MinimumPickerWidth = 90f;

        /// <summary>One picker: what it is called, what it currently says, and what opens its menu.</summary>
        private struct Slot
        {
            internal string Caption;
            internal string Label;
            internal Action Open;
        }

        internal static void Draw(Rect rect, Pawn pawn, UIColorPaletteDef palette)
        {
            if (pawn == null)
                return;

            List<Slot> slots = Slots(pawn);

            if (slots.Count == 0)
                return;

            float width = (rect.width - Gap * (slots.Count - 1)) / slots.Count;

            // A picker too narrow to show a policy name is worse than no picker: it reads as a row of broken
            // buttons. The strip is left empty until the tab is wide enough to say something.
            if (width < MinimumPickerWidth)
                return;

            for (int i = 0; i < slots.Count; i++)
            {
                Rect cell = new Rect(rect.x + i * (width + Gap), rect.y + TopPad, width,
                    CaptionHeight + CellHeight);

                DrawSlot(cell, slots[i], palette);
            }
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
        private static void DrawSlot(Rect rect, Slot slot, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextDisabled;

            // Measured rather than given the constant, because Widgets.Label clips instead of overflowing and
            // Tiny renders taller than a 14 pixel row at some UI scales.
            Rect caption = new Rect(rect.x + 2f, rect.y, Mathf.Max(0f, rect.width - 2f),
                Mathf.Max(CaptionHeight, UIFonts.LineHeightOf(GameFont.Tiny)));

            if (caption.width >= 24f)
                Widgets.LabelEllipses(caption, slot.Caption);

            Rect button = new Rect(rect.x, rect.y + CaptionHeight, rect.width, CellHeight);
            bool over = Mouse.IsOver(button);

            UIElementPainter.PaintButton(button, palette, over, over && Input.GetMouseButton(0));

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextPrimary;

            // The caret's room is taken out of the label rather than drawn over it, so a long policy name ends
            // in an ellipsis instead of running underneath the arrow.
            Rect label = new Rect(button.x + 7f, button.y, Mathf.Max(0f, button.width - 24f), button.height);

            if (label.width >= 24f)
                Widgets.LabelEllipses(label, slot.Label);

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = palette.TextDisabled;
            Widgets.Label(new Rect(button.x, button.y, Mathf.Max(0f, button.width - 7f), button.height), "▾");

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (Widgets.ButtonInvisible(button))
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
