using Gideon.UIFramework.Components.Colors;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;

namespace Gideon.UIOverhaul.Features.Mechs
{
    /// <summary>
    /// The typefaces, sizes and accent this tab is set in, matching the other restyled tabs.
    ///
    /// The same three roles the quests, power, bills, growing, pawns, hospital and research tabs use, so a
    /// player moving between them is reading one typographic system rather than eight: a display face for the
    /// tab's name, a condensed one for names in a column, and the mono for every figure and caption.
    ///
    /// Sizes are in points on the scale a word processor uses; see <see cref="UIFonts.PixelsPerPoint"/>.
    /// </summary>
    internal static class MechsFaces
    {
        /// <summary>Oswald. The tab's name and a dialog's title, and nothing else.</summary>
        internal const UIFace Display = UIFace.Oswald;

        /// <summary>Barlow Condensed. Mech names, mechanitor names, group names, work types, chip labels.</summary>
        internal const UIFace Condensed = UIFace.BarlowCondensed;

        /// <summary>Barlow. Sentences, which on this tab means notices and the settings dialog's prose.</summary>
        internal const UIFace Body = UIFace.Barlow;

        /// <summary>IBM Plex Mono. Every figure and every small caps caption.</summary>
        internal const UIFace Mono = UIFace.IBMPlexMono;

        /// <summary>
        /// This tab's color: a pale gunmetal, defaulting to <c>#9FC6CE</c>.
        ///
        /// <b>It never encodes a state.</b> Charge, integrity and bandwidth all carry meaning-bearing colors
        /// already, and a work priority carries the same three the pawns tab spends on priorities. This draws
        /// only on chrome: the header mark and title, the segment underline, the selected row's bar, the
        /// selected rail entry's bar, the auto repair box when it is on, and the colony bandwidth meter.
        /// See <see cref="UIColorRole.TabMechs"/> for why it is a near-neutral rather than a hue.
        /// </summary>
        internal static Color AccentOf(UIColorPaletteDef palette)
        {
            return palette == null ? new Color(0.624f, 0.776f, 0.808f) : palette.TabMechs;
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

            /// <summary>Small caps under a figure, a rail heading, a card's field captions.</summary>
            internal const float Caption = 7.5f;

            /// <summary>A mechanitor or group name in the rail, and a mech's name on a deck row.</summary>
            internal const float RailName = 11.25f;

            /// <summary>A count on the right of a rail entry, and a mechanitor's bandwidth figure.</summary>
            internal const float RailCount = 8.25f;

            /// <summary>A filter chip's label, a work mode segment, a button.</summary>
            internal const float Chip = 9.75f;

            /// <summary>
            /// The figures on a deck row: bandwidth cost, charge percentage, integrity.
            ///
            /// Smaller than <see cref="Chip"/> on purpose: these form columns down the whole deck whether or
            /// not anybody meant them to, and a column of figures wants to be quiet.
            /// </summary>
            internal const float Figure = 8.25f;

            /// <summary>The selected mech's name at the top of the detail pane.</summary>
            internal const float Detail = 12.75f;

            /// <summary>
            /// The grey lines under it: weight class, bandwidth cost, group, overseer.
            ///
            /// Mostly figures and short codes, which is why they are the mono rather than the condensed face.
            /// </summary>
            internal const float Meta = 8.25f;

            /// <summary>Sentences: notices, empty states, the settings dialog's explanations.</summary>
            internal const float Prose = 9.75f;

            /// <summary>One label in a list in the detail pane: a damaged part, a field name.</summary>
            internal const float Row = 9.75f;

            /// <summary>
            /// The priority number in its box, which is the one figure on this tab you click.
            ///
            /// Larger than every other figure here because it is a control rather than a reading, and because
            /// a right click on it has to land on something worth aiming at.
            /// </summary>
            internal const float Priority = 10.5f;

            /// <summary>A dialog's own title, which is a heading rather than a tab name.</summary>
            internal const float DialogTitle = 13.5f;
        }
    }
}
