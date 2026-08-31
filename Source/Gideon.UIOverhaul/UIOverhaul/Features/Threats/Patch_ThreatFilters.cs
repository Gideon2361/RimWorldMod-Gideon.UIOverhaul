using System;
using System.Collections.Generic;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Threats
{
    /// <summary>
    /// Stops a switched-off incident from being chosen.
    ///
    /// <b>The storyteller's own gate, which is the polite place to say no.</b> An incident that reports it cannot
    /// fire is one the storyteller passes over and replaces with something else, which is exactly what a player
    /// who turned it off wants: not a quieter game, a game with different events in it.
    /// </summary>
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.CanFireNow))]
    internal static class Patch_IncidentWorker_CanFireNow
    {
        public static void Postfix(IncidentWorker __instance, ref bool __result)
        {
            if (!__result || !ThreatToggles.Any)
                return;

            if (ThreatToggles.Disabled(__instance?.def))
                __result = false;
        }
    }

    /// <summary>
    /// Stops a switched-off incident from running even when nothing asked whether it could.
    ///
    /// <b>Both hooks are needed and this is the one that matters.</b> Several of these incidents are not rolled by
    /// the storyteller at all: a deep drill fires its own infestation, a battery fires its own short circuit, and
    /// a wastepack stockpile fires its own. Those call <c>TryExecute</c> directly and never touch
    /// <c>CanFireNow</c>, so a filter on the storyteller's gate alone would leave three of the twenty switches
    /// doing nothing.
    ///
    /// Returning false is a normal outcome here -- the method is a bool and every caller in the game handles it,
    /// because an incident that cannot find a spot or a target already returns false on its own.
    /// </summary>
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    internal static class Patch_IncidentWorker_TryExecute
    {
        public static bool Prefix(IncidentWorker __instance, ref bool __result)
        {
            if (!ThreatToggles.Any || !ThreatToggles.Disabled(__instance?.def))
                return true;

            __result = false;

            return false;
        }
    }

    /// <summary>
    /// Takes a switched-off raid strategy out of the running.
    ///
    /// <b>Patched on every worker that declares the method, not only on the base.</b> <c>CanUseWith</c> is
    /// virtual and several of vanilla's workers override it; an override that does not call base would walk
    /// straight past a patch on the base alone. The base is always in the list, so the target set can never come
    /// out empty -- which Harmony treats as an error rather than as nothing to do.
    ///
    /// A postfix rather than a prefix, so the answer is only ever narrowed: a strategy vanilla has already ruled
    /// out for its own reasons is never talked back into being available.
    /// </summary>
    internal static class Patch_RaidStrategyWorker_CanUseWith
    {
        public static void Postfix(RaidStrategyWorker __instance, IncidentParms parms,
            PawnGroupKindDef groupKind, ref bool __result)
        {
            if (!__result || !ThreatToggles.Any)
                return;

            if (ThreatToggles.Refuse(__instance?.def, parms, groupKind))
                __result = false;
        }

        /// <summary>
        /// The base declaration plus every subclass that declares its own.
        ///
        /// Shared by both worker families below rather than written twice: the shape of the problem is identical
        /// and the second copy is where the two would drift.
        /// </summary>
        internal static IEnumerable<MethodBase> Declarations(System.Type root, string name)
        {
            List<MethodBase> targets = new List<MethodBase>();

            MethodInfo baseMethod = AccessTools.DeclaredMethod(root, name);

            if (baseMethod != null)
                targets.Add(baseMethod);

            foreach (System.Type type in root.AllSubclassesNonAbstract())
            {
                MethodInfo declared = AccessTools.DeclaredMethod(type, name);

                if (declared != null)
                    targets.Add(declared);
            }

            if (targets.Count == 0)
                UIGuard.Report("Threats.NoTargets",
                    new System.MissingMethodException(root.Name + "." + name),
                    "The raid and incident switches for that kind of threat do nothing this session. Every "
                    + "other switch still works.");

            return targets;
        }
    }

    /// <summary>
    /// Takes a switched-off arrival mode out of the running.
    ///
    /// The same shape as the strategy filter above, and for the same reason: <c>CanUseWith</c> is virtual here
    /// too.
    /// </summary>
    internal static class Patch_PawnsArrivalModeWorker_CanUseWith
    {
        public static void Postfix(PawnsArrivalModeWorker __instance, IncidentParms parms, ref bool __result)
        {
            if (!__result || !ThreatToggles.Any)
                return;

            if (ThreatToggles.Refuse(__instance?.def, parms))
                __result = false;
        }
    }
    /// <summary>
    /// The raid strategy filter, applied by hand after the defs are in rather than by <c>PatchAll</c>.
    ///
    /// <b>Two separate faults made this necessary, and they compound.</b>
    ///
    /// <b>One: preparing a method runs its type's static constructor.</b> Harmony has to build a wrapper
    /// around the target, and building it initialises the declaring type. Our patches are applied from the
    /// mod's constructor, which RimWorld runs in <c>CreateModClasses</c> -- before any def exists. A modded
    /// strategy whose static constructor touches a def therefore throws a null reference the moment we go
    /// near it, through no fault of its own: it was written to be initialised at a normal time, and we
    /// arrived early. Vanilla Factions Expanded's deserter strategy is one such, reported on 2026-08-30.
    ///
    /// <b>Two: one bad target killed every other one.</b> The class asked <c>PatchAll</c> to apply a list of
    /// targets in a single call, so a failure on any one of them aborted the lot and the switches stopped
    /// working for vanilla strategies too.
    ///
    /// <b>So it is applied late and one at a time.</b> Late, because by then the defs are loaded and the
    /// static constructor that was throwing now succeeds, which keeps the feature working for that mod
    /// rather than merely surviving it. One at a time, because a type that still cannot be prepared should
    /// cost its own strategy and nothing else.
    ///
    /// <b>And overrides are patched, not just the base.</b> A Harmony patch on a virtual method does not run
    /// for an override that never calls base, so patching only <c>RaidStrategyWorker.CanUseWith</c> would
    /// quietly ignore every modded strategy -- which is the whole set this is trying to filter.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class ThreatStrategyPatch
    {
        static ThreatStrategyPatch()
        {
            UIGuard.Try("Threats.ApplyStrategyPatch", Apply,
                "Raid strategies are not filtered this session. The incident switches still work, and every "
                + "other part of this mod is unaffected.");
        }

        private static void Apply()
        {
            Harmony harmony = new Harmony(UIOverhaulMod.HarmonyId);

            int skipped = 0;

            skipped += Batch(harmony, typeof(RaidStrategyWorker),
                AccessTools.DeclaredMethod(typeof(Patch_RaidStrategyWorker_CanUseWith), "Postfix"));

            skipped += Batch(harmony, typeof(PawnsArrivalModeWorker),
                AccessTools.DeclaredMethod(typeof(Patch_PawnsArrivalModeWorker_CanUseWith), "Postfix"));

            if (skipped > 0)
            {
                Log.Warning(UILogTag.Prefix + skipped + " raid method(s) could not be patched. Each was "
                            + "reported above with the type it belongs to.");
            }
        }

        /// <summary>
        /// One family of CanUseWith overrides, patched one at a time.
        ///
        /// <b>Individually, so a type that cannot be prepared costs only itself.</b> Applied as one batch, a
        /// single modded strategy taking Harmony down with it left vanilla's strategies unfiltered too.
        /// </summary>
        private static int Batch(Harmony harmony, Type root, MethodInfo postfix)
        {
            if (postfix == null)
            {
                UIGuard.Report("Threats.NoPostfix", new MissingMethodException(root.Name + " postfix"),
                    "Raids of that kind are not filtered this session.");

                return 1;
            }

            HarmonyMethod wrapped = new HarmonyMethod(postfix);
            int skipped = 0;

            foreach (MethodBase target in Patch_RaidStrategyWorker_CanUseWith.Declarations(root, "CanUseWith"))
            {
                MethodBase one = target;

                // Named for the declaring type, so a report says which strategy could not be reached rather
                // than only that something could not.
                string where = one.DeclaringType != null ? one.DeclaringType.Name : "unknown";

                if (!UIGuard.Try("Threats.Target." + where,
                        () => harmony.Patch(one, null, wrapped),
                        "Raids using that strategy ignore the threat switches. Every other strategy is still "
                        + "filtered."))
                    skipped++;
            }

            return skipped;
        }
    }
}
