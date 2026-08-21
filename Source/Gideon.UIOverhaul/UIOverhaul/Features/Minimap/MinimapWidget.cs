using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Notifications;
using Gideon.UIOverhaul.Features.Options;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Minimap
{
    /// <summary>Which corner the minimap sits in.</summary>
    public enum MinimapCorner
    {
        BottomLeft,
        BottomRight,
        TopLeft,
        TopRight
    }

    /// <summary>How big the map area is drawn.</summary>
    public enum MinimapSize
    {
        Small,
        Medium,
        Large
    }

    /// <summary>
    /// The minimap panel: a baked picture of the map with the people and the camera drawn over it.
    ///
    /// <b>Three things update at three different rates, and that is the whole design.</b> The ground is rebaked
    /// every few seconds by <see cref="MinimapImage"/>. The people are rebuilt four times a second by
    /// <see cref="MinimapMarkers"/>. The camera rectangle is read every frame, because it is one property and
    /// because anything slower visibly stutters while you drag the view around. Collapsing these into one rate
    /// would mean either a stuttering rectangle or a colony rebuilt sixty times a second.
    ///
    /// <b>Everything drawn here respects fog.</b> Unexplored ground is drawn as nothing and pawns standing in
    /// it are not listed, so the panel never tells you something the colony has not seen.
    /// </summary>
    internal static class MinimapWidget
    {
        private const float HeaderHeight = 24f;
        private const float Inset = 6f;

        /// <summary>How far the panel sits from the screen edges it is docked against.</summary>
        private const float ScreenMargin = 16f;

        /// <summary>
        /// Room kept clear at the bottom of the screen for RimWorld's own main button row.
        ///
        /// Asked of the def rather than written as a number, so the panel keeps clearing it if the game or
        /// another mod changes that row's height.
        /// </summary>
        private static float BottomBar => MainButtonDef.ButtonHeight + 6f;

        private static readonly Color32 ColonistColor = new Color32(115, 191, 255, 255);
        private static readonly Color32 DownedColor = new Color32(155, 114, 217, 255);
        private static readonly Color32 HostileColor = new Color32(229, 77, 51, 255);
        private static readonly Color32 AnimalColor = new Color32(204, 166, 51, 255);

        private static bool collapsed;

        /// <summary>Whether the header is being dragged, and where it was grabbed relative to the panel.</summary>
        private static bool dragging;

        private static Vector2 dragOffset;

        /// <summary>
        /// Draws the panel, if it is switched on and there is a map to draw.
        ///
        /// Guarded here rather than at the patch, so a failure costs the minimap and leaves the rest of the map
        /// interface drawing.
        /// </summary>
        internal static void Draw()
        {
            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

            if (settings == null || !settings.showMinimapWidget)
                return;

            Map map = Find.CurrentMap;

            if (map == null)
                return;

            // The world view keeps the last map current, so a null check is not enough to know a map is being
            // looked at. RimWorld's own MapInterfaceOnGUI_AfterMainTabs guards its whole body on exactly this, but
            // a postfix runs whether or not that body did, so the minimap inherited none of it and kept drawing a
            // corner of the colony over the planet.
            //
            // Screenshot mode is filtered for the same reason vanilla filters it: a HUD panel is the thing that
            // should not appear in a picture of the colony.
            if (!WorldRendererUtility.DrawingMap || Find.UIRoot.screenshotMode.FiltersCurrentEvent)
                return;

            Rect panel = PanelRect(map, settings);

            UIGuardedPanel.Draw("Minimap", panel, () => Contents(panel, map, settings),
                "The minimap is not drawing. Nothing else is affected.");

            // Once a frame, and cheap: a walk of one entry per loaded map. Here rather than on a timer of its
            // own because this is the only place that knows the minimap is still alive.
            MinimapImage.Prune();
        }

        /// <summary>
        /// Where the whole panel sits, from the chosen corner.
        ///
        /// <b>The bottom corners clear the main button row.</b> Docking flush to the bottom would put the panel
        /// under the architect and work buttons, which is both unreadable and unclickable.
        /// </summary>
        private static Rect PanelRect(Map map, UIOverhaulSettingsFile settings)
        {
            float side = SideOf(settings.minimapSize);
            float width = side + Inset * 2f;
            float height = HeaderHeight + (collapsed ? 0f : side + Inset * 2f);

            bool right = settings.minimapCorner == MinimapCorner.BottomRight
                         || settings.minimapCorner == MinimapCorner.TopRight;

            bool bottom = settings.minimapCorner == MinimapCorner.BottomLeft
                          || settings.minimapCorner == MinimapCorner.BottomRight;

            // A position the player dragged to wins outright. The corner is then only what the panel started
            // from and what Reset puts it back to, which is why dragging does not have to clear it.
            if (settings.minimapX >= 0f && settings.minimapY >= 0f)
            {
                return Onscreen(new Rect(settings.minimapX, settings.minimapY, width, height));
            }

            float x = right ? UI.screenWidth - width - ScreenMargin : ScreenMargin;
            float y;

            if (!bottom)
            {
                y = ScreenMargin;
            }
            else if (right)
            {
                // <b>Above this mod's corner widgets, not merely above the button bar.</b> The clock, date,
                // weather and conditions readouts own the bottom right, and their height is not a number
                // anybody can know in advance: it depends on which are switched on, how many game conditions
                // are running, and what other mods have added. The panel reports where it ended, so the
                // minimap stacks on that answer rather than on a guess that breaks the first time a readout
                // is toggled.
                y = NotificationLayout.BottomRightTop - height - 6f;
            }
            else
            {
                y = UI.screenHeight - height - ScreenMargin - BottomBar;
            }

            // Never off the top, which a tall minimap over a tall corner panel on a short screen would
            // otherwise be.
            y = Mathf.Max(ScreenMargin, y);

            return new Rect(x, y, width, height);
        }

        /// <summary>
        /// Holds a rectangle inside the screen.
        ///
        /// <b>Applied to a dragged position on every draw, not only while dragging.</b> A panel parked against
        /// the right edge on a wide monitor is off the screen entirely at a smaller resolution, and a saved
        /// position that can strand the panel somewhere unreachable is worse than one that cannot be saved.
        /// </summary>
        private static Rect Onscreen(Rect rect)
        {
            // The header alone is enough to grab it by, so the panel may hang off the bottom as long as the
            // bar that drags it is reachable.
            float x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, UI.screenWidth - rect.width));
            float y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, UI.screenHeight - HeaderHeight));

            return new Rect(x, y, rect.width, rect.height);
        }

        /// <summary>
        /// Dragging the header moves the panel.
        ///
        /// <b>The header only, matching every other window in this mod.</b> Dragging from anywhere would fight
        /// the map area, where a press already means "jump the camera there", which is the whole reason
        /// window dragging was confined to title bars in the first place.
        ///
        /// <b>Written to settings when the drag ends, not while it runs.</b> Saving on every mouse move would
        /// rewrite the config file dozens of times a second.
        /// </summary>
        private static void HandleDrag(Rect header, Rect panel, UIOverhaulSettingsFile settings)
        {
            Event current = Event.current;

            if (current == null)
                return;

            if (current.type == EventType.MouseDown && current.button == 0 && header.Contains(current.mousePosition))
            {
                dragging = true;
                dragOffset = current.mousePosition - new Vector2(panel.x, panel.y);

                current.Use();

                return;
            }

            if (!dragging)
                return;

            if (current.type == EventType.MouseDrag)
            {
                Vector2 moved = current.mousePosition - dragOffset;

                Rect held = Onscreen(new Rect(moved.x, moved.y, panel.width, panel.height));

                settings.minimapX = held.x;
                settings.minimapY = held.y;

                current.Use();

                return;
            }

            if (current.type == EventType.MouseUp || current.rawType == EventType.MouseUp)
            {
                dragging = false;

                UIGuard.Try("Minimap.SavePosition", settings.Save, null);
            }
        }

        /// <summary>
        /// The side of the map area.
        ///
        /// <b>Whole pixels only.</b> The baked picture is blitted into this square, and a fractional side puts its
        /// edge between pixels, which shows as a seam along one side that moves as the panel is dragged. That is
        /// why these are three whole numbers rather than one number and a multiplier.
        /// </summary>
        private static float SideOf(MinimapSize size)
        {
            switch (size)
            {
                case MinimapSize.Small: return 160f;
                case MinimapSize.Large: return 300f;
                default: return 220f;
            }
        }

        private static void Contents(Rect panel, Map map, UIOverhaulSettingsFile settings)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Widgets.DrawBoxSolid(panel, palette.PanelBackground);
            UIElementPainter.OutlineRounded(panel, palette.Border, palette.PanelBackground);

            Rect header = new Rect(panel.x, panel.y, panel.width, HeaderHeight);
            DrawHeader(header, map, palette);

            // Before the map area claims the press. The header and the map never overlap, but the drag has to
            // see MouseUp anywhere on screen to finish, so it runs whether or not the cursor is still on it.
            HandleDrag(header, panel, settings);

            if (collapsed)
                return;

            // Exactly the map, with an even inset all round. The panel used to carry a footer saying "click
            // to jump", which told you once what the panel teaches you the first time you click it, and cost
            // eighteen pixels of every frame after that.
            // Derived from the panel rather than from the size setting, so the two cannot disagree: the panel
            // is already an inset either side of the map, which makes its width the map's own.
            Rect body = new Rect(panel.x + Inset, header.yMax + Inset,
                panel.width - Inset * 2f, panel.width - Inset * 2f);

            DrawMap(body, map, palette, settings);
        }

        private static void DrawHeader(Rect header, Map map, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(header, palette.SurfaceRaised);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextSecondary;

                Rect label = new Rect(header.x + 8f, header.y, Mathf.Max(0f, header.width - 34f),
                    header.height);

                if (label.width >= 24f)
                {
                    Widgets.LabelEllipses(label,
                        MapLabels.NameOf(map) + "  " + map.Size.x + " x " + map.Size.z);
                }

                Rect toggle = new Rect(header.xMax - 22f, header.y + 4f, 16f, 16f);

                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Mouse.IsOver(toggle) ? palette.TextPrimary : palette.TextDisabled;
                Widgets.Label(toggle, collapsed ? "+" : "-");

                if (Widgets.ButtonInvisible(toggle))
                {
                    collapsed = !collapsed;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// The picture, the people, and where the camera is looking.
        ///
        /// <b>The fitted rectangle is computed rather than left to ScaleMode.</b> Every marker and every click
        /// has to convert between cells and pixels, and that arithmetic needs the exact rectangle the texture
        /// landed in. Handing the job to ScaleToFit would letterbox the texture somewhere this code could not
        /// see, and the dots would sit next to the colony instead of on it.
        /// </summary>
        private static void DrawMap(Rect body, Map map, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            Texture2D texture = MinimapImage.For(map);

            if (texture == null)
                return;

            IntVec3 size = map.Size;

            if (size.x <= 0 || size.z <= 0)
                return;

            float scale = Mathf.Min(body.width / size.x, body.height / size.z);
            float width = size.x * scale;
            float height = size.z * scale;

            Rect fitted = new Rect(
                body.x + (body.width - width) * 0.5f,
                body.y + (body.height - height) * 0.5f,
                width, height);

            Widgets.DrawBoxSolid(fitted, new Color(0f, 0f, 0f, 1f));

            // <b>Forced to white, and not as a formality.</b> GUI.DrawTexture multiplies the texture by
            // GUI.color, and this draws from a postfix on RimWorld's own map interface where the colour on
            // entry belongs to whatever drew last. A tint left behind by the game or another mod would darken
            // the whole picture while the markers and text, which set their own colours, stayed correct --
            // which is exactly what a broken minimap looks like and nothing like what it points at.
            Color previousColor = GUI.color;
            GUI.color = Color.white;

            // StretchToFill into a rectangle we worked out ourselves, for the reason in the summary.
            GUI.DrawTexture(fitted, texture, ScaleMode.StretchToFill);

            GUI.color = previousColor;

            // <b>An outline, not OutlineRounded.</b> That helper fills the whole rect with the border colour
            // and then fills the inside with the second colour to punch the middle back out, so it needs an
            // opaque inside. Passing Color.clear drew nothing for the second step and left the first one
            // standing: a solid sheet of border grey over the entire map. Everything under it -- terrain,
            // mountains, the colony, the line between explored and fogged -- was drawn correctly and then
            // painted over, which is why every one of those looked equally missing.
            GUI.color = palette.Border;
            Widgets.DrawBox(fitted);
            GUI.color = previousColor;

            DrawMarkers(fitted, map, size, settings);
            DrawViewRect(fitted, size);
            HandleClicks(fitted, map, size);
        }

        /// <summary>
        /// The people, over the ground.
        ///
        /// <b>Hostiles are filtered here rather than left out of the cache.</b> The marker list is rebuilt four
        /// times a second, so filtering at the source would leave the switch taking up to a quarter of a second
        /// to visibly do anything. Filtering at the draw makes it immediate, and costs one comparison per
        /// marker.
        /// </summary>
        private static void DrawMarkers(Rect fitted, Map map, IntVec3 size, UIOverhaulSettingsFile settings)
        {
            List<MinimapMarker> markers = MinimapMarkers.For(map);

            bool enemies = settings.showMinimapEnemies;

            // Never smaller than two pixels. On a 400 cell map inside a 220 pixel panel one cell is half a
            // pixel, and a colonist drawn at true scale would be invisible -- which is the one thing a marker
            // may not be.
            float dot = Mathf.Max(2f, fitted.width / size.x * 1.5f);

            for (int i = 0; i < markers.Count; i++)
            {
                MinimapMarker marker = markers[i];

                if (!enemies && marker.Kind == MinimapMarkerKind.Hostile)
                    continue;

                Vector2 point = CellToPixel(fitted, size, marker.X, marker.Z);

                Widgets.DrawBoxSolid(
                    new Rect(point.x - dot * 0.5f, point.y - dot * 0.5f, dot, dot),
                    ColorOf(marker.Kind));
            }
        }

        private static Color ColorOf(MinimapMarkerKind kind)
        {
            switch (kind)
            {
                case MinimapMarkerKind.Hostile: return HostileColor;
                case MinimapMarkerKind.Animal: return AnimalColor;
                case MinimapMarkerKind.Downed: return DownedColor;
                default: return ColonistColor;
            }
        }

        /// <summary>
        /// The rectangle showing what the camera is looking at.
        ///
        /// Read every frame, unlike everything else here. It is a single property and it has to track a drag
        /// smoothly; on a timer it would lag behind the view in exactly the moment somebody is using it.
        /// </summary>
        private static void DrawViewRect(Rect fitted, IntVec3 size)
        {
            CameraDriver camera = Find.CameraDriver;

            if (camera == null)
                return;

            CellRect view = camera.CurrentViewRect;

            Vector2 min = CellToPixel(fitted, size, view.minX, view.minZ);
            Vector2 max = CellToPixel(fitted, size, view.maxX, view.maxZ);

            Rect outline = new Rect(
                Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y),
                Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y));

            // Clamped, because the camera can look past the edge of the map and an unclamped rectangle would
            // draw outside the panel and over whatever is beside it.
            outline = Clamp(outline, fitted);

            Color previous = GUI.color;

            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            Widgets.DrawBox(outline);

            GUI.color = previous;
        }

        private static Rect Clamp(Rect rect, Rect within)
        {
            float x = Mathf.Max(rect.x, within.x);
            float y = Mathf.Max(rect.y, within.y);
            float xMax = Mathf.Min(rect.xMax, within.xMax);
            float yMax = Mathf.Min(rect.yMax, within.yMax);

            return new Rect(x, y, Mathf.Max(0f, xMax - x), Mathf.Max(0f, yMax - y));
        }

        /// <summary>
        /// Clicking or dragging moves the camera there.
        ///
        /// <b>Raw events rather than ButtonInvisible.</b> A button reports a completed click, and dragging the
        /// view around never completes one, the same reason the schedule strip reads the mouse directly. The
        /// event is consumed either way, so a click meant for the minimap never also lands on the map behind it.
        /// </summary>
        private static void HandleClicks(Rect fitted, Map map, IntVec3 size)
        {
            Event current = Event.current;

            if (current == null || !fitted.Contains(current.mousePosition))
                return;

            bool pressed = current.type == EventType.MouseDown && current.button == 0;
            bool dragged = current.type == EventType.MouseDrag && current.button == 0;

            if (!pressed && !dragged)
                return;

            IntVec3 cell = PixelToCell(fitted, size, current.mousePosition);

            if (!cell.InBounds(map))
                return;

            Find.CameraDriver?.JumpToCurrentMapLoc(cell);

            current.Use();
        }

        /// <summary>
        /// A cell's centre, in screen pixels.
        ///
        /// <b>The z axis is flipped and the texture's is not.</b> Map z counts north from the south edge and
        /// screen y counts down from the top, so a marker's y is measured from the bottom of the rectangle.
        /// The texture needs no such flip: Unity fills row zero at the bottom, which is where map z zero
        /// belongs, so the picture and the markers agree without either being adjusted for the other.
        /// </summary>
        private static Vector2 CellToPixel(Rect fitted, IntVec3 size, int x, int z)
        {
            return new Vector2(
                fitted.x + (x + 0.5f) / size.x * fitted.width,
                fitted.yMax - (z + 0.5f) / size.z * fitted.height);
        }

        private static IntVec3 PixelToCell(Rect fitted, IntVec3 size, Vector2 point)
        {
            int x = Mathf.FloorToInt((point.x - fitted.x) / fitted.width * size.x);
            int z = Mathf.FloorToInt((fitted.yMax - point.y) / fitted.height * size.z);

            return new IntVec3(Mathf.Clamp(x, 0, size.x - 1), 0, Mathf.Clamp(z, 0, size.z - 1));
        }

        /// <summary>Drops everything held, for a game ending.</summary>
        internal static void Clear()
        {
            MinimapImage.Clear();
            MinimapMarkers.Clear();
            collapsed = false;
        }
    }
}
