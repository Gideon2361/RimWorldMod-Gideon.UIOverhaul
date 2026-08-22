using System;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// Lets livestock be given an allowed area, which RimWorld refuses on the grounds that a pen is how livestock
    /// is kept.
    ///
    /// <b>One property is the whole gate.</b> <c>Pawn_PlayerSettings.SupportsAllowedAreas</c> is
    /// <c>!pawn.Roamer &amp;&amp; !pawn.RaceProps.disableAreaControl</c>, and <c>Roamer</c> is true for any race
    /// with a <c>roamMtbDays</c>: cows, sheep, chickens, everything the pen system exists for. Everything else
    /// follows from that one answer. <c>RespectsAllowedArea</c> reads it, so the AI starts honoring the area;
    /// <c>ForbidUtility.IsForbidden</c> reads that, so wandering, grazing and every job target validator honor it
    /// too, without a single further patch. Vanilla's own animals tab column reads it, so the control appears
    /// there as well as here, and this mod's <c>PawnAreas</c> reads it, so the pawns tab, the group menu and the
    /// animal's card all offer it at once.
    ///
    /// <b>Nothing in the pen system reads it,</b> which is what makes this safe to change: pens, ropes, hitching
    /// posts and the roping work givers all key off <c>Roamer</c> and <c>FenceBlocked</c> directly, and none of
    /// them ask this question. A penned animal with an area is still penned.
    ///
    /// <b>Assignable is only half of it, and the other half is <see cref="LivestockRoaming"/>.</b> Roaming asks
    /// about ropes and the reachable map edge and never about areas, so on its own this patch would have given
    /// livestock somewhere to be and no reason to stay: they walk off the map after day five regardless. The
    /// companion patches, behind the same setting, teach the roaming state that an area or a pen means the animal
    /// is being kept.
    ///
    /// <b>Off by default,</b> so an install that never opens the setting behaves exactly as RimWorld does.
    ///
    /// <b>A postfix, and the hot path is one branch.</b> This property is read inside <c>IsForbidden</c>, which
    /// runs per cell during pathing and wander validation, so the shape matters: when vanilla already said yes
    /// there is nothing to decide and the postfix returns on its first test. The pawn is reached through a cached
    /// field accessor rather than reflection per call.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.SupportsAllowedAreas),
        MethodType.Getter)]
    internal static class Patch_PennedAnimalAreas
    {
        public static void Postfix(Pawn_PlayerSettings __instance, ref bool __result)
        {
            // Already assignable: the common case, and the one this must not slow down.
            if (__result)
                return;

            if (!Enabled)
                return;

            __result = Livestock(Owner(__instance));
        }

        /// <summary>
        /// Whether the setting is on.
        ///
        /// Read every call rather than latched, because the options window writes it live and an animal's area
        /// control appearing only after a restart would read as the setting not working.
        /// </summary>
        private static bool Enabled
        {
            get
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                return settings != null && settings.penAnimalsUseAreas;
            }
        }

        /// <summary>
        /// Whether this is one of the colony's own roaming animals.
        ///
        /// <b>Roaming is the only refusal being overridden.</b> <c>disableAreaControl</c> is a race saying outright
        /// that areas are not a thing it takes, which is a different statement from "this one lives in a pen", and
        /// it is left alone. Wild animals are excluded because an allowed area on something that does not belong to
        /// the colony means nothing, and offering the control would put a dead chip on every wildlife card.
        /// </summary>
        private static bool Livestock(Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Animal)
                return false;

            if (pawn.Faction != Faction.OfPlayer)
                return false;

            return pawn.Roamer && !pawn.RaceProps.disableAreaControl;
        }

        /// <summary>
        /// The pawn behind a settings object.
        ///
        /// <c>Pawn_PlayerSettings.pawn</c> is private with no accessor, so it is read through a cached field
        /// delegate: built on first use inside a guard, because a throw while resolving it would otherwise come out
        /// of a property RimWorld reads thousands of times a second.
        /// </summary>
        private static AccessTools.FieldRef<Pawn_PlayerSettings, Pawn> owner;

        private static bool resolved;

        internal static Pawn Owner(Pawn_PlayerSettings settings)
        {
            if (!resolved)
            {
                resolved = true;

                owner = UIGuard.Try("Animals.ResolveSettingsOwner",
                    () => AccessTools.FieldRefAccess<Pawn_PlayerSettings, Pawn>("pawn"), null, null);

                if (owner == null)
                {
                    Log.Warning(UILogTag.Prefix + "Pawn_PlayerSettings.pawn could not be read, so livestock "
                                + "cannot be given allowed areas. Everything else works.");
                }
            }

            return owner == null || settings == null ? null : owner(settings);
        }
    }
}
