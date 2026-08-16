using System.Collections.Generic;
using System.Text;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones
{
    /// <summary>
    /// The plants somebody has flagged as favorites, kept across colonies.
    ///
    /// <b>Why this is not in the save.</b> A favorite says something about how a person plays, not about one
    /// colony: whoever always plants rice and healroot wants that list on the next map too. Scribed into the
    /// save it would have to be rebuilt every new game, which is the point at which nobody bothers using it.
    /// So it lives in the mod's config file beside the rest of the preferences.
    ///
    /// <b>Kept as defNames rather than as <c>ThingDef</c>s,</b> which is what makes it survive a mod being
    /// switched off. A favorite naming a plant that is not currently loaded stays in the file untouched and
    /// starts working again when the mod comes back, where a resolved reference would have to be dropped on
    /// load and silently lost.
    ///
    /// The in-memory copy is a set built once from the settings string, because this is asked per plant per
    /// frame while the picker is open and splitting a string that often would be absurd.
    /// </summary>
    internal static class PlantFavorites
    {
        private const char Separator = ',';

        private static HashSet<string> names;

        /// <summary>
        /// What the set was built from.
        ///
        /// The rebuild is keyed on the settings string rather than on a dirty flag, so a config file edited
        /// by hand or reloaded by the options window is picked up without anything having to announce it.
        /// </summary>
        private static string builtFrom;

        private static HashSet<string> Names
        {
            get
            {
                string stored = UIGuard.Try("GrowZones.ReadFavorites",
                    () => UIOverhaulSettingsFile.Current?.favoritePlants ?? string.Empty, string.Empty,
                    "Favorite plants are not shown this session.");

                if (names != null && stored == builtFrom)
                    return names;

                builtFrom = stored;
                names = new HashSet<string>();

                foreach (string entry in stored.Split(Separator))
                {
                    string trimmed = entry.Trim();

                    if (!trimmed.NullOrEmpty())
                        names.Add(trimmed);
                }

                return names;
            }
        }

        internal static bool IsFavorite(ThingDef plant)
        {
            return plant != null && !plant.defName.NullOrEmpty() && Names.Contains(plant.defName);
        }

        /// <summary>Flags or unflags a plant, and writes the file.</summary>
        internal static void Toggle(ThingDef plant)
        {
            if (plant == null || plant.defName.NullOrEmpty())
                return;

            UIGuard.Try("GrowZones.WriteFavorites", () =>
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                if (settings == null)
                    return;

                HashSet<string> current = Names;

                if (!current.Remove(plant.defName))
                    current.Add(plant.defName);

                StringBuilder text = new StringBuilder();

                foreach (string name in current)
                {
                    if (text.Length > 0)
                        text.Append(Separator);

                    text.Append(name);
                }

                settings.favoritePlants = text.ToString();

                // Written back so the rebuild above does not fire on the next read and throw away the set we
                // have just edited. Saving is what makes the click stick, so it happens here rather than being
                // batched: the picker closes as soon as a bill is added.
                builtFrom = settings.favoritePlants;
                settings.Save();
            }, "That favorite could not be saved and is forgotten when the game restarts.");
        }

        /// <summary>
        /// Orders a plant list with favorites first, leaving the order within each group alone.
        ///
        /// <b>A stable partition rather than a sort,</b> and the distinction matters: the list arrives in the
        /// order the growing zone offers it, which is meaningful, and a comparison sort keyed on favorite
        /// status would be free to shuffle everything inside each group. Two passes preserve it exactly.
        /// </summary>
        internal static List<ThingDef> Ordered(List<ThingDef> plants)
        {
            if (plants == null || plants.Count == 0 || Names.Count == 0)
                return plants;

            List<ThingDef> ordered = new List<ThingDef>(plants.Count);

            foreach (ThingDef plant in plants)
            {
                if (IsFavorite(plant))
                    ordered.Add(plant);
            }

            // Nothing in this list is flagged, so the caller's own list is already the answer and copying it
            // would be a per-frame allocation for no change.
            if (ordered.Count == 0)
                return plants;

            foreach (ThingDef plant in plants)
            {
                if (!IsFavorite(plant))
                    ordered.Add(plant);
            }

            return ordered;
        }
    }
}
