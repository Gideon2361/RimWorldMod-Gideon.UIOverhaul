using Gideon.UIFramework.Helpers;

namespace Gideon.UIOverhaul.Features.Diagnostics
{
    /// <summary>
    /// Typefaces and sizes for the loading console and the loading screen.
    ///
    /// <b>Figures in mono, prose in condensed.</b> Almost everything on these two screens is a number that
    /// changes while you watch it -- a running total, a count climbing toward a ceiling, a duration. In a
    /// proportional face each digit has its own width, so a readout shifts sideways every time it updates: the
    /// 1 is narrow, the 8 is wide, and the whole figure jitters. Mono digits are one width, so a number counts
    /// in place. That is legibility of live data rather than a preference.
    ///
    /// The prose around them -- phase names, warnings, the words under each readout -- goes in the condensed
    /// face, because a diagnostic panel is mostly a dense column of names and width is the scarce thing.
    ///
    /// Sizes are in points on the same scale a word processor uses; see <see cref="UIFonts.PixelsPerPoint"/>.
    /// </summary>
    internal static class LoadingFaces
    {
        /// <summary>IBM Plex Mono. Figures, durations, counts, timestamps.</summary>
        internal const UIFace Mono = UIFace.IBMPlexMono;

        /// <summary>Barlow Condensed. Names in a column, where width is the scarce thing.</summary>
        internal const UIFace Condensed = UIFace.BarlowCondensed;

        internal static class Size
        {
            /// <summary>The four big readouts. Largest, because they are the answer.</summary>
            internal const float Readout = 15f;

            /// <summary>The small caps caption over each readout.</summary>
            internal const float Caption = 7.5f;

            /// <summary>The line of prose under each readout.</summary>
            internal const float Sub = 8.25f;

            /// <summary>Row text in the log and the timings column.</summary>
            internal const float Row = 9f;

            /// <summary>Durations and timestamps beside a row.</summary>
            internal const float Figure = 8.25f;

            /// <summary>The panel's own title and its tab labels.</summary>
            internal const float Title = 10.5f;

            /// <summary>The header counter and the footer tally.</summary>
            internal const float Counter = 8.25f;
        }
    }
}
