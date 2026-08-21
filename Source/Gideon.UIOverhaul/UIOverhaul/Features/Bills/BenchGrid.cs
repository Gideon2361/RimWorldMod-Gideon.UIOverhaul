using System;
using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// Every colonist worktable in the colony as cards, grouped by map, drawn into a rectangle.
    ///
    /// <b>One implementation, two hosts.</b> <see cref="Dialog_PickBench"/> is a window around this for the
    /// template flows, and it is the first step of <see cref="Dialog_AddWorkBill"/>. Written twice they would have
    /// drifted, and the drift would show as two different answers to the same question depending on how you got
    /// there.
    ///
    /// <b>The card carries what a float menu of names could not.</b> Which room the bench is in, how many bills it
    /// already holds out of the cap, and two flags worth a colour: no power on a bench that needs it, and full
    /// when it is at the cap. Those are the facts that decide which of three identical machining tables somebody
    /// wants.
    ///
    /// <b>Grouped by map because that is the case a name alone cannot disambiguate.</b> Two benches of the same def
    /// in two colonies read identically otherwise.
    ///
    /// <b>Instance rather than static, because the scroll position is state.</b> Two hosts on screen at once would
    /// otherwise fight over one scrollbar.
    /// </summary>
    internal sealed class BenchGrid
    {
        private const float CardWidth = 280f;
        private const float CardHeight = 96f;
        private const float CardGap = 8f;
        private const float GroupHeading = 26f;
        private const float Pad = 12f;

        private Vector2 scroll;
        private bool dragging;
        private float dragOffset;

        /// <summary>How many benches the last draw offered, for a host that wants to say so in a header.</summary>
        internal int Shown { get; private set; }

        /// <summary>
        /// Draws the grid and returns the bench clicked this frame, or null.
        ///
        /// <paramref name="allowFull"/> false makes a bench at its bill cap unpickable, which is right for
        /// anything that adds a bill and wrong for anything that only wants to name a bench.
        /// </summary>
        internal Building_WorkTable Draw(Rect rect, string search, bool allowFull, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, GzpPalette.BG);

            Rect inner = rect.ContractedBy(Pad);
            List<Map> maps = Find.Maps;

            Shown = 0;

            if (maps == null)
                return null;

            int columns = Mathf.Max(1, Mathf.FloorToInt((GzpPalette.ContentWidth(inner) + CardGap)
                                                        / (CardWidth + CardGap)));

            // Measured before drawing, because a scroll view needs its content height up front and the height
            // depends on how many benches each map contributes.
            float height = 0f;

            foreach (Map map in maps)
            {
                int count = In(map, search).Count;

                if (count == 0)
                    continue;

                Shown += count;
                height += GroupHeading + Mathf.CeilToInt(count / (float) columns) * (CardHeight + CardGap);
            }

            if (Shown == 0)
            {
                Color previous = GUI.color;
                GUI.color = GzpPalette.TextDim;

                Widgets.Label(new Rect(inner.x, inner.y + 4f, inner.width, 40f),
                    search.NullOrEmpty()
                        ? "No colonist workbench on any map."
                        : "No bench matches that.");

                GUI.color = previous;

                return null;
            }

            Rect view = new Rect(0f, 0f, GzpPalette.ContentWidth(inner), height);
            Building_WorkTable picked = null;

            Widgets.BeginScrollView(inner, ref scroll, view, false);

            try
            {
                float y = 0f;

                foreach (Map map in maps)
                {
                    List<Building_WorkTable> benches = In(map, search);

                    if (benches.Count == 0)
                        continue;

                    Heading(new Rect(0f, y, view.width, GroupHeading), Where(map));

                    y += GroupHeading;

                    for (int i = 0; i < benches.Count; i++)
                    {
                        Rect card = new Rect(i % columns * (CardWidth + CardGap),
                            y + i / columns * (CardHeight + CardGap), CardWidth, CardHeight);

                        if (Card(card, benches[i], allowFull, palette))
                            picked = benches[i];
                    }

                    y += Mathf.CeilToInt(benches.Count / (float) columns) * (CardHeight + CardGap);
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }

            GzpPalette.FlatScrollbar(inner, height, ref scroll, ref dragging, ref dragOffset);

            return picked;
        }

        /// <summary>Puts the scroll back to the top, for a host reopening on this step.</summary>
        internal void Reset()
        {
            scroll = Vector2.zero;
        }

        /// <summary>The worktables on one map that survive the search box.</summary>
        private static List<Building_WorkTable> In(Map map, string search)
        {
            List<Building_WorkTable> found = new List<Building_WorkTable>();
            List<Building> buildings = map?.listerBuildings?.allBuildingsColonist;

            if (buildings == null)
                return found;

            string term = (search ?? string.Empty).Trim();

            foreach (Building building in buildings)
            {
                if (!(building is Building_WorkTable bench))
                    continue;

                // Matched on the room as well as the name, so "kitchen" finds the stove and the butcher table
                // together, which is how somebody actually thinks about where a bill should go.
                if (term.Length > 0 && !Has(bench.LabelCap, term) && !Has(Room(bench), term))
                    continue;

                found.Add(bench);
            }

            return found;
        }

        private static bool Has(string text, string term)
        {
            return text != null && text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Draws one card and reports whether it was clicked.</summary>
        private static bool Card(Rect rect, Building_WorkTable bench, bool allowFull, UIColorPaletteDef palette)
        {
            int count = bench.billStack?.Count ?? 0;
            bool full = count >= BillCap.Current;
            bool blocked = full && !allowFull;
            bool dark = !Powered(bench);

            Color stripe = blocked
                ? GzpPalette.Bad
                : dark
                    ? GzpPalette.Warn
                    : GzpPalette.Accent;

            UIElementPainter.OutlineRounded(rect, stripe, GzpPalette.PanelBG);
            UIElementPainter.FillRounded(new Rect(rect.x, rect.y, 3f, rect.height), stripe);

            if (!blocked && Mouse.IsOver(rect))
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            Rect icon = new Rect(rect.x + 10f, rect.y + 10f, 44f, 44f);

            UIGuard.Try("Bills.BenchIcon",
                () => Widgets.DefIcon(icon, bench.def, bench.Stuff, alpha: blocked || dark ? 0.5f : 1f), null);

            Color previous = GUI.color;
            bool wrap = Text.WordWrap;

            Text.WordWrap = false;
            GUI.color = blocked || dark ? GzpPalette.TextDim : GzpPalette.Stat;

            Widgets.Label(new Rect(rect.x + 64f, rect.y + 8f, rect.width - 74f, 22f), bench.LabelCap);

            GUI.color = GzpPalette.TextDim;
            Text.Font = GameFont.Tiny;

            Widgets.Label(new Rect(rect.x + 64f, rect.y + 30f, rect.width - 74f, 20f), Room(bench) ?? "outdoors");

            // The line that decides between two identical benches, so it carries the colour when something is
            // wrong with the bench rather than with the choice.
            GUI.color = blocked ? GzpPalette.Bad : dark ? GzpPalette.Warn : GzpPalette.Stat;

            Widgets.Label(new Rect(rect.x + 64f, rect.yMax - 26f, rect.width - 74f, 20f), State(count, full, dark));

            Text.Font = GameFont.Small;
            Text.WordWrap = wrap;
            GUI.color = previous;

            if (blocked)
            {
                TooltipHandler.TipRegion(rect,
                    (TipSignal)("This bench already has its " + BillCap.Current + " bills."));

                return false;
            }

            return Widgets.ButtonInvisible(rect);
        }

        private static string State(int count, bool full, bool dark)
        {
            string bills = count == 0 ? "no bills yet" : count + " / " + BillCap.Current + " bills";

            if (full)
                bills = "full   " + count + " / " + BillCap.Current + " bills";

            return dark ? "no power   " + bills : bills;
        }

        /// <summary>
        /// Whether the bench has the power it needs, or needs none.
        ///
        /// A bench with no power comp is not unpowered, it is unpowered-irrelevant, so it reports as fine. Guarded
        /// because reaching a comp on a building a mod supplied is not something to trust with a bare cast.
        /// </summary>
        private static bool Powered(Building_WorkTable bench)
        {
            return UIGuard.Try("Bills.BenchPower", () =>
            {
                CompPowerTrader power = bench.TryGetComp<CompPowerTrader>();

                return power == null || power.PowerOn;
            }, true, null);
        }

        private static string Room(Building_WorkTable bench)
        {
            return UIGuard.Try("Bills.BenchRoom", () =>
            {
                Room room = bench.Position.GetRoom(bench.Map);

                return room == null || room.PsychologicallyOutdoors ? null : room.Role?.LabelCap;
            }, null, null);
        }

        private static string Where(Map map)
        {
            return UIGuard.Try("Bills.BenchMap", () => map?.Parent?.LabelCap ?? "This map", "This map", null);
        }

        private static void Heading(Rect rect, string text)
        {
            Color previous = GUI.color;

            Text.Font = GameFont.Tiny;
            GUI.color = GzpPalette.TextDim;

            Widgets.Label(new Rect(rect.x, rect.y + 6f, rect.width, 18f),
                (text ?? string.Empty).ToUpperInvariant());

            Text.Font = GameFont.Small;
            GUI.color = previous;
        }
    }
}
