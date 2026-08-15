using System;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Patches.UIElements
{
    /// <summary>
    /// Draws RimWorld's row tabs flat and square instead of as nested trapezoids.
    ///
    /// The active tab takes the panel's own fill with a 2px accent bar along its top and no line beneath it, so
    /// it reads as joined to the panel below. Inactive tabs sit on <c>SurfaceSunken</c> behind that panel with
    /// the baseline running under them. That is the whole of the "connected tabs" idea: the break in the
    /// baseline is what says which page you are on.
    ///
    /// <b>Only the drawing is replaced.</b> <c>TabDrawer.DrawTabs</c> owns the widths, the hit testing, the
    /// reverse-order pass that lets the selected tab win an overlap, the mouseover sounds, the tooltips and the
    /// click action -- and it calls <c>TabRecord.Draw</c> for the pixels. Patching here leaves every one of
    /// those alone. Patching <c>DrawTabs</c> instead would mean owning all of it, and it is generic over the
    /// record type, which makes it a poor thing to replace.
    ///
    /// <b>The 10px overlap is absorbed rather than removed.</b> DrawTabs lays each tab at
    /// <c>index * (width - 10)</c>, which is what lets trapezoids nest. Flat tabs drawn into those rects would
    /// overlap by 10 and show a seam in the wrong place, so each tab's body is inset from its left edge by
    /// exactly that amount: tab N's body then begins where tab N-1's body ends, touching with no gap. The rect
    /// DrawTabs hit tests is still the wider one, so the leftmost 10px of a tab answers to it rather than to
    /// its neighbour -- the same ambiguity the slanted edges always had, and invisible now that the edges are
    /// straight.
    /// </summary>
    [HarmonyPatch(typeof(TabRecord), nameof(TabRecord.Draw))]
    public static class Patch_TabRecord_Draw
    {
        /// <summary>Vanilla's own overlap constant, so this cannot drift from the layout it is compensating for.</summary>
        private const float Overlap = TabDrawer.TabHoriztonalOverlap;

        /// <summary>Height of the accent bar that marks the active tab.</summary>
        private const float AccentBar = 2f;

        /// <summary>Breathing room either side of the label.</summary>
        private const float LabelPad = 7f;

        public static bool Prefix(TabRecord __instance, Rect rect)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWrap = Text.WordWrap;
            Color previousColor = GUI.color;

            try
            {
                UIColorPaletteDef palette = UIColorPaletteDef.Active;
                bool selected = __instance.Selected;

                Rect body = new Rect(rect.x + Overlap, rect.y,
                    Mathf.Max(0f, rect.width - Overlap), rect.height);

                // The active tab is filled with the panel's color rather than a lighter "raised" one. Reading as
                // continuous with the panel is the point; a different fill would put a visible join across it.
                Widgets.DrawBoxSolid(body, selected ? palette.PanelBackground : palette.SurfaceSunken);

                if (!selected && Mouse.IsOver(rect))
                    Widgets.DrawBoxSolid(body, palette.HoverOverlay);

                if (selected)
                {
                    Widgets.DrawBoxSolid(new Rect(body.x, body.y, body.width, AccentBar), palette.Accent);
                }
                else
                {
                    // Baseline under an inactive tab only. Drawn under the active one it would cut the tab off
                    // from the panel, which is exactly what this design uses its absence to say.
                    GUI.color = palette.Border;
                    Widgets.DrawLineHorizontal(body.x, body.yMax - 1f, body.width);

                    // A hairline between neighbours, so a row of unlit tabs is legible as several rather than as
                    // one long sunken strip.
                    Widgets.DrawLineVertical(body.x, body.y + 4f, body.height - 5f);
                    GUI.color = previousColor;
                }

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;

                // Never wrapped: a tab is one line by construction, and DrawTabs caps the width at 200, so a long
                // label has to shorten rather than grow a second line the strip has no room for.
                Text.WordWrap = false;

                GUI.color = __instance.labelColor
                            ?? (selected ? palette.TextPrimary : palette.TextSecondary);

                Widgets.LabelEllipses(
                    new Rect(body.x + LabelPad, body.y, Mathf.Max(0f, body.width - LabelPad * 2f), body.height),
                    __instance.label);

                return false;
            }
            catch (Exception ex)
            {
                UIGuard.Report("Framework.TabRecordDraw", ex,
                    "Tabs are drawn with RimWorld's own trapezoid artwork.");

                return true;
            }
            finally
            {
                GUI.color = previousColor;
                Text.WordWrap = previousWrap;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }
    }
}
