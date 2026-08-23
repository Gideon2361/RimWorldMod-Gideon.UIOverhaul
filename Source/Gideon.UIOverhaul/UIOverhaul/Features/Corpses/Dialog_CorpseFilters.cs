using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Corpses
{
    /// <summary>
    /// The corpses tab's filters, all six in one window.
    ///
    /// <b>Checkbox lists with a search rather than menus,</b> which is the standing rule across this mod: a set of
    /// defs is a list that shows every option, which is selected, and how many there are. A float menu could show
    /// none of the three, and three multi-select menus would be three menus to open, read and close for one
    /// question.
    ///
    /// <b>Traits are one list with three states, not two lists.</b> Require and exclude are the same set of trait
    /// defs asked about twice, so a trait's row cycles ignored, required, excluded. Two lists would put every
    /// trait on screen twice and make "required in one, excluded in the other" a state a player could reach.
    ///
    /// <b>Every change takes effect at once.</b> There is no Apply: the tab behind this window is the preview, and
    /// a filter you have to commit is a filter you cannot feel out.
    /// </summary>
    internal sealed class Dialog_CorpseFilters : Window
    {
        private const float HeaderHeight = 30f;

        private const float TopRowHeight = 58f;

        private const float FooterHeight = 34f;

        private const float RowHeight = 24f;

        /// <summary>Taller than a name row, because a skill's row carries a slider as well as a name.</summary>
        private const float SkillRowHeight = 32f;

        /// <summary>
        /// How wide the skills column asks to be, and how much of its row the level slider takes.
        ///
        /// The slider is the part that cannot shrink: it is dragged, and it draws "6 - 20" above its own track, so
        /// below about this width the two thumbs meet under the label. What is left over is the name's, which is
        /// why the name is set at Tiny -- "Construction" at Small would not fit beside a slider that has to be
        /// this wide.
        /// </summary>
        private const float SkillColumnWidth = 300f;

        private const float SkillRangeWidth = 138f;

        /// <summary>The passion button, square.</summary>
        private const float PassionSize = 24f;

        /// <summary>
        /// Where the skill sliders' ids start.
        ///
        /// Each row's id is this plus the def's index, so the range is this to this plus 65535. The age slider
        /// above sits at 8,413,771, which is below the base and cannot collide however many skills are loaded.
        /// </summary>
        private const int SkillRangeId = 8_414_000;

        private const float Pad = 10f;

        private static readonly string[] Sexes = { "Either", "Male", "Female" };

        private readonly UITextBoxControl xenoSearch = new UITextBoxControl
        {
            Placeholder = "Search", Icon = TexButton.Search, MaxLength = 30
        };

        private readonly UITextBoxControl traitSearch = new UITextBoxControl
        {
            Placeholder = "Search", Icon = TexButton.Search, MaxLength = 30
        };

        private readonly List<Faction> factions = new List<Faction>();

        private Vector2 xenoScroll;

        private Vector2 traitScroll;

        private Vector2 factionScroll;

        private Vector2 skillScroll;

        internal Dialog_CorpseFilters()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            draggable = true;
            drawShadow = true;
        }

        internal static void Open()
        {
            Window existing = Find.WindowStack.WindowOfType<Dialog_CorpseFilters>();

            if (existing != null)
            {
                existing.Close(false);

                return;
            }

            Find.WindowStack.Add(new Dialog_CorpseFilters());
        }

        /// <summary>
        /// Wide enough for four lists side by side, and never wider than the screen.
        ///
        /// It was 760 by 560 with three lists. The skills column arrived on 2026-08-23 and is the widest of the
        /// four -- a row of it carries a name, a passion and a range where the others carry a name -- so the window
        /// grew by rather more than a quarter. Capped against the screen because a filter window that opens with
        /// its footer off the bottom cannot be closed by the button that closes it.
        /// </summary>
        public override Vector2 InitialSize
        {
            get
            {
                return new Vector2(Mathf.Min(1060f, UI.screenWidth - 40f),
                    Mathf.Min(620f, UI.screenHeight - 80f));
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Corpses.Filters", inRect, () => Contents(inRect),
                "The filter window could not finish drawing. Press Clear all if the list looks wrong.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Medium;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, HeaderHeight), "Filters");

                Text.Font = GameFont.Small;

                float y = inRect.y + HeaderHeight + 4f;

                Top(new Rect(inRect.x, y, inRect.width, TopRowHeight), palette);

                y += TopRowHeight + Pad;

                Rect columns = new Rect(inRect.x, y, inRect.width,
                    Mathf.Max(60f, inRect.yMax - FooterHeight - Pad - y));

                // The skills column is wider than the other three because its rows carry three controls rather
                // than one. Taken off the top and the rest shared, so the three name lists stay equal to each
                // other -- three equal columns and one wide one reads as a layout, where four different widths
                // reads as an accident.
                float skills = Mathf.Min(SkillColumnWidth, Mathf.Floor(columns.width * 0.4f));
                float width = Mathf.Floor((columns.width - skills - Pad * 3f) / 3f);

                Xenotypes(new Rect(columns.x, columns.y, width, columns.height), palette);

                Traits(new Rect(columns.x + width + Pad, columns.y, width, columns.height), palette);

                Factions(new Rect(columns.x + (width + Pad) * 2f, columns.y, width, columns.height), palette);

                Skills(new Rect(columns.x + (width + Pad) * 3f, columns.y, skills, columns.height), palette);

                Footer(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight), palette);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }
        }

        // ---------------------------------------------------------------------------------------
        // Sex and age
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The two filters that are one value each, side by side above the three lists.
        ///
        /// Above rather than in a fourth column, because a segmented control and a range slider are read in one
        /// glance while a checkbox list is read by scrolling, and mixing the two kinds down a row of columns makes
        /// the short ones look unfinished.
        /// </summary>
        private void Top(Rect rect, UIColorPaletteDef palette)
        {
            float half = Mathf.Floor((rect.width - Pad) * 0.5f);

            Rect sex = new Rect(rect.x, rect.y, half, rect.height);

            Caption(sex, "sex", palette);

            // Capped, and the leftover left as air. The window grew when the skills column arrived and half of it
            // is now five hundred pixels: three segments sharing that would be a hundred and seventy each to hold
            // the word "Female", which reads as three buttons waiting for something longer to say.
            float segment = Mathf.Min(112f, Mathf.Floor((sex.width - 6f) / 3f));

            int current = !CorpseFilter.Sex.HasValue ? 0 : CorpseFilter.Sex.Value == Gender.Male ? 1 : 2;

            for (int i = 0; i < 3; i++)
            {
                Rect slot = new Rect(sex.x + i * (segment + 3f), sex.y + 20f, segment, 26f);

                int index = i;

                TabParts.Segment(slot, Sexes[i], current == i, palette, () =>
                {
                    CorpseFilter.Sex = index == 0
                        ? (Gender?) null
                        : index == 1
                            ? Gender.Male
                            : Gender.Female;

                    CorpseRoster.Invalidate();
                });
            }

            Rect age = new Rect(rect.x + half + Pad, rect.y, half, rect.height);

            IntRange was = CorpseFilter.Age;

            Caption(age, was.min <= CorpseFilter.MinAge && was.max >= CorpseFilter.MaxAge
                ? "age, any"
                : "age " + was.min + " to " + was.max, palette);

            IntRange range = was;

            // The id is a constant of ours rather than a hash of the rect: RimWorld's range sliders key their drag
            // state on it, and an id that moved with the layout would drop the drag the moment the window resized.
            Widgets.IntRange(new Rect(age.x, age.y + 22f, age.width, 26f), 8_413_771, ref range,
                CorpseFilter.MinAge, CorpseFilter.MaxAge);

            if (range.min == was.min && range.max == was.max)
                return;

            CorpseFilter.Age = range;

            CorpseRoster.Invalidate();
        }

        // ---------------------------------------------------------------------------------------
        // The three lists
        // ---------------------------------------------------------------------------------------

        private void Xenotypes(Rect column, UIColorPaletteDef palette)
        {
            Rect list = Column(column, "xenotype, any of", CorpseFilter.Xenotypes.Count, xenoSearch, palette);

            List<XenotypeDef> all = new List<XenotypeDef>();

            UIGuard.Try("Corpses.FilterXeno", () =>
            {
                List<XenotypeDef> defs = DefDatabase<XenotypeDef>.AllDefsListForReading;

                for (int i = 0; i < defs.Count; i++)
                {
                    if (xenoSearch.IsEmpty || xenoSearch.Matches(defs[i].LabelCap))
                        all.Add(defs[i]);
                }

                all.Sort((a, b) => string.Compare(a.LabelCap, b.LabelCap, System.StringComparison.Ordinal));
            }, null);

            Rect view = new Rect(0f, 0f, list.width - 18f, all.Count * RowHeight + 4f);

            Widgets.BeginScrollView(list, ref xenoScroll, view);

            float y = 0f;

            for (int i = 0; i < all.Count; i++)
            {
                XenotypeDef def = all[i];

                bool on = CorpseFilter.Xenotypes.Contains(def);

                bool was = on;

                if (UICheckboxControl.Draw(new Rect(0f, y, view.width, RowHeight - 2f), ref on, palette,
                        def.LabelCap) && on != was)
                    CorpseFilter.Toggle(def);

                y += RowHeight;
            }

            Widgets.EndScrollView();

            Empty(list, all.Count, "No xenotypes.", palette);
        }

        /// <summary>
        /// Traits, each cycling through ignored, required and excluded.
        ///
        /// The row's own colour is the state: nothing, the accent for required, the warning colour for excluded.
        /// A checkbox could only have said two of the three.
        /// </summary>
        private void Traits(Rect column, UIColorPaletteDef palette)
        {
            int used = 0;

            foreach (KeyValuePair<TraitDef, TraitFilterState> pair in CorpseFilter.Traits)
            {
                if (pair.Value != TraitFilterState.Ignored)
                    used++;
            }

            Rect list = Column(column, "traits, all of", used, traitSearch, palette);

            List<TraitDef> all = new List<TraitDef>();

            UIGuard.Try("Corpses.FilterTraits", () =>
            {
                List<TraitDef> defs = DefDatabase<TraitDef>.AllDefsListForReading;

                for (int i = 0; i < defs.Count; i++)
                {
                    TraitDef def = defs[i];

                    if (def.degreeDatas == null || def.degreeDatas.Count == 0)
                        continue;

                    if (traitSearch.IsEmpty || traitSearch.Matches(Label(def)))
                        all.Add(def);
                }

                all.Sort((a, b) => string.Compare(Label(a), Label(b), System.StringComparison.Ordinal));
            }, null);

            Rect view = new Rect(0f, 0f, list.width - 18f, all.Count * RowHeight + 4f);

            Widgets.BeginScrollView(list, ref traitScroll, view);

            float y = 0f;

            for (int i = 0; i < all.Count; i++)
                y = Trait(view, y, all[i], palette);

            Widgets.EndScrollView();

            Empty(list, all.Count, "No traits.", palette);
        }

        private static string Label(TraitDef def)
        {
            return UIGuard.Try<string>("Corpses.TraitLabel",
                () => def.degreeDatas[0].label.CapitalizeFirst(), def.defName, null);
        }

        private static float Trait(Rect view, float y, TraitDef def, UIColorPaletteDef palette)
        {
            Rect row = new Rect(0f, y, view.width, RowHeight - 2f);

            TraitFilterState state = CorpseFilter.StateOf(def);

            bool over = Mouse.IsOver(row);

            if (state != TraitFilterState.Ignored)
                UIElementPainter.FillRounded(row,
                    state == TraitFilterState.Required ? palette.AccentMuted : palette.Warning);
            else if (over)
                UIElementPainter.FillRounded(row, palette.SurfaceRaised);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;

                GUI.color = state == TraitFilterState.Ignored ? palette.TextPrimary : palette.WindowBackground;

                UIRichText.Label(new Rect(row.x + 6f, row.y, Mathf.Max(20f, row.width - 70f), row.height),
                    Label(def));

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;

                UIRichText.Label(new Rect(row.xMax - 64f, row.y, 60f, row.height),
                    state == TraitFilterState.Required
                        ? "must have"
                        : state == TraitFilterState.Excluded
                            ? "must not"
                            : string.Empty);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            TooltipHandler.TipRegion(row,
                (TipSignal) "Click to step through ignored, must have, and must not have.");

            if (Widgets.ButtonInvisible(row))
                CorpseFilter.Cycle(def);

            return row.yMax + 2f;
        }

        /// <summary>
        /// Every skill, each with a passion requirement and a level range.
        ///
        /// <b>Asked for on 2026-08-23:</b> "let people select whether a passion in a skill is required and also
        /// allow them to set a min/max for each skill if desired." Which is the question you ask when you have one
        /// resurrector serum and four bodies: the one worth spending it on is the one with the levels and the
        /// passion, and until now the tab could show you that a column at a time but not filter on it.
        ///
        /// <b>Every skill is on screen rather than added one at a time.</b> Twelve rows fit, so there is nothing to
        /// pick from a list first: the row is either asking something or it is not, and a skill asking nothing
        /// costs a line of grey text. That is also why there is no search box over this column when the other two
        /// def lists have one -- twelve named rows in a fixed order are read, not searched.
        ///
        /// <b>The passion control has three states, one more than was asked for.</b> Ignored and required were the
        /// request; burning-only is the third click of the same control, and it is the state somebody filtering
        /// for a crafter actually wants. Said plainly here because it is a liberty: two states would have been
        /// what was asked for.
        /// </summary>
        private void Skills(Rect column, UIColorPaletteDef palette)
        {
            Rect list = Column(column, "skills, all of", CorpseFilter.SkillCount, null, palette);

            List<SkillDef> all = new List<SkillDef>();

            UIGuard.Try("Corpses.FilterSkills", () =>
            {
                List<SkillDef> defs = DefDatabase<SkillDef>.AllDefsListForReading;

                for (int i = 0; i < defs.Count; i++)
                    all.Add(defs[i]);

                // The game's own order, which is the order every skill list in the game is in -- the character
                // card first of all. Sorting these alphabetically would put Animals at the top and make a player
                // hunt for a row whose position they already know.
                //
                // Descending, which is what vanilla does and is not obvious: Shooting carries listOrder 120 and
                // Crafting 10, so ascending would put the list upside down. SkillUI caches this same sort in a
                // private field, so the sort is repeated here rather than borrowed.
                all.Sort((a, b) => b.listOrder.CompareTo(a.listOrder));
            }, null);

            Rect view = new Rect(0f, 0f, list.width - 18f, all.Count * SkillRowHeight + 4f);

            Widgets.BeginScrollView(list, ref skillScroll, view);

            float y = 0f;

            for (int i = 0; i < all.Count; i++)
                y = Skill(view, y, all[i], palette);

            Widgets.EndScrollView();

            Empty(list, all.Count, "No skills.", palette);
        }

        /// <summary>
        /// One skill's row: its name, its passion requirement, and the levels that pass.
        ///
        /// <b>The range slider carries its own numbers,</b> which is why there is no separate readout: it draws
        /// "6 - 20" above its own track. Adding one of ours would have said the same thing twice and cost the
        /// name forty pixels.
        ///
        /// <b>The slider's id is derived from the def,</b> not from the loop counter and not from the rect.
        /// RimWorld keys a range slider's drag state on that id, so an id that moved -- because the list scrolled,
        /// or because a mod's skill sorted differently -- would drop the drag mid-gesture and, worse, let two rows
        /// share one. <c>Def.index</c> is unique within a def type and stable for the session.
        /// </summary>
        private static float Skill(Rect view, float y, SkillDef def, UIColorPaletteDef palette)
        {
            Rect row = new Rect(0f, y, view.width, SkillRowHeight - 2f);

            SkillFilter filter = CorpseFilter.StateOf(def);

            bool over = Mouse.IsOver(row);

            if (filter.Active)
                UIElementPainter.FillRounded(row, palette.SurfaceRaised);
            else if (over)
                UIElementPainter.FillRounded(row, palette.SurfaceSunken);

            Rect range = new Rect(row.xMax - SkillRangeWidth, row.center.y - 13f, SkillRangeWidth, 26f);
            Rect flame = new Rect(range.x - PassionSize - 6f, row.center.y - PassionSize * 0.5f, PassionSize,
                PassionSize);
            Rect name = new Rect(row.x + 5f, row.y, Mathf.Max(20f, flame.x - row.x - 9f), row.height);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = filter.Active ? palette.TextPrimary : palette.TextSecondary;

                UIRichText.Label(name, def.skillLabel.NullOrEmpty()
                    ? def.LabelCap.ToString()
                    : def.skillLabel.CapitalizeFirst());
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            Passion(flame, def, filter, palette);

            IntRange levels = filter.Level;
            IntRange was = levels;

            Widgets.IntRange(range, SkillRangeId + def.index, ref levels, SkillRecord.MinLevel,
                SkillRecord.MaxLevel);

            // Restored because IntRange leaves the ambient colour it drew its own label in behind, and the next
            // row would inherit it.
            GUI.color = previousColor;

            if (levels.min != was.min || levels.max != was.max)
            {
                filter.Level = levels;

                CorpseFilter.Set(def, filter);
            }

            return row.yMax + 2f;
        }

        /// <summary>
        /// The passion requirement, as the game's own flame in three states.
        ///
        /// Filled when it is asking something, in the same two colours the trait rows use for their two states:
        /// the accent for a condition and the warning colour for the stricter one. Grey and hollow means the
        /// question is not being asked, which is not the same as "must have no passion" -- there is no such filter,
        /// because nobody has ever wanted one.
        /// </summary>
        private static void Passion(Rect box, SkillDef def, SkillFilter filter,
            UIColorPaletteDef palette)
        {
            bool set = filter.Passion != PassionFilterState.Ignored;
            bool burning = filter.Passion == PassionFilterState.Major;

            if (set)
                UIElementPainter.FillRounded(box, burning ? palette.Warning : palette.AccentMuted);
            else if (Mouse.IsOver(box))
                UIElementPainter.FillRounded(box, palette.SurfaceRaised);

            Texture2D icon = burning ? SkillUI.PassionMajorIcon : SkillUI.PassionMinorIcon;

            if (icon != null)
            {
                Color previous = GUI.color;

                GUI.color = set ? palette.WindowBackground : palette.TextDisabled;

                GUI.DrawTexture(new Rect(box.center.x - 7f, box.center.y - 7f, 14f, 14f), icon);

                GUI.color = previous;
            }

            if (Mouse.IsOver(box))
            {
                string state = burning
                    ? "Must have a burning passion for this skill."
                    : set
                        ? "Must have some passion for this skill."
                        : "Passion is not being asked about.";

                TooltipHandler.TipRegion(box, (TipSignal) (state
                    + "\n\nClick to step through: not asked, any passion, burning only."));
            }

            if (Widgets.ButtonInvisible(box))
                CorpseFilter.CyclePassion(def);
        }

        private void Factions(Rect column, UIColorPaletteDef palette)
        {
            Rect list = Column(column, "faction, any of", CorpseFilter.Factions.Count, null, palette);

            CorpseFilter.FactionsPresent(factions);

            Rect view = new Rect(0f, 0f, list.width - 18f, factions.Count * RowHeight + 4f);

            Widgets.BeginScrollView(list, ref factionScroll, view);

            float y = 0f;

            for (int i = 0; i < factions.Count; i++)
            {
                Faction faction = factions[i];

                bool on = CorpseFilter.Factions.Contains(faction);

                bool was = on;

                if (UICheckboxControl.Draw(new Rect(0f, y, view.width, RowHeight - 2f), ref on, palette,
                        faction.Name) && on != was)
                    CorpseFilter.Toggle(faction);

                y += RowHeight;
            }

            Widgets.EndScrollView();

            Empty(list, factions.Count,
                "Nobody on the map belongs to a faction.", palette);
        }

        // ---------------------------------------------------------------------------------------
        // Furniture
        // ---------------------------------------------------------------------------------------

        /// <summary>A column's heading, its count, an optional search box, and the rect the list gets.</summary>
        private static Rect Column(Rect column, string heading, int count, UITextBoxControl search,
            UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(column, palette.PanelBackground);

            Rect inner = column.ContractedBy(6f);

            Caption(inner, count > 0 ? heading + "  (" + count + ")" : heading, palette);

            float y = inner.y + 20f;

            if (search != null)
            {
                search.Draw(new Rect(inner.x, y, inner.width, 24f), palette);

                y += 28f;
            }

            return new Rect(inner.x, y, inner.width, Mathf.Max(20f, inner.yMax - y));
        }

        private static void Caption(Rect rect, string text, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;
                GUI.color = palette.TextDisabled;

                UIRichText.Label(new Rect(rect.x + 2f, rect.y, Mathf.Max(20f, rect.width - 4f),
                    UIFonts.LineHeightOf(GameFont.Tiny)), text);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Font = previousFont;
            }
        }

        private static void Empty(Rect list, int count, string text, UIColorPaletteDef palette)
        {
            if (count > 0)
                return;

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(list.x + 2f, list.y + 2f, list.width - 4f, 40f), text);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }
        }

        private void Footer(Rect rect, UIColorPaletteDef palette)
        {
            int active = CorpseFilter.Count;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = active > 0 ? palette.TextSecondary : palette.TextDisabled;

                Widgets.Label(new Rect(rect.x, rect.y, rect.width - 240f, rect.height),
                    active == 0
                        ? "Nothing filtered."
                        : active + (active == 1 ? " filter set." : " filters set."));
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (TabParts.Button(new Rect(rect.xMax - 216f, rect.y, 110f, 28f), "Clear all", palette, active > 0,
                    false, "Puts every filter back to showing everything."))
                CorpseFilter.Clear();

            if (TabParts.Button(new Rect(rect.xMax - 100f, rect.y, 100f, 28f), "Close", palette))
                Close();
        }
    }
}
