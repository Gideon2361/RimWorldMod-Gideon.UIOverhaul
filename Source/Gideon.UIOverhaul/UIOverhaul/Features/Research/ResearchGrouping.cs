using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// What the canvas is cut into blocks along.
    ///
    /// <b>Three, and the old one is still here.</b> Grouping by mod was what shipped in 14162 and it is what
    /// <see cref="Source"/> still does, exactly. That is the whole reason this is an enum rather than a
    /// replacement: the mod-block layout answers "what did this mod add", which is a real question even if it is
    /// rarely the one somebody opened the tab with, and deleting it to gain themes would be trading one lost
    /// question for another.
    /// </summary>
    internal enum ResearchGrouping
    {
        /// <summary>What a project is about, worked out from what it unlocks. See <see cref="ResearchTaxonomy"/>.</summary>
        Theme,

        /// <summary>The mod, DLC or Core it came from. The layout that shipped in 14162.</summary>
        Source,

        /// <summary>Neolithic through Archotech, in the game's own order.</summary>
        Tech
    }

    /// <summary>
    /// The grouping in force, and the key each one sorts a project by.
    ///
    /// <b>One key function per grouping, and nothing else differs.</b> <see cref="ResearchGraph"/> already built
    /// its blocks from an arbitrary string key with an ordering number, so all three groupings are the same layout
    /// code reading a different key -- which is what made this cheap enough to be worth offering rather than
    /// choosing on the player's behalf.
    ///
    /// <b>Stored in the settings file rather than the save.</b> It is a way of looking at a screen, not a fact
    /// about a colony, so it should follow the player between colonies the way the rest of this mod's preferences
    /// do.
    /// </summary>
    internal static class ResearchGroupings
    {
        /// <summary>The three, in the order the toolbar shows them.</summary>
        internal static readonly ResearchGrouping[] All =
        {
            ResearchGrouping.Theme,
            ResearchGrouping.Source,
            ResearchGrouping.Tech
        };

        internal static string LabelOf(ResearchGrouping grouping)
        {
            switch (grouping)
            {
                case ResearchGrouping.Source:
                    return "Source";

                case ResearchGrouping.Tech:
                    return "Tech";

                default:
                    return "Theme";
            }
        }

        internal static string TooltipOf(ResearchGrouping grouping)
        {
            switch (grouping)
            {
                case ResearchGrouping.Source:
                    return "One block per mod, DLC or Core, in load order.\n\nAnswers what a mod added. Related "
                           + "projects end up far apart, because almost every theme in the game is spread across "
                           + "several mods.";

                case ResearchGrouping.Tech:
                    return "One block per tech level, in the game's own order.\n\nAnswers what is reachable at "
                           + "your stage. Says nothing about what anything is for.";

                default:
                    return "One band per subject, worked out from what each project unlocks.\n\nAnswers what a "
                           + "project is for. A mod's own chain can end up split across bands; a cross-band "
                           + "prerequisite is drawn as far as the band edge and named, and the whole route "
                           + "appears when either end is selected.";
            }
        }

        private static ResearchGrouping cached = ResearchGrouping.Theme;

        private static int cachedFrame = -1;

        /// <summary>
        /// What is in force now.
        ///
        /// <b>Cached for the frame, because every node asks.</b> <see cref="ResearchSourceMarks.Wanted"/> consults
        /// this once per node to decide whether to draw a provenance mark, which on a full load order is three
        /// hundred and fifty times a frame -- and the answer cannot change between two of them, since it only
        /// changes when somebody clicks a segment. The frame stamp is the same device the graph uses for its
        /// signature.
        ///
        /// <b>Still the settings file underneath, never a field somebody has to remember to update.</b>
        /// <see cref="Set"/> writes the file and stamps the cache stale in the same breath, so there is no second
        /// answer that can drift from the first.
        /// </summary>
        internal static ResearchGrouping Current
        {
            get
            {
                if (cachedFrame == Time.frameCount)
                    return cached;

                cachedFrame = Time.frameCount;

                cached = UIGuard.Try("Research.ReadGrouping", () =>
                {
                    Options.UIOverhaulSettingsFile settings = Options.UIOverhaulSettingsFile.Current;

                    if (settings == null)
                        return ResearchGrouping.Theme;

                    return Parse(settings.researchGrouping);
                }, ResearchGrouping.Theme, null);

                return cached;
            }
        }

        internal static void Set(ResearchGrouping grouping)
        {
            UIGuard.Try("Research.WriteGrouping", () =>
            {
                Options.UIOverhaulSettingsFile settings = Options.UIOverhaulSettingsFile.Current;

                if (settings == null)
                    return;

                settings.researchGrouping = Store(grouping);
                settings.Save();

                // Before the graph is told, so the rebuild it triggers reads the new value rather than this
                // frame's cached old one.
                cachedFrame = -1;

                // The layout is keyed on the grouping through the graph's signature, so this is all it takes.
                ResearchGraph.Invalidate();
            }, "The research grouping could not be saved. The tab still uses it until the game is restarted.");
        }

        /// <summary>
        /// Stored as a word rather than a number, so a hand-edited settings file reads.
        ///
        /// Unknown values fall back to Theme instead of throwing, which is what happens to a file written by a
        /// newer version of this mod.
        /// </summary>
        internal static ResearchGrouping Parse(string stored)
        {
            if (stored.EqualsIgnoreCase("source"))
                return ResearchGrouping.Source;

            if (stored.EqualsIgnoreCase("tech"))
                return ResearchGrouping.Tech;

            return ResearchGrouping.Theme;
        }

        internal static string Store(ResearchGrouping grouping)
        {
            switch (grouping)
            {
                case ResearchGrouping.Source:
                    return "source";

                case ResearchGrouping.Tech:
                    return "tech";

                default:
                    return "theme";
            }
        }

        /// <summary>
        /// The block a project belongs to under the grouping in force: its name, its sort order, and its color.
        ///
        /// <b>Color is null for two of the three.</b> A band means nothing except "not the band beside it", so a
        /// hue is the only thing that can tell eleven of them apart at a glance. A mod name and a tech level are
        /// both words the player already knows, and coloring them would be decoration pretending to be
        /// information.
        /// </summary>
        internal static void KeyFor(ResearchProjectDef project, ResearchGrouping grouping,
            out string label, out int order, out ResearchBand? band)
        {
            band = null;

            switch (grouping)
            {
                case ResearchGrouping.Source:
                {
                    ModContentPack pack = project == null ? null : project.modContentPack;
                    System.Collections.Generic.List<ModContentPack> running =
                        LoadedModManager.RunningModsListForReading;

                    label = pack == null ? "Other" : pack.Name;
                    order = pack == null || running == null ? int.MaxValue : running.IndexOf(pack);

                    return;
                }

                case ResearchGrouping.Tech:
                {
                    TechLevel level = project == null ? TechLevel.Undefined : project.techLevel;

                    label = level == TechLevel.Undefined ? "Undefined" : level.ToStringHuman().CapitalizeFirst();
                    order = (int) level;

                    return;
                }

                default:
                {
                    ResearchBand found = ResearchTaxonomy.BandOf(project);

                    band = found;
                    label = ResearchBands.LabelOf(found);

                    // The enum's own order, which is the classifier's priority order. That puts Dark Knowledge at
                    // the top and Other at the bottom, and both are where they should be: the first is a band
                    // nobody browses casually, and the last is the one that means "we could not tell".
                    order = (int) found;

                    return;
                }
            }
        }
    }
}
