using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.ThingFilters
{
    /// <summary>
    /// Replaces the thing filter panel with <see cref="ThingFilterPanel"/>.
    ///
    /// <b>Why this method and not a smaller one.</b> <c>DoThingFilterConfigWindow</c> is the whole panel in one
    /// static method: it draws the two buttons, the search box, the range sliders, then constructs a
    /// <c>Listing_TreeThingFilter</c> and hands it the scroll view. There is no seam inside it that separates
    /// layout from drawing, and the tree's own rendering lives in private methods on a class the listing creates
    /// locally. Patching here is what lets the panel be redesigned; patching anything inside it would only restyle
    /// pieces of vanilla's arrangement.
    ///
    /// Everything above this method is untouched, and there is a lot of it. Storage tabs, bill dialogs, outfit and
    /// drug policies, caravan and trade requests all call this one method and keep their own windows, their own
    /// <c>UIState</c> and their own filters. That is why the whole panel could be replaced without a patch per
    /// caller.
    ///
    /// <b>Guarded with <c>TryOnce</c> rather than <c>Try</c>.</b> This draws a scroll view, and a throw partway
    /// through one leaves a clip group on Unity's stack that disturbs everything drawn afterwards, anywhere on
    /// screen. Retrying every frame would repeat that indefinitely, so the site is retired on its first failure and
    /// vanilla's panel takes over for the rest of the session -- a working filter that looks like stock RimWorld,
    /// rather than a broken one that looks like ours.
    ///
    /// The inversion in the return is worth reading twice: a prefix returns false to suppress the original, so
    /// success is false here and failure is true.
    /// </summary>
    [HarmonyPatch(typeof(ThingFilterUI), nameof(ThingFilterUI.DoThingFilterConfigWindow))]
    public static class Patch_ThingFilterUI_DoThingFilterConfigWindow
    {
        private const string Site = "ThingFilters.Panel";

        public static bool Prefix(Rect rect, ThingFilterUI.UIState state, ThingFilter filter,
            ThingFilter parentFilter, int openMask, IEnumerable<ThingDef> forceHiddenDefs,
            IEnumerable<SpecialThingFilterDef> forceHiddenFilters, bool forceHideHitPointsConfig,
            bool forceHideQualityConfig, bool showMentalBreakChanceRange,
            List<ThingDef> suppressSmallVolumeTags, Map map)
        {
            // Both are required and neither is optional in the signature, but a caller reached through another
            // mod's patch can still arrive with one missing. Handing them back to vanilla is more useful than
            // reporting a fault we caused by being in the way.
            if (state == null || filter == null)
                return true;

            return !UIGuard.TryOnce(Site,
                () => ThingFilterPanel.Draw(rect, state, filter, parentFilter, openMask, forceHiddenDefs,
                    forceHiddenFilters, forceHideHitPointsConfig, forceHideQualityConfig,
                    showMentalBreakChanceRange, suppressSmallVolumeTags, map),
                "The thing filter panel is drawn RimWorld's own way for the rest of this session.");
        }
    }
}
