using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// Projects and research tabs whose band we name outright, because nothing computable says it.
    ///
    /// <b>Why this exists, from the scan of 2026-08-23.</b> Across Aaron's 198 active mods, 42 add research and
    /// 190 projects came back. Mycohazard's four set <c>knowledgeCategory</c> and land in Dark Knowledge by rule
    /// with no help at all -- that is what a good rule looks like. <b>One with Death's twenty-nine set nothing:</b>
    /// no knowledge category, no Anomaly tab, tech level Neolithic throughout, and a tab of their own called
    /// Necromancy. Read by what they unlock they scatter -- Undead Forge to Production, undead implants and Neural
    /// Shackle to Medicine, Soul Shards and artifacts to Other -- and Aaron's instruction was that they belong
    /// together in Dark Knowledge. The Profaned is the same shape at three projects.
    ///
    /// So there is a table. This is the same answer the raid filters gave to the same problem: some content cannot
    /// be recognised by a rule and has to be named, and naming it in one visible place beats a rule bent until it
    /// happens to catch it.
    ///
    /// <b>Tab keys before project keys, and both before every other test.</b> A tab key covers a whole mod in one
    /// line and keeps covering it when that mod adds a project next month, which a list of defNames does not. A
    /// project key is for the exception inside an exception.
    ///
    /// <b>Nothing here is a setting.</b> A player who disagrees is disagreeing about what a word means, not about
    /// how they want to play, and a per-project band picker would be a screen nobody opens twice. Mods redirect
    /// their own content by patching this file's defName strings -- see <see cref="Extra"/>, which exists so a
    /// mod's own patch has somewhere to land without editing our source.
    /// </summary>
    internal static class ResearchBandOverrides
    {
        /// <summary>
        /// Research tabs whose every project belongs to one band.
        ///
        /// Keyed by <c>ResearchTabDef.defName</c>, which is what a mod author wrote and cannot change without
        /// changing their own XML.
        /// </summary>
        private static readonly Dictionary<string, ResearchBand> byTab =
            new Dictionary<string, ResearchBand>
            {
                // One with Death. Aaron's call of 2026-08-23; see the class note for why no rule can reach it.
                { "Necromancy", ResearchBand.DarkKnowledge },

                // The Profaned. Three projects -- basic and advanced profane insight, profane alchemy -- on their
                // own tab, and "insight" is the same mechanic Anomaly calls knowledge without using its def.
                { "BotchJob_ProfanedResearchTab", ResearchBand.DarkKnowledge }
            };

        /// <summary>
        /// Single projects whose band is named, when the tab they sit on is not all one thing.
        ///
        /// Empty at the time of writing, and that is worth leaving visibly empty: every case the scan turned up
        /// was a whole tab. A project key is here for the one that is not.
        /// </summary>
        private static readonly Dictionary<string, ResearchBand> byProject =
            new Dictionary<string, ResearchBand>();

        /// <summary>
        /// Room for a mod to redirect its own research without touching our source.
        ///
        /// Filled by anybody who wants to, at any time before the graph is first built, and cleared by nobody.
        /// A key added here wins over <see cref="byTab"/> and <see cref="byProject"/>, because a mod knows its own
        /// content better than our table does.
        /// </summary>
        internal static readonly Dictionary<string, ResearchBand> Extra =
            new Dictionary<string, ResearchBand>();

        /// <summary>
        /// The named band for this project, or null when nothing names it.
        ///
        /// Project keys beat tab keys: a table entry for one defName is a deliberate exception to whatever its tab
        /// says, and would be pointless if the tab won.
        /// </summary>
        internal static ResearchBand? For(ResearchProjectDef project)
        {
            return UIGuard.Try("Research.BandOverride", () =>
            {
                if (project == null)
                    return (ResearchBand?) null;

                ResearchBand found;

                if (!project.defName.NullOrEmpty())
                {
                    if (Extra.TryGetValue(project.defName, out found))
                        return found;

                    if (byProject.TryGetValue(project.defName, out found))
                        return found;
                }

                string tab = project.tab == null ? null : project.tab.defName;

                if (tab.NullOrEmpty())
                    return (ResearchBand?) null;

                if (Extra.TryGetValue(tab, out found))
                    return found;

                return byTab.TryGetValue(tab, out found) ? found : (ResearchBand?) null;
            }, null, null);
        }

        /// <summary>Why this project was overridden, for the detail panel and the dev listing.</summary>
        internal static string ReasonFor(ResearchProjectDef project)
        {
            if (project == null)
                return null;

            if (!project.defName.NullOrEmpty()
                && (Extra.ContainsKey(project.defName) || byProject.ContainsKey(project.defName)))
                return "Named by this mod's override table, by project.";

            string tab = project.tab == null ? null : project.tab.defName;

            if (!tab.NullOrEmpty() && (Extra.ContainsKey(tab) || byTab.ContainsKey(tab)))
                return "Named by this mod's override table, because every project on the "
                       + (project.tab.label.NullOrEmpty() ? tab : project.tab.label) + " tab belongs here.";

            return null;
        }
    }
}
