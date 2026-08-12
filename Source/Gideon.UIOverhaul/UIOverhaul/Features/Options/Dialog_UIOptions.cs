using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIFramework.Patches.UIElements;
using Gideon.UIOverhaul.Features.ButtonBar;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Options
{
    /// <summary>
    /// This mod's settings, as a window of our own.
    ///
    /// It began as a category in the vanilla Options window, which does not work: Dialog_Options builds
    /// its category list from DefDatabase&lt;OptionCategoryDef&gt;.AllDefs but skips any def whose
    /// ModContentPack is not official, logging "Unofficial OptionCategoryDef ... ignoring". Short of
    /// patching that filter, a mod cannot add a category at all -- so the settings live here instead,
    /// reached from the bar button, and get the theme applied to them rather than inheriting vanilla's.
    /// </summary>
    public class Dialog_UIOptions : Window
    {
        private const float HeaderHeight = 52f;
        private const float FooterHeight = 46f;
        private const float Pad = 16f;
        private const float RowHeight = 32f;

        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(620f, 560f);

        protected override float Margin => 0f;

        public Dialog_UIOptions()
        {
            doCloseX = false;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

            Widgets.DrawBoxSolid(inRect, palette.WindowBackground);

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            // Header
            Text.Font = GameFont.Medium;
            GUI.color = palette.TextPrimary;
            Widgets.Label(new Rect(inRect.x + Pad, inRect.y + 12f, inRect.width - Pad * 3f - 24f, 32f),
                "UI Options");

            Rect closeRect = new Rect(inRect.xMax - Pad - 24f, inRect.y + 14f, 24f, 24f);
            if (SmallButton(closeRect, "X", palette))
                Close();

            Rect body = new Rect(inRect.x + Pad, inRect.y + HeaderHeight,
                inRect.width - Pad * 2f, inRect.height - HeaderHeight - FooterHeight);

            Widgets.DrawBoxSolid(body, palette.PanelBackground);
            Rect inner = body.ContractedBy(10f);

            Text.Font = GameFont.Small;

            float viewHeight = 420f;
            Rect view = new Rect(0f, 0f, inner.width - 18f, viewHeight);
            Widgets.BeginScrollView(inner, ref scroll, view);

            float y = 0f;
            DrawThemeSection(view, ref y, palette, settings);
            y += 14f;
            DrawBarSection(view, ref y, palette, settings);

            Widgets.EndScrollView();

            Text.Anchor = previousAnchor;
            GUI.color = previousColor;
            Text.Font = previousFont;
        }

        private void DrawThemeSection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "Theme", palette);

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(0f, y, view.width, 38f),
                "Applies to this mod's windows and to the RimWorld controls it restyles. Takes effect "
                + "immediately.");
            y += 42f;
            GUI.color = palette.TextPrimary;

            List<UIColorPaletteDef> palettes = UIColorPaletteDef.All;
            if (palettes == null || palettes.Count == 0)
            {
                Widgets.Label(new Rect(0f, y, view.width, RowHeight), "No palettes are loaded.");
                y += RowHeight;
                return;
            }

            string activeName = UIColorPaletteDef.ActiveDefName.NullOrEmpty()
                ? UIColorPaletteDef.Default?.defName
                : UIColorPaletteDef.ActiveDefName;

            foreach (UIColorPaletteDef option in palettes)
            {
                Rect row = new Rect(0f, y, view.width, RowHeight);
                bool chosen = option.defName == activeName;

                if (chosen)
                    Widgets.DrawBoxSolid(row, palette.SelectionOverlay);

                string label = option.label.NullOrEmpty()
                    ? option.defName
                    : option.label.CapitalizeFirst();

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = chosen ? palette.TextPrimary : palette.TextSecondary;
                Widgets.Label(new Rect(24f, row.y, row.width - 28f, row.height), label);
                Text.Anchor = TextAnchor.UpperLeft;

                // Radio marker drawn as a filled square rather than vanilla's textured dot, so the row
                // matches everything else in this window.
                Rect marker = new Rect(4f, row.y + 10f, 12f, 12f);
                Widgets.DrawBoxSolid(marker, chosen ? palette.Accent : palette.SurfaceSunken);

                if (!option.description.NullOrEmpty())
                    TooltipHandler.TipRegion(row, (TipSignal) option.description);

                if (Widgets.ButtonInvisible(row) && !chosen)
                {
                    UIColorPaletteDef.ActiveDefName = option.defName;
                    settings.activePalette = option.defName;
                    settings.Save();
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                y += RowHeight + 2f;
            }

            GUI.color = palette.TextPrimary;
        }

        private void DrawBarSection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "Button bar", palette);

            if (SmallButton(new Rect(0f, y, 200f, RowHeight), "Arrange the bar...", palette))
            {
                Find.WindowStack.Add(new Dialog_ButtonBarEditor());
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += RowHeight + 4f;

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(0f, y, view.width, 40f),
                "Reorder tabs, rename them, take them off the bar, group them into menus, and choose "
                + "icons.");
            y += 44f;
            GUI.color = palette.TextPrimary;
        }

        private static void SectionHeader(Rect view, ref float y, string title, UIColorPaletteDef palette)
        {
            GameFont previous = Text.Font;
            Text.Font = GameFont.Medium;
            GUI.color = palette.TextPrimary;
            Widgets.Label(new Rect(0f, y, view.width, 30f), title);
            Text.Font = previous;

            y += 30f;
            Widgets.DrawBoxSolid(new Rect(0f, y, view.width, 1f), palette.Border);
            y += 8f;
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
