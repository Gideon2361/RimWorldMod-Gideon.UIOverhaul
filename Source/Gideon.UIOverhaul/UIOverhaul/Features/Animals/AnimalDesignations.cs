using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// Reading and writing the hunt, tame, slaughter and release orders on animals, one animal or a whole species
    /// at a time.
    ///
    /// <b>The count is the decision; which animals carry it out is arithmetic.</b> That is the one idea this file
    /// exists for. Vanilla asks the player to tick forty identical rows, and the player's actual intent was "hunt
    /// four of those deer". So the tab asks for a number and this works out which four, which is the sort of thing
    /// software should be doing on somebody's behalf.
    ///
    /// <b>Legality is vanilla's, not ours.</b> Whether an animal can be hunted or tamed at all is taken from the
    /// same tests the game's own columns and designators make, so an animal stops being offered here at the exact
    /// moment it stops being offered there. Where the two could drift, they would drift into a control that does
    /// nothing, which is the worst kind.
    ///
    /// <b>Warnings once per species, which is also vanilla's rhythm.</b> A drag over twelve beavers in the game
    /// produces one manhunter warning, not twelve, because the designator collects what it touched and reports per
    /// pawn kind at the end. A stepper is the same gesture, so it warns the same way.
    /// </summary>
    internal static class AnimalDesignations
    {
        /// <summary>
        /// Whether hunting this animal can be ordered.
        ///
        /// The four tests from <c>PawnColumnWorker_Hunt</c>: an animal or wild man, made of flesh, not belonging
        /// to a humanlike faction, and present on a map. Mechanoid wildlife is excluded by the flesh test, which
        /// is why it is here rather than being assumed.
        /// </summary>
        internal static bool CanHunt(Pawn animal)
        {
            if (animal == null || animal.Destroyed)
                return false;

            if (!animal.AnimalOrWildMan() || !animal.RaceProps.IsFlesh)
                return false;

            if (animal.Faction != null && animal.Faction.def.humanlikeFaction)
                return false;

            return animal.SpawnedOrAnyParentSpawned && animal.MapHeld != null;
        }

        /// <summary>
        /// Whether taming this animal can be ordered.
        ///
        /// <c>TameUtility.CanTame</c> covers the wildness ceiling, dryads and scaria; the spawned test is the
        /// column's own. Nothing here judges whether taming is a good idea, which is what the odds and the
        /// manhunter chance on the row are for.
        /// </summary>
        internal static bool CanTame(Pawn animal)
        {
            if (animal == null || animal.Destroyed)
                return false;

            return TameUtility.CanTame(animal) && animal.SpawnedOrAnyParentSpawned && animal.MapHeld != null;
        }

        internal static bool Ordered(Pawn animal, DesignationDef what)
        {
            if (animal == null || what == null)
                return false;

            return animal.MapHeld?.designationManager?.DesignationOn(animal, what) != null;
        }

        /// <summary>Adds this animal's standing orders into its group's counts. Called from the rebuild.</summary>
        internal static void Tally(AnimalGroup group, Pawn animal)
        {
            DesignationManager designations = animal.MapHeld?.designationManager;

            if (designations == null)
                return;

            if (designations.DesignationOn(animal, DesignationDefOf.Hunt) != null)
                group.HuntOrdered++;

            if (designations.DesignationOn(animal, DesignationDefOf.Tame) != null)
                group.TameOrdered++;

            if (designations.DesignationOn(animal, DesignationDefOf.Slaughter) != null)
                group.SlaughterOrdered++;

            if (designations.DesignationOn(animal, DesignationDefOf.ReleaseAnimalToWild) != null)
                group.ReleaseOrdered++;
        }

        // -------------------------------------------------------------------------------------------
        // Choosing
        // -------------------------------------------------------------------------------------------

        /// <summary>Scratch for a pick. The UI is single threaded, and a pick is over before the next one starts.</summary>
        private static readonly List<Pawn> Pool = new List<Pawn>();

        /// <summary>
        /// The animals a hunt of this size would take, best first.
        ///
        /// <b>Wounded first, then the best return for the walk.</b> An animal already bleeding is going to die
        /// and rot whether or not anybody shoots it, so it is free meat and comes first. After that the ordering
        /// is meat divided by distance rather than either alone, which is what a player actually weighs: nobody
        /// crosses the map for a hare, and nobody ignores an elephant at the door. The twenty in the divisor stops
        /// an animal standing on the doorstep from beating a whole herd just for being close.
        ///
        /// <b>A mother with a juvenile of her own is left until last.</b> Shooting her orphans an animal that
        /// then starves, which is a thing the player did not ask for and would not notice until it happened. She
        /// is not excluded outright, because a request for the whole group is unambiguous.
        ///
        /// The returned list is scratch and is valid until the next call.
        /// </summary>
        internal static List<Pawn> ChooseForHunt(AnimalGroup group, int count)
        {
            Pool.Clear();

            if (group == null || count <= 0)
                return Pool;

            for (int i = 0; i < group.Members.Count; i++)
            {
                Pawn animal = group.Members[i];

                if (CanHunt(animal))
                    Pool.Add(animal);
            }

            Pool.Sort(CompareForHunt);

            if (Pool.Count > count)
                Pool.RemoveRange(count, Pool.Count - count);

            return Pool;
        }

        private static int CompareForHunt(Pawn a, Pawn b)
        {
            int rank = HuntRank(b) - HuntRank(a);

            if (rank != 0)
                return rank;

            float aValue = HuntValue(a);
            float bValue = HuntValue(b);

            if (!Mathf.Approximately(aValue, bValue))
                return bValue > aValue ? 1 : -1;

            return string.Compare(a.ThingID, b.ThingID, System.StringComparison.Ordinal);
        }

        /// <summary>Wounded above healthy, and a nursing mother below both.</summary>
        private static int HuntRank(Pawn animal)
        {
            if (Nursing(animal))
                return -1;

            if (animal.Downed || animal.health?.hediffSet?.BleedRateTotal > 0.01f)
                return 1;

            return 0;
        }

        private static float HuntValue(Pawn animal)
        {
            float meat = AnimalFacts.Meat(animal);
            float distance = 0f;

            // The same colony centre the distance column shows, so the ordering agrees with what the player is
            // reading off the row rather than being measured from somewhere else.
            if (animal.Spawned && animal.Map != null)
                distance = animal.Position.DistanceTo(AnimalRoster.ColonyCentre(animal.Map));

            return meat / (distance + 20f);
        }

        /// <summary>
        /// Whether this animal has a juvenile child of its own alive on the same map.
        ///
        /// Only asked of females, and only through the direct relations list, which is short. Wild animals born on
        /// the map get a parent relation the same way tame ones do, so this catches the case that matters and
        /// costs nothing on a herd that was generated with the map.
        /// </summary>
        private static bool Nursing(Pawn animal)
        {
            if (animal == null || animal.gender != Gender.Female || animal.relations == null)
                return false;

            List<DirectPawnRelation> relations = animal.relations.DirectRelations;

            if (relations == null)
                return false;

            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation relation = relations[i];

                if (relation?.def != PawnRelationDefOf.Child)
                    continue;

                Pawn child = relation.otherPawn;

                if (child == null || child.Dead || child.Destroyed || !child.Spawned)
                    continue;

                if (child.Map == animal.Map && AnimalFacts.Juvenile(child))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The animals a taming order of this size would go after, best first.
        ///
        /// <b>Nearest, then females, then adults.</b> Distance decides how long a handler spends walking, which
        /// is the real cost of taming. Females come next because a tamed female is a herd rather than an animal,
        /// and adults before juveniles because a juvenile cannot be put to work or bred from for a season.
        /// </summary>
        internal static List<Pawn> ChooseForTame(AnimalGroup group, int count)
        {
            Pool.Clear();

            if (group == null || count <= 0)
                return Pool;

            for (int i = 0; i < group.Members.Count; i++)
            {
                Pawn animal = group.Members[i];

                if (CanTame(animal))
                    Pool.Add(animal);
            }

            Pool.Sort(CompareForTame);

            if (Pool.Count > count)
                Pool.RemoveRange(count, Pool.Count - count);

            return Pool;
        }

        private static int CompareForTame(Pawn a, Pawn b)
        {
            int rank = TameRank(b) - TameRank(a);

            if (rank != 0)
                return rank;

            return string.Compare(a.ThingID, b.ThingID, System.StringComparison.Ordinal);
        }

        private static int TameRank(Pawn animal)
        {
            int rank = 0;

            if (animal.gender == Gender.Female)
                rank += 2;

            if (!AnimalFacts.Juvenile(animal))
                rank += 1;

            return rank;
        }

        // -------------------------------------------------------------------------------------------
        // Writing
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Brings the number of hunt orders on this species to <paramref name="wanted"/>.
        ///
        /// <b>Adding and removing are not symmetrical, on purpose.</b> Adding picks the best candidates. Removing
        /// takes back the orders nobody has started on first, because a hunter already walking towards an animal
        /// with a rifle has done work that a stepper nudged down by one should not throw away. Only when every
        /// remaining order is claimed does it cancel a claimed one.
        /// </summary>
        internal static void SetHuntCount(AnimalGroup group, int wanted)
        {
            UIGuard.Try("Animals.SetHunt", () =>
            {
                if (group?.Map == null)
                    return;

                wanted = Mathf.Clamp(wanted, 0, group.Count);

                int ordered = CountOrdered(group, DesignationDefOf.Hunt);

                if (wanted > ordered)
                    Add(group, DesignationDefOf.Hunt, ChooseForHunt(group, group.Count), wanted - ordered);
                else if (wanted < ordered)
                    Remove(group, DesignationDefOf.Hunt, ordered - wanted);

                AnimalRoster.Invalidate();
            }, "The hunting orders were not changed.");
        }

        /// <summary>Brings the number of taming orders on this species to <paramref name="wanted"/>.</summary>
        internal static void SetTameCount(AnimalGroup group, int wanted)
        {
            UIGuard.Try("Animals.SetTame", () =>
            {
                if (group?.Map == null)
                    return;

                wanted = Mathf.Clamp(wanted, 0, group.Count);

                int ordered = CountOrdered(group, DesignationDefOf.Tame);

                if (wanted > ordered)
                    Add(group, DesignationDefOf.Tame, ChooseForTame(group, group.Count), wanted - ordered);
                else if (wanted < ordered)
                    Remove(group, DesignationDefOf.Tame, ordered - wanted);

                AnimalRoster.Invalidate();
            }, "The taming orders were not changed.");
        }

        private static int CountOrdered(AnimalGroup group, DesignationDef what)
        {
            DesignationManager designations = group.Map?.designationManager;

            if (designations == null)
                return 0;

            int count = 0;

            for (int i = 0; i < group.Members.Count; i++)
            {
                if (designations.DesignationOn(group.Members[i], what) != null)
                    count++;
            }

            return count;
        }

        /// <summary>
        /// Adds up to <paramref name="howMany"/> orders, walking the chosen order.
        ///
        /// The candidate list is the whole group in preference order rather than a slice of it, because some of
        /// the best candidates may already carry the order: asking for one more deer should add the best deer that
        /// is not already designated, not fail because the best one is spoken for.
        /// </summary>
        private static void Add(AnimalGroup group, DesignationDef what, List<Pawn> candidates, int howMany)
        {
            DesignationManager designations = group.Map?.designationManager;

            if (designations == null || howMany <= 0)
                return;

            Pawn warned = null;
            int added = 0;

            for (int i = 0; i < candidates.Count && added < howMany; i++)
            {
                Pawn animal = candidates[i];

                if (designations.DesignationOn(animal, what) != null)
                    continue;

                // Everything else goes, which is vanilla's own behaviour: hunting an animal being tamed cancels
                // the taming, and the two orders cannot both stand.
                designations.RemoveAllDesignationsOn(animal);
                designations.AddDesignation(new Designation(animal, what));

                warned = animal;
                added++;
            }

            if (warned == null)
                return;

            // One warning for the species, after the whole batch, the way a drag over a herd warns once. Doing it
            // per animal would put twelve identical manhunter messages on screen for one click.
            if (what == DesignationDefOf.Hunt)
                Designator_Hunt.ShowDesignationWarnings(warned);
            else if (what == DesignationDefOf.Tame)
                TameUtility.ShowDesignationWarnings(warned);
        }

        private static void Remove(AnimalGroup group, DesignationDef what, int howMany)
        {
            DesignationManager designations = group.Map?.designationManager;

            if (designations == null || howMany <= 0)
                return;

            // Two passes: the unclaimed orders first, then whatever is left. A single pass with a sort would read
            // better and would mean building a list to sort, for a decision this simple.
            for (int pass = 0; pass < 2 && howMany > 0; pass++)
            {
                for (int i = group.Members.Count - 1; i >= 0 && howMany > 0; i--)
                {
                    Pawn animal = group.Members[i];
                    Designation designation = designations.DesignationOn(animal, what);

                    if (designation == null)
                        continue;

                    if (pass == 0 && Claimed(animal))
                        continue;

                    designations.RemoveDesignation(designation);
                    howMany--;
                }
            }
        }

        /// <summary>Whether a colonist has already taken the job of dealing with this animal.</summary>
        private static bool Claimed(Pawn animal)
        {
            if (animal?.Map?.reservationManager == null)
                return false;

            return animal.Map.reservationManager.IsReservedByAnyoneOf(animal, Faction.OfPlayer);
        }

        /// <summary>
        /// Turns one order on or off for one animal, for the checkbox on an opened row.
        ///
        /// The same mutual exclusion as the group path, so unticking Hunt on one deer and ticking Tame does what
        /// it looks like it does.
        /// </summary>
        internal static void Toggle(Pawn animal, DesignationDef what, bool on)
        {
            UIGuard.Try("Animals.Toggle", () =>
            {
                DesignationManager designations = animal?.MapHeld?.designationManager;

                if (designations == null || what == null)
                    return;

                Designation existing = designations.DesignationOn(animal, what);

                if (on == (existing != null))
                    return;

                if (on)
                {
                    designations.RemoveAllDesignationsOn(animal);
                    designations.AddDesignation(new Designation(animal, what));

                    if (what == DesignationDefOf.Hunt)
                        Designator_Hunt.ShowDesignationWarnings(animal);
                    else if (what == DesignationDefOf.Tame)
                        TameUtility.ShowDesignationWarnings(animal);
                }
                else
                {
                    designations.RemoveDesignation(existing);
                }

                AnimalRoster.Invalidate();
            }, "The order was not changed.");
        }
    }
}
