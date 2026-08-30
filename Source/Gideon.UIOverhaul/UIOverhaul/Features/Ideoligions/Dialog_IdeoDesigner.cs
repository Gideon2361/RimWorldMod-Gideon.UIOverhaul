using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Ideoligions
{
    /// <summary>
    /// The screen you build a faith in.
    ///
    /// <b>Every control here is ours.</b> Not one <c>IdeoUIUtility.Do*</c> call, and no vanilla window ever opens
    /// on top: the meme grid, the doctrine picker and the review are drawn from the same palette and controls as
    /// the rest of the mod. That is the whole reason for taking this screen rather than dressing vanilla's --
    /// borrowing the drawing would have meant vanilla's 122x120 meme boxes inside our frame and
    /// <c>Dialog_ChooseMemes</c> opening over the top of it, which is the seam this mod exists to remove.
    ///
    /// <b>The commit is still RimWorld's.</b> <see cref="IdeoDraft"/> ends at
    /// <c>IdeoDevelopmentUtility.ConfirmChangesToIdeo</c> and <c>ApplyChangesToIdeo</c>, exactly as vanilla's own
    /// dialog does. Owning the drawing and owning the write are different things, and only the first is worth
    /// doing here.
    ///
    /// <b>Consequences sit beside the choice rather than after it.</b> Gains, requires, forbids and rules out are
    /// all readable from <c>MemeDef</c> before anything is committed, and the colony diff answers the question
    /// the mockup was built around: and then what happens to the colony I already have.
    /// </summary>
    public class Dialog_IdeoDesigner : Window
    {
        private enum Step
        {
            Memes,
            Doctrine,
            Review
        }

        private readonly IdeoDraft draft;

        private Step step = Step.Memes;
        private Vector2 mainScroll;
        private float mainHeight = 1f;
        private IssueDef issue;

        internal Dialog_IdeoDesigner(IdeoDraft draft)
        {
            this.draft = draft;

            forcePause = true;
            doCloseX = false;
            doCloseButton = false;
            absorbInputAroundWindow = true;
            forceCatchAcceptAndCancelEventEvenIfUnfocused = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(1180f, Mathf.Min(860f, UI.screenHeight - 60f)); }
        }

        protected override float Margin
        {
            get { return 0f; }
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Ideoligions.Designer", inRect, () => Contents(inRect),
                "The ideoligion designer shows a failure notice. Nothing has been committed: closing it leaves "
                + "the faith exactly as it was.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Widgets.DrawBoxSolid(inRect, palette.WindowBackground);

            Rect body = inRect.ContractedBy(14f);

            Head(new Rect(body.x, body.y, body.width, 52f), palette);
            Steps(new Rect(body.x, body.y + 58f, body.width, 30f), palette);

            float top = body.y + 96f;
            float bottom = body.yMax - 44f;

            Rect area = new Rect(body.x, top, body.width, bottom - top);

            // The consequence column rides along on the two steps where a choice is being made. On the review
            // there is nothing left to weigh, so the diff gets the full width instead.
            if (step == Step.Review)
            {
                Review(area, palette);
            }
            else
            {
                const float side = 320f;

                Rect main = new Rect(area.x, area.y, area.width - side - 12f, area.height);

                if (step == Step.Memes)
                    Memes(main, palette);
                else
                    Doctrine(main, palette);

                Side(new Rect(area.xMax - side, area.y, side, area.height), palette);
            }

            Footer(new Rect(body.x, body.yMax - 34f, body.width, 34f), palette);
        }

        // -------------------------------------------------------------------------------------------

        private void Head(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceRaised);

            Rect inner = rect.ContractedBy(8f);
            Rect crest = new Rect(inner.x, inner.y + (inner.height - 34f) * 0.5f, 34f, 34f);

            UIGuard.Try("Ideoligions.DesignerCrest", () =>
            {
                Color previous = GUI.color;

                GUI.color = draft.draft.Color;
                GUI.DrawTexture(crest, draft.draft.Icon);
                GUI.color = previous;
            }, null);

            string price = draft.original.development != null
                ? "costs " + draft.original.development.NextReformationDevelopmentPoints
                  + " development points, and you have " + draft.original.development.Points
                : null;

            TabParts.RowLabel(new Rect(crest.xMax + 10f, inner.y, inner.width - 50f, 22f),
                "Reforming " + draft.original.name, draft.draft.TextColor, GameFont.Medium);

            if (price != null)
            {
                TabParts.RowLabel(new Rect(crest.xMax + 10f, inner.y + 22f, inner.width - 50f, 16f), price,
                    palette.TextSecondary, GameFont.Tiny);
            }
        }

        /// <summary>
        /// The three steps.
        ///
        /// <b>Three rather than the mockup's six, and the missing ones are not dropped so much as not reached
        /// from here.</b> Foundation is fixed on a reform -- RimWorld does not let a faith change what kind of
        /// thing it is -- and Identity and Style are edits to a name, a description and a palette rather than
        /// choices with consequences, which is what this window is shaped around. They are still made through
        /// vanilla's own screen until they are built, and nothing here prevents that.
        /// </summary>
        private void Steps(Rect rect, UIColorPaletteDef palette)
        {
            Step[] steps = { Step.Memes, Step.Doctrine, Step.Review };
            float width = rect.width / steps.Length;

            for (int i = 0; i < steps.Length; i++)
            {
                Rect tab = new Rect(rect.x + i * width, rect.y, width - 4f, rect.height);
                Step which = steps[i];

                TabParts.Segment(tab, (i + 1).ToString("00") + "  " + Name(which), step == which, palette,
                    () => step = which);
            }
        }

        private static string Name(Step which)
        {
            return which == Step.Memes ? "Memes" : which == Step.Doctrine ? "Doctrine" : "Review";
        }

        // -------------------------------------------------------------------------------------------
        // Step 1: memes
        // -------------------------------------------------------------------------------------------

        private void Memes(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            Rect view = new Rect(0f, 0f, rect.width - 20f, mainHeight);

            Widgets.BeginScrollView(rect.ContractedBy(8f), ref mainScroll, view);

            float y = 0f;

            y = TabParts.Heading(view, y, "Structure", palette);
            y = Grid(view, y, Structures(), palette, true);

            y += 10f;
            y = TabParts.Heading(view, y,
                "Memes  -  " + draft.NormalMemes().Count + " of "
                + (IdeoFoundation.MemeCountRangeAbsolute.max - 1), palette);

            y = Grid(view, y, Normals(), palette, false);

            if (Event.current.type == EventType.Layout)
                mainHeight = Mathf.Max(1f, y);

            Widgets.EndScrollView();
        }

        /// <summary>Every structure meme in the database, which is a short list and worth showing whole.</summary>
        private List<MemeDef> Structures()
        {
            List<MemeDef> memes = new List<MemeDef>();
            List<MemeDef> all = DefDatabase<MemeDef>.AllDefsListForReading;

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].category == MemeCategory.Structure && !all[i].hiddenInChooseMemes)
                    memes.Add(all[i]);
            }

            return memes;
        }

        /// <summary>
        /// Every normal meme, taken ones first.
        ///
        /// Enumerated from the database rather than named, so a meme from a DLC that is not installed is simply
        /// not in the list and a modded one appears without being asked for.
        /// </summary>
        private List<MemeDef> Normals()
        {
            List<MemeDef> memes = new List<MemeDef>();
            List<MemeDef> all = DefDatabase<MemeDef>.AllDefsListForReading;

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].category == MemeCategory.Normal && !all[i].hiddenInChooseMemes)
                    memes.Add(all[i]);
            }

            memes.Sort((a, b) =>
            {
                int taken = (draft.draft.memes.Contains(b) ? 1 : 0).CompareTo(draft.draft.memes.Contains(a) ? 1 : 0);

                return taken != 0 ? taken : string.CompareOrdinal(a.label, b.label);
            });

            return memes;
        }

        private float Grid(Rect view, float y, List<MemeDef> memes, UIColorPaletteDef palette, bool structure)
        {
            const float cardWidth = 208f;
            const float cardHeight = 84f;
            const float gap = 8f;

            int columns = Mathf.Max(1, Mathf.FloorToInt((view.width + gap) / (cardWidth + gap)));
            float actual = (view.width - (columns - 1) * gap) / columns;

            for (int i = 0; i < memes.Count; i++)
            {
                int column = i % columns;
                int row = i / columns;

                Rect card = new Rect(view.x + column * (actual + gap), y + row * (cardHeight + gap), actual,
                    cardHeight);

                Card(card, memes[i], palette, structure);
            }

            int rows = Mathf.CeilToInt(memes.Count / (float) columns);

            return y + rows * (cardHeight + gap);
        }

        /// <summary>
        /// One meme, with its impact, its description and the reason it cannot be taken if it cannot.
        ///
        /// <b>Blocked is drawn dim with its reason rather than hidden,</b> which is the mockup's rule: the wall
        /// should be visible before you walk into it. Vanilla hides nothing either, but only tells you after the
        /// click, in a message that disappears.
        /// </summary>
        private void Card(Rect rect, MemeDef meme, UIColorPaletteDef palette, bool structure)
        {
            bool taken = structure ? draft.draft.StructureMeme == meme : draft.draft.memes.Contains(meme);
            string blocked = taken ? null : draft.Blocked(meme);
            bool usable = blocked == null;

            Color border = taken ? palette.Accent : palette.Border;
            Color inside = taken
                ? UIElementPainter.Composite(palette.PanelBackground, palette.SelectionOverlay)
                : usable && Mouse.IsOver(rect)
                    ? UIElementPainter.Composite(palette.SurfaceSunken, palette.HoverOverlay)
                    : palette.SurfaceSunken;

            UIElementPainter.OutlineRounded(rect, border, inside);

            Rect inner = rect.ContractedBy(8f);
            Color text = taken ? palette.Accent : usable ? palette.TextPrimary : palette.TextDisabled;

            TabParts.RowLabel(new Rect(inner.x, inner.y, inner.width - 56f, 18f), meme.LabelCap, text);

            if (meme.impact > 0)
            {
                TabParts.RowLabel(new Rect(inner.xMax - 54f, inner.y, 54f, 18f), "impact " + meme.impact,
                    palette.TextDisabled, GameFont.Tiny);
            }

            string blurb = blocked ?? meme.description;

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                GUI.color = blocked != null ? palette.Warning : palette.TextSecondary;

                Widgets.Label(new Rect(inner.x, inner.y + 20f, inner.width, inner.height - 20f),
                    blurb.NullOrEmpty() ? "" : blurb.Truncate(inner.width * 3.4f));
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }

            if (meme.description != null)
                TooltipHandler.TipRegion(rect, (TipSignal) meme.description);

            if (!Widgets.ButtonInvisible(rect))
                return;

            if (!usable)
            {
                Messages.Message(blocked.CapitalizeFirst(), MessageTypeDefOf.RejectInput, false);

                return;
            }

            if (structure)
                draft.SetStructure(meme);
            else
                draft.ToggleMeme(meme);

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        // -------------------------------------------------------------------------------------------
        // Step 2: doctrine
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// One issue at a time, with the same card the memes use, so one shape means "a thing you are choosing"
        /// throughout the window.
        /// </summary>
        private void Doctrine(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            Rect inner = rect.ContractedBy(8f);

            const float listWidth = 190f;

            Issues(new Rect(inner.x, inner.y, listWidth, inner.height), palette);

            Rect right = new Rect(inner.x + listWidth + 10f, inner.y, inner.width - listWidth - 10f,
                inner.height);

            if (issue == null)
            {
                TabParts.Line(right, right.y + 10f, "Pick an issue on the left.", palette.TextDisabled);

                return;
            }

            float y = right.y;

            y = TabParts.Heading(right, y, issue.LabelCap, palette);

            List<PreceptDef> options = Options(issue);

            for (int i = 0; i < options.Count; i++)
                y = Option(right, y, options[i], palette);
        }

        private void Issues(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect view = rect.ContractedBy(4f);
            float y = view.y;

            List<IssueDef> issues = IssuesInPlay();

            for (int i = 0; i < issues.Count; i++)
            {
                Rect row = new Rect(view.x, y, view.width, 24f);
                bool on = issues[i] == issue;

                if (on)
                    UIElementPainter.FillRounded(row, palette.SelectionOverlay);
                else if (Mouse.IsOver(row))
                    UIElementPainter.FillRounded(row, palette.HoverOverlay);

                TabParts.RowLabel(new Rect(row.x + 6f, row.y, row.width - 12f, row.height), issues[i].LabelCap,
                    on ? palette.Accent : palette.TextPrimary, GameFont.Tiny);

                if (Widgets.ButtonInvisible(row))
                {
                    issue = issues[i];
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                y += 25f;
            }
        }

        /// <summary>
        /// The issues this faith can rule on: the ones it already rules on, plus any its memes ask about.
        ///
        /// Built from the draft's own precepts rather than from the whole database, which would list every issue
        /// in every expansion including the ones this faith has no business having an opinion on.
        /// </summary>
        private List<IssueDef> IssuesInPlay()
        {
            List<IssueDef> issues = new List<IssueDef>();
            List<Precept> precepts = draft.draft.PreceptsListForReading;

            for (int i = 0; i < precepts.Count; i++)
            {
                Precept precept = precepts[i];

                if (precept?.def?.issue == null || !precept.def.visible)
                    continue;

                if (precept is Precept_Role || precept is Precept_Ritual)
                    continue;

                if (!issues.Contains(precept.def.issue))
                    issues.Add(precept.def.issue);
            }

            issues.Sort((a, b) => string.CompareOrdinal(a.label, b.label));

            return issues;
        }

        /// <summary>Every precept in the database that rules on this issue and that this faith could take.</summary>
        private List<PreceptDef> Options(IssueDef forIssue)
        {
            List<PreceptDef> options = new List<PreceptDef>();
            List<PreceptDef> all = DefDatabase<PreceptDef>.AllDefsListForReading;

            for (int i = 0; i < all.Count; i++)
            {
                PreceptDef def = all[i];

                if (def == null || def.issue != forIssue || !def.visible || !def.visibleOnAddFloatMenu)
                    continue;

                options.Add(def);
            }

            return options;
        }

        private float Option(Rect view, float y, PreceptDef def, UIColorPaletteDef palette)
        {
            const float height = 62f;

            Rect card = new Rect(view.x, y, view.width, height);
            bool taken = Taken(def);

            UIElementPainter.OutlineRounded(card, taken ? palette.Accent : palette.Border,
                taken
                    ? UIElementPainter.Composite(palette.PanelBackground, palette.SelectionOverlay)
                    : Mouse.IsOver(card)
                        ? UIElementPainter.Composite(palette.SurfaceSunken, palette.HoverOverlay)
                        : palette.SurfaceSunken);

            Rect inner = card.ContractedBy(8f);

            TabParts.RowLabel(new Rect(inner.x, inner.y, inner.width - 60f, 18f), def.LabelCap,
                taken ? palette.Accent : palette.TextPrimary);

            TabParts.RowLabel(new Rect(inner.xMax - 58f, inner.y, 58f, 18f), def.impact.ToString().ToLower(),
                palette.TextDisabled, GameFont.Tiny);

            if (!def.description.NullOrEmpty())
            {
                TabParts.RowLabel(new Rect(inner.x, inner.y + 20f, inner.width, 18f),
                    def.description.Truncate(inner.width * 3.4f), palette.TextSecondary, GameFont.Tiny);
            }

            if (Widgets.ButtonInvisible(card) && !taken)
            {
                Choose(def);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            return y + height + 6f;
        }

        private bool Taken(PreceptDef def)
        {
            List<Precept> precepts = draft.draft.PreceptsListForReading;

            for (int i = 0; i < precepts.Count; i++)
            {
                if (precepts[i]?.def == def)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Swaps the faith's ruling on one issue.
        ///
        /// <b>Removed then added, which is the order the game's own editing takes.</b> An issue holds one
        /// precept; adding a second without removing the first is how a faith ends up ruling two ways on the
        /// same question.
        /// </summary>
        private void Choose(PreceptDef def)
        {
            UIGuard.Try("Ideoligions.ChoosePrecept", () =>
            {
                List<Precept> precepts = new List<Precept>(draft.draft.PreceptsListForReading);

                for (int i = 0; i < precepts.Count; i++)
                {
                    if (precepts[i]?.def?.issue == def.issue)
                        draft.draft.RemovePrecept(precepts[i]);
                }

                Precept made = PreceptMaker.MakePrecept(def);

                if (made != null)
                    draft.draft.AddPrecept(made, true);

                draft.draft.RecachePrecepts();
            }, "That precept was not changed.");
        }

        // -------------------------------------------------------------------------------------------
        // The consequence column
        // -------------------------------------------------------------------------------------------

        private void Side(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect view = rect.ContractedBy(8f);
            float y = view.y;

            y = TabParts.Heading(view, y, "What this costs you", palette);

            List<ConsequenceRow> rows = IdeoConsequences.Of(draft);

            if (rows.Count == 0)
                y = TabParts.Line(view, y, "Nothing beyond the points.", palette.TextDisabled, GameFont.Tiny);

            for (int i = 0; i < rows.Count && i < 12; i++)
                y = Consequence(view, y, rows[i], palette);

            y += 10f;
            y = TabParts.Heading(view, y, "Your colony, after", palette);

            List<DiffRow> diff = IdeoConsequences.Colony(draft);

            if (diff.Count == 0)
            {
                TabParts.Line(view, y, "Nothing changes yet.", palette.TextDisabled, GameFont.Tiny);

                return;
            }

            for (int i = 0; i < diff.Count && i < 12; i++)
                y = Diff(view, y, diff[i], palette);
        }

        private static float Consequence(Rect view, float y, ConsequenceRow row, UIColorPaletteDef palette)
        {
            Color tint = row.kind == ConsequenceKind.Gains
                ? palette.Success
                : row.kind == ConsequenceKind.Requires
                    ? palette.Warning
                    : palette.Danger;

            string tag = row.kind == ConsequenceKind.Gains
                ? "gains"
                : row.kind == ConsequenceKind.Requires
                    ? "requires"
                    : row.kind == ConsequenceKind.Forbids
                        ? "forbids"
                        : "rules out";

            TabParts.RowLabel(new Rect(view.x, y, 62f, 18f), tag, tint, GameFont.Tiny);
            TabParts.RowLabel(new Rect(view.x + 66f, y, view.width - 66f, 18f), row.text, palette.TextSecondary,
                GameFont.Tiny);

            return y + 19f;
        }

        private static float Diff(Rect view, float y, DiffRow row, UIColorPaletteDef palette)
        {
            TabParts.RowLabel(new Rect(view.x, y, 12f, 18f), row.good ? "+" : "-",
                row.good ? palette.Success : palette.Danger, GameFont.Tiny);

            TabParts.RowLabel(new Rect(view.x + 14f, y, view.width - 14f, 18f), row.text, palette.TextSecondary,
                GameFont.Tiny);

            return y + 19f;
        }

        // -------------------------------------------------------------------------------------------
        // Step 3: review
        // -------------------------------------------------------------------------------------------

        private void Review(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            Rect view = rect.ContractedBy(12f);
            float y = view.y;

            y = Toggle(view, y, palette);
            y += 8f;

            y = TabParts.Heading(view, y, "What this costs you", palette);

            List<ConsequenceRow> rows = IdeoConsequences.Of(draft);

            for (int i = 0; i < rows.Count && i < 16; i++)
                y = Consequence(view, y, rows[i], palette);

            y += 10f;
            y = TabParts.Heading(view, y, "Your colony, after", palette);

            List<DiffRow> diff = IdeoConsequences.Colony(draft);

            if (diff.Count == 0)
                y = TabParts.Line(view, y, "Nothing has been changed yet.", palette.TextDisabled, GameFont.Tiny);

            for (int i = 0; i < diff.Count && i < 16; i++)
                y = Diff(view, y, diff[i], palette);

            Pair<Precept, Precept> clash = draft.Contradiction();

            if (clash.First == null || clash.Second == null)
                return;

            y += 10f;

            TabParts.Line(view, y,
                "These two contradict each other: " + clash.First.TipLabel + " and " + clash.Second.TipLabel
                + ". RimWorld allows it and so does this, but it is worth knowing you chose it.",
                palette.Warning, GameFont.Tiny);
        }

        /// <summary>
        /// The preserve switch, on the screen where the decision is actually made.
        ///
        /// <b>Toggling it re-runs the reconcile,</b> which is the only honest way to make it a live switch: the
        /// meme change has already happened by the time the player reaches this step, so turning the setting on
        /// afterwards has to rebuild the draft from the original rather than pretend the earlier reconcile never
        /// ran. That is what <c>Reopen</c> does.
        /// </summary>
        private float Toggle(Rect view, float y, UIColorPaletteDef palette)
        {
            Rect row = new Rect(view.x, y, view.width, 26f);
            bool on = IdeoDraft.Preserve;
            bool was = on;

            UICheckboxControl.Draw(new Rect(row.x, row.y + 3f, 20f, 20f), ref on, palette);

            TabParts.RowLabel(new Rect(row.x + 28f, row.y, row.width - 28f, 26f),
                "Keep the doctrine when memes change", palette.TextPrimary);

            TooltipHandler.TipRegion(row, (TipSignal) ("With this on, changing a meme leaves every precept, "
                + "role, ritual and demanded building exactly as you built it. RimWorld would otherwise "
                + "reconcile them against the new memes, dropping the ones it forbids and adding the ones it "
                + "demands.\n\nThis is read by this window and nowhere else. It cannot affect a new game's "
                + "starting ideoligion."));

            if (on != was && UIOverhaulSettingsFile.Current != null)
            {
                UIOverhaulSettingsFile.Current.preservePrecepts = on;

                UIGuard.Try("Ideoligions.SaveToggle", () => UIOverhaulSettingsFile.Current.Save(), null);

                Reopen();
            }

            return y + 30f;
        }

        /// <summary>
        /// Starts the draft again with the switch in its new position, keeping the meme set the player chose.
        ///
        /// A reconcile cannot be undone in place: once <c>EnsurePreceptsCompatibleWithMemes</c> has dropped a
        /// precept, the draft no longer knows what it was. Rebuilding from the untouched original and replaying
        /// the meme choices is exact, and it is cheap because the original is never edited.
        /// </summary>
        private void Reopen()
        {
            UIGuard.Try("Ideoligions.Reopen", () =>
            {
                List<MemeDef> chosen = new List<MemeDef>(draft.draft.memes);
                IdeoDraft fresh = IdeoDraft.Of(draft.original);

                if (fresh == null)
                    return;

                MemeDef structure = null;

                for (int i = 0; i < chosen.Count; i++)
                {
                    if (chosen[i] != null && chosen[i].category == MemeCategory.Structure)
                        structure = chosen[i];
                }

                if (structure != null)
                    fresh.SetStructure(structure);

                for (int i = 0; i < chosen.Count; i++)
                {
                    if (chosen[i] != null && chosen[i].category == MemeCategory.Normal
                        && !fresh.draft.memes.Contains(chosen[i]))
                        fresh.ToggleMeme(chosen[i]);
                }

                Close(false);
                Find.WindowStack.Add(new Dialog_IdeoDesigner(fresh) { step = Step.Review });
            }, "The switch was changed but the draft was not rebuilt. Close and reopen the designer.");
        }

        // -------------------------------------------------------------------------------------------

        private void Footer(Rect rect, UIColorPaletteDef palette)
        {
            if (TabParts.Button(new Rect(rect.x, rect.y, 130f, 32f), "Cancel", palette))
                Close();

            if (step != Step.Review)
            {
                if (TabParts.Button(new Rect(rect.xMax - 150f, rect.y, 150f, 32f), "Review changes", palette,
                        true, true))
                    step = Step.Review;

                return;
            }

            bool can = draft.Changed() && draft.original.development != null
                       && draft.original.development.CanReformNow;

            if (TabParts.Button(new Rect(rect.xMax - 150f, rect.y, 150f, 32f), "Reform", palette, can, true,
                    can
                        ? "Commit this reform. RimWorld will ask you to confirm the price first."
                        : draft.Changed()
                            ? "Not enough development points."
                            : "Nothing has been changed yet."))
                draft.Commit(() => Close());
        }
    }
}
