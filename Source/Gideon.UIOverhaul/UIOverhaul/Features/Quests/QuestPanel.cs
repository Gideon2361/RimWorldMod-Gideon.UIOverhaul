using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using RimWorld.Planet;
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
    [StaticConstructorOnStartup]
    internal static class QuestPanel
    {
        private static Vector2 railScroll;
        private static bool railDragging;
        private static float railDragOffset;

        internal const float WindowWidth = 1180f;
        internal const float WindowHeight = 740f;

        private const float Pad = 12f;
        private const float RailWidth = 210f;
        private const float HeaderHeight = 66f;
        private const float RowGap = 6f;

        /// <summary>Side of the header glyph, and the air between it and the title.</summary>
        private const float GlyphSize = 34f;

        private const float GlyphGap = 10f;

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

            if (QuestFacts.Selected != null)
            {
                y = Detail(view, y, QuestFacts.Selected, palette);

                if (Event.current.type == EventType.Layout)
                    viewHeight = Mathf.Max(1f, y);

                Widgets.EndScrollView();

                return;
            }

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

            float text = inner.x;

            if (Glyph != null)
            {
                Rect mark = new Rect(inner.x, inner.y + (inner.height - GlyphSize) * 0.5f, GlyphSize,
                    GlyphSize);

                Color previous = GUI.color;

                GUI.color = palette.Accent;
                GUI.DrawTexture(mark, Glyph);
                GUI.color = previous;

                text = mark.xMax + GlyphGap;
            }

            TabParts.RowLabel(new Rect(text, inner.y + 2f, 320f, 26f), "Quests", palette.Accent,
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

            TabParts.RowLabel(new Rect(text, inner.y + 28f, 320f, 18f),
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
        // One quest, opened
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The detail view: everything a card cannot hold, for the one quest that is open.
        ///
        /// <b>It replaces the list rather than sitting beside it.</b> Vanilla splits the width and gives the
        /// detail 64 percent of it, which is what forces its list down to names. The cards are the argument of
        /// this screen, so they get the full width, and opening one takes the whole column for as long as it is
        /// open. Closing is one click on a control that is always in the same place.
        ///
        /// <b>The accept controls live here and nowhere else.</b> A card is for comparing; a decision that
        /// spends a caravan and a fortnight should cost one deliberate click more than skimming does.
        /// </summary>
        private static float Detail(Rect view, float y, Quest quest, UIColorPaletteDef palette)
        {
            OfferRow row = QuestFacts.Offer(quest);

            Rect back = new Rect(view.x, y, 120f, 26f);

            if (TabParts.Button(back, "Back", palette))
            {
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();

                QuestFacts.Selected = null;
            }

            bool aside = row.quest.dismissed;

            Rect shelf = new Rect(back.xMax + 8f, back.y, 150f, 26f);

            if (TabParts.Button(shelf, aside ? "Put back on the list" : "Set aside", palette, true, false,
                    aside
                        ? "Return this to the list it came from."
                        : "Take this off the list. It carries on and its clock keeps running; it just stops "
                          + "taking up a row. Dismissed quests are their own entry in the rail."))
                QuestAccept.SetAside(quest, !aside);

            y = back.yMax + 8f;

            TabParts.RowLabel(new Rect(view.x, y, view.width, 30f), row.name, palette.Accent, GameFont.Medium,
                QuestFaces.Display, QuestFaces.Size.Title);

            y += 32f;

            string under = row.factions.NullOrEmpty() ? "No faction involved" : "With " + row.factions;

            if (row.rating > 0)
                under += "  -  challenge rating " + row.rating + " of 5";

            TabParts.RowLabel(new Rect(view.x, y, view.width, 20f), under, palette.TextSecondary, GameFont.Tiny,
                QuestFaces.Condensed, QuestFaces.Size.Subtitle);

            y += 26f;

            y = Accept(view, y, quest, row, palette);
            y = Block(view, y, "What you get", quest, row, palette);
            y = Prose(view, y, quest, palette);

            return y;
        }

        /// <summary>
        /// The accept controls, in whichever of the three shapes this quest calls for.
        ///
        /// <b>A quest with two or more rewards to choose between gets a button per alternative,</b> because
        /// picking the reward is what accepts it. A quest with exactly one still has to have that one chosen,
        /// so its single button takes the choice on the way through. A quest with no reward part at all is
        /// accepted plainly, which is the case this screen missed first time round.
        ///
        /// <b>Disabled with a reason rather than hidden.</b> A button that is not there tells you nothing; one
        /// that is greyed with RimWorld's own refusal on it tells you what to go and fix.
        /// </summary>
        private static float Accept(Rect view, float y, Quest quest, OfferRow row, UIColorPaletteDef palette)
        {
            if (QuestFacts.Showing != QuestList.Offers && QuestFacts.Showing != QuestList.Dismissed)
                return y;

            AcceptanceReport can = QuestAccept.Can(quest);
            QuestPart_Choice choice = QuestAccept.Choice(quest);

            string refusal = can.Accepted ? null : can.Reason;

            if (choice != null && choice.choices != null && choice.choices.Count >= 2)
            {
                TabParts.RowLabel(new Rect(view.x, y, view.width, 20f),
                    QuestFaces.Caps("Take one of these"), palette.TextDisabled, GameFont.Tiny, QuestFaces.Mono,
                    QuestFaces.Size.Label);

                y += 22f;

                for (int i = 0; i < row.choices.Count && i < choice.choices.Count; i++)
                    y = Alternative(view, y, row.choices[i], choice, choice.choices[i], quest, can, palette);

                return y + 6f;
            }

            QuestPart_Choice.Choice single = choice != null && choice.choices != null && choice.choices.Count == 1
                ? choice.choices[0]
                : null;

            Rect accept = new Rect(view.x, y, 180f, 30f);

            if (TabParts.Button(accept, "Accept quest", palette, can.Accepted, true,
                    refusal ?? "Accept this quest. If it needs somebody to accept it, you will be asked who."))
            {
                QuestPart_Choice.Choice localSingle = single;
                QuestPart_Choice localPart = choice;

                QuestAccept.Begin(quest,
                    localSingle == null ? (System.Action) null : () => localPart.Choose(localSingle));
            }

            return accept.yMax + 10f;
        }

        /// <summary>The reward stack in full, with a sheet button under any person among them.</summary>
        private static float Block(Rect view, float y, string title, Quest quest, OfferRow row,
            UIColorPaletteDef palette)
        {
            // Nothing to head. A quest whose whole reward is the choice above has no separate stack, and a
            // heading with blank space under it reads as a section that failed to draw.
            if (row.rewards.Count == 0)
                return y;

            TabParts.RowLabel(new Rect(view.x, y, view.width, 20f), QuestFaces.Caps(title),
                palette.TextDisabled, GameFont.Tiny, QuestFaces.Mono, QuestFaces.Size.Label);

            y += 22f;

            if (row.rewards.Count > 0)
            {
                string listed = Full(row.rewards);
                float tall = Wrapped(listed, view.width - 8f);

                Color previousColor = GUI.color;

                try
                {
                    GUI.color = palette.TextPrimary;

                    UITextControl.Paragraph(new Rect(view.x + 8f, y, view.width - 8f, tall), listed,
                        QuestFaces.Body, QuestFaces.Size.Body);
                }
                finally
                {
                    GUI.color = previousColor;
                }

                y += tall + 4f;
                y = Pawns(view, y, row.rewards, palette);
            }

            return y + 6f;
        }

        /// <summary>
        /// The quest's own words, wrapped.
        ///
        /// <b>Resolved through the game's own text pipeline,</b> so the grammar rules and named arguments a
        /// quest was written with come out as they were meant to rather than as raw tags.
        /// </summary>
        private static float Prose(Rect view, float y, Quest quest, UIColorPaletteDef palette)
        {
            string text = UIGuard.Try("Quests.Description",
                () => quest.description.RawText.NullOrEmpty() ? null : quest.description.Resolve(), null, null);

            if (text.NullOrEmpty())
                return y;

            TabParts.RowLabel(new Rect(view.x, y, view.width, 20f), QuestFaces.Caps("The offer"),
                palette.TextDisabled, GameFont.Tiny, QuestFaces.Mono, QuestFaces.Size.Label);

            y += 22f;

            Color previousColor = GUI.color;

            try
            {
                GUI.color = palette.TextSecondary;

                float height = Wrapped(text, view.width - 8f);

                UITextControl.Paragraph(new Rect(view.x + 8f, y, view.width - 8f, height), text,
                    QuestFaces.Body, QuestFaces.Size.Body);

                y += height + 8f;
            }
            finally
            {
                GUI.color = previousColor;
            }

            return y;
        }

        // -------------------------------------------------------------------------------------------
        // Rail
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The four lists, with "Set aside" separated below because dismissed quests are a different kind of
        /// thing rather than a fourth peer.
        ///
        /// <b>Mono figures, condensed names.</b> The counts line up as a column because they are drawn in the
        /// mono face at a point size; the labels are condensed because width is the scarce thing in a rail.
        /// Both are the rail control's own settings now rather than this screen's drawing code.
        /// </summary>
        private static void Rail(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            List<UIRailElement> elements = new List<UIRailElement>();

            elements.Add(new UIRailSectionHeaderControl(QuestFaces.Caps("Quests"))
            {
                Face = QuestFaces.Mono,
                Points = QuestFaces.Size.RailHead,
                Color = palette.TextDisabled
            });

            elements.Add(Row(QuestList.Offers, "Offers", palette));
            elements.Add(Row(QuestList.Active, "Active", palette));
            elements.Add(Row(QuestList.History, "History", palette));

            if (QuestFacts.Count(QuestList.Dismissed) > 0)
            {
                elements.Add(new UIRailDividerControl());
                elements.Add(new UIRailSectionHeaderControl(QuestFaces.Caps("Set aside"))
                {
                    Face = QuestFaces.Mono,
                    Points = QuestFaces.Size.RailHead,
                    Color = palette.TextDisabled
                });

                elements.Add(Row(QuestList.Dismissed, "Dismissed", palette));
            }

            string picked = UIRailControl.Draw(rect.ContractedBy(6f), elements,
                QuestFacts.Showing.ToString(), ref railScroll, ref railDragging, ref railDragOffset, palette,
                false);

            if (picked == null)
                return;

            foreach (QuestList which in (QuestList[]) Enum.GetValues(typeof(QuestList)))
            {
                if (which.ToString() == picked)
                {
                    QuestFacts.Showing = which;

                    break;
                }
            }
        }

        private static UIRailClickableEntry Row(QuestList which, string label, UIColorPaletteDef palette)
        {
            bool on = QuestFacts.Showing == which;

            return new UIRailClickableEntry(which.ToString(), label)
            {
                Count = QuestFacts.Count(which),
                Rise = 26f,
                Face = QuestFaces.Condensed,
                TextColor = on ? palette.Accent : (Color?) null,
                CountFace = QuestFaces.Mono,
                CountPoints = QuestFaces.Size.RailCount,
                CountColor = on ? palette.Accent : palette.TextDisabled
            };
        }

        // -------------------------------------------------------------------------------------------
        // Offers
        // -------------------------------------------------------------------------------------------

        private static float OfferList(Rect view, float y, UIColorPaletteDef palette)
        {
            QuestFacts.Offers(Offers, QuestFacts.Showing);

            if (Offers.Count == 0)
            {
                return TabParts.Line(view, y + 20f,
                    QuestFacts.Showing == QuestList.Dismissed
                        ? "Nothing has been set aside."
                        : "No quests are on offer. The storyteller will bring one along.",
                    palette.TextDisabled);
            }

            // Only the live list gets the deadline strip. On the dismissed list every pin would be for
            // something the player has already said they are not thinking about.
            if (QuestFacts.Showing == QuestList.Offers)
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
                ? palette.Accent
                : row.expires <= GenDate.TicksPerDay
                    ? palette.Danger
                    : row.expires <= GenDate.TicksPerDay * 3
                        ? palette.Warning
                        : palette.Border;

            // Inset by a pixel on all three sides, so the card's own outline stays unbroken around it. Drawn
            // flush it covered the left of the border, which made a selected card look like its highlight had
            // a bite out of it.
            Widgets.DrawBoxSolid(new Rect(box.x + 1f, box.y + 1f, 3f, box.height - 2f), stripe);

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
                ? "never expires"
                : "expires in " + QuestFacts.Period(row.expires);

            float whenWidth = UITextControl.Width(when, QuestFaces.Mono, QuestFaces.Size.When) + 8f;

            Aside(new Rect(band.xMax - 20f, band.y + 3f, 16f, 16f), row.quest, row.quest.dismissed, palette);

            band.width -= 26f;

            // An offer with no clock on it is not urgent and should not be dressed as though it were. It takes
            // the accent rather than a warning colour, which is the same thing the stripe does.
            Color whenColor = row.expires == int.MaxValue
                ? palette.Accent
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
                ? "No hurry. This offer does not lapse."
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
                    y = AlternativeLine(inner, y, row.choices[i], palette);
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

        /// <summary>
        /// One alternative on a list card, kept to a line.
        ///
        /// <b>The list is for comparing quests, not for choosing a reward.</b> A card that opened out into
        /// icons and an accept button for every alternative would be taller than the six cards around it and
        /// would put the decision in the place meant for the shortlist. The full treatment is one click away,
        /// in the detail view.
        /// </summary>
        private static float AlternativeLine(Rect inner, float y, ChoiceRow choice, UIColorPaletteDef palette)
        {
            float height = Mathf.Max(20f, UITextControl.LineHeight(QuestFaces.Body, QuestFaces.Size.Body) + 6f);

            Rect band = new Rect(inner.x + 76f, y, inner.width - 76f, height);

            UIElementPainter.OutlineRounded(band, palette.Border, palette.SurfaceSunken);

            TabParts.RowLabel(new Rect(band.x + 8f, band.y, band.width - 16f, height), Joined(choice.rewards),
                palette.TextPrimary, GameFont.Small, QuestFaces.Body, QuestFaces.Size.Body);

            return band.yMax + 2f;
        }

        /// <summary>Size of a reward's icon, and the gap between two of them.</summary>
        private const float IconSize = 30f;

        private const float IconGap = 3f;

        /// <summary>Width kept for the accept button at the right of a choice card.</summary>
        private const float TakeWidth = 110f;

        /// <summary>
        /// One alternative, as a card rather than a line.
        ///
        /// <b>A reward stack does not fit on a line and should not be asked to.</b> Transport pods with six
        /// kinds of thing in them produce a sentence that ellipses after the total value, which is the least
        /// useful part of it; what somebody choosing between three rewards wants is the list. So the text
        /// wraps, and the goods behind it are drawn as their own icons underneath.
        ///
        /// <b>Every icon opens the thing's info card.</b> A psylink neuroformer and a profane soul gem are
        /// both just names to somebody who has not used one, and the game already has a screen that explains
        /// any thing in it. Reaching that screen from the offer is the difference between choosing and
        /// guessing.
        /// </summary>
        private static float Alternative(Rect inner, float y, ChoiceRow choice, QuestPart_Choice part,
            QuestPart_Choice.Choice taken, Quest quest, AcceptanceReport can, UIColorPaletteDef palette)
        {
            string text = Full(choice.rewards);

            float textWidth = inner.width - TakeWidth - 24f;
            float textHeight = Wrapped(text, textWidth);

            // Counted before the card is sized, because the icons are the part that makes it tall and a card
            // measured without them clips its own contents.
            int icons = Icons(choice.rewards, null);

            int perRow = Mathf.Max(1, Mathf.FloorToInt(textWidth / (IconSize + IconGap)));
            int rows = icons == 0 ? 0 : Mathf.CeilToInt(icons / (float) perRow);

            float height = Mathf.Max(38f, textHeight + 12f + rows * (IconSize + IconGap));

            Rect card = new Rect(inner.x, y, inner.width, height);

            UIElementPainter.OutlineRounded(card, palette.Border, palette.SurfaceSunken);

            Rect body = new Rect(card.x + 10f, card.y + 6f, textWidth, card.height - 12f);

            Color previousColor = GUI.color;

            try
            {
                GUI.color = palette.TextPrimary;

                UITextControl.Paragraph(new Rect(body.x, body.y, body.width, textHeight), text,
                    QuestFaces.Body, QuestFaces.Size.Body);
            }
            finally
            {
                GUI.color = previousColor;
            }

            IconStrip(new Rect(body.x, body.y + textHeight + 4f, body.width, rows * (IconSize + IconGap)),
                choice.rewards, perRow, palette);

            Rect button = new Rect(card.xMax - TakeWidth - 8f, card.y + (card.height - 28f) * 0.5f,
                TakeWidth, 28f);

            if (TabParts.Button(button, "Take this", palette, can.Accepted, true,
                    can.Accepted ? null : can.Reason))
                QuestAccept.Begin(quest, () => part.Choose(taken));

            y = card.yMax + 4f;

            return Pawns(inner, y, choice.rewards, palette);
        }

        /// <summary>How tall a wrapped block of text will be at the panel's body font.</summary>
        /// <summary>
        /// How tall a wrapped block of quest prose is, in the face it will be drawn in.
        ///
        /// <b>Measured through the same face that draws it.</b> Sized against RimWorld's font and drawn in
        /// Barlow, a paragraph is either clipped at the bottom or trailed by a band of empty space, and which
        /// of the two depends on the words.
        /// </summary>
        private static float Wrapped(string text, float width)
        {
            return UIGuard.Try("Quests.Wrap",
                () => UITextControl.Height(text, QuestFaces.Body, QuestFaces.Size.Body, width), 22f, null);
        }

        /// <summary>
        /// Counts the things behind a set of rewards, and optionally collects them.
        ///
        /// One pass used twice, because the card has to know how many icons it will draw before it can decide
        /// how tall to be, and a second walk that disagreed with the first would clip the last row.
        /// </summary>
        private static int Icons(List<RewardRow> rewards, List<Thing> into)
        {
            int count = 0;

            for (int i = 0; i < rewards.Count; i++)
            {
                List<Thing> things = rewards[i].things;

                for (int t = 0; things != null && t < things.Count; t++)
                {
                    if (things[t] == null)
                        continue;

                    count++;

                    if (into != null)
                        into.Add(things[t]);
                }
            }

            return count;
        }

        private static readonly List<Thing> Shown = new List<Thing>();

        /// <summary>
        /// The goods, as icons that open their own info card.
        ///
        /// <b>Drawn from the real things rather than from their defs,</b> so a masterwork plasteel longsword
        /// shows the quality and the stuff it is actually made of. Those things exist already: the reward
        /// generated them when the quest did, and they are held until the reward is taken or dropped.
        /// </summary>
        private static void IconStrip(Rect rect, List<RewardRow> rewards, int perRow,
            UIColorPaletteDef palette)
        {
            Shown.Clear();

            if (Icons(rewards, Shown) == 0)
                return;

            for (int i = 0; i < Shown.Count; i++)
            {
                Thing thing = Shown[i];

                int column = i % perRow;
                int row = i / perRow;

                Rect slot = new Rect(rect.x + column * (IconSize + IconGap),
                    rect.y + row * (IconSize + IconGap), IconSize, IconSize);

                bool over = Mouse.IsOver(slot);

                UIElementPainter.OutlineRounded(slot, over ? palette.Accent : palette.Border,
                    palette.PanelBackground);

                UIGuard.Try("Quests.RewardIcon",
                    () => Widgets.ThingIcon(slot.ContractedBy(2f), thing), null);

                Count(slot, thing, palette);

                if (over)
                {
                    TooltipHandler.TipRegion(slot, (TipSignal) (UIGuard.Try("Quests.RewardLabel",
                        () => thing.LabelCap.ToString(), "?", null) + "\n\nClick for its details."));
                }

                if (!Widgets.ButtonInvisible(slot))
                    continue;

                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();

                UIGuard.Try("Quests.RewardCard",
                    () => Find.WindowStack.Add(new Dialog_InfoCard(thing)),
                    "That reward's details could not be opened. The offer is unaffected.");
            }
        }

        /// <summary>The stack count in the corner, for a reward that arrives as more than one.</summary>
        private static void Count(Rect slot, Thing thing, UIColorPaletteDef palette)
        {
            int stack = UIGuard.Try("Quests.RewardStack", () => thing.stackCount, 1, null);

            if (stack <= 1)
                return;

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.LowerRight;
                GUI.color = palette.TextPrimary;

                UITextControl.Label(new Rect(slot.x, slot.y, slot.width - 2f, slot.height - 1f),
                    stack.ToString(), QuestFaces.Mono, QuestFaces.Size.Chip);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }
        }

        /// <summary>
        /// A button per offered person, opening the read-only sheet.
        ///
        /// <b>One per pawn rather than a picker,</b> which is the rule the offer panel settled in 14167: an
        /// offer of several people exists to be compared, and a picker turns a comparison into a memory test.
        /// The click chooses which sheet is expanded, never which is reachable.
        ///
        /// <b>Not gated on the letters setting, which is a correction.</b> It was, on the reasoning that
        /// somebody who turned the panel off on letters had not asked for it on quests. That was wrong about
        /// what the setting is for: the letters panel appears unasked beside a dialog, and this is a button
        /// somebody presses. Turning off an interruption is not the same as refusing a door, and the version
        /// that gated it left a quest offering a joiner with no way at all to read him. Reported on
        /// 2026-08-30, on a quest offering a muffalo shaman named Clark.
        ///
        /// <b>A pawn the quest will not introduce still gets a row,</b> disabled and saying why. An absent
        /// control reads as a screen that forgot; a greyed one that explains itself reads as the quest keeping
        /// something back, which is what has actually happened.
        /// </summary>
        private static float Pawns(Rect inner, float y, List<RewardRow> rewards, UIColorPaletteDef palette)
        {
            for (int i = 0; i < rewards.Count; i++)
            {
                RewardRow reward = rewards[i];

                if (reward.pawn == null && !reward.pawnHidden)
                    continue;

                bool known = reward.pawn != null;

                string label = known
                    ? "Read " + UIGuard.Try("Quests.PawnName",
                        () => reward.pawn.LabelShortCap.ToString(), "them", null) + "..."
                    : "Details withheld";

                float width = TabParts.ButtonWidth(label);
                Rect button = new Rect(inner.x + 76f, y, width, 26f);

                if (TabParts.Button(button, label, palette, known, false,
                        known
                            ? "Open this person's skills, traits, backstory and health, exactly as they will "
                              + "read once they have joined. Nothing here can be changed."
                            : "This quest does not say who they are. Nothing about them is readable until "
                              + "they arrive.")
                    && known)
                    QuestPawnSheet.Open(reward.pawn);

                y = button.yMax + 3f;
            }

            return y;
        }

        /// <summary>Every reward in a set, folded onto one line, for a card row that has one.</summary>
        private static string Joined(List<RewardRow> rewards)
        {
            string joined = null;

            for (int i = 0; i < rewards.Count; i++)
            {
                string flat = QuestFacts.Flat(rewards[i].text);

                if (flat.NullOrEmpty())
                    continue;

                joined = joined == null ? flat : joined + ", " + flat;
            }

            return joined ?? "Nothing";
        }

        /// <summary>
        /// The same set with its line breaks kept, for the detail view, which has room to be a list.
        ///
        /// <b>Separated by a blank line rather than a comma.</b> Each reward is already a heading and its
        /// own list of things; running two of those together with a comma would put the second heading at the
        /// end of the first list.
        /// </summary>
        private static string Full(List<RewardRow> rewards)
        {
            string joined = null;

            for (int i = 0; i < rewards.Count; i++)
            {
                if (rewards[i].text.NullOrEmpty())
                    continue;

                joined = joined == null
                    ? rewards[i].text
                    : joined + Environment.NewLine + Environment.NewLine + rewards[i].text;
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

        /// <summary>
        /// One running quest: what it is waiting on, where it is, who it is holding, and a way in.
        ///
        /// <b>Two earlier versions of this row said nothing.</b> The first put "Holding nobody" under every
        /// name, because most quests hold no colonist. The second put "Running to no deadline" under every
        /// name, because most quests have no clock. A line that is the same on every row is not information,
        /// however true it is.
        ///
        /// <b>So the row leads with whichever fact this quest actually has.</b> A clock if it is running
        /// against one, otherwise where it is pointing and how far that is, and only then how long ago it was
        /// accepted. The place is the useful answer for a site quest, which is most of what a colony carries:
        /// it is not waiting on time, it is waiting on somebody walking there.
        /// </summary>
        private static float ActiveOne(Rect view, float y, ActiveRow row, UIColorPaletteDef palette)
        {
            QuestFacts.Deadlines(row.quest, Clocks);

            string clock = Clocks.Count > 0 && !Clocks[0].text.NullOrEmpty()
                ? Clocks[0].text
                : row.ends != int.MaxValue
                    ? "Ends in " + QuestFacts.Period(row.ends)
                    : null;

            string held = null;

            for (int i = 0; i < row.reserved.Count; i++)
            {
                string name = UIGuard.Try("Quests.Held",
                    () => row.reserved[i].LabelShortCap.ToString(), null, null);

                if (name.NullOrEmpty())
                    continue;

                held = held == null ? name : held + ", " + name;
            }

            string lead = clock ?? row.where
                ?? "Accepted " + QuestFacts.Period(
                    UIGuard.Try("Quests.Since", () => row.quest.TicksSinceAccepted, 0, null)) + " ago";

            bool place = clock == null && !row.where.NullOrEmpty();

            float height = 48f;

            if (held != null)
                height += 20f;

            if (clock != null && !row.where.NullOrEmpty())
                height += 20f;

            Rect box = new Rect(view.x, y, view.width, height);
            bool over = Mouse.IsOver(box);

            UIElementPainter.OutlineRounded(box, over ? palette.Accent : palette.Border,
                palette.PanelBackground);

            Color stripe = row.ends == int.MaxValue
                ? palette.Border
                : row.ends <= GenDate.TicksPerDay
                    ? palette.Danger
                    : row.ends <= GenDate.TicksPerDay * 3
                        ? palette.Warning
                        : palette.Accent;

            // Inset a pixel so the card's outline stays unbroken around it.
            Widgets.DrawBoxSolid(new Rect(box.x + 1f, box.y + 1f, 3f, box.height - 2f), stripe);

            Rect inner = new Rect(box.x + 12f, box.y + 6f, box.width - 24f, box.height - 12f);

            float right = inner.xMax;

            // The jump control is laid out from the right before the name, so a long quest name gives way to
            // it rather than pushing it off the card.
            Aside(new Rect(inner.xMax - 18f, inner.y + 3f, 16f, 16f), row.quest, row.quest.dismissed, palette);

            right = inner.xMax - 26f;

            bool jumpable = row.target.IsValid && UIGuard.Try("Quests.CanJump",
                () => CameraJumper.CanJump(row.target), false, null);

            if (jumpable)
            {
                Rect jump = new Rect(right - 74f, inner.y, 74f, 24f);

                if (TabParts.Button(jump, "Show me", palette, true, false,
                        "Move the camera to " + row.where + "."))
                {
                    UIGuard.Try("Quests.Jump", () => CameraJumper.TryJumpAndSelect(row.target),
                        "The camera could not be moved to that quest's target.");
                }

                right = jump.x - 8f;
            }

            if (!row.factions.NullOrEmpty())
            {
                float width = Mathf.Min(inner.width * 0.34f,
                    UITextControl.Width(row.factions, QuestFaces.Mono, QuestFaces.Size.Small) + 6f);

                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;

                try
                {
                    Text.Anchor = TextAnchor.MiddleRight;
                    GUI.color = palette.TextSecondary;

                    UITextControl.LabelEllipses(new Rect(right - width, inner.y, width, 22f),
                        row.factions, QuestFaces.Mono, QuestFaces.Size.Small);
                }
                finally
                {
                    GUI.color = previousColor;
                    Text.Anchor = previousAnchor;
                }

                right -= width + 8f;
            }

            TabParts.RowLabel(new Rect(inner.x, inner.y, Mathf.Max(60f, right - inner.x), 22f), row.name,
                palette.TextPrimary, GameFont.Small, QuestFaces.Condensed, QuestFaces.Size.Name);

            float cursor = inner.y + 22f;

            Color tint = clock == null
                ? place ? palette.TextSecondary : palette.TextDisabled
                : row.ends <= GenDate.TicksPerDay * 3
                    ? palette.Warning
                    : palette.TextSecondary;

            TabParts.RowLabel(new Rect(inner.x, cursor, inner.width, 20f), lead, tint, GameFont.Small,
                QuestFaces.Body, QuestFaces.Size.Body);

            cursor += 20f;

            // A quest with both a clock and a place gets both lines: the clock is what it is running against
            // and the place is where you go to do something about it.
            if (clock != null && !row.where.NullOrEmpty())
            {
                TabParts.RowLabel(new Rect(inner.x, cursor, inner.width, 20f), row.where, palette.TextDisabled,
                    GameFont.Small, QuestFaces.Body, QuestFaces.Size.Body);

                cursor += 20f;
            }

            if (held != null)
            {
                TabParts.RowLabel(new Rect(inner.x, cursor, 70f, 20f), QuestFaces.Caps("Holding"),
                    palette.TextDisabled, GameFont.Tiny, QuestFaces.Mono, QuestFaces.Size.Label);

                TabParts.RowLabel(new Rect(inner.x + 76f, cursor, inner.width - 76f, 20f), held,
                    palette.TextPrimary, GameFont.Small, QuestFaces.Body, QuestFaces.Size.Body);
            }

            if (Widgets.ButtonInvisible(box))
            {
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();

                QuestFacts.Selected = row.quest;
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

        /// <summary>
        /// The mark on a bar that runs off the end of the chart.
        ///
        /// <b>Pulsing rather than static,</b> because a clamped bar and a bar that genuinely ends at the edge
        /// of the axis are the same picture, and the difference between them is exactly what a clamped chart
        /// has to say out loud. Movement is the one channel nothing else on this panel is using.
        ///
        /// Through <c>Pulser</c>, which is RimWorld's own, so it keeps time with every other pulsing thing on
        /// screen instead of beating against them. Slow and shallow: this is a footnote, not an alert.
        /// </summary>
        private static void Beyond(Rect bar, Color tint)
        {
            const float width = 22f;

            Rect mark = new Rect(bar.xMax - width - 2f, bar.y, width, bar.height);

            Color previous = GUI.color;

            try
            {
                GUI.color = tint * UIGuard.Try("Quests.Pulse",
                    () => Pulser.PulseBrightness(0.6f, 0.45f), 1f, null);

                UITextControl.Label(mark, ">>>", QuestFaces.Mono, QuestFaces.Size.Small);
            }
            finally
            {
                GUI.color = previous;
            }
        }

        /// <summary>
        /// The set-aside control, drawn in RimWorld's own dismiss and restore icons.
        ///
        /// <b>The game's textures rather than a glyph of ours,</b> because a player who has used vanilla's
        /// quest tab already knows what these two marks mean, and a control that means "hide this" needs to be
        /// unmistakable from the one on the history rows that means "delete this for ever".
        ///
        /// <b>Loaded on first use and remembered.</b> ContentFinder walks the mod list, which is not something
        /// to do sixty times a second per row.
        /// </summary>
        private static void Aside(Rect rect, Quest quest, bool dismissed, UIColorPaletteDef palette)
        {
            Texture2D icon = dismissed ? Restore : Dismiss;

            if (icon == null)
                return;

            Color previous = GUI.color;

            try
            {
                GUI.color = Mouse.IsOver(rect) ? palette.Accent : palette.TextSecondary;

                GUI.DrawTexture(rect, icon);
            }
            finally
            {
                GUI.color = previous;
            }

            TooltipHandler.TipRegion(rect, (TipSignal) (dismissed
                ? "Put this back on the list."
                : "Set this aside. It carries on and its clock keeps running; it just stops taking up a row "
                  + "here. You can put it back from the Dismissed list."));

            if (Widgets.ButtonInvisible(rect))
                QuestAccept.SetAside(quest, !dismissed);
        }

        /// <summary>
        /// RimWorld's own dismiss and restore marks.
        ///
        /// <b>Loaded in a static constructor under <c>StaticConstructorOnStartup</c>,</b> which is the
        /// arrangement the game checks for: it warns about any type holding a static texture field without
        /// that attribute, because assets have to be loaded on the main thread and it cannot know when a
        /// lazily loaded one will be touched. This type used to load them on first draw, which worked and
        /// still drew the warning, since the check reads the field's type rather than watching what happens
        /// to it.
        ///
        /// <b>The game's textures rather than glyphs of ours,</b> because a player who has used vanilla's
        /// quest tab already knows what these two marks mean, and a control meaning "hide this" needs to be
        /// unmistakable from the one on the history rows meaning "delete this for ever".
        /// </summary>
        private static readonly Texture2D Dismiss;

        private static readonly Texture2D Restore;

        /// <summary>
        /// The tab's own glyph, drawn beside the title the way the power header draws its bolt.
        ///
        /// <b>The same texture the button on the bar uses,</b> so the mark a player clicked to get here is the
        /// mark waiting at the top of the screen.
        ///
        /// <b>Tinted to the accent rather than to a second colour of its own.</b> The power header takes the
        /// palette's amber because electricity reads amber; a chest has no such association, and giving this
        /// one amber too would make two tabs that are nothing alike look like each other in the corner of the
        /// eye. Matching the title is the quieter and more useful choice.
        /// </summary>
        private static readonly Texture2D Glyph;

        static QuestPanel()
        {
            // Through locals, because a readonly field can only be assigned in the constructor itself and
            // the guard runs its work in a closure. Guarded all the same: a missing texture should cost the
            // control its picture, not take the type down before anything has drawn.
            Texture2D dismiss = null;
            Texture2D restore = null;
            Texture2D glyph = null;

            UIGuard.Try("Quests.Icons", () =>
            {
                dismiss = ContentFinder<Texture2D>.Get("UI/Buttons/Dismiss", false);
                restore = ContentFinder<Texture2D>.Get("UI/Buttons/UnDismiss", false);
                glyph = ContentFinder<Texture2D>.Get("UI/MainButtonIcons/Quests", false);
            }, "The set-aside control has no icon this session. It still works.");

            Dismiss = dismiss;
            Restore = restore;
            Glyph = glyph;
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
                    // Clamped, because the axis is capped at twenty days and a quest can run past it. Left
                    // unclamped the bar drew straight out of the panel and over the scroll bar, which read as a
                    // layout fault rather than as a long quest.
                    bool beyond = Clocks[c].ticks > span;
                    float width = Mathf.Clamp(track.width * (Clocks[c].ticks / span), 3f, track.width);

                    Color tint = Clocks[c].ticks <= GenDate.TicksPerDay
                        ? palette.Danger
                        : Clocks[c].ticks <= GenDate.TicksPerDay * 3
                            ? palette.Warning
                            : palette.Accent;

                    Rect bar = new Rect(track.x, track.y, width, track.height);

                    UIElementPainter.OutlineRounded(bar, tint,
                        UIElementPainter.Composite(palette.SurfaceSunken,
                            new Color(tint.r, tint.g, tint.b, 0.22f)));

                    if (beyond)
                        Beyond(bar, tint);

                    if (Mouse.IsOver(bar) && !Clocks[c].text.NullOrEmpty())
                    {
                        TooltipHandler.TipRegion(bar, (TipSignal) (row.name + "\n\n" + Clocks[c].text
                            + (beyond ? "\n\nThis one runs past the end of the chart." : string.Empty)));
                    }
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

        /// <summary>Width of the outcome chip's column, so the chips line up rather than tracking their text.</summary>
        private const float OutcomeColumn = 120f;

        /// <summary>Width of the ended column.</summary>
        private const float EndedColumn = 96f;

        /// <summary>Width of the per-row remove control.</summary>
        private const float BinColumn = 26f;

        /// <summary>
        /// The history, as a table with headed columns.
        ///
        /// <b>Headed, because three ragged columns are not a table.</b> Without headings the outcome chip and
        /// the date read as decoration hanging off each name; with them the eye has something to run down, which
        /// is the entire reason to list forty finished quests rather than summarise them.
        /// </summary>
        private static float HistoryList(Rect view, float y, UIColorPaletteDef palette)
        {
            QuestFacts.History(Finished);

            if (Finished.Count == 0)
                return TabParts.Line(view, y + 20f, "Nothing has finished yet.", palette.TextDisabled);

            y = Sweep(view, y, palette);

            float top = y;
            float capHeight = 24f;

            Rect cap = new Rect(view.x, y, view.width, capHeight);

            UIElementPainter.FillRounded(cap,
                UIElementPainter.Composite(palette.PanelBackground, palette.HoverOverlay));

            Color previousLine = GUI.color;

            GUI.color = palette.Border;
            Widgets.DrawLineHorizontal(cap.x, cap.yMax, cap.width);
            GUI.color = previousLine;

            TabParts.RowLabel(new Rect(cap.x + 10f, cap.y, cap.width - 20f, capHeight),
                QuestFaces.Caps("Finished"), palette.TextSecondary, GameFont.Tiny, QuestFaces.Mono,
                QuestFaces.Size.BlockHead);

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextDisabled;

                UITextControl.LabelEllipses(new Rect(cap.x + 10f, cap.y, cap.width - 20f, capHeight),
                    QuestFaces.Caps("Most recent first"), QuestFaces.Mono, QuestFaces.Size.BlockHead);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }

            y = cap.yMax;

            y = HistoryHead(view, y, palette);

            for (int i = 0; i < Finished.Count; i++)
                y = HistoryOne(view, y, Finished[i], palette);

            Color previousFrame = GUI.color;

            GUI.color = palette.Border;
            Widgets.DrawBox(new Rect(view.x, top, view.width, y - top));
            GUI.color = previousFrame;

            return y + RowGap;
        }

        /// <summary>
        /// The two controls that clear out finished quests.
        ///
        /// <b>Both say what they will leave behind before you press them.</b> A sweep that silently does less
        /// than its label promises is worse than one that refuses, because the label is the only thing telling
        /// you whether the save was really cleaned.
        ///
        /// <b>Both are confirmed, because neither can be undone.</b> Removing a quest drops it from the save;
        /// there is no bin to fish it back out of.
        /// </summary>
        private static float Sweep(Rect view, float y, UIColorPaletteDef palette)
        {
            int removable;
            int chained;

            QuestHistory.Sweepable(out removable, out chained);

            Rect band = new Rect(view.x, y, view.width, 30f);
            Rect all = new Rect(band.x, band.y, 130f, 28f);

            string tip = removable == 0
                ? "Nothing here can be removed."
                : "Removes " + removable + " finished "
                  + (removable == 1 ? "quest" : "quests") + " from the save for good."
                  + Kept(chained);

            if (TabParts.Button(all, "Remove all", palette, removable > 0, false, tip))
            {
                int count = removable;

                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Remove " + count + " finished " + (count == 1 ? "quest" : "quests")
                    + " from this save?\n\nThis cannot be undone." + Kept(chained),
                    () => QuestHistory.Sweep(), true));
            }

            Rect group = new Rect(all.xMax + 8f, band.y, 150f, 28f);

            if (TabParts.Button(group, "Remove group...", palette, removable > 0, false,
                    "Remove the finished quests of one outcome: completed, failed or expired."))
                GroupMenu();

            string note = chained > 0
                ? chained + (chained == 1 ? " quest is" : " quests are") + " chained and will be kept"
                : removable > 0 ? "All of these can be removed" : null;

            if (!note.NullOrEmpty())
            {
                TabParts.RowLabel(new Rect(group.xMax + 12f, band.y, band.width - group.width - all.width - 28f,
                        28f),
                    note, palette.TextDisabled, GameFont.Tiny, QuestFaces.Body, QuestFaces.Size.Small);
            }

            return band.yMax + RowGap;
        }

        /// <summary>The tail both tooltips share, naming what a sweep will not touch.</summary>
        private static string Kept(int chained)
        {
            if (chained <= 0)
                return string.Empty;

            return "\n\n" + chained + (chained == 1 ? " quest is" : " quests are")
                   + " part of a chain and will be kept. Removing one end of a chain would leave the other "
                   + "pointing at nothing.";
        }

        /// <summary>
        /// The outcome picker.
        ///
        /// <b>Every group is listed, including the empty ones, and the empty ones are disabled.</b> A menu that
        /// hides what it has none of makes the reader wonder whether they misremembered the option; one that
        /// greys it out answers the question in place.
        /// </summary>
        private static void GroupMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            for (int i = 0; i < QuestHistory.Groups.Length; i++)
            {
                QuestState group = QuestHistory.Groups[i];
                int count = QuestHistory.CountOf(group);
                string label = QuestHistory.GroupLabel(group) + "  -  " + count;

                if (count == 0)
                {
                    options.Add(new FloatMenuOption(label + " (none)", null));

                    continue;
                }

                QuestState local = group;
                string name = QuestHistory.GroupLabel(group);

                options.Add(new FloatMenuOption(label, () =>
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "Remove " + count + " " + name + " " + (count == 1 ? "quest" : "quests")
                        + " from this save?\n\nThis cannot be undone.",
                        () => QuestHistory.SweepOf(local), true))));
            }

            if (options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>The column headings, in the same small caps the block heading uses.</summary>
        private static float HistoryHead(Rect view, float y, UIColorPaletteDef palette)
        {
            const float height = 22f;

            Rect band = new Rect(view.x + 10f, y, view.width - 20f, height);

            float ended = band.xMax - BinColumn - EndedColumn;
            float outcome = ended - OutcomeColumn;

            TabParts.RowLabel(new Rect(band.x, band.y, outcome - band.x - 8f, height), QuestFaces.Caps("Quest"),
                palette.TextDisabled, GameFont.Tiny, QuestFaces.Mono, QuestFaces.Size.Label);

            TabParts.RowLabel(new Rect(outcome, band.y, OutcomeColumn, height), QuestFaces.Caps("Outcome"),
                palette.TextDisabled, GameFont.Tiny, QuestFaces.Mono, QuestFaces.Size.Label);

            TabParts.RowLabel(new Rect(ended, band.y, EndedColumn, height), QuestFaces.Caps("Ended"),
                palette.TextDisabled, GameFont.Tiny, QuestFaces.Mono, QuestFaces.Size.Label);

            Color previous = GUI.color;

            GUI.color = palette.Border;
            Widgets.DrawLineHorizontal(band.x, band.yMax, band.width);
            GUI.color = previous;

            return band.yMax + 2f;
        }

        private static float HistoryOne(Rect view, float y, HistoryRow row, UIColorPaletteDef palette)
        {
            const float height = 26f;

            Rect band = new Rect(view.x + 10f, y, view.width - 20f, height);

            if (Mouse.IsOver(band))
                UIElementPainter.FillRounded(band, palette.HoverOverlay);

            Color tint = row.state == QuestState.EndedSuccess
                ? palette.Success
                : row.state == QuestState.EndedFailed
                    ? palette.Danger
                    : palette.TextDisabled;

            float ended = band.xMax - BinColumn - EndedColumn;
            float outcome = ended - OutcomeColumn;

            TabParts.RowLabel(new Rect(band.x, band.y, outcome - band.x - 8f, height), row.name,
                palette.TextPrimary, GameFont.Small, QuestFaces.Condensed, QuestFaces.Size.Name);

            // Placed at the column's left edge rather than sized to the row, so a column of chips has one edge
            // to read down instead of a ragged one that tracks the length of each word.
            float chip = TabParts.PillWidth(QuestFaces.Caps(row.outcome), OutcomeColumn - 8f, QuestFaces.Mono,
                QuestFaces.Size.Chip);

            TabParts.Pill(band, outcome, band.y + 3f, QuestFaces.Caps(row.outcome), tint, palette, chip, null,
                QuestFaces.Mono, QuestFaces.Size.Chip);

            TabParts.RowLabel(new Rect(ended, band.y, EndedColumn, height), Ended(row.ago),
                palette.TextDisabled, GameFont.Tiny, QuestFaces.Mono, QuestFaces.Size.Small);

            Bin(new Rect(band.xMax - BinColumn + 4f, band.y + 4f, 18f, 18f), row.quest, palette);

            return band.yMax + 1f;
        }

        /// <summary>
        /// The colony day a quest ended on.
        ///
        /// <b>A date rather than an age.</b> "58 days ago" is a number you have to subtract from today to place
        /// against anything else you remember; a day number is the same figure the rest of the game speaks in.
        /// </summary>
        private static string Ended(int ago)
        {
            return UIGuard.Try("Quests.EndedDay", () =>
            {
                int day = GenDate.DaysPassed - Mathf.FloorToInt(ago / (float) GenDate.TicksPerDay);

                return "day " + Mathf.Max(0, day);
            }, "--", null);
        }

        /// <summary>
        /// The per-row remove control.
        ///
        /// <b>Disabled rather than absent on a chained quest,</b> with the reason on it. An absent control on
        /// some rows and not others reads as a rendering fault; a greyed one that explains itself reads as a
        /// rule.
        /// </summary>
        private static void Bin(Rect rect, Quest quest, UIColorPaletteDef palette)
        {
            bool can = QuestHistory.Removable(quest);
            string blocked = QuestHistory.Blocked(quest);

            Color previous = GUI.color;

            try
            {
                GUI.color = !can
                    ? palette.TextDisabled
                    : Mouse.IsOver(rect)
                        ? palette.Danger
                        : palette.TextSecondary;

                Text.Anchor = TextAnchor.MiddleCenter;

                UITextControl.Label(rect, "x", QuestFaces.Mono, QuestFaces.Size.Body);

                Text.Anchor = TextAnchor.UpperLeft;
            }
            finally
            {
                GUI.color = previous;
            }

            TooltipHandler.TipRegion(rect, (TipSignal) (blocked
                ?? "Remove this quest from the save for good. This cannot be undone."));

            if (!can || !Widgets.ButtonInvisible(rect))
                return;

            QuestHistory.Remove(quest);
        }
    }
}
