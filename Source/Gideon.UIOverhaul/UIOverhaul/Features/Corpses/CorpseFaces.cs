using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;

namespace Gideon.UIOverhaul.Features.Corpses
{
    /// <summary>
    /// The typefaces, sizes and accent this tab is set in, matching what <c>IdeoFaces</c> does for the
    /// ideoligion tab.
    ///
    /// <b>The sizes are the same numbers, deliberately.</b> A title here is the same 15.75pt a faith's name
    /// is, a rail entry the same 12.75pt. Two screens that both call something a title and draw it at
    /// different sizes are two screens, however similar the words are; sharing the figures is what makes them
    /// one interface.
    /// </summary>
    internal static class CorpseFaces
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
        /// This tab's own color, and the first in the mod that is not the palette's accent.
        ///
        /// <b>A faded violet, defaulting to <c>#A98FC8</c>.</b> Every screen titled itself in
        /// <c>palette.Accent</c> until Aaron chose on 2026-08-31 to give tabs their own identities, and this
        /// is the first of them. Violet reads as solemn beside the mod's blue without reaching for a status
        /// color, and nothing semantic sits near it, so a violet mark here is never mistaken for a warning.
        ///
        /// <b>It comes off the palette rather than out of this file,</b> which is what keeps a per-tab color
        /// from being the one part of the interface that ignores the player's theme. Somebody building a
        /// palette sets <c>tabTheDead</c> beside every other color they are already setting; somebody who
        /// does not gets the default. Setting it equal to the accent puts this tab back in step with the
        /// rest, so the identity is opt-out rather than imposed.
        /// </summary>
        internal static Color AccentOf(UIColorPaletteDef palette)
        {
            return palette == null ? new Color(0.663f, 0.561f, 0.784f) : palette.TabTheDead;
        }

        /// <summary>Point sizes, on the same scale the rest of the mod counts in.</summary>
        internal static class Size
        {
            /// <summary>The screen's name in the header.</summary>
            internal const float Title = 15.75f;

            /// <summary>The line under it: how many bodies, and how many are still waiting.</summary>
            internal const float Subtitle = 10.5f;

            /// <summary>A rail heading, set in small caps.</summary>
            internal const float RailHead = 9.375f;

            /// <summary>A rail entry: a group of the dead, or a view of the ground.</summary>
            internal const float RailName = 12.75f;

            /// <summary>The count on the right of a rail entry.</summary>
            internal const float RailCount = 9.75f;
        }
    }
}
