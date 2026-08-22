using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Pawns;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>Which side of the tab an animal is listed on.</summary>
    internal enum AnimalKind
    {
        /// <summary>Tame, ours, and something we make decisions about.</summary>
        Colony,

        /// <summary>Unowned wildlife standing on one of our maps.</summary>
        Wild
    }

    /// <summary>
    /// One species in one place, which is the row this tab is built around.
    ///
    /// <b>The group is the unit of decision and that is why it is the unit of data.</b> Nobody decides about the
    /// fourth hare; they decide about hares. Every figure here is therefore a whole group figure, summed over the
    /// members rather than multiplied out from the def, because a juvenile is not worth an adult's meat and a
    /// pregnant female is not available for slaughter.
    ///
    /// Members are kept in the order the rows should list them: by name for colony animals, whose names are how
    /// the player refers to them, and by distance for wildlife, where the nearest is the one that matters.
    /// </summary>
    internal sealed class AnimalGroup
    {
        internal ThingDef Def;
        internal AnimalKind Kind;

        /// <summary>The map these are on, or null for a group travelling in a caravan.</summary>
        internal Map Map;

        /// <summary>The caravan these are travelling with, or null.</summary>
        internal Caravan Caravan;

        internal readonly List<Pawn> Members = new List<Pawn>();

        internal int Females;
        internal int Males;
        internal int Young;

        /// <summary>Butchering the whole group, using each animal's own body size.</summary>
        internal float Meat;

        internal float Leather;

        /// <summary>What the leather is called, or null when the species yields none.</summary>
        internal string LeatherLabel;

        internal float NutritionPerDay;

        // Counts behind the State column. Each is a reason a player would look at this row today.
        internal int Pregnant;
        internal int Downed;
        internal int Starving;
        internal int NeedsTending;
        internal int InMentalBreak;
        internal int Manhunters;
        internal int Hunting;

        // Standing designations, so the row can show what is already ordered rather than only what could be.
        internal int HuntOrdered;
        internal int TameOrdered;
        internal int SlaughterOrdered;
        internal int ReleaseOrdered;

        /// <summary>How many are one training step from forgetting something.</summary>
        internal int TrainingAtRisk;

        /// <summary>Days until the soonest decay in the group, or negative when nothing is decaying.</summary>
        internal float SoonestDecayDays = -1f;

        internal int FullyTrained;

        /// <summary>What the group produces, and how soon. The soonest member's reading.</summary>
        internal AnimalProduce Produce;

        /// <summary>How many members have a full coat or udder waiting to be gathered.</summary>
        internal int ReadyToGather;

        /// <summary>Whole group output per day, for a species that produces continuously.</summary>
        internal float ProducePerDay;

        /// <summary>Distance from the colony to the closest member, in cells. Negative when not applicable.</summary>
        internal int NearestDistance = -1;

        internal bool Predator;
        internal float ManhunterOnDamage;
        internal float ManhunterOnTameFail;
        internal float Wildness;
        internal string Trainability;
        internal AnimalTameOdds TameOdds;

        /// <summary>The pen holding the group, or null. <see cref="PenMixed"/> says the group disagrees.</summary>
        internal CompAnimalPenMarker Pen;

        internal bool PenMixed;

        /// <summary>How many are outside any pen while needing one, which is a problem worth showing.</summary>
        internal int Unpenned;

        /// <summary>
        /// How many would need a pen but are held by an allowed area instead.
        ///
        /// Only ever above zero with the livestock area setting on, since that is what makes an area hold a roamer
        /// at all. Kept apart from <see cref="Unpenned"/> so the row can say which of the two is keeping them
        /// rather than reporting a pen problem that has been solved another way.
        /// </summary>
        internal int AreaHeld;

        /// <summary>The area every member is held to, or null. <see cref="AreaMixed"/> says they differ.</summary>
        internal Area Area;

        internal bool AreaMixed;

        /// <summary>The auto slaughter limits for this species on this map, or null off a map.</summary>
        internal AutoSlaughterConfig Limits;

        internal int Count => Members.Count;

        internal bool AnyTrouble => Downed > 0 || Starving > 0 || NeedsTending > 0 || InMentalBreak > 0
                                    || Manhunters > 0;

        /// <summary>Whether the species limit is being exceeded, which is what turns the Cap column amber.</summary>
        internal bool OverLimit => Limits != null && Limits.maxTotal >= 0 && Count > Limits.maxTotal;
    }

    /// <summary>
    /// One heading in the list: a map or a caravan, and one of the two kinds of animal on it.
    ///
    /// <b>Map and kind together, rather than a map heading with two sub headings inside it.</b> The grid this
    /// draws into has one level of section, and the level worth having is the one that folds: a wildlife list is
    /// forty rows the player wants out of the way, and a colony list is the one they came for. Splitting by map
    /// alone would have made those fold together.
    /// </summary>
    internal sealed class AnimalSection
    {
        internal string Label;
        internal Map Map;
        internal Caravan Caravan;
        internal AnimalKind Kind;

        internal readonly List<AnimalGroup> Groups = new List<AnimalGroup>();

        internal int Animals;
        internal float Meat;
    }

    /// <summary>
    /// Every animal the colony can see, gathered per place and per species, once per rebuild rather than per cell.
    ///
    /// <b>Vanilla's own two lists, in one place.</b> Colony animals come from <c>mapPawns.ColonyAnimals</c> and
    /// wildlife from the same predicate <c>MainTabWindow_Wildlife</c> uses, fog rule included. That matters more
    /// than it sounds: a player comparing our tab against the one they know has to see the same animals, and the
    /// fog rule in particular is a rule about what the colony is allowed to know. Lifting it would have turned
    /// this tab into a map wide census, which is a cheat rather than an improvement.
    ///
    /// <b>Every loaded map, and caravans too.</b> Both vanilla tabs read <c>Find.CurrentMap</c> alone, so a pack
    /// muffalo on a gravship site or a herd walking to a trade settlement is invisible until you go and look. All
    /// of it is loaded and readable, so all of it is listed, grouped by place the way the pawns tab is.
    ///
    /// <b>Rebuilt on the game's clock, not the frame's.</b> Summarising a species walks its members and asks each
    /// one for stats, pregnancies, training and designations, which is real work to repeat sixty times a second
    /// for nothing: none of it can change while the game is paused. So a rebuild happens at most twice a game
    /// second, and anything the player does that ought to show immediately calls <see cref="Invalidate"/>. That
    /// pairing is the whole cache policy, and it is the same one the mod's other caches use.
    /// </summary>
    internal static class AnimalRoster
    {
        /// <summary>Ticks between rebuilds. Thirty is half a second at normal speed and nothing while paused.</summary>
        private const int RebuildIntervalTicks = 30;

        private static readonly List<AnimalSection> Built = new List<AnimalSection>();

        /// <summary>Sections not currently in use, kept to avoid rebuilding the lists every half second.</summary>
        private static readonly List<AnimalSection> Spare = new List<AnimalSection>();

        private static readonly List<AnimalGroup> SpareGroups = new List<AnimalGroup>();

        private static readonly Dictionary<ThingDef, AnimalGroup> Buckets = new Dictionary<ThingDef, AnimalGroup>();

        private static int builtAt = -99999;
        private static bool dirty = true;
        private static bool subscribed;

        /// <summary>
        /// The current sections, rebuilt first if they are stale.
        ///
        /// The caller may hold this list for the length of a draw and no longer. A rebuild reuses the same section
        /// and group objects, so a reference kept across frames would quietly start describing a different
        /// species.
        /// </summary>
        internal static List<AnimalSection> Sections
        {
            get
            {
                Subscribe();

                int now = Find.TickManager?.TicksGame ?? 0;

                if (dirty || now - builtAt >= RebuildIntervalTicks || now < builtAt)
                {
                    builtAt = now;
                    dirty = false;

                    UIGuard.Try("Animals.Gather", Rebuild,
                        "The animals tab could not finish reading the colony's animals, so the list may be "
                        + "incomplete until it refreshes.");
                }

                return Built;
            }
        }

        /// <summary>Forces the next read to rebuild. Called after anything the player does through this tab.</summary>
        internal static void Invalidate()
        {
            dirty = true;
        }

        /// <summary>
        /// Watches for the two events that make a held list wrong rather than stale.
        ///
        /// A destroyed pawn must not survive in a group even for the half second the interval allows, and a
        /// roster change is how an animal being tamed, bought, born or lost arrives. Subscribed once, lazily,
        /// because this type may never be touched in a session where the tab is never opened.
        /// </summary>
        private static void Subscribe()
        {
            if (subscribed)
                return;

            subscribed = true;

            UIGuard.Try("Animals.Subscribe", () =>
            {
                PawnLifecycle.Gone += Forget;
                PawnLifecycle.RosterChanged += Invalidate;
            }, "The animals tab will not notice animals arriving or being destroyed until it is reopened.");
        }

        private static void Forget(Pawn pawn)
        {
            dirty = true;
        }

        // -------------------------------------------------------------------------------------------
        // Gathering
        // -------------------------------------------------------------------------------------------

        private static void Rebuild()
        {
            Recycle();

            List<Map> maps = Find.Maps;

            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    Map map = maps[i];

                    if (map == null)
                        continue;

                    string place = MapLabels.NameOf(map);

                    GatherColony(map, place);
                    GatherWild(map, place);
                }
            }

            GatherCaravans();
        }

        /// <summary>
        /// Returns the previous sections and groups to the spare pools.
        ///
        /// Reuse rather than fresh allocation because this runs twice a game second for as long as the tab is
        /// open, and a colony with two hundred animals would otherwise churn a few hundred lists per second
        /// through the garbage collector for no reason at all.
        /// </summary>
        private static void Recycle()
        {
            for (int i = 0; i < Built.Count; i++)
            {
                AnimalSection section = Built[i];

                for (int g = 0; g < section.Groups.Count; g++)
                {
                    AnimalGroup group = section.Groups[g];

                    group.Members.Clear();
                    SpareGroups.Add(group);
                }

                section.Groups.Clear();
                Spare.Add(section);
            }

            Built.Clear();
        }

        private static AnimalSection TakeSection(string label, Map map, Caravan caravan, AnimalKind kind)
        {
            AnimalSection section;

            if (Spare.Count > 0)
            {
                section = Spare[Spare.Count - 1];
                Spare.RemoveAt(Spare.Count - 1);
            }
            else
            {
                section = new AnimalSection();
            }

            section.Label = label;
            section.Map = map;
            section.Caravan = caravan;
            section.Kind = kind;
            section.Animals = 0;
            section.Meat = 0f;

            return section;
        }

        private static AnimalGroup TakeGroup(ThingDef def, AnimalKind kind, Map map, Caravan caravan)
        {
            AnimalGroup group;

            if (SpareGroups.Count > 0)
            {
                group = SpareGroups[SpareGroups.Count - 1];
                SpareGroups.RemoveAt(SpareGroups.Count - 1);
            }
            else
            {
                group = new AnimalGroup();
            }

            group.Def = def;
            group.Kind = kind;
            group.Map = map;
            group.Caravan = caravan;

            group.Females = 0;
            group.Males = 0;
            group.Young = 0;
            group.Meat = 0f;
            group.Leather = 0f;
            group.LeatherLabel = null;
            group.NutritionPerDay = 0f;
            group.Pregnant = 0;
            group.Downed = 0;
            group.Starving = 0;
            group.NeedsTending = 0;
            group.InMentalBreak = 0;
            group.Manhunters = 0;
            group.Hunting = 0;
            group.HuntOrdered = 0;
            group.TameOrdered = 0;
            group.SlaughterOrdered = 0;
            group.ReleaseOrdered = 0;
            group.TrainingAtRisk = 0;
            group.SoonestDecayDays = -1f;
            group.FullyTrained = 0;
            group.Produce = new AnimalProduce();
            group.ReadyToGather = 0;
            group.ProducePerDay = 0f;
            group.NearestDistance = -1;
            group.Predator = false;
            group.ManhunterOnDamage = 0f;
            group.ManhunterOnTameFail = 0f;
            group.Wildness = 0f;
            group.Trainability = null;
            group.TameOdds = new AnimalTameOdds { Chance = -1f };
            group.Pen = null;
            group.PenMixed = false;
            group.Unpenned = 0;
            group.AreaHeld = 0;
            group.Area = null;
            group.AreaMixed = false;
            group.Limits = null;

            return group;
        }

        private static void GatherColony(Map map, string place)
        {
            List<Pawn> animals = map.mapPawns?.ColonyAnimals;

            if (animals == null || animals.Count == 0)
                return;

            Buckets.Clear();

            for (int i = 0; i < animals.Count; i++)
            {
                Pawn animal = animals[i];

                if (!Listable(animal))
                    continue;

                Bucket(animal, AnimalKind.Colony, map, null).Members.Add(animal);
            }

            Flush(place, map, null, AnimalKind.Colony);
        }

        /// <summary>
        /// Wildlife, by the same predicate vanilla's own tab uses.
        ///
        /// Insects are included because they carry the insect faction and vanilla lists them, which is not an
        /// oversight on their part: a hive's worth of megaspiders is exactly the sort of thing a player wants
        /// counted. Prisoners in cells and fogged cells are excluded for the same reason vanilla excludes them.
        /// </summary>
        private static void GatherWild(Map map, string place)
        {
            List<Pawn> all = map.mapPawns?.AllPawns;

            if (all == null || all.Count == 0)
                return;

            Buckets.Clear();

            for (int i = 0; i < all.Count; i++)
            {
                Pawn animal = all[i];

                if (!Listable(animal) || !animal.Spawned)
                    continue;

                if (animal.Faction != null && animal.Faction != Faction.OfInsects)
                    continue;

                if (!animal.AnimalOrWildMan() || animal.IsPrisonerInPrisonCell())
                    continue;

                if (animal.Position.Fogged(map))
                    continue;

                Bucket(animal, AnimalKind.Wild, map, null).Members.Add(animal);
            }

            Flush(place, map, null, AnimalKind.Wild);
        }

        /// <summary>
        /// Animals travelling with a player caravan.
        ///
        /// A caravan is a place in its own right rather than part of the map it left, because that is how the
        /// player thinks about it: those animals are away, and the question about them is whether they are
        /// carrying too much or bleeding, not which pen they belong to.
        /// </summary>
        private static void GatherCaravans()
        {
            List<Caravan> caravans = Find.WorldObjects?.Caravans;

            if (caravans == null)
                return;

            for (int i = 0; i < caravans.Count; i++)
            {
                Caravan caravan = caravans[i];

                if (caravan == null || !caravan.IsPlayerControlled)
                    continue;

                List<Pawn> pawns = caravan.PawnsListForReading;

                if (pawns == null || pawns.Count == 0)
                    continue;

                Buckets.Clear();

                for (int p = 0; p < pawns.Count; p++)
                {
                    Pawn animal = pawns[p];

                    if (!Listable(animal) || animal.Faction != Faction.OfPlayer)
                        continue;

                    Bucket(animal, AnimalKind.Colony, null, caravan).Members.Add(animal);
                }

                Flush(caravan.LabelCap, null, caravan, AnimalKind.Colony);
            }
        }

        /// <summary>An animal at all, and one that still exists.</summary>
        private static bool Listable(Pawn animal)
        {
            return animal != null && !animal.Destroyed && animal.RaceProps != null && animal.RaceProps.Animal;
        }

        private static AnimalGroup Bucket(Pawn animal, AnimalKind kind, Map map, Caravan caravan)
        {
            AnimalGroup group;

            if (Buckets.TryGetValue(animal.def, out group))
                return group;

            group = TakeGroup(animal.def, kind, map, caravan);
            Buckets.Add(animal.def, group);

            return group;
        }

        /// <summary>
        /// Turns the buckets into a section, summarising each group and ordering both levels.
        ///
        /// Nothing is added when the place has none of that kind, so a map with no wildlife in sight has no
        /// wildlife heading rather than an empty one.
        /// </summary>
        private static void Flush(string place, Map map, Caravan caravan, AnimalKind kind)
        {
            if (Buckets.Count == 0)
                return;

            AnimalSection section = TakeSection(place, map, caravan, kind);

            foreach (KeyValuePair<ThingDef, AnimalGroup> pair in Buckets)
            {
                AnimalGroup group = pair.Value;

                if (group.Members.Count == 0)
                {
                    SpareGroups.Add(group);

                    continue;
                }

                Summarise(group);

                section.Groups.Add(group);
                section.Animals += group.Count;
                section.Meat += group.Meat;
            }

            Buckets.Clear();

            if (section.Groups.Count == 0)
            {
                Spare.Add(section);

                return;
            }

            Order(section);
            Built.Add(section);
        }

        /// <summary>
        /// Group order within a section, and member order within a group.
        ///
        /// <b>Colony animals by name, wildlife by what is likely to kill you.</b> A colony list is something the
        /// player reads down looking for a name they know, so alphabetical is the only order that helps. A
        /// wildlife list is a threat and opportunity assessment: a predator at the door outranks a herd of deer
        /// however much meat is on them, and after that the biggest return comes first. Vanilla sorts wildlife by
        /// body size, which gets the second half of that right and has nothing to say about the first.
        /// </summary>
        private static void Order(AnimalSection section)
        {
            if (section.Kind == AnimalKind.Colony)
            {
                section.Groups.SortBy(g => g.Def.label);

                for (int i = 0; i < section.Groups.Count; i++)
                    section.Groups[i].Members.SortBy(p => p.LabelShortCap.ToString());

                return;
            }

            section.Groups.Sort(CompareWild);

            for (int i = 0; i < section.Groups.Count; i++)
            {
                AnimalGroup group = section.Groups[i];

                if (group.Map != null)
                    group.Members.SortBy(p => DistanceFrom(group.Map, p));
            }
        }

        private static int CompareWild(AnimalGroup a, AnimalGroup b)
        {
            int aThreat = a.Manhunters > 0 ? 2 : a.Predator ? 1 : 0;
            int bThreat = b.Manhunters > 0 ? 2 : b.Predator ? 1 : 0;

            if (aThreat != bThreat)
                return bThreat - aThreat;

            if (!Mathf.Approximately(a.Meat, b.Meat))
                return a.Meat > b.Meat ? -1 : 1;

            return string.Compare(a.Def.label, b.Def.label, System.StringComparison.OrdinalIgnoreCase);
        }

        // -------------------------------------------------------------------------------------------
        // Summarising
        // -------------------------------------------------------------------------------------------

        private static void Summarise(AnimalGroup group)
        {
            Pawn first = group.Members[0];

            group.Predator = AnimalFacts.Predator(first);
            group.ManhunterOnDamage = AnimalFacts.ManhunterOnDamage(first);
            group.ManhunterOnTameFail = AnimalFacts.ManhunterOnTameFail(first);
            group.Wildness = AnimalFacts.Wildness(first);
            group.Trainability = AnimalFacts.Trainability(first);
            group.LeatherLabel = AnimalFacts.LeatherLabel(first);

            if (group.Kind == AnimalKind.Wild)
                group.TameOdds = AnimalFacts.TameOdds(first);

            if (group.Map != null && group.Kind == AnimalKind.Colony)
                group.Limits = LimitsFor(group.Map, group.Def);

            bool firstPen = true;
            bool firstArea = true;

            for (int i = 0; i < group.Members.Count; i++)
            {
                Pawn animal = group.Members[i];

                Tally(group, animal);

                if (group.Kind == AnimalKind.Wild)
                {
                    if (group.Map != null)
                    {
                        int distance = DistanceFrom(group.Map, animal);

                        if (group.NearestDistance < 0 || distance < group.NearestDistance)
                            group.NearestDistance = distance;
                    }

                    continue;
                }

                CompAnimalPenMarker pen = AnimalFacts.Pen(animal);

                if (pen == null && AnimalPenUtility.NeedsToBeManagedByRope(animal))
                {
                    // An area answers the same question as a pen once the setting is on, so an animal held by one
                    // is not counted as unpenned: the tab would otherwise warn that a pen is needed next to the
                    // area that is keeping the animal perfectly well. Counted separately as well, so the cell can
                    // say which of the two is doing the work.
                    if (LivestockRoaming.HeldByArea(animal))
                        group.AreaHeld++;
                    else
                        group.Unpenned++;
                }

                if (firstPen)
                {
                    group.Pen = pen;
                    firstPen = false;
                }
                else if (group.Pen != pen)
                {
                    group.PenMixed = true;
                }

                Area area = animal.playerSettings?.AreaRestrictionInPawnCurrentMap;

                if (firstArea)
                {
                    group.Area = area;
                    firstArea = false;
                }
                else if (group.Area != area)
                {
                    group.AreaMixed = true;
                }
            }
        }

        /// <summary>
        /// Everything read off one animal that adds into its group.
        ///
        /// Split out from the walk above so the two orders of business stay legible: this is per animal, that is
        /// per group.
        /// </summary>
        private static void Tally(AnimalGroup group, Pawn animal)
        {
            group.Meat += AnimalFacts.Meat(animal);
            group.Leather += AnimalFacts.Leather(animal);
            group.NutritionPerDay += AnimalFacts.NutritionPerDay(animal);

            if (AnimalFacts.Juvenile(animal))
                group.Young++;
            else if (animal.gender == Gender.Female)
                group.Females++;
            else if (animal.gender == Gender.Male)
                group.Males++;

            if (animal.Downed)
                group.Downed++;

            if (animal.needs?.food != null && animal.needs.food.Starving)
                group.Starving++;

            if (animal.InMentalState)
            {
                if (animal.InAggroMentalState)
                    group.Manhunters++;
                else
                    group.InMentalBreak++;
            }

            if (animal.CurJobDef == JobDefOf.PredatorHunt)
                group.Hunting++;

            if (HealthAIUtility.ShouldBeTendedNowByPlayer(animal))
                group.NeedsTending++;

            AnimalPregnancy pregnancy = AnimalFacts.Pregnancy(animal);

            if (pregnancy.Pregnant)
                group.Pregnant++;

            AnimalDesignations.Tally(group, animal);

            if (group.Kind == AnimalKind.Wild)
                return;

            AnimalProduce produce = AnimalFacts.Produce(animal);

            if (produce.Any)
            {
                group.ProducePerDay += produce.PerDay;

                if (produce.Ready)
                    group.ReadyToGather++;

                // The soonest member's reading stands for the group, since that is the one that decides when
                // somebody has to walk out there. An unset group takes the first reading it sees.
                if (!group.Produce.Any || (!produce.Ready && produce.DaysLeft < group.Produce.DaysLeft)
                    || (produce.Ready && !group.Produce.Ready))
                    group.Produce = produce;
            }

            AnimalTrainingState training = AnimalTraining.Of(animal);

            if (training.Learned > 0)
                group.FullyTrained++;

            if (!training.Decaying)
                return;

            if (training.AnythingAtRisk)
                group.TrainingAtRisk++;

            if (group.SoonestDecayDays < 0f || training.DecayDaysLeft < group.SoonestDecayDays)
                group.SoonestDecayDays = training.DecayDaysLeft;
        }

        /// <summary>The auto slaughter limits vanilla holds for this species on this map, or null.</summary>
        internal static AutoSlaughterConfig LimitsFor(Map map, ThingDef def)
        {
            List<AutoSlaughterConfig> configs = map?.autoSlaughterManager?.configs;

            if (configs == null || def == null)
                return null;

            for (int i = 0; i < configs.Count; i++)
            {
                if (configs[i] != null && configs[i].animal == def)
                    return configs[i];
            }

            return null;
        }

        // -------------------------------------------------------------------------------------------
        // Distance
        // -------------------------------------------------------------------------------------------

        private static Map centreOf;
        private static IntVec3 centre;
        private static int centreAt = -99999;

        /// <summary>
        /// How far an animal is from the colony, in cells.
        ///
        /// <b>Measured from the home area, not from the map's middle or from a colonist.</b> The home area is the
        /// player's own statement of where the colony is, so it stays right for a base built in a corner, and it
        /// does not move about the way a wandering colonist does, which would make a distance column flicker.
        /// Averaging the cells is done once per map per rebuild and cached for the frames in between.
        /// </summary>
        private static int DistanceFrom(Map map, Pawn animal)
        {
            if (map == null || animal == null || !animal.Spawned)
                return -1;

            return Mathf.RoundToInt(animal.Position.DistanceTo(ColonyCentre(map)));
        }

        internal static IntVec3 ColonyCentre(Map map)
        {
            int now = Find.TickManager?.TicksGame ?? 0;

            if (centreOf == map && now - centreAt < RebuildIntervalTicks && now >= centreAt)
                return centre;

            centreOf = map;
            centreAt = now;
            centre = map.Center;

            Area home = map.areaManager?.Home;

            if (home == null || home.TrueCount == 0)
                return centre;

            long x = 0;
            long z = 0;
            int count = 0;

            foreach (IntVec3 cell in home.ActiveCells)
            {
                x += cell.x;
                z += cell.z;
                count++;
            }

            if (count > 0)
                centre = new IntVec3((int) (x / count), 0, (int) (z / count));

            return centre;
        }
    }
}
