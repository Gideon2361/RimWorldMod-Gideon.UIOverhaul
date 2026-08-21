using System;
using System.Collections.Generic;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Integrations
{
    /// <summary>
    /// Which pawns One with Death's necromancers are controlling, for the pawns tab's Undead filter.
    ///
    /// <b>Asked of that mod's own bookkeeping rather than guessed from a hediff.</b> A necromancer carries a
    /// <c>PawnComp_Necromancer</c> whose tracker holds the list of undead linked to it, which is the list the mod
    /// itself uses to decide who obeys whom. Anything else, a hediff name or a def prefix, would be our idea of
    /// undead rather than theirs, and would disagree the moment they changed it: a pawn appearing in or vanishing
    /// from a filter for no visible reason.
    ///
    /// <b>Read by reflection, with no reference to the assembly.</b> This mod must build and run with One with
    /// Death absent, so every type and field is resolved by name once and the whole feature reports itself
    /// unavailable if any of it is missing. That is also what makes the filter disappear rather than sit there
    /// permanently empty.
    ///
    /// <b>Linked undead only, and deliberately not the tracker's other lists.</b> It also holds implanted pawns
    /// and drain minions. An implanted pawn is a living colonist with an implant and belongs under Colonists, so
    /// including it would move a colonist into a filter called Undead. The narrow reading of "controlled by one of
    /// your necromancers" is the one field whose name means exactly that; if something is missing from the filter
    /// it is one field to add here.
    /// </summary>
    internal static class OneWithDeathIntegration
    {
        internal const string PackageId = "6224y.onewithdeath";

        private const string CompType = "OneWithDeath.PawnComp_Necromancer";

        private const string TrackerType = "OneWithDeath.Pawn_NecromancerTracker";

        private static Type comp;

        private static FieldInfo trackerField;

        private static FieldInfo linkedField;

        private static bool resolved;

        /// <summary>
        /// The undead the player's necromancers currently hold, rebuilt at most once a frame.
        ///
        /// <b>Frame stamped rather than rebuilt per pawn.</b> The category test runs once per pawn per rebuild of
        /// the tab, and walking every colonist's comps inside that would be quadratic on a colony with forty
        /// pawns. One walk per frame is the same answer for less work.
        /// </summary>
        private static readonly HashSet<Pawn> Controlled = new HashSet<Pawn>();

        private static int stamped = -1;

        /// <summary>
        /// Whether the filter can be offered at all: the mod is loaded and its bookkeeping is where expected.
        ///
        /// Both halves matter. The mod being present is not enough if a later version renamed the tracker, and a
        /// filter that can never match anything is worse than no filter, since it reads as one that is hiding
        /// pawns.
        /// </summary>
        internal static bool Available
        {
            get
            {
                Resolve();

                return comp != null && trackerField != null && linkedField != null;
            }
        }

        internal static bool IsControlledUndead(Pawn pawn)
        {
            if (pawn == null || !Available)
                return false;

            Refresh();

            return Controlled.Contains(pawn);
        }

        /// <summary>
        /// Adds the controlled undead standing on one map to a list, for the pass that gathers who the tab lists.
        ///
        /// <b>Taken from the tracker rather than found by filtering a map list, and that is not a shortcut.</b> The
        /// tab's other categories are all sifted out of <c>AllHumanlikeSpawned</c>, which works because a prisoner
        /// or a guest is a person. An undead is whatever was raised, including animals, so filtering the humanlike
        /// list would silently drop every raised beast. The authoritative list is the one the necromancer holds.
        ///
        /// <b>The map is a parameter because the tracker's list is not a map's list.</b> It holds everything a
        /// necromancer controls anywhere, while the caller runs once per map and every other source it draws from
        /// is already scoped to that map. Handing back the whole set therefore put the same undead under every map
        /// heading, which is what Aaron reported on 2026-08-21 with two maps and five undead listed twice.
        /// </summary>
        internal static void Fill(Map map, List<Pawn> into)
        {
            if (map == null || into == null || !Available)
                return;

            Refresh();

            foreach (Pawn pawn in Controlled)
            {
                // Spawned only, matching every other category: a pawn in a caravan or a pod is not on a map for
                // the tab to show a row about. Spawned is also what makes the map comparison safe to read.
                if (pawn != null && pawn.Spawned && !pawn.Dead && pawn.Map == map)
                    into.Add(pawn);
            }
        }

        private static void Resolve()
        {
            if (resolved)
                return;

            resolved = true;

            if (!ModIntegrations.Loaded(PackageId))
                return;

            UIGuard.Try("Pawns.BindOneWithDeath", () =>
            {
                comp = GenTypes.GetTypeInAnyAssembly(CompType);

                Type tracker = GenTypes.GetTypeInAnyAssembly(TrackerType);

                if (comp == null || tracker == null)
                    return;

                trackerField = comp.GetField("tracker", BindingFlags.Public | BindingFlags.Instance);
                linkedField = tracker.GetField("linkedUndead", BindingFlags.Public | BindingFlags.Instance);

                // Typed as the list it is, so the walk below can enumerate without another reflection step per
                // necromancer. A field of an unexpected type is treated as a missing one.
                if (linkedField != null && !typeof(IEnumerable<Pawn>).IsAssignableFrom(linkedField.FieldType))
                    linkedField = null;
            }, "The Undead filter on the pawns tab is unavailable this session.");
        }

        private static void Refresh()
        {
            if (stamped == Time.frameCount)
                return;

            stamped = Time.frameCount;

            Controlled.Clear();

            UIGuard.Try("Pawns.ReadUndead", () =>
            {
                List<Map> maps = Find.Maps;

                if (maps == null)
                    return;

                foreach (Map map in maps)
                {
                    // The necromancers are colonists, so the free colonist list is where they are. Their undead
                    // are not necessarily in it, which is the whole reason the answer comes from the tracker
                    // rather than from a list of pawns.
                    List<Pawn> colonists = map?.mapPawns?.FreeColonistsSpawned;

                    if (colonists == null)
                        continue;

                    foreach (Pawn pawn in colonists)
                        Collect(pawn);
                }
            }, null);
        }

        private static void Collect(Pawn pawn)
        {
            List<ThingComp> comps = pawn?.AllComps;

            if (comps == null)
                return;

            foreach (ThingComp candidate in comps)
            {
                if (candidate == null || !comp.IsInstanceOfType(candidate))
                    continue;

                object tracker = trackerField.GetValue(candidate);

                if (tracker == null)
                    continue;

                if (!(linkedField.GetValue(tracker) is IEnumerable<Pawn> linked))
                    continue;

                foreach (Pawn undead in linked)
                {
                    if (undead != null)
                        Controlled.Add(undead);
                }
            }
        }
    }
}
