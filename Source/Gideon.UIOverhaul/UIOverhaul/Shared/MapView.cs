using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Gideon.UIOverhaul.Shared
{
    /// <summary>
    /// Whether a map is what the player is actually looking at.
    ///
    /// <b>Not the same question as whether a map is current, and the difference has cost three bugs.</b> The world
    /// view leaves <c>Find.CurrentMap</c> set, so every "is there a map?" check passes while the planet fills the
    /// screen. On 2026-08-21 that put the minimap over the planet, filled the colonist bar with striped garbage
    /// because the tile cameras rendered a map nothing had drawn, and painted room labels across the world map.
    /// Three features, one wrong question, fixed three times before being asked once here.
    ///
    /// <b>Two tests, because there are two ways to be looking at something else.</b> <c>DrawingMap</c> is false on
    /// the planet, and a gravship cutscene shows the planet while leaving it true -- the mode is Background or None
    /// throughout, since a map is notionally still being drawn.
    ///
    /// <b>Screenshot mode is deliberately not folded in.</b> It answers a different question -- whether interface
    /// should appear in a picture the game is composing -- and applies to the minimap but not to world-space
    /// geometry like the floor labels, which are part of the picture.
    /// </summary>
    internal static class MapView
    {
        /// <summary>Whether a map, rather than the planet or a cutscene, is on screen.</summary>
        internal static bool OnScreen =>
            WorldRendererUtility.DrawingMap && !WorldComponent_GravshipController.CutsceneInProgress;

        /// <summary>
        /// Whether this particular map is the one on screen.
        ///
        /// For the hooks that run per map: <c>Map.MapUpdate</c> is called for every loaded map every frame, and
        /// only one of them is being drawn.
        /// </summary>
        internal static bool Showing(Map map)
        {
            return OnScreen && map != null && map == Find.CurrentMap;
        }
    }
}
