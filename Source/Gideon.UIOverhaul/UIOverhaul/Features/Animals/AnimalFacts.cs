using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// What an animal produces, and when the next lot is ready.
    ///
    /// One shape for wool, milk and chemfuel, because vanilla models all three the same way: a
    /// <c>CompHasGatherableBodyResource</c> that fills from zero to one over a fixed number of days. Eggs are the
    /// odd one out and are carried as a rate instead, since a hen's clutch is not something the player waits for.
    /// </summary>
    internal struct AnimalProduce
    {
        /// <summary>What comes out, named by its own def. Null when this animal produces nothing.</summary>
        internal string ResourceLabel;

        /// <summary>How much per gathering.</summary>
        internal int Amount;

        /// <summary>Days until the next gathering, or zero when it is ready now.</summary>
        internal float DaysLeft;

        internal bool Ready;

        /// <summary>Long run output per day. The only figure an egg layer has.</summary>
        internal float PerDay;

        internal bool Any => !ResourceLabel.NullOrEmpty();
    }

    /// <summary>How far along a pregnancy is, in the terms the player thinks in.</summary>
    internal struct AnimalPregnancy
    {
        internal bool Pregnant;

        /// <summary>Zero to one.</summary>
        internal float Progress;

        /// <summary>Days left, or zero when the race declares no gestation period.</summary>
        internal float DaysLeft;
    }

    /// <summary>
    /// What taming this animal would actually take: the best handler the colony has, and their odds.
    ///
    /// <b>Both halves are needed for the answer to mean anything.</b> A chance of 34% is encouraging and useless
    /// if no colonist meets the animal's minimum handling skill, because then nobody will ever attempt it. The
    /// wildlife rows show the chance and the pane says who it belongs to.
    /// </summary>
    internal struct AnimalTameOdds
    {
        /// <summary>Zero to one, or negative when it could not be worked out.</summary>
        internal float Chance;

        internal Pawn Handler;

        /// <summary>The skill the animal demands, from vanilla's own curve.</summary>
        internal int MinSkill;

        /// <summary>The best handler's Animals skill.</summary>
        internal int HandlerSkill;

        internal bool Known => Chance >= 0f;

        internal bool AnyoneSkilledEnough => Handler != null && HandlerSkill >= MinSkill;
    }

    /// <summary>
    /// The per animal readings this tab is built on, each one read from the game rather than reasoned about.
    ///
    /// <b>Every figure here has a vanilla source, and that is the rule for adding to it.</b> Meat is
    /// <c>MeatAmount</c>, which is where the butcher table gets its number, so a row that says 336 is a promise
    /// the game keeps. Leather is the race's own leather def and <c>LeatherAmount</c>. Nutrition eaten is the
    /// food need's own fall rate. Wool, milk and chemfuel come off the gathering comps, and eggs off the egg
    /// layer's properties. Nothing in this file estimates anything, because a tab whose numbers are nearly right
    /// is worse than one that says nothing: the player plans a winter around them.
    ///
    /// <b>No caching here.</b> These are stat reads and field reads. What costs something is summarising a whole
    /// species, which is why the group totals live in <see cref="AnimalRoster"/> behind an interval, and the
    /// pasture arithmetic lives in <see cref="AnimalPasture"/> behind vanilla's own cache.
    ///
    /// <b>Nothing here is guarded either.</b> Every caller is inside a guarded panel or a guarded manager tick,
    /// and a guard per accessor would allocate a closure per cell per frame to catch what the panel already
    /// catches. The one exception is <see cref="TameOdds"/>, which reaches into a private curve.
    /// </summary>
    internal static class AnimalFacts
    {
        /// <summary>
        /// Meat from butchering this animal, as the butcher table would produce it.
        ///
        /// A juvenile is worth less than an adult of the same species because body size scales with life stage,
        /// and this reads the pawn rather than the def so that is reflected. It is also why a group total is a
        /// sum over members and not a multiplication.
        /// </summary>
        internal static float Meat(Pawn animal)
        {
            if (animal == null || !animal.RaceProps.IsFlesh)
                return 0f;

            return animal.GetStatValue(StatDefOf.MeatAmount);
        }

        internal static float Leather(Pawn animal)
        {
            if (animal?.RaceProps?.leatherDef == null)
                return 0f;

            return animal.GetStatValue(StatDefOf.LeatherAmount);
        }

        /// <summary>The leather this animal yields, named as the stockpile names it, or null.</summary>
        internal static string LeatherLabel(Pawn animal)
        {
            ThingDef leather = animal?.RaceProps?.leatherDef;

            return leather?.label;
        }

        /// <summary>
        /// Nutrition this animal eats per day while fed.
        ///
        /// <b>Assuming fed on purpose.</b> The need's live fall rate slows while an animal is starving, so
        /// reading it directly would tell the player that a starving herd is cheap to keep, which is the exact
        /// moment the figure matters most. Vanilla exposes the assumption as a parameter, so the honest reading
        /// is the fed one.
        /// </summary>
        internal static float NutritionPerDay(Pawn animal)
        {
            Need_Food food = animal?.needs?.food;

            if (food == null)
                return 0f;

            return food.FoodFallPerTickAssumingCategory(HungerCategory.Fed) * GenDate.TicksPerDay;
        }

        /// <summary>
        /// What this animal produces next, whether that is wool, milk, chemfuel or eggs.
        ///
        /// The gathering comps are asked first and share one path, since <c>Fullness</c> and the interval from
        /// their properties describe all of them. An egg layer answers with a rate instead: the clutch arrives on
        /// its own and is hauled by whoever passes, so "in 1.4 days" would be a countdown to nothing the player
        /// has to do.
        /// </summary>
        internal static AnimalProduce Produce(Pawn animal)
        {
            AnimalProduce produce = new AnimalProduce();

            if (animal == null)
                return produce;

            CompShearable shearable = animal.TryGetComp<CompShearable>();

            if (shearable?.Props?.woolDef != null && shearable.Props.shearIntervalDays > 0)
                return Gatherable(shearable.Fullness, shearable.Props.shearIntervalDays,
                    shearable.Props.woolAmount, shearable.Props.woolDef);

            CompMilkable milkable = animal.TryGetComp<CompMilkable>();

            if (milkable?.Props?.milkDef != null && milkable.Props.milkIntervalDays > 0)
                return Gatherable(milkable.Fullness, milkable.Props.milkIntervalDays,
                    milkable.Props.milkAmount, milkable.Props.milkDef);

            CompEggLayer layer = animal.TryGetComp<CompEggLayer>();

            if (layer?.Props != null)
            {
                CompProperties_EggLayer props = layer.Props;

                if (props.eggLayFemaleOnly && animal.gender != Gender.Female)
                    return produce;

                ThingDef egg = props.eggUnfertilizedDef ?? props.eggFertilizedDef;

                if (egg == null || props.eggLayIntervalDays <= 0f)
                    return produce;

                produce.ResourceLabel = egg.label;
                // Average of the clutch range, rounded, because a hen laying "1 to 2" eggs is one egg to a
                // player and the rate below is where the halves belong.
                produce.Amount = Mathf.Max(1, Mathf.RoundToInt(props.eggCountRange.Average));
                produce.PerDay = props.eggCountRange.Average / props.eggLayIntervalDays;
                produce.Ready = layer.CanLayNow;
            }

            return produce;
        }

        /// <summary>
        /// One shape for the three resources that grow on an animal over time.
        ///
        /// <c>Fullness</c> is the fraction grown, so the days left are what is left of the interval. The rate is
        /// carried too, because a species row showing several animals wants a per day figure rather than
        /// whichever member happens to be closest to ready.
        /// </summary>
        private static AnimalProduce Gatherable(float fullness, int intervalDays, int amount, ThingDef resource)
        {
            AnimalProduce produce = new AnimalProduce
            {
                ResourceLabel = resource.label,
                Amount = Mathf.Max(1, amount),
                Ready = fullness >= 1f
            };

            produce.DaysLeft = Mathf.Max(0f, (1f - Mathf.Clamp01(fullness)) * intervalDays);
            produce.PerDay = produce.Amount / (float) intervalDays;

            return produce;
        }

        /// <summary>How far along a pregnancy is, or a struct saying there is not one.</summary>
        internal static AnimalPregnancy Pregnancy(Pawn animal)
        {
            AnimalPregnancy pregnancy = new AnimalPregnancy();

            Hediff_Pregnant hediff = animal?.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Pregnant)
                as Hediff_Pregnant;

            if (hediff == null)
                return pregnancy;

            pregnancy.Pregnant = true;
            pregnancy.Progress = Mathf.Clamp01(hediff.GestationProgress);

            float period = animal.RaceProps.gestationPeriodDays;

            if (period > 0f)
                pregnancy.DaysLeft = Mathf.Max(0f, (1f - pregnancy.Progress) * period);

            return pregnancy;
        }

        /// <summary>
        /// Whether this animal is too young to breed, which is also vanilla's own definition of young.
        ///
        /// <c>CurLifeStage.reproductive</c> is the test the auto slaughter limits are counted by, so using
        /// anything else here would put an animal in the young column of one panel and the adult column of
        /// another.
        /// </summary>
        internal static bool Juvenile(Pawn animal)
        {
            LifeStageDef stage = animal?.ageTracker?.CurLifeStage;

            return stage != null && !stage.reproductive;
        }

        internal static float Wildness(Pawn animal)
        {
            if (animal == null)
                return 0f;

            return animal.GetStatValue(StatDefOf.Wildness);
        }

        internal static string Trainability(Pawn animal)
        {
            TrainabilityDef trainability = animal?.RaceProps?.trainability;

            return trainability == null ? null : trainability.LabelCap.ToString();
        }

        internal static bool Predator(Pawn animal)
        {
            return animal != null && animal.RaceProps.predator;
        }

        /// <summary>The chance this animal turns manhunter if it is shot at.</summary>
        internal static float ManhunterOnDamage(Pawn animal)
        {
            if (animal == null)
                return 0f;

            return PawnUtility.GetManhunterOnDamageChance(animal);
        }

        /// <summary>The chance this animal turns manhunter when a taming attempt fails.</summary>
        internal static float ManhunterOnTameFail(Pawn animal)
        {
            if (animal == null)
                return 0f;

            return PawnUtility.GetManhunterOnTameFailChance(animal);
        }

        /// <summary>The pen this animal is currently inside, or null. Unenclosed pens count, as the game does.</summary>
        internal static CompAnimalPenMarker Pen(Pawn animal)
        {
            if (animal == null || !animal.Spawned)
                return null;

            if (!AnimalPenUtility.NeedsToBeManagedByRope(animal))
                return null;

            return AnimalPenUtility.GetCurrentPenOf(animal, true);
        }

        /// <summary>
        /// The odds of taming this animal, and who would be doing it.
        ///
        /// <b>Vanilla's own arithmetic, reached through a private curve.</b> The chance a handler lands a taming
        /// attempt is their <c>TameAnimalChance</c> stat scaled by a curve over the animal's wildness, and that
        /// curve is a private static on <c>InteractionWorker_RecruitAttempt</c>. Reimplementing it would mean
        /// copying numbers that Ludeon can change in a patch, and the failure would be silent: a percentage that
        /// stays plausible while being wrong. Reading theirs means the figure moves when the game's does.
        ///
        /// <b>When the curve cannot be found, the chance is reported as unknown rather than guessed.</b> The
        /// wildlife row then shows wildness alone, which is a fact rather than a fabrication.
        ///
        /// The handler is chosen the way <c>TameUtility</c> chooses one for its warning message: colonists with
        /// Handling active, falling back to every colonist, then the highest Animals skill among those who can
        /// talk and manipulate. Bond and venerated animal multipliers are deliberately left out, since they
        /// apply to a specific pairing and this answers a question about the colony.
        /// </summary>
        internal static AnimalTameOdds TameOdds(Pawn animal)
        {
            AnimalTameOdds odds = new AnimalTameOdds { Chance = -1f };

            if (animal == null || animal.Map == null)
                return odds;

            odds.MinSkill = TrainableUtility.MinimumHandlingSkill(animal);
            odds.Handler = BestHandler(animal.Map);

            if (odds.Handler == null)
                return odds;

            odds.HandlerSkill = odds.Handler.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0;

            SimpleCurve curve = AnimalReflection.WildnessTameCurve;

            if (curve == null)
                return odds;

            float chance = odds.Handler.GetStatValue(StatDefOf.TameAnimalChance)
                           * curve.Evaluate(Wildness(animal));

            odds.Chance = Mathf.Clamp01(chance);

            return odds;
        }

        /// <summary>
        /// The colonist who would attempt a taming, by the same reading vanilla uses for its warnings.
        ///
        /// Handling has to be an active work type for somebody to actually do it, so those colonists are
        /// preferred; the fallback to everyone matches vanilla and keeps the readout from going blank on a
        /// colony that has switched Handling off, where the answer is still "this is who could".
        /// </summary>
        private static Pawn BestHandler(Map map)
        {
            if (map?.mapPawns == null)
                return null;

            Pawn best = null;
            int bestSkill = -1;
            bool anyHandler = false;

            for (int pass = 0; pass < 2; pass++)
            {
                foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
                {
                    if (colonist?.skills == null || colonist.health?.capacities == null)
                        continue;

                    bool handles = colonist.workSettings != null
                                   && colonist.workSettings.WorkIsActive(WorkTypeDefOf.Handling);

                    if (pass == 0)
                    {
                        if (!handles)
                            continue;

                        anyHandler = true;
                    }

                    if (!colonist.health.capacities.CapableOf(PawnCapacityDefOf.Talking)
                        || !colonist.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                        continue;

                    int level = colonist.skills.GetSkill(SkillDefOf.Animals)?.Level ?? 0;

                    if (level <= bestSkill)
                        continue;

                    bestSkill = level;
                    best = colonist;
                }

                // A second pass only when nobody is assigned to Handling at all. Finding no *capable* handler on
                // the first pass is a real answer, and repeating the walk would replace it with somebody who
                // will never be given the job.
                if (anyHandler || best != null)
                    break;
            }

            return best;
        }
    }
}
