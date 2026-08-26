using System;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Integrations
{
    /// <summary>
    /// Fluffy Breakdowns' maintenance level, on our inspect pane.
    ///
    /// <b>What that mod does, and why it needs a bar.</b> It replaces RimWorld's random breakdowns with wear:
    /// every building using components has a durability from 1 down to 0, colonists top it back up, and the odds
    /// of a breakdown are read straight off that number. So it is a need, exactly like fuel or charge -- something
    /// that drains, has a line you should not cross, and is somebody's job to refill. The mod itself can only
    /// report it as a sentence, because a <c>ThingComp</c> inspect string is the one surface RimWorld gives it:
    /// it prefixes <c>CompBreakdownable.CompInspectStringExtra</c> with "Maintenance: 93%" and there is nowhere
    /// else for it to go. Asked for on 2026-08-25 as a bar, which is the shape the fact wanted all along.
    ///
    /// <b>Reflection by type name, and no reference.</b> Same rule as every integration here: a hard reference
    /// would make this mod refuse to load without Fluffy Breakdowns installed. Every member is resolved once and
    /// a rename in a future version costs this one block rather than the pane.
    ///
    /// <b>Detected by type rather than by package id,</b> which is the difference from the other integrations in
    /// this folder. Fluffy's original and the 1.6 continuation are different Workshop items with different ids --
    /// <c>theeyeofbrows.fluffybreakdowns</c> is only the one installed here -- and both ship the same assembly
    /// with the same namespace. Asking whether the type resolves answers the question for any of them, including
    /// a fork nobody has told us about, and it is the stronger test anyway: a package id can be present while the
    /// assembly failed to load.
    ///
    /// <b>The public pair is used rather than the convenient one.</b> <c>CompBreakdownable_Extensions.Durability</c>
    /// is an extension method and would read better, but it is <c>internal</c>, and reaching for a non-public
    /// member is how an integration ends up silently broken by a refactor that changed nothing anybody could see.
    /// <c>MapComponent_Durability.ForMap</c> and its <c>GetDurability</c> are both public, and the extension is a
    /// two-line wrapper over exactly those, so this asks the same question by the supported route.
    /// </summary>
    internal static class FluffyBreakdownsIntegration
    {
        /// <summary>The 1.6 continuation, which is the one installed here. Not the only id this can run beside.</summary>
        internal const string PackageId = "theeyeofbrows.fluffybreakdowns";

        private static bool resolved;

        private static bool usable;

        private static MethodInfo forMap;
        private static MethodInfo getDurability;
        private static FieldInfo threshold;

        /// <summary>Whether Fluffy Breakdowns is running and every member this needs was found.</summary>
        internal static bool Available
        {
            get { return Ready(); }
        }

        /// <summary>
        /// The threshold below which that mod considers a building to need maintenance.
        ///
        /// <b>Read live rather than cached, because it is a setting.</b> It is a public static field on the mod's
        /// own <c>Settings</c>, adjustable in its options page at any time, and a value cached at first use would
        /// leave our bar's mark somewhere the mod no longer agrees with. Its default is 0.7, which is also the
        /// fallback here, so a version that has renamed the field puts the mark where it has always been rather
        /// than at zero.
        /// </summary>
        internal static float Threshold
        {
            get
            {
                if (!Ready())
                    return 0.7f;

                return UIGuard.Try("Integrations.FluffyThreshold", () =>
                {
                    object value = threshold.GetValue(null);

                    return value is float ? (float) value : 0.7f;
                }, 0.7f, null);
            }
        }

        /// <summary>
        /// How maintained this building is, from 1 down to 0, or null when there is nothing to show.
        /// </summary>
        /// <remarks>
        /// Null covers four different absences and deliberately does not distinguish them, because the caller does
        /// the same thing about all of them: the mod is not running, this thing is not a building, it has no
        /// <c>CompBreakdownable</c>, or it is not on a map. That last one is not an error -- a building in a
        /// caravan's inventory or inside a gravship in flight has no map component to ask, and the mod's own
        /// extension answers a flat 1 there rather than a real reading. Answering null instead means the pane
        /// omits the block rather than drawing a full bar it cannot stand behind.
        /// </remarks>
        internal static float? Durability(Thing thing)
        {
            if (!Ready())
                return null;

            Building building = thing as Building;

            if (building == null || building.Map == null)
                return null;

            return UIGuard.Try<float?>("Integrations.FluffyDurability", () =>
            {
                // Asked of RimWorld rather than of the mod: a building with no CompBreakdownable is not
                // maintainable at all, and ForMap would happily create a map component to tell us so.
                if (building.TryGetComp<CompBreakdownable>() == null)
                    return null;

                object component = forMap.Invoke(null, new object[] { building.Map });

                if (component == null)
                    return null;

                object value = getDurability.Invoke(component, new object[] { building });

                return value is float ? (float?) (float) value : null;
            }, null, null);
        }

        /// <summary>
        /// Resolves the members once.
        ///
        /// <b>Overload chosen by parameter type, not by position.</b> <c>GetDurability</c> is overloaded on
        /// <c>CompBreakdownable</c> and on <c>Building</c>, so a plain <c>GetMethod</c> by name throws for
        /// ambiguity. Naming the argument type also means a third overload arriving later cannot silently
        /// change which one we call.
        /// </summary>
        private static bool Ready()
        {
            if (resolved)
                return usable;

            resolved = true;

            usable = UIGuard.Try("Integrations.FluffyResolve", () =>
            {
                Type durability = GenTypes.GetTypeInAnyAssembly("Fluffy_Breakdowns.MapComponent_Durability");
                Type settings = GenTypes.GetTypeInAnyAssembly("Fluffy_Breakdowns.Settings");

                if (durability == null || settings == null)
                    return false;

                forMap = durability.GetMethod("ForMap", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(Map) }, null);

                getDurability = durability.GetMethod("GetDurability",
                    BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Building) }, null);

                threshold = settings.GetField("MaintenanceThreshold", BindingFlags.Public | BindingFlags.Static);

                return forMap != null && getDurability != null && threshold != null;
            }, false,
                "Fluffy Breakdowns is installed but its maintenance level could not be read, so the inspect pane "
                + "leaves that block out. Nothing about maintenance itself is affected.");

            return usable;
        }
    }
}
