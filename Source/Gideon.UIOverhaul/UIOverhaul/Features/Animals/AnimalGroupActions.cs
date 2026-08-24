using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.ColonyBar;
using Gideon.UIOverhaul.Features.Pawns;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// Everything that can be set on every animal of one species at once, and how each of those settings currently
    /// reads.
    ///
    /// <b>This was a float menu until 2026-08-22 and is now the panel's Species settings section.</b> Aaron asked
    /// for the move, and the menu deserved to go: it hid nine settings behind a button, showed one at a time, and
    /// answered "what is this set to" only after two clicks. The same nine drawn as chips and switches say what
    /// they are set to without being touched, which is what a settings panel is for. The menu building is deleted
    /// rather than left unreachable, so there is one way to set a species and no second implementation to drift.
    ///
    /// <b>What is left of the float menu, and why.</b> Three of these open one: the allowed area, because
    /// <c>AreaUtility</c> draws the area's outline on the map while an entry is hovered and appends Manage areas,
    /// and reimplementing that would mean reimplementing the highlight; the pen, because it is a list of buildings
    /// whose length nobody can predict; and medical care, because it is RimWorld's own five-level enum. Each is
    /// reached from a chip that already says the current answer, which is the shape the individual animal's card
    /// uses too, so the two cards behave alike.
    ///
    /// <b>Composed from the colonist bar's group helpers where the two overlap, and only where they overlap.</b>
    /// Medical care and select all are the same operations on an animal as on a colonist. Apparel, drugs, reading
    /// and food are not: vanilla makes all four humanlike only, so offering them here would be four controls that
    /// quietly do nothing.
    ///
    /// <b>Every write is per animal, with the same test vanilla's own column makes,</b> so a group of seven where
    /// two are ineligible changes five and leaves two alone rather than refusing or pretending.
    ///
    /// <b>Nothing here confirms anything twice.</b> Slaughtering another faction's animal asks first, because
    /// vanilla asks first and the reason is diplomatic rather than cosmetic. A group of our own animals does not
    /// ask, for the same reason a drag over them does not.
    /// </summary>
    internal static class AnimalGroupActions
    {
        // ------------------------------------------------------------------ allowed area

        /// <summary>
        /// What the area chip says: the shared area, "mixed", or null when nothing in the group can take one.
        ///
        /// <b>One map rather than several.</b> The colonist bar's version of this is offered per map because a
        /// group of colonists can straddle two; a species group is built per place by the roster, so it has one
        /// map and one answer.
        /// </summary>
        internal static string AreaLabel(AnimalGroup group)
        {
            return GroupActions.Shared(group.Members, p =>
                !PawnAreas.Assignable(p) ? null : PawnAreas.Label(p));
        }

        /// <summary>Whether anything in the group can be given an area at all.</summary>
        internal static bool AreaAssignable(AnimalGroup group)
        {
            List<Pawn> members = group.Members;

            for (int i = 0; i < members.Count; i++)
            {
                if (PawnAreas.Assignable(members[i]))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Why no area can be set, for the chip to say in place of a value.
        ///
        /// Read off the first member rather than composed, because within one species the answer is the same for
        /// all of them: it is the race that decides, and the reason names the race's own situation.
        /// </summary>
        internal static string AreaReason(AnimalGroup group)
        {
            return group.Members.Count == 0 ? null : PawnAreas.Reason(group.Members[0]);
        }

        internal static void ChooseArea(AnimalGroup group, Action changed)
        {
            UIGuard.Try("Animals.GroupArea", () =>
            {
                Map map = group.Map;

                if (map?.areaManager == null)
                    return;

                List<Pawn> members = new List<Pawn>(group.Members);

                AreaUtility.MakeAllowedAreaListFloatMenu(area => UIGuard.Try("Animals.SetGroupArea", () =>
                {
                    for (int i = 0; i < members.Count; i++)
                    {
                        // Asked per animal rather than once for the group: vanilla's own column makes the same
                        // test, and inside one species a juvenile or a caravan member can differ.
                        if (PawnAreas.Assignable(members[i]))
                            members[i].playerSettings.AreaRestrictionInPawnCurrentMap = area;
                    }

                    changed?.Invoke();
                }, "The allowed area was not changed."), true, true, map);
            }, "The area list could not be built, so nothing was changed.");
        }

        // ------------------------------------------------------------------ medical care

        internal static string CareLabel(AnimalGroup group)
        {
            return GroupActions.CareLabel(group.Members);
        }

        internal static void ChooseCare(AnimalGroup group, Action changed)
        {
            GroupActions.Care(group.Members, changed);
        }

        // ------------------------------------------------------------------ master

        internal static string MasterLabel(AnimalGroup group)
        {
            return GroupActions.Shared(group.Members, p =>
            {
                if (p?.playerSettings == null || !CanHaveMaster(p))
                    return null;

                Pawn master = p.playerSettings.Master;

                return master == null ? "none" : master.LabelShortCap.ToString();
            });
        }

        /// <summary>
        /// Whether a master is a thing this animal can have at all.
        ///
        /// <b>Not <c>CanBeTrained</c>, which this asked until 2026-08-22.</b> That method answers "is there
        /// obedience training left to do", so it says no to an animal that has finished it: a fully obedient herd
        /// reported nothing at all and the readout vanished at exactly the point every one of them had a master
        /// worth reading. The same mistake, in the same words, cost the training pills their eligibility test
        /// earlier the same day.
        ///
        /// Learned counts, and so does eligible but unlearned, since a master can be lined up before the training
        /// finishes.
        /// </summary>
        private static bool CanHaveMaster(Pawn animal)
        {
            if (animal?.training == null)
                return false;

            if (animal.training.HasLearned(TrainableDefOf.Obedience))
                return true;

            bool visible;
            AcceptanceReport report = animal.training.CanAssignToTrain(TrainableDefOf.Obedience, out visible);

            return visible && report.Accepted;
        }

        internal static void ChooseMaster(AnimalGroup group, Action changed)
        {
            UIGuard.Try("Animals.PickMaster", () => Dialog_PickMaster.For(group.Members, changed));
        }

        // ------------------------------------------------------------------ following

        /// <summary>
        /// How the group stands on one of the two follow switches.
        ///
        /// <b>Two switches rather than the four combinations this used to offer.</b> The menu listed Never, When
        /// drafted, When doing field work and Both, because a plain checkbox could not say that a group disagreed
        /// with itself. A tri-state switch can, so the two settings are now what they actually are: two settings.
        /// </summary>
        internal static MultiCheckboxState FollowState(AnimalGroup group, bool drafted)
        {
            return AnimalPaneParts.StateOf(group.Members, p => p?.playerSettings == null
                ? (bool?) null
                : drafted ? p.playerSettings.followDrafted : p.playerSettings.followFieldwork);
        }

        internal static void SetFollow(AnimalGroup group, bool drafted, bool on, Action changed)
        {
            UIGuard.Try("Animals.SetFollow", () =>
            {
                List<Pawn> members = group.Members;

                for (int i = 0; i < members.Count; i++)
                {
                    Pawn animal = members[i];

                    if (animal?.playerSettings == null)
                        continue;

                    if (drafted)
                        animal.playerSettings.followDrafted = on;
                    else
                        animal.playerSettings.followFieldwork = on;
                }

                changed?.Invoke();
            }, "The follow setting was not changed.");
        }

        // ------------------------------------------------------------------ trained chores

        /// <summary>
        /// Whether anything in the group has learned this chore, which is vanilla's own gate on showing it.
        ///
        /// It also means the control is absent without Odyssey, since the trainable itself is then null.
        /// </summary>
        internal static bool AnyTrained(AnimalGroup group, TrainableDef skill)
        {
            if (skill == null)
                return false;

            List<Pawn> members = group.Members;

            for (int i = 0; i < members.Count; i++)
            {
                Pawn animal = members[i];

                if (animal?.training != null && animal.training.HasLearned(skill))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// How the group stands on one chore, counting only the animals that have learned it.
        ///
        /// These are switches rather than training requests: the animal already knows how to forage, and this says
        /// whether it may. Written through the same fields vanilla writes, so a change here shows in its tab and
        /// the other way round.
        /// </summary>
        internal static MultiCheckboxState ChoreState(AnimalGroup group, TrainableDef skill, Func<Pawn, bool> read)
        {
            return AnimalPaneParts.StateOf(group.Members, p =>
                p?.playerSettings == null || p.training == null || !p.training.HasLearned(skill)
                    ? (bool?) null
                    : read(p));
        }

        internal static void SetChore(AnimalGroup group, TrainableDef skill, bool on, Action<Pawn, bool> write,
            Action changed)
        {
            UIGuard.Try("Animals.SetChore", () =>
            {
                List<Pawn> members = group.Members;

                for (int i = 0; i < members.Count; i++)
                {
                    Pawn animal = members[i];

                    if (animal?.playerSettings != null && animal.training != null
                        && animal.training.HasLearned(skill))
                        write(animal, on);
                }

                changed?.Invoke();
            }, "The chore setting was not changed.");
        }

        // ------------------------------------------------------------------ pens

        /// <summary>
        /// Which pen holds this species, as a chip reads it.
        ///
        /// <b>Never null, and that is a crash fix rather than tidiness.</b> This returned null for "no pen worth
        /// mentioning" when it fed a float menu, where null meant leave the row out. A chip has no such
        /// convention: it drew the null and <c>Widgets.LabelEllipses</c> threw, which took the whole species panel
        /// down to a failure notice. Aaron hit it on 2026-08-22 the moment his goats stopped counting as unpenned,
        /// which is what an allowed area now does to them.
        /// </summary>
        internal static string PenLabel(AnimalGroup group)
        {
            if (group == null)
                return "none";

            if (group.PenMixed)
                return "mixed";

            return group.Pen != null ? group.Pen.RenamableLabel : "none";
        }

        /// <summary>
        /// Assigns a species to a pen, by editing the pens rather than the animals.
        ///
        /// <b>This is how pens actually work, and it is worth being explicit about.</b> An animal is not assigned
        /// to a pen; a pen states which species it accepts, and the ropers take animals to a pen that accepts
        /// them. So choosing a pen here allows this species in that one and disallows it in the others, which is
        /// the whole of what the player means by moving a herd. Doing it any other way would mean issuing rope
        /// jobs by hand and then fighting the work givers that send them back.
        ///
        /// <b>Still a list rather than a row of controls,</b> because the number of pens is whatever the player
        /// has built. The pens are found by walking the colony's buildings once, when the list opens: there is no
        /// list of pens on a map to ask for, and per frame is the wrong place for that cost.
        /// </summary>
        internal static void ChoosePen(AnimalGroup group, Action changed)
        {
            ChoosePen(group?.Map, group?.Def, changed);
        }

        /// <summary>
        /// The same menu for one animal, for the inspect pane.
        ///
        /// <b>It still moves the whole species, and it has to.</b> Everything in the summary above applies
        /// unchanged: a pen states which species it accepts and there is no per-animal setting to write, so
        /// choosing a pen for the cow in front of you allows cows in that pen and disallows them in the others.
        /// One animal is simply where the player happened to click. The menu wording is the same, so what the
        /// click does is not described differently in two places -- and the inspect pane's own row says which
        /// species it is about, since it is that animal's row.
        /// </summary>
        internal static void ChoosePenFor(Pawn animal, Action changed = null)
        {
            ChoosePen(animal?.MapHeld, animal?.def, changed);
        }

        private static void ChoosePen(Map map, ThingDef species, Action changed)
        {
            if (map == null || species == null)
                return;

            List<CompAnimalPenMarker> pens = PensOn(map);
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            if (pens.Count == 0)
            {
                options.Add(new FloatMenuOption("No pens on this map", null));

                Find.WindowStack.Add(new FloatMenu(options));

                return;
            }

            foreach (CompAnimalPenMarker pen in pens)
            {
                CompAnimalPenMarker captured = pen;

                options.Add(new FloatMenuOption(captured.RenamableLabel, UIGuard.Wrap("Animals.SetPen", () =>
                {
                    foreach (CompAnimalPenMarker other in pens)
                    {
                        if (other.AnimalFilter != null)
                            other.AnimalFilter.SetAllow(species, other == captured);
                    }

                    changed?.Invoke();
                })));
            }

            options.Add(new FloatMenuOption("Any pen", UIGuard.Wrap("Animals.AnyPen", () =>
            {
                foreach (CompAnimalPenMarker other in pens)
                {
                    if (other.AnimalFilter != null)
                        other.AnimalFilter.SetAllow(species, true);
                }

                changed?.Invoke();
            })));

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Every pen marker on a map.
        ///
        /// Read off the colonist buildings rather than through a def lookup, so a pen marker added by another mod
        /// is found as well as vanilla's.
        /// </summary>
        internal static List<CompAnimalPenMarker> PensOn(Map map)
        {
            List<CompAnimalPenMarker> pens = new List<CompAnimalPenMarker>();

            List<Building> buildings = map?.listerBuildings?.allBuildingsColonist;

            if (buildings == null)
                return pens;

            for (int i = 0; i < buildings.Count; i++)
            {
                Building building = buildings[i];

                if (building == null)
                    continue;

                CompAnimalPenMarker pen = building.TryGetComp<CompAnimalPenMarker>();

                if (pen != null)
                    pens.Add(pen);
            }

            return pens;
        }

        // ------------------------------------------------------------------ slaughter and release

        /// <summary>How many of the group carry one designation, for the switch's own label.</summary>
        internal static int OrderedCount(AnimalGroup group, DesignationDef what)
        {
            int count = 0;
            List<Pawn> members = group.Members;

            for (int i = 0; i < members.Count; i++)
            {
                if (AnimalDesignations.Ordered(members[i], what))
                    count++;
            }

            return count;
        }

        /// <summary>
        /// How the group stands on one designation, counting only the animals it could apply to.
        ///
        /// An animal that cannot be slaughtered at all is not a no: it is not part of the question, and counting
        /// it would leave a herd holding one visitor's pack animal reading as permanently mixed.
        /// </summary>
        internal static MultiCheckboxState DesignationState(AnimalGroup group, DesignationDef what)
        {
            return AnimalPaneParts.StateOf(group.Members, p => !CanDesignate(p)
                ? (bool?) null
                : AnimalDesignations.Ordered(p, what));
        }

        /// <summary>
        /// Ordering a whole species slaughtered or released, and taking it back.
        ///
        /// <b>Another faction's animal asks first, as vanilla does.</b> A visitor's pack animal standing in the
        /// pen looks exactly like ours in a list, and butchering it is a diplomatic act. The confirmation is
        /// raised once for the group rather than once per animal, and answering it applies to the eligible
        /// members.
        ///
        /// Cancelling never asks: taking an order back cannot be the thing anybody needed protecting from.
        /// </summary>
        internal static void SetDesignated(AnimalGroup group, DesignationDef what, bool on, Action changed)
        {
            UIGuard.Try("Animals.GroupDesignate", () =>
            {
                List<Pawn> members = new List<Pawn>(group.Members);

                if (!on)
                {
                    Apply(members, what, false, changed);

                    return;
                }

                Faction owner;

                if (Foreign(members, out owner))
                {
                    string title = what == DesignationDefOf.Slaughter
                        ? "AnimalSlaughterConfirm".Translate(members[0].Named("PAWN"), owner.Named("FACTION"))
                            .ToString()
                        : "AnimalReleaseConfirm".Translate(members[0].Named("PAWN"), owner.Named("FACTION"))
                            .ToString();

                    Find.WindowStack.Add(new Dialog_Confirm(title,
                        UIGuard.Wrap("Animals.ConfirmOrder", () => Apply(members, what, true, changed))));

                    return;
                }

                Apply(members, what, true, changed);
            }, "Nothing was ordered.");
        }

        private static bool Foreign(List<Pawn> members, out Faction owner)
        {
            owner = null;

            for (int i = 0; i < members.Count; i++)
            {
                Faction home = members[i]?.HomeFaction;

                if (home == null || home == Faction.OfPlayer)
                    continue;

                owner = home;

                return true;
            }

            return false;
        }

        private static void Apply(List<Pawn> members, DesignationDef what, bool on, Action changed)
        {
            foreach (Pawn animal in members)
            {
                // The same test vanilla's own column makes: our animal, made of flesh, and on a map. A group can
                // hold one that fails it, and that one is skipped rather than the whole order refusing.
                if (on && !CanDesignate(animal))
                    continue;

                AnimalDesignations.Toggle(animal, what, on);
            }

            changed?.Invoke();
        }

        private static bool CanDesignate(Pawn animal)
        {
            if (animal?.RaceProps == null)
                return false;

            return animal.RaceProps.Animal && animal.RaceProps.IsFlesh && animal.Faction == Faction.OfPlayer
                   && animal.SpawnedOrAnyParentSpawned;
        }

        // ------------------------------------------------------------------ wildlife

        internal static void HuntAll(AnimalGroup group, Action changed)
        {
            UIGuard.Try("Animals.HuntAll", () =>
            {
                AnimalDesignations.SetHuntCount(group, group.Count);

                changed?.Invoke();
            }, "Nothing was ordered.");
        }

        internal static void TameAll(AnimalGroup group, Action changed)
        {
            UIGuard.Try("Animals.TameAll", () =>
            {
                AnimalDesignations.SetTameCount(group, group.Count);

                changed?.Invoke();
            }, "Nothing was ordered.");
        }

        internal static void CancelOrders(AnimalGroup group, Action changed)
        {
            UIGuard.Try("Animals.CancelAll", () =>
            {
                AnimalDesignations.SetHuntCount(group, 0);
                AnimalDesignations.SetTameCount(group, 0);

                changed?.Invoke();
            }, "The orders were left as they were.");
        }

        // ------------------------------------------------------------------ selection

        internal static void SelectAll(AnimalGroup group)
        {
            UIGuard.Try("Animals.SelectAll", () => GroupActions.Select(group.Members),
                "Nothing was selected.");
        }
    }
}
