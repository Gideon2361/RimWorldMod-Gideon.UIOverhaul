using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Quests
{
    /// <summary>
    /// The quests tab: offers you have not taken, quests you are running, and the ones that are over.
    ///
    /// <b>The decision belongs in the list, which is the whole argument.</b> Vanilla spends a 36 percent wide
    /// column on the one field that cannot help you choose -- the name -- with a rating and about thirty five
    /// pixels of truncated time beside it, and puts everything a decision actually turns on into a pane that
    /// shows one quest at a time. Comparing two offers therefore means clicking back and forth and remembering.
    ///
    /// <b>So a card answers the four questions in the same order every time.</b> What you get, what it costs,
    /// what could go wrong, and how long you have. A fixed order is what makes cards comparable: the third line
    /// of one card is the third line of every card, so reading down them is reading a column rather than six
    /// paragraphs.
    ///
    /// <b>None of it is new data.</b> The expiry, the challenge rating, the charity flag and the reward stack
    /// are all the game's, and <c>QuestReserves</c> already knows which colonists a quest is holding because the
    /// game needs that to stop you sending them somewhere else. The read side lives in <see cref="QuestFacts"/>.
    /// </summary>
    internal static class QuestPanel
    {
        internal const float WindowWidth = 1180f;
        internal const float WindowHeight = 740f;

        private const float Pad = 12f;
        private const float RailWidth = 210f;
        private const float HeaderHeight = 66f;
        private const float RowGap = 6f;

        /// <summary>Air between the rail's divider and the heading under it, matched to the panel inset.</summary>
        private const float RuleGap = 12f;

        private static Vector2 scroll;
        private static float viewHeight = 1f;

        private static readonly List<OfferRow> Offers = new List<OfferRow>();
        private static readonly List<ActiveRow> Running = new List<ActiveRow>();
        private static readonly List<HistoryRow> Finished = new List<HistoryRow>();
        private static readonly Dictionary<int, float> Measured = new Dictionary<int, float>();
        private static readonly List<DeadlineRow> Clocks = new List<DeadlineRow>();

        internal static void Draw(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Rect body = inRect.ContractedBy(Pad);

            Header(new Rect(body.x, body.y, body.width, HeaderHeight), palette);

            float top = body.y + HeaderHeight + Pad;
            Rect below = new Rect(body.x, top, body.width, body.yMax - top);

            Rail(new Rect(below.x, below.y, RailWidth, below.height), palette);

            Rect main = new Rect(below.x + RailWidth + Pad, below.y,
                below.width - RailWidth - Pad, below.height);

            Rect view = new Rect(0f, 0f, main.width - 18f, viewHeight);

            Widgets.BeginScrollView(main, ref scroll, view);

            float y = 0f;

            switch (QuestFacts.Showing)
            {
                case QuestList.Active:
                    y = ActiveList(view, y, palette);

                    break;

                case QuestList.History:
                    y = HistoryList(view, y, palette);

                    break;

                default:
                    y = OfferList(view, y, palette);

                    break;
            }

            if (Event.current.type == EventType.Layout)
                viewHeight = Mathf.Max(1f, y);

            Widgets.EndScrollView();
        }

        // -------------------------------------------------------------------------------------------
        // Header
        // -------------------------------------------------------------------------------------------

        private static void Header(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceRaised);

            Rect inner = rect.ContractedBy(10f);

            TabParts.RowLabel(new Rect(inner.x, inner.y + 2f, 320f, 26f), "Quests", palette.Accent,
                GameFont.Medium, QuestFaces.Display, QuestFaces.Size.Title);

            int offers = QuestFacts.Count(QuestList.Offers);
            int active = QuestFacts.Count(QuestList.Active);

            // Counted here rather than asked of the list again, because the readout beside it says how many are
            // about to lapse and the two must not disagree on the same frame.
            int soon = 0;

            QuestFacts.Offers(Offers);

            for (int i = 0; i < Offers.Count; i++)
            {
                if (Offers[i].expires != int.MaxValue && Offers[i].expires <= GenDate.TicksPerDay * 3)
                    soon++;
            }

            TabParts.RowLabel(new Rect(inner.x, inner.y + 28f, 320f, 18f),
                offers == 0 ? "Nothing on offer" : offers + (offers == 1 ? " offer waiting" : " offers waiting"),
                palette.TextSecondary, GameFont.Tiny, QuestFaces.Condensed, QuestFaces.Size.Subtitle);

            float right = inner.xMax;

            right = Readout(inner, right, soon.ToString(), "expiring", soon > 0 ? palette.Warning : null, palette);
            right = Readout(inner, right, active.ToString(), "active", null, palette);
            Readout(inner, right, offers.ToString(), "offers", null, palette);
        }

        /// <summary>
        /// One readout: the figure with its caption in small caps under it.
        ///
        /// Figure above caption, and both right-aligned, for the reason the ideoligion header settled on: a
        /// readout is read figure first and the caption only says which figure it is, so a dim label on top puts
        /// something in the way of every number.
        /// </summary>
        private static float Readout(Rect inner, float right, string value, string caption, Color? tint,
            UIColorPaletteDef palette)
        {
            string label = QuestFaces.Caps(caption);

            float width = Mathf.Max(
                UITextControl.Width(value, QuestFaces.Mono, QuestFaces.Size.Readout),
                UITextControl.Width(label, QuestFaces.Mono, QuestFaces.Size.Caption)) + 4f;

            float figure = UITextControl.LineHeight(QuestFaces.Mono, QuestFaces.Size.Readout);
            float under = UITextControl.LineHeight(QuestFaces.Mono, QuestFaces.Size.Caption);

            float top = inner.y + (inner.height - figure - under - 2f) * 0.5f;

            Rect band = new Rect(right - width, top, width, figure + under + 2f);

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;

                GUI.color = tint ?? palette.TextPrimary;
                UITextControl.LabelEllipses(new Rect(band.x, band.y, band.width, figure), value,
                    QuestFaces.Mono, QuestFaces.Size.Readout);

                GUI.color = palette.TextSecondary;
                UITextControl.LabelEllipses(new Rect(band.x, band.y + figure + 2f, band.width, under), label,
                    QuestFaces.Mono, QuestFaces.Size.Caption);
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

        private static void Rail(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect view = rect.ContractedBy(6f);
            float y = view.y + 2f;

            // The leading space is deliberate: the heading is drawn from the panel's own x, so without it the
            // first letter sits on the border. A space rather than an inset rect, which would take the rule too.
            y = TabParts.Heading(view, y, QuestFaces.Caps(" Quests"), palette, false, QuestFaces.Mono,
                QuestFaces.Size.RailHead);

            y = Entry(view, y, QuestList.Offers, "Offers", palette);
            y = Entry(view, y, QuestList.Active, "Active", palette);
            y = Entry(view, y, QuestList.History, "History", palette);

            int dismissed = QuestFacts.Count(QuestList.Dismissed);

            if (dismissed <= 0)
                return;

            y += 6f;

            y = TabParts.Heading(view, y, QuestFaces.Caps(" Set aside"), palette, true, QuestFaces.Mono,
                QuestFaces.Size.RailHead, RuleGap);

            Entry(view, y, QuestList.Dismissed, "Dismissed", palette);
        }

        private static float Entry(Rect view, float y, QuestList which, string label, UIColorPaletteDef palette)
        {
            const float height = 26f;

            Rect row = new Rect(view.x, y, view.width, height);
            bool on = QuestFacts.Showing == which;

            if (on)
                UIElementPainter.FillRounded(row, palette.SelectionOverlay);
            else if (Mouse.IsOver(row))
                UIElementPainter.FillRounded(row, palette.HoverOverlay);

            string count = QuestFacts.Count(which).ToString();
            float countWidth = 28f;

            TabParts.RowLabel(new Rect(row.xMax - countWidth - 6f, row.y, countWidth, height), count,
                on ? palette.Accent : palette.TextDisabled, GameFont.Tiny, QuestFaces.Mono,
                QuestFaces.Size.RailCount);

            TabParts.RowLabel(new Rect(row.x + 8f, row.y, row.width - countWidth - 18f, height), label,
                on ? palette.Accent : palette.TextPrimary, GameFont.Small, QuestFaces.Condensed,
                QuestFaces.Size.RailName);

            if (Widgets.ButtonInvisible(row))
            {
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();

                QuestFacts.Showing = which;
                QuestFacts.Selected = null;
                scroll = Vector2.zero;
            }

            return y + height + 2f;
        }

        // -------------------------------------------------------------------------------------------
        // Offers
        // -------------------------------------------------------------------------------------------

        private static float OfferList(Rect view, float y, UIColorPaletteDef palette)
        {
            QuestFacts.Offers(Offers);

            if (Offers.Count == 0)
            {
                return TabParts.Line(view, y + 20f,
                    QuestFacts.Showing == QuestList.Dismissed
                        ? "Nothing has been set aside."
                        : "No quests are on offer. The storyteller will bring one along.",
                    palette.TextDisabled);
            }

            y = Strip(view, y, palette);

            for (int i = 0; i < Offers.Count; i++)
                y = Offer(view, y, Offers[i], palette);

            return y;
        }

        /// <summary>
        /// One offer as a card: name, rating and deadline across the top, then the four lines.
        ///
        /// <b>Measured from the last draw rather than predicted.</b> A formula for how tall a card will be is
        /// wrong the first time a reward kind is added to it and fails silently, and running the body twice to
        /// measure would hit-test every control in it twice and land every click twice.
        /// </summary>
        private static float Offer(Rect view, float y, OfferRow row, UIColorPaletteDef palette)
        {
            float height;

            if (!Measured.TryGetValue(row.quest.id, out height))
                height = 96f;

            Rect box = new Rect(view.x, y, view.width, height);
            bool on = QuestFacts.Selected == row.quest;

            UIElementPainter.OutlineRounded(box, on ? palette.Accent : palette.Border, palette.PanelBackground);

            // The stripe carries urgency, which is the one thing about an offer that changes on its own while
            // you are looking at the list.
            Color stripe = row.expires == int.MaxValue
                ? palette.Border
                : row.expires <= GenDate.TicksPerDay
                    ? palette.Danger
                    : row.expires <= GenDate.TicksPerDay * 3
                        ? palette.Warning
                        : palette.Border;

            Widgets.DrawBoxSolid(new Rect(box.x, box.y, 3f, box.height), stripe);

            Rect inner = new Rect(box.x + 12f, box.y + 8f, box.width - 24f, box.height - 16f);

            float cursor = inner.y;

            cursor = OfferHead(inner, cursor, row, palette);
            cursor = OfferLines(inner, cursor, row, palette);

            if (Event.current.type == EventType.Layout)
                Measured[row.quest.id] = cursor - box.y + 10f;

            if (Widgets.ButtonInvisible(box))
            {
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();

                QuestFacts.Selected = on ? null : row.quest;
            }

            return box.yMax + RowGap;
        }

        private static float OfferHead(Rect inner, float y, OfferRow row, UIColorPaletteDef palette)
        {
            const float height = 22f;

            Rect band = new Rect(inner.x, y, inner.width, height);

            string when = row.expires == int.MaxValue
                ? "no deadline"
                : "expires in " + QuestFacts.Period(row.expires);

            float whenWidth = UITextControl.Width(when, QuestFaces.Mono, QuestFaces.Size.When) + 8f;

            Color whenColor = row.expires == int.MaxValue
                ? palette.TextDisabled
                : row.expires <= GenDate.TicksPerDay
                    ? palette.Danger
                    : row.expires <= GenDate.TicksPerDay * 3
                        ? palette.Warning
                        : palette.TextSecondary;

            TextAnchor previousAnchor = Text.Anchor;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;

                Color previousColor = GUI.color;

                GUI.color = whenColor;

                UITextControl.LabelEllipses(new Rect(band.xMax - whenWidth, band.y, whenWidth, height), when,
                    QuestFaces.Mono, QuestFaces.Size.When);

                GUI.color = previousColor;
            }
            finally
            {
                Text.Anchor = previousAnchor;
            }

            float right = band.xMax - whenWidth - 8f;

            if (row.charity)
            {
                float chip = TabParts.PillWidth(QuestFaces.Caps("charity"), 9999f, QuestFaces.Mono,
                    QuestFaces.Size.Chip);

                TabParts.Pill(band, right - chip, band.y + 2f, QuestFaces.Caps("charity"), palette.Success,
                    palette, chip, null, QuestFaces.Mono, QuestFaces.Size.Chip);

                right -= chip + 6f;
            }

            right = Rating(band, right, row.rating, palette);

            TabParts.RowLabel(new Rect(band.x, band.y, Mathf.Max(60f, right - band.x - 8f), height),
                row.name, palette.TextPrimary, GameFont.Small, QuestFaces.Condensed, QuestFaces.Size.Name);

            return band.yMax + 2f;
        }

        /// <summary>
        /// The challenge rating as five dots, filled to the rating.
        ///
        /// <b>Dots rather than the number.</b> Vanilla prints the figure, and a figure out of an unstated
        /// maximum tells you nothing the first time you see it: three is only meaningful once you know it is
        /// three of five. A quest with no rating draws nothing at all rather than five empty dots, since an
        /// absent rating and a rating of zero are different things.
        /// </summary>
        private static float Rating(Rect band, float right, int rating, UIColorPaletteDef palette)
        {
            if (rating <= 0)
                return right;

            const float dot = 7f;
            const float gap = 3f;
            const int most = 5;

            float width = most * dot + (most - 1) * gap;
            float x = right - width;

            for (int i = 0; i < most; i++)
            {
                Rect spot = new Rect(x + i * (dot + gap), band.y + (band.height - dot) * 0.5f, dot, dot);

                Widgets.DrawBoxSolid(spot, i < rating ? palette.Warning : palette.Border);
            }

            TooltipHandler.TipRegion(new Rect(x, band.y, width, band.height),
                (TipSignal) ("Challenge rating " + rating + " of " + most
                             + ".\n\nRimWorld's own estimate of how hard this quest is, generated with it."));

            return x - 10f;
        }

        /// <summary>
        /// The four lines, in the order the question is actually asked in.
        ///
        /// Get, costs, risk, colony. Fixed, because a fixed order is what makes two cards comparable.
        /// </summary>
        private static float OfferLines(Rect inner, float y, OfferRow row, UIColorPaletteDef palette)
        {
            y = Choices(inner, y, row, palette);

            string costs = row.expires == int.MaxValue
                ? "Nothing until you accept"
                : "Decide within " + QuestFacts.Period(row.expires);

            y = Line(inner, y, "Costs", costs, palette.TextSecondary, palette);

            if (!row.factions.NullOrEmpty())
                y = Line(inner, y, "With", row.factions, palette.TextSecondary, palette);

            return y;
        }

        /// <summary>
        /// The reward stack, showing the alternatives as alternatives.
        ///
        /// <b>This is the line vanilla's list does not have at all,</b> and the one a mockup review caught as
        /// wrong in ours: a quest usually offers a choice, and drawing one reward of three misrepresents the
        /// offer. Two or more choices are drawn as a set with a count above them; a single reward stack is
        /// drawn as itself.
        /// </summary>
        private static float Choices(Rect inner, float y, OfferRow row, UIColorPaletteDef palette)
        {
            if (row.choices.Count == 0 && row.rewards.Count == 0)
                return Line(inner, y, "Get", "Nothing. This one is a favour.", palette.TextDisabled, palette);

            if (row.choices.Count > 1)
            {
                y = Line(inner, y, "Get", "Choose one of " + row.choices.Count + " on acceptance",
                    palette.Warning, palette);

                for (int i = 0; i < row.choices.Count; i++)
                    y = Alternative(inner, y, row.choices[i], palette);
            }
            else if (row.choices.Count == 1)
            {
                y = Line(inner, y, "Get", Joined(row.choices[0].rewards), palette.TextPrimary, palette);
                y = Pawns(inner, y, row.choices[0].rewards, palette);
            }

            if (row.rewards.Count > 0)
            {
                y = Line(inner, y, row.choices.Count > 1 ? "Also" : "Get", Joined(row.rewards),
                    palette.TextPrimary, palette);

                y = Pawns(inner, y, row.rewards, palette);
            }

            return y;
        }

        private static float Alternative(Rect inner, float y, ChoiceRow choice, UIColorPaletteDef palette)
        {
            float height = Mathf.Max(20f, UITextControl.LineHeight(QuestFaces.Body, QuestFaces.Size.Body) + 6f);

            Rect band = new Rect(inner.x + 76f, y, inner.width - 76f, height);

            UIElementPainter.OutlineRounded(band, palette.Border, palette.SurfaceSunken);

            TabParts.RowLabel(new Rect(band.x + 8f, band.y, band.width - 16f, height), Joined(choice.rewards),
                palette.TextPrimary, GameFont.Small, QuestFaces.Body, QuestFaces.Size.Body);

            y = band.yMax + 2f;

            return Pawns(inner, y, choice.rewards, palette);
        }

        /// <summary>
        /// A button per offered person, opening the read-only sheet.
        ///
        /// <b>One per pawn rather than a picker,</b> which is the rule the offer panel settled in 14167: an
        /// offer of several people exists to be compared, and a picker turns a comparison into a memory test.
        /// The click chooses which sheet is expanded, never which is reachable.
        /// </summary>
        private static float Pawns(Rect inner, float y, List<RewardRow> rewards, UIColorPaletteDef palette)
        {
            if (!QuestPawnSheet.Enabled)
                return y;

            for (int i = 0; i < rewards.Count; i++)
            {
                Pawn pawn = rewards[i].pawn;

                if (pawn == null)
                    continue;

                string label = "Read " + UIGuard.Try("Quests.PawnName",
                    () => pawn.LabelShortCap.ToString(), "them", null) + "...";

                float width = TabParts.ButtonWidth(label);
                Rect button = new Rect(inner.x + 76f, y, width, 26f);

                if (TabParts.Button(button, label, palette, true, false,
                        "Open this person's skills, traits, backstory and health, exactly as they will read "
                        + "once they have joined. Nothing here can be changed."))
                    QuestPawnSheet.Open(pawn);

                y = button.yMax + 3f;
            }

            return y;
        }

        private static string Joined(List<RewardRow> rewards)
        {
            string joined = null;

            for (int i = 0; i < rewards.Count; i++)
            {
                if (rewards[i].text.NullOrEmpty())
                    continue;

                joined = joined == null ? rewards[i].text : joined + ", " + rewards[i].text;
            }

            return joined ?? "Nothing";
        }

        /// <summary>One labelled line of a card: a small-caps label in the left column, the fact beside it.</summary>
        private static float Line(Rect inner, float y, string label, string text, Color color,
            UIColorPaletteDef palette)
        {
            float height = Mathf.Max(
                UITextControl.LineHeight(QuestFaces.Body, QuestFaces.Size.Body),
                UITextControl.LineHeight(QuestFaces.Mono, QuestFaces.Size.Label)) + 2f;

            Rect band = new Rect(inner.x, y, inner.width, height);

            TabParts.RowLabel(new Rect(band.x, band.y, 70f, height), QuestFaces.Caps(label),
                palette.TextDisabled, GameFont.Tiny, QuestFaces.Mono, QuestFaces.Size.Label);

            TabParts.RowLabel(new Rect(band.x + 76f, band.y, band.width - 76f, height), text, color,
                GameFont.Small, QuestFaces.Body, QuestFaces.Size.Body);

            return band.yMax + 1f;
        }

        // -------------------------------------------------------------------------------------------
        // Active
        // -------------------------------------------------------------------------------------------

        private static float ActiveList(Rect view, float y, UIColorPaletteDef palette)
        {
            QuestFacts.Active(Running);

            if (Running.Count == 0)
                return TabParts.Line(view, y + 20f, "Nothing is running.", palette.TextDisabled);

            y = Chart(view, y, palette);

            for (int i = 0; i < Running.Count; i++)
                y = ActiveOne(view, y, Running[i], palette);

            return y;
        }

        private static float ActiveOne(Rect view, float y, ActiveRow row, UIColorPaletteDef palette)
        {
            float height = 30f + Mathf.Max(1, row.reserved.Count) * 20f;

            Rect box = new Rect(view.x, y, view.width, height);

            UIElementPainter.OutlineRounded(box, palette.Border, palette.PanelBackground);

            Rect inner = new Rect(box.x + 12f, box.y + 6f, box.width - 24f, box.height - 12f);

            TabParts.RowLabel(new Rect(inner.x, inner.y, inner.width * 0.6f, 22f), row.name, palette.TextPrimary,
                GameFont.Small, QuestFaces.Condensed, QuestFaces.Size.Name);

            if (!row.factions.NullOrEmpty())
            {
                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;

                try
                {
                    Text.Anchor = TextAnchor.MiddleRight;
                    GUI.color = palette.TextSecondary;

                    UITextControl.LabelEllipses(new Rect(inner.x + inner.width * 0.6f, inner.y,
                        inner.width * 0.4f, 22f), row.factions, QuestFaces.Mono, QuestFaces.Size.Small);
                }
                finally
                {
                    GUI.color = previousColor;
                    Text.Anchor = previousAnchor;
                }
            }

            float cursor = inner.y + 24f;

            if (row.reserved.Count == 0)
            {
                TabParts.RowLabel(new Rect(inner.x, cursor, inner.width, 20f), "Holding nobody",
                    palette.TextDisabled, GameFont.Tiny, QuestFaces.Body, QuestFaces.Size.Body);

                return box.yMax + RowGap;
            }

            for (int i = 0; i < row.reserved.Count; i++)
            {
                Pawn pawn = row.reserved[i];

                TabParts.RowLabel(new Rect(inner.x, cursor, 70f, 20f), QuestFaces.Caps(i == 0 ? "Holding" : ""),
                    palette.TextDisabled, GameFont.Tiny, QuestFaces.Mono, QuestFaces.Size.Label);

                TabParts.RowLabel(new Rect(inner.x + 76f, cursor, inner.width - 76f, 20f),
                    UIGuard.Try("Quests.Held", () => pawn.LabelShortCap.ToString(), "?", null),
                    palette.TextPrimary, GameFont.Small, QuestFaces.Body, QuestFaces.Size.Body);

                cursor += 20f;
            }

            return box.yMax + RowGap;
        }

        // -------------------------------------------------------------------------------------------
        // The shared axis
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// How many days the chart covers: far enough to hold the furthest clock, and no further.
        ///
        /// <b>Scaled to the colony rather than fixed.</b> A fixed fortnight squashes six offers that all lapse
        /// within three days into the first fifth of the axis, which is the case the chart exists for. Floored
        /// at three days so a single imminent deadline does not fill the width and read as comfortable, and
        /// capped so one quest running to a season does not flatten everything else against the left edge.
        /// </summary>
        private static float Span(float furthest)
        {
            const float floor = 3f * GenDate.TicksPerDay;
            const float ceiling = 20f * GenDate.TicksPerDay;

            return Mathf.Clamp(furthest, floor, ceiling);
        }

        /// <summary>The day ticks under an axis, at whatever interval keeps the labels apart.</summary>
        private static void Axis(Rect track, float span, UIColorPaletteDef palette)
        {
            int days = Mathf.Max(1, Mathf.CeilToInt(span / GenDate.TicksPerDay));
            int step = days <= 6 ? 1 : days <= 12 ? 2 : 5;

            Color previous = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            try
            {
                Text.Anchor = TextAnchor.UpperCenter;

                for (int day = 0; day <= days; day += step)
                {
                    float x = track.x + track.width * (day * GenDate.TicksPerDay / span);

                    if (x > track.xMax)
                        break;

                    GUI.color = palette.Border;

                    Widgets.DrawLineVertical(x, track.y, track.height);

                    GUI.color = palette.TextDisabled;

                    UITextControl.Label(new Rect(x - 20f, track.yMax + 2f, 40f, 14f),
                        day == 0 ? "now" : "+" + day + "d", QuestFaces.Mono, QuestFaces.Size.Caption);
                }
            }
            finally
            {
                Text.Anchor = previousAnchor;
                GUI.color = previous;
            }
        }

        /// <summary>
        /// Every offer's deadline on one axis.
        ///
        /// <b>One axis is the whole point.</b> Six expiry strings down a column are six numbers to hold in your
        /// head; on a shared scale, the offers that lapse together are visibly together, which is the thing
        /// worth knowing before spending a caravan on one of them.
        /// </summary>
        private static float Strip(Rect view, float y, UIColorPaletteDef palette)
        {
            float furthest = 0f;
            int dated = 0;

            for (int i = 0; i < Offers.Count; i++)
            {
                if (Offers[i].expires == int.MaxValue)
                    continue;

                dated++;
                furthest = Mathf.Max(furthest, Offers[i].expires);
            }

            if (dated == 0)
                return y;

            float span = Span(furthest);

            Rect box = new Rect(view.x, y, view.width, 74f);

            UIElementPainter.OutlineRounded(box, palette.Border, palette.PanelBackground);

            TabParts.RowLabel(new Rect(box.x + 10f, box.y + 4f, box.width - 20f, 16f),
                QuestFaces.Caps("Deadlines"), palette.TextSecondary, GameFont.Tiny, QuestFaces.Mono,
                QuestFaces.Size.BlockHead);

            Rect track = new Rect(box.x + 14f, box.y + 24f, box.width - 28f, 30f);

            Axis(track, span, palette);

            for (int i = 0; i < Offers.Count; i++)
            {
                OfferRow row = Offers[i];

                if (row.expires == int.MaxValue)
                    continue;

                float x = track.x + track.width * (row.expires / span);

                Color tint = row.expires <= GenDate.TicksPerDay
                    ? palette.Danger
                    : row.expires <= GenDate.TicksPerDay * 3
                        ? palette.Warning
                        : palette.Accent;

                // Staggered by index so two offers lapsing within an hour of each other do not draw one pin
                // over the other and read as one deadline.
                float height = 10f + i % 3 * 6f;

                Widgets.DrawBoxSolid(new Rect(x - 1f, track.yMax - height, 2f, height), tint);

                Rect hot = new Rect(x - 6f, track.y, 12f, track.height);

                if (!Mouse.IsOver(hot))
                    continue;

                Widgets.DrawBoxSolid(new Rect(x - 1f, track.y, 2f, track.height), tint);

                TooltipHandler.TipRegion(hot,
                    (TipSignal) (row.name + "\n\nExpires in " + QuestFacts.Period(row.expires) + "."));
            }

            return box.yMax + RowGap;
        }

        /// <summary>
        /// What the running quests are committed to, on the same axis as each other.
        ///
        /// <b>A bar per clock rather than per quest.</b> A quest carries as many deadlines as its content gave
        /// it, and the overlap that matters is between clocks, not between quests: a lodger leaving on day 51
        /// and a raid due on day 52 belong to one quest and are two different problems.
        ///
        /// <b>A quest with no clock gets a row and no bar,</b> rather than being left out. Open-ended is an
        /// answer to "when does this end", and a chart that silently drops those quests reads as a complete
        /// list of commitments when it is not.
        /// </summary>
        private static float Chart(Rect view, float y, UIColorPaletteDef palette)
        {
            float furthest = 0f;

            for (int i = 0; i < Running.Count; i++)
            {
                if (Running[i].ends != int.MaxValue)
                    furthest = Mathf.Max(furthest, Running[i].ends);
            }

            if (furthest <= 0f)
                return y;

            float span = Span(furthest);

            float height = 30f + Running.Count * 22f + 18f;
            Rect box = new Rect(view.x, y, view.width, height);

            UIElementPainter.OutlineRounded(box, palette.Border, palette.PanelBackground);

            TabParts.RowLabel(new Rect(box.x + 10f, box.y + 4f, box.width - 20f, 16f),
                QuestFaces.Caps("Commitments"), palette.TextSecondary, GameFont.Tiny, QuestFaces.Mono,
                QuestFaces.Size.BlockHead);

            const float names = 190f;

            float cursor = box.y + 24f;

            for (int i = 0; i < Running.Count; i++)
            {
                ActiveRow row = Running[i];

                Rect band = new Rect(box.x + 10f, cursor, box.width - 20f, 20f);

                TabParts.RowLabel(new Rect(band.x, band.y, names - 8f, band.height), row.name,
                    palette.TextPrimary, GameFont.Small, QuestFaces.Condensed, QuestFaces.Size.Body);

                Rect track = new Rect(band.x + names, band.y + 3f, band.width - names - 4f, 14f);

                UIElementPainter.OutlineRounded(track, palette.Border, palette.SurfaceSunken);

                if (row.ends == int.MaxValue)
                {
                    TabParts.RowLabel(new Rect(track.x + 6f, track.y, track.width - 12f, track.height),
                        "open ended", palette.TextDisabled, GameFont.Tiny, QuestFaces.Mono,
                        QuestFaces.Size.Small);

                    cursor = band.yMax + 2f;

                    continue;
                }

                QuestFacts.Deadlines(row.quest, Clocks);

                for (int c = 0; c < Clocks.Count; c++)
                {
                    float width = Mathf.Max(3f, track.width * (Clocks[c].ticks / span));

                    Color tint = Clocks[c].ticks <= GenDate.TicksPerDay
                        ? palette.Danger
                        : Clocks[c].ticks <= GenDate.TicksPerDay * 3
                            ? palette.Warning
                            : palette.Accent;

                    Rect bar = new Rect(track.x, track.y, width, track.height);

                    UIElementPainter.OutlineRounded(bar, tint,
                        UIElementPainter.Composite(palette.SurfaceSunken,
                            new Color(tint.r, tint.g, tint.b, 0.22f)));

                    if (Mouse.IsOver(bar) && !Clocks[c].text.NullOrEmpty())
                        TooltipHandler.TipRegion(bar, (TipSignal) (row.name + "\n\n" + Clocks[c].text));
                }

                cursor = band.yMax + 2f;
            }

            Axis(new Rect(box.x + 10f + names, box.y + 24f, box.width - 20f - names - 4f,
                cursor - box.y - 26f), span, palette);

            return box.yMax + RowGap;
        }

        // -------------------------------------------------------------------------------------------
        // History
        // -------------------------------------------------------------------------------------------

        private static float HistoryList(Rect view, float y, UIColorPaletteDef palette)
        {
            QuestFacts.History(Finished);

            if (Finished.Count == 0)
                return TabParts.Line(view, y + 20f, "Nothing has finished yet.", palette.TextDisabled);

            for (int i = 0; i < Finished.Count; i++)
                y = HistoryOne(view, y, Finished[i], palette);

            return y;
        }

        private static float HistoryOne(Rect view, float y, HistoryRow row, UIColorPaletteDef palette)
        {
            const float height = 26f;

            Rect band = new Rect(view.x, y, view.width, height);

            if (Mouse.IsOver(band))
                UIElementPainter.FillRounded(band, palette.HoverOverlay);

            Color tint = row.state == QuestState.EndedSuccess
                ? palette.Success
                : row.state == QuestState.EndedFailed
                    ? palette.Danger
                    : palette.TextDisabled;

            float chip = TabParts.PillWidth(QuestFaces.Caps(row.outcome), 9999f, QuestFaces.Mono,
                QuestFaces.Size.Chip);

            TabParts.Pill(band, band.xMax - chip, band.y + 3f, QuestFaces.Caps(row.outcome), tint, palette,
                chip, null, QuestFaces.Mono, QuestFaces.Size.Chip);

            string ago = QuestFacts.Period(row.ago) + " ago";
            float agoWidth = UITextControl.Width(ago, QuestFaces.Mono, QuestFaces.Size.Small) + 10f;

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextDisabled;

                UITextControl.LabelEllipses(new Rect(band.xMax - chip - agoWidth - 8f, band.y, agoWidth, height),
                    ago, QuestFaces.Mono, QuestFaces.Size.Small);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }

            TabParts.RowLabel(new Rect(band.x, band.y, band.width - chip - agoWidth - 16f, height), row.name,
                palette.TextPrimary, GameFont.Small, QuestFaces.Condensed, QuestFaces.Size.Name);

            return band.yMax + 2f;
        }
    }
}
