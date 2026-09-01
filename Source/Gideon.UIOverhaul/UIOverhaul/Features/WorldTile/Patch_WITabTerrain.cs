using System.Reflection;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.WorldTile
{
    /// <summary>
    /// Replaces the world map's terrain tab with <see cref="WorldTilePanel"/>.
    ///
    /// <b>The whole method, because the whole method is the layout.</b> <c>FillTab</c> opens a scroll view,
    /// builds a <c>Listing_Standard</c> and pushes every reading through it as a label pair. There is no seam
    /// inside that to group, colour or reorder anything from, which is the entire content of this change.
    ///
    /// <b>Patched on the class that declares it.</b> <c>FillTab</c> is abstract on <c>InspectTabBase</c> and
    /// every world tab overrides it, so naming the base would have caught all of them at once.
    ///
    /// <b>Vanilla's misc rows still run.</b> Vanilla Expanded Framework appends to <c>ListMiscDetails</c> and
    /// is loaded here; the panel calls that method rather than reproducing it, so their rows survive a tab
    /// they know nothing about. See <c>WorldTilePanel.Misc</c>.
    ///
    /// <b>Guarded with <c>TryOnce</c>.</b> The panel draws nested groups and a scroll view, and a throw partway
    /// through one leaves Unity's clip stack unbalanced for everything after it. On the first failure this
    /// stands down for the session and vanilla's tab comes back, which is a real fallback rather than a
    /// nominal one: nothing else here is suppressed.
    ///
    /// A prefix returns false to suppress the original, so success is false and failure is true.
    /// </summary>
    [HarmonyPatch(typeof(WITab_Terrain), "FillTab")]
    public static class Patch_WITabTerrain_FillTab
    {
        public static bool Prefix(WITab_Terrain __instance)
        {
            if (!Enabled())
                return true;

            PlanetTile tile = Find.WorldSelector != null ? Find.WorldSelector.SelectedTile : PlanetTile.Invalid;

            if (!tile.Valid)
                return true;

            Vector2 size = SizeOf(__instance);

            if (size.x <= 0f || size.y <= 0f)
                return true;

            return !UIGuard.TryOnce("WorldTile.Tab",
                () => WorldTilePanel.Draw(new Rect(Vector2.zero, size), tile),
                "The world map's terrain tab is drawn RimWorld's own way for the rest of this session.");
        }

        /// <summary>
        /// The tab's own size.
        ///
        /// <c>InspectTabBase.size</c> is protected, and it is what vanilla lays this tab out against, so
        /// it is read rather than restated: a copied 440 by 540 would be a number that silently stops
        /// matching the day Ludeon changes theirs.
        /// </summary>
        private static readonly FieldInfo SizeField = AccessTools.Field(typeof(InspectTabBase), "size");

        private static Vector2 SizeOf(WITab_Terrain tab)
        {
            return UIGuard.Try("WorldTile.Size",
                () => SizeField == null ? Vector2.zero : (Vector2) SizeField.GetValue(tab),
                Vector2.zero, null);
        }

        /// <summary>
        /// Whether the player wants it.
        ///
        /// Shares the switch the rest of the inspector rework uses: somebody who turned that off asked for
        /// RimWorld's inspect panes, and this is one of them.
        /// </summary>
        private static bool Enabled()
        {
            return UIGuard.Try("WorldTile.Setting", () =>
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                return settings == null || settings.richInspectPane;
            }, true, null);
        }
    }
}
