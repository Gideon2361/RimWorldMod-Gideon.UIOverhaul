using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// The Bio body: where this person came from, what it cost them, and where they stand now.
    ///
    /// <b>It keeps the prose and adds the consequence.</b> A backstory is worth reading once and worth knowing
    /// forever after only for what it does to the skills, so the description is there and the skill gains are
    /// spelled out under it rather than left to be discovered on another tab.
    ///
    /// <b>Traits are chips and are not colour coded good or bad,</b> which is a deliberate departure from the
    /// mockup. There is no honest reading of whether a trait helps: Nimble is good, Volatile is bad, and
    /// Pyromaniac is whichever the colony needs least today, and none of that is a field on <c>TraitDegreeData</c>
    /// that could be read rather than guessed. A wrong colour on a trait is worse than no colour, so the chips
    /// carry the game's own full description on hover instead.
    /// </summary>
    internal static class InspectBioBody
    {
        /// <summary>Reused between frames, since the pane redraws every one of them.</summary>
        private static readonly List<WorkTypeDef> Disabled = new List<WorkTypeDef>();

        internal static float Draw(Rect view, Pawn pawn, UIColorPaletteDef palette)
        {
            if (pawn.story == null)
                return 0f;

            Rect left;
            Rect right;

            InspectBodies.Columns(view, out left, out right);

            bool split = InspectBodies.Live(right);

            float leftY = Backstory(left, view.y, pawn, palette);

            leftY = Incapable(left, leftY, pawn, palette);

            Rect second = split ? right : left;
            float secondY = split ? view.y : leftY;

            secondY = Traits(second, secondY, pawn, palette);
            secondY = Standing(second, secondY, pawn, palette);
            secondY = Workout(second, secondY, pawn, palette);

            // No editor button here. It was one, until 2026-08-23: a button on this panel can only be reached on
            // something that has this panel, and a corpse does not -- which made a dead pawn the one selection
            // that could not open the editor. It is an icon in the pane's own corner now, beside the info card,
            // where it works on every selection. See Editor.EditorButton.
            return (split ? Mathf.Max(leftY, secondY) : secondY) - view.y;
        }

        /// <summary>
        /// Rimbody's workout goals, when Rimbody is running.
        ///
        /// <b>Here because this is the panel about the body,</b> asked for on 2026-08-23. Rimbody keeps them on
        /// its own tab, which is one more tab to open for two numbers that belong beside a pawn's traits and
        /// standing.
        ///
        /// <b>Absent when Rimbody is not running, and absent for a pawn it has not measured.</b> The comp starts
        /// with both values at -1 and fills them in later, so a caption with two empty bars would be this mod
        /// inventing a state Rimbody does not have.
        ///
        /// <b>Each goal is a need row plus a control row.</b> The need row is the pane's own vocabulary -- name,
        /// value, a track, and the goal drawn on it as a tick -- which is the shape that makes a bar mean
        /// something, and it answers "are they there yet" without being touched. The control row underneath is
        /// the only interactive thing on this panel, and it is two controls rather than a text field because the
        /// question is a rough target and not a precise figure.
        /// </summary>
        private static float Workout(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            if (!Integrations.RimbodyIntegration.Available || !OfColony(pawn))
                return y;

            ThingComp comp = Integrations.RimbodyIntegration.Physique(pawn);

            if (comp == null)
                return y;

            y = InspectPaneParts.Cap(view, y, "Workout goals", null, palette);

            y = Goal(view, y, comp, palette, true);
            y = Goal(view, y, comp, palette, false);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// Whether these goals are the player's to set.
        ///
        /// <b>A workout goal is an order, not a fact,</b> and the only people who take orders here are the
        /// colony's own, its prisoners and its slaves. The block was drawn for anything Rimbody had measured,
        /// which meant a raider's corpse on the other side of the map offered two goal controls that command
        /// nobody. Reported 2026-08-28 against a dead pawn of another faction.
        ///
        /// <b>The three tests are not one test.</b> A prisoner keeps the faction they were captured from, so
        /// asking about the faction alone misses every prisoner; a slave who is not yet secure fails
        /// <c>IsColonist</c> and needs its own question. <c>IsColonist</c> also excludes subhumans, which is
        /// vanilla's rule rather than ours and costs nothing here -- Rimbody has no physique for one, so the
        /// comp test above would have stopped it anyway.
        /// </summary>
        private static bool OfColony(Pawn pawn)
        {
            return pawn.IsColonist || pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony;
        }

        /// <summary>
        /// One goal: where the body is now, where it is being taken, and the two controls that say so.
        ///
        /// <paramref name="muscle"/> picks which of the pair this is. One method rather than two, because the two
        /// differ in four values and nothing else -- and Rimbody spells one field <c>useFatgoal</c> and the other
        /// <c>useMuscleGoal</c>, which is exactly the sort of difference that gets copied wrong twice.
        /// </summary>
        private static float Goal(Rect view, float y, ThingComp comp, UIColorPaletteDef palette, bool muscle)
        {
            float ceiling = Integrations.RimbodyIntegration.Ceiling;

            float current = muscle
                ? Integrations.RimbodyIntegration.MuscleMass(comp)
                : Integrations.RimbodyIntegration.BodyFat(comp);

            bool on = muscle
                ? Integrations.RimbodyIntegration.UseMuscleGoal(comp)
                : Integrations.RimbodyIntegration.UseFatGoal(comp);

            float goal = muscle
                ? Integrations.RimbodyIntegration.MuscleGoal(comp)
                : Integrations.RimbodyIntegration.FatGoal(comp);

            string value = on
                ? current.ToString("0.0") + " to " + goal.ToString("0.0")
                : current.ToString("0.0");

            Color fill = muscle ? palette.Accent : palette.Info;

            y = InspectPaneParts.Need(view, y, muscle ? "Muscle mass" : "Body fat", value,
                on ? palette.TextPrimary : palette.TextSecondary, current / ceiling, fill,
                on ? new[] { goal / ceiling } : null, null, palette);

            return Controls(view, y, comp, palette, muscle, on, goal);
        }

        /// <summary>
        /// The switch and the slider.
        ///
        /// <b>The slider is only drawn when the goal is on,</b> because a slider that moves a number nothing
        /// reads is a control that lies about having an effect. With the goal off the row says so in words
        /// instead, which is also what tells you the switch is the thing to press.
        /// </summary>
        private static float Controls(Rect view, float y, ThingComp comp, UIColorPaletteDef palette, bool muscle,
            bool on, float goal)
        {
            const float box = 14f;
            const float gap = 6f;

            float row = Mathf.Max(box, UIFonts.LineHeightOf(GameFont.Tiny));

            Rect switchRect = new Rect(view.x, y + (row - box) * 0.5f, box, box);

            UIElementPainter.PaintCheckbox(switchRect,
                on ? MultiCheckboxState.On : MultiCheckboxState.Off, palette, false);

            Rect hit = new Rect(view.x, y, box + gap, row);

            if (Widgets.ButtonInvisible(hit))
            {
                if (muscle)
                    Integrations.RimbodyIntegration.SetUseMuscleGoal(comp, !on);
                else
                    Integrations.RimbodyIntegration.SetUseFatGoal(comp, !on);

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            TooltipHandler.TipRegion(hit, (TipSignal) (muscle
                ? "While this is on and the goal is above their muscle, they eat more often and pick strength "
                  + "exercises."
                : "While this is on and the goal is below their fat, they eat only when hungry and pick cardio."));

            Rect rest = new Rect(view.x + box + gap, y, Mathf.Max(20f, view.width - box - gap), row);

            if (!on)
            {
                GameFont previousFont = Text.Font;
                Color previousColor = GUI.color;

                try
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = palette.TextDisabled;

                    UIRichText.Label(rest, muscle ? "No muscle goal" : "No fat goal");
                }
                finally
                {
                    GUI.color = previousColor;
                    Text.Font = previousFont;
                }

                return y + row + InspectPaneParts.RowGap;
            }

            // Rimbody's own bounds and its own rounding. Written straight to the field, as its card does: there
            // is no setter and nothing to notify, and the comp is scribed by the game either way.
            float moved = Widgets.HorizontalSlider(rest, goal, 0f,
                Integrations.RimbodyIntegration.Ceiling, true, null, null, null,
                Integrations.RimbodyIntegration.Step);

            if (Mathf.Abs(moved - goal) >= 0.001f)
            {
                if (muscle)
                    Integrations.RimbodyIntegration.SetMuscleGoal(comp, moved);
                else
                    Integrations.RimbodyIntegration.SetFatGoal(comp, moved);
            }

            return y + row + InspectPaneParts.RowGap;
        }

        /// <summary>The two backstories, each with its own description including what it did to the skills.</summary>
        private static float Backstory(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            y = InspectPaneParts.Cap(view, y, "Backstory", null, palette);

            y = Slot(view, y, pawn, pawn.story.Childhood, "Childhood", palette);
            y = Slot(view, y, pawn, pawn.story.Adulthood, "Adulthood", palette);

            return y + InspectPaneParts.BlockGap;
        }

        private static float Slot(Rect view, float y, Pawn pawn, BackstoryDef story, string when,
            UIColorPaletteDef palette)
        {
            if (story == null)
                return y;

            string title = UIGuard.Try("Inspector.BackstoryTitle",
                () => story.TitleCapFor(pawn.gender), story.title, null);

            y = InspectPaneParts.Entry(view, y, title, when, palette.TextDisabled, null, palette);

            // Cached, and this pane needs it more than the editor does: it redraws for as long as anything is
            // selected. See Shared.BackstoryText for what FullDescriptionFor actually costs.
            string description = Shared.BackstoryText.For(story, pawn);

            if (!description.NullOrEmpty())
                y = InspectPaneParts.Note(view, y, description, palette) + InspectPaneParts.RowGap;

            return y;
        }

        /// <summary>
        /// The work this pawn cannot do, whatever the reason.
        ///
        /// <b>Work types rather than work tags,</b> because a work type is the thing the player actually sets on
        /// the work tab. Told as "cannot do Research", which is the sentence somebody assigning jobs needs, and
        /// not as the tag names the game reasons in.
        /// </summary>
        private static float Incapable(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            List<WorkTypeDef> disabled = Disabled;

            disabled.Clear();

            UIGuard.Try("Inspector.DisabledWork", () =>
            {
                List<WorkTypeDef> all = DefDatabase<WorkTypeDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] != null && all[i].visible && pawn.WorkTypeIsDisabled(all[i]))
                        disabled.Add(all[i]);
                }
            }, "The inspect pane cannot list what this pawn is incapable of.");

            if (disabled.Count == 0)
                return y;

            y = InspectPaneParts.Cap(view, y, "Cannot do", disabled.Count.ToString(), palette);

            float x = view.x;
            float rowHeight = 0f;

            for (int i = 0; i < disabled.Count; i++)
            {
                string label = disabled[i].labelShort.NullOrEmpty()
                    ? disabled[i].label
                    : disabled[i].labelShort;

                // Wrapped rather than clipped: a pawn incapable of six things would otherwise show three and give
                // no sign that the other three exist. The flow measures each chip before placing it, which is
                // what stops the one that does not fit being drawn overhanging the column first.
                InspectPaneParts.Chip(view, ref x, ref y, ref rowHeight, label.CapitalizeFirst(), palette.Danger,
                    false, palette);
            }

            return y + rowHeight + InspectPaneParts.BlockGap;
        }

        /// <summary>Every trait as a chip, with the game's own explanation of it on hover.</summary>
        private static float Traits(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            if (pawn.story.traits == null || pawn.story.traits.allTraits == null)
                return y;

            List<Trait> traits = pawn.story.traits.allTraits;

            y = InspectPaneParts.Cap(view, y, "Traits",
                traits.Count == 0 ? null : traits.Count.ToString(), palette);

            if (traits.Count == 0)
                return InspectPaneParts.Note(view, y, "None.", palette) + InspectPaneParts.BlockGap;

            float x = view.x;
            float rowHeight = 0f;

            for (int i = 0; i < traits.Count; i++)
            {
                Trait trait = traits[i];

                if (trait == null)
                    continue;

                bool suppressed = UIGuard.Try("Inspector.TraitSuppressed", () => trait.Suppressed, false, null);

                Rect chip = InspectPaneParts.Chip(view, ref x, ref y, ref rowHeight,
                    UIGuard.Try("Inspector.TraitLabel", () => trait.LabelCap, "?", null),
                    suppressed ? palette.TextDisabled : palette.Accent, false, palette);

                if (!Mouse.IsOver(chip))
                    continue;

                string tip = UIGuard.Try("Inspector.TraitTip", () => trait.TipString(pawn), null, null);

                if (!tip.NullOrEmpty())
                    TooltipHandler.TipRegion(chip, (TipSignal) tip);
            }

            return y + rowHeight + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// Age, kind, faith, rank and time served: the facts that are about standing rather than history.
        ///
        /// Every row here is one line and conditional on the expansion that invented it, so a vanilla install
        /// sees two lines and a fully expanded one sees six, without any of them being drawn empty.
        /// </summary>
        private static float Standing(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            y = InspectPaneParts.Cap(view, y, "Standing", null, palette);

            if (pawn.ageTracker != null)
            {
                int biological = pawn.ageTracker.AgeBiologicalYears;
                int chronological = pawn.ageTracker.AgeChronologicalYears;

                y = InspectPaneParts.Fact(view, y, "Age",
                    biological == chronological
                        ? biological.ToString()
                        : biological + " (born " + chronological + " years ago)",
                    palette.TextPrimary, palette);
            }

            y = Xenotype(view, y, pawn, palette);

            if (ModsConfig.IdeologyActive)
            {
                Ideo ideo = UIGuard.Try("Inspector.Ideo", () => pawn.Ideo, null, null);

                y = InspectPaneParts.Fact(view, y, "Ideoligion",
                    ideo != null ? ideo.name : "none",
                    ideo != null ? palette.TextPrimary : palette.TextDisabled, palette);

                string role = UIGuard.Try("Inspector.IdeoRole", () =>
                {
                    Precept_Role held = ideo != null ? ideo.GetRole(pawn) : null;

                    return held != null ? held.LabelForPawn(pawn) : null;
                }, null, null);

                y = InspectPaneParts.Fact(view, y, "Role", role.NullOrEmpty() ? "none" : role,
                    role.NullOrEmpty() ? palette.TextDisabled : palette.TextPrimary, palette);
            }

            if (ModsConfig.RoyaltyActive && pawn.royalty != null)
            {
                string title = UIGuard.Try("Inspector.Title", () =>
                {
                    RoyalTitleDef main = pawn.royalty.MainTitle();

                    return main != null ? main.GetLabelCapFor(pawn) : null;
                }, null, null);

                y = InspectPaneParts.Fact(view, y, "Title", title.NullOrEmpty() ? "none" : title,
                    title.NullOrEmpty() ? palette.TextDisabled : palette.TextPrimary, palette);
            }

            int served = UIGuard.Try("Inspector.TimeInColony",
                () => pawn.records != null
                    ? Mathf.RoundToInt(pawn.records.GetValue(RecordDefOf.TimeAsColonistOrColonyAnimal))
                    : 0, 0, null);

            if (served > 0)
                y = InspectPaneParts.Fact(view, y, "In the colony",
                    served.ToStringTicksToPeriod(false, false, false), palette.TextSecondary, palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// What kind of human this is, when Biotech is installed.
        ///
        /// <b>Beside Age rather than under Traits,</b> which was the other candidate. A xenotype is not a trait
        /// and it is not a list -- it is the single answer to "what are they", the same shape of fact as their
        /// age and their ideoligion, and it belongs in the run of one-line answers those two are already in.
        ///
        /// <b>Every pawn with genes has one, and Baseliner is a real answer.</b> <c>Xenotype</c> falls back to
        /// Baseliner rather than null, so there is no such thing as a colonist without a xenotype to report and
        /// nothing here needs an "unknown" case. <c>XenotypeLabelCap</c> also covers the two shapes the plain
        /// def cannot: a custom xenotype the player built, and a pawn whose genes match no xenotype at all,
        /// which the game calls unique.
        ///
        /// <b>The description on hover, because the label alone assumes you know the set.</b> Hussar, Genie and
        /// Neanderthal say nothing about what they cost or what they are for, and <c>XenotypeDescShort</c> is
        /// what the game shows for the same question on its own bio tab.
        ///
        /// Absent without Biotech, and absent for anything with no gene tracker -- an animal, a mech -- rather
        /// than reported as Baseliner, which would be a true sentence about a muffalo and a useless one.
        /// </summary>
        private static float Xenotype(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            if (!ModsConfig.BiotechActive || pawn.genes == null)
                return y;

            string label = UIGuard.Try("Inspector.Xenotype", () => pawn.genes.XenotypeLabelCap, null, null);

            if (label.NullOrEmpty())
                return y;

            float before = y;

            y = InspectPaneParts.Fact(view, y, "Xenotype", label, palette.TextPrimary, palette);

            Rect row = new Rect(view.x, before, view.width, y - before);

            if (!Mouse.IsOver(row))
                return y;

            string tip = UIGuard.Try("Inspector.XenotypeTip", () => pawn.genes.XenotypeDescShort, null, null);

            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(row, (TipSignal) tip);

            return y;
        }
    }
}
