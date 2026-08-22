using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>What decides when a hunting bill stops ordering.</summary>
    internal enum HuntingBillMode
    {
        /// <summary>Order until the stockpiles hold the target amount, then stop.</summary>
        UntilStocked,

        /// <summary>
        /// Never stop.
        ///
        /// For keeping a species down rather than keeping a larder up: predators near the colony, beavers in the
        /// tree line, boomrats near the hull. What is in store has nothing to do with whether you want those gone,
        /// which is why the target count is not consulted at all in this mode. The same idea as a growing zone's
        /// forever bill.
        /// </summary>
        Forever,

        /// <summary>
        /// Hunt only what is over a headcount.
        ///
        /// Asked for on 2026-08-22, and it is the mode between the other two: forever clears a species out, this
        /// thins it. Six deer on the map are wildlife; twenty are eating the crops, and the twenty first is the one
        /// to shoot. The stockpiles are not consulted, so it keeps working when the larder is already full, and it
        /// stops on its own the moment the herd is back where you wanted it.
        /// </summary>
        MaxPopulation
    }

    /// <summary>
    /// A standing order to hunt: what to keep stocked or what to keep culled, and which wildlife may be taken.
    ///
    /// <b>Deliberately a bill and not a manager.</b> It has the parts a workbench bill has and they mean the same
    /// things: something to keep, a target to keep it at, a repeat mode and a suspend switch. What it does not
    /// have is any notion of scheduling work, choosing hunters or optimising a colony. It orders animals hunted,
    /// and stops.
    ///
    /// <b>What it counts is a thing filter, not a category.</b> Asked for on 2026-08-22. A larder is not one def:
    /// a colony keeps meat, and some colonies want bluefur specifically, and some want both meat and leather held
    /// above a line. A filter says all of that in the interface players already use for storage and bills, and it
    /// costs nothing to read back: the stock is the sum of what the filter allows.
    ///
    /// <b>The safeguards are the interesting part.</b> An order that hunts whatever is nearest will eventually
    /// shoot the last pair of deer on the map, or start a manhunter pack over eleven units of meat. So a bill
    /// carries a floor to leave behind per species, a switch for predators, a ceiling on how risky a species may
    /// be, and a cap on how many hunts it will have running at once. All four default to the cautious answer,
    /// because a standing order acts while nobody is watching.
    ///
    /// <b>What is already ordered counts towards the target.</b> Four designated deer are 672 meat that is going
    /// to arrive, so a bill that ignored them would keep designating until the map was empty and then be
    /// surprised by six hundred surplus meat. Everything in the stocked mode hinges on that one sum.
    /// </summary>
    internal sealed class HuntingBill : IExposable
    {
        /// <summary>What the player called it, or null to be named after what it counts.</summary>
        internal string label;

        internal HuntingBillMode mode = HuntingBillMode.UntilStocked;

        /// <summary>
        /// The items this bill keeps stocked.
        ///
        /// <b>A filter of our own rather than a reference to somebody else's.</b> It is saved with the bill and
        /// edited through the game's own filter window, which this mod has already replaced with a modern panel,
        /// so the control is the one players know from storage and from workbench bills.
        ///
        /// Ignored entirely in <see cref="HuntingBillMode.Forever"/>, where there is no stock to keep.
        /// </summary>
        internal ThingFilter filter = new ThingFilter();

        /// <summary>How much of what the filter allows to keep in the stockpiles.</summary>
        internal int targetCount = 300;

        /// <summary>
        /// The level this bill waits for before acting again, or negative to act as soon as it is short.
        ///
        /// The same idea as a workbench bill's unpause threshold, and it matters more here: without it a colony
        /// sitting one unit under its target sends a hunter out for one hare, forever.
        /// </summary>
        internal int resumeAt = -1;

        internal bool suspended;

        /// <summary>
        /// Which species may be taken. Empty means anything huntable.
        ///
        /// <b>Defs the player chose, not a filter.</b> A thing filter would have been the tidier looking choice
        /// and the wrong one: animals are pawns and are not in the item category tree the filter window draws, and
        /// the answer to "may we hunt elephants" has to survive there being no elephant on the map today.
        /// </summary>
        internal List<ThingDef> species = new List<ThingDef>();

        /// <summary>Whether predators may be taken. Off by default: a wounded warg comes looking for you.</summary>
        internal bool allowPredators;

        /// <summary>
        /// The most manhunter risk on damage this bill will accept, as a fraction.
        ///
        /// One in ten by default, which passes deer, hares and muffalo and stops at boomalopes, beavers and
        /// anything that reliably turns on the colony when shot.
        /// </summary>
        internal float maxManhunterChance = 0.1f;

        /// <summary>
        /// How many of each species to leave alive on the map.
        ///
        /// Two by default, which is a breeding pair and also the difference between hunting a herd and clearing
        /// one. A culling bill is the case for setting it to zero.
        ///
        /// <b>Not used in <see cref="HuntingBillMode.MaxPopulation"/>,</b> where the headcount is the floor: one
        /// number that is both the ceiling to hunt down to and the population to leave standing. Carrying two
        /// numbers there would let a player set a floor above their own ceiling and produce a bill that silently
        /// never acts.
        /// </summary>
        internal int keepAlive = 2;

        /// <summary>
        /// The most of each species to leave on the map, in <see cref="HuntingBillMode.MaxPopulation"/>.
        ///
        /// <b>Per species rather than across the bill.</b> A bill naming deer and hares with a limit of six means
        /// six of each: a shared pool would make hunting a hare depend on how many deer wandered in, which is not
        /// a rule anybody would predict from the interface.
        ///
        /// <b>It counts the wildlife on the map, not the colony's own.</b> Your tame deer are yours; this is about
        /// how many are outside eating the crops, which is also all this bill can act on.
        /// </summary>
        internal int maxPopulation = 6;

        /// <summary>How many hunts this bill will have outstanding at once.</summary>
        internal int maxOutstanding = 6;

        /// <summary>The last tick this bill designated anything, for the row's own account of itself.</summary>
        internal int lastActedTick = -1;

        /// <summary>How many animals it ordered last time it acted.</summary>
        internal int lastOrderedCount;

        /// <summary>
        /// The category a bill saved before the filter existed was counting.
        ///
        /// Read on load so a bill made in 1.6.4871.14155 keeps doing what it was made for instead of coming back
        /// empty. Written too, so a save round trip does not silently drop it while both versions are in play.
        /// </summary>
        private ThingCategoryDef legacyCategory;

        private ThingDef legacyThing;

        internal HuntingBill()
        {
        }

        /// <summary>A new bill counting raw meat, which is what most of these will ever be.</summary>
        internal static HuntingBill NewMeatBill()
        {
            HuntingBill made = new HuntingBill();

            made.filter.SetDisallowAll();
            made.filter.SetAllow(ThingCategoryDefOf.MeatRaw, true);
            made.filter.ResolveReferences();

            return made;
        }

        /// <summary>A new bill that keeps one species down, whatever is in store.</summary>
        internal static HuntingBill NewCullBill()
        {
            HuntingBill made = new HuntingBill
            {
                mode = HuntingBillMode.Forever,
                keepAlive = 0,
                allowPredators = true,
                maxManhunterChance = 1f
            };

            made.filter.SetDisallowAll();
            made.filter.ResolveReferences();

            return made;
        }

        internal string Label
        {
            get
            {
                if (!label.NullOrEmpty())
                    return label;

                if (Stocked)
                    return Counted;

                string what = species != null && species.Count == 1
                    ? species[0].LabelCap.ToString()
                    : "Wildlife";

                return mode == HuntingBillMode.MaxPopulation ? what + ", over " + maxPopulation : what;
            }
        }

        /// <summary>
        /// What the filter allows, as a phrase.
        ///
        /// One name when it is one thing, a count when it is several, because "meat, leather, bluefur and 4 more"
        /// is a row nobody can read at a glance.
        /// </summary>
        internal string Counted
        {
            get
            {
                if (filter == null)
                    return "nothing";

                ThingDef only = null;
                int count = 0;

                foreach (ThingDef def in filter.AllowedThingDefs)
                {
                    count++;

                    if (count == 1)
                        only = def;
                    else if (count > 8)
                        break;
                }

                if (count == 0)
                    return "nothing";

                if (count == 1)
                    return only.LabelCap;

                return count > 8 ? "many items" : count + " items";
            }
        }

        /// <summary>The stock level this bill starts working again at.</summary>
        internal int ResumeThreshold => resumeAt >= 0 ? Mathf.Min(resumeAt, targetCount) : targetCount;

        internal bool Forever => mode == HuntingBillMode.Forever;

        /// <summary>
        /// Whether this bill works towards a stock level.
        ///
        /// <b>Asked in place of "is it forever" wherever the stockpiles are involved,</b> which is the distinction
        /// that actually matters now there are three modes: two of the three ignore what is in store, and testing
        /// for one of them by name is how the third quietly inherits the wrong branch.
        /// </summary>
        internal bool Stocked => mode == HuntingBillMode.UntilStocked;

        /// <summary>
        /// What the colony currently holds of what this bill counts.
        ///
        /// <b>The resource counter, which is the same figure the resource readout shows.</b> That means stored,
        /// unrotted and unfogged: meat lying in a field where an animal died has not arrived yet, and telling the
        /// player they have it would be how a colony starves with a full larder on paper.
        /// </summary>
        internal int Stock(Map map)
        {
            ResourceCounter counter = map?.resourceCounter;

            if (counter == null || filter == null)
                return 0;

            int total = 0;

            foreach (ThingDef def in filter.AllowedThingDefs)
                total += counter.GetCount(def);

            return total;
        }

        /// <summary>
        /// Whether this bill counts what butchering the animal would produce, and how much of it.
        ///
        /// Meat and leather are both checked against the filter, so a bill wanting bluefur is served by hunting
        /// the species whose hide it is and not by a hare that yields something else.
        /// </summary>
        internal float Contribution(Pawn animal)
        {
            if (animal?.RaceProps == null || filter == null)
                return 0f;

            float total = 0f;

            ThingDef meat = animal.RaceProps.meatDef;

            if (meat != null && filter.Allows(meat))
                total += AnimalFacts.Meat(animal);

            ThingDef leather = animal.RaceProps.leatherDef;

            if (leather != null && filter.Allows(leather))
                total += AnimalFacts.Leather(animal);

            return total;
        }

        /// <summary>
        /// Whether this bill is allowed to take this species, ignoring how many are left.
        ///
        /// The species list, the predator switch and the manhunter ceiling, in that order. Each is a decision the
        /// player made, so none of them is overridden by the colony being hungry: a bill that quietly started
        /// hunting wargs because meat ran out would be one nobody could trust to leave running.
        ///
        /// <b>Yield only matters in the stocked mode.</b> A culling bill is not trying to fill a shelf, so
        /// refusing a species because its meat is not on the filter would defeat the entire mode.
        /// </summary>
        internal bool Allows(AnimalGroup group)
        {
            if (group == null || group.Kind != AnimalKind.Wild || group.Def == null || group.Members.Count == 0)
                return false;

            if (species != null && species.Count > 0 && !species.Contains(group.Def))
                return false;

            if (!allowPredators && group.Predator)
                return false;

            if (group.ManhunterOnDamage > maxManhunterChance + 0.0001f)
                return false;

            if (!Stocked)
                return true;

            // Nothing to gain: a species whose meat and hide this bill does not count would be shot for nothing.
            return Contribution(group.Members[0]) > 0f;
        }

        /// <summary>
        /// How many of this group the bill may still take, after the population it leaves standing.
        ///
        /// The two floors are the same idea reached from opposite ends: a stocked or forever bill leaves a
        /// breeding pair behind, and an over population bill leaves exactly the headcount the player asked for.
        /// </summary>
        internal int Takeable(AnimalGroup group)
        {
            if (!Allows(group))
                return 0;

            int leave = mode == HuntingBillMode.MaxPopulation
                ? Mathf.Max(0, maxPopulation)
                : Mathf.Max(0, keepAlive);

            return Mathf.Max(0, group.Count - leave);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref label, "label");
            Scribe_Values.Look(ref mode, "mode");
            Scribe_Deep.Look(ref filter, "filter");
            Scribe_Values.Look(ref targetCount, "targetCount", 300);
            Scribe_Values.Look(ref resumeAt, "resumeAt", -1);
            Scribe_Values.Look(ref suspended, "suspended");
            Scribe_Collections.Look(ref species, "species", LookMode.Def);
            Scribe_Values.Look(ref allowPredators, "allowPredators");
            Scribe_Values.Look(ref maxManhunterChance, "maxManhunterChance", 0.1f);
            Scribe_Values.Look(ref keepAlive, "keepAlive", 2);
            Scribe_Values.Look(ref maxPopulation, "maxPopulation", 6);
            Scribe_Values.Look(ref maxOutstanding, "maxOutstanding", 6);
            Scribe_Values.Look(ref lastActedTick, "lastActedTick", -1);
            Scribe_Values.Look(ref lastOrderedCount, "lastOrderedAmount");
            Scribe_Defs.Look(ref legacyCategory, "category");
            Scribe_Defs.Look(ref legacyThing, "thing");

            if (Scribe.mode != LoadSaveMode.PostLoadInit)
                return;

            // A list saved while empty comes back null, and a null species list would read as "no species chosen"
            // rather than "any species", which are opposite meanings here.
            if (species == null)
                species = new List<ThingDef>();
            else
                species.RemoveAll(def => def == null);

            if (filter == null)
                filter = new ThingFilter();

            // The filter's own references have to be resolved before it can be asked what it allows, and a filter
            // that came back from a save has not had that done.
            filter.ResolveReferences();

            Migrate();
        }

        /// <summary>
        /// Brings a bill saved before the filter existed forward.
        ///
        /// It counted one category or one def. Allowing that in the filter reproduces exactly what it did, and
        /// clearing the old fields afterwards means the migration happens once rather than fighting a later edit.
        /// </summary>
        private void Migrate()
        {
            if (legacyCategory == null && legacyThing == null)
                return;

            bool empty = true;

            foreach (ThingDef def in filter.AllowedThingDefs)
            {
                empty = false;

                break;
            }

            if (empty)
            {
                if (legacyCategory != null)
                    filter.SetAllow(legacyCategory, true);

                if (legacyThing != null)
                    filter.SetAllow(legacyThing, true);

                filter.ResolveReferences();
            }

            legacyCategory = null;
            legacyThing = null;
        }
    }
}
