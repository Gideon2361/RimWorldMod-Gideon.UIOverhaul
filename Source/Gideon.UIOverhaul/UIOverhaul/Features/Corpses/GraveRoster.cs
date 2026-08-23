using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Corpses
{
    /// <summary>
    /// One grave, read once per rebuild: who is in it, who it is being kept for, and who it will take.
    /// </summary>
    internal sealed class GraveRecord
    {
        internal Building_Grave Grave;

        internal Map Map;

        /// <summary>The body inside, or null when the grave is empty.</summary>
        internal Corpse Corpse;

        internal Pawn Occupant;

        /// <summary>The pawn this grave is being kept for, living or dead. Null when it is open to anyone.</summary>
        internal Pawn Reserved;

        internal bool Sarcophagus;

        internal string Label;

        /// <summary>Stuff and quality, the two things that separate one sarcophagus from another.</summary>
        internal string Material;

        /// <summary>The art plate's title, on a sarcophagus that has one.</summary>
        internal string Art;

        internal string Occupied;

        internal string Died;

        internal RotStage Stage;

        internal string RotNote;

        internal float DaysRotted;

        internal bool AcceptsColonists;

        internal bool AcceptsStrangers;

        internal bool AcceptsSlaves;

        internal bool AcceptsAnimals;

        /// <summary>Whether a colonist has been told to open this grave and take the body out.</summary>
        internal bool EmptyQueued;

        /// <summary>
        /// The body this empty grave would be given if the Fill button were pressed, or null.
        ///
        /// Worked out at rebuild rather than in the cell: the answer is a walk of every corpse on the map, and a
        /// yard of twenty empty graves would do that walk twenty times a frame.
        /// </summary>
        internal Corpse Waiting;

        internal bool Empty
        {
            get { return Corpse == null; }
        }
    }

    /// <summary>
    /// A place the colony buries people: one room's worth of graves, or everything left out of doors.
    ///
    /// <b>A room, because that is what the game already counts.</b> RimWorld has no notion of a graveyard, but it
    /// has a Tomb room role scored off how many sarcophagi are in it and an impressiveness that feeds the
    /// recreation a colonist gets from visiting. So the room is the mausoleum, whether or not anybody called it
    /// one, and grouping by it is the only grouping the game will agree with.
    /// </summary>
    internal sealed class BurialSite
    {
        internal Map Map;

        internal Room Room;

        internal string Label;

        internal bool Outdoors;

        internal float Impressiveness;

        /// <summary>The game's own adjective for that score: "mediocre", "impressive", and so on.</summary>
        internal string Quality;

        /// <summary>What a colonist's grave visiting recreation is multiplied by here. One outdoors.</summary>
        internal float JoyFactor;

        internal int Free;

        internal readonly List<GraveRecord> Graves = new List<GraveRecord>();

        internal int Total
        {
            get { return Graves.Count; }
        }
    }

    /// <summary>
    /// Every grave the colony owns, grouped into the places it buries people.
    ///
    /// <b>A grave is the only building in RimWorld you are expected to manage one at a time and given no list
    /// of.</b> Which are free, which hold a raider you forgot about, which sarcophagus is reserved for somebody
    /// still alive, and which of them will actually accept the body currently on the kitchen floor: all of it is
    /// one click per grave, on a map, with no way to see two of them at once. That is what this side of the tab
    /// is for.
    ///
    /// <b>What a grave accepts is the whole of the management.</b> A sarcophagus ships refusing strangers and a
    /// grave ships taking humanlikes only, so a colony that has never opened a grave's storage tab has a yard
    /// full of graves that will not take the dead muffalo or the dead raider and no indication anywhere of why.
    /// The four toggles on a row are that filter, and they are why the Bury button on the other view can afford
    /// to respect it.
    /// </summary>
    internal static class GraveRoster
    {
        private const int RebuildIntervalTicks = 60;

        private static readonly List<BurialSite> Built = new List<BurialSite>();

        private static int builtAt = -99999;

        private static bool dirty = true;

        /// <summary>Graves across every loaded map, whether or not anybody is in them.</summary>
        internal static int TotalGraves;

        internal static int FreeGraves;

        internal static List<BurialSite> Sites
        {
            get
            {
                int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;

                if (dirty || now - builtAt >= RebuildIntervalTicks || now < builtAt)
                {
                    builtAt = now;
                    dirty = false;

                    UIGuard.Try("Corpses.Graves", Rebuild,
                        "The graves list could not be rebuilt, so it may be out of date until it refreshes.");
                }

                return Built;
            }
        }

        internal static void Invalidate()
        {
            dirty = true;
        }

        // -------------------------------------------------------------------------------------------
        // Gathering
        // -------------------------------------------------------------------------------------------

        private static void Rebuild()
        {
            Built.Clear();

            TotalGraves = 0;
            FreeGraves = 0;

            List<Map> maps = Find.Maps;

            for (int i = 0; maps != null && i < maps.Count; i++)
                GatherMap(maps[i], maps.Count > 1);

            for (int i = 0; i < Built.Count; i++)
                Built[i].Graves.Sort(CompareGraves);

            Number();

            Built.Sort(Compare);
        }

        private static void GatherMap(Map map, bool named)
        {
            if (map == null || map.listerThings == null)
                return;

            List<Thing> graves = map.listerThings.ThingsInGroup(ThingRequestGroup.Grave);

            if (graves == null)
                return;

            Dictionary<int, BurialSite> byRoom = new Dictionary<int, BurialSite>();

            BurialSite outdoors = null;

            for (int i = 0; i < graves.Count; i++)
            {
                Building_Grave grave = graves[i] as Building_Grave;

                if (grave == null || grave.Faction == null || !grave.Faction.IsPlayer)
                    continue;

                GraveRecord record = Read(grave, map);

                TotalGraves++;

                if (record.Empty && record.Reserved == null)
                    FreeGraves++;

                Room room = UIGuard.Try<Room>("Corpses.GraveRoom", () => grave.GetRoom(), null, null);

                bool outside = room == null || room.Dereferenced || room.PsychologicallyOutdoors;

                BurialSite site;

                if (outside)
                {
                    if (outdoors == null)
                    {
                        outdoors = Site(map, null, named);

                        Built.Add(outdoors);
                    }

                    site = outdoors;
                }
                else if (!byRoom.TryGetValue(room.ID, out site))
                {
                    site = Site(map, room, named);

                    byRoom[room.ID] = site;

                    Built.Add(site);
                }

                site.Graves.Add(record);

                if (record.Empty && record.Reserved == null)
                    site.Free++;
            }
        }

        private static BurialSite Site(Map map, Room room, bool named)
        {
            BurialSite site = new BurialSite
            {
                Map = map,
                Room = room,
                Outdoors = room == null,
                JoyFactor = 1f
            };

            UIGuard.Try("Corpses.Site", () =>
            {
                site.Label = room == null
                    ? "Outside"
                    : (room.GetRoomRoleLabel() ?? "Room").CapitalizeFirst();

                if (room == null)
                    return;

                site.Impressiveness = room.GetStat(RoomStatDefOf.Impressiveness);
                site.JoyFactor = room.GetStat(RoomStatDefOf.GraveVisitingJoyGainFactor);

                RoomStatScoreStage stage = RoomStatDefOf.Impressiveness.GetScoreStage(site.Impressiveness);

                site.Quality = stage != null ? stage.label : null;
            }, null);

            if (site.Label.NullOrEmpty())
                site.Label = "Room";

            if (named)
                site.Label = MapLabels.NameOf(map) + " - " + site.Label;

            return site;
        }

        private static GraveRecord Read(Building_Grave grave, Map map)
        {
            GraveRecord record = new GraveRecord
            {
                Grave = grave,
                Map = map,
                Sarcophagus = grave is Building_Sarcophagus,
                Stage = RotStage.Fresh
            };

            UIGuard.Try("Corpses.ReadGrave", () =>
            {
                record.Label = grave.def != null ? grave.def.LabelCap.ToString() : "Grave";
                record.Material = Material(grave);
                record.Reserved = grave.AssignedPawn;

                CompArt art = grave.TryGetComp<CompArt>();

                if (art != null && art.Active)
                    record.Art = art.Title;

                record.EmptyQueued = grave.Spawned && map.designationManager != null
                                                   && map.designationManager.DesignationOn(grave,
                                                       DesignationDefOf.Open) != null;

                Corpse corpse = grave.Corpse;

                if (corpse != null && !corpse.Bugged)
                {
                    record.Corpse = corpse;
                    record.Occupant = corpse.InnerPawn;
                    record.Occupied = record.Occupant != null ? record.Occupant.LabelShortCap.ToString() : "?";
                    record.Died = "died " + CorpseFacts.AgeOf(corpse).ToStringTicksToPeriodVague();
                    record.Stage = CorpseFacts.StageOf(corpse);
                    record.RotNote = CorpseFacts.RotNote(corpse);
                    record.DaysRotted = CorpseFacts.DaysRotted(corpse);
                }

                Accepts(record, grave);

                if (record.Corpse == null && record.Reserved == null)
                    record.Waiting = GraveActions.Neediest(record);
            }, null);

            return record;
        }

        private static string Material(Building_Grave grave)
        {
            string stuff = grave.Stuff != null ? grave.Stuff.LabelAsStuff : null;

            QualityCategory quality;

            string made = grave.TryGetQuality(out quality) ? quality.GetLabel() : null;

            if (stuff.NullOrEmpty())
                return made;

            return made.NullOrEmpty() ? stuff : stuff + ", " + made;
        }

        /// <summary>
        /// Which kinds of body this grave's own filter will take.
        ///
        /// <b>The animal answer is a walk of the category rather than a single test,</b> because a filter is a
        /// set of allowed defs and there is no "is this whole category allowed" question to ask it. Doing that
        /// walk once per rebuild is why the answer is stored on the record instead of asked in the cell.
        /// </summary>
        private static void Accepts(GraveRecord record, Building_Grave grave)
        {
            StorageSettings settings = grave.GetStoreSettings();

            if (settings == null || settings.filter == null)
                return;

            ThingFilter filter = settings.filter;

            bool humanlike = GraveActions.AnyAllowed(filter, ThingCategoryDefOf.CorpsesHumanlike);

            record.AcceptsColonists = humanlike
                                      && GraveActions.Allows(filter, GraveActions.ColonistFilter);

            record.AcceptsStrangers = humanlike
                                      && GraveActions.Allows(filter, GraveActions.StrangerFilter);

            record.AcceptsSlaves = ModsConfig.IdeologyActive && humanlike
                                                             && GraveActions.Allows(filter,
                                                                 GraveActions.SlaveFilter);

            record.AcceptsAnimals = GraveActions.AnyAllowed(filter, ThingCategoryDefOf.CorpsesAnimal);
        }

        // -------------------------------------------------------------------------------------------
        // Ordering
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Numbers sites that came out with the same name, so two tombs are tellable apart.
        ///
        /// Ordered by where they sit on the map rather than by discovery order, so the numbers stay put between
        /// rebuilds instead of swapping every time a grave is built.
        /// </summary>
        private static void Number()
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();

            for (int i = 0; i < Built.Count; i++)
            {
                int seen;

                counts.TryGetValue(Built[i].Label, out seen);

                counts[Built[i].Label] = seen + 1;
            }

            Built.Sort(Position);

            Dictionary<string, int> given = new Dictionary<string, int>();

            for (int i = 0; i < Built.Count; i++)
            {
                BurialSite site = Built[i];

                if (counts[site.Label] < 2)
                    continue;

                int next;

                given.TryGetValue(site.Label, out next);

                given[site.Label] = next + 1;

                site.Label = site.Label + " " + (next + 1);
            }
        }

        private static int Position(BurialSite a, BurialSite b)
        {
            IntVec3 left = Corner(a);
            IntVec3 right = Corner(b);

            int byX = left.x.CompareTo(right.x);

            return byX != 0 ? byX : left.z.CompareTo(right.z);
        }

        private static IntVec3 Corner(BurialSite site)
        {
            return UIGuard.Try("Corpses.SiteCorner", () =>
            {
                if (site.Room != null && !site.Room.Dereferenced)
                    return site.Room.ExtentsClose.Min;

                return site.Graves.Count > 0 ? site.Graves[0].Grave.Position : IntVec3.Zero;
            }, IntVec3.Zero, null);
        }

        /// <summary>
        /// Sites in the order you would look at them: the best tomb first, the open ground last.
        ///
        /// Impressiveness is the sort because it is the one thing about a burial site that has a mechanical
        /// effect -- it multiplies the recreation a colonist gets from visiting a grave there -- and a colony
        /// that has built a good tomb wants to see it at the top rather than hunt for it.
        /// </summary>
        private static int Compare(BurialSite a, BurialSite b)
        {
            if (a.Outdoors != b.Outdoors)
                return a.Outdoors ? 1 : -1;

            int byQuality = b.Impressiveness.CompareTo(a.Impressiveness);

            return byQuality != 0
                ? byQuality
                : string.Compare(a.Label, b.Label, System.StringComparison.Ordinal);
        }

        /// <summary>Occupied graves first, then reserved, then free, and alphabetical inside each.</summary>
        private static int CompareGraves(GraveRecord a, GraveRecord b)
        {
            int rankA = a.Corpse != null ? 0 : a.Reserved != null ? 1 : 2;
            int rankB = b.Corpse != null ? 0 : b.Reserved != null ? 1 : 2;

            if (rankA != rankB)
                return rankA.CompareTo(rankB);

            return string.Compare(a.Occupied ?? a.Label, b.Occupied ?? b.Label,
                System.StringComparison.Ordinal);
        }
    }
}
