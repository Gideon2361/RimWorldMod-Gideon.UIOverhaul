using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;

namespace Gideon.UIOverhaul.Features.Notifications
{
    /// <summary>
    /// Whether each notification surface is drawn by this mod or handed back to RimWorld.
    ///
    /// <b>An escape per surface rather than one switch for all three.</b> They are three separate replacements
    /// with three separate risk profiles: the messages are the least intrusive, the alerts add snoozing and hiding
    /// that a player may not want, and the letters take over the whole stack including the bundling. Somebody who
    /// dislikes one of them should be able to keep the other two, and somebody diagnosing an interaction with
    /// another mod needs to be able to switch off exactly one thing at a time.
    ///
    /// <b>Read every frame rather than latched at startup.</b> <c>NotificationCompatibility</c> answers a
    /// different question -- is another mod already drawing this -- and that one genuinely cannot change without a
    /// restart, so it is decided once in <c>Prepare</c> and the patch is never applied. This one is a preference,
    /// and a preference that needed a restart to take effect is a preference a player will assume is broken. The
    /// cost is a field read on a draw path, which is nothing next to the drawing it gates.
    ///
    /// <b>Defaults to on for all three,</b> because a restyle nobody can see is a restyle nobody knows to turn on.
    /// </summary>
    internal static class NotificationSettings
    {
        /// <summary>
        /// Whether this mod draws <paramref name="surface"/>.
        ///
        /// Guarded and defaulting to true: if the settings cannot be read at all, drawing our own version is the
        /// behavior the player installed the mod for, and a settings file that failed to load has already been
        /// reported by <c>UIOverhaulSettingsFile</c> itself.
        /// </summary>
        internal static bool Restyle(NotificationSurface surface)
        {
            return UIGuard.Try("Notifications.ReadRestyle", () => Read(surface), true,
                "Notifications are drawn by this mod, which is the default.");
        }

        private static bool Read(NotificationSurface surface)
        {
            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

            if (settings == null)
                return true;

            switch (surface)
            {
                case NotificationSurface.Letters:
                    return settings.restyleLetters;

                case NotificationSurface.Alerts:
                    return settings.restyleAlerts;

                default:
                    return settings.restyleMessages;
            }
        }
    }
}
