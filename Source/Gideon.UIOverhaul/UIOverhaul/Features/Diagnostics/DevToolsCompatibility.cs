using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Integrations;
using Verse;

namespace Gideon.UIOverhaul.Features.Diagnostics
{
    /// <summary>
    /// Stands this mod's developer tool patches down when Modern Dev Tools is loaded.
    ///
    /// <b>The same judgement <c>NotificationCompatibility</c> makes, for the same reason.</b> Astryl's Modern Dev
    /// Tools replaces the debug log, the debug actions window and the dev palette. So does this, for the log.
    /// Two replacing prefixes on one method do not merge -- whichever Harmony runs first returns false and the
    /// other never runs, decided by mod load order -- so the player would get one mod's log and no way to tell
    /// why. The test is not "is another mod present" but "will the thing still be handled if we step aside", and
    /// here it plainly will: what they get instead is a more thorough tool than this one, with error triage,
    /// mod attribution and a knowledge base that this deliberately does not attempt.
    ///
    /// <b>This is not a declared incompatibility, and should not become one.</b> The overlap is one window. Every
    /// other thing either mod does is unaffected, and a player may perfectly reasonably want both -- which is why
    /// this steps aside quietly rather than refusing to load alongside it.
    ///
    /// <b>Decided once, before any patching.</b> Harmony calls <c>Prepare</c> when the patch class is processed
    /// at startup, so a surface we stand down on is never patched at all rather than patched and then skipped
    /// every frame. The mod list cannot change without a restart, so there is nothing to re-check.
    /// </summary>
    internal static class DevToolsCompatibility
    {
        /// <summary>Modern Dev Tools, by package id. Lowercase, as RimWorld normalizes them.</summary>
        internal const string ModernDevToolsPackageId = "astryl.moderndevtools";

        private static bool? standDown;

        /// <summary>Whether another mod already owns the developer tool windows.</summary>
        internal static bool AnotherModOwnsDevTools
        {
            get
            {
                if (standDown.HasValue)
                    return standDown.Value;

                standDown = UIGuard.Try("Diagnostics.DetectDevTools", Detect, false,
                    "The debug log is restyled, which may conflict with another mod that restyles it.");

                return standDown.Value;
            }
        }

        private static bool Detect()
        {
            if (!ModIntegrations.Loaded(ModernDevToolsPackageId))
                return false;

            // Said out loud at a normal severity. A player running both who wonders why this mod's log is not
            // showing has exactly one place to find the answer, and it should not require turning on debug
            // logging to see it.
            Log.Message(UILogTag.Prefix + "\"Modern Dev Tools\" already replaces the debug log, so this mod is "
                        + "leaving it to that. Everything else this mod does is unaffected.");

            return true;
        }

        /// <summary>What each developer tool patch class returns from its Harmony <c>Prepare</c>.</summary>
        internal static bool ShouldPatch() => !AnotherModOwnsDevTools;
    }
}
