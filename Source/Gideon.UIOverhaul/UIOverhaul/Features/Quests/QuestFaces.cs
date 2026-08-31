using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Quests
{
    /// <summary>
    /// The typefaces and sizes the quests tab is set in.
    ///
    /// <b>The same four faces as the ideoligion screens, for the same reason.</b> They are already in the mod's
    /// font bundle, so a screen built from them needs nothing sourced, licensed or baked; and a player moving
    /// between two of this mod's tabs should be reading the same furniture rather than learning a second
    /// convention. The mapping is the one the mockups set: a display face for a title, a condensed face for
    /// names in a column, a body face for sentences, a monospace for figures and small-caps labels.
    ///
    /// <b>Sized in points, taken from the mockup.</b> A <c>GameFont</c> means "fill the line box RimWorld fills",
    /// which is a row height rather than a size, and two faces agreeing on a GameFont still draw at visibly
    /// different sizes. Points are comparable across faces, which is what lets one row carry two of them.
    /// </summary>
    internal static class QuestFaces
    {
        /// <summary>Oswald. A quest's name where it is the heading, and nothing else.</summary>
        internal const UIFace Display = UIFace.Oswald;

        /// <summary>Barlow Condensed. Names in a column, where width is the scarce thing.</summary>
        internal const UIFace Condensed = UIFace.BarlowCondensed;

        /// <summary>Barlow. The lines that read as sentences.</summary>
        internal const UIFace Body = UIFace.Barlow;

        /// <summary>IBM Plex Mono. Figures, deadlines, small-caps labels and chips.</summary>
        internal const UIFace Mono = UIFace.IBMPlexMono;

        /// <summary>The mockup's own sizes, in points.</summary>
        internal static class Size
        {
            /// <summary>The window's title, and a quest's name when the detail pane is showing it.</summary>
            internal const float Title = 21f;

            /// <summary>The line under a title.</summary>
            internal const float Subtitle = 14f;

            /// <summary>A header readout's figure, and the caption under it.</summary>
            internal const float Readout = 17f;

            internal const float Caption = 10f;

            /// <summary>A block heading and the suffix on the right of it.</summary>
            internal const float BlockHead = 10.5f;

            /// <summary>The rail's two headings, which head the panel rather than a box inside it.</summary>
            internal const float RailHead = 12.5f;

            /// <summary>A rail entry, and the count beside it.</summary>
            internal const float RailName = 17f;

            internal const float RailCount = 13f;

            /// <summary>A quest's name on a card.</summary>
            internal const float Name = 17f;

            /// <summary>A sentence: what a quest gives, costs, risks.</summary>
            internal const float Body = 14f;

            /// <summary>The small-caps label at the head of one of those lines.</summary>
            internal const float Label = 10.5f;

            /// <summary>A figure that lines up down the screen.</summary>
            internal const float Figure = 12f;

            /// <summary>The quieter second figure on a row.</summary>
            internal const float Small = 11f;

            /// <summary>A deadline.</summary>
            internal const float When = 11.5f;

            /// <summary>A chip.</summary>
            internal const float Chip = 10f;
        }

        /// <summary>
        /// Upper case, for the labels the mockup sets as small caps.
        ///
        /// Invariant rather than culture-aware, with the same known cost the ideoligion screens accept: a
        /// Turkish or Azeri locale wants a dotted capital from a dotted lower case i and will not get one.
        /// RimWorld's own interface takes the same shortcut.
        /// </summary>
        internal static string Caps(string text)
        {
            return text.NullOrEmpty() ? text : text.ToUpperInvariant();
        }
    }
}
