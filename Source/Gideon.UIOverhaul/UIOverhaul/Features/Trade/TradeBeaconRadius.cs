using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using UnityEngine;

namespace Gideon.UIOverhaul.Features.Trade
{
    /// <summary>
    /// How far an orbital trade beacon reaches, and how far the region walk that finds its cells is allowed to go.
    ///
    /// <b>RimWorld's own figure is a private const of 7.9,</b> which is a radius of not quite eight tiles: the
    /// beacon covers a 15 by 15 area with the corners clipped. Asked for on 2026-08-22 as a slider, defaulting to
    /// that same number, adjustable down to 3 and up to three times vanilla. The default is vanilla's so an
    /// install that never opens the setting plays the game the game shipped.
    ///
    /// <b>The region cap moves with the radius, and forgetting it would have been the quiet bug.</b> Vanilla's
    /// cell walk is a breadth first region traverse stopped after sixteen regions, which is generous for a radius
    /// of eight and not enough for one of twenty four: the ring would have been drawn at the size the player
    /// asked for while the cells past the sixteenth region silently refused to sell. It is scaled by area, since
    /// that is how many regions a circle covers, and floored at vanilla's own sixteen so a smaller radius never
    /// walks less than the game would have.
    /// </summary>
    internal static class TradeBeaconRadius
    {
        /// <summary>RimWorld's own radius, read off <c>Building_OrbitalTradeBeacon</c>.</summary>
        internal const float Default = 7.9f;

        /// <summary>
        /// The smallest radius offered.
        ///
        /// Three tiles still covers the beacon's own cell and the ring around it, so the building keeps working
        /// as a building. Below that it would be a beacon that can only sell what is standing on it.
        /// </summary>
        internal const float Minimum = 3f;

        /// <summary>Three times vanilla, which is the ceiling Aaron asked for.</summary>
        internal const float Maximum = Default * 3f;

        /// <summary>Vanilla's own region cap, and the floor for ours.</summary>
        private const int BaseRegions = 16;

        /// <summary>
        /// The radius in force.
        ///
        /// Clamped on read rather than on write, so a hand edited config with a silly number gives a sensible
        /// beacon instead of one that covers the map or nothing at all.
        /// </summary>
        internal static float Radius
        {
            get
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                if (settings == null)
                    return Default;

                return Mathf.Clamp(settings.tradeBeaconRadius, Minimum, Maximum);
            }
        }

        /// <summary>
        /// How many regions the cell walk may cross.
        ///
        /// Scaled by the square of the radius because that is how the covered area grows, and never below
        /// vanilla's sixteen. At the maximum radius this comes out at 144, which is a walk of a few hundred cells
        /// once per beacon per trade window rather than anything on a tick.
        /// </summary>
        internal static int MaxRegions
        {
            get
            {
                return UIGuard.Try("Trade.BeaconRegions", () =>
                {
                    float scale = Radius / Default;

                    return Mathf.Max(BaseRegions, Mathf.CeilToInt(BaseRegions * scale * scale));
                }, BaseRegions, null);
            }
        }
    }
}
