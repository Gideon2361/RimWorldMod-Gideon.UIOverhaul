using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.ColonyBar;
using Gideon.UIOverhaul.Features.Pawns;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// One animal's own card: how it is, what it knows, where it is allowed, and what is ordered on it.
    ///
    /// <b>Asked for on 2026-08-22:</b> everything about one animal, set from this tab, without going out to the
    /// animal on the map. Until now the tab could set a species and read an individual, which meant the last step
    /// of any real decision, this ewe rather than the flock, was made somewhere else. Vanilla is worse: an
    /// individual's area is on the Animals tab, its training is on the same tab in different columns, its medical
    /// care is on Assign, and its master is on Animals again, so four screens describe one animal.
    ///
    /// <b>The species card is not replaced, it is switched to.</b> Clicking an individual inside an opened species
    /// swaps this in beside the list; the chip at the top goes back. Both cards are the same width and use the same
    /// parts, so the swap reads as turning a page rather than as a different panel.
    ///
    /// <b>Every write goes through the same helper the rest of the mod uses.</b> The area through
    /// <see cref="PawnAreas"/>, which is the pawns tab's; the master through <see cref="Dialog_PickMaster"/>, which
    /// is the animal row's; medical care through <see cref="GroupActions"/>, which is the colonist bar's; training
    /// through <see cref="AnimalTraining"/>, which is vanilla's own recursive setter. Nothing here is a second
    /// opinion about what any of those mean.
    /// </summary>
    internal static class AnimalCardPane
    {
        private const float Pad = 10f;

        private static Vector2 scroll;

        /// <summary>Where the last draw ended. Measured rather than predicted, for the reason the species pane gives.</summary>
        private static float contentHeight = 600f;

        /// <summary>
        /// Draws the card. Returns false when this animal is no longer worth describing, which sends the pane back
        /// to the species it came from.
        /// </summary>
        internal static bool Draw(Rect rect, Pawn animal, AnimalGroup group, UIColorPaletteDef palette,
            Action changed, Action showSpecies)
        {
            if (animal == null || animal.Destroyed || animal.Dead)
                return false;

            return UIGuard.Try("Animals.Card", () =>
            {
                Widgets.DrawBoxSolid(rect, palette.PanelBackground);

                GUI.color = palette.Border;

                Widgets.DrawBox(rect, 1);

                GUI.color = Color.white;

                Contents(rect.ContractedBy(Pad), animal, group, palette, changed, showSpecies);

                return true;
            }, false, "This animal's card could not be drawn. The list beside it is unaffected.");
        }

        private static void Contents(Rect inner, Pawn animal, AnimalGroup group, UIColorPaletteDef palette,
            Action changed, Action showSpecies)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                float y = Back(inner, animal, palette, showSpecies);

                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextPrimary;

                Widgets.LabelEllipses(new Rect(inner.x, y, inner.width, 30f), animal.LabelShortCap);

                y += 30f;

                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                Widgets.LabelEllipses(new Rect(inner.x, y, inner.width, UIFonts.LineHeightOf(GameFont.Tiny)),
                    Descriptor(animal, group));

                y += UIFonts.LineHeightOf(GameFont.Tiny) + 6f;

                Rect body = new Rect(inner.x, y, inner.width, Mathf.Max(0f, inner.yMax - y));
                Rect view = new Rect(0f, 0f, body.width - 18f, contentHeight);

                Widgets.BeginScrollView(body, ref scroll, view);

                float at = 0f;

                at = Condition(view, at, animal, palette);
                at = Training(view, at, animal, group, palette, changed);
                at = Assignment(view, at, animal, palette, changed);
                at = Orders(view, at, animal, group, palette, changed);
                at = Actions(view, at, animal, palette);

                Widgets.EndScrollView();

                contentHeight = at;
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// The way back to the species.
        ///
        /// <b>A chip above the name rather than a close button,</b> because closing is not what somebody wants when
        /// they have finished with one animal: they want the herd they were looking at. The species is named on it,
        /// so it says where back goes.
        /// </summary>
        private static float Back(Rect inner, Pawn animal, UIColorPaletteDef palette, Action showSpecies)
        {
            Rect chip = new Rect(inner.x, inner.y, inner.width, 22f);
            bool over = Mouse.IsOver(chip);

            UIElementPainter.OutlineRounded(chip, over ? palette.Accent : palette.Border,
                over ? palette.SurfaceRaised : palette.PanelBackground);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = over ? palette.TextPrimary : palette.TextSecondary;

                if (TexButton.Collapse != null)
                {
                    GUI.DrawTexture(new Rect(chip.x + 6f, chip.center.y - 5f, 10f, 10f), TexButton.Collapse);
                }

                Widgets.LabelEllipses(new Rect(chip.x + 20f, chip.y, chip.width - 26f, chip.height),
                    "All " + animal.def.LabelCap);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (Widgets.ButtonInvisible(chip))
            {
                showSpecies?.Invoke();

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            return chip.yMax + 6f;
        }

        private static string Descriptor(Pawn animal, AnimalGroup group)
        {
            string gender = animal.gender == Gender.Female ? "female" : animal.gender == Gender.Male
                ? "male"
                : "genderless";

            string age = AnimalFacts.Juvenile(animal)
                ? "juvenile"
                : (animal.ageTracker?.AgeBiologicalYearsFloat ?? 0f).ToString("0.#") + " years";

            string kind = group != null && group.Kind == AnimalKind.Wild ? "wild" : "colony";

            return animal.def.LabelCap + ", " + gender + ", " + age + ", " + kind;
        }

        // ---------------------------------------------------------------------------------------
        // Condition
        // ---------------------------------------------------------------------------------------

        private static float Condition(Rect view, float y, Pawn animal, UIColorPaletteDef palette)
        {
            y = AnimalPaneParts.Heading(view, y, "CONDITION", palette);

            float health = animal.health?.summaryHealth?.SummaryHealthPercent ?? 1f;

            y = AnimalPaneParts.Pair(view, y, "Health", health.ToStringPercent(),
                health < 0.5f ? palette.Danger : health < 0.95f ? palette.Warning : palette.TextPrimary, palette);

            if (HealthAIUtility.ShouldBeTendedNowByPlayer(animal))
                y = AnimalPaneParts.Pair(view, y, "Wounds", "need tending", palette.Danger, palette);

            Need food = animal.needs?.food;

            if (food != null)
            {
                y = AnimalPaneParts.Pair(view, y, "Food", food.CurLevelPercentage.ToStringPercent(),
                    food.CurLevelPercentage < 0.15f ? palette.Danger : palette.TextPrimary, palette);
            }

            Need rest = animal.needs?.rest;

            if (rest != null)
            {
                y = AnimalPaneParts.Pair(view, y, "Rest", rest.CurLevelPercentage.ToStringPercent(),
                    palette.TextPrimary, palette);
            }

            AnimalPregnancy pregnancy = AnimalFacts.Pregnancy(animal);

            if (pregnancy.Pregnant)
            {
                y = AnimalPaneParts.Pair(view, y, "Pregnant", Days(pregnancy.DaysLeft) + " left",
                    palette.TextPrimary, palette);
            }

            AnimalProduce produce = AnimalFacts.Produce(animal);

            if (produce.Any)
            {
                y = AnimalPaneParts.Pair(view, y, produce.ResourceLabel,
                    produce.Ready ? "ready" : Days(produce.DaysLeft) + " away",
                    produce.Ready ? palette.Success : palette.TextPrimary, palette);
            }

            Pawn bond = BondedTo(animal);

            if (bond != null)
                y = AnimalPaneParts.Pair(view, y, "Bonded to", bond.LabelShortCap, palette.Mood, palette);

            return y + 6f;
        }

        /// <summary>
        /// Who this animal is bonded to, if anybody.
        ///
        /// Worth a line of its own on a card that can order a slaughter: killing a bonded animal is a mood hit on
        /// the colonist who bonded with it, and that colonist is not otherwise named anywhere near the button.
        /// </summary>
        private static Pawn BondedTo(Pawn animal)
        {
            List<DirectPawnRelation> relations = animal.relations?.DirectRelations;

            if (relations == null)
                return null;

            for (int i = 0; i < relations.Count; i++)
            {
                if (relations[i]?.def == PawnRelationDefOf.Bond)
                    return relations[i].otherPawn;
            }

            return null;
        }

        // ---------------------------------------------------------------------------------------
        // Training
        // ---------------------------------------------------------------------------------------

        private static float Training(Rect view, float y, Pawn animal, AnimalGroup group,
            UIColorPaletteDef palette, Action changed)
        {
            if (group != null && group.Kind == AnimalKind.Wild)
                return y;

            List<TrainableDef> kinds = AnimalTrainingBoxes.KindsFor(animal);

            if (kinds.Count == 0)
                return y;

            // Copied out, because the reads inside the draw use the same scratch list.
            TrainableDef[] set = kinds.ToArray();

            y = AnimalPaneParts.Heading(view, y, "TRAINING", palette);

            for (int i = 0; i < set.Length; i++)
            {
                Rect row = new Rect(view.x, y, view.width, AnimalTrainingBoxes.PillHeight + 4f);

                AnimalTrainingBoxes.DrawForAnimalKind(row, animal, set[i], palette, changed);

                y = row.yMax + 2f;
            }

            AnimalTrainingState training = AnimalTraining.Of(animal);

            if (training.Decaying && training.AnythingAtRisk)
            {
                y = AnimalPaneParts.Pair(view, y + 4f, "Forgets something in", Days(training.DecayDaysLeft),
                    palette.Danger, palette);
            }

            return y + 6f;
        }

        // ---------------------------------------------------------------------------------------
        // Assignment
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Where this animal is allowed, who it answers to, and how it is treated when hurt.
        ///
        /// <b>Only for the colony's own.</b> A wild animal has no allowed area, no master and no medical care until
        /// it is tamed, so the section is absent rather than drawn full of dead chips.
        /// </summary>
        private static float Assignment(Rect view, float y, Pawn animal, UIColorPaletteDef palette, Action changed)
        {
            if (animal.playerSettings == null || animal.Faction != Faction.OfPlayer)
                return y;

            y = AnimalPaneParts.Heading(view, y, "ASSIGNMENT", palette);

            y = AnimalPaneParts.Chip(view, y, "Area", PawnAreas.Label(animal), palette,
                () => PawnAreas.Choose(animal, changed), PawnAreas.Assignable(animal) ? null : PawnAreas.Reason(animal));

            Pawn master = animal.playerSettings.Master;

            y = AnimalPaneParts.Chip(view, y, "Master", master == null ? "none" : master.LabelShortCap.ToString(),
                palette, () => Dialog_PickMaster.For(animal, changed));

            y = AnimalPaneParts.Chip(view, y, "Medical care", animal.playerSettings.medCare.GetLabel(), palette,
                () => GroupActions.Care(One(animal), changed));

            // Following is only meaningful with a master, and vanilla's own columns are drawn dead without one
            // rather than hidden, which is the same choice: the setting exists, it just has nothing to follow.
            bool canFollow = master != null;

            y = Toggle(view, y, "Follows when drafted", animal.playerSettings.followDrafted, canFollow, palette,
                value =>
                {
                    animal.playerSettings.followDrafted = value;

                    changed?.Invoke();
                });

            y = Toggle(view, y, "Follows to field work", animal.playerSettings.followFieldwork, canFollow, palette,
                value =>
                {
                    animal.playerSettings.followFieldwork = value;

                    changed?.Invoke();
                });

            // The pen is the one assignment that genuinely is not an individual's to make: a pen states which
            // species it accepts and the ropers take whoever qualifies, so there is no per animal answer to give.
            // Said rather than left out, because its absence next to Area would otherwise read as an oversight.
            if (animal.Roamer)
            {
                y = AnimalPaneParts.Chip(view, y, "Pen", string.Empty, palette, null,
                    "set for the whole species");
            }

            y = Chore(view, y, animal, TrainableDefOf.Forage, "Allowed to forage", palette, changed,
                () => animal.playerSettings.animalForage, value => animal.playerSettings.animalForage = value);

            y = Chore(view, y, animal, TrainableDefOf.Dig, "Allowed to dig", palette, changed,
                () => animal.playerSettings.animalDig, value => animal.playerSettings.animalDig = value);

            return y + 6f;
        }

        /// <summary>
        /// One of Odyssey's trained chores, shown only once this animal has actually learned it.
        ///
        /// The same gate the species menu puts on the two rows these match, and for the same reason: without it
        /// every chicken's card would carry two toggles that do nothing.
        /// </summary>
        private static float Chore(Rect view, float y, Pawn animal, TrainableDef skill, string label,
            UIColorPaletteDef palette, Action changed, Func<bool> read, Action<bool> write)
        {
            if (skill == null || animal.training == null || !animal.training.HasLearned(skill))
                return y;

            return Toggle(view, y, label, read(), true, palette, value =>
            {
                write(value);

                changed?.Invoke();
            });
        }

        // ---------------------------------------------------------------------------------------
        // Orders
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The designations on this one animal: slaughter and release for the colony's own, hunt and tame for
        /// wildlife.
        ///
        /// <b>The tame odds are on the card rather than in a tooltip,</b> because they are the whole decision for a
        /// wild animal: a 2% chance with a manhunter risk on failure is a different proposition from a 60% one, and
        /// the number is not otherwise anywhere on this tab for one animal.
        /// </summary>
        private static float Orders(Rect view, float y, Pawn animal, AnimalGroup group, UIColorPaletteDef palette,
            Action changed)
        {
            bool wild = group == null ? animal.Faction != Faction.OfPlayer : group.Kind == AnimalKind.Wild;

            y = AnimalPaneParts.Heading(view, y, "ORDERS", palette);

            if (wild)
            {
                AnimalTameOdds odds = AnimalFacts.TameOdds(animal);

                y = Designation(view, y, "Tame it", animal, DesignationDefOf.Tame,
                    AnimalDesignations.CanTame(animal), palette, changed);

                y = Designation(view, y, "Hunt it", animal, DesignationDefOf.Hunt,
                    AnimalDesignations.CanHunt(animal), palette, changed);

                if (odds.Known)
                {
                    y = AnimalPaneParts.Pair(view, y + 4f, "Tame chance", odds.Chance.ToStringPercent(),
                        odds.Chance < 0.1f ? palette.Warning : palette.TextPrimary, palette);

                    if (odds.Handler != null)
                    {
                        y = AnimalPaneParts.Pair(view, y, "Best handler",
                            odds.Handler.LabelShortCap + " at " + odds.HandlerSkill,
                            odds.AnyoneSkilledEnough ? palette.TextPrimary : palette.Danger, palette);
                    }
                }

                float manhunter = AnimalFacts.ManhunterOnTameFail(animal);

                if (manhunter > 0f)
                {
                    y = AnimalPaneParts.Pair(view, y, "Turns manhunter if it fails",
                        manhunter.ToStringPercent(), palette.Danger, palette);
                }

                return y + 6f;
            }

            y = Designation(view, y, "Slaughter it", animal, DesignationDefOf.Slaughter, true, palette, changed);

            y = Designation(view, y, "Release to the wild", animal, DesignationDefOf.ReleaseAnimalToWild, true,
                palette, changed);

            Pawn bond = BondedTo(animal);

            if (bond != null && AnimalDesignations.Ordered(animal, DesignationDefOf.Slaughter))
            {
                y = AnimalPaneParts.Pair(view, y + 4f, "Bonded", bond.LabelShortCap + " will grieve",
                    palette.Danger, palette);
            }

            return y + 6f;
        }

        // ---------------------------------------------------------------------------------------
        // Actions
        // ---------------------------------------------------------------------------------------

        private static float Actions(Rect view, float y, Pawn animal, UIColorPaletteDef palette)
        {
            y = AnimalPaneParts.Heading(view, y, "THIS ANIMAL", palette);

            Rect button = new Rect(view.x, y, view.width, 28f);

            if (UIActionButtonControl.Draw(button, "Show me on the map", palette))
                PawnCameraJump.Request(animal);

            return button.yMax + 6f;
        }

        // ---------------------------------------------------------------------------------------
        // Parts
        // ---------------------------------------------------------------------------------------

        /// <summary>One designation as a checkbox, worded as the order rather than as the state.</summary>
        private static float Designation(Rect view, float y, string label, Pawn animal, DesignationDef what,
            bool allowed, UIColorPaletteDef palette, Action changed)
        {
            bool ordered = AnimalDesignations.Ordered(animal, what);

            return Toggle(view, y, label, ordered, allowed, palette, value =>
            {
                AnimalDesignations.Toggle(animal, what, value);

                changed?.Invoke();
            });
        }

        /// <summary>
        /// A checkbox row.
        ///
        /// Through the framework's own checkbox, which is the switch the rest of this mod uses, so a setting on
        /// this card looks like a setting everywhere else.
        /// </summary>
        private static float Toggle(Rect view, float y, string label, bool value, bool enabled,
            UIColorPaletteDef palette, Action<bool> write)
        {
            Rect row = new Rect(view.x, y, view.width, 24f);
            bool held = value;

            if (UICheckboxControl.Draw(row, ref held, palette, label, null, UICheckboxSide.Left, !enabled)
                && enabled)
            {
                write(held);

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            return row.yMax + AnimalPaneParts.RowGap;
        }

        /// <summary>Scratch for the helpers that take a group. One animal is still a list to them.</summary>
        private static readonly List<Pawn> Single = new List<Pawn>();

        private static List<Pawn> One(Pawn animal)
        {
            Single.Clear();
            Single.Add(animal);

            return Single;
        }

        private static string Days(float days)
        {
            if (days < 0f)
                return "never";

            return days < 1f ? Mathf.RoundToInt(days * 24f) + "h" : days.ToString("0.#") + "d";
        }
    }
}
