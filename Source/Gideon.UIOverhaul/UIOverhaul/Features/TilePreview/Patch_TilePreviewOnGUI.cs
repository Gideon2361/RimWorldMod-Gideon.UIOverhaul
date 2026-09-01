using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld.Planet;

namespace Gideon.UIOverhaul.Features.TilePreview
{
    /// <summary>
    /// Draws the tile preview over the world map.
    ///
    /// <b>A postfix on the world corner rather than a patch of its own.</b> That method is already the mod's
    /// once-a-frame hook on the world screen, it runs at the right point in the draw order, and a postfix is
    /// unaffected by whether our own corner is drawing or has stood down.
    ///
    /// <b>Retired on its first failure.</b> The panel draws a texture inside a group; a throw partway through
    /// one leaves Unity's clip stack unbalanced for everything drawn after it, and repeating that every frame
    /// would be a broken world map rather than a missing preview.
    /// </summary>
    [HarmonyPatch(typeof(WorldGlobalControls), nameof(WorldGlobalControls.WorldGlobalControlsOnGUI))]
    public static class Patch_WorldGlobalControls_TilePreview
    {
        public static void Postfix()
        {
            if (!Enabled())
                return;

            UIGuard.TryOnce("TilePreview.Draw", TilePreviewPanel.Draw,
                "The world map's tile preview is switched off for the rest of this session. Nothing else on "
                + "the planet is affected.");
        }

        /// <summary>
        /// Whether the player wants it.
        ///
        /// <b>It has a switch because the noise chain is copied from a generation step.</b> That step is
        /// Ludeon's to change between versions, and if it drifts the preview is wrong rather than broken --
        /// which is the failure that gets noticed late. A player who can see it is wrong needs to be able to
        /// turn it off without removing the mod.
        /// </summary>
        private static bool Enabled()
        {
            return UIGuard.Try("TilePreview.Setting", () =>
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                return settings == null || settings.showTilePreview;
            }, true, null);
        }
    }
}
