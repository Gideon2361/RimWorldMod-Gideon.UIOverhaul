using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Factions
{
    /// <summary>
    /// The factions tab: where you stand with every power on the planet, and what is holding it there.
    ///
    /// <b>Vanilla draws two numbers and hides everything that explains them.</b> Current goodwill and natural
    /// goodwill sit side by side, the second one unlabeled on a black rectangle, and the caps, the recent
    /// events and the breakdown of the resting value are all built on hover and thrown away. One column of the
    /// six has a heading, the words "Enemy of", drawn at a hardcoded x of 614.
    ///
    /// <b>The resting band is the correction this screen exists to make.</b> Read as a pair, the two figures
    /// look like a journey: eighty eight now, sixty five later. They are not. The game leaves a standing alone
    /// anywhere inside fifty either side of the natural value and only pulls it back from outside that, by ten
    /// at a time and once every fifty days. Drawing the band rather than the point turns the question from
    /// "which of these numbers is the real one" into "is this faction settled, and if not, which way is it
    /// going".
    ///
    /// <b>The list is grouped, because the flat one is unreadable by the midgame.</b> Vanilla walks
    /// <c>AllFactionsInViewOrder</c> into a single scroll of eighty pixel rows with an alternating highlight,
    /// so allies, neutrals, a permanent enemy and two beaten factions arrive as one undifferentiated column.
    ///
    /// <b>Nothing here is stored.</b> Every figure is read live in <see cref="FactionsFacts"/> off the faction,
    /// the goodwill situation manager and the world object list.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class FactionsPanel
    {
        internal const float WindowWidth = 1180f;
        internal const float WindowHeight = 720f;

        private const float Pad = 12f;
        private const float RailWidth = 200f;
        private const float HeaderHeight = 66f;
        private const float StripHeight = 26f;
        private const float RowGap = 6f;

        /// <summary>Side of the header glyph, and the air between it and the title.</summary>
        private const float GlyphSize = 34f;

        private const float GlyphGap = 10f;

        private const float RowHeight = 44f;

        private const float CrestWidth = 34f;
        private const float StandingWidth = 104f;
        private const float EnemiesWidth = 112f;
        private const float BasesWidth = 76f;
        private const float ColumnGap = 10f;

        /// <summary>
        /// The tab's own mark, drawn beside the title the way every other restyled tab draws its own.
        ///
        /// The same texture the button on the bar uses, so the glyph a player clicked to get here is the glyph
        /// waiting at the top of the screen. Loaded in a static constructor because the game warns about any
        /// type holding a static texture field without one: the check reads the field's type rather than
        /// watching when the texture is fetched.
        /// </summary>
        private static readonly Texture2D Glyph;

        static FactionsPanel()
        {
            // Through a local, because a readonly field can only be assigned in the constructor itself and the
            // guard does its work in a closure.
            Texture2D glyph = null;

            UIGuard.Try("Factions.Glyph",
                () => glyph = ContentFinder<Texture2D>.Get("UI/MainButtonIcons/Factions", false),
                "The factions header has no glyph this session. Everything on the tab still reads.");

            Glyph = glyph;
        }

        private static readonly List<FactionRow> Rows = new List<FactionRow>();

        private static readonly List<FactionRow> Shown = new List<FactionRow>();

        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search factions",
            Icon = TexButton.Search,
            MaxLength = 40
        };

        private static Vector2 scroll;
        private static float viewHeight = 1f;

        private static Vector2 railScroll;
        private static bool railDragging;
        private static float railDragOffset;

        /// <summary>Which rail entry is chosen. A key rather than an index, so the rail can change shape.</summary>
        private static string filter = FilterAll;

        /// <summary>
        /// The faction whose card is open, held as the faction rather than as a row index.
        ///
        /// A row index would quietly move onto a different faction the moment one was defeated or the filter
        /// changed, which is the same reason the power tab holds its grid by net.
        /// </summary>
        private static Faction opened;

        private const string FilterAll = "all";
        private const string FilterAllied = "allied";
        private const string FilterNeutral = "neutral";
        private const string FilterHostile = "hostile";
        private const string FilterDrifting = "drifting";
        private const string FilterHeld = "held";
        private const string FilterNever = "never";
        private const string FilterBeaten = "beaten";
        private const string KindPrefix = "kind:";

        internal static void Draw(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Rect body = inRect.ContractedBy(Pad);

            FactionsFacts.All(Rows);

            if (Rows.Count == 0)
            {
                TabParts.Line(body, body.y + 40f, "There is nobody else on this planet yet.",
                    palette.TextSecondary);

                return;
            }

            Header(new Rect(body.x, body.y, body.width, HeaderHeight), palette);

            float top = body.y + HeaderHeight + Pad;
            Rect below = new Rect(body.x, top, body.width, body.yMax - top);

            Rail(new Rect(below.x, below.y, RailWidth, below.height), palette);

            Rect main = new Rect(below.x + RailWidth + Pad, below.y, below.width - RailWidth - Pad,
                below.height);

            Strip(new Rect(main.x, main.y, main.width, StripHeight), palette);

            Rect list = new Rect(main.x, main.y + StripHeight + RowGap, main.width,
                main.yMax - main.y - StripHeight - RowGap);

            Filtered(Shown);

            // The gutter is reserved whether or not the list overflows, so the columns do not shift sideways
            // the moment one more faction appears.
            Rect view = new Rect(0f, 0f, list.width - 18f, viewHeight);

            Widgets.BeginScrollView(list, ref scroll, view);

            float y = 0f;

            if (Shown.Count == 0)
            {
                TabParts.RowLabel(new Rect(view.x, y + 8f, view.width, 22f), Empty(), palette.TextDisabled,
                    GameFont.Small, FactionsFaces.Body, FactionsFaces.Size.Body);

                y += 34f;
            }
            else
            {
                y = Group(view, y, FactionGroup.Allied, "Allied", palette);
                y = Group(view, y, FactionGroup.Neutral, "Neutral", palette);
                y = Group(view, y, FactionGroup.Hostile, "Hostile", palette);
                y = Group(view, y, FactionGroup.Beaten, "Beaten", palette);
            }

            if (Event.current.type == EventType.Layout)
                viewHeight = Mathf.Max(1f, y);

            Widgets.EndScrollView();
        }

        /// <summary>What to say when a filter or a search has emptied the list.</summary>
        private static string Empty()
        {
            if (!Search.IsEmpty)
                return "No faction here is called that.";

            switch (filter)
            {
                case FilterAllied: return "Nobody is allied with you.";
                case FilterNeutral: return "Nobody is neutral toward you.";
                case FilterHostile: return "Nobody is hostile toward you.";
                case FilterDrifting: return "Every standing is inside its resting band.";
                case FilterHeld: return "Nothing is holding any standing down.";
                case FilterNever: return "Every faction here can be reasoned with.";
                case FilterBeaten: return "Nobody has been driven off the planet.";
                default: return "Nothing to show.";
            }
        }

        // -------------------------------------------------------------------------------------------
        // Header
        // -------------------------------------------------------------------------------------------

        private static void Header(Rect rect, UIColorPaletteDef palette)
        {
            // SurfaceSunken, the same fill the rail beside it uses: header and rail are both chrome framing the
            // content, so they share a surface and the blocks between them sit above it.
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(10f);

            float text = inner.x;

            if (Glyph != null)
            {
                Rect slot = new Rect(inner.x, inner.y + (inner.height - GlyphSize) * 0.5f, GlyphSize,
                    GlyphSize);

                Color previous = GUI.color;

                // The mark and the title are one colour, because together they are the tab. Never a standing
                // colour: on this screen green, grey and red are verdicts on a faction, and the tab must not
                // look like it is passing one on itself.
                GUI.color = palette.TabFactions;

                GUI.DrawTexture(slot, Glyph);
                GUI.color = previous;

                text = slot.xMax + GlyphGap;
            }

            TabParts.RowLabel(new Rect(text, inner.y + 2f, 340f, 26f), "Factions", palette.TabFactions,
                GameFont.Medium, FactionsFaces.Display, FactionsFaces.Size.Title);

            int allied = 0;
            int neutral = 0;
            int hostile = 0;
            int drifting = 0;
            int beaten = 0;

            for (int i = 0; i < Rows.Count; i++)
            {
                switch (Rows[i].group)
                {
                    case FactionGroup.Allied: allied++; break;
                    case FactionGroup.Neutral: neutral++; break;
                    case FactionGroup.Hostile: hostile++; break;
                    case FactionGroup.Beaten: beaten++; break;
                }

                if (Rows[i].drifting)
                    drifting++;
            }

            int standing = Rows.Count - beaten;

            string subtitle = standing + (standing == 1 ? " power still standing" : " powers still standing");

            if (beaten > 0)
                subtitle += "  -  " + beaten + " beaten";

            if (drifting > 0)
                subtitle += "  -  " + drifting + " on the move";

            TabParts.RowLabel(new Rect(text, inner.y + 28f, 420f, 18f), subtitle, palette.TextSecondary,
                GameFont.Tiny, FactionsFaces.Condensed, FactionsFaces.Size.Subtitle);

            float right = inner.xMax;

            if (beaten > 0)
                right = Readout(inner, right, beaten.ToString(), "beaten", palette.TextDisabled, palette);

            if (drifting > 0)
                right = Readout(inner, right, drifting.ToString(), "on the move", palette.Warning, palette);

            right = Readout(inner, right, hostile.ToString(), "hostile",
                hostile > 0 ? palette.Danger : palette.TextDisabled, palette);

            right = Readout(inner, right, neutral.ToString(), "neutral", palette.TextSecondary, palette);

            // Laid out last so it lands furthest from the title, which on a row read from the right is the
            // first thing reached.
            Readout(inner, right, allied.ToString(), "allied",
                allied > 0 ? palette.Success : palette.TextDisabled, palette);
        }

        private static float Readout(Rect inner, float right, string value, string caption, Color tint,
            UIColorPaletteDef palette)
        {
            string label = FactionsFaces.Caps(caption);

            float width = Mathf.Max(
                UITextControl.Width(value, FactionsFaces.Mono, FactionsFaces.Size.Readout),
                UITextControl.Width(label, FactionsFaces.Mono, FactionsFaces.Size.Caption)) + 4f;

            float figure = UITextControl.LineHeight(FactionsFaces.Mono, FactionsFaces.Size.Readout);
            float under = UITextControl.LineHeight(FactionsFaces.Mono, FactionsFaces.Size.Caption);

            float top = inner.y + (inner.height - figure - under - 2f) * 0.5f;

            Rect band = new Rect(right - width, top, width, figure + under + 2f);

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;

                GUI.color = tint;
                UITextControl.LabelEllipses(new Rect(band.x, band.y, band.width, figure), value,
                    FactionsFaces.Mono, FactionsFaces.Size.Readout);

                GUI.color = palette.TextSecondary;
                UITextControl.LabelEllipses(new Rect(band.x, band.y + figure + 2f, band.width, under), label,
                    FactionsFaces.Mono, FactionsFaces.Size.Caption);
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

        private static readonly List<UIRailElement> Elements = new List<UIRailElement>();

        private static readonly List<FactionDef> Kinds = new List<FactionDef>();

        /// <summary>
        /// Three questions down the side, in the order they get asked.
        ///
        /// <b>Standing first, because that is what the tab is for.</b> Then the three things worth watching
        /// that no vanilla screen names at all: a standing being pulled back, a standing held below its
        /// ceiling, and a faction that can never be anything but hostile. Then the faction types, which is a
        /// filter rather than a warning, and last the beaten, who are on the list only because they used to
        /// matter.
        /// </summary>
        private static void Rail(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            int allied = 0;
            int neutral = 0;
            int hostile = 0;
            int drifting = 0;
            int held = 0;
            int never = 0;
            int beaten = 0;

            Kinds.Clear();

            for (int i = 0; i < Rows.Count; i++)
            {
                FactionRow row = Rows[i];

                switch (row.group)
                {
                    case FactionGroup.Allied: allied++; break;
                    case FactionGroup.Neutral: neutral++; break;
                    case FactionGroup.Hostile: hostile++; break;
                    case FactionGroup.Beaten: beaten++; break;
                }

                if (row.drifting)
                    drifting++;

                if (row.ceiling < 100)
                    held++;

                if (!row.hasGoodwill && !row.defeated)
                    never++;

                if (row.faction.def != null && !Kinds.Contains(row.faction.def))
                    Kinds.Add(row.faction.def);
            }

            Elements.Clear();

            Elements.Add(Section("Standing", palette));
            Elements.Add(Entry(FilterAll, "Everyone", Rows.Count, null, palette));
            Elements.Add(Entry(FilterAllied, "Allied", allied, allied > 0 ? palette.Success : (Color?) null,
                palette));
            Elements.Add(Entry(FilterNeutral, "Neutral", neutral, null, palette));
            Elements.Add(Entry(FilterHostile, "Hostile", hostile, hostile > 0 ? palette.Danger : (Color?) null,
                palette));

            Elements.Add(new UIRailDividerControl());
            Elements.Add(Section("Watch", palette));
            Elements.Add(Entry(FilterDrifting, "On the move", drifting,
                drifting > 0 ? palette.Warning : (Color?) null, palette));
            Elements.Add(Entry(FilterHeld, "Held down", held, held > 0 ? palette.Warning : (Color?) null,
                palette));
            Elements.Add(Entry(FilterNever, "Never friendly", never, null, palette));

            if (Kinds.Count > 1)
            {
                Elements.Add(new UIRailDividerControl());
                Elements.Add(Section("Kind", palette));

                Kinds.Sort((a, b) => string.Compare(a.LabelCap.Resolve(), b.LabelCap.Resolve(),
                    System.StringComparison.CurrentCultureIgnoreCase));

                for (int i = 0; i < Kinds.Count; i++)
                {
                    int count = 0;

                    for (int j = 0; j < Rows.Count; j++)
                    {
                        if (Rows[j].faction.def == Kinds[i])
                            count++;
                    }

                    Elements.Add(Entry(KindPrefix + Kinds[i].defName, Kinds[i].LabelCap.Resolve(), count, null,
                        palette));
                }
            }

            if (beaten > 0)
            {
                Elements.Add(new UIRailDividerControl());
                Elements.Add(Section("Gone", palette));
                Elements.Add(Entry(FilterBeaten, "Beaten", beaten, null, palette));
            }

            // A faction type can leave the list entirely when its last faction is beaten, taking its rail
            // entry with it. Falling back to everyone is better than leaving the list filtered by something
            // the player can no longer see selected, and can no longer click off.
            if (!Offered(filter))
                filter = FilterAll;

            string picked = UIRailControl.Draw(rect.ContractedBy(6f), Elements, filter, ref railScroll,
                ref railDragging, ref railDragOffset, palette, false);

            if (picked == null || picked == filter)
                return;

            filter = picked;

            // The open card is closed on a filter change rather than carried across it: the row it belongs to
            // is usually not in the new list, and a card floating under a heading it does not belong to reads
            // as a drawing fault.
            opened = null;
            scroll = Vector2.zero;
        }

        /// <summary>Whether the rail as just built still has the chosen entry on it.</summary>
        private static bool Offered(string key)
        {
            for (int i = 0; i < Elements.Count; i++)
            {
                if (Elements[i].Key == key)
                    return true;
            }

            return false;
        }

        private static UIRailSectionHeaderControl Section(string title, UIColorPaletteDef palette)
        {
            return new UIRailSectionHeaderControl(FactionsFaces.Caps(title))
            {
                Face = FactionsFaces.Mono,
                Points = FactionsFaces.Size.RailHead,
                Color = palette.TextDisabled
            };
        }

        private static UIRailClickableEntry Entry(string key, string label, int count, Color? tint,
            UIColorPaletteDef palette)
        {
            bool on = key == filter;

            return new UIRailClickableEntry(key, label)
            {
                Rise = 28f,
                Face = FactionsFaces.Condensed,
                Points = FactionsFaces.Size.RailName,
                TextColor = on ? palette.TabFactions : (Color?) null,
                Count = count,
                CountFace = FactionsFaces.Mono,
                CountPoints = FactionsFaces.Size.RailCount,
                CountColor = on ? palette.TabFactions : tint,
                Disabled = count == 0 && key != FilterAll
            };
        }

        // -------------------------------------------------------------------------------------------
        // Strip
        // -------------------------------------------------------------------------------------------

        private static void Strip(Rect rect, UIColorPaletteDef palette)
        {
            Search.Draw(new Rect(rect.x, rect.y, Mathf.Min(260f, rect.width * 0.4f), rect.height), palette);
        }

        /// <summary>The rows the rail and the search box between them have left.</summary>
        private static void Filtered(List<FactionRow> into)
        {
            into.Clear();

            for (int i = 0; i < Rows.Count; i++)
            {
                FactionRow row = Rows[i];

                if (!Passes(row))
                    continue;

                if (!Search.IsEmpty && !Search.Matches(row.name) && !Search.Matches(row.kind)
                    && !Search.Matches(row.leader ?? string.Empty))
                {
                    continue;
                }

                into.Add(row);
            }
        }

        private static bool Passes(FactionRow row)
        {
            switch (filter)
            {
                case FilterAll: return true;
                case FilterAllied: return row.group == FactionGroup.Allied;
                case FilterNeutral: return row.group == FactionGroup.Neutral;
                case FilterHostile: return row.group == FactionGroup.Hostile;
                case FilterDrifting: return row.drifting;
                case FilterHeld: return row.ceiling < 100;
                case FilterNever: return !row.hasGoodwill && !row.defeated;
                case FilterBeaten: return row.group == FactionGroup.Beaten;
            }

            if (filter != null && filter.StartsWith(KindPrefix))
                return row.faction.def != null && row.faction.def.defName == filter.Substring(KindPrefix.Length);

            return true;
        }

        // -------------------------------------------------------------------------------------------
        // Groups and rows
        // -------------------------------------------------------------------------------------------

        private static float Group(Rect view, float y, FactionGroup group, string title,
            UIColorPaletteDef palette)
        {
            int count = 0;

            for (int i = 0; i < Shown.Count; i++)
            {
                if (Shown[i].group == group)
                    count++;
            }

            if (count == 0)
                return y;

            float cardHeight = 0f;

            for (int i = 0; i < Shown.Count; i++)
            {
                if (Shown[i].group == group && Shown[i].faction == opened)
                    cardHeight = FactionCard.HeightOf(Shown[i]);
            }

            const float cap = 22f;
            const float columns = 17f;

            float height = cap + columns + count * RowHeight + cardHeight + 6f;

            Rect box = new Rect(view.x, y, view.width, height);

            UIElementPainter.OutlineRounded(box, palette.Border, palette.PanelBackground);

            Rect head = new Rect(box.x, box.y, box.width, cap);

            UIElementPainter.FillRounded(head,
                UIElementPainter.Composite(palette.PanelBackground, palette.HoverOverlay));

            TabParts.RowLabel(new Rect(head.x + 10f, head.y, head.width - 20f, head.height),
                FactionsFaces.Caps(title), group == FactionGroup.Hostile ? palette.Danger
                    : group == FactionGroup.Allied ? palette.Success : palette.TextSecondary,
                GameFont.Tiny, FactionsFaces.Mono, FactionsFaces.Size.BlockHead);

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextDisabled;

                UITextControl.LabelEllipses(new Rect(head.x + 10f, head.y, head.width - 20f, head.height),
                    FactionsFaces.Caps(Suffix(group, count)), FactionsFaces.Mono,
                    FactionsFaces.Size.BlockHead);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }

            Rect band = new Rect(box.x + 12f, head.yMax, box.width - 24f, columns);

            Columns(band, palette);

            float cursor = band.yMax;

            for (int i = 0; i < Shown.Count; i++)
            {
                if (Shown[i].group != group)
                    continue;

                cursor = Row(new Rect(box.x + 12f, cursor, box.width - 24f, RowHeight), Shown[i], palette);

                if (Shown[i].faction == opened)
                {
                    cursor = FactionCard.Draw(new Rect(box.x + 12f, cursor, box.width - 24f,
                        FactionCard.HeightOf(Shown[i])), Shown[i], palette);
                }
            }

            return box.yMax + RowGap;
        }

        /// <summary>The right side of a group's heading: the count, and what is notable about the group.</summary>
        private static string Suffix(FactionGroup group, int count)
        {
            switch (group)
            {
                case FactionGroup.Beaten:
                    return count + (count == 1 ? " faction, no settlements left" : " factions, no settlements left");
                case FactionGroup.Hostile:
                    return count + (count == 1 ? " faction" : " factions");
                default:
                    return count + (count == 1 ? " faction" : " factions");
            }
        }

        /// <summary>
        /// The column headings, drawn above every group rather than once at the top.
        ///
        /// <b>Repeated on purpose.</b> Each group is its own short table and the list scrolls, so one strip at
        /// the top is a strip that is gone by the time it is needed. It costs seventeen pixels a group and it
        /// is still five more headings than vanilla draws in total.
        /// </summary>
        private static void Columns(Rect band, UIColorPaletteDef palette)
        {
            Widgets.DrawLineHorizontal(band.x, band.yMax - 1f, band.width, palette.Border);

            float x = band.x + CrestWidth + ColumnGap;

            float fixedWidth = CrestWidth + StandingWidth + EnemiesWidth + BasesWidth + ColumnGap * 5f;
            float flexible = Mathf.Max(120f, band.width - fixedWidth);

            float nameWidth = flexible * 0.44f;
            float scaleWidth = flexible - nameWidth;

            Heading(new Rect(x, band.y, nameWidth, band.height), "Faction", palette);
            x += nameWidth + ColumnGap;

            Heading(new Rect(x, band.y, StandingWidth, band.height), "Standing", palette);
            x += StandingWidth + ColumnGap;

            Heading(new Rect(x, band.y, scaleWidth, band.height),
                "Goodwill, and the band it rests in", palette);
            x += scaleWidth + ColumnGap;

            Heading(new Rect(x, band.y, EnemiesWidth, band.height), "At war with", palette);
            x += EnemiesWidth + ColumnGap;

            HeadingRight(new Rect(x, band.y, BasesWidth, band.height), "Bases", palette);
        }

        private static void Heading(Rect rect, string text, UIColorPaletteDef palette)
        {
            TabParts.RowLabel(rect, FactionsFaces.Caps(text), palette.TextDisabled, GameFont.Tiny,
                FactionsFaces.Mono, FactionsFaces.Size.Label);
        }

        private static void HeadingRight(Rect rect, string text, UIColorPaletteDef palette)
        {
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextDisabled;

                UITextControl.LabelEllipses(rect, FactionsFaces.Caps(text), FactionsFaces.Mono,
                    FactionsFaces.Size.Label);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }
        }

        private static float Row(Rect band, FactionRow row, UIColorPaletteDef palette)
        {
            bool open = row.faction == opened;

            if (open)
                Widgets.DrawBoxSolid(band, palette.SelectionOverlay);
            else if (Mouse.IsOver(band))
                Widgets.DrawBoxSolid(band, palette.HoverOverlay);

            float fixedWidth = CrestWidth + StandingWidth + EnemiesWidth + BasesWidth + ColumnGap * 5f;
            float flexible = Mathf.Max(120f, band.width - fixedWidth);

            float nameWidth = flexible * 0.44f;
            float scaleWidth = flexible - nameWidth;

            float x = band.x;

            Crest(new Rect(x, band.y + (band.height - 30f) * 0.5f, 30f, 30f), row.faction, row.icon,
                row.color, row.defeated, palette);

            x += CrestWidth + ColumnGap;

            Who(new Rect(x, band.y, nameWidth, band.height), row, palette);
            x += nameWidth + ColumnGap;

            Standing(new Rect(x, band.y, StandingWidth, band.height), row, palette);
            x += StandingWidth + ColumnGap;

            Scale(new Rect(x, band.y, scaleWidth, band.height), row, palette, false);
            x += scaleWidth + ColumnGap;

            Enemies(new Rect(x, band.y, EnemiesWidth, band.height), row, palette);
            x += EnemiesWidth + ColumnGap;

            Bases(new Rect(x, band.y, BasesWidth, band.height), row, palette);

            if (Widgets.ButtonInvisible(band))
            {
                opened = open ? null : row.faction;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            return band.yMax;
        }

        /// <summary>
        /// A faction's own icon in its own colour, which is how the game identifies one everywhere else.
        ///
        /// <b>Dimmed for a beaten faction rather than greyed out entirely,</b> because the colour is still how
        /// you recognise them in the "at war with" column of everybody else's row.
        /// </summary>
        internal static void Crest(Rect rect, Faction faction, Texture2D icon, Color color, bool faded,
            UIColorPaletteDef palette)
        {
            if (icon == null)
            {
                UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

                return;
            }

            Color previous = GUI.color;

            GUI.color = faded ? new Color(color.r, color.g, color.b, 0.4f) : color;
            GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit);
            GUI.color = previous;
        }

        private static void Who(Rect rect, FactionRow row, UIColorPaletteDef palette)
        {
            float top = rect.y + (rect.height - 34f) * 0.5f;

            TabParts.RowLabel(new Rect(rect.x, top, rect.width, 18f), row.name,
                row.defeated ? palette.TextDisabled : palette.TextPrimary, GameFont.Small,
                FactionsFaces.Condensed, FactionsFaces.Size.Name);

            string under = row.kind;

            if (!row.leader.NullOrEmpty())
                under += "  -  " + row.leader;

            TabParts.RowLabel(new Rect(rect.x, top + 17f, rect.width, 16f), under, palette.TextDisabled,
                GameFont.Tiny, FactionsFaces.Body, FactionsFaces.Size.Sub);
        }

        /// <summary>
        /// The relation word, in this mod's colours rather than vanilla's.
        ///
        /// <b>Vanilla draws Ally in <c>Color.green</c> and Neutral in <c>(0, 0.75, 1)</c>.</b> Those are
        /// unmixed primaries: on the game's greys the neutral reads as a hyperlink and the ally glows. Success,
        /// secondary text and danger say the same three things at the weights everything else on this screen is
        /// drawn at, and grey is the honest colour for neutral.
        /// </summary>
        internal static Color Tint(FactionRow row, UIColorPaletteDef palette)
        {
            if (row.defeated)
                return palette.TextDisabled;

            switch (row.relation)
            {
                case FactionRelationKind.Ally: return palette.Success;
                case FactionRelationKind.Hostile: return palette.Danger;
                default: return palette.TextSecondary;
            }
        }

        private static void Standing(Rect rect, FactionRow row, UIColorPaletteDef palette)
        {
            float top = rect.y + (rect.height - 32f) * 0.5f;

            TabParts.RowLabel(new Rect(rect.x, top, rect.width, 18f), row.relation.GetLabelCap(),
                Tint(row, palette), GameFont.Small, FactionsFaces.Condensed, FactionsFaces.Size.Standing);

            string under = row.defeated
                ? "beaten"
                : !row.hasGoodwill
                    ? "never friendly"
                    : FactionsFacts.Movement(row);

            Color tint = row.drifting || (row.hasGoodwill && row.ceiling < 100 && row.stored > row.goodwill)
                ? palette.Warning
                : palette.TextDisabled;

            TabParts.RowLabel(new Rect(rect.x, top + 16f, rect.width, 14f), FactionsFaces.Caps(under), tint,
                GameFont.Tiny, FactionsFaces.Mono, FactionsFaces.Size.Label);
        }

        // -------------------------------------------------------------------------------------------
        // The goodwill scale
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Standing drawn against the band it rests in, from a hundred against to a hundred for.
        ///
        /// <b>The band is the whole point of the control.</b> A standing inside it is not going anywhere; a
        /// standing outside it is pulled back by ten every fifty days until it is inside again. Two bare
        /// figures cannot say that, and the pair vanilla draws actively suggests the opposite.
        ///
        /// <b>The band is grey, not the tab colour, and that was a correction.</b> It was drawn in
        /// <c>TabFactions</c> on the reasoning that the identity should mark what the screen is pointing at.
        /// That breaks the rule every other tab identity in this palette follows -- see
        /// <see cref="UIColorRole.TabPawns"/>, which states it outright: an identity never touches a row. It
        /// also put a second colour inside a control that already carries three, and for an allied faction
        /// the pin, the ally zone and the band would all have been green. The band is the scale's own
        /// furniture, like the zones at either end, so it is drawn as furniture.
        ///
        /// <b>The ceiling is amber, and the pull is the only other amber on the tab.</b> Both are genuinely
        /// warnings: one is a mark the standing cannot climb past however many gifts are sent, the other is
        /// the game about to move a number the player did not move.
        /// </summary>
        internal static void Scale(Rect rect, FactionRow row, UIColorPaletteDef palette, bool large)
        {
            if (!row.hasGoodwill)
            {
                TabParts.RowLabel(rect,
                    row.defeated
                        ? "Nothing left to have a standing with."
                        : row.permanentEnemy
                            ? "Nothing will make them friendly. There is no standing to move."
                            : "They have no standing to read.",
                    palette.TextDisabled, GameFont.Tiny, FactionsFaces.Body, FactionsFaces.Size.Sub);

                return;
            }

            float figureHeight = large ? 20f : 17f;
            float trackHeight = large ? 22f : 15f;

            float top = rect.y + (rect.height - figureHeight - trackHeight) * 0.5f;

            Figures(new Rect(rect.x, top, rect.width, figureHeight), row, palette, large);

            Track(new Rect(rect.x, top + figureHeight, rect.width, trackHeight), row, palette, large);
        }

        private static void Figures(Rect rect, FactionRow row, UIColorPaletteDef palette, bool large)
        {
            float points = large ? FactionsFaces.Size.Readout : FactionsFaces.Size.Figure;

            string now = FactionsFacts.Signed(row.goodwill);

            float width = UITextControl.Width(now, FactionsFaces.Mono, points) + 8f;

            TabParts.RowLabel(new Rect(rect.x, rect.y, width, rect.height), now, Tint(row, palette),
                GameFont.Small, FactionsFaces.Mono, points);

            string rest = "resting band " + FactionsFacts.Signed(row.restingLow) + " to "
                          + FactionsFacts.Signed(row.restingHigh);

            if (row.ceiling < 100)
                rest += ", held at " + FactionsFacts.Signed(row.ceiling);

            TabParts.RowLabel(new Rect(rect.x + width, rect.y, Mathf.Max(0f, rect.width - width), rect.height),
                rest, palette.TextDisabled, GameFont.Tiny, FactionsFaces.Body, FactionsFaces.Size.Sub);
        }

        private static void Track(Rect rect, FactionRow row, UIColorPaletteDef palette, bool large)
        {
            float thickness = large ? 6f : 4f;
            float barTop = rect.y + (rect.height - thickness) * 0.5f;

            Rect bar = new Rect(rect.x, barTop, rect.width, thickness);

            Widgets.DrawBoxSolid(bar, palette.SurfaceSunken);

            // The two ends where a standing stops being neutral, at a quarter of the strength the marks on top
            // of them are drawn at: they are the scale's own furniture, not readings.
            float end = rect.width * 0.125f;

            Widgets.DrawBoxSolid(new Rect(rect.x, barTop, end, thickness), Faded(palette.Danger, 0.30f));
            Widgets.DrawBoxSolid(new Rect(rect.xMax - end, barTop, end, thickness),
                Faded(palette.Success, 0.28f));

            float low = At(rect, row.restingLow);
            float high = At(rect, row.restingHigh);

            Widgets.DrawBoxSolid(new Rect(low, barTop, Mathf.Max(1f, high - low), thickness),
                Faded(palette.TextSecondary, 0.42f));

            if (row.ceiling < 100)
            {
                float ceiling = At(rect, row.ceiling);

                Widgets.DrawBoxSolid(new Rect(ceiling, barTop, Mathf.Max(1f, rect.xMax - ceiling), thickness),
                    Faded(palette.Warning, 0.34f));

                Widgets.DrawBoxSolid(new Rect(ceiling - 1f, rect.y + 2f, 2f, rect.height - 4f),
                    palette.Warning);
            }

            // The pull back into the band, drawn from where the standing actually is to the edge it is being
            // taken to. Only when there is one: for most factions most of the time this is absent, which is
            // itself the reading.
            if (row.drifting)
            {
                float from = At(rect, row.stored);
                float to = row.driftDirection > 0 ? low : high;

                Widgets.DrawBoxSolid(new Rect(Mathf.Min(from, to), barTop + (thickness - 2f) * 0.5f,
                    Mathf.Abs(to - from), 2f), palette.Warning);
            }

            // The resting value itself, as a hairline inside its band. It is a term of the band rather than a
            // reading of its own, so it is drawn thinner than anything else on the track and in the same
            // family as the band around it.
            Widgets.DrawBoxSolid(new Rect(At(rect, row.natural) - 0.5f, barTop - 2f, 1f, thickness + 4f),
                palette.TextSecondary);

            // Where the standing stood before the ceiling clipped it. Drawn only when the two disagree, which
            // is a fact the game gives the player no way of seeing today.
            if (row.stored != row.goodwill)
            {
                Widgets.DrawBoxSolid(new Rect(At(rect, row.stored) - 0.5f, barTop - 1f, 1f, thickness + 2f),
                    palette.TextDisabled);
            }

            float pinHeight = large ? 18f : 12f;

            Widgets.DrawBoxSolid(new Rect(At(rect, row.goodwill) - 1.5f,
                rect.y + (rect.height - pinHeight) * 0.5f, 3f, pinHeight), Tint(row, palette));
        }

        private static float At(Rect track, int goodwill)
        {
            return track.x + track.width * FactionsFacts.Fraction(goodwill);
        }

        private static Color Faded(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        // -------------------------------------------------------------------------------------------
        // The last two columns
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Who else is at war with them, as their own crests.
        ///
        /// <b>Vanilla draws these too, and gives them no heading and no names.</b> The crests are how a
        /// faction is recognised everywhere else in the game, so they stay; the tooltip is what turns a row of
        /// coloured shapes into an answer.
        /// </summary>
        private static void Enemies(Rect rect, FactionRow row, UIColorPaletteDef palette)
        {
            if (row.enemies == null || row.enemies.Count == 0)
            {
                TabParts.RowLabel(rect, "nobody", palette.TextDisabled, GameFont.Tiny, FactionsFaces.Mono,
                    FactionsFaces.Size.Label);

                return;
            }

            const float side = 18f;
            const float step = 21f;

            int fits = Mathf.Max(1, Mathf.FloorToInt((rect.width - 26f) / step));
            int drawn = Mathf.Min(fits, row.enemies.Count);

            float x = rect.x;
            float y = rect.y + (rect.height - side) * 0.5f;

            for (int i = 0; i < drawn; i++)
            {
                Faction other = row.enemies[i];

                Crest(new Rect(x, y, side, side), other, other.def?.FactionIcon, other.Color, other.defeated,
                    palette);

                x += step;
            }

            if (row.enemies.Count > drawn)
            {
                TabParts.RowLabel(new Rect(x, rect.y, rect.xMax - x, rect.height),
                    "+" + (row.enemies.Count - drawn), palette.TextDisabled, GameFont.Tiny,
                    FactionsFaces.Mono, FactionsFaces.Size.Label);
            }

            TooltipHandler.TipRegion(rect, EnemyNames(row));
        }

        private static string EnemyNames(FactionRow row)
        {
            string text = "At war with:";

            for (int i = 0; i < row.enemies.Count; i++)
                text += "\n  " + row.enemies[i].Name.CapitalizeFirst();

            return text;
        }

        private static void Bases(Rect rect, FactionRow row, UIColorPaletteDef palette)
        {
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = row.settlements == 0 ? palette.TextDisabled : palette.TextSecondary;

                UITextControl.LabelEllipses(rect, row.settlements == 0 ? "none" : row.settlements.ToString(),
                    FactionsFaces.Mono, FactionsFaces.Size.Figure);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }
        }

        /// <summary>
        /// Opens a faction's card and scrolls to it, for vanilla's own "show me this faction" call sites.
        ///
        /// The info card and the pawn table's faction icon both ask the factions tab to scroll to a faction.
        /// Since ours is what opens, ours is what has to answer.
        /// </summary>
        internal static void Reveal(Faction faction)
        {
            if (faction == null)
                return;

            opened = faction;
            filter = FilterAll;

            Search.Clear();

            // Left to the next frame's layout rather than computed here: the row's position depends on the
            // group heights, and those are only known once the list has been walked.
            scroll = Vector2.zero;
        }
    }
}
