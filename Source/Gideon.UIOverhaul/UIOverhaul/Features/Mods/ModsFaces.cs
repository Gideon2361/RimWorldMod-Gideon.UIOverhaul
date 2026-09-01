using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Mods
{
    /// <summary>
    /// The typefaces, sizes and identity color the mods page is set in, matching <c>FactionsFaces</c> and
    /// the rest of the suite.
    ///
    /// The sizes are the same figures the colony tabs use, deliberately. This page is not one of them, but a
    /// title here is still the same 15.75pt a faith's name is: screens that call the same thing a title and
    /// draw it at different sizes are two interfaces however alike the words are.
    /// </summary>
    internal static class ModsFaces
    {
        /// <summary>Oswald. The screen's name, and the selected mod's name in the detail pane.</summary>
        internal const UIFace Display = UIFace.Oswald;

        /// <summary>Barlow Condensed. Names in a column, where width is the scarce thing.</summary>
        internal const UIFace Condensed = UIFace.BarlowCondensed;

        /// <summary>Barlow. Sentences.</summary>
        internal const UIFace Body = UIFace.Barlow;

        /// <summary>IBM Plex Mono. Figures, small-caps labels, load order numerals and pills.</summary>
        internal const UIFace Mono = UIFace.IBMPlexMono;

        /// <summary>
        /// This page's color: a muted indigo, defaulting to <c>#8F96C9</c>.
        ///
        /// <b>Off the palette rather than out of this file,</b> so a theme can move it. See
        /// <see cref="UIColorRole.PageMods"/>, whose notes cover why it shares a hue with the research tab.
        /// </summary>
        internal static Color AccentOf(UIColorPaletteDef palette)
        {
            return palette == null ? new Color(0.561f, 0.588f, 0.788f) : palette.PageMods;
        }

        /// <summary>Point sizes, on the same scale the rest of the mod counts in.</summary>
        internal static class Size
        {
            /// <summary>The screen's name in the header.</summary>
            internal const float Title = 15.75f;

            /// <summary>The line under it: how many are active, and whether anything is wrong.</summary>
            internal const float Subtitle = 10.5f;

            /// <summary>A figure in a header readout.</summary>
            internal const float Readout = 12.75f;

            /// <summary>The small-caps word under a readout figure.</summary>
            internal const float Caption = 7.5f;

            /// <summary>A rail heading, set in small caps.</summary>
            internal const float RailHead = 9.375f;

            /// <summary>A rail entry: a scope, a problem kind, a source or a saved list.</summary>
            internal const float RailName = 12.75f;

            /// <summary>The count on the right of a rail entry.</summary>
            internal const float RailCount = 9f;

            /// <summary>A mod's name in the list.</summary>
            internal const float RowName = 12.75f;

            /// <summary>The load order numeral, and the source word beside a name.</summary>
            internal const float RowFigure = 9.375f;

            /// <summary>A state pill on a row, and the column strip above them.</summary>
            internal const float Chip = 7.875f;

            /// <summary>The selected mod's name in the detail pane.</summary>
            internal const float DetailName = 14.25f;

            /// <summary>A label in the detail pane's key and value grid.</summary>
            internal const float DetailLabel = 7.875f;

            /// <summary>Body text: the description, a requirement line, the problem band.</summary>
            internal const float DetailBody = 10.5f;
        }

        /// <summary>Small caps, the way every other screen in the suite spells a label.</summary>
        internal static string Caps(string text)
        {
            return text.NullOrEmpty() ? text : text.ToUpperInvariant();
        }
    }
}
