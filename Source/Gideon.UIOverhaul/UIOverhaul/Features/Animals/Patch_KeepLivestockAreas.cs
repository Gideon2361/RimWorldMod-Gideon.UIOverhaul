using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// Stops RimWorld throwing away a roamer's allowed area every time the game loads.
    ///
    /// <b>This is the fault Aaron reported on 2026-08-22:</b> areas assigned to his goats were simply gone, the
    /// control reading "unrestricted" where it had said Animals. Nothing in this mod cleared them.
    /// <c>Pawn_PlayerSettings.ExposeData</c> does, in three lines near the end:
    ///
    /// <code>
    /// if (Scribe.mode == LoadSaveMode.PostLoadInit &amp;&amp; pawn.Roamer)
    ///     allowedAreas.Clear();
    /// </code>
    ///
    /// The area survives being saved, because the save pass only strips null entries, and is wiped on the way back
    /// in. From vanilla's side that is tidy rather than hostile: a roamer can never respect an area, so an area on
    /// one is stale data. With the livestock area setting on it is not stale, it is the setting, and losing it on
    /// load makes the whole feature last exactly one session.
    ///
    /// <b>Saved before the method and put back after it,</b> because that is the only seam that does not involve
    /// editing IL in the middle of an <c>ExposeData</c>. The prefix takes a copy of the dictionary while it still
    /// has contents; the postfix writes them back over whatever the clear left. Both halves do nothing at all
    /// unless the game is in the post-load pass, this pawn is a roamer, and the setting is on, which between them
    /// mean this runs for a few dozen animals once per load.
    ///
    /// <b>With the setting off, vanilla's behavior is left exactly as it is,</b> including the clear: an area that
    /// nothing honors is stale data again, and keeping it would be keeping a lie.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.ExposeData))]
    internal static class Patch_KeepLivestockAreas
    {
        /// <summary>
        /// What the prefix rescued, for the postfix to put back.
        ///
        /// A field rather than a Harmony state parameter because it is a single value passed between two halves of
        /// one call, and ExposeData is never re-entered: the scribe walks one object at a time.
        /// </summary>
        private static List<KeyValuePair<Map, Area>> rescued;

        public static void Prefix(Pawn_PlayerSettings __instance)
        {
            rescued = null;

            UIGuard.Try("Animals.KeepAreas.Save", () =>
            {
                if (!Applies(__instance))
                    return;

                Dictionary<Map, Area> areas = Areas(__instance);

                if (areas == null || areas.Count == 0)
                    return;

                rescued = new List<KeyValuePair<Map, Area>>(areas);
            }, null);
        }

        public static void Postfix(Pawn_PlayerSettings __instance)
        {
            List<KeyValuePair<Map, Area>> keep = rescued;

            rescued = null;

            if (keep == null)
                return;

            UIGuard.Try("Animals.KeepAreas.Restore", () =>
            {
                Dictionary<Map, Area> areas = Areas(__instance);

                if (areas == null)
                    return;

                for (int i = 0; i < keep.Count; i++)
                {
                    // Null on either side is what vanilla's own save pass strips, so it is not put back: a map or
                    // an area that did not survive the load is gone, and re-adding the pair would leave a
                    // dictionary entry pointing at nothing.
                    if (keep[i].Key == null || keep[i].Value == null)
                        continue;

                    areas.SetOrAdd(keep[i].Key, keep[i].Value);
                }
            }, "An animal's allowed area was not restored after loading.");
        }

        /// <summary>
        /// Whether this call is the one that would lose an area.
        ///
        /// The same three conditions vanilla's clear is guarded by, plus the setting: any other pass, any other
        /// kind of pawn, or the setting off, and both halves of this patch cost one comparison.
        /// </summary>
        private static bool Applies(Pawn_PlayerSettings settings)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit)
                return false;

            UIOverhaulSettingsFile file = UIOverhaulSettingsFile.Current;

            if (file == null || !file.penAnimalsUseAreas)
                return false;

            Pawn pawn = Patch_PennedAnimalAreas.Owner(settings);

            return pawn != null && pawn.RaceProps != null && pawn.RaceProps.Animal && pawn.Roamer;
        }

        /// <summary>
        /// The private dictionary the areas live in.
        ///
        /// Reached rather than rebuilt through the public property because that one is keyed on the pawn's current
        /// map: an animal in a caravan, or one that has been on two maps, has entries the property cannot see, and
        /// rescuing only the visible one would quietly lose the rest.
        /// </summary>
        private static AccessTools.FieldRef<Pawn_PlayerSettings, Dictionary<Map, Area>> areas;

        private static bool resolved;

        private static Dictionary<Map, Area> Areas(Pawn_PlayerSettings settings)
        {
            if (!resolved)
            {
                resolved = true;

                areas = UIGuard.Try("Animals.ResolveAllowedAreas",
                    () => AccessTools.FieldRefAccess<Pawn_PlayerSettings, Dictionary<Map, Area>>("allowedAreas"),
                    null, null);

                if (areas == null)
                {
                    Log.Warning(UILogTag.Prefix + "Pawn_PlayerSettings.allowedAreas could not be read, so "
                                + "livestock areas are still cleared when a save is loaded. Everything else "
                                + "works.");
                }
            }

            return areas == null || settings == null ? null : areas(settings);
        }
    }
}
