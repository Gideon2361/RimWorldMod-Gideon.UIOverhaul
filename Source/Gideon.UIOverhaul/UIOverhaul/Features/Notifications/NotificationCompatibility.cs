using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Notifications
{
    /// <summary>
    /// Stands this mod's notification patches down when another mod already owns those surfaces.
    ///
    /// <b>The problem is specific rather than general.</b> Modern Notifications replaces the drawing of messages,
    /// alerts and letters with a replacing prefix, and so does this mod. Two replacing prefixes on one method do not
    /// merge -- whichever Harmony runs first returns false and the other never runs, decided by mod load order. The
    /// player would get one mod's messages and possibly the other's alerts, with no way to tell why.
    ///
    /// <b>Standing down per surface rather than declaring a hard incompatibility.</b> An
    /// <c>incompatibleWith</c> entry is what this mod uses for Clean Architect and Growing Zones Plus, and it is
    /// right there because those overlap almost completely. This overlap is partial: Modern Notifications also
    /// rebuilds the bottom right panel and adds a calendar, neither of which this mod replaces, so a player may
    /// perfectly reasonably want both. Refusing to run alongside it would take those away to solve a conflict that
    /// only affects three methods.
    ///
    /// <b>This is not the guard that was wrong before.</b> The workshop upload patch deliberately has no
    /// stand-down, because standing down there would have left <i>nothing</i> handling the dialog -- the other mod
    /// was this mod's own, and the patch had been removed from it. Here, standing down leaves a working, maintained
    /// implementation in charge of the surface. The test is not "is another mod present" but "will the thing still
    /// be handled if we step aside".
    ///
    /// <b>Decided once, before any patching.</b> Harmony calls <c>Prepare</c> when the patch class is processed at
    /// startup, so a surface we stand down on is never patched at all rather than patched and then skipped every
    /// frame. The mod list cannot change without a restart, so there is nothing to re-check.
    /// </summary>
    internal static class NotificationCompatibility
    {
        /// <summary>
        /// Mods that own the notification surfaces, by package id.
        ///
        /// A list rather than one constant because this is a category, not a special case: the next mod that
        /// replaces these three methods should be one line here rather than another copy of the reasoning above.
        /// Lowercase, because that is how RimWorld normalizes a package id.
        /// </summary>
        private static readonly string[] Owners =
        {
            "astryl.modernnotifications"
        };

        private static bool? standDown;

        /// <summary>
        /// Whether another mod is already drawing these surfaces.
        ///
        /// Latched, because it is read once per patch class and the answer cannot change while the game is running.
        /// Guarded, because it runs during startup patching, where an exception does not produce a mod with plain
        /// notifications -- it produces a Harmony patch that failed to apply, and a log the player has to interpret.
        /// </summary>
        internal static bool AnotherModOwnsNotifications
        {
            get
            {
                if (standDown.HasValue)
                    return standDown.Value;

                standDown = UIGuard.Try("Notifications.DetectOwner", Detect, false,
                    "Notification surfaces are restyled, which may conflict with another mod that restyles them.");

                return standDown.Value;
            }
        }

        private static bool Detect()
        {
            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;

            if (mods == null)
                return false;

            foreach (ModContentPack mod in mods)
            {
                string id = mod?.PackageId;

                if (id == null)
                    continue;

                foreach (string owner in Owners)
                {
                    if (!id.Equals(owner, System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Said out loud, at a normal severity. A player who installed both and wonders why this mod's
                    // cards are not showing has exactly one place to find the answer, and it should not require
                    // turning on debug logging.
                    Log.Message(UILogTag.Prefix + "\"" + mod.Name + "\" already draws the message, alert and "
                                + "letter surfaces, so this mod is leaving them to it. Everything else this mod "
                                + "does is unaffected.");

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// What each notification patch class returns from its Harmony <c>Prepare</c>.
        ///
        /// A method rather than each class reading the property, so the three of them cannot come to different
        /// conclusions and leave one surface half-replaced.
        /// </summary>
        internal static bool ShouldPatch() => !AnotherModOwnsNotifications;
    }
}
