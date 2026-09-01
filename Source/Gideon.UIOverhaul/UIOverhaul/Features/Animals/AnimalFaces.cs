using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// The typefaces, sizes and accent this tab is set in, matching <c>IdeoFaces</c> and
    /// <c>CorpseFaces</c>.
    ///
    /// The sizes are the same figures those two use, deliberately: a title here is the same 15.75pt a
    /// faith's name is, a rail entry the same 12.75pt. Screens that both call something a title and draw it
    /// at different sizes are two interfaces, however alike the words are.
    /// </summary>
    internal static class AnimalFaces
    {
        /// <summary>Oswald. The screen's name, and only that.</summary>
        internal const UIFace Display = UIFace.Oswald;

        /// <summary>Barlow Condensed. Names in a column, where width is the scarce thing.</summary>
        internal const UIFace Condensed = UIFace.BarlowCondensed;

        /// <summary>Barlow. Sentences.</summary>
        internal const UIFace Body = UIFace.Barlow;

        /// <summary>IBM Plex Mono. Figures, small-caps labels and chips.</summary>
        internal const UIFace Mono = UIFace.IBMPlexMono;

        /// <summary>
        /// This tab's color: a muted sage, defaulting to <c>#98AC80</c>.
        ///
        /// <b>Green for animals, and deliberately not the success green.</b> <c>#61C461</c> is saturated and
        /// bright; this is neither, so a sage title cannot be misread as a healthy reading on a screen that
        /// is full of actual health readings.
        ///
        /// <b>Off the palette rather than out of this file,</b> so a theme can move it. See
        /// <see cref="UIColorRole.TabAnimals"/>.
        /// </summary>
        internal static Color AccentOf(UIColorPaletteDef palette)
        {
            return palette == null ? new Color(0.596f, 0.675f, 0.502f) : palette.TabAnimals;
        }

        /// <summary>Point sizes, on the same scale the rest of the mod counts in.</summary>
        internal static class Size
        {
            /// <summary>The screen's name in the header.</summary>
            internal const float Title = 15.75f;

            /// <summary>The line under it: which map, and what is standing on it.</summary>
            internal const float Subtitle = 10.5f;

            /// <summary>A rail heading, set in small caps.</summary>
            internal const float RailHead = 9.375f;

            /// <summary>A rail entry: a scope, or a kind of standing order.</summary>
            internal const float RailName = 12.75f;

            /// <summary>The count on the right of a rail entry.</summary>
            internal const float RailCount = 9.75f;
        }
    }
}
