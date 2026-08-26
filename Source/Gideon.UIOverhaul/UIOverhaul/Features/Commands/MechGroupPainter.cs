using System.Collections.Generic;
using System.Reflection;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Commands
{
    /// <summary>
    /// The mechanitor's control group gizmo, drawn in this mod's theme.
    ///
    /// <b>It was the one button in the command row that still looked like vanilla,</b> and the reason is written
    /// on <see cref="Patch_CommandGizmo"/>: that patch covers <c>Command</c>, and this gizmo derives straight from
    /// <c>Gizmo</c> and paints <c>Command.BGTex</c> itself. So a mechanitor's row was our flat panels with one
    /// beveled stone tablet sitting in the middle of them. Reported on 2026-08-25.
    ///
    /// <b>The caveat on that patch does not apply here.</b> It declines to reskin third party gizmos because it
    /// would mean guessing at drawing code we have never seen. This one is RimWorld's, its source is readable, and
    /// what it does is a background, two icons, a label and a portrait grid.
    ///
    /// <b>Presentation only. Every decision still belongs to the game.</b> Selecting mechs goes through
    /// <c>Find.Selector</c>, the work mode menu is vanilla's own <c>RightClickFloatMenuOptions</c> returned by
    /// opening a float menu, recharge settings open vanilla's dialog, and whether the group can be commanded at
    /// all is <c>Tracker.CanControlMechs</c> answered by the game. Nothing about mechs is reimplemented.
    /// </summary>
    internal static class MechGroupPainter
    {
        private static readonly FieldInfo GroupField =
            AccessTools.Field(typeof(MechanitorControlGroupGizmo), "controlGroup");

        private static readonly FieldInfo MergedField =
            AccessTools.Field(typeof(MechanitorControlGroupGizmo), "mergedControlGroups");

        /// <summary>
        /// <c>Gizmo.disabled</c> is protected and <c>Disable</c> only ever sets it, with no way back.
        ///
        /// Vanilla assigns this field every frame from <c>CanControlMechs</c>, so a group that becomes
        /// commandable again stops being disabled. Reaching the field is what lets us do the same; calling
        /// <c>Disable</c> instead would latch the gizmo off the first time a mechanitor was downed.
        /// </summary>
        private static readonly FieldInfo DisabledField = AccessTools.Field(typeof(Gizmo), "disabled");

        /// <summary>Vanilla's own icon path, which is content rather than code.</summary>
        private static readonly CachedTexture PowerIcon = new CachedTexture("UI/Icons/MechRechargeSettings");

        private const float IconSize = 26f;
        private const float Pad = 6f;

        /// <summary>Whether the reskin can run at all. A field we cannot reach means we cannot draw this.</summary>
        internal static bool Available => GroupField != null && MergedField != null && DisabledField != null;

        internal static GizmoResult Draw(MechanitorControlGroupGizmo gizmo, Vector2 topLeft, float maxWidth,
            GizmoRenderParms parms)
        {
            MechanitorControlGroup group = GroupField.GetValue(gizmo) as MechanitorControlGroup;

            if (group == null)
                return new GizmoResult(GizmoState.Clear);

            List<MechanitorControlGroup> merged = MergedField.GetValue(gizmo) as List<MechanitorControlGroup>;

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            AcceptanceReport report = group.Tracker.CanControlMechs;
            bool disabled = !report.Accepted;

            DisabledField.SetValue(gizmo, disabled);
            gizmo.disabledReason = report.Reason;

            Rect rect = new Rect(topLeft.x, topLeft.y, gizmo.GetWidth(maxWidth), Gizmo.Height);
            Rect inner = rect.ContractedBy(Pad);

            bool over = Mouse.IsOver(inner);

            List<Pawn> mechs = group.MechsForReading;

            if (parms.highLight)
                Widgets.DrawStrongHighlight(rect.ExpandedBy(4f));

            Face(rect, palette, disabled, mechs.Count == 0, parms);

            Rect title = Title(inner, group, merged, palette);

            if (mechs.Count == 0)
            {
                Empty(inner, palette);

                return new GizmoResult(GizmoState.Clear);
            }

            Select(title, mechs);

            bool overPower;
            bool overMode = Icons(rect, group, palette, disabled, out overPower);

            Grid(new Rect(inner.x, inner.y + IconSize + 4f, inner.width, inner.height - IconSize - 4f), group,
                mechs, palette);

            if (Find.WindowStack.FloatMenu == null && !overPower)
                Tip(rect, gizmo, group, disabled);

            // The work mode icon opens vanilla's own menu, which the gizmo grid builds from
            // RightClickFloatMenuOptions. Returning the state is how a gizmo asks for that.
            if (overMode && Event.current.type == EventType.MouseDown)
                return new GizmoResult(GizmoState.OpenedFloatMenu, Event.current);

            return new GizmoResult(over ? GizmoState.Mouseover : GizmoState.Clear);
        }

        /// <summary>
        /// The panel behind it, matching a command button exactly.
        ///
        /// <b>An empty group is drawn sunken rather than dimmed.</b> It is not a control that has been switched
        /// off -- it is a container with nothing in it, and vanilla says as much in words in the middle of it. A
        /// disabled group is a different thing and keeps the disabled treatment a command button uses, so the two
        /// do not read alike.
        /// </summary>
        private static void Face(Rect rect, UIColorPaletteDef palette, bool disabled, bool empty,
            GizmoRenderParms parms)
        {
            Color fill = disabled || empty ? palette.SurfaceSunken : palette.PanelBackground;
            Color edge = palette.Border;

            if (parms.lowLight)
                fill = new Color(fill.r, fill.g, fill.b, fill.a * 0.55f);

            UIElementPainter.OutlineRounded(rect, edge, fill);
        }

        /// <summary>The "Group 1" or "Groups 1, 2" line. Returns the rect it took, which is its own hit target.</summary>
        private static Rect Title(Rect inner, MechanitorControlGroup group, List<MechanitorControlGroup> merged,
            UIColorPaletteDef palette)
        {
            // Vanilla's wording and vanilla's sort, taken through the same translation keys so a translated game
            // reads the same here as it does everywhere else.
            TaggedString text = (!merged.NullOrEmpty() ? "Groups".Translate() : "Group".Translate())
                                + " " + group.Index;

            if (!merged.NullOrEmpty())
            {
                merged.SortBy(other => other.Index);

                for (int i = 0; i < merged.Count; i++)
                    text += ", " + merged[i].Index;
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Rect used;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextPrimary;

                text = text.Truncate(inner.width);

                Vector2 size = Text.CalcSize(text);

                used = new Rect(inner.x, inner.y, size.x, size.y);

                Widgets.Label(used, text);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            return used;
        }

        /// <summary>What a group with nothing in it says, in the middle of the space its mechs would fill.</summary>
        private static void Empty(Rect inner, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = palette.TextDisabled;

                Widgets.Label(inner, "(" + "NoMechs".Translate() + ")");
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>Clicking the group's name selects everything in it, which is what vanilla does.</summary>
        private static void Select(Rect title, List<Pawn> mechs)
        {
            if (!Mouse.IsOver(title))
                return;

            Widgets.DrawHighlight(title);

            if (!Widgets.ButtonInvisible(title))
                return;

            Find.Selector.ClearSelection();

            for (int i = 0; i < mechs.Count; i++)
                Find.Selector.Select(mechs[i]);
        }

        /// <summary>
        /// The recharge and work mode icons at the top right. Returns whether the work mode one is hovered.
        /// </summary>
        private static bool Icons(Rect rect, MechanitorControlGroup group, UIColorPaletteDef palette, bool disabled,
            out bool overPower)
        {
            Rect power = new Rect(rect.xMax - IconSize - Pad, rect.y + Pad, IconSize, IconSize);
            Rect mode = new Rect(power.x - IconSize, rect.y + Pad, IconSize, IconSize);

            overPower = !disabled && Mouse.IsOver(power);

            Widgets.DrawTextureFitted(power, PowerIcon.Texture, 1f);

            if (overPower)
            {
                UIElementPainter.FillRounded(power, palette.HoverOverlay);

                if (Widgets.ButtonInvisible(power))
                    Find.WindowStack.Add(new Dialog_RechargeSettings(group));
            }

            Widgets.DrawTextureFitted(mode, group.WorkMode.uiIcon, 1f);

            bool overMode = !disabled && Mouse.IsOver(mode);

            if (overMode)
                UIElementPainter.FillRounded(mode, palette.HoverOverlay);

            return overMode;
        }

        /// <summary>
        /// The mechs themselves, as portraits in as square a grid as fits.
        ///
        /// <b>The cell size is solved for rather than guessed.</b> A group holds anything from one mech to a
        /// dozen, in a box 118 pixels wide, so the largest square that still fits them all is the only sensible
        /// answer -- and it has to be found by trying, because how many fit per row depends on the size itself.
        /// </summary>
        private static void Grid(Rect rect, MechanitorControlGroup group, List<Pawn> mechs,
            UIColorPaletteDef palette)
        {
            if (rect.width <= 0f || rect.height <= 0f || mechs.Count == 0)
                return;

            float size = rect.height;
            int columns = 1;

            for (float trial = rect.height; trial >= 1f; trial--)
            {
                columns = Mathf.Max(1, Mathf.FloorToInt(rect.width / trial));

                int rows = Mathf.Max(1, Mathf.FloorToInt(rect.height / trial));

                if (columns * rows >= mechs.Count)
                {
                    size = trial;

                    break;
                }
            }

            int usedRows = Mathf.CeilToInt(mechs.Count / (float) columns);

            float offsetX = (rect.width - columns * size) * 0.5f;
            float offsetY = (rect.height - usedRows * size) * 0.5f;

            for (int i = 0; i < mechs.Count; i++)
            {
                Pawn mech = mechs[i];

                if (mech == null)
                    continue;

                Rect cell = new Rect(rect.x + i % columns * size + offsetX,
                    rect.y + i / columns * size + offsetY, size, size);

                // A mech the mechanitor is not currently controlling, flagged in the palette's own danger color
                // rather than vanilla's hard coded red so it reads as a warning in whatever theme is loaded.
                if (!group.Tracker.ControlledPawns.Contains(mech))
                {
                    Color danger = palette.Danger;

                    Widgets.DrawRectFast(cell, new Color(danger.r, danger.g, danger.b, 0.35f));
                }

                GUI.DrawTexture(cell, PortraitsCache.Get(mech, cell.size, Rot4.East, default(Vector3),
                    mech.kindDef.controlGroupPortraitZoom));

                Portrait(cell, mech, columns);
            }
        }

        /// <summary>One portrait's hover, click and selection marker.</summary>
        private static void Portrait(Rect cell, Pawn mech, int columns)
        {
            if (Mouse.IsOver(cell))
            {
                Widgets.DrawHighlight(cell);

                MouseoverSounds.DoRegion(cell, SoundDefOf.Mouseover_Command);

                if (Event.current.type == EventType.MouseDown)
                {
                    // Shift adds to the selection, a plain click goes to the mech. Vanilla's rule, and the one a
                    // player already has in their hands from the colonist bar.
                    if (Event.current.shift)
                        Find.Selector.Select(mech);
                    else
                        CameraJumper.TryJumpAndSelect(mech);
                }

                TargetHighlighter.Highlight(mech, true, false);
            }

            if (Find.Selector.IsSelected(mech))
                SelectionDrawerUtility.DrawSelectionOverlayOnGUI(mech, cell, 0.8f / columns, 20f);
        }

        /// <summary>Vanilla's tooltip, rebuilt from the same pieces because its closure is not reachable.</summary>
        private static void Tip(Rect rect, Gizmo gizmo, MechanitorControlGroup group, bool disabled)
        {
            TooltipHandler.TipRegion(rect, () =>
            {
                string text = ("ControlGroup".Translate() + " #" + group.Index)
                              .Colorize(ColoredText.TipSectionTitleColor) + "\n\n";

                text += ("CurrentMechWorkMode".Translate() + ": " + group.WorkMode.LabelCap)
                        .Colorize(ColoredText.TipSectionTitleColor) + "\n" + group.WorkMode.description + "\n\n";

                List<string> lines = new List<string>();
                List<Pawn> mechs = group.MechsForReading;

                for (int i = 0; i < mechs.Count; i++)
                {
                    Pawn mech = mechs[i];

                    if (mech?.needs?.energy == null)
                        continue;

                    lines.Add((mech.LabelCap + " (" + mech.needs.energy.CurLevelPercentage.ToStringPercent()
                               + " " + "EnergyLower".Translate() + ")").Resolve());
                }

                text += "AssignedMechs".Translate().Colorize(ColoredText.TipSectionTitleColor) + "\n"
                        + lines.ToLineList(" - ");

                if (disabled && !gizmo.disabledReason.NullOrEmpty())
                {
                    text += ("\n\n" + "DisabledCommand".Translate() + ": " + gizmo.disabledReason)
                        .Colorize(ColorLibrary.RedReadable);
                }

                return text;
            }, 2545872);
        }
    }
}
