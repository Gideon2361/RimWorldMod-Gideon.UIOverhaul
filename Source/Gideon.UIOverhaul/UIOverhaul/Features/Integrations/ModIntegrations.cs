using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Integrations
{
    /// <summary>
    /// Which other mods this one has something extra to offer alongside.
    ///
    /// <b>An integration is not a patch of another mod's behavior.</b> Everything here adds something that mod
    /// chose not to do, using whatever it made public, and does nothing at all when that mod is absent. That is
    /// the difference between this and <c>NotificationCompatibility</c>, which stands our own features <i>down</i>
    /// when somebody else already owns a surface.
    ///
    /// <b>Detection is by package id and is decided once.</b> The mod list cannot change without a restart, so
    /// there is nothing to re-check, and a lookup per frame on a draw path would be a cost paid forever for an
    /// answer that never moves.
    /// </summary>
    internal static class ModIntegrations
    {
        /// <summary>
        /// Phinix, the in-game chat and trading client. Lowercase, because that is how RimWorld normalizes a
        /// package id.
        /// </summary>
        internal const string PhinixPackageId = "thomotron.phinix";

        private static Dictionary<string, bool> present;

        /// <summary>Whether a mod with this package id is loaded.</summary>
        internal static bool Loaded(string packageId)
        {
            return UIGuard.Try("Integrations.Detect", () => Detect(packageId), false,
                "One mod integration is switched off because the mod list could not be read.");
        }

        /// <summary>Whether this mod has anything to offer alongside what is installed.</summary>
        internal static bool AnyAvailable => Loaded(PhinixPackageId);

        private static bool Detect(string packageId)
        {
            if (present == null)
                present = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            bool found;

            if (present.TryGetValue(packageId, out found))
                return found;

            found = false;

            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;

            if (mods != null)
            {
                foreach (ModContentPack mod in mods)
                {
                    if (mod?.PackageId == null
                        || !mod.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    found = true;

                    break;
                }
            }

            present[packageId] = found;

            return found;
        }
    }
}
