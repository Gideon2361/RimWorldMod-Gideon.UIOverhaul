using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Inspector;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Power
{
    /// <summary>
    /// The power tab: every grid on the map, and how long each one has left.
    ///
    /// <b>The headline is a countdown, not a wattage.</b> Minus eight hundred watts is alarming on a colony
    /// with one battery and irrelevant on a colony with twelve, which is why the overlay showing that number
    /// has never been enough. Stored energy over the deficit is hours until the lights go out, and that single
    /// division is the thing a power screen exists to say.
    ///
    /// <b>Grids plural is the other half.</b> A colony ends up with grids it did not mean to have, and the
    /// only sign of it today is something going dark. <c>hasPowerSource</c> already knows, so a net of six
    /// lights wired to nothing is named as a gap rather than shown as a zero.
    ///
    /// <b>None of it is new data.</b> The read side lives in <see cref="PowerFacts"/> and every figure on it
    /// comes off <c>PowerNet</c>, which maintains them whether anything reads them or not.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class PowerPanel
    {
        internal const float WindowWidth = 1120f;
        internal const float WindowHeight = 700f;

        private const float Pad = 12f;
        private const float RailWidth = 210f;
        private const float HeaderHeight = 66f;
        private const float RowGap = 6f;

        private static Vector2 scroll;
        private static float viewHeight = 1f;

        private static readonly List<DrawRow> Makers = new List<DrawRow>();
        private static readonly List<DrawRow> Takers = new List<DrawRow>();
        private static readonly List<FaultRow> Faults = new List<FaultRow>();

        internal static void Draw(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            Map map = Find.CurrentMap;

            Rect body = inRect.ContractedBy(Pad);

            List<GridRow> grids = PowerFacts.All(map);

            if (grids.Count == 0)
            {
                TabParts.Line(body, body.y + 40f,
                    map == null
                        ? "No map to read a grid on."
                        : "Nothing here is wired up yet.", palette.TextSecondary);

                return;
            }

            GridRow shown = Current(grids);

            Header(new Rect(body.x, body.y, body.width, HeaderHeight), shown, palette);

            float top = body.y + HeaderHeight + Pad;
            Rect below = new Rect(body.x, top, body.width, body.yMax - top);

            Rail(new Rect(below.x, below.y, RailWidth, below.height), grids, palette);

            Rect main = new Rect(below.x + RailWidth + Pad, below.y,
                below.width - RailWidth - Pad, below.height);

            Rect view = new Rect(0f, 0f, main.width - 18f, viewHeight);

            Widgets.BeginScrollView(main, ref scroll, view);

            float y = Balance(view, 0f, shown, palette);

            y = Lists(view, y, shown, palette);
            y = Burners(view, y, shown, palette);
            y = Cellar(view, y, shown, palette);

            if (Event.current.type == EventType.Layout)
                viewHeight = Mathf.Max(1f, y);

            Widgets.EndScrollView();
        }

        /// <summary>
        /// The grid being shown, falling back to the largest.
        ///
        /// <b>Held by net rather than by index,</b> because the list is sorted by size and a grid can grow or
        /// shrink between frames. An index would quietly move the selection onto a different grid the moment
        /// somebody built a lamp.
        /// </summary>
        private static GridRow Current(List<GridRow> grids)
        {
            for (int i = 0; i < grids.Count; i++)
            {
                if (grids[i].net == PowerFacts.Selected)
                    return grids[i];
            }

            PowerFacts.Selected = grids[0].net;

            return grids[0];
        }

        // -------------------------------------------------------------------------------------------
        // Header
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The tab's own bolt, drawn beside the title the way the ideoligions header draws a faith's crest.
        ///
        /// <b>The same texture the button on the bar uses,</b> so the glyph a player clicked to get here is
        /// the glyph waiting at the top of the screen. It is the mod's own icon rather than a vanilla one,
        /// which is why there is a file to point at.
        ///
        /// <b>Tinted from the palette rather than to a literal yellow.</b> Warning is the palette's amber and
        /// the nearest thing it keeps to yellow; taking it from there means the header follows a theme change
        /// instead of staying the one colour that was hardcoded when it was written.
        ///
        /// <b>Loaded in a static constructor under <c>StaticConstructorOnStartup</c>,</b> because the game
        /// warns about any type holding a static texture field without it: the check reads the field's type
        /// rather than watching when the texture is fetched.
        /// </summary>
        private static readonly Texture2D Bolt;

        static PowerPanel()
        {
            // Through a local, because a readonly field can only be assigned in the constructor itself and
            // the guard does its work in a closure.
            Texture2D bolt = null;

            UIGuard.Try("Power.Bolt",
                () => bolt = ContentFinder<Texture2D>.Get("UI/MainButtonIcons/Power", false),
                "The power header has no glyph this session. Everything on the tab still reads.");

            Bolt = bolt;
        }

        /// <summary>Side of the header glyph, and the air between it and the title.</summary>
        private const float BoltSize = 34f;

        private const float BoltGap = 10f;

        private static void Header(Rect rect, GridRow grid, UIColorPaletteDef palette)
        {
            // PanelBackground rather than SurfaceRaised: the two are the same value as the window behind
            // them in the default dark palette, so a raised header had nothing but its border to separate
            // it from the page and from the sunken rail beside it.
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            Rect inner = rect.ContractedBy(10f);

            float text = inner.x;

            if (Bolt != null)
            {
                Rect glyph = new Rect(inner.x, inner.y + (inner.height - BoltSize) * 0.5f, BoltSize,
                    BoltSize);

                Color previous = GUI.color;

                GUI.color = palette.Warning;
                GUI.DrawTexture(glyph, Bolt);
                GUI.color = previous;

                text = glyph.xMax + BoltGap;
            }

            TabParts.RowLabel(new Rect(text, inner.y + 2f, 320f, 26f), "Power", palette.Accent,
                GameFont.Medium, PowerFaces.Display, PowerFaces.Size.Title);

            TabParts.RowLabel(new Rect(text, inner.y + 28f, 320f, 18f),
                grid.name + "  -  " + grid.buildings + (grid.buildings == 1 ? " building" : " buildings"),
                palette.TextSecondary, GameFont.Tiny, PowerFaces.Condensed, PowerFaces.Size.Subtitle);

            float right = inner.xMax;

            right = Readout(inner, right, Mathf.RoundToInt(grid.stored).ToString("N0"), "Wd stored", null,
                palette);

            right = Readout(inner, right, PowerFacts.Power(grid.balance), "balance",
                grid.balance < 0f ? palette.Warning : palette.Success, palette);

            // The countdown is laid out last so it lands furthest from the title, which on a row read from the
            // right is the first thing reached.
            Readout(inner, right, PowerFacts.Hours(grid.hoursLeft), "to empty",
                grid.hoursLeft < 0f
                    ? palette.TextDisabled
                    : grid.hoursLeft < 12f
                        ? palette.Danger
                        : palette.Warning,
                palette);
        }

        private static float Readout(Rect inner, float right, string value, string caption, Color? tint,
            UIColorPaletteDef palette)
        {
            string label = PowerFaces.Caps(caption);

            float width = Mathf.Max(
                UITextControl.Width(value, PowerFaces.Mono, PowerFaces.Size.Readout),
                UITextControl.Width(label, PowerFaces.Mono, PowerFaces.Size.Caption)) + 4f;

            float figure = UITextControl.LineHeight(PowerFaces.Mono, PowerFaces.Size.Readout);
            float under = UITextControl.LineHeight(PowerFaces.Mono, PowerFaces.Size.Caption);

            float top = inner.y + (inner.height - figure - under - 2f) * 0.5f;

            Rect band = new Rect(right - width, top, width, figure + under + 2f);

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;

                GUI.color = tint ?? palette.TextPrimary;
                UITextControl.LabelEllipses(new Rect(band.x, band.y, band.width, figure), value,
                    PowerFaces.Mono, PowerFaces.Size.Readout);

                GUI.color = palette.TextSecondary;
                UITextControl.LabelEllipses(new Rect(band.x, band.y + figure + 2f, band.width, under), label,
                    PowerFaces.Mono, PowerFaces.Size.Caption);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }

            return band.x - 14f;
        }

        // -------------------------------------------------------------------------------------------
        // Rail
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// One row per grid: a status dot, the grid's name, and its balance.
        ///
        /// <b>The dot is a glyph rather than a swatch</b> because it is a small centered mark, not a stripe
        /// down the leading edge. Colour carries the same three states as the figure beside it -- no source,
        /// running at a deficit, or healthy -- so the row reads at a glance and again on inspection.
        /// </summary>
        private static Vector2 railScroll;
        private static bool railDragging;
        private static float railDragOffset;

        private static void Rail(Rect rect, List<GridRow> grids, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            List<UIRailElement> elements = new List<UIRailElement>();

            elements.Add(new UIRailSectionHeaderControl(PowerFaces.Caps("Grids"))
            {
                Face = PowerFaces.Mono,
                Points = PowerFaces.Size.RailHead,
                Color = palette.TextDisabled
            });

            for (int i = 0; i < grids.Count; i++)
            {
                GridRow grid = grids[i];
                bool on = grid.net == PowerFacts.Selected;

                Color state = !grid.hasSource
                    ? palette.Danger
                    : grid.balance < 0f
                        ? palette.Warning
                        : palette.Success;

                elements.Add(new UIRailClickableEntry(i.ToString(), grid.name)
                {
                    Rise = 30f,
                    Face = PowerFaces.Condensed,
                    Points = PowerFaces.Size.RailName,
                    TextColor = on ? palette.Accent : (Color?) null,
                    Trailing = grid.hasSource ? PowerFacts.Power(grid.balance) : "dark",
                    CountFace = PowerFaces.Mono,
                    CountPoints = PowerFaces.Size.RailCount,
                    CountColor = state,
                    IconSize = 7f,
                    Glyph = (slot, color) => Widgets.DrawBoxSolid(slot, state)
                });
            }

            string picked = UIRailControl.Draw(rect.ContractedBy(6f), elements,
                IndexOfSelected(grids), ref railScroll, ref railDragging, ref railDragOffset, palette, false);

            if (picked == null)
                return;

            int index;

            if (int.TryParse(picked, out index) && index >= 0 && index < grids.Count)
                PowerFacts.Selected = grids[index].net;
        }

        private static string IndexOfSelected(List<GridRow> grids)
        {
            for (int i = 0; i < grids.Count; i++)
            {
                if (grids[i].net == PowerFacts.Selected)
                    return i.ToString();
            }

            return null;
        }

        // -------------------------------------------------------------------------------------------
        // Balance
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// What the grid makes against what it takes, and what the batteries are doing about the difference.
        /// </summary>
        private static float Balance(Rect view, float y, GridRow grid, UIColorPaletteDef palette)
        {
            Rect box = new Rect(view.x, y, view.width, 132f);

            UIElementPainter.OutlineRounded(box, palette.Border, palette.PanelBackground);

            Rect cap = new Rect(box.x, box.y, box.width, 22f);

            UIElementPainter.FillRounded(cap,
                UIElementPainter.Composite(palette.PanelBackground, palette.HoverOverlay));

            TabParts.RowLabel(new Rect(cap.x + 10f, cap.y, cap.width - 20f, cap.height),
                PowerFaces.Caps("Balance"), palette.TextSecondary, GameFont.Tiny, PowerFaces.Mono,
                PowerFaces.Size.BlockHead);

            Rect inner = new Rect(box.x + 12f, cap.yMax + 8f, box.width - 24f, box.height - cap.height - 16f);

            float third = inner.width / 3f;

            Figure(new Rect(inner.x, inner.y, third, 42f), "Producing",
                Mathf.RoundToInt(grid.producing).ToString("N0") + " W", palette.Success, palette);

            Figure(new Rect(inner.x + third, inner.y, third, 42f), "Drawing",
                Mathf.RoundToInt(grid.drawing).ToString("N0") + " W", palette.Danger, palette);

            Figure(new Rect(inner.x + third * 2f, inner.y, third, 42f), "Balance",
                PowerFacts.Power(grid.balance),
                grid.balance < 0f ? palette.Warning : palette.Success, palette);

            Rect track = new Rect(inner.x, inner.y + 48f, inner.width, 12f);

            UIElementPainter.OutlineRounded(track, palette.Border, palette.SurfaceSunken);

            float total = Mathf.Max(1f, grid.producing + grid.drawing);

            Widgets.DrawBoxSolid(new Rect(track.x + 1f, track.y + 1f,
                (track.width - 2f) * (grid.producing / total), track.height - 2f), palette.Success);

            Widgets.DrawBoxSolid(new Rect(track.x + 1f + (track.width - 2f) * (grid.producing / total),
                track.y + 1f, (track.width - 2f) * (grid.drawing / total), track.height - 2f), palette.Danger);

            Charge(new Rect(inner.x, track.yMax + 6f, inner.width, 22f), grid, palette);

            return box.yMax + RowGap;
        }

        /// <summary>The point size the grid's state pill sets at.</summary>
        private const float PillPoints = 6.75f;

        /// <summary>
        /// What the batteries hold, as a bar, with the grid's state moving beside it.
        ///
        /// <b>It was a sentence and a sentence is the wrong shape for it.</b> "Filling. 2,214 of 3,600 Wd
        /// stored." makes the reader do the division to find out how full that is, which is the only thing
        /// anybody wants from the line. A bar answers it without arithmetic and leaves the figures beside it
        /// for whoever wants the exact number.
        ///
        /// <b>The pill is the same control the inspect pane draws on a single battery.</b> One grid is one
        /// bank of them, and the state of that bank is the same four words moving the same four ways.
        ///
        /// <b>A grid with no batteries gets neither.</b> An empty bar reading nought of nought is a reading;
        /// saying there is nothing to read is information.
        /// </summary>
        private static void Charge(Rect rect, GridRow grid, UIColorPaletteDef palette)
        {
            if (!grid.hasSource)
            {
                TabParts.RowLabel(rect, "Nothing on this grid can generate. It is a gap, not a shortage.",
                    palette.Danger, GameFont.Small, PowerFaces.Body, PowerFaces.Size.Body);

                return;
            }

            if (grid.capacity <= 0f)
            {
                TabParts.RowLabel(rect, "No batteries on this grid. It runs on what it makes.",
                    palette.TextDisabled, GameFont.Small, PowerFaces.Body, PowerFaces.Size.Body);

                return;
            }

            ChargeFlow flow = ChargePill.Flow(grid.stored, grid.capacity, grid.balance);

            float wide = ChargePill.Width(flow, PillPoints);
            float tall = ChargePill.Height(PillPoints);

            ChargePill.Draw(rect, rect.x, rect.y + (rect.height - tall) * 0.5f, flow, palette, PillPoints);

            string figures = Mathf.RoundToInt(grid.stored).ToString("N0") + " / "
                             + Mathf.RoundToInt(grid.capacity).ToString("N0") + " Wd";

            float numbers = UITextControl.Width(figures, PowerFaces.Mono, PowerFaces.Size.Small) + 8f;

            Rect bar = new Rect(rect.x + wide + 10f, rect.y + (rect.height - 12f) * 0.5f,
                Mathf.Max(40f, rect.width - wide - 10f - numbers), 12f);

            UIElementPainter.OutlineRounded(bar, palette.Border, palette.SurfaceSunken);

            float share = Mathf.Clamp01(grid.stored / grid.capacity);

            if (share > 0f)
            {
                Widgets.DrawBoxSolid(new Rect(bar.x + 1f, bar.y + 1f,
                        Mathf.Max(1f, (bar.width - 2f) * share), bar.height - 2f),
                    InspectPaneParts.Level(share, palette));
            }

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextSecondary;

                UITextControl.LabelEllipses(new Rect(bar.xMax, rect.y, numbers, rect.height), figures,
                    PowerFaces.Mono, PowerFaces.Size.Small);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }
        }

        private static void Figure(Rect rect, string caption, string value, Color tint,
            UIColorPaletteDef palette)
        {
            TabParts.RowLabel(new Rect(rect.x, rect.y, rect.width, 14f), PowerFaces.Caps(caption),
                palette.TextDisabled, GameFont.Tiny, PowerFaces.Mono, PowerFaces.Size.Label);

            TabParts.RowLabel(new Rect(rect.x, rect.y + 15f, rect.width, 26f), value, tint, GameFont.Medium,
                PowerFaces.Mono, PowerFaces.Size.Figure);
        }

        // -------------------------------------------------------------------------------------------
        // Producers and consumers
        // -------------------------------------------------------------------------------------------

        private static float Lists(Rect view, float y, GridRow grid, UIColorPaletteDef palette)
        {
            PowerFacts.Traders(grid.net, true, Makers);
            PowerFacts.Traders(grid.net, false, Takers);

            float half = (view.width - RowGap) * 0.5f;

            float left = List(new Rect(view.x, y, half, 0f), y, "Producing", Makers, true, palette);

            float right = List(new Rect(view.x + half + RowGap, y, half, 0f), y, "Drawing", Takers, false,
                palette);

            return Mathf.Max(left, right);
        }

        /// <summary>How many rows each of the two lists shows before it scrolls.</summary>
        private const int ListRows = 10;

        private const float ListRow = 22f;

        private static Vector2 makerScroll;
        private static Vector2 takerScroll;

        /// <summary>
        /// One side of the make-and-take pair, ten rows tall whatever it holds.
        ///
        /// <b>Both sides are the same height on purpose.</b> Sized to their contents they staggered badly: a
        /// colony with three generators and eighteen kinds of consumer got a short box beside a very tall one,
        /// with the block under them starting at the bottom of the taller. Two columns that start and end
        /// together read as a comparison, which is what they are.
        ///
        /// <b>Ten rows, then it scrolls.</b> Ten is enough for every producer any colony has and for the
        /// consumers worth acting on, which are the ones at the top; the tail is reachable rather than
        /// dropped, and neither list dictates how tall the screen is.
        /// </summary>
        private static float List(Rect view, float y, string title, List<DrawRow> rows, bool makers,
            UIColorPaletteDef palette)
        {
            float height = 30f + ListRows * ListRow + 8f;

            Rect box = new Rect(view.x, y, view.width, height);

            UIElementPainter.OutlineRounded(box, palette.Border, palette.PanelBackground);

            Rect cap = new Rect(box.x, box.y, box.width, 22f);

            UIElementPainter.FillRounded(cap,
                UIElementPainter.Composite(palette.PanelBackground, palette.HoverOverlay));

            float total = 0f;

            for (int i = 0; i < rows.Count; i++)
                total += Mathf.Abs(rows[i].watts);

            TabParts.RowLabel(new Rect(cap.x + 10f, cap.y, cap.width - 20f, cap.height),
                PowerFaces.Caps(title), palette.TextSecondary, GameFont.Tiny, PowerFaces.Mono,
                PowerFaces.Size.BlockHead);

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextDisabled;

                UITextControl.LabelEllipses(new Rect(cap.x + 10f, cap.y, cap.width - 20f, cap.height),
                    PowerFaces.Caps(Mathf.RoundToInt(total).ToString("N0") + " W from " + rows.Count),
                    PowerFaces.Mono, PowerFaces.Size.BlockHead);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }

            if (rows.Count == 0)
            {
                TabParts.RowLabel(new Rect(box.x + 12f, cap.yMax + 4f, box.width - 24f, 20f),
                    makers ? "Nothing here generates." : "Nothing here draws.", palette.TextDisabled,
                    GameFont.Small, PowerFaces.Body, PowerFaces.Size.Body);

                return box.yMax + RowGap;
            }

            Rect outer = new Rect(box.x, cap.yMax + 4f, box.width, box.yMax - cap.yMax - 8f);

            bool overflows = rows.Count > ListRows;

            // The view is only narrowed when it actually scrolls, so a list of three does not leave a strip of
            // empty gutter down its right side pretending a bar is missing.
            Rect inner = new Rect(0f, 0f, outer.width - (overflows ? 18f : 0f), rows.Count * ListRow);

            if (makers)
                Widgets.BeginScrollView(outer, ref makerScroll, inner);
            else
                Widgets.BeginScrollView(outer, ref takerScroll, inner);

            // The bar is share of the largest rather than share of the total: against the total the top row is
            // the only visible bar, and the shape of the list is exactly what makes it worth drawing.
            float biggest = Mathf.Max(1f, Mathf.Abs(rows[0].watts));

            float cursor = 0f;

            for (int i = 0; i < rows.Count; i++)
                cursor = Row(inner, cursor, rows[i], biggest, makers, palette);

            Widgets.EndScrollView();

            return box.yMax + RowGap;
        }

        private static float Row(Rect box, float y, DrawRow row, float biggest, bool maker,
            UIColorPaletteDef palette)
        {
            const float height = 22f;
            const float share = 70f;
            const float figure = 78f;

            Rect band = new Rect(box.x + 12f, y, box.width - 24f, height);

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;

                GUI.color = row.idle == row.count ? palette.TextDisabled
                    : maker ? palette.Success : palette.Danger;

                UITextControl.LabelEllipses(new Rect(band.xMax - figure, band.y, figure, height),
                    PowerFacts.Power(row.watts), PowerFaces.Mono, PowerFaces.Size.Small);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }

            Rect track = new Rect(band.xMax - figure - share - 8f, band.y + (height - 6f) * 0.5f, share, 6f);

            UIElementPainter.OutlineRounded(track, palette.Border, palette.SurfaceSunken);

            float fill = Mathf.Clamp01(Mathf.Abs(row.watts) / biggest);

            if (fill > 0f)
            {
                Widgets.DrawBoxSolid(new Rect(track.x + 1f, track.y + 1f,
                    Mathf.Max(1f, (track.width - 2f) * fill), track.height - 2f),
                    maker ? palette.Success : palette.Danger);
            }

            string name = row.count > 1 ? row.name + "  x" + row.count : row.name;

            TabParts.RowLabel(new Rect(band.x, band.y, track.x - band.x - 8f, height), name,
                palette.TextPrimary, GameFont.Small, PowerFaces.Condensed, PowerFaces.Size.Name);

            return band.yMax;
        }

        private static readonly List<FuelRow> Burning = new List<FuelRow>();

        /// <summary>
        /// What the grid is burning, by fuel, with every burner of it underneath.
        ///
        /// <b>Headed by the fuel rather than by the generator,</b> because a colony stocks chemfuel and wood
        /// rather than "the generator by the east wall", and the question behind opening this is whether one
        /// of those is about to run out. The burners sit under their fuel so the answer and the things giving
        /// it are in one place.
        ///
        /// <b>The countdown is the point again.</b> Two hundred chemfuel means nothing on its own; two hundred
        /// chemfuel at forty five a day is four days, and four days is a decision about whether somebody is
        /// hauling today.
        /// </summary>
        private static float Burners(Rect view, float y, GridRow grid, UIColorPaletteDef palette)
        {
            PowerFacts.Fuels(grid.net, Burning);

            if (Burning.Count == 0)
                return y;

            int rows = 0;

            for (int i = 0; i < Burning.Count; i++)
                rows += 1 + Burning[i].burners.Count;

            float height = 30f + rows * 22f + Burning.Count * 4f + 8f;

            Rect box = new Rect(view.x, y, view.width, height);

            UIElementPainter.OutlineRounded(box, palette.Border, palette.PanelBackground);

            Rect cap = new Rect(box.x, box.y, box.width, 22f);

            UIElementPainter.FillRounded(cap,
                UIElementPainter.Composite(palette.PanelBackground, palette.HoverOverlay));

            TabParts.RowLabel(new Rect(cap.x + 10f, cap.y, cap.width - 20f, cap.height),
                PowerFaces.Caps("Fuel"), palette.TextSecondary, GameFont.Tiny, PowerFaces.Mono,
                PowerFaces.Size.BlockHead);

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextDisabled;

                UITextControl.LabelEllipses(new Rect(cap.x + 10f, cap.y, cap.width - 20f, cap.height),
                    PowerFaces.Caps(Burning.Count + (Burning.Count == 1 ? " kind" : " kinds")),
                    PowerFaces.Mono, PowerFaces.Size.BlockHead);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }

            float cursor = cap.yMax + 4f;

            for (int i = 0; i < Burning.Count; i++)
                cursor = Fuel(box, cursor, Burning[i], palette);

            return box.yMax + RowGap;
        }

        private static float Fuel(Rect box, float y, FuelRow fuel, UIColorPaletteDef palette)
        {
            const float height = 22f;
            const float figure = 96f;

            Rect band = new Rect(box.x + 12f, y, box.width - 24f, height);

            Color tint = fuel.days < 0f
                ? palette.TextDisabled
                : fuel.days < 1f
                    ? palette.Danger
                    : fuel.days < 3f
                        ? palette.Warning
                        : palette.Success;

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = tint;

                UITextControl.LabelEllipses(new Rect(band.xMax - figure, band.y, figure, height),
                    PowerFacts.Days(fuel.days), PowerFaces.Mono, PowerFaces.Size.Small);

                GUI.color = palette.TextSecondary;

                string rate = fuel.perDay > 0f
                    ? Mathf.RoundToInt(fuel.held) + " left, " + fuel.perDay.ToString("0.#") + "/day"
                    : Mathf.RoundToInt(fuel.held) + " left, not burning";

                UITextControl.LabelEllipses(new Rect(band.xMax - figure - 200f, band.y, 196f, height), rate,
                    PowerFaces.Mono, PowerFaces.Size.Small);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }

            TabParts.RowLabel(new Rect(band.x, band.y, band.width - figure - 208f, height), fuel.name,
                palette.TextPrimary, GameFont.Small, PowerFaces.Condensed, PowerFaces.Size.Name);

            float cursor = band.yMax;

            for (int i = 0; i < fuel.burners.Count; i++)
                cursor = Burner(box, cursor, fuel.burners[i], palette);

            return cursor + 4f;
        }

        /// <summary>
        /// One generator under its fuel: how full it is, and how long that lasts.
        ///
        /// <b>Indented and quieter than the fuel above it,</b> because these are the detail behind a figure
        /// that has already been given. Somebody who only wanted to know whether the wood is running out has
        /// their answer on the row above and can stop reading.
        /// </summary>
        private static float Burner(Rect box, float y, BurnerRow burner, UIColorPaletteDef palette)
        {
            const float height = 22f;
            const float figure = 96f;
            const float bar = 70f;

            Rect band = new Rect(box.x + 26f, y, box.width - 38f, height);

            float share = burner.capacity > 0f ? Mathf.Clamp01(burner.held / burner.capacity) : 0f;

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;

                GUI.color = burner.days < 0f
                    ? palette.TextDisabled
                    : burner.days < 1f
                        ? palette.Danger
                        : palette.TextSecondary;

                UITextControl.LabelEllipses(new Rect(band.xMax - figure, band.y, figure, height),
                    PowerFacts.Days(burner.days), PowerFaces.Mono, PowerFaces.Size.Small);

                GUI.color = palette.TextDisabled;

                UITextControl.LabelEllipses(new Rect(band.xMax - figure - 90f, band.y, 86f, height),
                    Mathf.RoundToInt(burner.held) + " / " + Mathf.RoundToInt(burner.capacity),
                    PowerFaces.Mono, PowerFaces.Size.Small);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }

            Rect track = new Rect(band.xMax - figure - 90f - bar - 8f, band.y + (height - 6f) * 0.5f, bar, 6f);

            UIElementPainter.OutlineRounded(track, palette.Border, palette.SurfaceSunken);

            if (share > 0f)
            {
                Widgets.DrawBoxSolid(new Rect(track.x + 1f, track.y + 1f,
                        Mathf.Max(1f, (track.width - 2f) * share), track.height - 2f),
                    InspectPaneParts.Level(share, palette));
            }

            TabParts.RowLabel(new Rect(band.x, band.y, track.x - band.x - 8f, height), burner.name,
                palette.TextSecondary, GameFont.Small, PowerFaces.Body, PowerFaces.Size.Body);

            return band.yMax;
        }

        private static readonly List<BatteryRow> Cells = new List<BatteryRow>();

        /// <summary>
        /// The batteries and the trouble, side by side.
        ///
        /// <b>Trouble was full width and did not need to be.</b> Its rows are a name and a chip, so most of
        /// that width was empty and a colony with ten unpowered things got a very wide column of the same
        /// short line. Halving it leaves room for the batteries, which are the other half of the countdown at
        /// the top: the hours come from what these hold.
        /// </summary>
        private static float Cellar(Rect view, float y, GridRow grid, UIColorPaletteDef palette)
        {
            PowerFacts.Batteries(grid.net, Cells);
            PowerFacts.Faults(grid, Faults);

            if (Cells.Count == 0 && Faults.Count == 0)
                return y;

            float half = (view.width - RowGap) * 0.5f;

            float left = Cells.Count > 0
                ? Storage(new Rect(view.x, y, half, 0f), y, grid, palette)
                : y;

            float right = Faults.Count > 0
                ? Trouble(new Rect(view.x + half + RowGap, y, half, 0f), y, palette)
                : y;

            return Mathf.Max(left, right);
        }

        /// <summary>
        /// Every battery on the grid, emptiest first.
        ///
        /// <b>Emptiest first because that is the one that stops carrying.</b> A bank drains together but does
        /// not always fill together: a battery that was built later, or was disconnected for a while, sits
        /// lower than the rest and is the first to leave the countdown short.
        /// </summary>
        private static float Storage(Rect view, float y, GridRow grid, UIColorPaletteDef palette)
        {
            float height = 30f + Cells.Count * 22f + 26f;

            Rect box = new Rect(view.x, y, view.width, height);

            UIElementPainter.OutlineRounded(box, palette.Border, palette.PanelBackground);

            Rect cap = new Rect(box.x, box.y, box.width, 22f);

            UIElementPainter.FillRounded(cap,
                UIElementPainter.Composite(palette.PanelBackground, palette.HoverOverlay));

            TabParts.RowLabel(new Rect(cap.x + 10f, cap.y, cap.width - 20f, cap.height),
                PowerFaces.Caps("Batteries"), palette.TextSecondary, GameFont.Tiny, PowerFaces.Mono,
                PowerFaces.Size.BlockHead);

            float share = grid.capacity > 0f ? Mathf.Clamp01(grid.stored / grid.capacity) : 0f;

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextDisabled;

                UITextControl.LabelEllipses(new Rect(cap.x + 10f, cap.y, cap.width - 20f, cap.height),
                    PowerFaces.Caps(Mathf.RoundToInt(share * 100f) + "% of "
                                    + Mathf.RoundToInt(grid.capacity).ToString("N0") + " Wd"),
                    PowerFaces.Mono, PowerFaces.Size.BlockHead);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }

            float cursor = cap.yMax + 4f;

            for (int i = 0; i < Cells.Count; i++)
                cursor = Cell(box, cursor, Cells[i], palette);

            // The bank total sits under the individual ones rather than only in the header, because it is the
            // figure the countdown at the top of the screen is divided from.
            Rect footer = new Rect(box.x + 12f, cursor + 2f, box.width - 24f, 20f);

            TabParts.RowLabel(footer,
                Mathf.RoundToInt(grid.stored).ToString("N0") + " Wd across "
                + Cells.Count + (Cells.Count == 1 ? " battery" : " batteries"),
                palette.TextDisabled, GameFont.Small, PowerFaces.Body, PowerFaces.Size.Body);

            return box.yMax + RowGap;
        }

        private static float Cell(Rect box, float y, BatteryRow cell, UIColorPaletteDef palette)
        {
            const float height = 22f;
            const float figure = 74f;
            const float bar = 70f;

            Rect band = new Rect(box.x + 12f, y, box.width - 24f, height);

            float share = cell.capacity > 0f ? Mathf.Clamp01(cell.stored / cell.capacity) : 0f;

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = InspectPaneParts.Level(share, palette);

                UITextControl.LabelEllipses(new Rect(band.xMax - figure, band.y, figure, height),
                    Mathf.RoundToInt(cell.stored).ToString("N0") + " Wd", PowerFaces.Mono,
                    PowerFaces.Size.Small);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }

            Rect track = new Rect(band.xMax - figure - bar - 8f, band.y + (height - 6f) * 0.5f, bar, 6f);

            UIElementPainter.OutlineRounded(track, palette.Border, palette.SurfaceSunken);

            if (share > 0f)
            {
                Widgets.DrawBoxSolid(new Rect(track.x + 1f, track.y + 1f,
                        Mathf.Max(1f, (track.width - 2f) * share), track.height - 2f),
                    InspectPaneParts.Level(share, palette));
            }

            TabParts.RowLabel(new Rect(band.x, band.y, track.x - band.x - 8f, height), cell.name,
                cell.on ? palette.TextPrimary : palette.TextDisabled, GameFont.Small, PowerFaces.Body,
                PowerFaces.Size.Body);

            return band.yMax;
        }

        // -------------------------------------------------------------------------------------------
        // Trouble
        // -------------------------------------------------------------------------------------------

        private static float Trouble(Rect view, float y, UIColorPaletteDef palette)
        {
            float height = 30f + Faults.Count * 22f + 8f;

            Rect box = new Rect(view.x, y, view.width, height);

            UIElementPainter.OutlineRounded(box, palette.Border, palette.PanelBackground);

            Rect cap = new Rect(box.x, box.y, box.width, 22f);

            UIElementPainter.FillRounded(cap,
                UIElementPainter.Composite(palette.PanelBackground, palette.HoverOverlay));

            TabParts.RowLabel(new Rect(cap.x + 10f, cap.y, cap.width - 20f, cap.height),
                PowerFaces.Caps("Trouble"), palette.TextSecondary, GameFont.Tiny, PowerFaces.Mono,
                PowerFaces.Size.BlockHead);

            float cursor = cap.yMax + 4f;

            for (int i = 0; i < Faults.Count; i++)
            {
                FaultRow fault = Faults[i];

                Rect band = new Rect(box.x + 12f, cursor, box.width - 24f, 22f);

                Color tint = fault.severe ? palette.Danger : palette.Warning;

                float chip = TabParts.PillWidth(PowerFaces.Caps(fault.state), 9999f, PowerFaces.Mono,
                    PowerFaces.Size.Chip);

                TabParts.Pill(band, band.xMax - chip, band.y + 2f, PowerFaces.Caps(fault.state), tint, palette,
                    chip, null, PowerFaces.Mono, PowerFaces.Size.Chip);

                string name = fault.detail.NullOrEmpty() || fault.detail == "1"
                    ? fault.name
                    : fault.name + "  x" + fault.detail;

                TabParts.RowLabel(new Rect(band.x, band.y, band.width - chip - 8f, 22f), name,
                    palette.TextPrimary, GameFont.Small, PowerFaces.Condensed, PowerFaces.Size.Name);

                cursor = band.yMax;
            }

            return box.yMax + RowGap;
        }
    }
}
