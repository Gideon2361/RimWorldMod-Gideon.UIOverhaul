using System.Collections.Generic;
using System.Text;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// The recipes somebody has starred in the bill picker, kept across colonies.
    ///
    /// <b>The plant favorites, applied to recipes,</b> and deliberately a separate list rather than a shared one:
    /// a person who always plants rice is making a statement about farming, and one who always smelts slag is
    /// making a different statement about a different bench. Mixing them into one list would mean a plant and a
    /// recipe could collide on a defName and each would float the other to the top of a window it has nothing to
    /// do with.
    ///
    /// <b>Not in the save, for the same reason plant favorites are not.</b> A favorite says how somebody plays,
    /// not what one colony is doing, and a list that had to be rebuilt every new game is a list nobody uses.
    ///
    /// <b>Stored as names rather than as resolved defs,</b> which is what makes it survive a mod being switched
    /// off: an entry naming a recipe that is not loaded sits in the file untouched and starts working again when
    /// the mod comes back.
    /// </summary>
    internal static class RecipeFavorites
    {
        private const char Separator = ',';

        /// <summary>Separates a recipe's defName from the precept that styles it.</summary>
        private const char StyleMark = ':';

        private static HashSet<string> names;

        /// <summary>
        /// What the set was built from.
        ///
        /// Keyed on the settings string rather than on a dirty flag, so a file edited by hand or reloaded by the
        /// options window is picked up without anything having to announce it.
        /// </summary>
        private static string builtFrom;

        private static HashSet<string> Names
        {
            get
            {
                string stored = UIGuard.Try("Bills.ReadFavorites",
                    () => UIOverhaulSettingsFile.Current?.favoriteRecipes ?? string.Empty, string.Empty,
                    "Favorite recipes are not shown this session.");

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

        /// <summary>
        /// The stored name for one entry in the picker.
        ///
        /// The style is part of it because the same recipe appears once per ideology style it has, and those are
        /// separate choices: starring the one that builds your ideoligion's altar should not star the plain one.
        /// </summary>
        private static string KeyOf(RecipeOffer offer)
        {
            string recipe = offer?.Recipe?.defName;

            if (recipe.NullOrEmpty())
                return null;

            string style = offer.Style?.def?.defName;

            return style.NullOrEmpty() ? recipe : recipe + StyleMark + style;
        }

        internal static bool IsFavorite(RecipeOffer offer)
        {
            string key = KeyOf(offer);

            return key != null && Names.Contains(key);
        }

        /// <summary>Stars or unstars an entry, and writes the file.</summary>
        internal static void Toggle(RecipeOffer offer)
        {
            string key = KeyOf(offer);

            if (key == null)
                return;

            UIGuard.Try("Bills.WriteFavorites", () =>
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                if (settings == null)
                    return;

                HashSet<string> current = Names;

                if (!current.Remove(key))
                    current.Add(key);

                StringBuilder text = new StringBuilder();

                foreach (string name in current)
                {
                    if (text.Length > 0)
                        text.Append(Separator);

                    text.Append(name);
                }

                settings.favoriteRecipes = text.ToString();

                // Written back so the rebuild above does not fire on the next read and throw away the set just
                // edited. Saved here rather than batched, because the click has to stick even if the window is
                // closed immediately afterwards.
                builtFrom = settings.favoriteRecipes;
                settings.Save();
            }, "That favorite could not be saved and is forgotten when the game restarts.");
        }

        /// <summary>
        /// Orders the picker's list with favorites first, leaving the order within each group alone.
        ///
        /// <b>A stable partition rather than a sort.</b> The list arrives in the bench def's own recipe order,
        /// which is meaningful, and a comparison sort keyed on favorite status would be free to shuffle
        /// everything inside each group. Two passes preserve it exactly.
        /// </summary>
        internal static List<RecipeOffer> Ordered(List<RecipeOffer> offers)
        {
            if (offers == null || offers.Count == 0 || Names.Count == 0)
                return offers;

            List<RecipeOffer> ordered = new List<RecipeOffer>(offers.Count);

            foreach (RecipeOffer offer in offers)
            {
                if (IsFavorite(offer))
                    ordered.Add(offer);
            }

            // Nothing here is starred, so the caller's own list is already the answer and copying it would be an
            // allocation per frame for no change.
            if (ordered.Count == 0)
                return offers;

            foreach (RecipeOffer offer in offers)
            {
                if (!IsFavorite(offer))
                    ordered.Add(offer);
            }

            return ordered;
        }
    }
}
