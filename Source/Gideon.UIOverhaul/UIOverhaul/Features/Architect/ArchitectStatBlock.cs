using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Architect
{
    /// <summary>One labeled stat, already formatted for display.</summary>
    public struct ArchitectStatRow
    {
        public string Label;
        public string Value;
    }

    /// <summary>
    /// The comparative stats shown for one option in the architect's option pane -- a material for a
    /// stuffable building, or one variant out of a designator dropdown.
    ///
    /// The point is choosing between options, so the rows are the ones that actually differ between them.
    /// Which rows those are depends on what is being placed:
    ///
    ///   building  -- hit points, work to build, flammability, beauty, market value
    ///   terrain   -- cleanliness, beauty, work to build, market value
    ///
    /// Beauty is on the building list because it is the stat that separates one material from another more than
    /// any other: gold carries a +20 offset and a x4 factor, jade +10 and x2.5, uranium x0.5, and steel nothing
    /// at all. Somebody choosing between a steel wall and a gold one is usually choosing on exactly this, which
    /// made it the one obvious number missing from the cards.
    ///
    /// It stays absent for the materials that do not move it, and that is deliberate rather than a gap. Beauty
    /// declares defaultBaseValue 0 and hideAtValue 0, so the game hides its own reading at zero -- the same
    /// answer the zero test in Add already gives. A steel wall drops the row; a gold one shows it.
    ///
    /// Nothing about apparel here, because nothing in the architect places apparel. Armor and insulation are
    /// apparel-only stats -- ArmorRating_Sharp and its siblings are declared in Stats_Apparel.xml under
    /// category Apparel and no building def references them -- so they belong with the bills work, where
    /// choosing a material for a garment is a real decision. A stuffed wall's toughness comes through
    /// MaxHitPoints instead, which is why that is the building row that matters.
    ///
    /// Values come from GetStatValueAbstract with the chosen stuff, which is the same call vanilla's own
    /// previews use, so a number here matches what the thing will have once placed.
    /// </summary>
    public static class ArchitectStatBlock
    {
        /// <summary>Reused: this is rebuilt whenever the pane redraws, which is every frame.</summary>
        private static readonly List<ArchitectStatRow> Rows = new List<ArchitectStatRow>();

        public static List<ArchitectStatRow> For(BuildableDef placing, ThingDef stuff)
        {
            Rows.Clear();

            if (placing == null)
                return Rows;

            if (placing is ThingDef)
            {
                Add(placing, stuff, StatDefOf.MaxHitPoints);
                Add(placing, stuff, StatDefOf.WorkToBuild);
                Add(placing, stuff, StatDefOf.Flammability);
                Add(placing, stuff, StatDefOf.Beauty);
                Add(placing, stuff, StatDefOf.MarketValue);
                return Rows;
            }

            // Terrain. Stone floors are not stuffed -- each stone gets its own TerrainDef, grouped under a
            // designator dropdown -- so the option pane for a floor is comparing whole terrains, and these
            // are the stats that separate them.
            Add(placing, stuff, Named("Cleanliness"));
            Add(placing, stuff, StatDefOf.Beauty);
            Add(placing, stuff, StatDefOf.WorkToBuild);
            Add(placing, stuff, StatDefOf.MarketValue);

            return Rows;
        }

        private static void Add(BuildableDef placing, ThingDef stuff, StatDef stat)
        {
            if (stat == null)
                return;

            float value = placing.GetStatValueAbstract(stat, stuff);

            // Omitted rather than shown as zero when the stat does not apply. Declared-in-statBases alone is
            // too strict a test: a building's market value is derived from its cost list and is never
            // declared, so a stat is kept if the def names it or if it resolves to something non-zero.
            if (!Declared(placing, stat) && value == 0f)
                return;

            Rows.Add(new ArchitectStatRow
            {
                Label = stat.LabelCap,
                Value = stat.ValueToString(value)
            });
        }

        private static bool Declared(BuildableDef placing, StatDef stat)
        {
            List<StatModifier> bases = placing.statBases;
            if (bases == null)
                return false;

            for (int i = 0; i < bases.Count; i++)
            {
                if (bases[i]?.stat == stat)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// A stat looked up by name, for one that StatDefOf does not expose.
        ///
        /// Cleanliness needs this and needs it to go through DefDatabase&lt;StatDef&gt;: two defs share that
        /// defName -- a RoomStatDef used for room quality, and the StatDef terrain declares in statBases --
        /// and only a typed lookup picks the right one.
        /// </summary>
        private static StatDef Named(string defName)
        {
            return DefDatabase<StatDef>.GetNamedSilentFail(defName);
        }
    }
}
