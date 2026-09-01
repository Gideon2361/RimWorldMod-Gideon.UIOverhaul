using Gideon.UIFramework.Helpers;

namespace Gideon.UIOverhaul.Features.GrowZones.UI
{
    /// <summary>
    /// Typefaces and sizes for the growing zones tab.
    ///
    /// The same three roles the quest, power and ideoligion tabs use, so a player moving between them is
    /// not reading three different typographic systems: a display face for the tab's name, a condensed one
    /// for names in a column, and the mono for every figure.
    ///
    /// Sizes are in points on the scale a word processor uses; see <see cref="UIFonts.PixelsPerPoint"/>.
    /// </summary>
    internal static class GrowFaces
    {
        /// <summary>Oswald. The tab's own name, and nothing else.</summary>
        internal const UIFace Display = UIFace.Oswald;

        /// <summary>Barlow Condensed. Crop and zone names, where width is the scarce thing.</summary>
        internal const UIFace Condensed = UIFace.BarlowCondensed;

        /// <summary>IBM Plex Mono. Percentages, yields, temperatures and every caption.</summary>
        internal const UIFace Mono = UIFace.IBMPlexMono;

        internal static class Size
        {
            internal const float Title = 15.75f;
            internal const float Subtitle = 10.5f;

            /// <summary>The header's three figures.</summary>
            internal const float Readout = 12.75f;

            /// <summary>Small caps under a figure, and the caption on a block.</summary>
            internal const float Caption = 7.5f;

            internal const float RailName = 11.25f;
            internal const float RailCount = 8.25f;

            /// <summary>The one big number inside a block.</summary>
            internal const float Figure = 15f;

            internal const float Body = 10.5f;
            internal const float Small = 8.25f;
        }
    }
}
