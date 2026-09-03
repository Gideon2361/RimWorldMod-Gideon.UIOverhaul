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
    /// <b>Work priorities are not on the row.</b> They are read in the detail pane and compared across
    /// mechs in the by work view, both on cards carrying the full work name, because that is what the
    /// pawns tab does and its card grid exists precisely to stop a work label being cut short.
    ///
    /// <b>The shutdown line is drawn on the trough, not on the fill.</b>
    /// <c>Need_MechEnergy.ShutdownUntil</c> is 15, a constant in the game with no representation anywhere in
    /// its interface. It is a fact about the scale rather than about the value, so it belongs to the track.
    /// </summary>
    internal static class MechRow
    {
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

            // No rule between rows.
            //
            // <b>There was one, and it was drawn in white.</b> <c>Widgets.DrawLineHorizontal</c> paints in
            // whatever <c>GUI.color</c> happens to be, which is white unless the caller sets it, so every
            // row carried a bright line across it that read as a rendering fault rather than as a divider.
            //
            // Removed rather than recolored: a forty pixel row with a portrait, a name and a second line
            // has enough structure of its own, and the hover and selection washes are what actually
            // tell one row from the next. The two rules that remain on this screen are structural, separate
            // the card's header band from its rows, and are drawn in Border.
            float x = rect.x + 10f;

            // The portrait owns its own click, which is the camera jump. The rest of the row selects.
            Rect portrait = new Rect(x, rect.center.y - MechsPanel.Portrait * 0.5f, MechsPanel.Portrait,
                MechsPanel.Portrait);

            PawnPortraitCell.Draw(portrait, mech, palette, palette.SurfaceSunken);

            x = portrait.xMax + 10f;

            Identity(new Rect(x, rect.y, MechsPanel.Name, rect.height), mech, palette);

            x += MechsPanel.Name + 10f;

            // The by work view's own column: this mech's priority for the work type the card is about.
            //
            // <b>It is here rather than a reading because this is where setting it across mechs belongs.</b>
            // The pawns tab keeps per pawn detail in a card grid and sends comparison to the work tab; the
            // by work view is this tab's work tab, so the number on it is editable for the same reason the
            // work tab's cells are. The card's heading already names the work type in full, so the box
            // carries the figure alone and nothing is abbreviated to make room.
            if (emphasis != null)
            {
                x = PriorityBox(new Rect(x, rect.center.y - MechsPanel.PriorityBoxSize * 0.5f,
                    MechsPanel.PriorityBoxSize, MechsPanel.PriorityBoxSize), mech, emphasis, palette,
                    prioritiesLive);
            }

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

        private static void Identity(Rect rect, Pawn mech, UIColorPaletteDef palette)
        {
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = palette.TextPrimary;

            UITextControl.LabelEllipses(new Rect(rect.x, rect.y + 3f, rect.width, 18f), mech.LabelShortCap,
                MechsFaces.Condensed, MechsFaces.Size.RailName);

            Text.Anchor = anchor;
            GUI.color = color;

            Trailing(new Rect(rect.x, rect.y + 20f, rect.width, 15f), mech, palette);
        }

        /// <summary>
        /// The second line: what kind of mech this is, and the group's own name for it.
        ///
        /// <b>No work types here, and there were.</b> The row used to carry them as four letter chips, which
        /// is the one thing the pawns tab's card grid was built to avoid: its <c>MinCardWidth</c> is sized
        /// for the longest work label precisely so a name is never cut, and abbreviating "Hauling" to HAUL
        /// is that fault taken further. A mech's priorities are read in the detail pane, on the same cards
        /// with the same full names the pawns tab uses, and compared across mechs in the by work view.
        /// </summary>
        private static void Trailing(Rect rect, Pawn mech, UIColorPaletteDef palette)
        {
            if (rect.width < 20f)
                return;

            string weight = MechFacts.WeightClass(mech);
            string tag = MechFacts.Tag(mech);

            string text = weight + (MechFacts.IsWorkMech(mech) ? string.Empty : "  -  combat")
                                 + (tag.NullOrEmpty() ? string.Empty : "  -  " + tag);

            if (text.NullOrEmpty())
                return;

            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextDisabled;

            UITextControl.LabelEllipses(rect, text, MechsFaces.Mono, MechsFaces.Size.Figure);

            Text.Anchor = anchor;
            GUI.color = color;
        }

        // -------------------------------------------------------------------------------------------
        // Figures and controls
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// One editable priority, for the work type a by work card is about. Returns the x it ends at.
        ///
        /// The same range, the same wrap and the same buttons as the detail pane and the pawns tab: left
        /// click raises, right click lowers, and the circuit passes through 0 for off.
        /// </summary>
        private static float PriorityBox(Rect box, Pawn mech, WorkTypeDef work, UIColorPaletteDef palette,
            bool live)
        {
            int priority = MechFacts.PriorityOf(mech, work);
            bool over = Mouse.IsOver(box);

            UIElementPainter.OutlineRounded(box, palette.Border,
                priority == 0 ? palette.SurfaceRaised : palette.SurfaceSunken);

            if (over)
                Widgets.DrawBoxSolid(box, palette.HoverOverlay);

            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            Color tint = priority <= 0 ? palette.TextDisabled : WorkPanel.ColorOfPriority(priority, palette);

            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = live ? tint : new Color(tint.r, tint.g, tint.b, 0.45f);

            UITextControl.Label(box, priority.ToString(), MechsFaces.Mono, MechsFaces.Size.Priority);

            Text.Anchor = anchor;
            GUI.color = color;

            TooltipHandler.TipRegion(box, (TipSignal) (WorkPanel.LabelOf(work)
                + "\n\nLeft click raises the priority, right click lowers it."
                + (live
                    ? string.Empty
                    : "\n\nIdle right now: this mech's group is not in work mode.")));

            if (over && Event.current.type == EventType.MouseDown)
            {
                int next = priority + (Event.current.button == 1 ? -1 : 1);

                if (next > WorkPriorityRange.Lowest)
                    next = 0;
                else if (next < 0)
                    next = WorkPriorityRange.Lowest;

                MechFacts.SetPriority(mech, work, next);

                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                Event.current.Use();
            }

            return box.xMax + 10f;
        }

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
