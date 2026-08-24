using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// Hides worn headgear everywhere, so a colony of identical helmets is a colony of faces again.
    ///
    /// <b>One patch, and it is on the gate rather than on the drawing.</b>
    /// <c>PawnRenderNodeWorker_Apparel_Head.HeadgearVisible</c> is consulted twice by the engine: once by that
    /// worker's own <c>CanDrawNow</c>, which decides whether the hat is drawn, and once by
    /// <c>PawnRenderTree</c>, which uses it to decide whether to set the skip flags that suppress hair, beard and
    /// eyes under the hat. Answering false there therefore takes the helmet off *and* puts the hair back. Patching
    /// the drawing instead would have left every colonist bald under a hat that was no longer there.
    ///
    /// <b>Why this needs a patch at all.</b> The portrait cache takes a <c>renderHeadgear</c> argument and always
    /// has, which is how the colonist bar hid hats on portraits. The map has no such argument: a pawn's sprite is
    /// submitted to the frame once by <c>DynamicDrawManager</c> and every camera that renders afterwards picks up
    /// the same submission, so the live tiles in the bar showed the helmet however the bar asked. Asked for on
    /// 2026-08-23 to apply to the map as well -- "just hide headgear period with that setting enabled".
    ///
    /// <b>The colony's own people only.</b> A mechanoid's head is not wearing anything, and a shambler in a
    /// salvaged helmet is a thing you need to recognise on sight rather than a face you are trying to tell from
    /// another face. See <see cref="Colonist"/> for why one vanilla property covers both.
    ///
    /// <b>Except in orbit.</b> On a space map a helmet is not a hat, it is the difference between a colonist who
    /// can go outside and one who cannot, and that is the one place the picture has to keep telling you.
    ///
    /// <b>And except in the character editor's own preview,</b> whose entire purpose is looking at the pawn under
    /// what they are wearing. It says "Show headgear" beside a switch, and a global rule that silently overrode it
    /// would be the same fault this feature was reported as.
    ///
    /// <b>And except before the game has started,</b> for the same reason twice over: the starting characters page
    /// carries its own Show headgear switch, and the colonist bar this setting exists for does not exist yet.
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderNodeWorker_Apparel_Head),
        nameof(PawnRenderNodeWorker_Apparel_Head.HeadgearVisible))]
    internal static class Patch_HeadgearVisible
    {
        /// <summary>
        /// Guarded, and the guard's fallback is vanilla's answer.
        ///
        /// This runs for every pawn on screen every frame, inside RimWorld's own draw pass where a throw is not
        /// ours to catch. <c>UIGuard</c> reports once and the answer falls through unchanged, which is a colony
        /// wearing its hats rather than a colony that stopped rendering.
        /// </summary>
        public static void Postfix(PawnDrawParms parms, ref bool __result)
        {
            if (!__result)
                return;

            if (Hidden(parms))
                __result = false;
        }

        private static bool Hidden(PawnDrawParms parms)
        {
            return UIGuard.Try("Pawns.HideHeadgear", () =>
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                if (settings == null || !settings.barHideHeadgear)
                    return false;

                // Not before the game has started. The starting characters page renders the party as portraits
                // and offers its own Show headgear switch right there, so a global rule winning would read as
                // that switch being broken -- and the setting is about telling your colony apart on the map and
                // in the bar, neither of which exists yet.
                if (Current.ProgramState != ProgramState.Playing)
                    return false;

                if (Editor.EditorRender.ShowingUnderHat)
                    return false;

                return Colonist(parms.pawn) && !InOrbit(parms.pawn);
            }, false, null);
        }

        /// <summary>
        /// Whether this is one of the colony's own people, as the game itself counts them.
        ///
        /// <b><c>Pawn.IsColonist</c> answers the whole question, which is why nothing here is hand-rolled.</b> It
        /// requires the player's faction, requires humanlike -- so no mechanoid, whose "headgear" is part of the
        /// body -- and excludes the subhuman, which is what shamblers, awoken corpses and ghouls are. Asked for on
        /// 2026-08-23 as "only non-mech colonists so we don't impact shamblers and mechs"; that property is
        /// exactly that set, and it will stay exactly that set when Ludeon adds the next thing that wears a
        /// helmet and is not a person.
        ///
        /// The consequence worth knowing: raiders, visitors, traders and prisoners keep their hats. Telling your
        /// own colony apart is what the setting is for, and a raider in a helmet is a raider in a helmet.
        /// </summary>
        private static bool Colonist(Pawn pawn)
        {
            return pawn != null && pawn.IsColonist;
        }

        /// <summary>
        /// Whether this pawn is somewhere a helmet is life support rather than a hat.
        ///
        /// <b>The map's planet layer, not a vacuum check.</b> A pawn inside a pressurised gravship in orbit is not
        /// breathing vacuum this second, and testing for that would flicker their helmet on and off as they walked
        /// through an airlock. "In orbit" is a fact about where the colony is, which is what was asked for.
        ///
        /// A pawn with no map -- carried, caravanning, in a pod -- is not in orbit, and is also not being drawn on
        /// a map, so this only ever decides the portrait for them.
        /// </summary>
        private static bool InOrbit(Pawn pawn)
        {
            Map map = pawn?.MapHeld;

            if (map == null || !map.Tile.Valid)
                return false;

            PlanetLayerDef layer = map.Tile.LayerDef;

            return layer != null && layer.isSpace;
        }
    }
}
