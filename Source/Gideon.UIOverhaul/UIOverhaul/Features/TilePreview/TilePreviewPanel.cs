using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.TilePreview
{
    /// <summary>
    /// Typefaces and sizes for the preview panel, on the same scale the restyled tabs count in.
    ///
    /// <b>The accent rather than an identity colour.</b> The per-tab colours say which screen you are on, and
    /// this is not a screen: it is a panel laid over the planet while the world map is what you are looking at.
    /// </summary>
    internal static class TilePreviewFaces
    {
        internal const UIFace Display = UIFace.Oswald;

        internal const UIFace Condensed = UIFace.BarlowCondensed;

        internal const UIFace Mono = UIFace.IBMPlexMono;

        internal static class Size
        {
            internal const float Title = 12.75f;

            internal const float Caption = 7.5f;

            /// <summary>The headline figure.</summary>
            internal const float Lead = 15f;

            internal const float Figure = 11.25f;

            internal const float Name = 9.75f;

            internal const float Note = 7.5f;
        }
    }

    /// <summary>
    /// The tile preview, drawn over the world map.
    ///
    /// <b>The picture answers the question and the figures caption it.</b> Biome, rainfall, growing period and
    /// average temperature are on the tile inspector already; repeating them here would make this a second copy
    /// of a screen the player is looking at. Every figure in the column is one that can only be known by reading
    /// the map that has not been generated yet, which is the whole reason this exists.
    ///
    /// <b>Top right, because every other corner is taken.</b> The inspect pane is bottom left, our own world
    /// controls are bottom right, and the tabs are along the bottom. Top right is the one place a panel this
    /// size does not cover something the player is using.
    ///
    /// <b>It follows the cursor rather than the selection.</b> Choosing a landing site means comparing tiles,
    /// and a preview that needs a click per tile turns a comparison into a sequence.
    /// </summary>
    internal static class TilePreviewPanel
    {
        private const float Margin = 8f;

        /// <summary>Side of the picture. The field is square, whatever the map size is.</summary>
        private const float ImageSize = 168f;

        private const float ColumnWidth = 158f;

        private const float Pad = 8f;

        private const float HeaderHeight = 22f;

        private const float RowHeight = 19f;

        private const float Gap = 6f;

        /// <summary>The strip along the bottom that says this reading is an estimate.</summary>
        private const float FooterHeight = 14f;

        /// <summary>The selection an analysis was last started for, so one click starts one generation.</summary>
        private static PlanetTile chosen = PlanetTile.Invalid;

        internal static float Width
        {
            get { return ImageSize + ColumnWidth + Pad * 3f; }
        }

        internal static float Height
        {
            get { return HeaderHeight + ImageSize + Pad * 2f + 2f + FooterHeight; }
        }

        internal static void Draw()
        {
            if (Event.current.type == EventType.Layout)
                return;

            Selection();

            TilePreviewJob.Advance();

            Harvest();

            // Pinned to the tile under analysis rather than the one under the cursor, so the player can watch
            // the thing they asked for instead of losing it the moment the mouse moves.
            PlanetTile tile = TilePreviewJob.Running && TilePreviewJob.Tile.Valid
                ? TilePreviewJob.Tile
                : Hovered();

            if (!tile.Valid)
                return;

            TilePreviewEntry entry = TilePreviewCache.For(tile);

            if (entry == null || !entry.Valid || entry.Texture == null)
                return;

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Rect rect = new Rect(UI.screenWidth - Width - Margin, Margin, Width, Height);

            // The panel is over the globe rather than over a surface, so it carries its own ground the way the
            // rest of the mod's over-the-map chrome does.
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.HudBackground);

            Rect head = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, HeaderHeight);

            Widgets.DrawBoxSolid(head, palette.SurfaceSunken);
            Widgets.DrawBoxSolid(new Rect(head.x, head.yMax, head.width, 1f), palette.Border);

            TabParts.RowLabel(new Rect(head.x + Pad, head.y, head.width * 0.55f, head.height),
                "If you settle here", palette.Accent, GameFont.Small, TilePreviewFaces.Display,
                TilePreviewFaces.Size.Title);

            if (!entry.Biome.NullOrEmpty())
            {
                Right(new Rect(head.x, head.y, head.width - Pad, head.height), entry.Biome,
                    palette.TextDisabled, TilePreviewFaces.Mono, TilePreviewFaces.Size.Caption);
            }

            Rect image = new Rect(rect.x + Pad, head.yMax + Pad, ImageSize, ImageSize);

            if (TilePreviewJob.Running && TilePreviewJob.Tile == tile)
            {
                TilePreviewZoom.Draw(image, entry.Texture, palette);
            }
            else
            {
                Widgets.DrawBoxSolid(image, palette.SurfaceSunken);
                GUI.DrawTexture(image, entry.Texture, ScaleMode.ScaleToFit);
                Widgets.DrawBox(image);
            }

            Figures(new Rect(image.xMax + Pad, image.y, ColumnWidth, image.height), entry.Reading, palette);

            Footer(new Rect(rect.x + Pad, image.yMax + Pad, rect.width - Pad * 2f, FooterHeight), palette,
                entry, tile);
        }

        /// <summary>
        /// Starts a true analysis when the player clicks a tile.
        ///
        /// <b>The selection is the click.</b> Watching <c>WorldSelector.SelectedTile</c> rather than reading
        /// mouse events means this agrees with whatever the world map decided a click was, including the ones
        /// that land on a world object rather than on bare ground.
        ///
        /// A tile already carrying a real answer is not generated twice; the cached one is what gets shown.
        /// </summary>
        private static void Selection()
        {
            PlanetTile selected = UIGuard.Try("TilePreview.Selected", () =>
            {
                WorldSelector selector = Find.WorldSelector;

                return selector != null ? selector.SelectedTile : PlanetTile.Invalid;
            }, PlanetTile.Invalid, null);

            if (selected == chosen)
                return;

            chosen = selected;

            if (selected.Valid && !TilePreviewCache.Analyzed(selected))
                TilePreviewJob.Start(selected);
        }

        /// <summary>
        /// Reads the finished map into the cache and lets it go.
        ///
        /// Done here rather than inside the job because it is the one part that touches Unity: the texture
        /// upload has to happen while the GUI is the thing running.
        /// </summary>
        private static void Harvest()
        {
            Map finished = TilePreviewJob.Finished;

            if (finished == null)
                return;

            PlanetTile analyzed = TilePreviewJob.Tile;

            TilePreviewReading reading;

            Texture2D texture = TilePreviewImage.RenderTrue(finished, out reading);

            TilePreviewCache.Replace(analyzed, texture, reading);

            TilePreviewJob.Complete();
        }

        /// <summary>
        /// The line that says this panel is a guess.
        ///
        /// <b>Because everything above it is an estimate and nothing about it looks like one.</b> The reading is
        /// rebuilt from the elevation and fertility step alone, so it knows nothing about the landmarks,
        /// mutators and mod added generation steps that carve a lake into a tile or drop a chasm through it.
        /// That gap does not announce itself: the arithmetic succeeds and returns a plausible map of a world
        /// that will not be generated, under a confident percentage in large type. A figure that can be wrong
        /// has to say so beside the figure, not in a changelog.
        /// </summary>
        private static void Footer(Rect rect, UIColorPaletteDef palette, TilePreviewEntry entry, PlanetTile tile)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 1f), palette.Border);

            bool analyzing = TilePreviewJob.Running && TilePreviewJob.Tile == tile;

            string text;
            Color tint;

            if (analyzing)
            {
                text = TilePreviewZoom.Caption();
                tint = palette.Accent;
            }
            else if (entry != null && entry.True)
            {
                text = "True analysis";
                tint = palette.Success;
            }
            else
            {
                text = "Click map tile for true analysis";
                tint = palette.TextDisabled;
            }

            Hint(new Rect(rect.x, rect.y + 1f, rect.width, rect.height - 1f), text, tint,
                TilePreviewFaces.Condensed, TilePreviewFaces.Size.Note);
        }

        private static void Hint(Rect rect, string text, Color color, UIFace face, float points)
        {
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
                GUI.color = color;

                UITextControl.LabelEllipses(rect, text, face, points);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }
        }

        /// <summary>
        /// The six figures, largest first.
        ///
        /// Water is absent on a dry tile rather than reading nought: a figure that is always nothing on almost
        /// every map is a row spent saying so.
        /// </summary>
        private static void Figures(Rect rect, TilePreviewReading reading, UIColorPaletteDef palette)
        {
            float y = rect.y;

            y = Lead(rect, y, reading.Buildable, palette);

            y = Row(rect, y, reading.LargestRun, "Largest run",
                reading.LargestRun < 25 ? palette.Warning : palette.TextPrimary, palette);

            y = Row(rect, y, reading.Mountain, "Under mountain",
                reading.Mountain >= 15 ? palette.Warning : palette.TextPrimary, palette);

            y = Row(rect, y, reading.Rock, "Minable rock", palette.TextPrimary, palette);

            y = Row(rect, y, reading.Fertile, "Fertile ground", palette.TextPrimary, palette);

            if (reading.Water > 0)
                y = Row(rect, y, reading.Water, "Water", palette.TextPrimary, palette);

            if (reading.Structures > 0)
                Row(rect, y, reading.Structures, "Structures", palette.TextPrimary, palette);
        }

        /// <summary>The headline: the share of the map that is neither stone nor water.</summary>
        private static float Lead(Rect rect, float y, int percent, UIColorPaletteDef palette)
        {
            Rect band = new Rect(rect.x, y, rect.width, 34f);

            TabParts.RowLabel(new Rect(band.x, band.y, band.width, 20f), percent + "%", palette.Accent,
                GameFont.Medium, TilePreviewFaces.Mono, TilePreviewFaces.Size.Lead);

            TabParts.RowLabel(new Rect(band.x, band.y + 19f, band.width, 14f), "BUILDABLE",
                palette.TextDisabled, GameFont.Tiny, TilePreviewFaces.Mono, TilePreviewFaces.Size.Caption);

            Widgets.DrawBoxSolid(new Rect(band.x, band.yMax + 2f, band.width, 1f), palette.Border);

            return band.yMax + Gap + 2f;
        }

        private static float Row(Rect rect, float y, int percent, string name, Color tint,
            UIColorPaletteDef palette)
        {
            Rect band = new Rect(rect.x, y, rect.width, RowHeight);

            TabParts.RowLabel(new Rect(band.x, band.y, 42f, band.height), percent + "%", tint,
                GameFont.Small, TilePreviewFaces.Mono, TilePreviewFaces.Size.Figure);

            TabParts.RowLabel(new Rect(band.x + 46f, band.y, band.width - 46f, band.height), name,
                palette.TextSecondary, GameFont.Small, TilePreviewFaces.Condensed,
                TilePreviewFaces.Size.Name);

            return band.yMax;
        }

        /// <summary>
        /// A right aligned label.
        ///
        /// Not <c>TabParts.RowLabel</c>, which forces <c>MiddleLeft</c> on the way in: setting the anchor around
        /// that call does nothing, which is the fault the growing zones header carried.
        /// </summary>
        private static void Right(Rect rect, string text, Color color, UIFace face, float points)
        {
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                Text.WordWrap = false;
                GUI.color = color;

                UITextControl.LabelEllipses(rect, text, face, points);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }
        }

        /// <summary>
        /// The tile under the cursor, or the selected one when the cursor is off the globe.
        ///
        /// <b>Nothing while a window has the pointer.</b> The world map keeps drawing under an open dialog, and
        /// a preview of whatever tile happens to lie behind that dialog is noise.
        /// </summary>
        private static PlanetTile Hovered()
        {
            return UIGuard.Try("TilePreview.Hovered", () =>
            {
                if (Find.WindowStack != null && Find.WindowStack.GetWindowAt(UI.MousePositionOnUIInverted) != null)
                    return PlanetTile.Invalid;

                PlanetTile mouse = GenWorld.MouseTile();

                if (mouse.Valid)
                    return mouse;

                WorldSelector selector = Find.WorldSelector;

                return selector != null ? selector.SelectedTile : PlanetTile.Invalid;
            }, PlanetTile.Invalid, null);
        }
    }
}
