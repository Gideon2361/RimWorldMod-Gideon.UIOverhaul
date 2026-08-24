using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>What a click on a node asked for.</summary>
    internal enum NodeClick
    {
        None,

        /// <summary>Make this the project the detail panel is about.</summary>
        Select,

        /// <summary>The corner affordance: put it in the queue, or take it out again.</summary>
        ToggleQueue
    }

    /// <summary>
    /// One project as it is drawn on the canvas.
    ///
    /// <b>Every node is the same size, and that is a rule rather than a convenience.</b> A node's height cannot
    /// depend on anything that changes, because the layout is computed from it: a techprint arriving and reflowing
    /// the whole graph would move every node out from under the pointer. So the name gets two lines whether or not
    /// it needs them.
    ///
    /// <b>Three lines and nothing else, which is the second version of this.</b> The first reserved a whole chip
    /// band on every node for a line of text -- and since a missing prerequisite is by far the commonest state,
    /// that band was almost always a truncated "Needs Intermedi..." saying what the incoming arrow already said.
    /// It cost seventeen pixels a node, which is the difference between ten projects on screen and thirty. The
    /// five states worth flagging now get one small picture on the figures line.
    ///
    /// <b>The stripe carries the state and the border carries the selection.</b> Two channels, so a selected
    /// blocked project still says it is blocked. Vanilla has one and spends it on the selection.
    /// </summary>
    internal static class ResearchNodeArt
    {
        private const float StripeWidth = 3f;

        private const float PadLeft = 8f;

        private const float PadRight = 5f;

        private const float PadTop = 3f;

        private const float ProgressHeight = 3f;

        /// <summary>How strong the green wash on a finished card is. Matches the work panel's own marking.</summary>
        private const float DoneWashAlpha = 0.14f;

        /// <summary>How strong the fill across the card of the project being worked on is.</summary>
        private const float ProgressFillAlpha = 0.16f;

        /// <summary>The round affordance in the top right: the queue number, or the button that puts one there.</summary>
        private const float BadgeSize = 15f;

        /// <summary>
        /// How tall every node is.
        ///
        /// Measured from the fonts rather than written down, because the font is a setting: a player who turns
        /// tiny text off gets taller lines, and a literal that suited the default would clip the bottom line off
        /// every node on the canvas.
        /// </summary>
        internal static float NodeHeight
        {
            get
            {
                // The name is Small and the state row is Tiny, from 2026-08-23: Aaron asked for the name not to
                // be Tiny, and it is the one string on the card somebody actually reads rather than scans.
                float line = UIFonts.LineHeightOf(GameFont.Small) + UIFonts.LineHeightOf(GameFont.Tiny);

                // Two rows, not three, from 2026-08-23: the name on one line and the state beside the cost on
                // the next. It was three because the name wrapped, and a name allowed two lines takes two
                // whether it needs them or not -- so every node in the game was as tall as the longest name in
                // it. Ellipsing the name instead makes the card a fixed two rows and the whole canvas a third
                // shorter. The width went up to pay for it; see ResearchGraph.NodeWidth.
                return Mathf.Ceil(PadTop + line + 1f + ProgressHeight + PadTop);
            }
        }

        /// <summary>
        /// Draws one node and reports what was pressed.
        ///
        /// <paramref name="rect"/> is in screen space: the caller has already applied the scroll.
        /// </summary>
        internal static NodeClick Draw(Rect rect, ResearchNode node, UIColorPaletteDef palette, bool selected,
            int queuePlace, bool dimmed)
        {
            ResearchState state = ResearchFacts.StateOf(node);
            ResearchProjectDef project = node.Project;

            bool over = Mouse.IsOver(rect);
            bool actionable = ResearchActions.Actionable(node);
            Color accent = ResearchFacts.ColorFor(state, palette, project.knowledgeCategory);

            Body(rect, project, palette, state, selected, over, dimmed);

            // The left edge carries the theme, not the state, from 2026-08-23 on Aaron's instruction. It was the
            // state, and the argument for that was that the stripe and the border were two channels -- but the
            // state is now spelled out in words on the second row of every node, which is a better channel than
            // a colour nobody has a key for. The theme has no other home on the node at all.
            //
            // Read from the taxonomy rather than from the group, so the colour means the same thing under all
            // three groupings instead of vanishing whenever the blocks are cut some other way.
            Stripe(rect, ResearchBands.ColorFor(ResearchTaxonomy.BandOf(project), palette), state, dimmed);

            float x = rect.x + PadLeft;
            float width = rect.width - PadLeft - PadRight;

            // Two fonts, two line heights. The name reads at Small and the state row scans at Tiny, so a single
            // "line" local would have put one of them in the other's space.
            float nameLine = UIFonts.LineHeightOf(GameFont.Small);
            float line = UIFonts.LineHeightOf(GameFont.Tiny);

            Color ink = Ink(palette, state, dimmed);

            // The name gives up its top right corner whenever the badge is there, so a queue number never lands
            // on top of a letter.
            float reserved = queuePlace > 0 || (over && actionable) ? BadgeSize - 4f : 0f;
            Rect nameBand = new Rect(x, rect.y + PadTop, width - reserved, nameLine);

            if (state == ResearchState.Unknown)
                ResearchMask.Draw(nameBand, ResearchMask.Key(project, "name"), palette.Mood, GameFont.Small);
            else
                Name(nameBand, project.LabelCap.ToString(), ink);

            Figures(new Rect(x, rect.y + PadTop + nameLine, width, line), node, project, state, palette, dimmed);

            Progress(new Rect(x, rect.yMax - PadTop - ProgressHeight, width, ProgressHeight), project, palette,
                accent, state);

            string tooltip = ResearchFacts.TooltipFor(node, state);

            if (over && !tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, (TipSignal) tooltip);

            return Press(rect, over, actionable, queuePlace, palette);
        }

        /// <summary>
        /// The figures line: what it costs and how long it will take, and what is standing in the way.
        ///
        /// <b>The tech level used to be here and is gone.</b> "Neolithic 5000" was on every node in the game, and
        /// neither half earned its space: the level is a filter at the top of the screen and is in the tooltip,
        /// and a bare point total answers nothing. Days at the colony's own rate answers the question somebody
        /// actually has.
        /// </summary>
        /// <summary>
        /// The node's second row: what to do about it on the left, what it costs on the right.
        ///
        /// <b>Rebuilt on 2026-08-23 to the mockup's shape.</b> It used to be the cost on the left with an icon, a
        /// short mark and a tick collected on the right, and three of those four said the same thing in different
        /// alphabets: a green tick, a success-coloured icon and a mark all meaning finished. Worse, the states a
        /// player can act on -- ready, researching, finished -- printed nothing at all, on the reasoning that a
        /// node you can start needs no caption. Aaron asked for the words back and he was right. A caption slot
        /// that is blank on some nodes and full on others reads as missing information, and a column of nodes
        /// cannot be scanned for Available if Available is blank.
        ///
        /// So: the state in words, in the state's colour, on the left. The cost, muted, on the right. Nothing
        /// else. The icon and the tick are gone -- the word does their job and does it in one alphabet.
        ///
        /// <b>The state gets the room and the cost gets what is left.</b> The cost is four digits and a day count
        /// at its longest and never needs ellipsing; the state is anything from "Done" to "Needs microelectronics"
        /// and always does. Measuring the cost and giving the state the remainder is the only division of a
        /// hundred and ninety pixels that cannot clip the number.
        /// </summary>
        private static void Figures(Rect band, ResearchNode node, ResearchProjectDef project,
            ResearchState state, UIColorPaletteDef palette, bool dimmed)
        {
            Color flag = dimmed
                ? palette.TextDisabled
                : ResearchFacts.ColorFor(state, palette, project.knowledgeCategory);

            // The cost is masked on an undiscovered node for the same reason its name is: knowing a project is
            // eight thousand points is knowing something about it.
            if (state == ResearchState.Unknown)
            {
                // A third, not a half. The masked cost is a run of glyphs that means nothing, and "Not yet
                // understood" is the one readable thing on the card -- so the readable half gets the room.
                float half = band.width * 0.32f;

                ResearchMask.Draw(new Rect(band.xMax - half, band.y, half, band.height),
                    ResearchMask.Key(project, "cost"), palette.TextDisabled);

                TabParts.RowLabel(new Rect(band.x, band.y, Mathf.Max(0f, band.width - half - 4f), band.height),
                    ResearchFacts.ChipFor(node, state), flag, GameFont.Tiny);

                return;
            }

            string cost = Cost(project);

            // Measured with CalcSize and not UIRichText.WidthOf, which adds the thirteen pixel ellipsis reserve.
            // The cost is right-aligned, four digits at its longest and never ellipsed, so that reserve was
            // thirteen pixels taken from the state caption beside it -- and the caption is the half that gets cut
            // off. "Needs Study of necromancy" came out as "Needs Study o..." for exactly this.
            float costWidth = cost.NullOrEmpty() ? 0f : Measure(cost);

            if (costWidth > 0f)
            {
                GameFont font = Text.Font;
                TextAnchor anchor = Text.Anchor;
                Color previous = GUI.color;

                try
                {
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleRight;
                    GUI.color = palette.TextDisabled;

                    Widgets.Label(new Rect(band.xMax - costWidth, band.y, costWidth, band.height), cost);
                }
                finally
                {
                    GUI.color = previous;
                    Text.Anchor = anchor;
                    Text.Font = font;
                }
            }

            string chip = ResearchFacts.ChipFor(node, state);

            if (chip.NullOrEmpty())
                return;

            // Bold for the three states that are about what you can do, plain for the ones explaining why you
            // cannot. Available and Done are the words somebody scans a column for, and they are short enough
            // that bold costs them nothing; "Needs intermediate necromancy" is a sentence to read once and it
            // needs the room more than it needs the weight.
            // Bold for the three states that are about what you can do, plain for the ones explaining why you
            // cannot. Available and Done are the words somebody scans a column for, and they are short enough
            // that bold costs them nothing; "Needs intermediate necromancy" is a sentence to read once and it
            // needs the room more than the weight.
            if (state == ResearchState.Ready || state == ResearchState.Finished
                                             || state == ResearchState.Researching)
                chip = "<b>" + chip + "</b>";

            TabParts.RowLabel(new Rect(band.x, band.y, Mathf.Max(0f, band.width - costWidth - 6f), band.height),
                chip, flag, GameFont.Tiny);
        }

        /// <summary>
        /// The corner affordance and the click handling, which are one thing because they overlap.
        ///
        /// <b>The queue number and the add button live in the same fifteen pixels.</b> A queued node shows its
        /// place; hovering it shows a cross instead, because the number is also how you take it out. An unqueued
        /// node shows nothing until the pointer is on it. That keeps a canvas of three hundred nodes free of three
        /// hundred plus signs.
        /// </summary>
        private static NodeClick Press(Rect rect, bool over, bool actionable, int queuePlace,
            UIColorPaletteDef palette)
        {
            Rect badge = new Rect(rect.xMax - BadgeSize - 2f, rect.y + 2f, BadgeSize, BadgeSize);
            bool onBadge = over && Mouse.IsOver(badge);

            if (queuePlace > 0)
            {
                UIElementPainter.FillRounded(badge, palette.Accent);

                if (onBadge && ResearchGlyphs.Cross != null)
                    Icon(badge, ResearchGlyphs.Cross, palette.WindowBackground);
                else
                    Number(badge, queuePlace, palette.WindowBackground);
            }
            else if (over && actionable)
            {
                UIElementPainter.OutlineRounded(badge, palette.Accent,
                    onBadge ? palette.Accent : palette.PanelBackground);

                if (ResearchGlyphs.Plus != null)
                    Icon(badge, ResearchGlyphs.Plus, onBadge ? palette.WindowBackground : palette.Accent);
            }

            if (onBadge && (queuePlace > 0 || actionable))
            {
                TooltipHandler.TipRegion(badge,
                    (TipSignal) (queuePlace > 0 ? "Take out of the queue" : "Add to the queue"));

                if (Widgets.ButtonInvisible(badge))
                    return NodeClick.ToggleQueue;

                // Consumed, so a press on the badge that did not register as a click cannot fall through and
                // select the node underneath it.
                return NodeClick.None;
            }

            return Widgets.ButtonInvisible(rect) ? NodeClick.Select : NodeClick.None;
        }

        private static void Icon(Rect badge, Texture2D icon, Color color)
        {
            Color previous = GUI.color;

            GUI.color = color;
            GUI.DrawTexture(badge.ContractedBy(3f), icon);
            GUI.color = previous;
        }

        private static void Number(Rect badge, int place, Color color)
        {
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color previous = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = color;

                Widgets.Label(badge, place.ToString());
            }
            finally
            {
                GUI.color = previous;
                Text.Anchor = anchor;
                Text.Font = font;
            }
        }

        /// <summary>
        /// The node's fill and outline.
        ///
        /// A ghost is dashed in the mockup and is drawn faded here instead: IMGUI has no dashed rectangle, and
        /// faking one out of a dozen box fills per node is a lot of draw calls to say what a flat alpha says.
        /// </summary>
        private static void Body(Rect rect, ResearchProjectDef project, UIColorPaletteDef palette,
            ResearchState state, bool selected,
            bool over, bool dimmed)
        {
            Color inside = state == ResearchState.Ghost || dimmed
                ? UIElementPainter.Composite(palette.WindowBackground,
                    new Color(palette.PanelBackground.r, palette.PanelBackground.g, palette.PanelBackground.b,
                        0.5f))
                : over
                    ? palette.SurfaceRaised
                    : palette.PanelBackground;

            Color border = selected
                ? palette.Accent
                : state == ResearchState.Researching
                    ? palette.Accent
                    : state == ResearchState.Finished
                        ? UIElementPainter.Composite(palette.PanelBackground,
                            new Color(palette.Success.r, palette.Success.g, palette.Success.b, 0.5f))
                        : palette.Border;

            UIElementPainter.Outline(rect, border, inside, selected ? 2f : 1f);

            // A finished project gets the diagonal stripe wash in green -- the same card art the work grid and the
            // work panel use to mark a cell, so a done card is recognisable as done from the corner of the eye
            // rather than by reading the word on it. Skipped when the node is dimmed, since a washed card that is
            // also faded reads as neither.
            if (state == ResearchState.Finished && !dimmed)
                UIElementPainter.PaintStripeWash(rect.ContractedBy(1f),
                    new Color(palette.Success.r, palette.Success.g, palette.Success.b, DoneWashAlpha));

            // The project actually being worked on fills from the left as it goes. The thin bar at the foot of
            // every card gives the exact fraction; this is the one that can be found across a canvas of three
            // hundred without hunting for a three pixel line.
            if (state == ResearchState.Researching && !dimmed)
            {
                float percent = Mathf.Clamp01(project.ProgressPercent);

                if (percent > 0f)
                    Widgets.DrawBoxSolid(new Rect(rect.x + 1f, rect.y + 1f, (rect.width - 2f) * percent,
                            rect.height - 2f),
                        new Color(palette.Accent.r, palette.Accent.g, palette.Accent.b, ProgressFillAlpha));
            }
        }

        private static void Stripe(Rect rect, Color accent, ResearchState state, bool dimmed)
        {
            Color color = state == ResearchState.Ghost || dimmed
                ? new Color(accent.r, accent.g, accent.b, 0.35f)
                : accent;

            // The whole edge, flush to the corners, on Aaron's instruction of 2026-08-23. It was inset four
            // pixels top and bottom, which made it a dash floating beside the card rather than part of it; the
            // mockup drew this as the card's left border and that is what it should be. Painted over the outline
            // on that edge deliberately -- a band colour interrupted by two pixels of grey at each corner reads
            // as a mistake.
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, StripeWidth + 1f, rect.height), color);
        }

        private static Color Ink(UIColorPaletteDef palette, ResearchState state, bool dimmed)
        {
            if (dimmed || state == ResearchState.Ghost)
                return palette.TextDisabled;

            switch (state)
            {
                case ResearchState.Finished:
                case ResearchState.Prerequisite:
                case ResearchState.Mechanitor:
                    return palette.TextSecondary;

                default:
                    return palette.TextPrimary;
            }
        }

        /// <summary>
        /// The name, wrapped to two lines and cut off rather than pushed off.
        ///
        /// Wrapped rather than ellipsed on one line, because a two-word project name is the common case and
        /// "Microelectronics ba..." is worse than two short lines.
        /// </summary>
        /// <summary>
        /// How wide a string draws at the node font, with no ellipsis reserve.
        ///
        /// For text that is right-aligned and never shortened, where <c>UIRichText.WidthOf</c>'s thirteen pixel
        /// reserve would be room taken from whatever sits beside it.
        /// </summary>
        private static float Measure(string text)
        {
            GameFont font = Text.Font;

            try
            {
                Text.Font = GameFont.Tiny;

                return Text.CalcSize(text).x;
            }
            finally
            {
                Text.Font = font;
            }
        }

        private static void Name(Rect band, string text, Color color)
        {
            GameFont font = Text.Font;
            Color previous = GUI.color;
            bool wrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Small;

                // Never wrapped, from 2026-08-23. A wrapping name forces every node in the game to be as tall
                // as the longest name in it, and the second line landed on the row beneath whenever the reserve
                // was wrong -- which is the truncation family this mod has now hit six times. One line with an
                // ellipsis is a fixed height, and the full name is in the tooltip and the detail panel.
                Text.WordWrap = false;
                GUI.color = color;

                UIRichText.Label(band, text);
            }
            finally
            {
                Text.WordWrap = wrap;
                GUI.color = previous;
                Text.Font = font;
            }
        }

        /// <summary>
        /// What it costs, and how long that is at the colony's rate.
        ///
        /// Knowledge projects get their category and cost instead: they are not bought with researcher time, so
        /// there is no day figure to give and pretending otherwise would be the one number on the node that lies.
        /// </summary>
        private static string Cost(ResearchProjectDef project)
        {
            if (project.knowledgeCategory != null)
                return project.knowledgeCategory.LabelCap.ToString() + " "
                       + project.knowledgeCost.ToString("F0");

            string cost = project.CostApparent.ToString("F0");

            if (project.IsFinished)
                return cost;

            float days = ResearchRate.DaysFor(project);

            return days < 0f ? cost : cost + "   " + ResearchRate.Days(days);
        }

        /// <summary>
        /// The progress bar, drawn only when there is progress to show.
        ///
        /// An empty bar on every unstarted project would be three hundred grey lines saying nothing. A finished
        /// project does not get one either: the tick and the green outline already say it.
        /// </summary>
        private static void Progress(Rect rect, ResearchProjectDef project, UIColorPaletteDef palette,
            Color accent, ResearchState state)
        {
            if (state == ResearchState.Finished || state == ResearchState.Unknown)
                return;

            float percent = project.ProgressPercent;

            if (percent <= 0.001f)
                return;

            Widgets.DrawBoxSolid(rect, palette.SurfaceSunken);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(percent), rect.height),
                accent);
        }
    }
}
