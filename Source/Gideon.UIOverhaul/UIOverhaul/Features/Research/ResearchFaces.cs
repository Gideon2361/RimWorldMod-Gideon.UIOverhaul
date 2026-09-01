using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// The typefaces, sizes and accent this tab is set in, matching the other restyled tabs.
    ///
    /// The same three roles the quests, power, bills, growing, pawns and hospital tabs use, so a player moving
    /// between them is reading one typographic system rather than seven: a display face for the tab's name, a
    /// condensed one for names in a column, and the mono for every figure and caption.
    ///
    /// Sizes are in points on the scale a word processor uses; see <see cref="UIFonts.PixelsPerPoint"/>.
    /// </summary>
    internal static class ResearchFaces
    {
        /// <summary>Oswald. The tab's own name, and nothing else.</summary>
        internal const UIFace Display = UIFace.Oswald;

        /// <summary>Barlow Condensed. Project names, band headings, rail entries and chip labels.</summary>
        internal const UIFace Condensed = UIFace.BarlowCondensed;

        /// <summary>Barlow. Sentences, which on this tab means the detail panel and the queue's footer note.</summary>
        internal const UIFace Body = UIFace.Barlow;

        /// <summary>IBM Plex Mono. Every figure and every small caps caption.</summary>
        internal const UIFace Mono = UIFace.IBMPlexMono;

        /// <summary>
        /// This tab's color: a muted iris, defaulting to <c>#8B90CC</c>.
        ///
        /// <b>It never touches the canvas.</b> The twelve band colors own everything inside the graph; this
        /// draws only on the chrome around it, in the header mark and title, the segment underline and the
        /// selected row of each rail. That separation is what lets it sit eleven degrees from the Flight and
        /// Space band without the two ever being compared. See <see cref="UIColorRole.TabResearch"/>.
        /// </summary>
        internal static Color AccentOf(UIColorPaletteDef palette)
        {
            return palette == null ? new Color(0.545f, 0.565f, 0.800f) : palette.TabResearch;
        }

        /// <summary>Point sizes, on the same scale the rest of the mod counts in.</summary>
        internal static class Size
        {
            /// <summary>The tab's name in the header.</summary>
            internal const float Title = 15.75f;

            /// <summary>The line under it.</summary>
            internal const float Subtitle = 10.5f;

            /// <summary>The header's four figures.</summary>
            internal const float Readout = 12.75f;

            /// <summary>Small caps under a figure, a rail heading, and the strip's own captions.</summary>
            internal const float Caption = 7.5f;

            /// <summary>A block name in the contents rail, and a project name in the queue.</summary>
            internal const float RailName = 11.25f;

            /// <summary>The count on the right of a rail entry, and a queued project's day figure.</summary>
            internal const float RailCount = 8.25f;

            /// <summary>A filter chip's label, which sits beside its count.</summary>
            internal const float Chip = 9.75f;

            /// <summary>
            /// A band heading on the canvas, which is set in caps and has to hold its own against nodes.
            /// </summary>
            internal const float Band = 11.25f;

            /// <summary>
            /// The cost and day figure on the right of every node.
            ///
            /// Smaller than <see cref="Chip"/> on purpose: these form a column down the whole canvas whether
            /// or not anybody meant them to, and a column of figures wants to be quiet.
            /// </summary>
            internal const float Figure = 8.25f;

            /// <summary>The selected project's name at the top of the detail panel.</summary>
            internal const float Detail = 12.75f;

            /// <summary>
            /// The two grey lines under it: the tech level and cost, and how far through it the colony is.
            ///
            /// Both are mostly figures, which is why they are the mono rather than the condensed face.
            /// </summary>
            internal const float Meta = 8.25f;

            /// <summary>
            /// Sentences: a project's description, the note saying why it was filed where it was, and the
            /// warning under a queue entry that cannot start.
            /// </summary>
            internal const float Prose = 9.75f;

            /// <summary>
            /// One name in a list in the detail panel: an unlock, a prerequisite, something this leads to.
            ///
            /// The same size as a chip, because these are the same kind of thing -- a short label to scan
            /// rather than a sentence to read.
            /// </summary>
            internal const float Row = 9.75f;

            /// <summary>
            /// A project's name on its node, which is the most-repeated string on the screen.
            ///
            /// The same size as a name in the rail, because it is the same kind of thing. It used to be
            /// whatever <c>GameFont.Small</c> mapped to in this face, which is a size nobody chose: it came
            /// out of matching RimWorld's line box, so the node was as tall as the game's font wanted rather
            /// than as tall as the words needed.
            /// </summary>
            internal const float Node = 11.25f;

            /// <summary>A dialog's own title, which is a heading rather than a tab name.</summary>
            internal const float DialogTitle = 13.5f;
        }
    }
}
