using System.Collections.Generic;
using System.Text;
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
    /// One mech on the deck: who it is, what it is set to do, how charged it is and what shape it is in.
    ///
    /// <b>The second line is the work priorities.</b> RimWorld's table has no column for them because it has
    /// no screen for them at all, and they have been in every save since Biotech shipped, driving every work
    /// mech on the map. Two chips and a count of the rest, ordered by priority, so eight mechs can be read
    /// without selecting any of them.
    ///
    /// <b>The shutdown line is drawn on the trough, not on the fill.</b>
    /// <c>Need_MechEnergy.ShutdownUntil</c> is 15, a constant in the game with no representation anywhere in
    /// its interface. It is a fact about the scale rather than about the value, so it belongs to the track.
    /// </summary>
    internal static class MechRow
    {
        /// <summary>Most priority chips a row shows before it starts counting instead.</summary>
        private const int Chips = 2;

        private static readonly List<WorkTypeDef> ordered = new List<WorkTypeDef>();

        private static readonly StringBuilder tip = new StringBuilder();

        internal static void Draw(Rect rect, Pawn mech, UIColorPaletteDef palette, bool prioritiesLive,
            WorkTypeDef emphasis = null)
        {
            if (mech == null)
                return;

            bool selected = MechsPanel.Selected == mech;
            bool over = Mouse.IsOver(rect);

            if (selected)
            {
                Widgets.DrawBoxSolid(rect, palette.SelectionOverlay);
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 4f, 3f, rect.height - 8f),
                    MechsFaces.AccentOf(palette));
            }
            else if (over)
            {
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);
            }

            Widgets.DrawLineHorizontal(rect.x + 6f, rect.y, rect.width - 12f);

            float x = rect.x + 10f;

            // The portrait owns its own click, which is the camera jump. The rest of the row selects.
            Rect portrait = new Rect(x, rect.center.y - MechsPanel.Portrait * 0.5f, MechsPanel.Portrait,
                MechsPanel.Portrait);

            PawnPortraitCell.Draw(portrait, mech, palette, palette.SurfaceSunken);

            x = portrait.xMax + 10f;

            Identity(new Rect(x, rect.y, MechsPanel.Name, rect.height), mech, palette, prioritiesLive,
                emphasis);

            x += MechsPanel.Name + 10f;

            Figure(new Rect(x, rect.y, MechsPanel.Cost, rect.height),
                MechFacts.BandwidthCost(mech) + " bw", palette.TextSecondary);

            x += MechsPanel.Cost + 10f;

            // Right hand fixtures first, so the energy track takes whatever is genuinely left.
            float right = rect.xMax - 10f;

            Rect box = new Rect(right - MechsPanel.Toggle, rect.center.y - 12f, MechsPanel.Toggle, 24f);

            Repair(box, mech, palette);

            right -= MechsPanel.Toggle + 10f;

            float integrity = MechFacts.Integrity(mech);

            Figure(new Rect(right - MechsPanel.Integrity, rect.y, MechsPanel.Integrity, rect.height),
                Mathf.RoundToInt(integrity * 100f) + "%",
                integrity < 0.999f ? palette.Warning : palette.TextSecondary);

            right -= MechsPanel.Integrity + 10f;

            ChargeFlow flow = MechFacts.Flow(mech);
            string word = ChargePill.Word(flow);
            float pillWidth = TabParts.PillWidth(word, 9999f, MechsFaces.Mono, MechsFaces.Size.Caption);

            ChargePill.Draw(rect, right - pillWidth, rect.center.y - 8f, flow, palette,
                MechsFaces.Size.Caption);

            right -= pillWidth + 8f;

            Figure(new Rect(right - MechsPanel.Percent, rect.y, MechsPanel.Percent, rect.height),
                MechFacts.ChargeText(mech), palette.TextSecondary);

            right -= MechsPanel.Percent + 8f;

            Track(new Rect(x, rect.center.y - 4f, Mathf.Max(24f, right - x), 7f), mech, palette);

            // The row's own click, taken after every control that sits inside it so none of them is stolen.
            if (Widgets.ButtonInvisible(rect))
            {
                MechsPanel.Select(mech);

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            if (over)
                TooltipHandler.TipRegion(rect, (TipSignal) Tooltip(mech, prioritiesLive));
        }

        // -------------------------------------------------------------------------------------------
        // Name and work
        // -------------------------------------------------------------------------------------------

        private static void Identity(Rect rect, Pawn mech, UIColorPaletteDef palette, bool prioritiesLive,
            WorkTypeDef emphasis)
        {
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = palette.TextPrimary;

            UITextControl.LabelEllipses(new Rect(rect.x, rect.y + 3f, rect.width, 18f), mech.LabelShortCap,
                MechsFaces.Condensed, MechsFaces.Size.RailName);

            Text.Anchor = anchor;
            GUI.color = color;

            Rect line = new Rect(rect.x, rect.y + 20f, rect.width, 15f);

            float x = line.x;

            if (MechFacts.IsWorkMech(mech))
                x = Priorities(line, x, mech, palette, prioritiesLive, emphasis);

            Trailing(new Rect(x, line.y, Mathf.Max(0f, line.xMax - x), line.height), mech, palette,
                x > line.x);
        }

        /// <summary>Up to two priority chips, then a count of the rest. Returns the x it ends at.</summary>
        private static float Priorities(Rect line, float x, Pawn mech, UIColorPaletteDef palette,
            bool prioritiesLive, WorkTypeDef emphasis)
        {
            Order(mech, emphasis);

            int drawn = 0;

            for (int i = 0; i < ordered.Count && drawn < Chips; i++)
            {
                WorkTypeDef work = ordered[i];
                int priority = MechFacts.PriorityOf(mech, work);

                string text = MechFacts.Abbreviate(work) + " " + priority;
                float width = TabParts.PillWidth(text, 9999f, MechsFaces.Mono, MechsFaces.Size.Caption);

                if (x + width > line.xMax - 18f)
                    break;

                Color tint = priority <= 0 ? palette.TextDisabled : WorkPanel.ColorOfPriority(priority, palette);

                if (!prioritiesLive)
                    tint = new Color(tint.r, tint.g, tint.b, 0.4f);

                TabParts.Pill(line, x, line.y, text, tint, palette, 9999f, null, MechsFaces.Mono,
                    MechsFaces.Size.Caption);

                x += width + 3f;
                drawn++;
            }

            if (drawn < ordered.Count)
            {
                string more = "+" + (ordered.Count - drawn);
                float width = TabParts.PillWidth(more, 9999f, MechsFaces.Mono, MechsFaces.Size.Caption);

                if (x + width <= line.xMax)
                {
                    TabParts.Pill(line, x, line.y, more, palette.TextDisabled, palette, 9999f, null,
                        MechsFaces.Mono, MechsFaces.Size.Caption);

                    x += width + 3f;
                }
            }

            return x;
        }

        /// <summary>
        /// The mech's work types, most urgent first.
        ///
        /// Priority 0 means switched off and sorts last rather than first, so the two chips a row has room
        /// for are the two things this mech actually does. In the by-work view the work type whose card this
        /// is comes first whatever its number, since that is the column the reader is scanning.
        /// </summary>
        private static void Order(Pawn mech, WorkTypeDef emphasis)
        {
            ordered.Clear();

            List<WorkTypeDef> works = MechFacts.WorkTypes(mech);

            if (works == null)
                return;

            ordered.AddRange(works);

            ordered.SortBy(work =>
            {
                if (work == emphasis)
                    return -1;

                int priority = MechFacts.PriorityOf(mech, work);

                return priority <= 0 ? 999 : priority;
            });
        }

        /// <summary>The weight class, or the group's tag for this mech when it has one.</summary>
        private static void Trailing(Rect rect, Pawn mech, UIColorPaletteDef palette, bool afterChips)
        {
            if (rect.width < 20f)
                return;

            string tag = MechFacts.Tag(mech);
            string weight = MechFacts.WeightClass(mech);

            string text = tag.NullOrEmpty()
                ? (afterChips ? weight : weight + (MechFacts.IsWorkMech(mech) ? string.Empty : "  -  combat"))
                : tag;

            if (text.NullOrEmpty())
                return;

            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextDisabled;

            UITextControl.LabelEllipses(new Rect(rect.x + (afterChips ? 3f : 0f), rect.y, rect.width, rect.height),
                text, MechsFaces.Mono, MechsFaces.Size.Figure);

            Text.Anchor = anchor;
            GUI.color = color;
        }

        // -------------------------------------------------------------------------------------------
        // Figures and controls
        // -------------------------------------------------------------------------------------------

        private static void Figure(Rect rect, string text, Color color)
        {
            TextAnchor anchor = Text.Anchor;
            Color previous = GUI.color;

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = color;

            UITextControl.Label(rect, text, MechsFaces.Mono, MechsFaces.Size.Figure);

            Text.Anchor = anchor;
            GUI.color = previous;
        }

        /// <summary>The charge track, with the shutdown line drawn across the trough it belongs to.</summary>
        private static void Track(Rect rect, Pawn mech, UIColorPaletteDef palette)
        {
            float charge = MechFacts.Charge(mech);

            if (charge < 0f)
                return;

            UIElementPainter.Outline(rect, palette.Border, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(1f);

            Color fill = charge * 100f <= MechFacts.ShutdownAt
                ? palette.Danger
                : charge < 0.4f
                    ? palette.Warning
                    : palette.Success;

            Widgets.DrawBoxSolid(new Rect(inner.x, inner.y, inner.width * charge, inner.height), fill);

            float mark = inner.x + inner.width * (MechFacts.ShutdownAt / 100f);

            Widgets.DrawBoxSolid(new Rect(mark, rect.y, 1f, rect.height), palette.ControlBackgroundFaded);
        }

        /// <summary>
        /// The auto repair box.
        ///
        /// Drawn straight onto <c>CompMechRepairable.autoRepair</c>, which is the field vanilla's own column
        /// writes. Nothing else is involved: no setting of ours holds a copy of it.
        /// </summary>
        private static void Repair(Rect box, Pawn mech, UIColorPaletteDef palette)
        {
            CompMechRepairable comp = MechFacts.Repairable(mech);

            if (comp == null)
                return;

            bool value = comp.autoRepair;

            Rect square = new Rect(box.center.x - 8f, box.center.y - 8f, 16f, 16f);

            if (UICheckboxControl.Draw(square, ref value, palette))
                comp.autoRepair = value;

            TooltipHandler.TipRegion(square, (TipSignal) ("Repair this mech automatically.\n\nThe overseer "
                                                          + "does the work, and it costs them time and a "
                                                          + "little of the mech's own energy per point of "
                                                          + "damage repaired."));
        }

        private static string Tooltip(Pawn mech, bool prioritiesLive)
        {
            tip.Length = 0;

            tip.Append(mech.LabelCap);

            string weight = MechFacts.WeightClass(mech);

            if (!weight.NullOrEmpty())
                tip.Append("\n").Append(weight).Append(", ").Append(MechFacts.BandwidthCost(mech))
                    .Append(" bandwidth");

            Pawn overseer = mech.GetOverseer();

            if (overseer != null)
                tip.Append("\nOverseen by ").Append(overseer.LabelShortCap);

            List<WorkTypeDef> works = MechFacts.WorkTypes(mech);

            if (works != null && works.Count > 0)
            {
                tip.Append("\n\nWork priorities");

                for (int i = 0; i < works.Count; i++)
                {
                    int priority = MechFacts.PriorityOf(mech, works[i]);

                    tip.Append("\n  ").Append(WorkPanel.LabelOf(works[i])).Append("  ")
                        .Append(priority <= 0 ? "off" : priority.ToString());
                }

                if (!prioritiesLive)
                {
                    tip.Append("\n\nThese are idle: this mech's group is not in work mode, so its think tree "
                               + "never reaches the work giver.");
                }
            }

            return tip.ToString();
        }
    }
}
