using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Work;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Mechs
{
    /// <summary>
    /// The middle column: one card per control group, and the rows inside them.
    ///
    /// <b>The card header holds everything that belongs to the group and the rows hold only what differs.</b>
    /// That is the whole difference from the table this replaces. Work mode is one control set once rather
    /// than a cell repeated down every row whose neighbours silently change with it; the recharge band and
    /// the allowed area are stated instead of being reachable only from a gizmo; and the area says
    /// <c>n/a</c> rather than lying with a value when the group's mode makes it inapplicable, which is
    /// something RimWorld's own header tip admits in words and its table never shows.
    ///
    /// <b>Measured and drawn by the same code.</b> <see cref="Height"/> and <see cref="Draw"/> walk the same
    /// lists through the same filters, so the scroll view can never disagree with the cards in it.
    /// </summary>
    internal static class MechDeck
    {
        private static readonly List<WorkTypeDef> workBuckets = new List<WorkTypeDef>();

        private static readonly List<ThingDef> kindBuckets = new List<ThingDef>();

        private static readonly List<Pawn> bucket = new List<Pawn>();

        internal static void Draw(Rect rect, ref Vector2 scroll, UIColorPaletteDef palette)
        {
            float width = rect.width - 18f;
            Rect canvas = new Rect(0f, 0f, width, Mathf.Max(Height(), rect.height));

            Widgets.BeginScrollView(rect, ref scroll, canvas);

            float y = 0f;
            bool any = false;

            if (MechsPanel.OnUnlinked)
            {
                any = MechRoster.Unlinked.Count > 0;

                if (any)
                {
                    y = Card(canvas, y, palette, "Unlinked", "no overseer", null, MechRoster.Unlinked,
                        palette.Danger,
                        "These mechs answer to nobody. Their mechanitor died or lost their mechlink. A "
                        + "mechanitor can take them back on by selecting them and commanding them, if they "
                        + "have the bandwidth.");
                }
            }
            else
            {
                switch (MechsPanel.View)
                {
                    case MechView.Group:
                        y = Groups(canvas, y, palette, ref any);
                        break;

                    case MechView.Work:
                        y = Work(canvas, y, palette, ref any);
                        break;

                    case MechView.Kind:
                        y = Kinds(canvas, y, palette, ref any);
                        break;

                    default:
                        y = Flat(canvas, y, palette, ref any);
                        break;
                }

                y = Gestation(canvas, y, palette, ref any);
            }

            if (!any)
                Empty(canvas, palette);

            Widgets.EndScrollView();
        }

        // -------------------------------------------------------------------------------------------
        // Measurement
        // -------------------------------------------------------------------------------------------

        /// <summary>How tall the deck is, walked exactly the way it is drawn.</summary>
        private static float Height()
        {
            float y = 0f;

            if (MechsPanel.OnUnlinked)
            {
                return MechRoster.Unlinked.Count == 0
                    ? 0f
                    : CardHeight(MechRoster.Unlinked.Count, false);
            }

            switch (MechsPanel.View)
            {
                case MechView.Group:
                    for (int i = 0; i < MechRoster.Mechanitors.Count; i++)
                    {
                        MechanitorEntry owner = MechRoster.Mechanitors[i];

                        for (int g = 0; g < owner.Groups.Count; g++)
                        {
                            MechGroupEntry group = owner.Groups[g];

                            if (!MechsPanel.InRail(owner, group))
                                continue;

                            y += CardHeight(Visible(group.Mechs), true) + MechsPanel.Spacing;
                        }
                    }

                    break;

                case MechView.Work:
                    BuildWorkBuckets();

                    for (int i = 0; i < workBuckets.Count; i++)
                    {
                        FillWork(workBuckets[i]);
                        y += CardHeight(bucket.Count, false) + MechsPanel.Spacing;
                    }

                    break;

                case MechView.Kind:
                    BuildKindBuckets();

                    for (int i = 0; i < kindBuckets.Count; i++)
                    {
                        FillKind(kindBuckets[i]);
                        y += CardHeight(bucket.Count, false) + MechsPanel.Spacing;
                    }

                    break;

                default:
                    Collect();

                    if (MechsPanel.Scratch.Count > 0)
                        y += CardHeight(MechsPanel.Scratch.Count, false) + MechsPanel.Spacing;

                    break;
            }

            if (MechRoster.Gestating.Count > 0)
                y += CardHeight(MechRoster.Gestating.Count, false) + MechsPanel.Spacing;

            return y;
        }

        private static float CardHeight(int rows, bool columnHead)
        {
            return MechsPanel.CardHead + (columnHead ? MechsPanel.ColumnHead : 0f)
                                       + Mathf.Max(rows, 1) * MechsPanel.Rows + 4f;
        }

        private static int Visible(List<Pawn> mechs)
        {
            int count = 0;

            for (int i = 0; i < mechs.Count; i++)
            {
                if (MechsPanel.Passes(mechs[i]))
                    count++;
            }

            return count;
        }

        // -------------------------------------------------------------------------------------------
        // The four views
        // -------------------------------------------------------------------------------------------

        private static float Groups(Rect canvas, float y, UIColorPaletteDef palette, ref bool any)
        {
            for (int i = 0; i < MechRoster.Mechanitors.Count; i++)
            {
                MechanitorEntry owner = MechRoster.Mechanitors[i];

                for (int g = 0; g < owner.Groups.Count; g++)
                {
                    MechGroupEntry group = owner.Groups[g];

                    if (!MechsPanel.InRail(owner, group))
                        continue;

                    any = true;
                    y = GroupCard(canvas, y, palette, owner, group);
                }
            }

            return y;
        }

        private static float Work(Rect canvas, float y, UIColorPaletteDef palette, ref bool any)
        {
            BuildWorkBuckets();

            for (int i = 0; i < workBuckets.Count; i++)
            {
                WorkTypeDef work = workBuckets[i];

                FillWork(work);

                any = true;

                y = Card(canvas, y, palette, WorkPanel.LabelOf(work), bucket.Count + " assigned", work,
                    bucket, null, work.description);
            }

            return y;
        }

        private static float Kinds(Rect canvas, float y, UIColorPaletteDef palette, ref bool any)
        {
            BuildKindBuckets();

            for (int i = 0; i < kindBuckets.Count; i++)
            {
                ThingDef kind = kindBuckets[i];

                FillKind(kind);

                any = true;

                y = Card(canvas, y, palette, kind.LabelCap, bucket.Count.ToString(), null, bucket, null,
                    null);
            }

            return y;
        }

        private static float Flat(Rect canvas, float y, UIColorPaletteDef palette, ref bool any)
        {
            Collect();

            if (MechsPanel.Scratch.Count == 0)
                return y;

            any = true;

            return Card(canvas, y, palette, "All mechs", MechsPanel.Scratch.Count.ToString(), null,
                MechsPanel.Scratch, null, null);
        }

        // -------------------------------------------------------------------------------------------
        // Cards
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// One control group: its header, its column captions, and its mechs.
        /// </summary>
        private static float GroupCard(Rect canvas, float y, UIColorPaletteDef palette, MechanitorEntry owner,
            MechGroupEntry group)
        {
            int rows = Visible(group.Mechs);
            float height = CardHeight(rows, true);
            Rect card = new Rect(canvas.x, y, canvas.width, height);

            UIElementPainter.OutlineRounded(card, palette.Border, palette.PanelBackground);

            Rect head = new Rect(card.x + 1f, card.y + 1f, card.width - 2f, MechsPanel.CardHead);

            Widgets.DrawBoxSolid(head, palette.SurfaceSunken);
            Widgets.DrawLineHorizontal(head.x, head.yMax, head.width);

            GroupHeader(head, palette, owner, group);

            Rect columns = new Rect(card.x + 1f, head.yMax, card.width - 2f, MechsPanel.ColumnHead);

            ColumnCaptions(columns, palette);

            float row = columns.yMax;

            for (int i = 0; i < group.Mechs.Count; i++)
            {
                Pawn mech = group.Mechs[i];

                if (!MechsPanel.Passes(mech))
                    continue;

                MechRow.Draw(new Rect(card.x + 1f, row, card.width - 2f, MechsPanel.Rows), mech, palette,
                    MechFacts.PrioritiesLive(mech));

                row += MechsPanel.Rows;
            }

            if (rows == 0)
            {
                TabParts.RowLabel(new Rect(card.x + 14f, row, card.width - 28f, MechsPanel.Rows),
                    group.Mechs.Count == 0
                        ? "Empty. Select a mech and use Move to group."
                        : "Every mech here is filtered out.",
                    palette.TextDisabled, MechsFaces.Body, MechsFaces.Size.Prose);
            }

            return y + height + MechsPanel.Spacing;
        }

        /// <summary>
        /// A card with a plain heading rather than a group's controls: work, kind, flat and unlinked.
        /// </summary>
        private static float Card(Rect canvas, float y, UIColorPaletteDef palette, string title,
            string trailing, WorkTypeDef work, List<Pawn> mechs, Color? tint, string tip)
        {
            float height = CardHeight(mechs.Count, false);
            Rect card = new Rect(canvas.x, y, canvas.width, height);

            UIElementPainter.OutlineRounded(card, palette.Border, palette.PanelBackground);

            Rect head = new Rect(card.x + 1f, card.y + 1f, card.width - 2f, MechsPanel.CardHead);

            Widgets.DrawBoxSolid(head, palette.SurfaceSunken);
            Widgets.DrawLineHorizontal(head.x, head.yMax, head.width);

            TabParts.RowLabel(new Rect(head.x + 10f, head.y, head.width - 120f, head.height), title,
                tint ?? palette.TextPrimary, MechsFaces.Condensed, MechsFaces.Size.RailName);

            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = palette.TextDisabled;

            UITextControl.Label(new Rect(head.xMax - 110f, head.y, 100f, head.height),
                (trailing ?? string.Empty).ToUpperInvariant(), MechsFaces.Mono, MechsFaces.Size.Caption);

            Text.Anchor = anchor;
            GUI.color = color;

            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(head, (TipSignal) tip);

            float row = head.yMax;

            for (int i = 0; i < mechs.Count; i++)
            {
                MechRow.Draw(new Rect(card.x + 1f, row, card.width - 2f, MechsPanel.Rows), mechs[i], palette,
                    MechFacts.PrioritiesLive(mechs[i]), work);

                row += MechsPanel.Rows;
            }

            if (mechs.Count == 0)
            {
                TabParts.RowLabel(new Rect(card.x + 14f, row, card.width - 28f, MechsPanel.Rows), "Nothing.",
                    palette.TextDisabled, MechsFaces.Body, MechsFaces.Size.Prose);
            }

            return y + height + MechsPanel.Spacing;
        }

        /// <summary>
        /// The group's own controls: its number, its work mode, its recharge band and its area.
        ///
        /// <b>The work mode is one control, set once.</b> In RimWorld's table it is a cell repeated down every
        /// row of the group, and changing one row silently changes its neighbours because the mode belongs to
        /// the group. Clicking a segment calls <c>MechanitorControlGroup.SetWorkMode</c>, which is exactly
        /// what <c>MechanitorControlGroupGizmo.GetWorkModeOptions</c> calls; right clicking opens that menu
        /// itself, so a mod that adds a work mode is still reachable.
        /// </summary>
        private static void GroupHeader(Rect head, UIColorPaletteDef palette, MechanitorEntry owner,
            MechGroupEntry group)
        {
            float x = head.x + 10f;

            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextDisabled;

            string caption = ("Group " + group.Index).ToUpperInvariant();
            float captionWidth = UITextControl.Width(caption, MechsFaces.Mono, MechsFaces.Size.Caption) + 12f;

            UITextControl.Label(new Rect(x, head.y, captionWidth, head.height), caption, MechsFaces.Mono,
                MechsFaces.Size.Caption);

            Text.Anchor = anchor;
            GUI.color = color;

            x += captionWidth;

            // The right hand fields first, so the segments know where they must stop.
            float right = Fields(head, palette, group);

            List<MechWorkModeDef> all = MechModes.All();

            for (int i = 0; i < all.Count; i++)
            {
                MechWorkModeDef mode = all[i];
                MechWorkModeDef chosen = mode;

                float width = UITextControl.Width(mode.LabelCap, MechsFaces.Condensed, MechsFaces.Size.Chip)
                              + 26f;

                if (x + width > right - 8f)
                    break;

                Rect rect = new Rect(x, head.y + 3f, width, head.height - 6f);
                bool on = group.Mode == mode;

                if (ModeSegment(rect, mode, on, palette))
                {
                    UIGuard.Try("Mechs.SetWorkMode", () => group.Group.SetWorkMode(chosen),
                        "That work mode could not be set. The mechanitor's own command row still sets it.");

                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                x += width + 2f;
            }
        }

        /// <summary>One work mode: its icon, its name, and the tab's underline when it is the chosen one.</summary>
        private static bool ModeSegment(Rect rect, MechWorkModeDef mode, bool on, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect);

            if (on)
                UIElementPainter.OutlineRounded(rect, palette.ControlBackgroundFaded, palette.HoverOverlay);
            else if (over)
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            Color color = GUI.color;
            TextAnchor anchor = Text.Anchor;

            Rect icon = new Rect(rect.x + 5f, rect.center.y - 7f, 14f, 14f);

            if (mode.uiIcon != null)
            {
                GUI.color = on ? palette.TextPrimary : palette.TextDisabled;
                GUI.DrawTexture(icon, mode.uiIcon);
            }

            GUI.color = on ? palette.TextPrimary : over ? palette.TextSecondary : palette.TextDisabled;
            Text.Anchor = TextAnchor.MiddleLeft;

            UITextControl.Label(new Rect(icon.xMax + 4f, rect.y, rect.width - 24f, rect.height),
                mode.LabelCap, MechsFaces.Condensed, MechsFaces.Size.Chip);

            Text.Anchor = anchor;
            GUI.color = color;

            TooltipHandler.TipRegion(rect, (TipSignal) (mode.LabelCap + "\n\n" + mode.description));

            return Widgets.ButtonInvisible(rect) && !on;
        }

        /// <summary>
        /// The group's recharge band, allowed area and bandwidth, right aligned. Returns their left edge.
        ///
        /// <b>The area says <c>n/a</c> when it does not apply.</b> RimWorld's own header tip on the column
        /// admits that allowed areas only affect mechs in work and recharge modes; its table prints a value
        /// anyway. An escorting group's area does nothing and this says so.
        /// </summary>
        private static float Fields(Rect head, UIColorPaletteDef palette, MechGroupEntry group)
        {
            float right = head.xMax - 10f;

            right = Field(head, right, "band", group.Band.ToString(), palette,
                "Bandwidth the mechs in this group are between them occupying.", null);

            bool areaApplies = group.Mode == MechWorkModeDefOf.Work || group.Mode == MechWorkModeDefOf.Recharge;

            Area area = AreaOf(group);

            right = Field(head, right, "area", areaApplies ? (area == null ? "Unrestricted" : area.Label) : "n/a",
                palette,
                areaApplies
                    ? "Where the mechs in this group may go. Set it on each mech, the way you would a colonist."
                    : "Allowed areas only apply to mechs in work and recharge modes, so this group's does "
                      + "nothing right now. RimWorld says so in the tooltip on its own column and prints a "
                      + "value anyway.",
                areaApplies ? (Color?) null : palette.TextDisabled);

            FloatRange band = group.Group.mechRechargeThresholds;

            right = Field(head, right, "recharge at",
                Mathf.RoundToInt(band.min * 100f) + "% - " + Mathf.RoundToInt(band.max * 100f) + "%", palette,
                "The charge this group's mechs go to a recharger at, and the charge they leave at. Click to "
                + "open RimWorld's own recharge settings.", null,
                () => Find.WindowStack.Add(new Dialog_RechargeSettings(group.Group)));

            return right;
        }

        private static Area AreaOf(MechGroupEntry group)
        {
            for (int i = 0; i < group.Mechs.Count; i++)
            {
                Pawn mech = group.Mechs[i];

                if (mech != null && mech.playerSettings != null)
                    return mech.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap;
            }

            return null;
        }

        /// <summary>One small caps caption over a value, right aligned. Returns the x it ends at.</summary>
        private static float Field(Rect head, float right, string caption, string value,
            UIColorPaletteDef palette, string tip, Color? valueColor, System.Action clicked = null)
        {
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;
            bool wrap = Text.WordWrap;

            try
            {
                Text.WordWrap = false;

                float width = Mathf.Max(
                    UITextControl.Width(caption.ToUpperInvariant(), MechsFaces.Mono, MechsFaces.Size.Caption),
                    UITextControl.Width(value, MechsFaces.Mono, MechsFaces.Size.Figure)) + 14f;

                Rect cell = new Rect(right - width, head.y + 2f, width, head.height - 4f);

                bool over = clicked != null && Mouse.IsOver(cell);

                if (over)
                    Widgets.DrawBoxSolid(cell, palette.HoverOverlay);

                Text.Anchor = TextAnchor.UpperRight;
                GUI.color = palette.TextDisabled;

                UITextControl.Label(new Rect(cell.x, cell.y, cell.width - 4f, 12f), caption.ToUpperInvariant(),
                    MechsFaces.Mono, MechsFaces.Size.Caption);

                Text.Anchor = TextAnchor.LowerRight;
                GUI.color = valueColor ?? (over ? palette.TextPrimary : palette.TextSecondary);

                UITextControl.Label(new Rect(cell.x, cell.y + 10f, cell.width - 4f, cell.height - 10f), value,
                    MechsFaces.Mono, MechsFaces.Size.Figure);

                if (!tip.NullOrEmpty())
                    TooltipHandler.TipRegion(cell, (TipSignal) tip);

                if (clicked != null && Widgets.ButtonInvisible(cell))
                {
                    UIGuard.Try("Mechs.RechargeDialog", clicked,
                        "RimWorld's recharge settings could not be opened from here. The same dialog is on "
                        + "the mechanitor's command row.");

                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                return cell.x - 6f;
            }
            finally
            {
                Text.WordWrap = wrap;
                GUI.color = color;
                Text.Anchor = anchor;
            }
        }

        private static void ColumnCaptions(Rect rect, UIColorPaletteDef palette)
        {
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            GUI.color = palette.TextDisabled;
            Text.Anchor = TextAnchor.MiddleLeft;

            float x = rect.x + 10f + MechsPanel.Portrait + 10f;

            UITextControl.Label(new Rect(x, rect.y, MechsPanel.Name, rect.height),
                "MECH AND WORK PRIORITIES", MechsFaces.Mono, MechsFaces.Size.Caption);

            x += MechsPanel.Name + 10f;

            Text.Anchor = TextAnchor.MiddleRight;

            UITextControl.Label(new Rect(x, rect.y, MechsPanel.Cost, rect.height), "BW", MechsFaces.Mono,
                MechsFaces.Size.Caption);

            Text.Anchor = TextAnchor.MiddleLeft;

            UITextControl.Label(new Rect(x + MechsPanel.Cost + 10f, rect.y, 80f, rect.height), "ENERGY",
                MechsFaces.Mono, MechsFaces.Size.Caption);

            Text.Anchor = TextAnchor.MiddleRight;

            float right = rect.xMax - 10f - MechsPanel.Toggle - 10f;

            UITextControl.Label(new Rect(right - MechsPanel.Integrity, rect.y, MechsPanel.Integrity,
                rect.height), "HP", MechsFaces.Mono, MechsFaces.Size.Caption);

            Text.Anchor = TextAnchor.MiddleCenter;

            UITextControl.Label(new Rect(rect.xMax - 10f - MechsPanel.Toggle, rect.y, MechsPanel.Toggle,
                rect.height), "REP", MechsFaces.Mono, MechsFaces.Size.Caption);

            Text.Anchor = anchor;
            GUI.color = color;
        }

        // -------------------------------------------------------------------------------------------
        // Gestation
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The mechs that do not exist yet, which are spending bandwidth anyway.
        ///
        /// A readout rather than a bill editor. Starting a gestation happens at the gestator and the bills
        /// tab already exists; what belongs here is the fact that some of the bandwidth in the header is
        /// already gone.
        /// </summary>
        private static float Gestation(Rect canvas, float y, UIColorPaletteDef palette, ref bool any)
        {
            if (MechRoster.Gestating.Count == 0)
                return y;

            any = true;

            float height = CardHeight(MechRoster.Gestating.Count, false);
            Rect card = new Rect(canvas.x, y, canvas.width, height);

            UIElementPainter.OutlineRounded(card, palette.Border, palette.PanelBackground);

            Rect head = new Rect(card.x + 1f, card.y + 1f, card.width - 2f, MechsPanel.CardHead);

            Widgets.DrawBoxSolid(head, palette.SurfaceSunken);
            Widgets.DrawLineHorizontal(head.x, head.yMax, head.width);

            TabParts.RowLabel(new Rect(head.x + 10f, head.y, head.width - 140f, head.height), "Gestating",
                palette.Info, MechsFaces.Condensed, MechsFaces.Size.RailName);

            int reserved = 0;

            for (int i = 0; i < MechRoster.Gestating.Count; i++)
                reserved += MechRoster.Gestating[i].Band;

            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = palette.TextDisabled;

            UITextControl.Label(new Rect(head.xMax - 130f, head.y, 120f, head.height),
                (reserved + " BW RESERVED"), MechsFaces.Mono, MechsFaces.Size.Caption);

            float row = head.yMax;

            for (int i = 0; i < MechRoster.Gestating.Count; i++)
            {
                MechGestationEntry entry = MechRoster.Gestating[i];
                Rect line = new Rect(card.x + 1f, row, card.width - 2f, MechsPanel.Rows);

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextPrimary;

                UITextControl.LabelEllipses(new Rect(line.x + 14f, line.y, 220f, line.height),
                    entry.Produced == null ? "a mech" : entry.Produced.LabelCap, MechsFaces.Condensed,
                    MechsFaces.Size.RailName);

                GUI.color = palette.TextDisabled;

                UITextControl.LabelEllipses(new Rect(line.x + 240f, line.y, 240f, line.height),
                    (entry.State ?? string.Empty).ToUpperInvariant()
                    + (entry.Overseer == null ? string.Empty : "  -  " + entry.Overseer.LabelShortCap),
                    MechsFaces.Mono, MechsFaces.Size.Figure);

                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.Info;

                UITextControl.Label(new Rect(line.xMax - 60f, line.y, 50f, line.height), entry.Band + " bw",
                    MechsFaces.Mono, MechsFaces.Size.Figure);

                row += MechsPanel.Rows;
            }

            Text.Anchor = anchor;
            GUI.color = color;

            return y + height + MechsPanel.Spacing;
        }

        // -------------------------------------------------------------------------------------------
        // Buckets
        // -------------------------------------------------------------------------------------------

        private static void BuildWorkBuckets()
        {
            workBuckets.Clear();

            Collect();

            for (int i = 0; i < MechsPanel.Scratch.Count; i++)
            {
                List<WorkTypeDef> works = MechFacts.WorkTypes(MechsPanel.Scratch[i]);

                for (int w = 0; works != null && w < works.Count; w++)
                {
                    if (!workBuckets.Contains(works[w]))
                        workBuckets.Add(works[w]);
                }
            }

            workBuckets.SortBy(work => work.naturalPriority * -1);
        }

        private static void FillWork(WorkTypeDef work)
        {
            bucket.Clear();

            Collect();

            for (int i = 0; i < MechsPanel.Scratch.Count; i++)
            {
                Pawn mech = MechsPanel.Scratch[i];
                List<WorkTypeDef> works = MechFacts.WorkTypes(mech);

                if (works != null && works.Contains(work))
                    bucket.Add(mech);
            }

            // Most urgent first, then by name, so a card answers "who does this first" by being read from
            // the top. Priority 0 means switched off and sorts last rather than first.
            bucket.SortBy(mech =>
            {
                int priority = MechFacts.PriorityOf(mech, work);

                return priority <= 0 ? 999 : priority;
            });
        }

        private static void BuildKindBuckets()
        {
            kindBuckets.Clear();

            Collect();

            for (int i = 0; i < MechsPanel.Scratch.Count; i++)
            {
                ThingDef kind = MechsPanel.Scratch[i].def;

                if (kind != null && !kindBuckets.Contains(kind))
                    kindBuckets.Add(kind);
            }

            kindBuckets.SortBy(kind => kind.label);
        }

        private static void FillKind(ThingDef kind)
        {
            bucket.Clear();

            Collect();

            for (int i = 0; i < MechsPanel.Scratch.Count; i++)
            {
                if (MechsPanel.Scratch[i].def == kind)
                    bucket.Add(MechsPanel.Scratch[i]);
            }
        }

        /// <summary>Every mech the rail and the filters let through, in one list.</summary>
        private static void Collect()
        {
            List<Pawn> into = MechsPanel.Scratch;

            into.Clear();

            for (int i = 0; i < MechRoster.Mechanitors.Count; i++)
            {
                MechanitorEntry owner = MechRoster.Mechanitors[i];

                for (int g = 0; g < owner.Groups.Count; g++)
                {
                    MechGroupEntry group = owner.Groups[g];

                    if (!MechsPanel.InRail(owner, group))
                        continue;

                    for (int m = 0; m < group.Mechs.Count; m++)
                    {
                        if (MechsPanel.Passes(group.Mechs[m]))
                            into.Add(group.Mechs[m]);
                    }
                }
            }
        }

        private static void Empty(Rect canvas, UIColorPaletteDef palette)
        {
            string line = MechRoster.MechCount == 0
                ? "This colony has no mechs. A mechanitor with a mechlink can gestate one at a mech gestator."
                : "Nothing here matches the filters in the strip above.";

            TabParts.Note(new Rect(canvas.x + 14f, canvas.y, canvas.width - 28f, 80f), 10f, line, palette,
                MechsFaces.Body, MechsFaces.Size.Prose);
        }
    }

    /// <summary>The work modes there are, in the game's own order, rebuilt when the def list changes.</summary>
    internal static class MechModes
    {
        private static readonly List<MechWorkModeDef> modes = new List<MechWorkModeDef>();

        private static int builtFor = -1;

        internal static List<MechWorkModeDef> All()
        {
            List<MechWorkModeDef> all = DefDatabase<MechWorkModeDef>.AllDefsListForReading;

            if (builtFor == all.Count)
                return modes;

            builtFor = all.Count;
            modes.Clear();
            modes.AddRange(all);
            modes.SortBy(mode => mode.uiOrder);

            return modes;
        }
    }
}
