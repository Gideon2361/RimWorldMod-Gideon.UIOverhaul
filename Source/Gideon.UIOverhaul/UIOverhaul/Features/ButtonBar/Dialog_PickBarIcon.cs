using System;
using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Helpers;
using Gideon.UIFramework.Patches.UIElements;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.ButtonBar
{
    /// <summary>
    /// Picks a bar button's icon from a grid of previews.
    ///
    /// Only the icons the game actually offers are shown -- see <see cref="UIBarIconSource"/> -- which is
    /// what makes a visual grid workable at all. A search box filters by path and by the mod that supplied
    /// it, since two mods commonly ship a similar-looking icon and the source is how you tell them apart.
    /// </summary>
    public class Dialog_PickBarIcon : Window
    {
        private const float CellSize = 64f;
        private const float CellGap = 6f;
        private const float HeaderHeight = 88f;
        private const float FooterHeight = 46f;
        private const float Pad = 14f;

        private readonly UIButtonBarEntry entry;
        private readonly List<UIBarIcon> icons;

        private string search = "";
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(720f, 560f);

        protected override float Margin => 0f;

        public Dialog_PickBarIcon(UIButtonBarEntry entry)
        {
            this.entry = entry;
            icons = UIBarIconSource.All;

            doCloseX = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
        }

        private List<UIBarIcon> Visible()
        {
            if (search.NullOrEmpty())
                return icons;

            List<UIBarIcon> filtered = new List<UIBarIcon>();
            foreach (UIBarIcon icon in icons)
            {
                if (icon.Path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || (icon.Source != null
                        && icon.Source.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                    filtered.Add(icon);
            }

            return filtered;
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("ButtonBar.IconPicker", inRect, () => DrawContents(inRect),
                "The icon picker shows a failure notice; the tab keeps whichever icon it already had.");
        }

        private void DrawContents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            // Deliberately no fill: RimWorld has already painted this color and the window border across
            // inRect, and repainting it here covered the border. See Patch_Widgets_WindowChrome.

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Medium;
            GUI.color = palette.TextPrimary;
            Widgets.Label(new Rect(inRect.x + Pad, inRect.y + 10f, 300f, 30f), "Choose an icon");

            Text.Font = GameFont.Small;
            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(inRect.x + Pad, inRect.y + 40f, inRect.width - Pad * 2f, 20f),
                $"{icons.Count} icon(s) from button definitions and from mods' UI/MainButtonIcons folders.");
            GUI.color = palette.TextPrimary;

            Rect searchRect = new Rect(inRect.x + Pad, inRect.y + 60f, 280f, 26f);
            Widgets.DrawBoxSolid(searchRect, palette.SurfaceSunken);
            GUI.color = palette.AccentMuted;
            Widgets.DrawBox(searchRect, 1);
            GUI.color = palette.TextPrimary;
            search = Widgets.TextField(searchRect.ContractedBy(4f, 1f), search);

            if (search.NullOrEmpty() && !Mouse.IsOver(searchRect))
            {
                GUI.color = palette.TextDisabled;
                Widgets.Label(searchRect.ContractedBy(7f, 2f), "Search...");
                GUI.color = palette.TextPrimary;
            }

            Rect grid = new Rect(inRect.x + Pad, inRect.y + HeaderHeight,
                inRect.width - Pad * 2f, inRect.height - HeaderHeight - FooterHeight);

            DrawGrid(grid, palette);

            Rect footer = new Rect(inRect.x + Pad, inRect.yMax - FooterHeight + 6f,
                inRect.width - Pad * 2f, 32f);

            if (SmallButton(new Rect(footer.x, footer.y, 170f, 32f), "Use no icon", palette))
            {
                entry.icon = null;
                SoundDefOf.Click.PlayOneShotOnCamera();
                Close();
            }

            if (SmallButton(new Rect(footer.xMax - 120f, footer.y, 120f, 32f), "Cancel", palette))
                Close();

            Text.Anchor = previousAnchor;
            GUI.color = previousColor;
            Text.Font = previousFont;
        }

        private void DrawGrid(Rect outRect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(outRect, palette.PanelBackground);
            Rect inner = outRect.ContractedBy(8f);

            List<UIBarIcon> visible = Visible();

            int columns = Mathf.Max(1, Mathf.FloorToInt((inner.width - 18f) / (CellSize + CellGap)));
            int rows = Mathf.CeilToInt(visible.Count / (float) columns);

            Rect view = new Rect(0f, 0f, inner.width - 18f, rows * (CellSize + CellGap) + CellGap);
            Widgets.BeginScrollView(inner, ref scroll, view);

            for (int i = 0; i < visible.Count; i++)
            {
                int column = i % columns;
                int row = i / columns;

                Rect cell = new Rect(
                    column * (CellSize + CellGap),
                    row * (CellSize + CellGap),
                    CellSize, CellSize);

                // Only what is on screen. A large load order can put several hundred icons in here, and
                // drawing every cell would cost a texture blit each for rows nobody is looking at.
                if (cell.yMax < scroll.y || cell.y > scroll.y + inner.height)
                    continue;

                DrawCell(cell, visible[i], palette);
            }

            Widgets.EndScrollView();
        }

        private void DrawCell(Rect cell, UIBarIcon icon, UIColorPaletteDef palette)
        {
            bool chosen = !entry.icon.NullOrEmpty()
                          && string.Equals(entry.icon, icon.Path, StringComparison.OrdinalIgnoreCase);
            bool over = Mouse.IsOver(cell);

            Widgets.DrawBoxSolid(cell, palette.SurfaceSunken);

            if (chosen)
                Widgets.DrawBoxSolid(cell, palette.SelectionOverlay);
            else if (over)
                Widgets.DrawBoxSolid(cell, palette.HoverOverlay);

            Color previous = GUI.color;
            GUI.color = chosen || over ? palette.BorderFocused : palette.Border;
            Widgets.DrawBox(cell, 1);

            // Tinted on the same ramp the bar uses, so the grid previews what the button will look like
            // rather than showing every icon brighter here than it will ever appear in play. Brightening
            // the one under the cursor doubles as hover feedback.
            GUI.color = chosen || over ? palette.TextPrimary : palette.TextSecondary;
            if (icon.Texture != null)
                GUI.DrawTexture(cell.ContractedBy(8f), icon.Texture, ScaleMode.ScaleToFit);
            GUI.color = previous;

            TooltipHandler.TipRegion(cell, (TipSignal) (icon.Path + "\n" + icon.Source));

            if (!Widgets.ButtonInvisible(cell))
                return;

            entry.icon = icon.Path;
            SoundDefOf.Click.PlayOneShotOnCamera();
            Close();
        }

        private static bool SmallButton(Rect r, string label, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(r);
            UIElementPainter.PaintButton(r, palette, over, over && Input.GetMouseButton(0));

            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            GUI.color = palette.TextPrimary;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label);

            Text.Anchor = previousAnchor;
            GUI.color = previousColor;

            return Widgets.ButtonInvisible(r);
        }
    }
}
