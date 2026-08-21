using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// Reading and setting which area a pawn is allowed in, for the pawns tab's Area column.
    ///
    /// <b>Asked for on 2026-08-21,</b> to put the allowed area from vanilla's Restrict tab on ours. That tab pairs
    /// the timetable with the area for a reason: both are answers to "where and when is this pawn allowed to be",
    /// and ours already had the timetable.
    ///
    /// <b>The map comes from the pawn, not from the camera.</b> Vanilla's <c>AreaAllowedGUI</c> lists
    /// <c>Find.CurrentMap</c>'s areas while writing to a property keyed on the pawn's own map, which is harmless in
    /// a tab that only ever shows one map and wrong in this one: our rows are grouped by map, so a pocket dimension
    /// and the colony are on screen together, and offering the colony's areas to a pawn standing in the pocket
    /// would name areas that pawn can never be in. Every read and write here goes through the pawn's map instead.
    ///
    /// <b>Nothing here decides what an area means.</b> The list, its order, the unrestricted entry, the hover
    /// highlight and the manager are all RimWorld's own, through <see cref="AreaUtility"/>. This is the wiring
    /// between our column and that, and the eligibility test is copied from vanilla's own column rather than
    /// invented, so a pawn stops being assignable here at the moment they stop being assignable in vanilla's tab.
    /// </summary>
    internal static class PawnAreas
    {
        /// <summary>
        /// Whether this pawn can be given an allowed area at all.
        ///
        /// <b>The same four tests vanilla's own column makes,</b> in the same order: the colony's own pawns only, a
        /// mutant whose kind takes area orders, a mechanoid with an overseer to take them from, and a race that has
        /// not switched area control off. Reproducing the reasoning instead would drift, and the drift shows as a
        /// control that either does nothing or is missing where vanilla offers one.
        /// </summary>
        internal static bool Assignable(Pawn pawn)
        {
            return UIGuard.Try("Pawns.AreaAssignable", () =>
            {
                if (pawn?.playerSettings == null || pawn.Faction != Faction.OfPlayer)
                    return false;

                if (pawn.IsMutant && !pawn.mutant.Def.respectsAllowedArea)
                    return false;

                if (pawn.RaceProps.IsMechanoid && pawn.GetOverseer() == null)
                    return false;

                return pawn.playerSettings.SupportsAllowedAreas;
            }, false, null);
        }

        /// <summary>The area this pawn is held to on the map they are on, or null for unrestricted.</summary>
        internal static Area Current(Pawn pawn)
        {
            return UIGuard.Try("Pawns.AreaOf", () => pawn?.playerSettings?.AreaRestrictionInPawnCurrentMap, null,
                null);
        }

        /// <summary>What the button says. RimWorld's own wording, so "unrestricted" reads as it does elsewhere.</summary>
        internal static string Label(Pawn pawn)
        {
            return UIGuard.Try("Pawns.AreaLabel", () => AreaUtility.AreaAllowedLabel(pawn), string.Empty, null);
        }

        /// <summary>
        /// Why the column is blank for this pawn, for a tooltip, or null when it is not blank.
        ///
        /// <b>A blank cell with no explanation reads as a fault.</b> Vanilla writes a message into the cell itself
        /// for the two cases a player is most likely to go looking for; ours says the same thing in a tooltip
        /// rather than spending a column's width on a sentence that applies to one row in twenty.
        /// </summary>
        internal static string Reason(Pawn pawn)
        {
            return UIGuard.Try("Pawns.AreaReason", () =>
            {
                if (pawn == null || Assignable(pawn))
                    return null;

                if (pawn.Faction != Faction.OfPlayer)
                    return "Only the colony's own pawns take area orders.";

                if (AnimalPenUtility.NeedsToBeManagedByRope(pawn))
                    return "This animal is kept by its pen rather than by an area.";

                // ToString rather than left as a TaggedString: every other branch here is a plain string, and a
                // lambda with two return types infers no type at all, which picks the guard's void overload and
                // fails to compile with an error that does not mention translation.
                if (pawn.RaceProps.Dryad)
                    return "CannotAssignAllowedAreaToDryad".Translate().ToString();

                if (pawn.RaceProps.IsMechanoid)
                    return "This mech has no overseer to take orders from.";

                return "This pawn does not take area orders.";
            }, null, null);
        }

        /// <summary>
        /// Opens the area menu for one pawn.
        ///
        /// <b>Built by RimWorld,</b> which is what supplies the unrestricted entry, the areas actually assignable
        /// as allowed, the outline drawn on the map while an entry is hovered, and Manage areas on the end. The
        /// manager is worth having here rather than in the column heading: it takes a map, and with several maps on
        /// screen a heading button would have to guess which one was meant.
        /// </summary>
        internal static void Choose(Pawn pawn)
        {
            UIGuard.Try("Pawns.AreaMenu", () =>
            {
                Map map = pawn?.MapHeld;

                if (map?.areaManager == null || pawn.playerSettings == null)
                    return;

                AreaUtility.MakeAllowedAreaListFloatMenu(area => Set(pawn, area), true, true, map);
            }, "The area list could not be built, so nothing was changed.");
        }

        /// <summary>
        /// Writes the choice.
        ///
        /// <b>Guarded separately from the menu that offered it,</b> because this runs from the float menu's own
        /// OnGUI rather than from the panel that drew the row: an exception here would reach RimWorld instead of
        /// being caught and reported. The same reasoning as the policy pickers, which say so at more length.
        /// </summary>
        private static void Set(Pawn pawn, Area area)
        {
            UIGuard.Try("Pawns.SetArea", () => pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area,
                "The allowed area was not changed.");
        }
    }
}
