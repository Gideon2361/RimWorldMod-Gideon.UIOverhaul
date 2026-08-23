using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

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

            return (split ? Mathf.Max(leftY, secondY) : secondY) - view.y;
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

            string description = UIGuard.Try("Inspector.BackstoryText",
                () => story.FullDescriptionFor(pawn).Resolve(), null, null);

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

        /// <summary>Age, faith, rank and time served: the facts that are about position rather than history.</summary>
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
    }
}
