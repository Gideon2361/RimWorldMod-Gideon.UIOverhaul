using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.ColonyBar
{
    /// <summary>
    /// How often live tiles are redrawn. The values are real-time intervals, not tick counts.
    ///
    /// <b>Real time rather than ticks, and the numbers are why.</b> <c>GenTicks.TicksPerRealSecond</c> is 60, but
    /// <c>TickManager.TickRateMultiplier</c> returns 1 at normal speed, 3 at Fast and 12 at Superfast when little
    /// is happening. A tick-paced interval would therefore render up to twelve times more often while
    /// fast-forwarding, which is exactly when the simulation is already spending the most CPU per real second. The
    /// setting exists to bound work per real second, so real time is its unit. The minimap's marker cache is paced
    /// the same way and for the same reason.
    /// </summary>
    public enum PawnViewRefresh
    {
        Ms500,
        Ms250,
        Ms125,
        Ms50,
        EveryFrame
    }

    /// <summary>
    /// Live camera views of pawns, one small render target each, for the colonist bar's tiles.
    ///
    /// <b>RimWorld draws nothing outside the camera, so this cannot simply point a camera somewhere.</b> Terrain
    /// sections are culled in <c>MapDrawer.DrawMapMesh</c> and things in
    /// <c>DynamicDrawManager.ComputeCulledThings</c>, both against <c>Find.CameraDriver.CurrentViewRect</c>. A
    /// second camera aimed at a pawn across the map would render bare ground, because nothing there was ever
    /// submitted for drawing.
    ///
    /// <b>The way through is the one the engine already uses.</b> Both of those cullers widen their rect when
    /// <c>WorldComponent_GravshipController.GravshipRenderInProgess</c> is set, which is how 1.6 renders a gravship
    /// that is not on screen. This does the same thing for its own regions: see
    /// <see cref="Patch_TileCulling"/>, which widens the view rect for the duration of <c>Map.MapUpdate</c> and
    /// nothing else, so only those two cullers ever see the wider rect.
    ///
    /// <b>Current map only, and that is an engine limit rather than a decision.</b> The drawing block in
    /// <c>Map.MapUpdate</c> is guarded by <c>Find.CurrentMap == this</c>, so no other map is drawn at all. A pawn
    /// somewhere else has nothing to render and falls back to their portrait, which is also why the tile carries a
    /// map badge.
    ///
    /// <b>One frame of latency, on purpose.</b> The regions have to be known before <c>Map.MapUpdate</c> runs,
    /// and which tiles are due is worked out while the bar draws, which is afterwards. So each frame widens the
    /// rect for the tiles chosen at the end of the previous one. Nobody can see a frame.
    /// </summary>
    internal static class PawnTileView
    {
        /// <summary>
        /// Render target size. Taller than wide, because a pawn is.
        ///
        /// <b>The aspect lives here rather than in the drawing.</b> A camera rendering to a texture takes its
        /// aspect from that texture, so this is what decides how wide the view is for a given zoom. A square
        /// target spent a third of every tile on ground either side of the pawn.
        /// </summary>
        private const int ResolutionX = 96;

        private const int ResolutionY = 124;

        /// <summary>
        /// Half the view's height in cells, which is what an orthographic camera's size means.
        ///
        /// <b>Framed on the pawn, not on the room.</b> 1.0 shows two cells top to bottom and about one and a half
        /// across, so a colonist fills most of the tile. Tightened twice on 2026-08-21: 3.5 rendered them as a
        /// speck and 1.75 still read as a patch of floor with somebody in it.
        ///
        /// <b>Not tighter than this.</b> A humanlike's drawn sprite overflows its cell -- headgear especially --
        /// and two cells of height is what keeps a hat inside the frame rather than cropped at the top.
        /// </summary>
        private const float OrthoSize = 1f;

        /// <summary>
        /// Cells drawn around the pawn, as a radius, which is deliberately wider than the camera sees.
        ///
        /// <b>The margin is not slack.</b> This is what gets added to the cull rect, and the camera is centred on
        /// <c>DrawPos</c> while the region is centred on <c>Position</c> -- up to half a cell apart, more while a
        /// pawn is walking. A region only as large as the view would show undrawn black along one edge as they
        /// move. Two covers five by five against a view of three and a half by three.
        /// </summary>
        private const int Radius = 2;

        /// <summary>A tile unused for this long is released, so folding a group eventually frees its memory.</summary>
        private const float IdleSeconds = 6f;

        private sealed class Tile
        {
            internal RenderTexture Texture;
            internal float RenderedAt;
            internal float TouchedAt;
        }

        private static readonly Dictionary<Pawn, Tile> Tiles = new Dictionary<Pawn, Tile>();

        /// <summary>Pawns wanted this frame, in bar order, refilled by the bar before it draws them.</summary>
        private static readonly List<Pawn> Wanted = new List<Pawn>();

        /// <summary>Regions the next <c>Map.MapUpdate</c> must draw, chosen at the end of this frame.</summary>
        private static readonly List<CellRect> Pending = new List<CellRect>();

        /// <summary>
        /// The pawns whose regions <see cref="Pending"/> currently names, and so the ones that may be rendered on
        /// the next frame rather than this one.
        ///
        /// <b>This list is the frame of latency,</b> and it has to exist rather than be implied: the cull rect is
        /// read during Update and the cameras run during OnGUI, so the set drawn for and the set rendered are one
        /// frame apart by construction.
        /// </summary>
        private static readonly List<Pawn> Scheduled = new List<Pawn>();

        private static readonly List<Pawn> Expired = new List<Pawn>();

        private static Camera camera;

        private static int cursor;

        private static int lastFrame = -1;

        /// <summary>The regions <see cref="Patch_TileCulling"/> should widen the view rect to include.</summary>
        internal static List<CellRect> PendingRegions => Pending;

        /// <summary>Whether the feature is on and able to run at all.</summary>
        internal static bool Enabled
        {
            get
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                return settings != null && settings.showGroupedColonistBar && settings.livePawnView;
            }
        }

        /// <summary>Seconds between refreshes of one tile, or zero for every frame.</summary>
        private static float Interval
        {
            get
            {
                switch (UIOverhaulSettingsFile.Current?.pawnViewRefresh ?? PawnViewRefresh.Ms250)
                {
                    case PawnViewRefresh.Ms500: return 0.5f;
                    case PawnViewRefresh.Ms125: return 0.125f;
                    case PawnViewRefresh.Ms50: return 0.05f;
                    case PawnViewRefresh.EveryFrame: return 0f;
                    default: return 0.25f;
                }
            }
        }

        /// <summary>
        /// Whether this pawn can have a live tile at all: the feature is on, a map is on screen, and they are on it.
        ///
        /// Asked before a tile is allocated rather than after a blank one is rendered, so a pawn in a caravan or on
        /// another map never holds a render target it cannot fill.
        /// </summary>
        internal static bool Possible(Pawn pawn)
        {
            // MapOnScreen rather than a current-map check alone: on the world view nothing has drawn the map this
            // frame, so a tile camera renders whatever was left in its target -- the striped garbage Aaron saw on
            // 2026-08-21. Shared with the minimap and the floor labels, which had the same fault.
            return Enabled && Shared.MapView.Showing(pawn?.Map) && pawn.Spawned;
        }

        /// <summary>Notes that this pawn's tile is on screen this frame. Call before <see cref="Refresh"/>.</summary>
        internal static void Want(Pawn pawn)
        {
            if (Possible(pawn))
                Wanted.Add(pawn);
        }

        /// <summary>
        /// The texture to draw for a pawn, or null when there is nothing ready and a portrait should be drawn.
        ///
        /// Null rather than a blank texture, because a blank tile reads as a bug and a portrait does not.
        /// </summary>
        internal static Texture GetTexture(Pawn pawn)
        {
            // Possible is asked again here, not just when scheduling. A tile that has been rendered keeps its
            // texture, so without this the bar would go on drawing the last picture of the map after the player
            // left for the world view -- a stale colony behind a planet, which is worse than a portrait because it
            // looks current.
            if (pawn == null || !Possible(pawn) || !Tiles.TryGetValue(pawn, out Tile tile))
                return null;

            return tile.RenderedAt > 0f ? tile.Texture : null;
        }

        /// <summary>
        /// Renders the tiles that are due, then chooses the ones for next frame.
        ///
        /// <b>Called once per frame, on repaint only.</b> OnGUI runs more than once per frame for layout and for
        /// each event, and rendering on every pass would multiply the cost by something the setting does not
        /// control.
        ///
        /// <b>How many run is derived from the interval rather than fixed.</b> Over one interval every visible tile
        /// must be covered, so this frame's share is <c>count * deltaTime / interval</c>. That keeps the cost per
        /// frame flat instead of refreshing every tile on the same frame, which would show as a periodic stutter
        /// four times a second rather than as a saving.
        /// </summary>
        internal static void Refresh()
        {
            // Cleared however this call ends, including the passes that render nothing. OnGUI runs several times
            // per frame -- a layout pass and one per event -- and every pass refills this list, so leaving it for
            // the repaint pass to clear would let it grow by a whole colony per event.
            try
            {
                bool repaint = Event.current == null || Event.current.type == EventType.Repaint;

                if (!repaint || lastFrame == Time.frameCount)
                    return;

                lastFrame = Time.frameCount;

                UIGuard.Try("Bar.TileRefresh", () =>
                {
                    Sweep();

                    if (!Enabled || Wanted.Count == 0)
                    {
                        Pending.Clear();
                        Scheduled.Clear();

                        return;
                    }

                    // Render first, schedule second, and the order is the whole fix for tiles that came out
                    // black. Scheduled holds the pawns chosen at the end of the previous frame, which are exactly
                    // the ones whose regions this frame's Map.MapUpdate was told to draw. Rendering the set chosen
                    // *now* aims cameras at ground nothing has drawn yet.
                    //
                    // The old code did both in one step, so a tile was only ever filled when the pawn happened to
                    // be inside the player's own camera view and got drawn regardless. That is why the symptom was
                    // intermittent and moved around as the view moved.
                    Render(Scheduled);
                    Schedule();
                }, null);
            }
            finally
            {
                Wanted.Clear();
            }
        }

        /// <summary>
        /// The tiles to render this frame, and the regions to widen the next map update by.
        ///
        /// Round-robin from a cursor that survives between frames, so every tile comes round in turn rather than
        /// the first few being refreshed repeatedly while the rest go stale.
        /// </summary>
        private static void Schedule()
        {
            float interval = Interval;
            float now = Time.realtimeSinceStartup;

            int budget = interval <= 0f
                ? Wanted.Count
                : Mathf.Clamp(Mathf.CeilToInt(Wanted.Count * Time.deltaTime / interval), 1, Wanted.Count);

            Scheduled.Clear();

            for (int i = 0; i < Wanted.Count && Scheduled.Count < budget; i++)
            {
                Pawn pawn = Wanted[(cursor + i) % Wanted.Count];
                Tile tile = Ensure(pawn);

                if (tile == null)
                    continue;

                // Never rendered wins outright: a tile with nothing in it is showing a portrait, and getting it
                // its first picture matters more than keeping another one current.
                bool blank = tile.RenderedAt <= 0f;

                if (blank || interval <= 0f || now - tile.RenderedAt >= interval)
                    Scheduled.Add(pawn);
            }

            cursor = Wanted.Count == 0 ? 0 : (cursor + Mathf.Max(1, budget)) % Wanted.Count;

            Pending.Clear();

            // Position rather than DrawPos, because a CellRect is centred on a cell. The camera uses DrawPos, and
            // Radius carries the margin between the two.
            foreach (Pawn pawn in Scheduled)
                Pending.Add(CellRect.CenteredOn(pawn.Position, Radius));
        }

        /// <summary>
        /// Points the camera at each due pawn and renders.
        ///
        /// <b>The main camera is the template, not a guess.</b> Copying its rotation, clear flags, culling mask and
        /// height means the tile is the same projection of the same world the player is already looking at, only
        /// re-centred and zoomed. Rebuilding those by hand is how a tile ends up lit differently from the map.
        /// </summary>
        private static void Render(List<Pawn> due)
        {
            if (due.Count == 0)
                return;

            Camera tile = Rig();
            Camera main = Find.Camera;

            if (tile == null || main == null)
                return;

            RenderTexture previous = RenderTexture.active;

            foreach (Pawn pawn in due)
            {
                if (!Possible(pawn) || !Tiles.TryGetValue(pawn, out Tile slot) || slot.Texture == null)
                    continue;

                Vector3 at = pawn.DrawPos;

                tile.transform.position = new Vector3(at.x, main.transform.position.y, at.z);
                tile.transform.rotation = main.transform.rotation;
                tile.orthographicSize = OrthoSize;
                tile.targetTexture = slot.Texture;

                tile.Render();

                slot.RenderedAt = Time.realtimeSinceStartup;
            }

            tile.targetTexture = null;
            RenderTexture.active = previous;
        }

        /// <summary>Builds the camera once, disabled so that it only ever renders when told to.</summary>
        private static Camera Rig()
        {
            if (camera != null)
                return camera;

            return UIGuard.Try("Bar.TileCamera", () =>
            {
                Camera main = Find.Camera;

                if (main == null)
                    return null;

                GameObject host = new GameObject("GideonPawnTileCamera", typeof(Camera));

                Object.DontDestroyOnLoad(host);

                Camera built = host.GetComponent<Camera>();

                built.CopyFrom(main);
                built.orthographic = true;

                // Disabled so Unity never renders it as part of the normal camera pass. Every render here is an
                // explicit Render call, which is what keeps the cost equal to the number of tiles refreshed.
                built.enabled = false;
                built.targetTexture = null;

                camera = built;

                return built;
            }, null, null);
        }

        private static Tile Ensure(Pawn pawn)
        {
            if (Tiles.TryGetValue(pawn, out Tile existing))
            {
                existing.TouchedAt = Time.realtimeSinceStartup;

                return existing;
            }

            return UIGuard.Try("Bar.TileAlloc", () =>
            {
                Tile made = new Tile
                {
                    Texture = new RenderTexture(ResolutionX, ResolutionY, 16, RenderTextureFormat.ARGB32),
                    TouchedAt = Time.realtimeSinceStartup
                };

                made.Texture.name = "GideonPawnTile";
                made.Texture.Create();

                Tiles.Add(pawn, made);

                return made;
            }, null, null);
        }

        /// <summary>
        /// Releases tiles nobody has asked about for a while, and everything at once when the feature goes off.
        ///
        /// A render target is real video memory, so a colony that folded every group should not still be holding
        /// forty of them. The delay stops a fold and immediate unfold from paying to allocate again.
        /// </summary>
        private static void Sweep()
        {
            if (Tiles.Count == 0)
                return;

            bool all = !Enabled;
            float now = Time.realtimeSinceStartup;

            Expired.Clear();

            foreach (KeyValuePair<Pawn, Tile> pair in Tiles)
            {
                if (all || pair.Key == null || pair.Key.Destroyed || now - pair.Value.TouchedAt > IdleSeconds)
                    Expired.Add(pair.Key);
            }

            foreach (Pawn pawn in Expired)
            {
                if (Tiles.TryGetValue(pawn, out Tile tile) && tile.Texture != null)
                {
                    tile.Texture.Release();
                    Object.Destroy(tile.Texture);
                }

                Tiles.Remove(pawn);
            }

            Expired.Clear();
        }
    }
}
