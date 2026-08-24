using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// The colour a node wears to say where it came from.
    ///
    /// <b>Why the source needs a mark at all.</b> Grouping by theme is the whole point of the rework, and it
    /// costs the one thing the mod-block layout gave away for free: a glance told you what a mod had added. So
    /// provenance moves from being the layout to being a mark on the node, plus a filter. It is the same
    /// information demoted from an axis to an attribute, which is the trade the whole feature rests on.
    ///
    /// <b>On the top edge, not the left, and the mockup had this wrong.</b> The left stripe already carries the
    /// project's state and has since 14162 -- see <see cref="ResearchNodeArt"/>, where the stripe and the border
    /// are deliberately two channels so a selected blocked project still reads as blocked. Taking that away for
    /// provenance would trade something load-bearing for something decorative. The top edge was unused.
    ///
    /// <b>Hidden when the grouping is already by source,</b> because the block heading says it then and a mark
    /// repeating the heading on every node in the block is chrome.
    ///
    /// <b>Core and the five expansions get fixed colours; a mod gets one derived from its own packageId.</b> Two
    /// honest limits on that. Forty-two mods across the hue circle land about eight degrees apart, so a mod's
    /// colour is good for telling one node from the node beside it and useless for identifying which mod it is --
    /// the tooltip does that, and it is the only thing that can. And a colour is stable across sessions because it
    /// comes from the packageId rather than from load order, so a node does not change colour when the player
    /// installs something unrelated.
    /// </summary>
    internal static class ResearchSourceMarks
    {
        /// <summary>How tall the mark is. Two pixels: it is a hint, not a heading.</summary>
        internal const float Thickness = 2f;

        /// <summary>
        /// Fixed colours for the official content, matching the mockup and the band listing.
        ///
        /// Keyed on packageId rather than on name, because a name is translated and a packageId is not.
        /// </summary>
        private static readonly Dictionary<string, Color> official =
            new Dictionary<string, Color>
            {
                { "ludeon.rimworld", new Color(0.541f, 0.565f, 0.600f) },
                { "ludeon.rimworld.royalty", new Color(0.851f, 0.694f, 0.290f) },
                { "ludeon.rimworld.ideology", new Color(0.788f, 0.416f, 0.353f) },
                { "ludeon.rimworld.biotech", new Color(0.373f, 0.659f, 0.373f) },
                { "ludeon.rimworld.anomaly", new Color(0.608f, 0.447f, 0.851f) },
                { "ludeon.rimworld.odyssey", new Color(0.373f, 0.659f, 0.788f) }
            };

        private static readonly Dictionary<ModContentPack, Color> cache =
            new Dictionary<ModContentPack, Color>();

        internal static void Invalidate()
        {
            cache.Clear();
        }

        /// <summary>Whether the mark should be drawn at all under the grouping in force.</summary>
        internal static bool Wanted(ResearchGrouping grouping)
        {
            return grouping != ResearchGrouping.Source;
        }

        internal static Color ColorFor(ResearchProjectDef project, UIColorPaletteDef palette)
        {
            ModContentPack pack = project == null ? null : project.modContentPack;

            if (pack == null)
                return palette == null ? Color.grey : palette.TextDisabled;

            Color found;

            if (cache.TryGetValue(pack, out found))
                return found;

            found = UIGuard.Try("Research.SourceColor", () => Compute(pack), Color.grey, null);

            cache[pack] = found;

            return found;
        }

        private static Color Compute(ModContentPack pack)
        {
            string id = pack.PackageIdPlayerFacing;

            if (!id.NullOrEmpty())
            {
                Color known;

                if (official.TryGetValue(id.ToLowerInvariant(), out known))
                    return known;
            }
            else
            {
                id = pack.Name ?? string.Empty;
            }

            // A stable hash of our own rather than string.GetHashCode, which is not guaranteed to be the same
            // between runtimes or even between runs of the same one -- a node changing colour on restart would
            // be a bug nobody could reproduce on demand.
            int hash = 17;

            for (int i = 0; i < id.Length; i++)
                hash = hash * 31 + id[i];

            // Saturation and value are fixed so every mod's colour is equally readable against both themes, and
            // only the hue varies. Kept off full saturation: eleven band hues are already on this canvas, and a
            // source mark that shouted would compete with the thing it is annotating.
            float hue = (hash & 0x7FFFFFFF) % 360 / 360f;

            return Color.HSVToRGB(hue, 0.45f, 0.80f);
        }

        /// <summary>The name to show for a project's source, for a tooltip.</summary>
        internal static string NameFor(ResearchProjectDef project)
        {
            ModContentPack pack = project == null ? null : project.modContentPack;

            return pack == null || pack.Name.NullOrEmpty() ? "Unknown" : pack.Name;
        }

        /// <summary>Draws the mark along the top inside edge of a node.</summary>
        internal static void Draw(Rect node, ResearchProjectDef project, UIColorPaletteDef palette, bool dimmed)
        {
            Color color = ColorFor(project, palette);

            if (dimmed)
                color = new Color(color.r, color.g, color.b, 0.35f);

            Widgets.DrawBoxSolid(new Rect(node.x + 1f, node.y + 1f, node.width - 2f, Thickness), color);
        }
    }
}
