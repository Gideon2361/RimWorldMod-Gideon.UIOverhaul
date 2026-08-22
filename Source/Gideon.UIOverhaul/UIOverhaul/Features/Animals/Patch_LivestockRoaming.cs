using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// Whether livestock is being kept somewhere on purpose, which is the question the roaming behavior never
    /// asks.
    ///
    /// <b>Vanilla's rule.</b> <c>MentalStateWorker_Roaming.CanRoamNow</c> lets any of the colony's roamers walk
    /// off the map after day five if it is not roped and can reach the edge, and that is the whole test. An
    /// enclosed pen stops it only as a side effect, by making the map edge unreachable; an allowed area does not
    /// stop it at all, because nothing in that path consults one. So the area control added on 2026-08-22 gave
    /// livestock somewhere to be and no reason to stay, which is the gap Aaron closed by asking for this.
    ///
    /// <b>Two answers count as being kept.</b> An allowed area with anything in it, and standing in a pen that
    /// accepts this animal. The area is read through <c>EffectiveAreaRestrictionInPawnCurrentMap</c> rather than
    /// the raw one, which means it is null unless the area is actually being respected: an animal carrying an area
    /// from a save made while the setting was on does not silently keep the benefit after it is turned off.
    ///
    /// <b>Unenclosed pens count, and that is the only part of the pen half that changes anything.</b> An enclosed
    /// pen already stops roaming through <c>CanReachMapEdge</c>, so counting only enclosed ones would be a test
    /// that never fires. A pen with a gap in the fence is the case worth catching: the player built a pen and
    /// meant it, and the honest response to the gap is the unenclosed pen alert, not the herd leaving.
    /// </summary>
    internal static class LivestockRoaming
    {
        /// <summary>
        /// Whether this animal is being kept somewhere, so roaming away is not what the player wants.
        ///
        /// Cheapest test first: the area is a dictionary lookup, and the pen is a walk of the connected districts,
        /// which is the same work vanilla's own roping givers do and is not worth doing twice when the area has
        /// already answered.
        /// </summary>
        internal static bool Held(Pawn pawn)
        {
            return HeldByArea(pawn) || HeldByPen(pawn);
        }

        /// <summary>
        /// Whether an allowed area is keeping this animal.
        ///
        /// <b>Read as the effective area rather than the raw one,</b> which means it is null unless the area is
        /// actually being respected: an animal carrying an area from a save made while the setting was on does not
        /// silently keep the benefit after it is turned off.
        ///
        /// An empty area is not a place to be kept: that is the state a freshly made area is in, and vanilla's own
        /// area setter ignores one for the same reason.
        ///
        /// Asked on its own as well as through <see cref="Held"/>, because the pen needed alert wants exactly this
        /// question: it has already established that the animal has no pen.
        /// </summary>
        internal static bool HeldByArea(Pawn pawn)
        {
            if (!Enabled || pawn?.playerSettings == null)
                return false;

            Area area = pawn.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap;

            return area != null && area.TrueCount > 0;
        }

        /// <summary>Whether the animal is standing in a pen that accepts it, gap in the fence or not.</summary>
        internal static bool HeldByPen(Pawn pawn)
        {
            if (!Enabled || pawn == null || !pawn.Spawned || pawn.Map == null || !pawn.Roamer)
                return false;

            return AnimalPenUtility.GetCurrentPenOf(pawn, true) != null;
        }

        /// <summary>
        /// Whether the setting is on.
        ///
        /// The same one that grants livestock an allowed area in the first place, because this is the other half of
        /// that: an area that does not hold the animal in is a control that looks like it works and does not.
        /// </summary>
        private static bool Enabled
        {
            get
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                return settings != null && settings.penAnimalsUseAreas;
            }
        }
    }

    /// <summary>
    /// Stops the roaming state starting on livestock that is being kept somewhere.
    ///
    /// <b>Patched at <c>CanRoamNow</c> rather than at either caller,</b> because both callers go through it:
    /// <c>MentalStateWorker_Roaming.StateCanOccur</c>, which is how the mental break system offers the state, and
    /// <c>JobGiver_StartRoaming</c>, which is how an idle roamer starts one from its own think tree. One patch
    /// covers both and cannot fall out of step with them.
    ///
    /// Not a hot path: this runs when an animal is looking for a job or a break is being considered, not per cell
    /// or per tick, so the pen walk inside <see cref="LivestockRoaming.Held"/> costs no more than vanilla's own
    /// <c>CanReachMapEdge</c> in the same method.
    /// </summary>
    [HarmonyPatch(typeof(MentalStateWorker_Roaming), nameof(MentalStateWorker_Roaming.CanRoamNow))]
    internal static class Patch_LivestockRoamingStart
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (!__result)
                return;

            if (UIGuard.Try("Animals.RoamingStart", () => LivestockRoaming.Held(pawn), false,
                    "Livestock roaming is decided RimWorld's own way."))
                __result = false;
        }
    }

    /// <summary>
    /// Ends a roam already under way once the animal is being kept somewhere.
    ///
    /// <b>Without this the feature would be half of one.</b> The message says the muffalo is leaving, the player
    /// reacts by giving it an area, and nothing happens: the state only ends by itself when the animal is roped or
    /// has no exit to walk to. Reacting to the warning is the entire point of the warning.
    ///
    /// <b>Vanilla's own shape, one line further along.</b> <c>MentalState_Roaming.MentalStateTick</c> already calls
    /// <c>RecoverFromState</c> when the animal has been roped, which is the same thought: it is being kept now, so
    /// it is not leaving. This adds the pen and the area to what counts as being kept.
    ///
    /// <b>Checked on an interval, because this one is per tick.</b> The pen test walks connected districts, which
    /// is far too much to do every tick for every roaming animal. Two seconds of lag before a herd turns around is
    /// invisible; the hash offset spreads the cost so a dozen roamers do not all pay it on the same tick. The
    /// delta form is the one that survives RimWorld's variable tick rates: a plain modulo test misses ticks
    /// entirely when the game is running several per frame.
    /// </summary>
    [HarmonyPatch(typeof(MentalState_Roaming), nameof(MentalState_Roaming.MentalStateTick))]
    internal static class Patch_LivestockRoamingEnd
    {
        private const int CheckInterval = 120;

        public static void Postfix(MentalState_Roaming __instance, int delta)
        {
            UIGuard.Try("Animals.RoamingEnd", () =>
            {
                Pawn pawn = __instance?.pawn;

                if (pawn == null || !pawn.Spawned)
                    return;

                // Vanilla may have ended the state during the tick this follows, for its own reasons: roped, or no
                // exit spot left. Recovering from a state that is no longer the current one would be reaching into
                // the handler for something that has already been cleaned up.
                if (pawn.MentalState != __instance)
                    return;

                if (!pawn.IsHashIntervalTick(CheckInterval, delta))
                    return;

                if (LivestockRoaming.Held(pawn))
                    __instance.RecoverFromState();
            }, "A roaming animal is left to RimWorld's own handling.");
        }
    }
}
