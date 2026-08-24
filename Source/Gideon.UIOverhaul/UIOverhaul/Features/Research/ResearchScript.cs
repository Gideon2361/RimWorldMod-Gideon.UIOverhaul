using Verse;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// Which characters an undiscovered Anomaly project is written in.
    ///
    /// <b>Five options because the choice is a taste, and one of them is Off.</b> An unreadable screen is a
    /// preference rather than an improvement, and disliking it should not cost somebody the rest of the tab.
    ///
    /// <b><see cref="Generated"/> is the default and the only one that needs nothing.</b> Its marks are
    /// rasterized in code through <c>UIGlyphCanvas</c>, so they tint with the palette and there is no atlas to
    /// go missing. The three named scripts are baked atlases under the mod's Fonts folder; see
    /// <see cref="ResearchScriptAtlas"/> for why a font file cannot simply be loaded.
    /// </summary>
    public enum ResearchScript
    {
        /// <summary>Marks drawn in code: arcs, rings, spikes and dots. Needs no font.</summary>
        Generated,

        /// <summary>Noto Sans Imperial Aramaic. Angular and sparse, two to four straight strokes a character.</summary>
        ImperialAramaic,

        /// <summary>Noto Sans Mende Kikakui. The busiest of the three: loops, hooks and dots.</summary>
        MendeKikakui,

        /// <summary>Noto Sans Siddham. Characters hanging off a headline, the way Devanagari does.</summary>
        Siddham,

        /// <summary>Nothing is masked. Every project reads in plain words.</summary>
        Off
    }

    /// <summary>
    /// The names and files behind <see cref="ResearchScript"/>.
    ///
    /// <b>Separate from the enum because three unrelated places ask.</b> The settings reader needs to parse a
    /// name out of a hand-editable file, the picker needs a name for a tooltip, and the atlas needs a file name.
    /// Putting all three on the enum's own switch statements is how one of them ends up disagreeing.
    /// </summary>
    internal static class ResearchScripts
    {
        /// <summary>Every option, in picker order.</summary>
        internal static readonly ResearchScript[] All =
        {
            ResearchScript.Generated,
            ResearchScript.ImperialAramaic,
            ResearchScript.MendeKikakui,
            ResearchScript.Siddham,
            ResearchScript.Off
        };

        /// <summary>
        /// The readable name, which lives in the tooltip.
        ///
        /// <b>The picker itself is labelled in the script's own characters,</b> asked for on 2026-08-23 in those
        /// words: an option you cannot preview is an option you have to pick twice. So the name goes where this
        /// mod puts that sort of thing anyway.
        /// </summary>
        internal static string Named(ResearchScript script)
        {
            switch (script)
            {
                case ResearchScript.ImperialAramaic: return "Imperial Aramaic";
                case ResearchScript.MendeKikakui: return "Mende Kikakui";
                case ResearchScript.Siddham: return "Siddham";
                case ResearchScript.Off: return "Off";
                default: return "Generated";
            }
        }

        /// <summary>The baked atlas's file name, or null for the options that have no atlas.</summary>
        internal static string AtlasFor(ResearchScript script)
        {
            switch (script)
            {
                case ResearchScript.ImperialAramaic: return "NotoSansImperialAramaic";
                case ResearchScript.MendeKikakui: return "NotoSansMendeKikakui";
                case ResearchScript.Siddham: return "NotoSansSiddham";
                default: return null;
            }
        }

        /// <summary>
        /// A name from the settings file, falling back rather than complaining.
        ///
        /// The same reasoning as every other named setting in this mod: the config is hand-editable and a
        /// misspelled script is not worth a warning on the way into the game.
        /// </summary>
        internal static ResearchScript Parse(string value)
        {
            if (value.NullOrEmpty())
                return ResearchScript.Generated;

            foreach (ResearchScript script in All)
            {
                if (value.EqualsIgnoreCase(script.ToString()))
                    return script;
            }

            return ResearchScript.Generated;
        }
    }
}
