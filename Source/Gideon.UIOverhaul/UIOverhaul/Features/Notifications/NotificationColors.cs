using Gideon.UIFramework.Defs;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Notifications
{
    /// <summary>
    /// Which palette role a notification's colored edge takes, given what kind of notification it is.
    ///
    /// <b>Five tones, because more than that cannot be told apart at a glance.</b> Threat, setback, notice, task
    /// and good news. That is the useful resolution for a color someone reads out of the corner of their eye; a
    /// distinct hue per message type would be a legend to memorize rather than information.
    ///
    /// <b>Mapped to palette roles, not to fixed colors.</b> The whole mod is themeable, and a notification that
    /// stayed the same red on a light theme would be the one element that ignored it. Danger, Warning, Accent,
    /// Info and Success already mean exactly these five things everywhere else in the UI.
    ///
    /// <b>A neutral event is the accent, and that is a deliberate change from what it used to be.</b> A trade
    /// caravan arriving, visitors showing up, a quest offered -- these were grey, because that is the color
    /// vanilla's <c>NeutralEvent</c> def carries and it read as "nothing worth a color". They are the events a
    /// player most often wants to act on, and grey is what the eye skips. The accent is the mod's own "here is
    /// something", which is exactly what they are.
    ///
    /// <b>Orange is derived rather than added to the palette.</b> There is no orange role, and adding one would
    /// mean a new field on <see cref="UIColorPaletteDef"/>, an entry in both shipped palettes, and a missing value
    /// in every palette a player has already written. Halfway between warning and danger is what orange <i>is</i>,
    /// so blending the two gives the right color on any theme, including one whose warning is not yellow.
    /// </summary>
    internal static class NotificationColors
    {
        /// <summary>
        /// The middle rung of the threat ramp: worse than a setback, short of a disaster.
        ///
        /// Shared with <see cref="AlertCards"/> so a small threat's letter and a high priority alert are the same
        /// color. The two sit inches apart on the same edge of the screen and describe the same event often
        /// enough that two different oranges would read as two different things.
        /// </summary>
        internal static Color Orange(UIColorPaletteDef palette)
        {
            return Color.Lerp(palette.Warning, palette.Danger, 0.5f);
        }

        /// <summary>
        /// The edge color for a message.
        ///
        /// <b>A modded message type gets the neutral role rather than a guess.</b> The schedule colors defer to a
        /// mod author's own choice where they made one, and the same courtesy was intended here -- but
        /// <c>MessageTypeDef</c> carries only a <c>sound</c>, no color. Vanilla draws every message in the same
        /// white text and tells them apart by sound alone, so there is no authored color to respect.
        /// </summary>
        internal static Color For(MessageTypeDef def, UIColorPaletteDef palette)
        {
            if (def == null)
                return palette.TextSecondary;

            // Reference comparison against the DefOfs rather than a defName switch: these are resolved once by
            // vanilla and comparing them is a pointer compare, where a string switch would allocate nothing but
            // would silently stop matching if a defName were ever renamed.
            if (def == MessageTypeDefOf.ThreatBig)
                return palette.Danger;

            if (def == MessageTypeDefOf.ThreatSmall)
                return Orange(palette);

            if (def == MessageTypeDefOf.NegativeEvent || def == MessageTypeDefOf.RejectInput
                                                      || def == MessageTypeDefOf.CautionInput)
                return palette.Warning;

            if (def == MessageTypeDefOf.PositiveEvent)
                return palette.Success;

            // The two that changed. A neutral event is a notice worth reading, and a silent input is this mod
            // telling the player it did what they asked -- both are the accent's job rather than the information
            // role's, which now covers only the completion of something the colony was working on.
            if (def == MessageTypeDefOf.NeutralEvent || def == MessageTypeDefOf.SilentInput)
                return palette.Accent;

            if (def == MessageTypeDefOf.TaskCompletion)
                return palette.Info;

            // Somebody else's message type, and MessageTypeDef offers nothing to infer a tone from.
            return palette.TextSecondary;
        }

        /// <summary>
        /// The edge color for a letter.
        ///
        /// <b>Vanilla's letters are mapped to roles; everyone else's keep their authored color.</b> This is the
        /// opposite of the message rule above, and the difference is that <c>LetterDef</c> <i>does</i> carry a
        /// color -- so a mod that adds a letter has said something about its tone, and overriding that would be
        /// this mod deciding it knows better about an event it has never heard of.
        ///
        /// The base game's own colors are a different case. They are fixed values chosen against RimWorld's
        /// palette, not against the player's: <c>PositiveEvent</c> is a specific blue and <c>NeutralEvent</c> a
        /// specific grey, and neither follows a theme. Mapping the ones this mod can identify is what keeps a
        /// light theme from showing dark-theme letter colors, and it is also where the neutral events stop being
        /// grey.
        ///
        /// <b>Expansion defs may be null and that is handled by construction.</b> The <c>MayRequire</c> fields on
        /// <c>LetterDefOf</c> are null without their expansion, and <paramref name="def"/> has already been tested
        /// for null, so a comparison against a missing def is simply false rather than a match.
        /// </summary>
        internal static Color For(LetterDef def, UIColorPaletteDef palette)
        {
            if (def == null)
                return palette.TextSecondary;

            if (def == LetterDefOf.ThreatBig || def == LetterDefOf.Death || def == LetterDefOf.Bossgroup)
                return palette.Danger;

            if (def == LetterDefOf.ThreatSmall || def == LetterDefOf.EntityDiscovered)
                return Orange(palette);

            if (def == LetterDefOf.NegativeEvent || def == LetterDefOf.RitualOutcomeNegative)
                return palette.Warning;

            if (def == LetterDefOf.PositiveEvent || def == LetterDefOf.RitualOutcomePositive
                                                 || def == LetterDefOf.BabyBirth
                                                 || def == LetterDefOf.BabyToChild
                                                 || def == LetterDefOf.ChildToAdult
                                                 || def == LetterDefOf.ChildBirthday)
                return palette.Success;

            // The neutral family: something happened, or somebody is asking. Trade caravans, visitors, joiners
            // and the pawn choices all arrive on one of these, and all of them are things to go and look at.
            if (def == LetterDefOf.NeutralEvent || def == LetterDefOf.AcceptVisitors
                                                || def == LetterDefOf.AcceptJoiner
                                                || def == LetterDefOf.AcceptCreepJoiner
                                                || def == LetterDefOf.ChoosePawn
                                                || def == LetterDefOf.RelicHuntInstallationFound)
                return palette.Accent;

            // The pile of older letters. Deliberately toneless: it stands for several letters at once, and any
            // color it took would be the tone of whichever one happened to be first.
            if (def == LetterDefOf.BundleLetter)
                return palette.TextSecondary;

            // Somebody else's letter def, drawn in the color its author chose.
            return def.color;
        }
    }
}
