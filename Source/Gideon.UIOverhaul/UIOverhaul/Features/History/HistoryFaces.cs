using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;

namespace Gideon.UIOverhaul.Features.History
{
    /// <summary>
    /// The typefaces, sizes, accent and chart series this tab is set in, matching the other restyled tabs.
    ///
    /// The same roles the quests, power, bills, growing, pawns, hospital and research tabs use, so a player
    /// moving between them is reading one typographic system rather than eight: a display face for the tab's
    /// name, a condensed one for names in a column, the mono for every figure, and the upright face for the one
    /// place this tab draws sentences.
    ///
    /// Sizes are in points on the scale a word processor uses; see <see cref="UIFonts.PixelsPerPoint"/>.
    /// </summary>
    internal static class HistoryFaces
    {
        /// <summary>Oswald. The tab's own name, and nothing else.</summary>
        internal const UIFace Display = UIFace.Oswald;

        /// <summary>Barlow Condensed. Rail entries, chip labels, list rows and battle names.</summary>
        internal const UIFace Condensed = UIFace.BarlowCondensed;

        /// <summary>
        /// Barlow. The letter body in the archive's detail pane, and nothing else.
        ///
        /// <b>The only tab so far that needs the upright face for content rather than for chrome.</b> Every
        /// other restyled screen draws names, figures and captions, all of which are short enough that a
        /// condensed face costs the reader nothing. A letter body is four or five lines of real prose, and
        /// condensed type read at that length is condensed type working against the person reading it.
        /// </summary>
        internal const UIFace Body = UIFace.Barlow;

        /// <summary>IBM Plex Mono. Every figure, every date and every small caps caption.</summary>
        internal const UIFace Mono = UIFace.IBMPlexMono;

        /// <summary>
        /// This tab's color: a muted cyan, defaulting to <c>#72B3C0</c>.
        ///
        /// <b>It never draws inside the axes.</b> The chart series own the plot; this draws only on the chrome
        /// around it, which is what lets the series be gold without arguing with the title above them. See
        /// <see cref="UIColorRole.TabHistory"/> for why this landed on cyan rather than on the green it was
        /// drawn in.
        /// </summary>
        internal static Color AccentOf(UIColorPaletteDef palette)
        {
            return palette == null ? new Color(0.447f, 0.702f, 0.753f) : palette.TabHistory;
        }

        /// <summary>
        /// The chart's series colors, darkest last, resolved from the palette by name.
        ///
        /// <b>Named colors rather than roles, because "fourth series in a chart" is not a job the interface
        /// does.</b> A role is something the whole suite reaches for; this is one screen's ramp.
        /// <c>UIColorPaletteDef.Custom</c> is the documented mechanism for exactly that: a compiled-in default
        /// that a theme may override and does not have to know about.
        ///
        /// <b>One hue at four steps, because the parts add up to the whole.</b> Wealth is the group this was
        /// drawn for, and there items, buildings and creatures sum to the total; vanilla paints those four
        /// yellow, amber, olive and sea green, as though they were four subjects rather than one quantity and
        /// its parts. A ramp says what the numbers are, and it is what lets the components be stacked bands
        /// under a single outline instead of four curves crossing each other.
        ///
        /// A group whose curves do <i>not</i> sum takes the same four colors as plain lines, which is the
        /// honest fallback: they are still four series and still need telling apart, and nothing about the ramp
        /// claims they are parts of anything.
        /// </summary>
        internal static Color Series(UIColorPaletteDef palette, int index)
        {
            Color[] fallback = Fallback;

            if (index < 0)
                index = 0;

            // Wrapping rather than clamping. A modded group with five recorders would otherwise draw its fifth
            // and fourth curves in the same color, and two curves the same color is worse than two curves the
            // same color as two others further up the list.
            index %= fallback.Length;

            return palette == null
                ? fallback[index]
                : palette.Custom("Gideon.Chart." + (char) ('A' + index), fallback[index]);
        }

        /// <summary>How many distinct series colors exist before they repeat.</summary>
        internal const int SeriesCount = 4;

        private static readonly Color[] Fallback =
        {
            new Color(0.878f, 0.765f, 0.486f), // #E0C37C
            new Color(0.761f, 0.627f, 0.353f), // #C2A05A
            new Color(0.576f, 0.467f, 0.247f), // #93773F
            new Color(0.420f, 0.341f, 0.188f)  // #6B5730
        };

        /// <summary>Point sizes, on the same scale the rest of the mod counts in.</summary>
        internal static class Size
        {
            /// <summary>The tab's name in the header.</summary>
            internal const float Title = 15.75f;

            /// <summary>The line under it.</summary>
            internal const float Subtitle = 10.5f;

            /// <summary>The header's four figures.</summary>
            internal const float Readout = 12.75f;

            /// <summary>Small caps under a figure, a rail heading, a column caption, a card heading.</summary>
            internal const float Caption = 7.5f;

            /// <summary>A rail entry's name.</summary>
            internal const float RailName = 11.25f;

            /// <summary>The value on the right of a rail entry.</summary>
            internal const float RailCount = 8.25f;

            /// <summary>A filter chip's label.</summary>
            internal const float Chip = 9.75f;

            /// <summary>An archive row's label, a battle's name, a statistics row's label.</summary>
            internal const float Row = 11.25f;

            /// <summary>A date in a column, a figure in a statistics row, a legend value.</summary>
            internal const float Figure = 9.75f;

            /// <summary>The one big number at the top of a statistics card.</summary>
            internal const float Headline = 18f;

            /// <summary>The letter body in the detail pane.</summary>
            internal const float Prose = 9.75f;

            /// <summary>
            /// The numbers down the side of the plot and along the bottom of it.
            ///
            /// The smallest thing on the tab on purpose. Axis labels are read once to calibrate the shape and
            /// then never again, and a plot whose furniture competes with its curves is a plot nobody reads.
            /// </summary>
            internal const float Axis = 7.5f;
        }
    }
}
