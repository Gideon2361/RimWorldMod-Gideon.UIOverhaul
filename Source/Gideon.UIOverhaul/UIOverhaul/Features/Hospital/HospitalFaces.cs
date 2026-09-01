using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>
    /// The typefaces, sizes and accent this tab is set in, matching the other restyled tabs.
    ///
    /// The sizes are their figures, deliberately: a title here is the same 15.75pt a faith's name is, a rail
    /// entry the same 12.75pt.
    /// </summary>
    internal static class HospitalFaces
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
        /// This tab's color: a muted orchid, defaulting to <c>#CC8BC7</c>.
        ///
        /// <b>Not the red a hospital would reach for.</b> Red is <c>danger</c>, and this is the one screen
        /// where danger genuinely appears in the rows; a title in the alarm color over a condition in the
        /// alarm color is the mistake the bills tab made with amber.
        ///
        /// <b>Not the rose either, which was the intent.</b> The pawns tab reached the magenta side first,
        /// and two identities that close on the two tabs that both list the same people would have been a
        /// distinction nobody could make. See <see cref="UIColorRole.TabHospital"/>.
        /// </summary>
        internal static Color AccentOf(UIColorPaletteDef palette)
        {
            return palette == null ? new Color(0.800f, 0.545f, 0.780f) : palette.TabHospital;
        }

        /// <summary>Point sizes, on the same scale the rest of the mod counts in.</summary>
        internal static class Size
        {
            /// <summary>The screen's name in the header.</summary>
            internal const float Title = 15.75f;

            /// <summary>The line under it.</summary>
            internal const float Subtitle = 10.5f;

            /// <summary>A rail heading, set in small caps.</summary>
            internal const float RailHead = 9.375f;

            /// <summary>A rail entry: a triage group, or a scope across all of them.</summary>
            internal const float RailName = 12.75f;

            /// <summary>The count on the right of a rail entry.</summary>
            internal const float RailCount = 9.75f;
        }
    }
}
