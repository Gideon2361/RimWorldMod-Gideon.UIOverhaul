using System.Collections.Generic;
using System.Reflection;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.TilePreview;
using Gideon.UIOverhaul.Shared;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.WorldTile
{
    /// <summary>
    /// Typefaces and sizes for the world tile inspector, on the scale the restyled tabs count in.
    ///
    /// <b>The accent rather than an identity colour.</b> Per-tab colours say which screen you are on, and this
    /// is not a screen: it is a tab of the inspect pane, which uses the accent everywhere else.
    /// </summary>
    internal static class WorldTileFaces
    {
        internal const UIFace Display = UIFace.Oswald;

        internal const UIFace Condensed = UIFace.BarlowCondensed;

        internal const UIFace Mono = UIFace.IBMPlexMono;

        internal const UIFace Body = UIFace.Barlow;

        internal static class Size
        {
            internal const float Title = 14.25f;

            internal const float Where = 7.5f;

            internal const float BlockHead = 9.375f;

            internal const float Name = 11.25f;

            internal const float Value = 9.75f;

            internal const float Lore = 9f;
        }
    }

    /// <summary>
    /// The world map's terrain tab, in the shape the rest of the mod uses.
    ///
    /// <b>Vanilla answers eighteen questions in one voice.</b> Time zone and a debug tile id are set in the same
    /// type, at the same weight, on the same kind of row as the growing period; nothing is coloured, so
    /// "Year-round" and "20 days" read alike; and four lines of biome prose sit above every reading. This groups
    /// the readings by the decision each belongs to, leads with the three that decide it, and colours the ones
    /// that carry a verdict.
    ///
    /// <b>Vanilla's own misc list still runs, at the bottom.</b> Vanilla Expanded Framework patches
    /// <c>ListMiscDetails</c> to append its rows, and it is loaded here. Suppressing the whole tab would have
    /// deleted them silently, so that one method is called rather than reproduced, and anything hung off it
    /// keeps working. See <see cref="Misc"/>.
    /// </summary>
    internal static class WorldTilePanel
    {
        private const float Pad = 10f;

        private const float HeaderHeight = 52f;

        private const float MarkSize = 26f;

        private const float BlockHeadHeight = 19f;

        private const float RowHeight = 19f;

        private const float BlockGap = 8f;

        /// <summary>Side of the map preview beside its figures.</summary>
        private const float PreviewSize = 104f;

        private static Vector2 scroll;

        private static readonly List<WorldTileFact> Scratch = new List<WorldTileFact>();

        /// <summary>Vanilla's misc rows, found once. Null means the method moved and the block is skipped.</summary>
        private static MethodInfo misc;

        private static bool foundMisc;

        internal static void Draw(Rect rect, PlanetTile planetTile)
        {
            Tile tile = Find.WorldGrid[planetTile];

            if (tile == null)
                return;

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Rect inner = rect.ContractedBy(Pad);

            Rect head = new Rect(inner.x, inner.y, inner.width, HeaderHeight);

            Header(head, tile, planetTile, palette);

            Rect body = new Rect(inner.x, head.yMax + BlockGap, inner.width,
                Mathf.Max(0f, inner.yMax - head.yMax - BlockGap));

            Rect view = new Rect(0f, 0f, UIScrollBarControl.ContentWidth(body), Height(tile, planetTile));

            Widgets.BeginScrollView(body, ref scroll, view, false);

            float y = 0f;

            y = Preview(view, y, planetTile, palette);

            y = Rows(view, y, "Living here", tile, planetTile, palette, WorldTileFacts.Living);
            y = Rows(view, y, "The ground", tile, planetTile, palette, WorldTileFacts.Ground);
            y = Hazards(view, y, tile, planetTile, palette);

            Misc(view, y, tile, planetTile, palette);

            Widgets.EndScrollView();
        }

        // ---------------------------------------------------------------------------------------
        // Header
        // ---------------------------------------------------------------------------------------

        private static void Header(Rect rect, Tile tile, PlanetTile planetTile, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect box = rect.ContractedBy(8f);

            Rect mark = new Rect(box.x, box.y + (box.height - MarkSize) * 0.5f, MarkSize, MarkSize);

            Pin(mark, palette.Accent);

            float text = mark.xMax + 8f;

            TabParts.RowLabel(new Rect(text, box.y, 170f, 20f), WorldTileFacts.Name(tile), palette.Accent,
                GameFont.Small, WorldTileFaces.Display, WorldTileFaces.Size.Title);

            string where = WorldTileFacts.Where(tile, planetTile);

            if (!where.NullOrEmpty())
            {
                TabParts.RowLabel(new Rect(text, box.y + 19f, 190f, 14f), where, palette.TextDisabled,
                    GameFont.Tiny, WorldTileFaces.Mono, WorldTileFaces.Size.Where);
            }

            WorldTileFacts.Header(tile, planetTile, Scratch);

            float x = box.xMax;

            for (int i = Scratch.Count - 1; i >= 0; i--)
            {
                WorldTileFact fact = Scratch[i];

                x = TabParts.Readout(box, x, fact.Name, fact.Value, palette, fact.Tip,
                    fact.ColorIn(palette));
            }
        }

        /// <summary>The map pin, drawn rather than shipped: two shapes and no texture to load.</summary>
        private static void Pin(Rect rect, Color color)
        {
            float w = rect.width;

            Widgets.DrawBoxSolid(new Rect(rect.x + w * 0.22f, rect.y + w * 0.10f, w * 0.56f, w * 0.56f),
                color);

            Widgets.DrawBoxSolid(new Rect(rect.x + w * 0.44f, rect.y + w * 0.60f, w * 0.12f, w * 0.30f),
                color);
        }

        // ---------------------------------------------------------------------------------------
        // Blocks
        // ---------------------------------------------------------------------------------------

        private delegate void Reader(Tile tile, PlanetTile planetTile, List<WorldTileFact> into);

        private static float Rows(Rect view, float y, string caption, Tile tile, PlanetTile planetTile,
            UIColorPaletteDef palette, Reader reader)
        {
            reader(tile, planetTile, Scratch);

            if (Scratch.Count == 0)
                return y;

            float height = BlockHeadHeight + Scratch.Count * RowHeight + 8f;

            Rect block = new Rect(view.x, y, view.width, height);

            Frame(block, caption, null, palette, false);

            float row = block.y + BlockHeadHeight + 4f;

            for (int i = 0; i < Scratch.Count; i++)
                row = Row(block, row, Scratch[i], palette);

            return block.yMax + BlockGap;
        }

        /// <summary>
        /// The hazards, and the landmark's own words about them.
        ///
        /// The description moves here from the top of the tab: it is flavour for whatever is wrong with the
        /// place, so it reads as an explanation rather than as a preamble in front of the facts.
        /// </summary>
        private static float Hazards(Rect view, float y, Tile tile, PlanetTile planetTile,
            UIColorPaletteDef palette)
        {
            WorldTileFacts.Hazards(tile, planetTile, Scratch);

            // Read straight out of the scratch list rather than copied into a new one: nothing between
            // here and the last row refills it, and this runs every frame.
            List<WorldTileFact> facts = Scratch;

            string lore = WorldTileFacts.Lore(tile);

            float loreHeight = lore.NullOrEmpty()
                ? 0f
                : UITextControl.Height(lore, WorldTileFaces.Body, WorldTileFaces.Size.Lore,
                      view.width - 24f) + 10f;

            if (facts.Count == 0 && loreHeight <= 0f)
                return y;

            float height = BlockHeadHeight + facts.Count * RowHeight + loreHeight + 8f;

            Rect block = new Rect(view.x, y, view.width, height);

            bool alarm = false;

            for (int i = 0; i < facts.Count; i++)
            {
                if (facts[i].Tone == WorldTileTone.Warning || facts[i].Tone == WorldTileTone.Bad)
                    alarm = true;
            }

            Frame(block, "Hazards", null, palette, alarm);

            float row = block.y + BlockHeadHeight + 4f;

            for (int i = 0; i < facts.Count; i++)
                row = Row(block, row, facts[i], palette);

            if (loreHeight > 0f)
            {
                Rect text = new Rect(block.x + 12f, row + 4f, block.width - 20f, loreHeight - 8f);

                Color previous = GUI.color;

                GUI.color = palette.TextSecondary;

                UITextControl.Paragraph(text, lore, WorldTileFaces.Body, WorldTileFaces.Size.Lore);

                GUI.color = previous;
            }

            return block.yMax + BlockGap;
        }

        /// <summary>
        /// The map a settlement here would generate, and what the grid says about it.
        ///
        /// <b>The preview from the world map, on the tile you have actually chosen.</b> It draws in the corner
        /// on hover because there was nowhere for it; a selected tile has a panel, and the shape of its map
        /// belongs at the top of that panel beside the figures that describe it.
        /// </summary>
        private static float Preview(Rect view, float y, PlanetTile planetTile, UIColorPaletteDef palette)
        {
            TilePreviewEntry entry = TilePreviewCache.For(planetTile);

            if (entry == null || !entry.Valid || entry.Texture == null)
                return y;

            float height = BlockHeadHeight + PreviewSize + 12f;

            Rect block = new Rect(view.x, y, view.width, height);

            Frame(block, "The map you would get", null, palette, false);

            Rect image = new Rect(block.x + 10f, block.y + BlockHeadHeight + 6f, PreviewSize, PreviewSize);

            Widgets.DrawBoxSolid(image, palette.SurfaceSunken);
            GUI.DrawTexture(image, entry.Texture, ScaleMode.ScaleToFit);
            Widgets.DrawBox(image);

            Rect figures = new Rect(image.xMax + 10f, image.y, block.xMax - image.xMax - 20f, PreviewSize);

            float row = figures.y + 4f;

            row = Figure(figures, row, entry.Reading.Buildable, "buildable", palette.Accent, palette, true);
            row = Figure(figures, row, entry.Reading.LargestRun, "largest open run",
                entry.Reading.LargestRun < 25 ? palette.Warning : palette.TextPrimary, palette, false);
            Figure(figures, row, entry.Reading.Mountain, "under mountain",
                entry.Reading.Mountain >= 15 ? palette.Warning : palette.TextPrimary, palette, false);

            return block.yMax + BlockGap;
        }

        private static float Figure(Rect band, float y, int percent, string name, Color tint,
            UIColorPaletteDef palette, bool lead)
        {
            float height = lead ? 26f : 20f;

            TabParts.RowLabel(new Rect(band.x, y, 46f, height), percent + "%", tint, GameFont.Small,
                WorldTileFaces.Mono, lead ? 13.5f : WorldTileFaces.Size.Value);

            TabParts.RowLabel(new Rect(band.x + 50f, y, Mathf.Max(0f, band.width - 50f), height), name,
                palette.TextSecondary, GameFont.Small, WorldTileFaces.Condensed, WorldTileFaces.Size.Name);

            return y + height;
        }

        /// <summary>
        /// Vanilla's misc rows, drawn through vanilla's own listing.
        ///
        /// <b>Called rather than reproduced, and that is the whole point of it.</b> Vanilla Expanded Framework
        /// hangs a patch on this method to add its own rows; reproducing the three readings it makes would have
        /// dropped anything anybody else appends. Invoking it means their patch still runs.
        ///
        /// It is vanilla's own styling inside our panel, which is a seam. The seam is the honest price of not
        /// silently deleting another mod's rows.
        /// </summary>
        private static void Misc(Rect view, float y, Tile tile, PlanetTile planetTile,
            UIColorPaletteDef palette)
        {
            MethodInfo method = MiscMethod();

            if (method == null)
                return;

            Rect block = new Rect(view.x, y, view.width, MiscHeight);

            Frame(block, "More", null, palette, false);

            Rect inner = new Rect(block.x + 10f, block.y + BlockHeadHeight + 4f, block.width - 20f,
                block.height - BlockHeadHeight - 8f);

            UIGuard.Try("WorldTile.Misc", () =>
            {
                Listing_Standard listing = new Listing_Standard { verticalSpacing = 0f };

                listing.Begin(inner);

                method.Invoke(null, new object[] { listing, tile, planetTile });

                // Measured from what was actually drawn and used on the next frame, which is how
                // vanilla sizes this tab too. It matters because the rows are not all ours: Vanilla
                // Expanded Framework appends to this method, and a block sized for the three readings
                // RimWorld makes would clip whatever anybody else adds.
                miscHeight = BlockHeadHeight + listing.CurHeight + 12f;

                listing.End();
            }, null);
        }

        /// <summary>Last frame's measured height for the More block, floored so it never collapses.</summary>
        private static float miscHeight;

        private static float MiscHeight
        {
            get { return Mathf.Max(BlockHeadHeight + 3f * RowHeight + 12f, miscHeight); }
        }

        private static MethodInfo MiscMethod()
        {
            if (foundMisc)
                return misc;

            foundMisc = true;

            misc = UIGuard.Try<MethodInfo>("WorldTile.FindMisc",
                () => AccessTools.Method(typeof(WITab_Terrain), "ListMiscDetails"), null, null);

            return misc;
        }

        // ---------------------------------------------------------------------------------------
        // Parts
        // ---------------------------------------------------------------------------------------

        private static void Frame(Rect block, string caption, string suffix, UIColorPaletteDef palette,
            bool alarm)
        {
            UIElementPainter.OutlineRounded(block, palette.Border, palette.PanelBackground);

            Rect bar = new Rect(block.x + 1f, block.y + 1f, block.width - 2f, BlockHeadHeight);

            Widgets.DrawBoxSolid(bar, palette.SurfaceSunken);
            Widgets.DrawBoxSolid(new Rect(bar.x, bar.yMax, bar.width, 1f), palette.Border);

            TabParts.RowLabel(new Rect(bar.x + 10f, bar.y, bar.width - 20f, bar.height),
                caption.ToUpperInvariant(), alarm ? palette.Warning : palette.TextSecondary, GameFont.Tiny,
                WorldTileFaces.Mono, WorldTileFaces.Size.BlockHead);
        }

        private static float Row(Rect block, float y, WorldTileFact fact, UIColorPaletteDef palette)
        {
            Rect band = new Rect(block.x + 10f, y, block.width - 20f, RowHeight);

            TabParts.RowLabel(new Rect(band.x, band.y, band.width * 0.5f, band.height), fact.Name,
                palette.TextSecondary, GameFont.Small, WorldTileFaces.Condensed, WorldTileFaces.Size.Name);

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                Text.WordWrap = false;
                GUI.color = fact.ColorIn(palette);

                UITextControl.LabelEllipses(new Rect(band.x + band.width * 0.42f, band.y,
                        band.width * 0.58f, band.height), fact.Value, WorldTileFaces.Mono,
                    WorldTileFaces.Size.Value);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }

            if (!fact.Tip.NullOrEmpty())
                TooltipHandler.TipRegion(band, (TipSignal) fact.Tip);

            return band.yMax;
        }

        /// <summary>
        /// How tall the scrolling content is.
        ///
        /// Measured rather than accumulated, because the scroll view has to be told before anything is drawn.
        /// Generous by a block's worth: a short view scrolls harmlessly and a clipped one loses a row.
        /// </summary>
        private static float Height(Tile tile, PlanetTile planetTile)
        {
            float total = 0f;

            WorldTileFacts.Living(tile, planetTile, Scratch);
            total += BlockHeadHeight + Scratch.Count * RowHeight + 8f + BlockGap;

            WorldTileFacts.Ground(tile, planetTile, Scratch);
            total += BlockHeadHeight + Scratch.Count * RowHeight + 8f + BlockGap;

            WorldTileFacts.Hazards(tile, planetTile, Scratch);
            total += BlockHeadHeight + Scratch.Count * RowHeight + 8f + BlockGap;

            string lore = WorldTileFacts.Lore(tile);

            if (!lore.NullOrEmpty())
            {
                total += UITextControl.Height(lore, WorldTileFaces.Body, WorldTileFaces.Size.Lore, 300f)
                         + 10f;
            }

            total += BlockHeadHeight + PreviewSize + 12f + BlockGap;
            total += MiscHeight;

            return total + 8f;
        }
    }
}
