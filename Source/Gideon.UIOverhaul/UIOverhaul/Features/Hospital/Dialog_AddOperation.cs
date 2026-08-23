using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>
    /// The operation picker: everything you could have done to one patient, and everything about the one you have
    /// chosen.
    ///
    /// <b>Two panes, laid out like the plant selector on the growing zones tab.</b> A searchable list of what is
    /// possible on the left, the whole of one choice on the right, and one button that commits. Choosing an
    /// operation is exactly the shape of question that arrangement was built for: many candidates, each with
    /// several facts worth reading before you pick.
    ///
    /// <b>The body part is a picker, not six near-identical rows.</b> RimWorld offers "install bionic eye (left
    /// eye)" and "install bionic eye (right eye)" as separate menu entries; they are one operation and a choice,
    /// and collapsing them is most of what makes this list readable.
    ///
    /// <b>The surgeon and the chance are shown before you commit,</b> which vanilla never does: you queue the bill
    /// and find out afterwards. The number is the one the game will roll against, and the note says plainly what
    /// it does not yet include, because a confident wrong number is worse than an honest partial one.
    ///
    /// <b>Add and pick another exists because operations come in sets.</b> Two eyes, an arm, and the removal that
    /// has to precede it. Closing and reopening the window for each is the friction this replaces.
    /// </summary>
    internal class Dialog_AddOperation : Window
    {
        private const float ListWidth = 330f;

        private const float Gutter = 12f;

        private const float HeaderHeight = 40f;

        private const float FooterHeight = 40f;

        private const float RowHeight = 46f;

        private const float ChipHeight = 22f;

        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search operations",
            Icon = TexButton.Search,
            MaxLength = 40
        };

        private readonly Pawn patient;

        private readonly List<HospitalOperation> options = new List<HospitalOperation>();

        private readonly List<HospitalOperation> shown = new List<HospitalOperation>();

        private readonly List<Pawn> surgeons = new List<Pawn>();

        private HospitalOperation selected;

        private BodyPartRecord part;

        /// <summary>Null means every kind. A chip sets it.</summary>
        private HospitalOperationKind? kind;

        /// <summary>Whether the list is showing only what could be done right now.</summary>
        private bool possibleOnly;

        private Vector2 listScroll;

        private Vector2 detailScroll;

        /// <summary>How tall the detail column came to last frame. Zero means it has not been measured.</summary>
        private float measuredDetail;

        private int queued;

        internal Dialog_AddOperation(Pawn patient)
        {
            this.patient = patient;

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(880f, 640f); }
        }

        protected override float Margin
        {
            get { return 12f; }
        }

        public override void PostOpen()
        {
            base.PostOpen();

            Search.Clear();

            Refresh();
        }

        /// <summary>Rebuilds the offer list and keeps the selection if it survived.</summary>
        private void Refresh()
        {
            RecipeDef was = selected != null ? selected.Recipe : null;
            BodyPartRecord wasPart = part;

            HospitalSurgery.Options(patient, options);

            selected = null;
            part = null;

            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Recipe != was)
                    continue;

                selected = options[i];

                break;
            }

            if (selected == null && options.Count > 0)
                selected = options[0];

            if (selected == null)
                return;

            if (wasPart != null && selected.Parts.Contains(wasPart))
                part = wasPart;
            else if (selected.Parts.Count > 0)
                part = selected.Parts[0];

            RefreshSurgeons();
        }

        private void RefreshSurgeons()
        {
            if (selected == null)
            {
                surgeons.Clear();

                return;
            }

            HospitalSurgery.Surgeons(patient.MapHeld, selected.Recipe, patient, surgeons);
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Hospital.AddOperation", inRect, () => Contents(inRect),
                "This window failed to draw. Nothing has been queued, and operations can still be added from the "
                + "pawn's own health tab.");
        }

        private void Contents(Rect inRect)
        {
            if (patient == null)
                return;

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Header(new Rect(inRect.x, inRect.y, inRect.width - 30f, HeaderHeight), palette);

                // Back to the body font before anything else draws, so no control below inherits Medium.
                Text.Font = GameFont.Small;

                Rect body = new Rect(inRect.x, inRect.y + HeaderHeight + 4f, inRect.width,
                    Mathf.Max(0f, inRect.height - HeaderHeight - FooterHeight - 12f));

                List(new Rect(body.x, body.y, ListWidth, body.height), palette);

                Detail(new Rect(body.x + ListWidth + Gutter, body.y,
                    Mathf.Max(0f, body.width - ListWidth - Gutter), body.height), palette);

                Footer(new Rect(inRect.x, inRect.yMax - FooterHeight + 6f, inRect.width, FooterHeight - 6f),
                    palette);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private void Header(Rect rect, UIColorPaletteDef palette)
        {
            Rect portrait = new Rect(rect.x, rect.y - 2f, 34f, 34f);

            PawnPortraitCell.Draw(portrait, patient, palette, palette.SurfaceSunken);

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Medium;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(portrait.xMax + 8f, rect.y, rect.width - portrait.width - 8f, 32f),
                    "An operation for " + patient.LabelShortCap);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }
        }

        // ---------------------------------------------------------------------------------------
        // Left: the list
        // ---------------------------------------------------------------------------------------

        private void List(Rect rect, UIColorPaletteDef palette)
        {
            Search.Draw(new Rect(rect.x, rect.y, rect.width, 26f), palette);

            Rect chips = new Rect(rect.x, rect.y + 30f, rect.width, ChipHeight);

            Chips(chips, palette);

            float y = chips.yMax + 6f;

            Filter();

            Rect list = new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y));
            Rect view = new Rect(0f, 0f, list.width - 18f, shown.Count * (RowHeight + 3f) + 4f);

            Widgets.BeginScrollView(list, ref listScroll, view);

            float at = 0f;

            for (int i = 0; i < shown.Count; i++)
            {
                Row(new Rect(0f, at, view.width, RowHeight), shown[i], palette);

                at += RowHeight + 3f;
            }

            Widgets.EndScrollView();

            if (shown.Count != 0)
                return;

            TabParts.Note(new Rect(list.x + 4f, list.y, list.width - 8f, 0f), list.y + 8f,
                options.Count == 0
                    ? "There is nothing that can be done to " + patient.LabelShortCap + " surgically."
                    : "Nothing matches. Clear the search, or turn off \"possible now\".", palette);
        }

        /// <summary>
        /// The kind chips and the one switch worth having beside them.
        ///
        /// <b>"Possible now" is off by default and that is the point of the list.</b> An operation whose part you
        /// do not have is a shopping list entry, and hiding it is exactly what vanilla does and what makes its
        /// menu unhelpful. The switch is there for the moment you have stopped shopping and want to act.
        /// </summary>
        private void Chips(Rect rect, UIColorPaletteDef palette)
        {
            float width = Mathf.Floor((rect.width - 4f * 3f) / 5f);
            float x = rect.x;

            Chip(new Rect(x, rect.y, width, rect.height), "All", kind == null, palette, () => kind = null);
            x += width + 4f;

            Chip(new Rect(x, rect.y, width, rect.height), "Medical",
                kind == HospitalOperationKind.Medical, palette, () => kind = HospitalOperationKind.Medical);

            x += width + 4f;

            Chip(new Rect(x, rect.y, width, rect.height), "Parts",
                kind == HospitalOperationKind.Prosthetic, palette,
                () => kind = HospitalOperationKind.Prosthetic);

            x += width + 4f;

            Chip(new Rect(x, rect.y, width, rect.height), "Implants",
                kind == HospitalOperationKind.Implant, palette, () => kind = HospitalOperationKind.Implant);

            x += width + 4f;

            Chip(new Rect(x, rect.y, rect.xMax - x, rect.height), "Removals",
                kind == HospitalOperationKind.Removal, palette, () => kind = HospitalOperationKind.Removal);
        }

        private void Chip(Rect rect, string label, bool on, UIColorPaletteDef palette, Action chosen)
        {
            TabParts.Segment(rect, label, on, palette, chosen);
        }

        private void Filter()
        {
            shown.Clear();

            for (int i = 0; i < options.Count; i++)
            {
                HospitalOperation option = options[i];

                if (kind.HasValue && option.Kind != kind.Value)
                    continue;

                if (possibleOnly && !option.Possible)
                    continue;

                if (!Search.IsEmpty && !Search.Matches(option.Label))
                    continue;

                shown.Add(option);
            }
        }

        /// <summary>
        /// One offer: what it is, whether it can be done, and the one line that matters about it.
        ///
        /// <b>The refusal is on the row's face rather than behind a hover.</b> "No bionic arm" said out loud is
        /// the difference between a menu and a shopping list, and it is the whole reason this shows operations
        /// RimWorld would have hidden.
        /// </summary>
        private void Row(Rect rect, HospitalOperation option, UIColorPaletteDef palette)
        {
            bool chosen = option == selected;

            // Composited, not handed over translucent: an outline is two fills, so a selection overlay given as
            // the inside lands on the accent border and fills the row almost solid with it.
            Color surface = chosen
                ? UIElementPainter.Composite(palette.PanelBackground, palette.SelectionOverlay)
                : Mouse.IsOver(rect)
                    ? palette.SurfaceRaised
                    : palette.PanelBackground;

            UIElementPainter.OutlineRounded(rect, chosen ? palette.Accent : palette.Border, surface);

            Rect inner = rect.ContractedBy(6f);

            string state = option.Reason.NullOrEmpty()
                ? option.Missing.Count == 0 ? "ready" : Shortfall(option)
                : option.Reason;

            Color stateColor = option.Possible ? palette.Success : palette.Warning;

            // Laid out right to left, and capped at two fifths of the row: the pill is sized from its own text,
            // and an uncapped one saying "no analgesic regeneration injector" was wider than the card it sat in.
            float pillWidth = TabParts.PillWidth(state, inner.width * 0.4f);
            float pillX = inner.xMax - pillWidth;

            TabParts.Pill(inner, pillX, inner.y, state, stateColor, palette, pillWidth, surface);

            TabParts.Line(new Rect(inner.x, inner.y, Mathf.Max(20f, pillX - inner.x - 6f), 0f), inner.y,
                option.Label, palette.TextPrimary);

            TabParts.Line(inner, inner.y + UIFonts.LineHeightOf(GameFont.Small), Subline(option),
                palette.TextDisabled, GameFont.Tiny);

            if (!Widgets.ButtonInvisible(rect) || chosen)
                return;

            selected = option;
            part = option.Parts.Count > 0 ? option.Parts[0] : null;

            RefreshSurgeons();
        }

        /// <summary>
        /// What is short, in as few words as the row can be read in.
        ///
        /// <b>The thing is not named twice.</b> "Administer acetaminophen" beside a pill reading "no
        /// acetaminophen" says the same word twice on one line and costs the label the room it needed; when the
        /// missing thing is already in the title, the pill only has to say there is none.
        /// </summary>
        private static string Shortfall(HospitalOperation option)
        {
            if (option.Missing.Count != 1)
                return "missing " + option.Missing.Count;

            string missing = option.Missing[0].label;

            if (option.Label != null && missing != null
                                     && option.Label.IndexOf(missing, StringComparison.OrdinalIgnoreCase) >= 0)
                return "none in stock";

            return "no " + missing;
        }

        private string Subline(HospitalOperation option)
        {
            if (option.Parts.Count == 1)
                return option.Parts[0].LabelCap;

            if (option.Parts.Count > 1)
                return option.Parts.Count + " possible places";

            RecipeDef recipe = option.Recipe;

            return recipe != null && !recipe.description.NullOrEmpty()
                ? recipe.description
                : "Affects the whole body.";
        }

        // ---------------------------------------------------------------------------------------
        // Right: the chosen operation
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Everything about one operation, in a scroll view whose height is remembered rather than predicted.
        ///
        /// <b>Remembered, because a formula for how tall this comes to is wrong the first time a block is added
        /// and fails silently.</b> Three panels in this mod clipped their last rows that way before the pattern
        /// changed to measuring the previous draw.
        /// </summary>
        private void Detail(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            if (selected == null)
                return;

            Rect inner = rect.ContractedBy(10f);

            Rect view = new Rect(0f, 0f, inner.width - 18f,
                measuredDetail > 0f ? measuredDetail : inner.height);

            Widgets.BeginScrollView(inner, ref detailScroll, view);

            Rect column = new Rect(0f, 0f, view.width, view.height);
            float y = 0f;

            y = TabParts.Line(column, y, selected.Label, palette.TextPrimary, GameFont.Medium);

            RecipeDef recipe = selected.Recipe;

            if (recipe != null && !recipe.description.NullOrEmpty())
                y = TabParts.Note(column, y + 2f, recipe.description, palette) + TabParts.BlockGap;
            else
                y += TabParts.BlockGap;

            y = Where(column, y, palette);
            y = Needs(column, y, palette);
            y = Surgeon(column, y, palette);
            y = Warnings(column, y, palette);

            measuredDetail = y + 8f;

            Widgets.EndScrollView();
        }

        /// <summary>The body part, as a picker, which is the whole reason six eye rows became one.</summary>
        private float Where(Rect column, float y, UIColorPaletteDef palette)
        {
            if (selected.Parts.Count == 0)
                return y;

            y = TabParts.Heading(column, y, "WHERE", palette);

            float width = Mathf.Min(160f, column.width);
            float x = column.x;
            float rowTop = y;

            for (int i = 0; i < selected.Parts.Count; i++)
            {
                BodyPartRecord candidate = selected.Parts[i];

                if (x + width > column.xMax)
                {
                    x = column.x;
                    rowTop += 26f;
                }

                BodyPartRecord captured = candidate;

                TabParts.Segment(new Rect(x, rowTop, width - 4f, 24f), candidate.LabelCap,
                    candidate == part, palette, () => part = captured);

                x += width;
            }

            y = rowTop + 26f;

            if (part != null)
                y = TabParts.Note(column, y + 2f, PartNote(part), palette);

            return y + TabParts.BlockGap;
        }

        /// <summary>What is already wrong with the part being operated on, since that is why you picked it.</summary>
        private string PartNote(BodyPartRecord record)
        {
            return UIGuard.Try<string>("Hospital.PartNote", () =>
            {
                float health = patient.health.hediffSet.GetPartHealth(record);
                float max = record.def.GetMaxHealth(patient);

                if (health >= max)
                    return patient.LabelShortCap + "'s " + record.Label + " is undamaged.";

                return patient.LabelShortCap + "'s " + record.Label + " is at "
                       + Mathf.RoundToInt(health) + " of " + Mathf.RoundToInt(max) + ".";
            }, null, null);
        }

        /// <summary>Have against need, per ingredient, which is what turns a refusal into a shopping list.</summary>
        private float Needs(Rect column, float y, UIColorPaletteDef palette)
        {
            RecipeDef recipe = selected.Recipe;

            if (recipe == null || recipe.ingredients == null || recipe.ingredients.Count == 0)
                return y;

            y = TabParts.Heading(column, y,
                selected.Missing.Count == 0 ? "NEEDS: ALL IN STOCK" : "NEEDS", palette);

            Map map = patient.MapHeld;

            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                IngredientCount ingredient = recipe.ingredients[i];

                if (ingredient == null)
                    continue;

                ThingDef def = Cheapest(ingredient, map);

                if (def == null)
                    continue;

                int want = Mathf.CeilToInt(ingredient.GetBaseCount());
                int have = HospitalSurgery.Stock(map, def);

                // The name lane stops where the count starts. Handing both the full width and drawing one of them
                // right-aligned lets a long ingredient name run underneath its own figure.
                string count = have + " of " + want;

                TabParts.Line(
                    new Rect(column.x, y, Mathf.Max(30f, column.width - UIRichText.WidthOf(count) - 6f), 0f), y,
                    def.LabelCap, palette.TextSecondary, GameFont.Tiny);

                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;

                try
                {
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.UpperRight;
                    GUI.color = have >= want ? palette.Success : palette.Danger;

                    Widgets.Label(new Rect(column.x, y, column.width, UIFonts.LineHeightOf(GameFont.Tiny)),
                        count);
                }
                finally
                {
                    GUI.color = previousColor;
                    Text.Anchor = previousAnchor;
                    Text.Font = previousFont;
                }

                y += UIFonts.LineHeightOf(GameFont.Tiny) + TabParts.RowGap;
            }

            return y + TabParts.BlockGap;
        }

        /// <summary>
        /// The one ingredient worth naming out of a filter that may allow many.
        ///
        /// A recipe asking for "medicine" allows three kinds; naming the one the colony has most of is the honest
        /// answer to "what will this use", and naming the first def in an unordered filter would be arbitrary.
        /// </summary>
        private static ThingDef Cheapest(IngredientCount ingredient, Map map)
        {
            return UIGuard.Try<ThingDef>("Hospital.Ingredient", () =>
            {
                ThingDef best = null;
                int most = -1;

                foreach (ThingDef def in ingredient.filter.AllowedThingDefs)
                {
                    int have = HospitalSurgery.Stock(map, def);

                    if (best != null && have <= most)
                        continue;

                    best = def;
                    most = have;
                }

                return best;
            }, null, null);
        }

        /// <summary>
        /// Who could do it and how likely each of them is to get it right.
        ///
        /// <b>The number is the one the game rolls against,</b> not a guess: it is the quality the surgery outcome
        /// effect computes, which is exactly what the first outcome in the list succeeds on. The note says what it
        /// cannot yet know.
        /// </summary>
        private float Surgeon(Rect column, float y, UIColorPaletteDef palette)
        {
            int required = HospitalSurgery.RequiredSkill(selected.Recipe);

            y = TabParts.Heading(column, y,
                required > 0 ? "SURGEON: MEDICINE " + required + " REQUIRED" : "SURGEON", palette);

            if (surgeons.Count == 0)
                return TabParts.Note(column, y,
                           required > 0
                               ? "Nobody here has Medicine " + required + ". The bill can still be queued and will "
                                 + "wait."
                               : "Nobody here can operate.", palette, GameFont.Tiny, palette.Danger)
                       + TabParts.BlockGap;

            int shownCount = Mathf.Min(surgeons.Count, 4);

            for (int i = 0; i < shownCount; i++)
            {
                Pawn surgeon = surgeons[i];
                float chance = HospitalSurgery.ChanceFor(selected.Recipe, surgeon, patient, part);

                string reading = "skill " + HospitalSurgery.SkillOf(surgeon) + " - "
                                 + Mathf.RoundToInt(chance * 100f) + "%";

                // The name lane stops where the reading starts, so a long name cannot run underneath its own
                // number.
                TabParts.Line(
                    new Rect(column.x, y, Mathf.Max(30f, column.width - UIRichText.WidthOf(reading) - 6f), 0f), y,
                    surgeon.LabelShortCap, palette.TextSecondary, GameFont.Tiny);

                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;

                try
                {
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.UpperRight;
                    GUI.color = chance >= 0.9f ? palette.Success
                        : chance >= 0.6f ? palette.Warning : palette.Danger;

                    Widgets.Label(new Rect(column.x, y, column.width, UIFonts.LineHeightOf(GameFont.Tiny)),
                        reading);
                }
                finally
                {
                    GUI.color = previousColor;
                    Text.Anchor = previousAnchor;
                    Text.Font = previousFont;
                }

                y += UIFonts.LineHeightOf(GameFont.Tiny) + TabParts.RowGap;
            }

            if (surgeons.Count > shownCount)
                y = TabParts.Note(column, y, (surgeons.Count - shownCount) + " others could also do it.",
                    palette);

            y = TabParts.Note(column, y + 2f,
                "The chance is the surgeon's own stat against this operation, with the bed they are lying in "
                + "counted. The medicine is not: it is chosen when the doctor arrives, and glitterworld will beat "
                + "this number while herbal will fall short of it.", palette);

            return y + TabParts.BlockGap;
        }

        /// <summary>The things worth saying out loud before somebody commits.</summary>
        private float Warnings(Rect column, float y, UIColorPaletteDef palette)
        {
            List<string> notes = new List<string>();

            UIGuard.Try("Hospital.Warnings", () =>
            {
                if (!patient.InBed())
                    notes.Add(patient.LabelShortCap
                              + " is not in a bed. Surgery outside one is more likely to fail.");
                else if (patient.CurrentBed() != null && !patient.CurrentBed().Medical)
                    notes.Add(patient.LabelShortCap
                              + " is in an ordinary bed. A medical bed would improve the odds.");

                if (!selected.Reason.NullOrEmpty())
                    notes.Add(selected.Reason);

                for (int i = 0; i < selected.Missing.Count; i++)
                    notes.Add("There is no " + selected.Missing[i].label + " on this map.");

                TaggedString confirmation = selected.Recipe.Worker.GetConfirmation(patient);

                if (!confirmation.NullOrEmpty())
                    notes.Add(confirmation);

                if (patient.Faction != null && !patient.Faction.IsPlayer && !patient.Faction.Hidden
                    && !patient.Faction.HostileTo(Faction.OfPlayer)
                    && selected.Recipe.Worker.IsViolationOnPawn(patient, part, Faction.OfPlayer))
                    notes.Add("This will anger " + patient.Faction.Name + ".");
            }, null);

            if (notes.Count == 0)
                return y;

            y = TabParts.Heading(column, y, "WATCH OUT", palette);

            for (int i = 0; i < notes.Count; i++)
                y = TabParts.Note(column, y, notes[i], palette, GameFont.Tiny, palette.Warning)
                    + TabParts.RowGap;

            return y + TabParts.BlockGap;
        }

        // ---------------------------------------------------------------------------------------
        // Footer
        // ---------------------------------------------------------------------------------------

        private void Footer(Rect rect, UIColorPaletteDef palette)
        {
            bool only = possibleOnly;

            if (UICheckboxControl.Draw(new Rect(rect.x, rect.y, 180f, 30f), ref only, palette, "Possible now"))
                possibleOnly = only;

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(rect.x + 190f, rect.y + 8f, 220f, 24f),
                    queued == 0 ? string.Empty : "queued for " + patient.LabelShortCap + ": " + queued);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }

            bool can = selected != null && selected.Possible;

            string refusal = selected == null
                ? "Nothing is selected."
                : selected.Possible
                    ? null
                    : selected.Reason.NullOrEmpty()
                        ? "Something this operation needs is not on the map."
                        : selected.Reason;

            if (TabParts.Button(new Rect(rect.xMax - 300f, rect.y, 145f, 30f), "Add and pick another",
                    palette, can, false, refusal))
                Commit(false);

            if (TabParts.Button(new Rect(rect.xMax - 150f, rect.y, 150f, 30f), "Add operation", palette, can,
                    true, refusal))
                Commit(true);
        }

        private void Commit(bool close)
        {
            if (selected == null)
                return;

            HospitalSurgery.Queue(patient, selected.Recipe, part);

            queued++;

            if (close)
            {
                Close();

                return;
            }

            // The offer list changes the moment a bill exists: an operation on a part that is now spoken for is
            // no longer offered, and re-reading is cheaper than reasoning about which rows survived.
            Refresh();
        }
    }
}
