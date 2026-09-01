using Gideon.UIFramework.Helpers;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// Typefaces and sizes for the pawns tab.
    ///
    /// The same three roles the quest, power, bills and growing zones tabs use, so a player moving between
    /// them is not reading four different typographic systems: a display face for the tab's name, a condensed
    /// one for names in a column, and the mono for every figure and caption.
    ///
    /// Sizes are in points on the scale a word processor uses; see <see cref="UIFonts.PixelsPerPoint"/>.
    /// </summary>
    internal static class PawnFaces
    {
        /// <summary>Oswald. The tab's own name, and nothing else.</summary>
        internal const UIFace Display = UIFace.Oswald;

        /// <summary>Barlow Condensed. Map names and category labels, where width is the scarce thing.</summary>
        internal const UIFace Condensed = UIFace.BarlowCondensed;

        /// <summary>IBM Plex Mono. Counts, percentages and every caption.</summary>
        internal const UIFace Mono = UIFace.IBMPlexMono;

        internal static class Size
        {
            internal const float Title = 15.75f;
            internal const float Subtitle = 10.5f;

            /// <summary>The header's four figures.</summary>
            internal const float Readout = 12.75f;

            /// <summary>Small caps under a figure, and the rail's own heading.</summary>
            internal const float Caption = 7.5f;

            internal const float RailName = 11.25f;
            internal const float RailCount = 8.25f;

            /// <summary>A category chip's label, which sits beside its count.</summary>
            internal const float Chip = 9.75f;
        }
    }
}
