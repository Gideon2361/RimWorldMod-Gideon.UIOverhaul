using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// What a pen grows against what the animals in it eat, quadrum by quadrum.
    ///
    /// <b>Every figure is vanilla's own.</b> Nutrition grown per day per quadrum, nutrition eaten per day and the
    /// hay already stockpiled all come off <c>PenFoodCalculator</c>, which is the same object the pen marker's
    /// inspect pane reads. Nothing here models grass growth; it reports what the game already worked out.
    /// </summary>
    internal struct PastureReading
    {
        internal bool Available;

        /// <summary>A pen that is not closed grows nothing dependable, and the game says so first.</summary>
        internal bool Unenclosed;

        /// <summary>Nutrition grown per day, one entry per quadrum in chronological order.</summary>
        internal float[] PerQuadrum;

        internal float ConsumptionPerDay;

        internal float StockpiledNutrition;

        /// <summary>Cells in the pen, and how many of those can grow anything.</summary>
        internal int Cells;

        internal int SoilCells;

        /// <summary>The quadrum with the least left over, which is the one that decides the herd's size.</summary>
        internal Quadrum WorstQuadrum;

        /// <summary>Grown minus eaten in that quadrum. Negative is a herd that starves unless something changes.</summary>
        internal float WorstMargin;

        /// <summary>
        /// What the pen grows per day in that quadrum.
        ///
        /// Carried rather than looked up again from <see cref="PerQuadrum"/>, because that array is in
        /// chronological order and a quadrum is an enum: the two happen to agree in this version of the game and
        /// are not the same thing, as RimWorld having a second list in a different order shows.
        /// </summary>
        internal float WorstGrown;

        internal bool Short => Available && WorstMargin < 0f;

        /// <summary>How long the stockpile covers the worst quadrum's shortfall, in days.</summary>
        internal float DaysOfStockpile
        {
            get
            {
                if (!Short || StockpiledNutrition <= 0f)
                    return 0f;

                return StockpiledNutrition / -WorstMargin;
            }
        }
    }

    /// <summary>
    /// The pasture arithmetic, lifted out of the pen marker's inspect pane and put where the head count is.
    ///
    /// <b>This is the question the animals tab has never answered.</b> A player looking at seven muffalo wants to
    /// know whether seven is too many, and the game does know: it computes the pen's growth per quadrum and the
    /// herd's consumption, and then shows it on a fence post, one pen at a time, in a panel nobody opens while
    /// deciding how many animals to keep. Putting it beside the count and the auto slaughter limit is the whole
    /// point, because those three numbers are one decision.
    ///
    /// <b>Only penned animals have a pasture, and the readout says nothing about the rest.</b> Chickens and dogs
    /// eat from the kitchen, so a forecast for them would be a different calculation about a different resource,
    /// and inventing one would be worse than leaving the column blank. The reading is empty unless the species is
    /// rope managed and actually in a pen.
    ///
    /// <b>Cached by the game, not by us.</b> <c>PenFoodCalculator</c> is rebuilt at most every twenty ticks
    /// behind vanilla's own accessor, which is exactly the policy this tab wants, so asking it once per drawn row
    /// costs a field read almost every time. Nothing here adds a second cache on top of that.
    /// </summary>
    internal static class AnimalPasture
    {
        /// <summary>Scratch for the colony wide walk, so a per frame readout allocates nothing.</summary>
        private static readonly List<CompAnimalPenMarker> Seen = new List<CompAnimalPenMarker>();

        /// <summary>
        /// The reading for one pen, or an unavailable reading when there is no pen.
        ///
        /// <b>The worst quadrum is picked over the whole year rather than from today forward,</b> which is
        /// deliberate: a herd that survives autumn and starves in winter is a herd that is too big now, and a
        /// forecast that only looked ahead would go quiet in spring and say nothing until it was too late to do
        /// anything about it.
        /// </summary>
        internal static PastureReading ForPen(CompAnimalPenMarker pen)
        {
            PastureReading reading = new PastureReading { WorstQuadrum = Quadrum.Undefined };

            if (pen == null)
                return reading;

            return UIGuard.Try("Animals.Pasture", () => Read(pen), reading, null);
        }

        private static PastureReading Read(CompAnimalPenMarker pen)
        {
            PastureReading reading = new PastureReading { WorstQuadrum = Quadrum.Undefined };

            PenFoodCalculator food = pen.PenFoodCalculator;

            if (food == null)
                return reading;

            reading.Available = true;
            reading.Unenclosed = food.Unenclosed;
            reading.ConsumptionPerDay = food.SumNutritionConsumptionPerDay;
            reading.StockpiledNutrition = food.sumStockpiledNutritionAvailableNow;
            reading.Cells = food.numCells;
            reading.SoilCells = food.numCellsSoil;

            List<Quadrum> quadrums = QuadrumUtility.QuadrumsInChronologicalOrder;

            reading.PerQuadrum = new float[quadrums.Count];

            for (int i = 0; i < quadrums.Count; i++)
            {
                Quadrum quadrum = quadrums[i];
                float grown = food.nutritionPerDayPerQuadrum.ForQuadrum(quadrum);

                reading.PerQuadrum[i] = grown;

                float margin = grown - reading.ConsumptionPerDay;

                if (reading.WorstQuadrum != Quadrum.Undefined && margin >= reading.WorstMargin)
                    continue;

                reading.WorstQuadrum = quadrum;
                reading.WorstMargin = margin;
                reading.WorstGrown = grown;
            }

            return reading;
        }

        /// <summary>
        /// The reading for a species, which is the reading for the pen it is standing in.
        ///
        /// Unavailable for a species that is not penned, and for a group split across pens, where a single answer
        /// would have to pick one pen and pretend. A split group shows its pen column as mixed instead, which is
        /// the more useful thing to know first.
        /// </summary>
        internal static PastureReading ForGroup(AnimalGroup group)
        {
            if (group == null || group.Kind != AnimalKind.Colony || group.PenMixed)
                return new PastureReading { WorstQuadrum = Quadrum.Undefined };

            return ForPen(group.Pen);
        }

        /// <summary>
        /// How many animals of this species the worst quadrum is short by.
        ///
        /// <b>Expressed in animals because that is the lever the player has.</b> A shortfall of 1.4 nutrition per
        /// day is a number nobody can act on; "two muffalo too many" is an instruction. Rounded up, since half an
        /// animal over the line still starves the pen.
        /// </summary>
        internal static int ShortBy(PastureReading reading, AnimalGroup group)
        {
            if (!reading.Short || group == null || group.Count == 0 || group.NutritionPerDay <= 0f)
                return 0;

            float each = group.NutritionPerDay / group.Count;

            if (each <= 0f)
                return 0;

            return Mathf.Max(1, Mathf.CeilToInt(-reading.WorstMargin / each));
        }

        /// <summary>
        /// How many of this species the pasture would carry through the worst quadrum.
        ///
        /// The other side of <see cref="ShortBy"/>, for a group that is inside its means: the number the player
        /// could grow to. Both are approximate in the same honest way, since a pen shared with another species
        /// splits its grass between them and the split depends on who eats first.
        /// </summary>
        internal static int Carries(PastureReading reading, AnimalGroup group)
        {
            if (!reading.Available || group == null || group.Count == 0 || group.NutritionPerDay <= 0f)
                return -1;

            float each = group.NutritionPerDay / group.Count;

            if (each <= 0f)
                return -1;

            float grown = reading.PerQuadrum == null ? 0f : Lowest(reading.PerQuadrum);

            // Other species in the same pen are already eating, so what is left for this one is the growth minus
            // everybody else's consumption. Counting the whole pen's growth against this species alone would
            // promise room that a second herd has already taken.
            float others = Mathf.Max(0f, reading.ConsumptionPerDay - group.NutritionPerDay);

            return Mathf.Max(0, Mathf.FloorToInt((grown - others) / each));
        }

        private static float Lowest(float[] values)
        {
            float lowest = float.MaxValue;

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] < lowest)
                    lowest = values[i];
            }

            return lowest == float.MaxValue ? 0f : lowest;
        }

        /// <summary>
        /// The worst pasture reading anywhere in the colony, for the toolbar.
        ///
        /// <b>Distinct pens, taken from the animals rather than from a building scan.</b> A pen with nothing in it
        /// cannot be short of anything, so walking the groups finds every pen that could be a problem and none
        /// that cannot. It also means this costs a short list walk rather than a pass over every colonist
        /// building.
        /// </summary>
        internal static PastureReading Worst(List<AnimalSection> sections)
        {
            PastureReading worst = new PastureReading { WorstQuadrum = Quadrum.Undefined };

            if (sections == null)
                return worst;

            Seen.Clear();

            for (int s = 0; s < sections.Count; s++)
            {
                AnimalSection section = sections[s];

                if (section.Kind != AnimalKind.Colony)
                    continue;

                for (int g = 0; g < section.Groups.Count; g++)
                {
                    CompAnimalPenMarker pen = section.Groups[g].Pen;

                    if (pen == null || Seen.Contains(pen))
                        continue;

                    Seen.Add(pen);

                    PastureReading reading = ForPen(pen);

                    if (!reading.Available)
                        continue;

                    if (!worst.Available || reading.WorstMargin < worst.WorstMargin)
                        worst = reading;
                }
            }

            Seen.Clear();

            return worst;
        }
    }
}
