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
                float line = UIFonts.LineHeightOf(GameFont.Tiny);

                return Mathf.Ceil(PadTop + line * 3f + 1f + ProgressHeight + PadTop);
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

            Body(rect, palette, state, selected, over, dimmed);
            Stripe(rect, accent, state, dimmed);

            float x = rect.x + PadLeft;
            float width = rect.width - PadLeft - PadRight;
            float line = UIFonts.LineHeightOf(GameFont.Tiny);

            Color ink = Ink(palette, state, dimmed);

            // The name gives up its top right corner whenever the badge is there, so a queue number never lands
            // on top of a letter.
            float reserved = queuePlace > 0 || (over && actionable) ? BadgeSize - 4f : 0f;
            Rect nameBand = new Rect(x, rect.y + PadTop, width - reserved, line * 2f);

            if (state == ResearchState.Unknown)
                ResearchMask.Draw(new Rect(nameBand.x, nameBand.y, nameBand.width, line),
                    ResearchMask.Key(project, "name"), palette.Mood);
            else
                Name(nameBand, project.LabelCap.ToString(), ink);

            Figures(new Rect(x, rect.y + PadTop + line * 2f, width, line), node, project, state, palette,
                dimmed);

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
        private static void Figures(Rect band, ResearchNode node, ResearchProjectDef project,
            ResearchState state, UIColorPaletteDef palette, bool dimmed)
        {
            Texture2D icon = state == ResearchState.Unknown ? null : ResearchFacts.IconFor(state);
            string mark = state == ResearchState.Unknown ? null : ResearchFacts.MarkFor(state, project);
            Color flag = ResearchFacts.ColorFor(state, palette, project.knowledgeCategory);

            float right = band.xMax;

            if (icon != null)
            {
                Color previous = GUI.color;
                Rect at = new Rect(right - IconSide(band), band.y + (band.height - IconSide(band)) * 0.5f, IconSide(band),
                    IconSide(band));

                GUI.color = dimmed ? palette.TextDisabled : flag;
                GUI.DrawTexture(at, icon);
                GUI.color = previous;

                right = at.x - 2f;
            }

            if (mark != null)
            {
                float markWidth = TabParts.PillWidth(mark);

                TabParts.RowLabel(new Rect(right - markWidth, band.y, markWidth, band.height), mark,
                    dimmed ? palette.TextDisabled : flag, GameFont.Tiny);

                right = right - markWidth - 2f;
            }

            if (state == ResearchState.Finished && ResearchGlyphs.Tick != null)
            {
                Color previous = GUI.color;

                GUI.color = palette.Success;
                GUI.DrawTexture(new Rect(right - 10f, band.center.y - 5f, 10f, 10f), ResearchGlyphs.Tick);
                GUI.color = previous;

                right -= 12f;
            }

            Rect figures = new Rect(band.x, band.y, Mathf.Max(0f, right - band.x), band.height);

            if (state == ResearchState.Unknown)
                ResearchMask.Draw(figures, ResearchMask.Key(project, "cost"), palette.TextDisabled);
            else
                TabParts.RowLabel(figures, Cost(project), palette.TextDisabled, GameFont.Tiny);
        }

        /// <summary>A square the height of the line, for an icon sitting in it.</summary>
        private static float IconSide(Rect band)
        {
            return Mathf.Min(band.height, 11f);
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
        private static void Body(Rect rect, UIColorPaletteDef palette, ResearchState state, bool selected,
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

            UIElementPainter.OutlineRounded(rect, border, inside, selected ? 2f : 1f);
        }

        private static void Stripe(Rect rect, Color accent, ResearchState state, bool dimmed)
        {
            Color color = state == ResearchState.Ghost || dimmed
                ? new Color(accent.r, accent.g, accent.b, 0.35f)
                : accent;

            Widgets.DrawBoxSolid(new Rect(rect.x + 1f, rect.y + 4f, StripeWidth, rect.height - 8f), color);
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
        private static void Name(Rect band, string text, Color color)
        {
            GameFont font = Text.Font;
            Color previous = GUI.color;
            bool wrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = true;
                GUI.color = color;

                Widgets.Label(band, text);
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
