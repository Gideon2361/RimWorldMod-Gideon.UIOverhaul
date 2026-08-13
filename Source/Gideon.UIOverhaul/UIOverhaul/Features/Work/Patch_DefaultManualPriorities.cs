using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Work
{
    /// <summary>
    /// Starts a new game with manual priorities switched on.
    ///
    /// Patched on InitNewGame rather than on PlaySettings itself, because the setting is a plain field whose
    /// default is baked into the object -- there is no initializer to intercept. Doing it here also confines
    /// the change to new games: an existing save keeps whatever the player chose, which a blanket default
    /// applied on load would silently overwrite.
    ///
    /// The reason for the change: the whole point of this mod's work tab is the 0-9 range, and a player who
    /// opens it with the setting off sees checkboxes and no indication that priorities exist at all.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.InitNewGame))]
    public static class Patch_Game_InitNewGame
    {
        /// <summary>
        /// Guarded on principle rather than because a way for it to throw is known: the null check covers the one
        /// hazard here, and InitNewGame is a place where an escape means a new colony that cannot be started.
        /// </summary>
        public static void Postfix()
        {
            UIGuard.Try("Work.DefaultManualPriorities", () =>
            {
                if (Current.Game?.playSettings != null)
                    Current.Game.playSettings.useWorkPriorities = true;
            }, "A new colony starts with manual work priorities switched off. It can be turned on from the "
               + "bottom-right toggle.");
        }
    }
}
