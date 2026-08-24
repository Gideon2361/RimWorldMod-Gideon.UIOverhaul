using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Mood
{
    /// <summary>
    /// Mood penalties the game charges for a decision the player made on purpose.
    ///
    /// <b>Written onto the thought stages, because a mood number has no seam.</b> A thought's mood is read
    /// straight off <c>def.stages[i].baseMoodEffect</c> every time <c>Thought.MoodOffset</c> runs, by every
    /// caller there is -- the needs tab, the mood bar, the breakdown threshold. Nothing computes it, so there is
    /// nothing to patch; the number is the whole implementation.
    ///
    /// <b>Baselines captured once, before anything is written,</b> for the same reason as everywhere else: a
    /// second read would take our own numbers for the game's, and turning the switch off would then restore
    /// whatever it was last set to instead of what RimWorld ships.
    ///
    /// <b>Nothing here is stored in the save.</b> A memory already on a pawn keeps its stage and picks up the new
    /// number the next time it is asked, which is within a tick -- so a switch thrown mid-game takes effect on
    /// colonists who are already grumbling rather than only on the next night's sleep.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class MoodFixes
    {
        private sealed class Baseline
        {
            internal ThoughtDef Def;

            internal float[] Mood;
        }

        private static bool captured;

        private static Baseline barracks;

        static MoodFixes()
        {
            UIGuard.Try("Mood.Startup", Apply,
                "Mood settings are not applied this session. The game's own numbers are in force.");
        }

        /// <summary>Whether the barracks thought exists on this install to configure.</summary>
        internal static bool BarracksAvailable
        {
            get { return barracks != null; }
        }

        /// <summary>
        /// Writes the current settings onto the thought stages.
        ///
        /// Called at startup, when a mood setting changes, and when the config file is reloaded from disk.
        /// </summary>
        internal static void Apply()
        {
            Capture();

            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

            bool neutral = settings != null && settings.barracksAreNeutral;

            if (barracks == null)
                return;

            List<ThoughtStage> stages = barracks.Def.stages;

            for (int i = 0; i < stages.Count && i < barracks.Mood.Length; i++)
            {
                if (stages[i] == null)
                    continue;

                // The floor rather than a flat zero. Every stage from awful to impressive is a penalty and those
                // are what the switch is for; the top four are a bonus a player earned by building a barracks
                // good enough to be worth sharing, and taking that away is not what "neutral" asks for.
                stages[i].baseMoodEffect =
                    neutral ? Mathf.Max(0f, barracks.Mood[i]) : barracks.Mood[i];
            }
        }

        private static void Capture()
        {
            if (captured)
                return;

            captured = true;

            barracks = Read(ThoughtDefOf.SleptInBarracks);
        }

        private static Baseline Read(ThoughtDef def)
        {
            if (def == null || def.stages == null || def.stages.Count == 0)
                return null;

            float[] mood = new float[def.stages.Count];

            for (int i = 0; i < def.stages.Count; i++)
                mood[i] = def.stages[i] != null ? def.stages[i].baseMoodEffect : 0f;

            return new Baseline { Def = def, Mood = mood };
        }
    }
}
