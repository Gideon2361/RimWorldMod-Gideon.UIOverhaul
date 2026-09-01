using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// The typefaces, sizes and accent this tab is set in, matching the other restyled tabs.
    ///
    /// The sizes are their figures, deliberately: a title here is the same 15.75pt a faith's name is, a rail
    /// entry the same 12.75pt.
    /// </summary>
    internal static class BillFaces
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
        /// This tab's color: a warm clay, defaulting to <c>#C4907A</c>.
        ///
        /// <b>Warm, because every other identity is cool.</b> Violet, steel blue, sage, teal and the growing
        /// tab's yellow green all sit on the cold half of the wheel, so a sixth cool hue had nowhere left to
        /// stand.
        ///
        /// <b>Kept clear of the warning gold,</b> which matters more here than elsewhere: amber is a state
        /// this screen shows often, on a bill with no stock or one nobody can work.
        /// </summary>
        internal static Color AccentOf(UIColorPaletteDef palette)
        {
            return palette == null ? new Color(0.769f, 0.565f, 0.478f) : palette.TabBills;
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

            /// <summary>A rail entry: a bench, or a filter across all of them.</summary>
            internal const float RailName = 12.75f;

            /// <summary>The count on the right of a rail entry.</summary>
            internal const float RailCount = 9.75f;
        }
    }
}
