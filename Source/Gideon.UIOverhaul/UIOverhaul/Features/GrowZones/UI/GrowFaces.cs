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

        /// <summary>
        /// Point sizes, and they are the sizes the other restyled tabs use rather than sizes of their own.
        ///
        /// <b>Every small one was a step and a half under the shared scale.</b> A block caption set at 7.5pt is
        /// ten pixels tall, which is under the size the rest of the mod calls its smallest readable text, and a
        /// screen carrying five of them reads as a screen somebody shrank. Raised to the figures the bills,
        /// hospital and quest tabs already count in: a rail entry is 12.75pt here because it is 12.75pt there.
        /// </summary>
        internal static class Size
        {
            internal const float Title = 15.75f;
            internal const float Subtitle = 10.5f;

            /// <summary>The header's three figures.</summary>
            internal const float Readout = 12.75f;

            /// <summary>Small caps under a figure, and the caption on a block.</summary>
            internal const float Caption = 9.375f;

            internal const float RailName = 12.75f;
            internal const float RailCount = 9.75f;

            /// <summary>The one big number inside a block.</summary>
            internal const float Figure = 15f;

            /// <summary>A crop's name, which is a name in a column and sized like one.</summary>
            internal const float Body = 12.75f;

            /// <summary>The line under a name or a figure.</summary>
            internal const float Small = 9.75f;
        }
    }
}
