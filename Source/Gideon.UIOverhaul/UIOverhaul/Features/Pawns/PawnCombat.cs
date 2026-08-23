using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// The two standing orders about getting hurt: whether a pawn patches themselves up, and what they do when
    /// an enemy walks into the room.
    ///
    /// <b>Asked for on 2026-08-23,</b> to put both on the pawns tab. They are the last two settings from
    /// vanilla's Assign tab that this mod could read but not change: <see cref="Templates.PawnPolicySet"/> has
    /// carried <c>selfTend</c> and <c>hostilityResponse</c> since templates were written, so a template could
    /// already move them between colonists while nothing in the mod could show you what they were.
    ///
    /// <b>Vanilla files them two tabs apart, and that is the thing worth fixing.</b> Self-tend is a checkbox at
    /// the bottom of the Health tab; hostility response is an icon in the corner of the inspect pane. Both are
    /// answers to "what does this person do when it goes wrong", both are set once per colonist and then
    /// forgotten, and both are the kind of thing you want to sweep across the whole colony at once -- after a
    /// raid, or the first time a new arrival gets shot. A column does that in one pass down the list.
    ///
    /// <b>Every eligibility test here is vanilla's own, called rather than reproduced.</b> Self-tend asks the
    /// same four questions <c>HealthCardUtility</c> asks before it draws its checkbox; attack mode asks
    /// <c>Pawn_PlayerSettings.UsesConfigurableHostilityResponse</c>, which is the property vanilla's own inspect
    /// pane gates on. Reproducing either would drift, and the drift shows as a control that does nothing or is
    /// missing where vanilla offers one.
    /// </summary>
    internal static class PawnCombat
    {
        // ---------------------------------------------------------------------------------------
        // Self-tend
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Whether this pawn can be told to tend themselves.
        ///
        /// <b>The same four tests vanilla's health card makes,</b> in the same order: a game in progress, one of
        /// the colony's own colonists, alive, and old enough. A baby is excluded by vanilla and so is excluded
        /// here; a slave is not, because <c>IsColonist</c> counts a secured slave, and that is vanilla's answer
        /// rather than ours to second-guess.
        /// </summary>
        internal static bool SelfTendable(Pawn pawn)
        {
            return UIGuard.Try("Pawns.SelfTendable", () =>
            {
                if (pawn?.playerSettings == null || pawn.Dead)
                    return false;

                if (Current.ProgramState != ProgramState.Playing)
                    return false;

                return pawn.IsColonist && !pawn.DevelopmentalStage.Baby();
            }, false, null);
        }

        internal static bool SelfTends(Pawn pawn)
        {
            return UIGuard.Try("Pawns.SelfTends", () => pawn?.playerSettings != null
                                                        && pawn.playerSettings.selfTend, false, null);
        }

        /// <summary>Why the column is blank for this pawn, for a tooltip, or null when it is not blank.</summary>
        internal static string SelfTendReason(Pawn pawn)
        {
            return UIGuard.Try("Pawns.SelfTendReason", () =>
            {
                if (pawn == null || SelfTendable(pawn))
                    return null;

                if (pawn.DevelopmentalStage.Baby())
                    return "A baby cannot tend anybody, including themselves.";

                if (!pawn.RaceProps.Humanlike)
                    return "Animals cannot tend themselves.";

                if (pawn.IsSubhuman)
                    return "This pawn cannot be taught medicine.";

                return "Only the colony's own colonists can be told to tend themselves.";
            }, null, null);
        }

        /// <summary>
        /// Writes the switch, and says the two things vanilla says about it.
        ///
        /// <b>Both messages are vanilla's own, by key.</b> Turning it on for somebody incapable of Doctor work is
        /// refused outright, and turning it on for somebody capable but unassigned is allowed with a warning --
        /// self-tend is done as doctor work, so a colonist with Doctor at zero will simply never do it. Wording
        /// them ourselves would put two sentences in the game that say the same thing differently, and the second
        /// one is the more useful of the two: it names the reason a switch that is on does nothing.
        /// </summary>
        internal static void SetSelfTend(Pawn pawn, bool value)
        {
            UIGuard.Try("Pawns.SetSelfTend", () =>
            {
                if (pawn?.playerSettings == null)
                    return;

                if (value && pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor))
                {
                    Messages.Message("MessageCannotSelfTendEver".Translate(pawn.LabelShort, pawn),
                        MessageTypeDefOf.RejectInput, false);

                    return;
                }

                pawn.playerSettings.selfTend = value;

                if (value && pawn.workSettings != null
                          && pawn.workSettings.GetPriority(WorkTypeDefOf.Doctor) == 0)
                    Messages.Message("MessageSelfTendUnsatisfied".Translate(pawn.LabelShort, pawn),
                        MessageTypeDefOf.CautionInput, false);
            }, "Self-tend was not changed.");
        }

        /// <summary>
        /// What the switch says on hover: vanilla's own explanation, plus the one caveat that makes an enabled
        /// switch do nothing.
        ///
        /// The caveat is only added when it applies, so the tooltip is a sentence about this colonist rather than
        /// a standing disclaimer. A colonist who cannot do Doctor work at all is told so instead, which is the
        /// difference between "this will not work yet" and "this will never work".
        /// </summary>
        internal static string SelfTendTooltip(Pawn pawn)
        {
            return UIGuard.Try("Pawns.SelfTendTip", () =>
            {
                string tip = "AllowSelfTendTip".Translate(Faction.OfPlayer.def.pawnsPlural,
                    0.7f.ToStringPercent()).CapitalizeFirst().ToString();

                if (pawn == null)
                    return tip;

                if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor))
                    return tip + "\n\n" + pawn.LabelShortCap + " cannot do Doctor work at all, so this cannot "
                           + "be turned on.";

                if (pawn.workSettings != null && pawn.workSettings.GetPriority(WorkTypeDefOf.Doctor) == 0)
                    return tip + "\n\n" + pawn.LabelShortCap + " is not assigned to Doctor work, so this will "
                           + "have no effect until they are.";

                return tip;
            }, string.Empty, null);
        }

        // ---------------------------------------------------------------------------------------
        // Attack mode
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The three modes in the order they are offered, which is the order the enum declares them.
        ///
        /// Held as an array rather than walked with <c>Enum.GetValues</c> per frame, which is what vanilla's
        /// dropdown does: this is read once per row per frame, and that call allocates.
        /// </summary>
        internal static readonly HostilityResponseMode[] Modes =
        {
            HostilityResponseMode.Ignore,
            HostilityResponseMode.Attack,
            HostilityResponseMode.Flee
        };

        /// <summary>
        /// Whether this pawn chooses how to react to enemies.
        ///
        /// <b>Vanilla's own property,</b> which is colonists and colony subhumans whose kind allows it, and only
        /// while nobody else is holding them: a prisoner or a guest has a host faction, and their reaction to a
        /// raid is not the player's to set.
        /// </summary>
        internal static bool Respondable(Pawn pawn)
        {
            return UIGuard.Try("Pawns.Respondable", () => pawn?.playerSettings != null && !pawn.Dead
                                                          && pawn.playerSettings
                                                              .UsesConfigurableHostilityResponse, false, null);
        }

        internal static HostilityResponseMode Response(Pawn pawn)
        {
            return UIGuard.Try("Pawns.Response", () => pawn?.playerSettings == null
                    ? HostilityResponseMode.Flee
                    : pawn.playerSettings.hostilityResponse,
                HostilityResponseMode.Flee, null);
        }

        /// <summary>Why the column is blank for this pawn, for a tooltip, or null when it is not blank.</summary>
        internal static string ResponseReason(Pawn pawn)
        {
            return UIGuard.Try("Pawns.ResponseReason", () =>
            {
                if (pawn == null || Respondable(pawn))
                    return null;

                if (pawn.HostFaction != null)
                    return "Somebody else is holding " + pawn.LabelShortCap
                                                       + ", so how they react to enemies is not ours to set.";

                if (pawn.RaceProps.IsMechanoid)
                    return "A mech fights or does not fight as its overseer is ordered.";

                if (!pawn.RaceProps.Humanlike)
                    return "Animals react to enemies on their own.";

                return "This pawn does not take orders about enemies.";
            }, null, null);
        }

        /// <summary>
        /// Whether Attack can be chosen at all.
        ///
        /// <b>Vanilla leaves it out of the menu rather than refusing it,</b> so a pacifist is never offered the
        /// one mode they cannot obey. Ours draws the segment and disables it instead: a segment that comes and
        /// goes would move the other two under the pointer between one row and the next, and a pacifist is worth
        /// seeing as a pacifist rather than as a row with a gap in it.
        /// </summary>
        internal static bool CanAttack(Pawn pawn)
        {
            return UIGuard.Try("Pawns.CanAttack", () => pawn != null
                                                        && !pawn.WorkTagIsDisabled(WorkTags.Violent), false,
                null);
        }

        internal static void SetResponse(Pawn pawn, HostilityResponseMode mode)
        {
            UIGuard.Try("Pawns.SetResponse", () =>
            {
                if (pawn?.playerSettings == null)
                    return;

                if (mode == HostilityResponseMode.Attack && !CanAttack(pawn))
                    return;

                pawn.playerSettings.hostilityResponse = mode;

                // The same concept vanilla's own button reports, so choosing a mode here retires the tutorial
                // prompt about it rather than leaving it to nag about something already understood.
                PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.HostilityResponse,
                    KnowledgeAmount.SpecificInteraction);
            }, "The attack mode was not changed.");
        }

        /// <summary>RimWorld's own icon for a mode, so ours and the inspect pane's are the same picture.</summary>
        internal static Texture2D Icon(HostilityResponseMode mode)
        {
            return UIGuard.Try("Pawns.ResponseIcon", () => mode.GetIcon(), BaseContent.BadTex, null);
        }

        internal static string Label(HostilityResponseMode mode)
        {
            return UIGuard.Try("Pawns.ResponseLabel", () => mode.GetLabel(), mode.ToString(), null);
        }

        /// <summary>
        /// What one segment says on hover: the mode's own name, then what it means.
        ///
        /// <b>Written here rather than taken from vanilla's tip,</b> which describes the control as a whole and
        /// says nothing about which mode is which. Icon-only segments need the names on hover, and three
        /// sentences of our own are the price of not spending a hundred and fifty pixels on labels in every row.
        /// </summary>
        internal static string ResponseTooltip(Pawn pawn, HostilityResponseMode mode)
        {
            string name = Label(mode);

            switch (mode)
            {
                case HostilityResponseMode.Ignore:
                    return name + "\n\nCarry on working with an enemy in the room, unless something attacks "
                           + "them.";

                case HostilityResponseMode.Attack:
                    return CanAttack(pawn)
                        ? name + "\n\nGo for the nearest enemy without being told."
                        : name + "\n\n" + (pawn == null ? "This pawn" : pawn.LabelShortCap)
                               + " cannot fight, so this mode is not available to them.";

                case HostilityResponseMode.Flee:
                    return name + "\n\nRun for safety, away from the enemy.";

                default:
                    return name;
            }
        }
    }
}
