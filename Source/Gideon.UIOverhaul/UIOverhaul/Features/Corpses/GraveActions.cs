using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Corpses
{
    /// <summary>
    /// The four kinds of body a grave can be told to take, and the one word each of them goes by.
    ///
    /// A set rather than four booleans threaded through five signatures: the row draws them as a strip of pills
    /// and the toggle has to say which pill was hit.
    /// </summary>
    internal enum GraveAudience
    {
        Colonists,

        Strangers,

        Slaves,

        Animals
    }

    /// <summary>
    /// What the graves view can change, all of it through the game's own storage and assignment machinery.
    ///
    /// <b>Emptying a grave is a designation, not a teleport.</b> <c>DesignationDefOf.Open</c> plus
    /// <c>WorkGiver_Open</c> is exactly how vanilla has a colonist walk over and open a container, so exhuming
    /// somebody costs the labour it should, appears in the work list, and can be called off by pressing the same
    /// button again. The only vanilla route to the same thing is deconstructing the grave, which is why nobody
    /// does it.
    ///
    /// <b>Assignment is <c>Pawn_Ownership.ClaimGrave</c>, which is what the grave's own gizmo calls.</b> It
    /// raises the grave's priority to critical and narrows it to that one body, so the reservation is understood
    /// by the hauling system rather than only by this tab.
    /// </summary>
    internal static class GraveActions
    {
        // ---------------------------------------------------------------------------------------
        // The special filters that separate one kind of humanlike corpse from another
        // ---------------------------------------------------------------------------------------

        internal static SpecialThingFilterDef ColonistFilter
        {
            get { return Special("AllowCorpsesColonist"); }
        }

        internal static SpecialThingFilterDef StrangerFilter
        {
            get { return Special("AllowCorpsesStranger"); }
        }

        internal static SpecialThingFilterDef SlaveFilter
        {
            get { return Special("AllowCorpsesSlave"); }
        }

        private static readonly Dictionary<string, SpecialThingFilterDef> Filters =
            new Dictionary<string, SpecialThingFilterDef>();

        /// <summary>
        /// One of the corpse special filters, looked up once.
        ///
        /// Not through a <c>DefOf</c>: <c>AllowCorpsesSlave</c> only exists with Ideology loaded, and a DefOf
        /// field for a def that may be absent is a load-time error rather than a null.
        /// </summary>
        private static SpecialThingFilterDef Special(string defName)
        {
            SpecialThingFilterDef found;

            if (Filters.TryGetValue(defName, out found))
                return found;

            found = DefDatabase<SpecialThingFilterDef>.GetNamedSilentFail(defName);

            Filters[defName] = found;

            return found;
        }

        internal static bool Allows(ThingFilter filter, SpecialThingFilterDef special)
        {
            return filter != null && special != null && filter.Allows(special);
        }

        /// <summary>
        /// The corpse defs under a category, flattened once and kept.
        ///
        /// <c>DescendantThingDefs</c> is an iterator over every child category, and the animal one has a corpse
        /// def per race in the game. Walking that per grave per rebuild is several thousand allocations a second
        /// on a colony with a decent yard, for an answer that cannot change after the defs are loaded.
        /// </summary>
        private static readonly Dictionary<ThingCategoryDef, List<ThingDef>> Members =
            new Dictionary<ThingCategoryDef, List<ThingDef>>();

        private static List<ThingDef> Under(ThingCategoryDef category)
        {
            List<ThingDef> found;

            if (Members.TryGetValue(category, out found))
                return found;

            found = new List<ThingDef>();

            foreach (ThingDef def in category.DescendantThingDefs)
                found.Add(def);

            Members[category] = found;

            return found;
        }

        /// <summary>Whether a filter lets anything at all through from a category.</summary>
        internal static bool AnyAllowed(ThingFilter filter, ThingCategoryDef category)
        {
            return UIGuard.Try("Corpses.AnyAllowed", () =>
            {
                if (filter == null || category == null)
                    return false;

                List<ThingDef> defs = Under(category);

                for (int i = 0; i < defs.Count; i++)
                {
                    if (filter.Allows(defs[i]))
                        return true;
                }

                return false;
            }, false, null);
        }

        // ---------------------------------------------------------------------------------------
        // What a grave accepts
        // ---------------------------------------------------------------------------------------

        internal static string LabelOf(GraveAudience audience)
        {
            switch (audience)
            {
                case GraveAudience.Colonists: return "colonists";
                case GraveAudience.Strangers: return "strangers";
                case GraveAudience.Slaves: return "slaves";
                default: return "animals";
            }
        }

        internal static bool Accepts(GraveRecord record, GraveAudience audience)
        {
            switch (audience)
            {
                case GraveAudience.Colonists: return record.AcceptsColonists;
                case GraveAudience.Strangers: return record.AcceptsStrangers;
                case GraveAudience.Slaves: return record.AcceptsSlaves;
                default: return record.AcceptsAnimals;
            }
        }

        /// <summary>
        /// Turns one audience on or off for a grave.
        ///
        /// <b>Switching an audience on also allows its category, because a special filter alone does nothing.</b>
        /// The humanlike toggles are exclusions applied to defs the filter already allows, so turning "strangers"
        /// on for a grave whose humanlike corpses were disallowed wholesale would light the pill up and change
        /// nothing at all. Turning one off leaves the category alone, so the other two keep working.
        /// </summary>
        internal static void SetAccepts(GraveRecord record, GraveAudience audience, bool allow)
        {
            UIGuard.Try("Corpses.SetAccepts", () =>
            {
                StorageSettings settings = record.Grave.GetStoreSettings();

                if (settings == null || settings.filter == null)
                    return;

                ThingFilter filter = settings.filter;

                if (audience == GraveAudience.Animals)
                {
                    filter.SetAllow(ThingCategoryDefOf.CorpsesAnimal, allow);

                    GraveRoster.Invalidate();

                    return;
                }

                SpecialThingFilterDef special = audience == GraveAudience.Colonists
                    ? ColonistFilter
                    : audience == GraveAudience.Strangers
                        ? StrangerFilter
                        : SlaveFilter;

                if (special == null)
                    return;

                if (allow)
                    filter.SetAllow(ThingCategoryDefOf.CorpsesHumanlike, true);

                filter.SetAllow(special, allow);

                GraveRoster.Invalidate();
            }, "That grave's settings could not be changed.");
        }

        // ---------------------------------------------------------------------------------------
        // Reservation
        // ---------------------------------------------------------------------------------------

        internal static void Reserve(GraveRecord record, Pawn pawn)
        {
            UIGuard.Try("Corpses.Reserve", () =>
            {
                if (pawn == null)
                {
                    Clear(record);

                    return;
                }

                if (pawn.ownership == null)
                    return;

                pawn.ownership.ClaimGrave(record.Grave);

                // A body already on the map is the commonest thing to reserve a grave for, and a forbidden one
                // would sit there with the grave waiting for it.
                Corpse corpse = pawn.Corpse;

                if (corpse != null && corpse.Spawned)
                    corpse.SetForbidden(false, false);

                GraveRoster.Invalidate();
                CorpseRoster.Invalidate();
            }, "The grave could not be reserved.");
        }

        internal static void Clear(GraveRecord record)
        {
            UIGuard.Try("Corpses.Unreserve", () =>
            {
                Pawn held = record.Grave.AssignedPawn;

                if (held != null && held.ownership != null)
                    held.ownership.UnclaimGrave();

                GraveRoster.Invalidate();
                CorpseRoster.Invalidate();
            }, "The reservation could not be cleared.");
        }

        /// <summary>
        /// The body this empty grave should be given, or null when nothing on the map needs it.
        ///
        /// <b>Our own dead first, then whoever has been out longest.</b> The colony pays -10 mood for a colonist
        /// left in the open and nothing at all for a raider, so that ordering is the mood clock and not a
        /// sentiment. Bodies already reserved for another grave are skipped, and so is anything this grave's own
        /// filter refuses -- the same rule the Bury button on the other view follows.
        /// </summary>
        internal static Corpse Neediest(GraveRecord record)
        {
            return UIGuard.Try("Corpses.Neediest", () =>
            {
                if (record.Map == null || record.Map.listerThings == null)
                    return null;

                StorageSettings settings = record.Grave.GetStoreSettings();

                if (settings == null)
                    return null;

                List<Thing> corpses = record.Map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);

                Corpse best = null;
                float bestScore = float.MinValue;

                for (int i = 0; corpses != null && i < corpses.Count; i++)
                {
                    Corpse corpse = corpses[i] as Corpse;

                    if (corpse == null || corpse.Bugged)
                        continue;

                    Pawn pawn = corpse.InnerPawn;

                    if (pawn == null || pawn.ownership == null || pawn.ownership.AssignedGrave != null)
                        continue;

                    if (!settings.AllowedToAccept(corpse))
                        continue;

                    float score = CorpseFacts.CountsAsUnburied(corpse) ? 1000000f : 0f;

                    score += CorpseFacts.AgeOf(corpse);

                    if (score <= bestScore)
                        continue;

                    bestScore = score;
                    best = corpse;
                }

                return best;
            }, null, null);
        }

        // ---------------------------------------------------------------------------------------
        // Emptying
        // ---------------------------------------------------------------------------------------

        internal static void SetEmptying(GraveRecord record, bool wanted)
        {
            UIGuard.Try("Corpses.Empty", () =>
            {
                Building_Grave grave = record.Grave;

                if (grave == null || !grave.Spawned || grave.Map == null
                    || grave.Map.designationManager == null)
                    return;

                Designation existing = grave.Map.designationManager.DesignationOn(grave, DesignationDefOf.Open);

                if (wanted && existing == null)
                    grave.Map.designationManager.AddDesignation(new Designation(grave, DesignationDefOf.Open));
                else if (!wanted && existing != null)
                    grave.Map.designationManager.RemoveDesignation(existing);

                GraveRoster.Invalidate();
            }, "The grave could not be marked.");
        }

        // ---------------------------------------------------------------------------------------
        // Building more
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Hands the player the build tool for a grave or a sarcophagus, and gets out of the way.
        ///
        /// <b>The architect designator rather than a placement of our own.</b> Where a grave goes is a decision
        /// about rooms, roofs and walking distance that only makes sense on the map, so the button's whole job is
        /// to close the tab with the right tool already in hand.
        /// </summary>
        internal static void Build(string defName)
        {
            UIGuard.Try("Corpses.Build", () =>
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);

                if (def == null)
                    return;

                Find.MainTabsRoot.EscapeCurrentTab(false);

                Find.DesignatorManager.Select(new Designator_Build(def));
            }, "The build tool could not be opened.");
        }
    }
}
