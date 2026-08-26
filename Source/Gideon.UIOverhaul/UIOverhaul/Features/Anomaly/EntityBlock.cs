using System;
using System.Globalization;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Inspector;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Anomaly
{
    /// <summary>
    /// What the inspect pane says about an anomaly entity.
    ///
    /// <b>The pane was nearly empty for one, and the cause was that an entity reads as an animal.</b>
    /// <c>RaceProps.Animal</c> is <c>intelligence == Animal &amp;&amp; !IsMechanoid</c>, which a noctol satisfies, so
    /// the overview sent it down the animal path and drew a training block for something that cannot be trained,
    /// a master and allowed area for something with no player faction, and a butcher yield. Three blocks that
    /// each correctly decided they had nothing to say, which added up to a blank panel. Reported 2026-08-25.
    ///
    /// <b>So entities get their own branch and their own block.</b> The questions worth answering about one are
    /// not the questions about a cow: how far the study has got, whether the thing is contained strongly enough
    /// to stay put, and how close it is to acting.
    ///
    /// <b>Everything here is read from the comps rather than recomputed.</b> <c>CompStudiable</c> owns the study
    /// progress, <c>CompHoldingPlatformTarget</c> knows which platform holds it, and <c>CompActivity</c> owns the
    /// activity level and the threshold suppression works to. Containment is the one comparison this makes
    /// itself, and it is a comparison of two stats the game already publishes.
    ///
    /// A comp that is absent is a row that is not drawn. Not every entity is studiable, not every one can be
    /// held, and only some have an activity level, so the block is whatever this particular thing actually has.
    /// </summary>
    internal static class EntityBlock
    {
        /// <summary>
        /// Draws the block and returns the new y. Returns <paramref name="y"/> untouched when there is nothing
        /// to say, which keeps the caller from opening a heading over an empty space.
        /// </summary>
        internal static float Draw(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            if (pawn == null)
                return y;

            return UIGuard.Try("Anomaly.EntityBlock", () => Rows(view, y, pawn, palette), y,
                "The inspect pane's entity block is not shown.");
        }

        private static float Rows(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            CompStudiable study = pawn.TryGetComp<CompStudiable>();
            CompHoldingPlatformTarget held = pawn.TryGetComp<CompHoldingPlatformTarget>();
            CompActivity activity = pawn.TryGetComp<CompActivity>();

            if (study == null && held == null && activity == null)
                return y;

            float start = y;

            y = InspectPaneParts.Cap(view, y, "Entity", null, palette);

            if (study != null)
                y = Study(view, y, pawn, study, palette);

            if (activity != null)
                y = Activity(view, y, activity, palette);

            if (held != null)
                y = Containment(view, y, pawn, held, palette);

            return y > start ? y + InspectPaneParts.BlockGap : start;
        }

        /// <summary>
        /// The same facts read from the platform rather than from the thing on it.
        ///
        /// <b>Selecting the platform and selecting what is chained to it are different clicks and were very
        /// different panes.</b> The entity gets everything below; the platform got a health bar, a market value
        /// and three lines of raw inspect string at the foot of the window -- containment strength, who is on it,
        /// and when it can next be studied. Asked for on 2026-08-25, and they are the right facts: a platform is
        /// only interesting because of what is on it, and the question somebody clicking one has is whether it is
        /// strong enough and whether there is anything to do about the occupant.
        ///
        /// <b>The strength is read from the holder comp, not from the stat directly.</b> <c>ContainmentStrength</c>
        /// on <see cref="CompEntityHolder"/> is virtual, so a modded platform that computes its own strength is
        /// asked rather than measured, and it is the number vanilla's own <c>SafelyContains</c> compares against.
        /// Falling back to the stat when there is no comp keeps a platform from a mod that skipped it working.
        ///
        /// <b>Why the room conditions are stated.</b> Being outdoors or having a door pinned open cuts the stat,
        /// and vanilla mentions it in a parenthesis at the end of a line. It is the difference between a platform
        /// that is too weak and one that is only too weak <i>here</i>, which is the whole of what to do next.
        /// </summary>
        internal static float Platform(Rect view, float y, Building_HoldingPlatform platform,
            UIColorPaletteDef palette)
        {
            if (platform == null)
                return y;

            return UIGuard.Try("Anomaly.PlatformBlock", () => PlatformRows(view, y, platform, palette), y,
                "The inspect pane's containment block is not shown.");
        }

        private static float PlatformRows(Rect view, float y, Building_HoldingPlatform platform,
            UIColorPaletteDef palette)
        {
            Pawn held = platform.HeldPawn;

            CompEntityHolder holder = platform.TryGetComp<CompEntityHolder>();

            float strength = holder != null
                ? holder.ContainmentStrength
                : platform.GetStatValue(StatDefOf.ContainmentStrength);

            y = InspectPaneParts.Cap(view, y, "Containment", Mathf.RoundToInt(strength).ToString(), palette);

            if (held == null)
            {
                y = InspectPaneParts.Fact(view, y, "Holding", "Nothing".Translate().CapitalizeFirst(),
                    palette.TextDisabled, palette);

                y = Conditions(view, y, platform, palette);
                y = Breakdown(view, y, platform, palette);

                return y + InspectPaneParts.BlockGap;
            }

            float required = held.GetStatValue(StatDefOf.MinimumContainmentStrength);

            bool enough = strength >= required;

            y = InspectPaneParts.Fact(view, y, "Holding", held.LabelShortCap,
                enough ? palette.TextPrimary : palette.Danger, palette);

            // Full once the platform is strong enough: past the requirement the surplus buys nothing, so a bar
            // that kept climbing would imply a margin that does not exist. Same reasoning as the entity's own
            // containment row, which this is the other half of.
            float fraction = required <= 0f ? 1f : Mathf.Clamp01(strength / required);

            Color tone = enough ? palette.Success : palette.Danger;

            y = InspectPaneParts.Need(view, y, "Strength",
                Mathf.RoundToInt(strength) + " of " + Mathf.RoundToInt(required), tone, fraction, tone, null,
                enough ? null : "too weak to hold this", palette);

            y = Conditions(view, y, platform, palette);
            y = Breakdown(view, y, platform, palette);

            CompHoldingPlatformTarget target = held.TryGetComp<CompHoldingPlatformTarget>();

            if (target != null && target.isEscaping)
                y = InspectPaneParts.Fact(view, y, "Occupant", "escaping", palette.Danger, palette);

            CompStudiable study = held.TryGetComp<CompStudiable>();

            if (study != null)
                y = Study(view, y, held, study, palette);

            CompActivity activity = held.TryGetComp<CompActivity>();

            if (activity != null)
                y = Activity(view, y, activity, palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// The two room conditions that cut a platform's strength, said only when one of them applies.
        ///
        /// Both are vanilla's own tests rather than our reading of a room: <c>IsOutside</c> and
        /// <c>StatWorker_ContainmentStrength.AnyDoorForcedOpen</c> are what the stat worker itself consults, so a
        /// platform this calls compromised is compromised by the game's reckoning.
        /// </summary>
        private static float Conditions(Rect view, float y, Building_HoldingPlatform platform,
            UIColorPaletteDef palette)
        {
            if (!platform.Spawned)
                return y;

            if (platform.IsOutside())
            {
                return InspectPaneParts.Fact(view, y, "Room", "Outdoors".Translate(), palette.Warning, palette);
            }

            if (StatWorker_ContainmentStrength.AnyDoorForcedOpen(platform.GetRoom()))
            {
                return InspectPaneParts.Fact(view, y, "Room",
                    "Stat_ContainmentStrength_DoorForcedOpen".Translate(), palette.Warning, palette);
            }

            return y;
        }

        /// <summary>
        /// What the containment number is actually made of: lighting, walls, doors, floor, and the rest.
        ///
        /// <b>Read from the stat's own explanation rather than worked out again.</b>
        /// <c>StatWorker_ContainmentStrength</c> computes all of this in <c>CalculateValues</c>, which is private,
        /// as is the struct it returns -- so the choice was to reimplement the arithmetic or to ask the game what
        /// it already decided. Reimplementing it would mean owning a copy of curves, a 0.9 per-platform falloff
        /// and a -30 roof penalty that Ludeon can change in a patch, and being quietly wrong the day they do.
        /// <c>GetExplanationUnfinalized</c> is public, is what the info card shows, and hands back exactly these
        /// factors already worded and already translated.
        ///
        /// <b>So what is written here is a layout, not a calculation.</b> One line becomes one row, the label on
        /// the left and the number on the right, coloured by whether it is helping or hurting -- which is the
        /// question the wall of text in the info card makes you work out for yourself.
        ///
        /// <b>Nothing at all outdoors, and that is correct.</b> The stat worker returns an empty set of values for
        /// a room that is psychologically outdoors or touches the map edge, because none of these factors apply
        /// there. <see cref="Conditions"/> is what says so in words, which is why it stays.
        /// </summary>
        private static float Breakdown(Rect view, float y, Building_HoldingPlatform platform,
            UIColorPaletteDef palette)
        {
            if (!platform.Spawned)
                return y;

            string explanation = UIGuard.Try("Anomaly.ContainmentExplanation",
                () => StatDefOf.ContainmentStrength.Worker.GetExplanationUnfinalized(
                    StatRequest.For(platform), ToStringNumberSense.Absolute), null, null);

            if (explanation.NullOrEmpty())
                return y;

            string[] lines = explanation.Split('\n');

            bool captioned = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (line.Length == 0)
                    continue;

                // Split at the last colon rather than the first: the value never contains one, and a translated
                // label might.
                int split = line.LastIndexOf(": ", StringComparison.Ordinal);

                if (split <= 0 || split + 2 >= line.Length)
                    continue;

                string label = line.Substring(0, split).Trim();
                string value = line.Substring(split + 2).Trim();

                if (label.Length == 0 || value.Length == 0)
                    continue;

                if (!captioned)
                {
                    y = InspectPaneParts.Cap(view, y, "Made up of", null, palette);

                    captioned = true;
                }

                y = InspectPaneParts.Fact(view, y, label, value, Tone(value, palette), palette);
            }

            return y;
        }

        /// <summary>
        /// Green for what is helping, red for what is costing, plain for a multiplier.
        ///
        /// <b>Parsed in the current culture first.</b> The stat worker formats these with <c>{0:F2}</c>, which
        /// follows the player's locale -- so a German game writes "-30,00" and an invariant parse would read that
        /// as nothing at all and colour a penalty as neutral. Invariant is the fallback rather than the rule.
        /// </summary>
        private static Color Tone(string value, UIColorPaletteDef palette)
        {
            // A multiplier is neither a gain nor a loss on its own: x0.90 is a penalty and x1.10 is not, but the
            // number it acts on is every row above it, so colouring it either way would overstate it.
            if (value.StartsWith("x", StringComparison.OrdinalIgnoreCase))
                return palette.TextSecondary;

            int end = value.IndexOf(' ');
            string number = end > 0 ? value.Substring(0, end) : value;

            float parsed;

            if (!float.TryParse(number, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed)
                && !float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return palette.TextSecondary;
            }

            if (parsed > 0f)
                return palette.Success;

            return parsed < 0f ? palette.Danger : palette.TextSecondary;
        }

        /// <summary>
        /// How far the study has got, and what is holding it up.
        ///
        /// <b>The waiting is worth as much as the progress.</b> A studiable entity is only studiable every so
        /// often -- <c>Props.frequencyTicks</c> -- so "43%" on its own does not answer the question somebody
        /// selecting it is actually asking, which is whether there is anything to do about it right now.
        /// </summary>
        private static float Study(Rect view, float y, Pawn pawn, CompStudiable study,
            UIColorPaletteDef palette)
        {
            if (study.Completed)
            {
                y = InspectPaneParts.Fact(view, y, "Study", "complete", palette.Success, palette);
            }
            else
            {
                float progress = Mathf.Clamp01(study.ProgressPercent);

                int wait = study.TicksTilNextStudy;

                string note = wait > 0
                    ? "next in " + wait.ToStringTicksToPeriod()
                    : "ready to study";

                y = InspectPaneParts.Need(view, y, "Study", InspectPaneParts.Percent(progress),
                    palette.TextPrimary, progress, palette.Info, null, note, palette);
            }

            // Switched off deliberately is a different state from not yet studied, and the pane should not let
            // somebody mistake one for the other while they wait for progress that is never coming.
            if (!study.studyEnabled)
                y = InspectPaneParts.Fact(view, y, "Studying", "switched off", palette.Warning, palette);

            KnowledgeCategoryDef category = study.KnowledgeCategory;

            if (category != null)
                y = InspectPaneParts.Fact(view, y, "Knowledge", category.LabelCap, palette.TextSecondary,
                    palette);

            // Ours, and the reason this row exists at all: with an assignment set, nobody else will study or
            // suppress the thing, and that is invisible everywhere else once the picker is closed.
            Pawn assigned = StudyAssignments.AssignedTo(pawn);

            if (assigned != null)
                y = InspectPaneParts.Fact(view, y, "Assigned to", assigned.LabelShortCap, palette.Accent,
                    palette);

            return y;
        }

        /// <summary>
        /// How close this is to doing something, against the level suppression works to hold it under.
        ///
        /// The threshold rides on the bar rather than being stated beside it, for the reason the mood bar carries
        /// its break thresholds: a number is only meaningful against the line it is about to cross.
        /// </summary>
        private static float Activity(Rect view, float y, CompActivity activity, UIColorPaletteDef palette)
        {
            if (activity.Deactivated)
                return InspectPaneParts.Fact(view, y, "Activity", "deactivated", palette.TextDisabled, palette);

            float level = Mathf.Clamp01(activity.ActivityLevel);
            float threshold = Mathf.Clamp01(activity.suppressIfAbove);

            Color fill = level > threshold ? palette.Danger : palette.Warning;

            y = InspectPaneParts.Need(view, y, "Activity", InspectPaneParts.Percent(level), fill, level, fill,
                new[] { threshold }, activity.IsActive ? "active" : null, palette);

            if (!activity.suppressionEnabled && activity.CanBeSuppressed)
                y = InspectPaneParts.Fact(view, y, "Suppression", "switched off", palette.Warning, palette);

            return y;
        }

        /// <summary>
        /// Whether the platform holding this is strong enough to keep it.
        ///
        /// <b>The one comparison this file makes rather than reads,</b> and both halves are stats the game
        /// publishes: <c>MinimumContainmentStrength</c> on the entity is what it demands, and
        /// <c>ContainmentStrength</c> on the platform is what that platform provides. Under-contained is the
        /// state that produces an escape, so it is drawn in the danger tone rather than left as two numbers for
        /// the reader to compare.
        /// </summary>
        private static float Containment(Rect view, float y, Pawn pawn, CompHoldingPlatformTarget held,
            UIColorPaletteDef palette)
        {
            if (held.isEscaping)
                y = InspectPaneParts.Fact(view, y, "Containment", "escaping", palette.Danger, palette);

            Building_HoldingPlatform platform = held.HeldPlatform;

            if (platform == null)
                return InspectPaneParts.Fact(view, y, "Held", "not on a platform", palette.TextSecondary,
                    palette);

            float required = pawn.GetStatValue(StatDefOf.MinimumContainmentStrength);
            float actual = platform.GetStatValue(StatDefOf.ContainmentStrength);

            bool enough = actual >= required;

            string value = Mathf.RoundToInt(actual) + " of " + Mathf.RoundToInt(required);

            // Full bar once the platform is strong enough: past the requirement the surplus buys nothing, so a
            // bar that kept climbing would imply a margin that does not exist.
            float fraction = required <= 0f ? 1f : Mathf.Clamp01(actual / required);

            Color tone = enough ? palette.Success : palette.Danger;

            return InspectPaneParts.Need(view, y, "Containment", value, tone, fraction, tone, null,
                enough ? null : "too weak to hold this", palette);
        }
    }
}
