using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Ideoligions
{
    /// <summary>
    /// The ideoligions tab: the screen you live with, as opposed to the one you build in.
    ///
    /// <b>Ordered by rate of change, which is the whole argument.</b> Conviction moves every hour, roles and
    /// obligations every day, demands every week, and doctrine only at a reform. Vanilla spends the screen on the
    /// precept list -- the one part that does not change between reforms -- and puts everything that does change
    /// into alerts, tooltips and other windows. So the blocks here run conviction, roles, obligations, demands,
    /// doctrine, and the doctrine gets one line per issue rather than a card each.
    ///
    /// <b>None of this is new data.</b> <c>Pawn_IdeoTracker.Certainty</c> and <c>CertaintyChangePerDay</c> are per
    /// pawn and already computed; <c>IdeoDevelopmentTracker</c> holds the points and whether a reform is
    /// affordable; <c>Precept_Role</c> knows its apparel requirements and its holder;
    /// <c>IdeoBuildingPresenceDemand</c> knows which altar is missing. This is a re-layout, not an invention, and
    /// the read side of it lives in <see cref="IdeoFacts"/>.
    ///
    /// <b>The rail lists the colony's faiths, not the game's.</b> A colony with three ideoligions is the case
    /// vanilla handles worst, and every block behind the rail is scoped to the one selected. Faiths known but not
    /// present sit below, dimmed. In classic mode the rail is not drawn at all, because one ideoligion covers
    /// everybody and a list of one is a column of wasted pixels.
    /// </summary>
    internal static class IdeoPanel
    {
        /// <summary>
        /// Wide enough that the rail does not come out of the blocks' pocket.
        ///
        /// The rail grew by 95 to stop faith names truncating, and that width came straight off the column
        /// beside it -- roles and obligations are drawn side by side there, so each lost half of it just as
        /// their own name columns were widened. This gives it back rather than having the two changes cancel.
        /// Clamped to the screen where it is used, so a display too small for it is not left with a window it
        /// cannot drag back.
        /// </summary>
        internal const float WindowWidth = 1215f;

        internal const float WindowHeight = 760f;

        private const float Pad = 12f;
        private const float RailWidth = 285f;
        private const float HeaderHeight = 74f;
        private const float BlockGap = 10f;
        private const float RowGap = 2f;

        /// <summary>
        /// Air between the rail's divider and the heading under it.
        ///
        /// Matched to the distance the first heading sits from the panel border, so the two headings are set
        /// off from the line above them by the same amount and read as a pair. At the shared default of four
        /// the second one hung off its rule instead.
        /// </summary>
        private const float RuleGap = 12f;

        /// <summary>
        /// Whether the conviction block is sorted by name rather than by devotion.
        ///
        /// Kept here, not saved, because it is a way of looking at the list rather than a preference about it:
        /// the reason to sort by name is to find one colonist, and once they are found the reason is gone. A
        /// setting that survived a restart would leave the block permanently answering the question nobody was
        /// asking any more, which is the one the default order answers well.
        /// </summary>
        private static bool Alphabetical;

        private static Vector2 scroll;
        private static float viewHeight = 1f;

        internal static void Draw(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            Ideo ideo = IdeoFacts.Selected();

            Rect body = inRect.ContractedBy(Pad);

            if (ideo == null)
            {
                TabParts.Line(body, body.y + 40f, "No ideoligion to show.", palette.TextSecondary);

                return;
            }

            List<Ideo> here = IdeoFacts.Faiths(true);
            List<Ideo> elsewhere = IdeoFacts.Faiths(false);

            Header(new Rect(body.x, body.y, body.width, HeaderHeight), ideo, here.Count, palette);

            float top = body.y + HeaderHeight + Pad;
            Rect below = new Rect(body.x, top, body.width, body.yMax - top);

            // Classic mode gives everybody one faith, so there is nothing for the rail to choose between; and a
            // world with exactly one ideoligion in it does not need a list of one either.
            bool rail = !ideo.classicMode && here.Count + elsewhere.Count > 1;

            Rect main = below;

            if (rail)
            {
                Rail(new Rect(below.x, below.y, RailWidth, below.height), here, elsewhere, ideo, palette);

                main = new Rect(below.x + RailWidth + Pad, below.y,
                    below.width - RailWidth - Pad, below.height);
            }

            Blocks(main, ideo, palette);
        }

        // -------------------------------------------------------------------------------------------
        // Header
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Crest, name, and the three numbers worth carrying at the top: believers, other faiths, and how close
        /// the next reform is.
        ///
        /// <b>Development points are the headline number of the three,</b> because they are the only one that is
        /// a countdown to something the player does. Vanilla puts them on the reform button's tooltip.
        /// </summary>
        private static void Header(Rect rect, Ideo ideo, int faithsHere, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceRaised);

            Rect inner = rect.ContractedBy(10f);

            Rect crest = new Rect(inner.x, inner.y + (inner.height - 46f) * 0.5f, 46f, 46f);

            UIGuard.Try("Ideoligions.Crest", () =>
            {
                Color previous = GUI.color;

                GUI.color = ideo.Color;
                GUI.DrawTexture(crest, ideo.Icon);
                GUI.color = previous;
            }, null);

            float x = crest.xMax + 10f;

            // The reform button and the readouts are laid out from the right, so a long ideoligion name is what
            // gives way rather than the numbers.
            float right = inner.xMax;

            if (ideo.Fluid)
            {
                Rect button = new Rect(right - 130f, inner.y + (inner.height - 30f) * 0.5f, 130f, 30f);

                right = button.x - 12f;

                bool can = ideo.development != null && ideo.development.CanReformNow;

                if (TabParts.Button(button, "Reform" + "...", palette, can, true,
                        can ? "Open the designer and spend this faith's development points on a reform."
                            : "Not enough development points yet. They come from rituals and from converting "
                              + "somebody to this ideoligion."))
                    Designer.OpenReform(ideo);
            }

            right = Readout(inner, right, Points(ideo), "to reform", palette);
            right = Readout(inner, right, faithsHere.ToString(), "other faiths", palette);
            Readout(inner, right, ideo.ColonistBelieverCountCached.ToString(), "believers", palette);

            Rect name = new Rect(x, inner.y + 2f, Mathf.Max(60f, right - x - 12f), 28f);

            TabParts.RowLabel(name, ideo.name, ideo.TextColor, GameFont.Medium, IdeoFaces.Display, IdeoFaces.Size.Title);

            TabParts.RowLabel(new Rect(x, name.yMax, name.width, 20f), Subtitle(ideo), palette.TextSecondary,
                GameFont.Tiny, IdeoFaces.Condensed, IdeoFaces.Size.Subtitle);
        }

        /// <summary>Points against the price of the next reform, or a dash for a faith that cannot reform.</summary>
        private static string Points(Ideo ideo)
        {
            if (!ideo.Fluid || ideo.development == null)
                return "--";

            return ideo.development.Points + " / " + ideo.development.NextReformationDevelopmentPoints;
        }

        /// <summary>What kind of faith this is, in the game's own words for each part.</summary>
        private static string Subtitle(Ideo ideo)
        {
            string text = ideo.adjective.NullOrEmpty() ? ideo.name : ideo.adjective;

            if (!ideo.memberName.NullOrEmpty())
                text += "  " + "-" + "  " + ideo.memberName;

            text += "  " + "-" + "  " + (ideo.Fluid ? "fluid" : "fixed");

            MemeDef structure = ideo.StructureMeme;

            if (structure != null)
                text += "  " + "-" + "  " + structure.LabelCap;

            return text;
        }

        /// <summary>
        /// One readout: the figure, with its caption in small caps under it. Returns the x the next one ends at.
        ///
        /// <b>The figure sits above the caption, which is the way round the mockup has it and the way round we
        /// did not.</b> A header readout is read figure-first -- the caption only says which figure it is -- so
        /// putting the caption on top makes the eye cross a dim label to reach the number every time. Reported
        /// against the real screen on 2026-08-30.
        ///
        /// <b>Right-aligned, both lines.</b> These are laid out from the right edge inward, so a right edge is
        /// the one thing every readout shares; centring them instead leaves the figures on a ragged line.
        /// </summary>
        private static float Readout(Rect inner, float right, string value, string caption,
            UIColorPaletteDef palette)
        {
            string label = IdeoFaces.Caps(caption);

            float width = Mathf.Max(
                UITextControl.Width(value, IdeoFaces.Mono, IdeoFaces.Size.Readout),
                UITextControl.Width(label, IdeoFaces.Mono, IdeoFaces.Size.Caption)) + 4f;

            float figure = UITextControl.LineHeight(IdeoFaces.Mono, IdeoFaces.Size.Readout);
            float under = UITextControl.LineHeight(IdeoFaces.Mono, IdeoFaces.Size.Caption);

            float top = inner.y + (inner.height - figure - under - 2f) * 0.5f;

            Rect band = new Rect(right - width, top, width, figure + under + 2f);

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;

                GUI.color = palette.TextPrimary;
                UITextControl.LabelEllipses(new Rect(band.x, band.y, band.width, figure), value,
                    IdeoFaces.Mono, IdeoFaces.Size.Readout);

                GUI.color = palette.TextSecondary;
                UITextControl.LabelEllipses(new Rect(band.x, band.y + figure + 2f, band.width, under), label,
                    IdeoFaces.Mono, IdeoFaces.Size.Caption);
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

        private static void Rail(Rect rect, List<Ideo> here, List<Ideo> elsewhere, Ideo selected,
            UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect view = rect.ContractedBy(6f);
            float y = view.y + 2f;

            // The two leading spaces are deliberate. The rail's rule runs the full width of the panel and the
            // heading under it is drawn from the same x, so without them the first letter sits on the border.
            // Spaces rather than an inset rect, because insetting the rect would carry the rule in with it.
            y = TabParts.Heading(view, y, IdeoFaces.Caps("  In this colony"), palette, false, IdeoFaces.Mono,
                IdeoFaces.Size.RailHead);

            for (int i = 0; i < here.Count; i++)
                y = Entry(view, y, here[i], selected, palette, true);

            if (elsewhere.Count == 0)
                return;

            y += 8f;
            y = TabParts.Heading(view, y, IdeoFaces.Caps("  Known elsewhere"), palette, true, IdeoFaces.Mono,
                IdeoFaces.Size.RailHead, RuleGap);

            for (int i = 0; i < elsewhere.Count; i++)
                y = Entry(view, y, elsewhere[i], selected, palette, false);
        }

        /// <summary>
        /// One faith in the rail: its own colour as a swatch, its name, and its believer count.
        ///
        /// <b>Selecting writes vanilla's own selection,</b> so this screen, a pawn's bio panel and anything else
        /// that reads <c>IdeoUIUtility.selected</c> stay in step.
        /// </summary>
        private static float Entry(Rect view, float y, Ideo ideo, Ideo selected, UIColorPaletteDef palette,
            bool present)
        {
            const float height = 26f;

            Rect row = new Rect(view.x, y, view.width, height);
            bool on = ideo == selected;

            if (on)
                UIElementPainter.FillRounded(row, palette.SelectionOverlay);
            else if (Mouse.IsOver(row))
                UIElementPainter.FillRounded(row, palette.HoverOverlay);

            Rect swatch = new Rect(row.x + 5f, row.y + (height - 9f) * 0.5f, 9f, 9f);

            Color previous = GUI.color;
            GUI.color = ideo.Color;
            GUI.DrawTexture(swatch, BaseContent.WhiteTex);
            GUI.color = previous;

            string count = present ? ideo.ColonistBelieverCountCached.ToString() : "-";
            float countWidth = 24f;

            TabParts.RowLabel(new Rect(row.xMax - countWidth - 4f, row.y, countWidth, height), count,
                palette.TextDisabled, GameFont.Tiny, IdeoFaces.Mono, IdeoFaces.Size.RailCount);

            TabParts.RowLabel(new Rect(swatch.xMax + 6f, row.y, row.width - 20f - countWidth - 12f, height),
                ideo.name, on ? ideo.TextColor : present ? palette.TextPrimary : palette.TextDisabled,
                GameFont.Small, IdeoFaces.Condensed, IdeoFaces.Size.RailName);

            if (Widgets.ButtonInvisible(row))
            {
                IdeoUIUtility.SetSelected(ideo);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            return y + height + RowGap;
        }

        // -------------------------------------------------------------------------------------------
        // The blocks
        // -------------------------------------------------------------------------------------------

        private static void Blocks(Rect rect, Ideo ideo, UIColorPaletteDef palette)
        {
            Map map = Find.CurrentMap;

            List<ConvictionRow> conviction = IdeoFacts.Conviction(ideo, Alphabetical);
            List<RoleRow> roles = IdeoFacts.Roles(ideo);
            List<ObligationRow> obligations = IdeoFacts.Obligations(ideo);
            List<DemandRow> demands = IdeoFacts.Demands(ideo, map);
            List<DoctrineRow> doctrine = IdeoFacts.Doctrine(ideo);

            Rect view = new Rect(0f, 0f, rect.width - 18f, viewHeight);

            Widgets.BeginScrollView(rect, ref scroll, view);

            float y = 0f;

            y = Conviction(view, y, conviction, palette);

            // Roles and obligations share a line: both are short, both are daily, and side by side they read as
            // one answer to "who is doing what, and what is owed".
            float half = (view.width - BlockGap) * 0.5f;

            Rect column = new Rect(view.x, y, half, 0f);
            Rect beside = new Rect(view.x + half + BlockGap, y, half, 0f);

            // Demands is stacked under roles rather than run across the page, because it is the shortest block
            // on the screen: a colony usually owes its faith one altar, so at full width it was a header with a
            // single name under it and the other two thirds of the row empty. Under roles it fills the column
            // that obligations, which has more rows, would otherwise leave short.
            float left = Demands(column, Roles(column, y, roles, palette), demands, map, palette);
            float right = Obligations(beside, y, obligations, ideo, palette);

            y = Mathf.Max(left, right);
            y = Doctrine(view, y, doctrine, palette);

            if (Event.current.type == EventType.Layout)
                viewHeight = Mathf.Max(1f, y);

            Widgets.EndScrollView();
        }

        /// <summary>
        /// How tall each block was the last time it drew, keyed by its title.
        ///
        /// <b>Measured from the last draw rather than predicted, and rather than drawn twice.</b> A formula for
        /// how tall a block will be is wrong the first time a row is added to it and fails silently, which is a
        /// fault this codebase has already paid for; running the body once to measure and again to draw is worse
        /// still, because every button inside it would be hit-tested twice and every click would land twice.
        /// Painting the frame at last frame's height costs one frame of lag on the rare frame a block changes
        /// size, and nothing at all on every other frame.
        /// </summary>
        private static readonly Dictionary<string, float> measured = new Dictionary<string, float>();

        /// <summary>
        /// How wide a column of names has to be, measured from the names themselves.
        ///
        /// <b>Every one of these columns started as a round number and every one of them was too narrow.</b>
        /// A ritual called Anima tree linking or Gravship launch does not fit in the hundred-odd pixels that
        /// looked generous while the block was being written, and the reader gets "Anima tree li..." beside a
        /// column of empty space -- the value it was truncated to make room for is usually the words "never
        /// held". Reported against the obligations block on 2026-08-30.
        ///
        /// <b>Measured, then clamped, rather than simply widened.</b> Widening the constant moves the problem to
        /// the next long name; measuring answers it for the names actually in this colony. The ceiling is what
        /// stops one absurd name from eating the column beside it, and it is a fraction of the block rather than
        /// a number, because these blocks are drawn at half width beside each other and at full width alone.
        /// </summary>
        /// <param name="face">
        /// The face the column will be drawn in. Measuring in one face and drawing in another is the same
        /// truncation bug in a new coat: Barlow Condensed fits noticeably more in a given width than the game
        /// font does, so a column sized against the wrong one is either short or padded with air.
        /// </param>
        private static float Column(List<string> labels, float points, float minimum, float available,
            float share = 0.55f, UIFace face = UIFace.Game)
        {
            GameFont previous = Text.Font;

            try
            {
                Text.Font = UIFonts.Nearest(points);

                float widest = 0f;

                for (int i = 0; labels != null && i < labels.Count; i++)
                {
                    if (labels[i].NullOrEmpty())
                        continue;

                    float w = face == UIFace.Game
                        ? Text.CalcSize(labels[i]).x
                        : UITextControl.Width(labels[i], face, points);

                    widest = Mathf.Max(widest, w);
                }

                return Mathf.Clamp(widest + 8f, minimum, Mathf.Max(minimum, available * share));
            }
            finally
            {
                Text.Font = previous;
            }
        }

        /// <summary>
        /// A titled box drawn around one body. Returns the y under it.
        /// </summary>
        /// <param name="hot">
        /// The tail of <paramref name="suffix"/> that is a control rather than a readout, or null when the
        /// whole suffix is just text. Only this part lights on hover and only this part takes the click, so a
        /// block can carry a count and a sort order in one line without the count looking pressable.
        /// </param>
        private static float Block(Rect view, float y, string title, string suffix, UIColorPaletteDef palette,
            System.Func<Rect, float, float> body, string hot = null, System.Action onHot = null)
        {
            const float capHeight = 24f;

            float height;

            // First sight of this block: one row's worth, so the frame is never zero-height and the body is
            // never clipped to nothing before it has had a chance to say how tall it is.
            if (!measured.TryGetValue(title, out height))
                height = capHeight + 34f;

            Rect box = new Rect(view.x, y, view.width, height);

            UIElementPainter.OutlineRounded(box, palette.Border, palette.PanelBackground);

            Rect cap = new Rect(box.x, box.y, box.width, capHeight);

            // Lifted off the panel rather than set in surfaceRaised, and the palette's own note says why:
            // "surfaceRaised is darker than the panel in this theme, which is right for a card and wrong for a
            // control". A block cap is neither -- it is a band along the top of a panel, and the mockup has it
            // lighter than what it sits on. Drawn in surfaceRaised it came out darker instead, so every block
            // read as a recessed strip rather than a heading.
            //
            // Composited off the panel so the lift follows the palette: white over a dark theme, black over a
            // light one, and never the wrong side of whatever the panel happens to be.
            UIElementPainter.FillRounded(cap,
                UIElementPainter.Composite(palette.PanelBackground, palette.HoverOverlay));

            // The rule under it, which the mockup has and this did not. Without it the band has no bottom edge
            // and the header floats on the same field as the first row.
            Color previousLine = GUI.color;

            GUI.color = palette.Border;
            Widgets.DrawLineHorizontal(cap.x, cap.yMax, cap.width);
            GUI.color = previousLine;

            // Tiny and upper case, which is what the mockup's block headers are: the smallest thing on the
            // screen rather than the largest. At Small it came out bigger than the rows underneath it, so a
            // block announced itself more loudly than anything it contained -- and five of those stacked down
            // the tab read as five headlines with data between them.
            TabParts.RowLabel(new Rect(cap.x + 10f, cap.y, cap.width - 20f, capHeight),
                title.ToUpperInvariant(), palette.TextSecondary, GameFont.Tiny, IdeoFaces.Mono,
                IdeoFaces.Size.BlockHead);

            if (!suffix.NullOrEmpty())
            {
                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;

                try
                {
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleRight;
                    GUI.color = palette.TextSecondary;

                    // Mono and upper case, matching the header on the other side of the cap. This one was still
                    // in the game's own font: it was the only label on the block that never got a face, so a
                    // row of blocks had mono headers on the left and RimWorld's face on the right.
                    Rect band = new Rect(cap.x + 10f, cap.y, cap.width - 20f, capHeight);

                    // The hot tail is measured and drawn on its own so it can take a hover colour the rest of
                    // the suffix does not. Drawn second, over the right end of the line the full suffix just
                    // laid down, which keeps one right edge for both and needs no layout arithmetic.
                    UITextControl.LabelEllipses(band, IdeoFaces.Caps(suffix), IdeoFaces.Mono,
                        IdeoFaces.Size.BlockHead);

                    if (!hot.NullOrEmpty() && onHot != null)
                    {
                        float width = UITextControl.Width(IdeoFaces.Caps(hot), IdeoFaces.Mono,
                            IdeoFaces.Size.BlockHead);

                        Rect control = new Rect(band.xMax - width, band.y, width, band.height);

                        bool over = Mouse.IsOver(control);

                        GUI.color = over ? palette.Accent : palette.TextSecondary;

                        UITextControl.LabelEllipses(control, IdeoFaces.Caps(hot), IdeoFaces.Mono,
                            IdeoFaces.Size.BlockHead);

                        if (Widgets.ButtonInvisible(control))
                        {
                            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();

                            onHot();
                        }
                    }
                }
                finally
                {
                    GUI.color = previousColor;
                    Text.Anchor = previousAnchor;
                    Text.Font = previousFont;
                }
            }

            // Drawn after the frame so the rows sit on top of the background rather than under it, and measured
            // as it goes so the next frame's box is the right size.
            Rect inner = new Rect(box.x + 10f, box.y + capHeight + 6f, box.width - 20f, 0f);

            measured[title] = body(inner, inner.y) + 8f - box.y;

            return box.yMax + BlockGap;
        }

        // -------------------------------------------------------------------------------------------

        private static float Conviction(Rect view, float y, List<ConvictionRow> rows, UIColorPaletteDef palette)
        {
            int drifting = IdeoFacts.Drifting(rows);

            string order = Alphabetical ? "Alpha - Descending" : "Devotion - Ascending";

            string suffix = rows.Count == 0
                ? "nobody here holds it"
                : (drifting > 0 ? drifting + " drifting" + "  -  " : "") + order;

            return Block(view, y, "Conviction", suffix, palette, (inner, top) =>
            {
                if (rows.Count == 0)
                    return TabParts.Line(inner, top, "No colonist follows this ideoligion.", palette.TextDisabled);

                float cursor = top;

                List<string> names = new List<string>();

                for (int i = 0; i < rows.Count; i++)
                    names.Add(rows[i].pawn.LabelShortCap);

                // Twice what it was, on both the floor and the ceiling. The bar is the least important thing on
                // this row: it says the same as the percentage beside it, and a name cut to "Undead Nekt..."
                // fails at the one thing the row exists for, which is telling you who is slipping. The floor is
                // raised as well as the cap so the bars start at the same x whatever the roster is called --
                // a column that moves as colonists come and go is harder to read down than a slightly wide one.
                float column = Column(names, IdeoFaces.Size.Name, 180f, inner.width, 0.5f, IdeoFaces.Condensed);

                for (int i = 0; i < rows.Count; i++)
                    cursor = ConvictionRowDraw(inner, cursor, rows[i], column, palette);

                return cursor;
            }, rows.Count == 0 ? null : order, () => Alphabetical = !Alphabetical);
        }

        private static float ConvictionRowDraw(Rect inner, float y, ConvictionRow row, float column,
            UIColorPaletteDef palette)
        {
            const float height = 22f;

            Rect band = new Rect(inner.x, y, inner.width, height);

            TabParts.RowLabel(new Rect(band.x, band.y, column, height), row.pawn.LabelShortCap,
                palette.TextPrimary, GameFont.Small, IdeoFaces.Condensed, IdeoFaces.Size.Name);

            Color tint = row.certainty < IdeoFacts.ConvertingBelow
                ? palette.Danger
                : row.certainty < IdeoFacts.DoubtingBelow
                    ? palette.Warning
                    : row.certainty >= IdeoFacts.DevoutFrom
                        ? palette.Success
                        : palette.Accent;

            float chipWidth = 88f;
            float driftWidth = 62f;
            float numberWidth = 42f;

            Rect bar = new Rect(band.x + column + 6f, band.y + 7f,
                Mathf.Max(40f, band.width - column - 6f - numberWidth - driftWidth - chipWidth - 12f), 8f);

            UIProgressBarControl.Draw(bar, row.certainty, palette, tint);

            TabParts.RowLabel(new Rect(bar.xMax + 4f, band.y, numberWidth, height),
                row.certainty.ToStringPercent("0"), palette.TextSecondary, GameFont.Tiny, IdeoFaces.Mono,
                IdeoFaces.Size.Figure);

            string drift = row.drift > 0.0005f
                ? "+" + row.drift.ToStringPercent("0") + "/d"
                : row.drift < -0.0005f
                    ? row.drift.ToStringPercent("0") + "/d"
                    : "steady";

            Color driftColor = row.drift > 0.0005f
                ? palette.Success
                : row.drift < -0.0005f
                    ? palette.Danger
                    : palette.TextDisabled;

            TabParts.RowLabel(new Rect(bar.xMax + 4f + numberWidth, band.y, driftWidth, height), drift,
                driftColor, GameFont.Tiny, IdeoFaces.Mono, IdeoFaces.Size.Small);

            string word = row.certainty < IdeoFacts.ConvertingBelow
                ? "slipping"
                : row.certainty < IdeoFacts.DoubtingBelow
                    ? "doubting"
                    : row.certainty >= IdeoFacts.DevoutFrom
                        ? "devout"
                        : "settled";

            TabParts.Pill(band, band.xMax - chipWidth, band.y + 2f, IdeoFaces.Caps(word), tint, palette, 9999f, null, IdeoFaces.Mono, IdeoFaces.Size.Chip);

            TooltipHandler.TipRegion(band, (TipSignal) (row.pawn.LabelShortCap + " is " + word
                + " at " + row.certainty.ToStringPercent("0") + " certainty.\n\nCertainty falls when they see "
                + "things their faith forbids and rises when the faith is reinforced. A colonist at zero "
                + "certainty leaves the faith."));

            return y + height + RowGap;
        }

        // -------------------------------------------------------------------------------------------

        private static float Roles(Rect view, float y, List<RoleRow> rows, UIColorPaletteDef palette)
        {
            int unfilled = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].holder == null)
                    unfilled++;
            }

            string suffix = rows.Count == 0 ? null : unfilled > 0 ? unfilled + " unfilled" : "all filled";

            return Block(view, y, "Roles", suffix, palette, (inner, top) =>
            {
                if (rows.Count == 0)
                    return TabParts.Line(inner, top, "This faith has no roles.", palette.TextDisabled);

                float cursor = top;

                List<string> names = new List<string>();

                for (int i = 0; i < rows.Count; i++)
                    names.Add(rows[i].role.LabelCap);

                // Narrower share than the obligations block: this row carries a holder's name and a chip after
                // the role, so the role cannot have most of the width even when its name is long.
                float column = Column(names, IdeoFaces.Size.Name, 80f, inner.width, 0.4f, IdeoFaces.Condensed);

                for (int i = 0; i < rows.Count; i++)
                    cursor = RoleRowDraw(inner, cursor, rows[i], column, palette);

                return cursor;
            });
        }

        private static float RoleRowDraw(Rect inner, float y, RoleRow row, float column,
            UIColorPaletteDef palette)
        {
            const float height = 22f;

            Rect band = new Rect(inner.x, y, inner.width, height);

            TabParts.RowLabel(new Rect(band.x, band.y, column, height), row.role.LabelCap, palette.Accent,
                GameFont.Small, IdeoFaces.Condensed, IdeoFaces.Size.Name);

            string chip;
            Color tint;

            if (row.holder == null)
            {
                chip = row.eligible > 0 ? row.eligible + " eligible" : "none eligible";
                tint = row.eligible > 0 ? palette.Warning : palette.TextDisabled;
            }
            else if (row.fault != null)
            {
                chip = row.fault;
                tint = palette.Danger;
            }
            else
            {
                chip = "qualified";
                tint = palette.Success;
            }

            float chipWidth = Mathf.Min(TabParts.PillWidth(IdeoFaces.Caps(chip), 9999f, IdeoFaces.Mono, IdeoFaces.Size.Chip), band.width * 0.5f);

            TabParts.Pill(band, band.xMax - chipWidth, band.y + 2f, IdeoFaces.Caps(chip), tint, palette, chipWidth, null, IdeoFaces.Mono, IdeoFaces.Size.Chip);

            TabParts.RowLabel(new Rect(band.x + column + 4f, band.y,
                    Mathf.Max(20f, band.width - column - 4f - chipWidth - 6f), height),
                row.holder != null ? row.holder.LabelShortCap : "nobody assigned",
                row.holder != null ? palette.TextPrimary : palette.TextDisabled,
                GameFont.Small, IdeoFaces.Body, IdeoFaces.Size.Body);

            return y + height + RowGap;
        }

        // -------------------------------------------------------------------------------------------

        private static float Obligations(Rect view, float y, List<ObligationRow> rows, Ideo ideo,
            UIColorPaletteDef palette)
        {
            int owed = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].owed)
                    owed++;
            }

            string suffix = rows.Count == 0 ? null : owed > 0 ? owed + " owed" : "nothing owed";

            return Block(view, y, "Obligations", suffix, palette, (inner, top) =>
            {
                if (rows.Count == 0)
                    return TabParts.Line(inner, top, "This faith has no rituals.", palette.TextDisabled);

                float cursor = top;

                // Below three believers RimWorld stops raising obligations at all, which otherwise reads as a
                // faith that has stopped asking for anything.
                if (!ideo.ObligationsActive)
                {
                    cursor = TabParts.Line(inner, cursor,
                        "Too few believers for obligations (" + Ideo.MinBelieversToEnableObligations
                        + " needed).", palette.Warning, GameFont.Tiny);
                }

                List<string> names = new List<string>();

                for (int i = 0; i < rows.Count; i++)
                    names.Add(rows[i].ritual.LabelCap);

                // Wider than the others, because a ritual's name is the longest thing in any of these blocks
                // and what it competes with is usually the words "never held".
                float column = Column(names, IdeoFaces.Size.Name, 96f, inner.width, 0.62f, IdeoFaces.Condensed);

                for (int i = 0; i < rows.Count; i++)
                    cursor = ObligationRowDraw(inner, cursor, rows[i], column, palette);

                return cursor;
            });
        }

        private static float ObligationRowDraw(Rect inner, float y, ObligationRow row, float column,
            UIColorPaletteDef palette)
        {
            const float height = 22f;

            Rect band = new Rect(inner.x, y, inner.width, height);

            TabParts.RowLabel(new Rect(band.x, band.y, column, height), row.ritual.LabelCap, palette.TextPrimary,
                GameFont.Small, IdeoFaces.Condensed, IdeoFaces.Size.Name);

            string text = row.when;

            if (row.note != null)
                text += "   " + row.note;

            // Said out loud, because a row standing for three rituals that all answer to "trial" otherwise looks
            // like two of them went missing.
            if (row.variants > 1)
                text += "   " + row.variants + " kinds";

            TabParts.RowLabel(new Rect(band.x + column + 6f, band.y, band.width - column - 6f, height), text,
                row.owed ? palette.Warning : palette.TextSecondary, GameFont.Tiny, IdeoFaces.Mono,
                IdeoFaces.Size.When);

            return y + height + RowGap;
        }

        // -------------------------------------------------------------------------------------------

        private static float Demands(Rect view, float y, List<DemandRow> rows, Map map, UIColorPaletteDef palette)
        {
            int unmet = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].met)
                    unmet++;
            }

            string where = map != null ? map.Parent?.Label : null;
            string suffix = rows.Count == 0
                ? null
                : (unmet > 0 ? unmet + " unmet" : "all met") + (where.NullOrEmpty() ? "" : "  -  " + where);

            return Block(view, y, "What the faith demands", suffix, palette, (inner, top) =>
            {
                if (map == null)
                    return TabParts.Line(inner, top, "No map to judge these against.", palette.TextDisabled);

                if (rows.Count == 0)
                    return TabParts.Line(inner, top, "This faith demands no buildings here.", palette.TextDisabled);

                float cursor = top;

                for (int i = 0; i < rows.Count; i++)
                    cursor = DemandRowDraw(inner, cursor, rows[i], palette);

                return cursor;
            });
        }

        private static float DemandRowDraw(Rect inner, float y, DemandRow row, UIColorPaletteDef palette)
        {
            const float height = 22f;

            Rect band = new Rect(inner.x, y, inner.width, height);

            Color tint = row.met ? palette.Success : row.disrespected ? palette.Warning : palette.Danger;

            float chipWidth = Mathf.Min(TabParts.PillWidth(IdeoFaces.Caps(row.state), 9999f, IdeoFaces.Mono, IdeoFaces.Size.Chip), 160f);

            TabParts.Pill(band, band.xMax - chipWidth, band.y + 2f, IdeoFaces.Caps(row.state), tint, palette, chipWidth, null, IdeoFaces.Mono, IdeoFaces.Size.Chip);

            TabParts.RowLabel(new Rect(band.x, band.y, band.width - chipWidth - 8f, height),
                row.precept.LabelCap, palette.TextPrimary, GameFont.Small, IdeoFaces.Body, IdeoFaces.Size.Body);

            return y + height + RowGap;
        }

        // -------------------------------------------------------------------------------------------

        private static float Doctrine(Rect view, float y, List<DoctrineRow> rows, UIColorPaletteDef palette)
        {
            string suffix = rows.Count == 0
                ? null
                : rows.Count + " precepts across " + IdeoFacts.Issues(rows) + " issues";

            return Block(view, y, "Doctrine", suffix, palette, (inner, top) =>
            {
                if (rows.Count == 0)
                    return TabParts.Line(inner, top, "This faith rules on nothing.", palette.TextDisabled);

                float cursor = top;

                List<string> issues = new List<string>();

                for (int i = 0; i < rows.Count; i++)
                    issues.Add(rows[i].issue);

                // Measured against the issue names in this faith, so "Diversity of thought" is not cut down to
                // make room for a stance that had the width to spare. Half again the room it used to get.
                float column = Column(issues, IdeoFaces.Size.Body, 165f, inner.width, 0.45f, IdeoFaces.Body);

                for (int i = 0; i < rows.Count; i++)
                    cursor = DoctrineRowDraw(inner, cursor, rows[i], column, palette);

                return cursor;
            });
        }

        /// <summary>
        /// One issue and where the faith stands on it.
        ///
        /// <b>Tinted by impact, which is what <c>IdeoUIUtility</c>'s own list does.</b> A high-impact precept
        /// moves colonists' moods and a low-impact one is flavour, and the difference is the only thing that
        /// makes a list of fourteen scannable.
        ///
        /// The icon is <c>Precept.Icon</c>, which falls back to the ideoligion's own when a precept has none, so
        /// a modded precept with no art still lines up in the column instead of leaving a hole.
        /// </summary>
        private static float DoctrineRowDraw(Rect inner, float y, DoctrineRow row, float column,
            UIColorPaletteDef palette)
        {
            const float height = 24f;

            Rect band = new Rect(inner.x, y, inner.width, height);

            // Impact is a stripe rather than a word. It was a column reading "low" fifteen times down a list of
            // nineteen, which spent real width restating the least interesting thing on the row; as a mark in
            // the margin it is read without being looked at, and the two rows that are not low stand out.
            Color impact = row.impact == PreceptImpact.High
                ? palette.Danger
                : row.impact == PreceptImpact.Medium
                    ? palette.Warning
                    : palette.Accent;

            Widgets.DrawBoxSolid(new Rect(band.x, band.y + 3f, 3f, height - 6f), impact);

            TooltipHandler.TipRegion(new Rect(band.x, band.y, 8f, height),
                (TipSignal) (row.impact.ToString().ToLower() + " impact"));

            Rect icon = new Rect(band.x + 11f, band.y + 2f, 20f, 20f);

            UIGuard.Try("Ideoligions.PreceptIcon", () =>
            {
                Texture2D texture = row.precept.Icon;

                if (texture == null)
                    return;

                Color previous = GUI.color;

                GUI.color = palette.TextSecondary;
                GUI.DrawTexture(icon, texture);
                GUI.color = previous;
            }, null);

            // <b>The issue leads and the stance answers it, so the issue is the one set in the reading face.</b>
            // It was the other way round: the issue small, dim and monospaced, the stance large and bright. That
            // made the answer the heading and the question the annotation, and a reader scanning for a subject
            // -- which is the only way anybody uses this list -- had to read the quiet column to find it.
            //
            // The case moved with the face rather than staying with the column. Mono at a dim brightness reads
            // as a deliberate label when it is upper case and as body text set in the wrong font when it is not,
            // which is the rule IdeoFaces.Caps exists for; so the caps followed the mono across.
            TabParts.RowLabel(new Rect(icon.xMax + 8f, band.y, column, height), row.issue, palette.TextPrimary,
                GameFont.Small, IdeoFaces.Body, IdeoFaces.Size.Body);

            // The stance is the answer to the issue and is the thing worth reading, so it takes the primary
            // colour and a third of what is left; the effect follows it in the secondary.
            float stanceX = icon.xMax + 8f + column + 8f;
            float remaining = Mathf.Max(40f, band.xMax - stanceX);
            float stanceWidth = row.effect.NullOrEmpty() ? remaining : remaining * 0.4f;

            TabParts.RowLabel(new Rect(stanceX, band.y, stanceWidth, height), IdeoFaces.Caps(row.stance),
                palette.TextSecondary, GameFont.Tiny, IdeoFaces.Mono, IdeoFaces.Size.Issue);

            if (!row.effect.NullOrEmpty())
            {
                TabParts.RowLabel(new Rect(stanceX + stanceWidth + 6f, band.y,
                        Mathf.Max(20f, remaining - stanceWidth - 6f), height), row.effect,
                    palette.TextSecondary, GameFont.Tiny, IdeoFaces.Mono, IdeoFaces.Size.Small);
            }

            if (Mouse.IsOver(band))
            {
                UIGuard.Try("Ideoligions.PreceptTip",
                    () => TooltipHandler.TipRegion(band, (TipSignal) row.precept.GetTip()), null);
            }

            return y + height + RowGap;
        }
    }
}
