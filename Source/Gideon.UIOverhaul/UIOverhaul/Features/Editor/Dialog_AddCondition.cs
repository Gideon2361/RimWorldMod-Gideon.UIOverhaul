using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// Adding a health condition, as one window with steps rather than a chain of pick lists.
    ///
    /// <b>Why it stopped being pick lists, on Aaron's instruction of 2026-08-23.</b> The old flow opened a list of
    /// conditions, closed it, opened a list of body parts, closed it, and added the hediff -- and there was
    /// nowhere in that sequence to say anything else about what was being added. That is the whole reason One with
    /// Death's control expansion could only ever be added at level one: <b>the flow had no step that could ask.</b>
    /// A chain of modal lists cannot grow a third question without becoming a third modal list, and by then nobody
    /// can see what they picked two windows ago.
    ///
    /// <b>Three steps, and the third is the point.</b> Condition, then where it goes, then how much of it. The
    /// strip along the top says what has been chosen so far and lets any earlier step be revisited without
    /// starting again, which is the thing a chain of lists cannot do at all.
    ///
    /// <b>The third step is built from the def, not from a table of special cases.</b> A <c>Hediff_Level</c> gets
    /// a level between the def's own <c>minSeverity</c> and <c>maxSeverity</c>, because that is the range
    /// <c>Hediff_Level.ChangeLevel</c> clamps to. Everything else gets a severity over the same range when the def
    /// describes one. A def that says nothing about either shows no third step and adds at its own initial
    /// severity, exactly as before.
    ///
    /// <b>Laid out like the bill wizard,</b> which is the mod's other add-a-thing flow: header, numbered step
    /// strip, body, footer. Two wizards that do not look alike are two things to learn.
    /// </summary>
    public class Dialog_AddCondition : Window
    {
        private const float HeaderHeight = 42f;
        private const float StripHeight = 32f;
        private const float FooterHeight = 48f;
        private const float Pad = 12f;
        private const float RowHeight = 30f;

        private enum Step
        {
            Condition,
            Part,
            Amount
        }

        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search conditions",
            Icon = TexButton.Search,
            MaxLength = 40
        };

        private readonly EditorContext context;

        private Step step = Step.Condition;

        private HediffDef chosen;

        private BodyPartRecord part;

        /// <summary>Set once a part has been settled, so "whole body" is a decision rather than a blank.</summary>
        private bool partSettled;

        private int level = 1;

        private float severity = -1f;

        private string query = string.Empty;

        private Vector2 scroll;

        private readonly List<HediffDef> conditions = new List<HediffDef>();

        private readonly List<HediffDef> shown = new List<HediffDef>();

        private readonly List<BodyPartRecord> parts = new List<BodyPartRecord>();

        private Dialog_AddCondition(EditorContext context)
        {
            this.context = context;

            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
            draggable = true;
        }

        public override Vector2 InitialSize =>
            new Vector2(Mathf.Min(620f, UI.screenWidth - 80f), Mathf.Min(560f, UI.screenHeight - 80f));

        internal static void Open(EditorContext context)
        {
            UIGuard.Try("Editor.OpenAddCondition", () =>
            {
                if (context?.Pawn == null)
                    return;

                Search.Clear();

                Find.WindowStack.Add(new Dialog_AddCondition(context));
            }, "The add condition window could not be opened.");
        }

        public override void PostOpen()
        {
            base.PostOpen();

            Build();
        }

        private void Build()
        {
            UIGuard.Try("Editor.BuildConditionList", () =>
            {
                conditions.Clear();
                conditions.AddRange(DefDatabase<HediffDef>.AllDefsListForReading);

                conditions.Sort((a, b) => string.Compare(EditorParts.LabelOf(a), EditorParts.LabelOf(b),
                    System.StringComparison.OrdinalIgnoreCase));

                Filter();
            }, null);
        }

        private void Filter()
        {
            shown.Clear();

            for (int i = 0; i < conditions.Count; i++)
            {
                HediffDef def = conditions[i];

                if (query.NullOrEmpty()
                    || EditorParts.LabelOf(def).IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    shown.Add(def);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Editor.AddCondition", inRect, () => Contents(inRect),
                "The add condition window could not finish drawing.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Text.Font = GameFont.Medium;
            GUI.color = palette.TextPrimary;

            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 40f, HeaderHeight), "Add a condition");

            Text.Font = GameFont.Small;

            Strip(new Rect(inRect.x, inRect.y + HeaderHeight, inRect.width, StripHeight), palette);

            Rect body = new Rect(inRect.x, inRect.y + HeaderHeight + StripHeight + 6f, inRect.width,
                inRect.height - HeaderHeight - StripHeight - FooterHeight - 12f);

            switch (step)
            {
                case Step.Part:
                    PartStep(body, palette);

                    break;

                case Step.Amount:
                    AmountStep(body, palette);

                    break;

                default:
                    ConditionStep(body, palette);

                    break;
            }

            Footer(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight), palette);

            GUI.color = palette.TextPrimary;
        }

        /// <summary>
        /// The numbered steps, each showing what was chosen for it.
        ///
        /// A step already answered is clickable, so a wrong condition is one click to fix rather than a cancel and
        /// a restart. A step not yet reached is not.
        /// </summary>
        private void Strip(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.Outline(rect, palette.Border, palette.SurfaceSunken);

            float x = rect.x + Pad;

            x = Tab(rect, x, 1, chosen == null ? "Condition" : EditorParts.LabelOf(chosen), Step.Condition,
                true, palette);

            x = Tab(rect, x, 2, !partSettled ? "Where" : part == null ? "Whole body" : part.LabelCap.ToString(),
                Step.Part, chosen != null, palette);

            if (Asks(chosen))
                Tab(rect, x, 3, IsLevelled(chosen) ? "Level " + level : "Severity", Step.Amount, partSettled,
                    palette);
        }

        private float Tab(Rect rect, float x, int number, string label, Step which, bool reachable,
            UIColorPaletteDef palette)
        {
            bool here = step == which;
            string text = number + ".  " + label;
            float width = TabParts.ButtonWidth(text, 20f);
            Rect tab = new Rect(x, rect.y + 3f, width, rect.height - 6f);

            if (here)
                UIElementPainter.Outline(tab, palette.Accent, palette.AccentMuted);
            else if (reachable && Mouse.IsOver(tab))
                Widgets.DrawHighlight(tab);

            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = here ? palette.TextPrimary : reachable ? palette.TextSecondary : palette.TextDisabled;

            UIRichText.Label(tab, text);

            Text.Anchor = anchor;
            Text.Font = font;
            GUI.color = palette.TextPrimary;

            if (reachable && !here && Widgets.ButtonInvisible(tab))
            {
                step = which;

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            return x + width + 6f;
        }

        // -------------------------------------------------------------------------------------------
        // Step one: which condition
        // -------------------------------------------------------------------------------------------

        private void ConditionStep(Rect rect, UIColorPaletteDef palette)
        {
            Search.Draw(new Rect(rect.x, rect.y, rect.width, 26f), palette);

            if (Search.Text != query)
            {
                query = Search.Text ?? string.Empty;

                Filter();

                scroll = Vector2.zero;
            }

            Rect list = new Rect(rect.x, rect.y + 32f, rect.width, rect.height - 32f);

            UIElementPainter.Outline(list, palette.Border, palette.SurfaceSunken);

            Rect inner = list.ContractedBy(4f);
            Rect view = new Rect(0f, 0f, inner.width - 18f, shown.Count * RowHeight);

            Widgets.BeginScrollView(inner, ref scroll, view);

            try
            {
                float y = 0f;

                for (int i = 0; i < shown.Count; i++)
                {
                    if (y + RowHeight >= scroll.y && y <= scroll.y + inner.height)
                        ConditionRow(new Rect(0f, y, view.width, RowHeight - 1f), shown[i], palette);

                    y += RowHeight;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void ConditionRow(Rect row, HediffDef def, UIColorPaletteDef palette)
        {
            bool here = def == chosen;

            if (here)
                Widgets.DrawBoxSolid(row, palette.AccentMuted);
            else if (Mouse.IsOver(row))
                Widgets.DrawHighlight(row);

            TabParts.RowLabel(new Rect(row.x + 8f, row.y, row.width - 120f, row.height),
                EditorParts.LabelOf(def), here ? palette.TextPrimary : palette.TextSecondary);

            if (!def.isBad)
                TabParts.RowLabel(new Rect(row.xMax - 110f, row.y, 104f, row.height), "not harmful",
                    palette.TextDisabled, GameFont.Tiny);

            string description = EditorParts.DescriptionOf(def);

            if (!description.NullOrEmpty())
                TooltipHandler.TipRegion(row, new TipSignal(description, def.GetHashCode()));

            if (!Widgets.ButtonInvisible(row))
                return;

            Choose(def);

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Takes a condition and moves on, resetting everything downstream of it.
        ///
        /// Going back and picking a different condition has to clear the part and the amount: a part that suited
        /// the old def may not exist for the new one, and a level of six means nothing on a def that stops at one.
        /// </summary>
        private void Choose(HediffDef def)
        {
            chosen = def;
            part = null;
            partSettled = false;
            severity = -1f;
            level = Mathf.Max(1, Mathf.RoundToInt(def.initialSeverity));

            BuildParts();

            step = Step.Part;
        }

        // -------------------------------------------------------------------------------------------
        // Step two: where it goes
        // -------------------------------------------------------------------------------------------

        private void BuildParts()
        {
            UIGuard.Try("Editor.BuildPartList", () =>
            {
                parts.Clear();

                Pawn pawn = context.Pawn;

                if (pawn?.health?.hediffSet == null)
                    return;

                foreach (BodyPartRecord record in pawn.health.hediffSet.GetNotMissingParts())
                    parts.Add(record);
            }, null);
        }

        private void PartStep(Rect rect, UIColorPaletteDef palette)
        {
            bool needsPart = HediffPlacement.NeedsPart(chosen);

            float y = rect.y;

            if (needsPart)
            {
                GUI.color = palette.TextSecondary;

                Widgets.Label(new Rect(rect.x, y, rect.width, 34f),
                    EditorParts.LabelOf(chosen) + " has to sit on a body part. The brain is where the game puts "
                    + "this kind of thing.");

                GUI.color = palette.TextPrimary;

                y += 38f;
            }

            Rect list = new Rect(rect.x, y, rect.width, rect.yMax - y);

            UIElementPainter.Outline(list, palette.Border, palette.SurfaceSunken);

            Rect inner = list.ContractedBy(4f);
            int rows = parts.Count + (needsPart ? 0 : 1);
            Rect view = new Rect(0f, 0f, inner.width - 18f, rows * RowHeight);

            Widgets.BeginScrollView(inner, ref scroll, view);

            try
            {
                float top = 0f;

                if (!needsPart)
                {
                    PartRow(new Rect(0f, top, view.width, RowHeight - 1f), null, palette);

                    top += RowHeight;
                }

                for (int i = 0; i < parts.Count; i++)
                {
                    PartRow(new Rect(0f, top, view.width, RowHeight - 1f), parts[i], palette);

                    top += RowHeight;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void PartRow(Rect row, BodyPartRecord record, UIColorPaletteDef palette)
        {
            bool here = partSettled && record == part;

            if (here)
                Widgets.DrawBoxSolid(row, palette.AccentMuted);
            else if (Mouse.IsOver(row))
                Widgets.DrawHighlight(row);

            TabParts.RowLabel(new Rect(row.x + 8f, row.y, row.width - 90f, row.height),
                record == null ? "Whole body" : record.LabelCap.ToString(),
                here ? palette.TextPrimary : palette.TextSecondary);

            TabParts.RowLabel(new Rect(row.xMax - 84f, row.y, 78f, row.height),
                record == null ? "no particular part" : record.def != null ? record.def.hitPoints + " hp" : null,
                palette.TextDisabled, GameFont.Tiny);

            if (!Widgets.ButtonInvisible(row))
                return;

            part = record;
            partSettled = true;

            step = Asks(chosen) ? Step.Amount : Step.Part;

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        // -------------------------------------------------------------------------------------------
        // Step three: how much
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Whether this def has anything to ask about beyond where it goes.
        ///
        /// A levelled hediff always does. Anything else only when the def describes a severity range worth moving
        /// through -- a def whose min and max are the same has one severity and no question.
        /// </summary>
        private static bool Asks(HediffDef def)
        {
            if (def == null)
                return false;

            return IsLevelled(def) || def.maxSeverity > def.minSeverity + 0.001f;
        }

        private static bool IsLevelled(HediffDef def)
        {
            return def != null && def.hediffClass != null
                               && typeof(Hediff_Level).IsAssignableFrom(def.hediffClass);
        }

        private void AmountStep(Rect rect, UIColorPaletteDef palette)
        {
            float y = rect.y;

            if (IsLevelled(chosen))
            {
                int low = Mathf.Max(1, Mathf.RoundToInt(chosen.minSeverity));
                int high = Mathf.Max(low, Mathf.RoundToInt(chosen.maxSeverity));

                GUI.color = palette.TextSecondary;

                Widgets.Label(new Rect(rect.x, y, rect.width, 34f),
                    EditorParts.LabelOf(chosen) + " is counted in levels. This one goes from " + low + " to "
                    + high + ".");

                GUI.color = palette.TextPrimary;

                y += 40f;

                level = Mathf.Clamp(level, low, high);

                Rect slider = new Rect(rect.x, y, rect.width, 28f);

                level = Mathf.RoundToInt(Widgets.HorizontalSlider(slider, level, low, high, true,
                    "Level " + level, low.ToString(), high.ToString(), 1f));

                return;
            }

            float min = chosen.minSeverity;
            float max = chosen.maxSeverity;

            if (severity < 0f)
                severity = Mathf.Clamp(chosen.initialSeverity, min, max);

            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(rect.x, y, rect.width, 34f),
                "How far along " + EditorParts.LabelOf(chosen) + " is when it is added.");

            GUI.color = palette.TextPrimary;

            y += 40f;

            severity = Widgets.HorizontalSlider(new Rect(rect.x, y, rect.width, 28f), severity, min, max, true,
                "Severity " + severity.ToString("0.##"), min.ToString("0.##"), max.ToString("0.##"));
        }

        // -------------------------------------------------------------------------------------------
        // Footer
        // -------------------------------------------------------------------------------------------

        private void Footer(Rect rect, UIColorPaletteDef palette)
        {
            bool ready = chosen != null && partSettled;

            if (TabParts.Button(new Rect(rect.x, rect.y + 8f, TabParts.ButtonWidth("Cancel"), 30f), "Cancel",
                    palette))
                Close();

            string add = "Add condition";
            float width = TabParts.ButtonWidth(add, 22f);

            if (TabParts.Button(new Rect(rect.xMax - width, rect.y + 8f, width, 30f), add, palette, ready, true,
                    ready ? null : "Pick a condition and where it goes first."))
            {
                Add();

                Close();
            }
        }

        private void Add()
        {
            UIGuard.Try("Editor.AddConditionCommit", () =>
            {
                float? wanted = null;

                if (Asks(chosen) && !IsLevelled(chosen) && severity >= 0f)
                    wanted = severity;

                EditorState.AddCondition(context, chosen, part, IsLevelled(chosen) ? level : 0, wanted);
            }, "That condition could not be added.");
        }
    }
}
