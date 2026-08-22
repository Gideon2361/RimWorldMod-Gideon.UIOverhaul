using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Mining
{
    /// <summary>
    /// Paints every ore vein on the map while the mine designator is up.
    ///
    /// <b>Asked for on 2026-08-22,</b> with Mine Sight named as the reference. The problem it answers is real and
    /// entirely visual: ore veins are drawn as rock with a slightly different texture, and on a mountain map at
    /// anything but full zoom they are indistinguishable from the granite around them. Players end up hovering
    /// cell by cell reading the tooltip, or mining a whole face to find the compacted steel in it.
    ///
    /// <b>Only while the mine designator is selected,</b> which is Mine Sight's rule and the right one: this is
    /// an answer to "where should I dig", so it belongs on screen exactly while that question is being asked. An
    /// always-on overlay would be a second colour scheme covering every mountain on the map for the whole game.
    ///
    /// <b>Coloured by what the vein yields</b> rather than one colour for all ore, because the question is rarely
    /// "is this anything" and usually "is this the plasteel". The colour is the mined thing's own, so gold reads
    /// gold and jade reads jade with nothing to learn.
    ///
    /// <b>Drawn with RimWorld's own <c>CellBoolDrawer</c>,</b> the same machinery behind the fertility and roof
    /// overlays: one mesh for the whole map, regenerated only when the contents change, rather than a draw call
    /// per cell. A map component because a drawer belongs to a map and RimWorld builds one of these per map with
    /// no registration needed.
    /// </summary>
    internal class MapComponent_MineableOverlay : MapComponent
    {
        /// <summary>
        /// How faint the wash is.
        ///
        /// Below vanilla's own 0.33 default, because this covers whole mountain faces rather than the handful of
        /// cells a fertility overlay lights up: at a third opacity a stone wall stops reading as a stone wall.
        /// </summary>
        private const float Opacity = 0.24f;

        /// <summary>
        /// How long a drawn overlay may be stale, in real seconds.
        ///
        /// <b>Real time rather than ticks,</b> because designating happens while paused as often as not and a
        /// tick timer would leave the overlay frozen exactly then. Two seconds is under the time it takes to
        /// notice a mined cell still shaded, and the cost is one map walk, only while the designator is up.
        /// </summary>
        private const float Refresh = 2f;

        private CellBoolDrawer drawer;

        private float refreshedAt = -999f;

        public MapComponent_MineableOverlay(Map map) : base(map)
        {
        }

        public override void MapComponentUpdate()
        {
            UIGuard.Try("Mining.Overlay", Draw,
                "The mineable overlay is not being drawn. Mining itself is unaffected.");
        }

        private void Draw()
        {
            if (!Wanted())
                return;

            if (drawer == null)
            {
                drawer = new CellBoolDrawer(Ore, Tint, CellTint, map.Size.x, map.Size.z, Opacity);

                refreshedAt = -999f;
            }

            // The map changes under the overlay as pawns mine, so it is rebuilt on a timer while it is up. Marked
            // dirty rather than rebuilt here: the drawer regenerates on its own next draw, once, however many
            // times this is called in a frame.
            if (Time.realtimeSinceStartup - refreshedAt > Refresh)
            {
                refreshedAt = Time.realtimeSinceStartup;

                drawer.SetDirty();
            }

            drawer.MarkForDraw();
            drawer.CellBoolDrawerUpdate();
        }

        /// <summary>
        /// Whether the overlay belongs on screen right now.
        ///
        /// <b>Both mine designators count.</b> Vanilla has the ordinary one and the vein one, and somebody using
        /// either is asking the same question. Anything else selected, or nothing, and the overlay is gone.
        /// </summary>
        private bool Wanted()
        {
            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

            if (settings == null || !settings.showMineableOverlay)
                return false;

            if (map == null || Find.CurrentMap != map)
                return false;

            Designator selected = Find.DesignatorManager?.SelectedDesignator;

            return selected is Designator_Mine || selected is Designator_MineVein;
        }

        /// <summary>
        /// Whether this cell holds something worth pointing at.
        ///
        /// <b><c>isResourceRock</c> is the test, which leaves plain stone out.</b> Granite is mineable and is not
        /// what anybody is looking for: shading every wall on a mountain map would be the same as shading none of
        /// them. A modded vein is included on the same terms as vanilla's, since the flag is the def's own claim
        /// about what it is.
        /// </summary>
        private bool Ore(int index)
        {
            if (map == null || index < 0 || index >= map.cellIndices.NumGridCells)
                return false;

            Building edifice = map.edificeGrid[index];

            return edifice?.def?.building != null && edifice.def.building.isResourceRock
                   && !map.fogGrid.IsFogged(edifice.Position);
        }

        /// <summary>
        /// The overall tint, which the per cell colour is drawn against.
        ///
        /// White, so a vein's own colour arrives unchanged: the drawer multiplies the two, and anything else here
        /// would shift every ore towards one hue.
        /// </summary>
        private Color Tint()
        {
            return Color.white;
        }

        private Color CellTint(int index)
        {
            return UIGuard.Try("Mining.OreColor", () =>
            {
                Building edifice = map?.edificeGrid?[index];
                ThingDef yield = edifice?.def?.building?.mineableThing;

                if (yield == null)
                    return Color.white;

                // The material's own colour where it has one, which is what makes gold read as gold. Steel,
                // plasteel, gold, silver, jade and uranium all carry one; anything else falls back to its icon
                // colour and then to white rather than being left invisible.
                if (yield.stuffProps != null && yield.stuffProps.color != default(Color))
                    return yield.stuffProps.color;

                if (yield.graphicData != null && yield.graphicData.color != default(Color))
                    return yield.graphicData.color;

                return Color.white;
            }, Color.white, null);
        }
    }
}
