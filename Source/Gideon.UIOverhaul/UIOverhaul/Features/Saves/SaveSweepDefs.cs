using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// Decides which of the def names a save mentions no longer exist.
    ///
    /// <b>Split from the scanner on purpose.</b> <see cref="SaveSweepScan"/> reads a file and knows nothing about
    /// the game, which is what lets it be run against a save from a test harness. Whether <c>BionicArm</c> is still
    /// a def is a question only the loaded game can answer, so that half lives here.
    ///
    /// <b>The answer depends on the mod list, and that is not a defect.</b> A def is missing because the mod
    /// providing it is not active right now. Turn that mod back on and the same save reports nothing. The window
    /// says so next to the count, because a player who has temporarily disabled a mod must not be told their
    /// colony is broken.
    ///
    /// <b>A kind this cannot resolve is passed over in silence.</b> Reporting a def as missing is an invitation to
    /// delete the record holding it, so an unrecognised kind produces no finding at all rather than a guess.
    /// </summary>
    internal static class SaveSweepDefs
    {
        /// <summary>
        /// How to ask the game whether one name still resolves, per kind of def.
        ///
        /// <b>A table of closures rather than reflection.</b> <c>DefDatabase</c> is generic over the def type, so the
        /// type has to be known when the code is compiled. Naming each one here costs a line apiece and keeps the
        /// lookup a direct call, which matters when it runs across a thousand names.
        /// </summary>
        private static readonly Dictionary<string, Func<string, bool>> Lookups =
            new Dictionary<string, Func<string, bool>>(StringComparer.Ordinal)
            {
                { "ThingDef", name => DefDatabase<ThingDef>.GetNamedSilentFail(name) != null },
                { "HediffDef", name => DefDatabase<HediffDef>.GetNamedSilentFail(name) != null },
                { "TraitDef", name => DefDatabase<TraitDef>.GetNamedSilentFail(name) != null },
                { "ThoughtDef", name => DefDatabase<ThoughtDef>.GetNamedSilentFail(name) != null },
                { "GeneDef", name => DefDatabase<GeneDef>.GetNamedSilentFail(name) != null },
                { "AbilityDef", name => DefDatabase<AbilityDef>.GetNamedSilentFail(name) != null },
                { "NeedDef", name => DefDatabase<NeedDef>.GetNamedSilentFail(name) != null },
                { "SkillDef", name => DefDatabase<SkillDef>.GetNamedSilentFail(name) != null },
                { "TaleDef", name => DefDatabase<TaleDef>.GetNamedSilentFail(name) != null }
            };

        /// <summary>
        /// The names in <paramref name="report"/> that no longer resolve, grouped by kind.
        ///
        /// An empty result is the healthy answer and the common one. A kind with no entry means either every name
        /// resolved or the kind is not one this can check; the two are deliberately indistinguishable to the caller,
        /// because neither is a finding.
        /// </summary>
        internal static Dictionary<string, HashSet<string>> Missing(SaveSweepReport report)
        {
            return UIGuard.Try("Saves.Sweep.Defs", () => Resolve(report),
                new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
                "The save's defs could not be checked, so no broken references are listed.");
        }

        private static Dictionary<string, HashSet<string>> Resolve(SaveSweepReport report)
        {
            Dictionary<string, HashSet<string>> missing =
                new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            if (report == null || report.DefNames == null)
                return missing;

            foreach (KeyValuePair<string, HashSet<string>> pair in report.DefNames)
            {
                if (!Lookups.TryGetValue(pair.Key, out Func<string, bool> exists))
                    continue;

                foreach (string name in pair.Value)
                {
                    // A null def is written as the literal "null" by the scribe, and means the field was empty
                    // rather than pointing at something that has gone. Nothing to report and nothing to remove.
                    if (name == "null" || exists(name))
                        continue;

                    if (!missing.TryGetValue(pair.Key, out HashSet<string> gone))
                    {
                        gone = new HashSet<string>(StringComparer.Ordinal);
                        missing[pair.Key] = gone;
                    }

                    gone.Add(name);
                }
            }

            return missing;
        }

        /// <summary>How many names are missing in total, which is the number the window leads with.</summary>
        internal static int Count(Dictionary<string, HashSet<string>> missing)
        {
            int total = 0;

            if (missing == null)
                return 0;

            foreach (KeyValuePair<string, HashSet<string>> pair in missing)
                total += pair.Value.Count;

            return total;
        }
    }
}
