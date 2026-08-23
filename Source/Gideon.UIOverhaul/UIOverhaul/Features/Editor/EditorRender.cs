using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// The pawn, whole, beside the controls that change them.
    ///
    /// <b>Full body, not a portrait.</b> Every other picture of a pawn in this mod is head and shoulders, because
    /// that is what identifies somebody in a list. This is the one place the boots matter, so it asks
    /// <c>PortraitsCache.Get</c> for no camera offset and no zoom -- which is all a full-body shot is. The colonist
    /// bar asks the same method for a lift and 1.5x and gets a face.
    ///
    /// <b>It turns.</b> South, east, north and west, because half the apparel in the game looks different from
    /// behind and a duster is mostly back.
    ///
    /// <b>Two toggles, not the three the proposal drew.</b> <c>PortraitsCache.Get</c> takes
    /// <c>renderClothes</c> and <c>renderHeadgear</c>, so hiding apparel and hiding headgear cost nothing and
    /// affect the picture only. There is no third: a portrait never draws the equipped weapon at all, so a "show
    /// weapon" switch would have been a control with nothing behind it.
    /// </summary>
    internal static class EditorRender
    {
        internal const float ColumnWidth = 196f;

        private const float StandHeight = 260f;

        private const float FacingHeight = 24f;

        /// <summary>Which way the render is facing. Kept across panels, since it is a viewing preference.</summary>
        private static Rot4 facing = Rot4.South;

        private static bool showApparel = true;

        private static bool showHeadgear = true;

        private static readonly string[] Facings = { "S", "E", "N", "W" };

        /// <summary>Reset when the window opens, so a fresh pawn is not inspected from behind and undressed.</summary>
        internal static void Reset()
        {
            facing = Rot4.South;
            showApparel = true;
            showHeadgear = true;
        }

        internal static void Draw(Rect column, Pawn pawn, string caption, UIColorPaletteDef palette)
        {
            if (pawn == null)
                return;

            Widgets.DrawBoxSolid(column, palette.SurfaceSunken);

            Rect inner = column.ContractedBy(8f);

            float y = inner.y;

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;
                GUI.color = palette.TextDisabled;

                UIRichText.Label(new Rect(inner.x, y, inner.width, EditorParts.CaptionHeight),
                    caption ?? "Live");
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Font = previousFont;
            }

            y += EditorParts.CaptionHeight + 4f;

            float height = Mathf.Min(StandHeight, inner.height - EditorParts.CaptionHeight - FacingHeight - 70f);

            Rect stand = new Rect(inner.x, y, inner.width, Mathf.Max(80f, height));

            Stand(stand, pawn, palette);

            y = stand.yMax + 6f;

            Facing(new Rect(inner.x, y, inner.width, FacingHeight), palette);

            y += FacingHeight + 8f;

            bool apparel = showApparel;

            if (UICheckboxControl.Draw(new Rect(inner.x, y, inner.width, 22f), ref apparel, palette,
                    "Show apparel", "Hides clothing from this picture only. Nobody is undressed."))
                showApparel = apparel;

            y += 24f;

            bool headgear = showHeadgear;

            if (UICheckboxControl.Draw(new Rect(inner.x, y, inner.width, 22f), ref headgear, palette,
                    "Show headgear", "Hides hats from this picture only."))
                showHeadgear = headgear;
        }

        /// <summary>
        /// The render itself.
        ///
        /// <b>Asked for at the rect's own pixel size rather than a fixed one.</b> A portrait cached at 128 and
        /// drawn into 240 is a blurred pawn, which on the one screen in the mod whose job is to show you exactly
        /// what somebody looks like is the whole feature failing quietly.
        /// </summary>
        private static void Stand(Rect rect, Pawn pawn, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            UIGuard.Try("Editor.Render", () =>
            {
                // Square, and as large as the shorter side allows: a pawn render is square whatever it is asked
                // for, so a non-square rect would letterbox it and the frame would no longer be centred on it.
                float side = Mathf.Min(rect.width, rect.height) - 8f;

                if (side < 32f)
                    return;

                RenderTexture render = PortraitsCache.Get(pawn, new Vector2(side, side), facing,
                    default(Vector3), 1f, true, true, showHeadgear, showApparel);

                if (render == null)
                    return;

                GUI.DrawTexture(new Rect(rect.center.x - side * 0.5f, rect.center.y - side * 0.5f, side, side),
                    render);
            }, null);
        }

        private static void Facing(Rect rect, UIColorPaletteDef palette)
        {
            float width = Mathf.Floor((rect.width - 9f) * 0.25f);

            for (int i = 0; i < 4; i++)
            {
                Rect slot = new Rect(rect.x + i * (width + 3f), rect.y, width, rect.height);

                Rot4 which = new Rot4(i);

                TabParts.Segment(slot, Facings[i], facing.AsInt == i, palette, () => facing = which);
            }
        }
    }
}
