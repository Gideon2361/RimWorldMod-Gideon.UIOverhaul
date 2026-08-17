using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones
{
    /// <summary>
    /// Lets saves written by the standalone Growing Zones Plus mod keep their growing zones.
    ///
    /// <b>What breaks without this.</b> Scribe stores components, zones and bills by class name. Growing
    /// Zones Plus was folded into this mod, and its types moved from the <c>Growing_Zones_Plus</c> namespace
    /// into <c>Gideon.UIOverhaul.Features.GrowZones</c>. A save made before the merge still names the old
    /// ones, so <c>ScribeExtractor</c> cannot resolve them, falls back to the abstract base -- <c>Verse.Zone</c>,
    /// <c>Verse.MapComponent</c> -- and throws, which drops the object entirely.
    ///
    /// The damage is not limited to what is dropped. Those objects live in collections the whole game walks,
    /// so a hole left in <c>zoneManager.AllZones</c> takes out every mod that iterates zones, including this
    /// one: a real load produced 6 dropped zones and then a NullReferenceException in our own
    /// <c>ConvertZones</c>, several steps downstream of the actual cause.
    ///
    /// <b>Three names, not the two that show up in a log.</b> <c>Bill_Growing</c> never reports a failure
    /// because the zones holding those bills are dropped first, so it is never reached. Aliasing only what
    /// the log complains about would fix the zones and then lose every bill inside them.
    ///
    /// <b>A postfix rather than a registered <c>BackCompatibilityConverter</c>,</b> which is the mechanism
    /// this looks like it should use. <c>BackCompatibility.GetBackCompatibleType</c> skips the entire
    /// converter chain when <c>CheckSaveIdenticalToCurrentEnvironment</c> is true, and that test turns on
    /// things outside our control -- the game build and whether the mod list changed. A rename needs to be
    /// honoured every time the old name appears, not only when the environment happens to look different, so
    /// this sits after the resolution and answers precisely when nothing else could.
    ///
    /// <b>It costs nothing on a normal load.</b> The postfix returns immediately unless the game has already
    /// failed to resolve a name, which never happens for a save this mod wrote.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_LegacyTypeNames
    {
        /// <summary>
        /// Old fully-qualified name to current type.
        ///
        /// Every one of these is a pure namespace change: the class names themselves did not move. A rewrite
        /// rule mapping the whole old namespace would therefore work today, and is still not what this does.
        /// A rule would silently claim any future <c>Growing_Zones_Plus.Something</c> that this mod does not
        /// actually provide and hand back a wrong type, where an explicit list fails cleanly instead. Three
        /// entries is a small price for that.
        /// </summary>
        private static readonly Dictionary<string, Type> Renamed = new Dictionary<string, Type>
        {
            { "Growing_Zones_Plus.Zone_GrowingPlus", typeof(Zone_GrowingPlus) },
            { "Growing_Zones_Plus.Bill_Growing", typeof(Bill_Growing) },
            { "Growing_Zones_Plus.MapComponentGzp", typeof(MapComponentGzp) }
        };

        [HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.GetBackCompatibleType))]
        [HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.GetBackCompatibleTypeDirect))]
        [HarmonyPostfix]
        public static void Rename(string providedClassName, ref Type __result)
        {
            // Only when the game could not resolve it. Anything that already resolved is left alone, so this
            // can never shadow a type another mod provides.
            if (__result != null || providedClassName.NullOrEmpty())
                return;

            Type renamed;

            if (!Renamed.TryGetValue(providedClassName, out renamed))
                return;

            __result = renamed;

            Report(providedClassName, renamed);
        }

        /// <summary>
        /// Says once per old name that a rename was applied.
        ///
        /// <b>Announced rather than done silently.</b> Somebody reading a log to work out why an old save
        /// behaves oddly should be able to see that its zones arrived through a compatibility path. Once per
        /// name, because a save can hold hundreds of these and the interesting fact is that it happened at
        /// all.
        /// </summary>
        private static void Report(string oldName, Type newType)
        {
            if (!Announced.Add(oldName))
                return;

            UIGuard.Try("GrowZones.LegacyTypeName", () => Log.Message(
                    "[UI Overhaul] Loading a save from before Growing Zones Plus was merged in: reading "
                    + oldName + " as " + newType.FullName + "."),
                null);
        }

        private static readonly HashSet<string> Announced = new HashSet<string>();
    }
}
