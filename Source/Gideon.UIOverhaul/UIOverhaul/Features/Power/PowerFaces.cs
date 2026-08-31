using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Power
{
    /// <summary>
    /// The typefaces and sizes the power tab is set in.
    ///
    /// The same four faces the ideoligion and quest screens use, for the same reason: they are already in the
    /// font bundle, and a player moving between this mod's tabs should be reading one convention rather than
    /// learning a third. Sizes are in points, so two faces sharing a row are comparable.
    /// </summary>
    internal static class PowerFaces
    {
        internal const UIFace Display = UIFace.Oswald;
        internal const UIFace Condensed = UIFace.BarlowCondensed;
        internal const UIFace Body = UIFace.Barlow;
        internal const UIFace Mono = UIFace.IBMPlexMono;

        internal static class Size
        {
            internal const float Title = 15.75f;
            internal const float Subtitle = 10.5f;
            internal const float Readout = 12.75f;
            internal const float Caption = 7.5f;
            internal const float BlockHead = 7.875f;
            internal const float RailHead = 9.375f;
            internal const float RailName = 12.75f;
            internal const float RailCount = 9f;
            internal const float Name = 11.25f;
            internal const float Body = 10.5f;
            internal const float Label = 7.875f;

            /// <summary>The balance figures, which are the largest thing on the screen after the title.</summary>
            internal const float Figure = 15f;

            internal const float Small = 8.25f;
            internal const float Chip = 7.5f;
        }

        internal static string Caps(string text)
        {
            return text.NullOrEmpty() ? text : text.ToUpperInvariant();
        }
    }
}
