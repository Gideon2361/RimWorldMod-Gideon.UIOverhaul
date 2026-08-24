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
    [HarmonyPatch]
    internal static class Patch_RaidStrategyWorker_CanUseWith
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return Declarations(typeof(RaidStrategyWorker), "CanUseWith");
        }

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
    [HarmonyPatch]
    internal static class Patch_PawnsArrivalModeWorker_CanUseWith
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return Patch_RaidStrategyWorker_CanUseWith.Declarations(typeof(PawnsArrivalModeWorker),
                "CanUseWith");
        }

        public static void Postfix(PawnsArrivalModeWorker __instance, IncidentParms parms, ref bool __result)
        {
            if (!__result || !ThreatToggles.Any)
                return;

            if (ThreatToggles.Refuse(__instance?.def, parms))
                __result = false;
        }
    }
}
