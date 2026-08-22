using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Factions
{
    /// <summary>
    /// One faction's standing with the colony, written the way the rest of the game writes it.
    ///
    /// <b>Backlog 31,</b> which asks for the standing of a faction to be visible on anything tied to one, rather
    /// than something to go and look up on the Factions tab. Reading vanilla first narrowed that considerably,
    /// and the note in the backlog is worth correcting: quests and letters already carry it.
    /// <c>MainTabWindow_Quests</c> draws <c>FactionUIUtility.DrawRelatedFactionInfo</c> for every involved
    /// faction, and <c>ChoiceLetter.OpenLetter</c> builds a <c>Dialog_NodeTreeWithFactionInfo</c>, which draws the
    /// same thing. What is genuinely missing is the world map, where only settlements say it, and the letter
    /// stack's hover text.
    ///
    /// <b>Vanilla's own wording and colours, not ours.</b> <c>FactionRelationKind.GetLabelCap</c> and
    /// <c>GetColor</c> are what the Factions tab, the quest card and the letter dialog already use, so a
    /// relation reads the same everywhere rather than the same fact appearing in two vocabularies depending on
    /// which panel it is in. This is one of the few places where matching the game beats matching our palette.
    /// </summary>
    internal static class FactionStanding
    {
        /// <summary>
        /// The standing as a line of text, coloured, or null when there is nothing worth saying.
        ///
        /// Null for our own faction, for one with no relations to speak of, and for a hidden faction, which is
        /// the same test vanilla's settlement inspect string makes before printing the goodwill: a hidden faction
        /// has a relation the player is not supposed to be reading yet.
        /// </summary>
        internal static string Line(Faction faction)
        {
            return UIGuard.Try("Factions.Standing", () =>
            {
                if (faction == null || faction.IsPlayer || faction.Hidden || faction.def == null)
                    return null;

                if (!faction.def.CanEverBeNonHostile && !faction.HostileTo(Faction.OfPlayer))
                    return null;

                FactionRelationKind kind = faction.PlayerRelationKind;
                string text = kind.GetLabelCap() + " (" + faction.PlayerGoodwill.ToStringWithSign() + ")";

                return Colored(text, kind.GetColor());
            }, null, null);
        }

        /// <summary>
        /// Wraps text in a colour tag.
        ///
        /// RimWorld's label styles have rich text on, which is how vanilla's own inspect strings and tooltips
        /// carry colour, so a tag here renders wherever one of theirs would.
        /// </summary>
        private static string Colored(string text, Color color)
        {
            return "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + text + "</color>";
        }
    }
}
