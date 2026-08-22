using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// Takes animals that have an allowed area out of the Pen needed alert, and renames it to what it is now
    /// warning about.
    ///
    /// <b>Why it needed changing.</b> Vanilla's alert lists every roamer with no pen, because a pen is the only
    /// thing that keeps one. With the livestock area setting on that is no longer true: an area keeps them too,
    /// and the roaming state honors it. So a colony that answered the alert the new way was still being told to
    /// build a pen, which is an alert that cannot be cleared by doing the right thing. Aaron reported it on
    /// 2026-08-22, the same day the area work landed.
    ///
    /// <b>"Animal roaming risk" rather than "Pen needed",</b> also his call, and it follows from the same change:
    /// the alert used to name the answer, and there are two answers now. Naming the risk instead leaves the player
    /// to pick one, and the explanation says both.
    ///
    /// <b>Only while the setting is on.</b> With it off, a pen genuinely is the only answer and vanilla's own
    /// wording is the accurate one, so nothing is touched.
    ///
    /// <b>Patched at <c>CalculateTargets</c>, which does both jobs at once.</b> That method is what
    /// <c>GetReport</c> calls to rebuild the list, and <c>Alert.Recalculate</c> reads the label immediately after
    /// the report, so writing <c>defaultLabel</c> here keeps the label in step with the setting without patching
    /// <c>Alert.GetLabel</c> on the base class, which every alert in the game shares.
    /// </summary>
    [HarmonyPatch(typeof(Alert_AnimalPenNeeded), "CalculateTargets")]
    internal static class Patch_PenNeededAlert
    {
        /// <summary>What the alert is called once an area is also an answer.</summary>
        private const string Label = "Animal roaming risk";

        public static void Postfix(Alert_AnimalPenNeeded __instance)
        {
            UIGuard.Try("Animals.PenAlert", () =>
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                if (settings == null || !settings.penAnimalsUseAreas)
                    return;

                Rename(__instance);
                Filter(__instance);
            }, "The pen alert is left as RimWorld built it.");
        }

        /// <summary>
        /// Drops the animals an area is already keeping.
        ///
        /// The pen half of the question is vanilla's own and is left alone: an animal standing in a pen was never
        /// added to this list in the first place.
        /// </summary>
        private static void Filter(Alert_AnimalPenNeeded alert)
        {
            List<GlobalTargetInfo> targets = Targets(alert);

            if (targets == null)
                return;

            for (int i = targets.Count - 1; i >= 0; i--)
            {
                Pawn animal = targets[i].Thing as Pawn;

                if (animal != null && LivestockRoaming.HeldByArea(animal))
                    targets.RemoveAt(i);
            }
        }

        private static void Rename(Alert_AnimalPenNeeded alert)
        {
            Resolve();

            if (label == null || alert == null)
                return;

            label(alert) = Label;
        }

        /// <summary>
        /// The alert's own target list and its label field, both non-public.
        ///
        /// Resolved once, inside a guard, and a miss leaves the alert exactly as RimWorld built it. Alerts are
        /// recalculated on a timer rather than per frame, so a delegate per access is not worth avoiding beyond
        /// the caching already here.
        /// </summary>
        private static AccessTools.FieldRef<Alert_AnimalPenNeeded, List<GlobalTargetInfo>> list;

        private static AccessTools.FieldRef<Alert, string> label;

        private static bool resolved;

        private static List<GlobalTargetInfo> Targets(Alert_AnimalPenNeeded alert)
        {
            Resolve();

            return list == null || alert == null ? null : list(alert);
        }

        private static void Resolve()
        {
            if (resolved)
                return;

            resolved = true;

            list = UIGuard.Try("Animals.ResolveAlertTargets",
                () => AccessTools.FieldRefAccess<Alert_AnimalPenNeeded, List<GlobalTargetInfo>>("targets"),
                null, null);

            label = UIGuard.Try("Animals.ResolveAlertLabel",
                () => AccessTools.FieldRefAccess<Alert, string>("defaultLabel"), null, null);

            if (list == null || label == null)
            {
                Log.Warning(UILogTag.Prefix + "Alert_AnimalPenNeeded did not have the expected members, so it "
                            + "still names pens rather than roaming risk. Everything else works.");
            }
        }
    }

    /// <summary>
    /// Adds the second answer to the alert's explanation.
    ///
    /// Vanilla's text names a pen and a hitching post, which was the whole list when it was written. With the
    /// setting on there is a third, and an explanation that omits it sends the player to build a pen they may not
    /// want. One sentence, appended rather than replacing, so RimWorld's own advice about pens is still there for
    /// somebody who would rather have one.
    /// </summary>
    [HarmonyPatch(typeof(Alert_AnimalPenNeeded), nameof(Alert_AnimalPenNeeded.GetExplanation))]
    internal static class Patch_PenNeededExplanation
    {
        public static void Postfix(ref TaggedString __result)
        {
            TaggedString text = __result;

            __result = UIGuard.Try("Animals.PenAlertText", () =>
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                if (settings == null || !settings.penAnimalsUseAreas)
                    return text;

                return text + "\n\nAn allowed area also keeps them, and one of these animals given an area will "
                       + "not wander off.";
            }, text, null);
        }
    }
}
