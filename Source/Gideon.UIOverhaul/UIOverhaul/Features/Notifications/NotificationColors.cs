using Gideon.UIFramework.Defs;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Notifications
{
    /// <summary>
    /// Which palette role a notification's colored edge takes, given what kind of notification it is.
    ///
    /// <b>Four tones, because more than four cannot be told apart at a glance.</b> Threat, setback, task and good
    /// news. That is the useful resolution for a color someone reads out of the corner of their eye; a distinct hue
    /// per message type would be a legend to memorize rather than information.
    ///
    /// <b>Mapped to palette roles, not to fixed colors.</b> The whole mod is themeable, and a notification that
    /// stayed the same red on a light theme would be the one element that ignored it. Danger, Warning, Info and
    /// Success already mean exactly these four things everywhere else in the UI.
    ///
    /// <b>A message type this does not know gets the neutral role, and there is nothing better available.</b> The
    /// schedule colors defer to a mod author's own choice where they made one, and the same courtesy was intended
    /// here -- but <c>MessageTypeDef</c> carries only a <c>sound</c>, no color. Vanilla draws every message in the
    /// same white text and distinguishes them by sound alone, so there is no authored color to respect. A modded
    /// message type therefore reads as neutral rather than as a guess at its tone, which is the honest answer: we do
    /// not know whether someone else's message is good news.
    /// </summary>
    internal static class NotificationColors
    {
        /// <summary>
        /// The edge color for a message.
        ///
        /// Keyed off the def rather than off the message, so the same reasoning serves letters and alerts when those
        /// surfaces arrive.
        /// </summary>
        internal static Color For(MessageTypeDef def, UIColorPaletteDef palette)
        {
            if (def == null)
                return palette.TextSecondary;

            // Reference comparison against the DefOfs rather than a defName switch: these are resolved once by
            // vanilla and comparing them is a pointer compare, where a string switch would allocate nothing but
            // would silently stop matching if a defName were ever renamed.
            if (def == MessageTypeDefOf.ThreatBig || def == MessageTypeDefOf.ThreatSmall)
                return palette.Danger;

            if (def == MessageTypeDefOf.NegativeEvent || def == MessageTypeDefOf.RejectInput
                                                      || def == MessageTypeDefOf.CautionInput)
                return palette.Warning;

            if (def == MessageTypeDefOf.PositiveEvent)
                return palette.Success;

            if (def == MessageTypeDefOf.TaskCompletion || def == MessageTypeDefOf.NeutralEvent
                                                       || def == MessageTypeDefOf.SilentInput)
                return palette.Info;

            // Somebody else's message type, and MessageTypeDef offers nothing to infer a tone from.
            return palette.TextSecondary;
        }
    }
}
