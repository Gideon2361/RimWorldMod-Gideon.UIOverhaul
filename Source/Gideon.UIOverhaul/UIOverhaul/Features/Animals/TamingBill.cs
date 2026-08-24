using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// How many of one species a taming bill wants, by sex.
    ///
    /// <b>Two numbers rather than one, on Aaron's instruction of 2026-08-23.</b> A total headcount cannot express
    /// the thing people actually want from taming, which is a breeding pair: "six muffalo" is satisfied by six
    /// males, and a bill that satisfies itself that way has done nothing useful. Asking for males and females
    /// separately is the smallest shape that can say "two of each".
    /// </summary>
    internal class TamingTarget : IExposable
    {
        internal ThingDef species;

        internal int males;

        internal int females;

        internal TamingTarget()
        {
        }

        internal TamingTarget(ThingDef species, int males, int females)
        {
            this.species = species;
            this.males = males;
            this.females = females;
        }

        internal int Wanted(Gender gender)
        {
            return gender == Gender.Female ? females : males;
        }

        internal void Set(Gender gender, int count)
        {
            count = Mathf.Clamp(count, 0, Ceiling);

            if (gender == Gender.Female)
                females = count;
            else
                males = count;
        }

        /// <summary>
        /// The most either number may be.
        ///
        /// High enough for a herd and low enough that a mistyped number cannot order the taming of every animal
        /// on the map. The pen and the food are the real limits, and neither is this bill's business.
        /// </summary>
        internal const int Ceiling = 50;

        internal bool Empty
        {
            get { return males <= 0 && females <= 0; }
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref males, "males");
            Scribe_Values.Look(ref females, "females");
        }
    }

    /// <summary>
    /// A standing instruction to tame animals until the colony has the ones it asked for.
    ///
    /// <b>Its own type rather than a mode on the hunting bill,</b> which is what Aaron chose on 2026-08-23 when
    /// asked. The two read alike and share almost no data: a hunting bill counts a stock of items through a thing
    /// filter, and this counts living animals by species and sex. Folding them together would have meant a class
    /// where half the fields are dead in either mode, and two save formats pretending to be one.
    ///
    /// <b>What it counts is tame animals of the player's faction on the map, including juveniles.</b> A calf is a
    /// tame muffalo that is going to grow, so counting it is what stops a bill taming a replacement for an animal
    /// it already has. The dialog says so, because it is the one rule here somebody could reasonably expect to go
    /// the other way.
    ///
    /// <b>A minimum chance and an assignable tamer, both asked for.</b> The chance is the guard: below it the
    /// bill leaves an animal alone rather than spending a handler's day on a coin flip that ends in a manhunter.
    /// The tamer is who the bill plans around -- their stat and their skill decide that chance -- which is the
    /// honest meaning of assigning one here. It does not reserve the job for them: which colonist actually walks
    /// out is decided by RimWorld's work priorities, and taking that over would mean owning the handling work
    /// giver.
    /// </summary>
    internal class TamingBill : IExposable
    {
        internal string label;

        internal List<TamingTarget> targets = new List<TamingTarget>();

        /// <summary>
        /// The lowest taming chance worth ordering, zero to one.
        ///
        /// Five per cent by default, which refuses almost nothing: the point of the default is that the guard
        /// exists and is visible, not that this mod has an opinion about which animals are worth taming.
        /// </summary>
        internal float minTameChance = 0.05f;

        /// <summary>Who the bill plans around. Null means the best handler the colony has at the time.</summary>
        internal Pawn tamer;

        internal bool suspended;

        /// <summary>
        /// How many taming orders this bill may have outstanding at once.
        ///
        /// The same safeguard the hunting bills carry and for the same reason: a colony short of eight animals
        /// should not send every handler in a different direction at once.
        /// </summary>
        internal int maxOutstanding = 6;

        internal int lastActedTick = -1;

        internal int lastOrderedCount;

        internal string Label
        {
            get
            {
                if (!label.NullOrEmpty())
                    return label;

                if (targets == null || targets.Count == 0)
                    return "Taming";

                if (targets.Count == 1 && targets[0].species != null)
                    return "Tame " + targets[0].species.label;

                return "Tame " + targets.Count + " species";
            }
        }

        /// <summary>Whether this bill has anything it could act on at all.</summary>
        internal bool Idle
        {
            get
            {
                if (targets == null)
                    return true;

                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i] != null && targets[i].species != null && !targets[i].Empty)
                        return false;
                }

                return true;
            }
        }

        internal TamingTarget TargetFor(ThingDef species)
        {
            if (species == null || targets == null)
                return null;

            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null && targets[i].species == species)
                    return targets[i];
            }

            return null;
        }

        /// <summary>Adds a species with one of each, which is what somebody adding a species almost always means.</summary>
        internal void Add(ThingDef species)
        {
            if (species == null || TargetFor(species) != null)
                return;

            if (targets == null)
                targets = new List<TamingTarget>();

            targets.Add(new TamingTarget(species, 1, 1));
        }

        internal void Remove(ThingDef species)
        {
            TamingTarget found = TargetFor(species);

            if (found != null)
                targets.Remove(found);
        }

        /// <summary>
        /// How many tame animals of this species and sex the colony has on this map.
        ///
        /// <b>Counted from the map's own colony animal list</b> rather than from a roster of ours, so a bill and
        /// the animals tab can never disagree about the herd. Juveniles are counted; see the class note.
        /// </summary>
        internal static int Held(Map map, ThingDef species, Gender gender)
        {
            return UIGuard.Try("Animals.TameHeld", () =>
            {
                if (map == null || species == null)
                    return 0;

                List<Pawn> animals = map.mapPawns.SpawnedColonyAnimals;

                if (animals == null)
                    return 0;

                int held = 0;

                for (int i = 0; i < animals.Count; i++)
                {
                    Pawn animal = animals[i];

                    if (animal != null && !animal.Dead && animal.def == species && animal.gender == gender)
                        held++;
                }

                return held;
            }, 0, null);
        }

        /// <summary>A new bill with nothing chosen yet, for the add button.</summary>
        internal static TamingBill NewBill()
        {
            return new TamingBill();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref label, "label");
            Scribe_Collections.Look(ref targets, "targets", LookMode.Deep);
            Scribe_Values.Look(ref minTameChance, "minTameChance", 0.05f);
            Scribe_References.Look(ref tamer, "tamer");
            Scribe_Values.Look(ref suspended, "suspended");
            Scribe_Values.Look(ref maxOutstanding, "maxOutstanding", 6);
            Scribe_Values.Look(ref lastActedTick, "lastActedTick", -1);
            Scribe_Values.Look(ref lastOrderedCount, "lastOrderedCount");

            // A list scribed deep comes back null when it was empty, and every reader here would then have to
            // check. Rebuilt on load instead, which is the same thing the hunting bill does with its species.
            if (Scribe.mode == LoadSaveMode.PostLoadInit && targets == null)
                targets = new List<TamingTarget>();
        }
    }
}
