using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Factions
{
    /// <summary>
    /// The typefaces and sizes the factions tab is set in.
    ///
    /// The same four faces every other restyled tab uses, at the sizes the power tab settled on: a player
    /// moving between this mod's tabs should be reading one convention rather than learning an eighth. Sizes
    /// are in points rather than picked from <c>GameFont</c>, so a name and the figure beside it are actually
    /// comparable.
    /// </summary>
    internal static class FactionsFaces
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

            /// <summary>A faction's name, which is the largest thing in a row.</summary>
            internal const float Name = 12.75f;

            /// <summary>The kind and the leader, under the name.</summary>
            internal const float Sub = 9.375f;

            internal const float Standing = 12.375f;
            internal const float Body = 10.5f;
            internal const float Label = 7.875f;

            /// <summary>Goodwill, which is the figure the whole row is built around.</summary>
            internal const float Figure = 10.5f;

            internal const float Small = 8.25f;
            internal const float Chip = 7.5f;
        }

        internal static string Caps(string text)
        {
            return text.NullOrEmpty() ? text : text.ToUpperInvariant();
        }
    }
}
