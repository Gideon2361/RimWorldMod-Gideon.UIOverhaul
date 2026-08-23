using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Corpses
{
    /// <summary>
    /// Which section of the tab a body belongs in, which is the same question as what you may do with it.
    ///
    /// <b>The order of this enum is the order of the screen.</b> Our own dead first, because they are the only
    /// ones that cost mood every hour they are left; then the guests we are answerable for; then the pile of gear
    /// the raid left; then animals, which are a butchering job with a clock on it; then mechanoids, which are
    /// scrap.
    /// </summary>
    internal enum CorpseKind
    {
        Ours,

        Guests,

        Hostiles,

        Animals,

        Mechanoids
    }

    /// <summary>One skill worth reading off a dead person, with the passion that made it worth growing.</summary>
    internal struct CorpseSkill
    {
        internal string Label;

        internal int Level;

        internal Passion Passion;
    }

    /// <summary>
    /// Everything the tab reads off a corpse, and the two clocks nothing else in the game shows.
    ///
    /// <b>Rot is the only clock in RimWorld that runs on something you own and is never displayed.</b> The
    /// inspect string says "fresh" or "rotting" and stops; it never says how long until the meat is worthless,
    /// how long until the gear starts taking damage, or how many days of decay a resurrection would be paying
    /// for. All three are the same number, read off <see cref="CompRottable"/>, and it is computed here once per
    /// rebuild rather than in a cell that redraws sixty times a second.
    ///
    /// <b>The second clock is the mood one, and it is the harsher of the two.</b> A colonist corpse that has been
    /// lying spawned for 90,000 ticks gives <c>ColonistLeftUnburied</c> to the whole colony at -10, and nothing on
    /// screen counts down to it. Vanilla's own alert is the authority on which bodies qualify, so this asks it
    /// rather than reimplementing the four exclusions it applies.
    /// </summary>
    internal static class CorpseFacts
    {
        /// <summary>
        /// Ticks a colonist's body may lie in the open before the whole colony takes the mood hit.
        ///
        /// Vanilla's own figure, from <c>ThoughtWorker_ColonistLeftUnburied</c>: a day and a half. Written here
        /// because the tab counts down to it, and a countdown to a number we invented would be a lie.
        /// </summary>
        internal const int UnburiedGraceTicks = 90000;

        /// <summary>Below this a corpse's rot rate is nil and it is being kept rather than decaying.</summary>
        private const float FrozenRate = 0.001f;

        // ---------------------------------------------------------------------------------------
        // Who
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Which section this body belongs in.
        ///
        /// Mechanoids before animals, because a mechanoid is not humanlike and would otherwise land among the
        /// muffalo; and the faction test after both, because a tamed mechanoid still belongs with the scrap.
        /// </summary>
        internal static CorpseKind KindOf(Pawn pawn)
        {
            return UIGuard.Try("Corpses.Kind", () =>
            {
                if (pawn == null || pawn.RaceProps == null)
                    return CorpseKind.Guests;

                if (pawn.RaceProps.IsMechanoid)
                    return CorpseKind.Mechanoids;

                if (!pawn.RaceProps.Humanlike)
                    return CorpseKind.Animals;

                if (pawn.Faction != null && pawn.Faction.IsPlayer)
                    return CorpseKind.Ours;

                if (pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer))
                    return CorpseKind.Hostiles;

                return CorpseKind.Guests;
            }, CorpseKind.Guests, null);
        }

        /// <summary>The section's own name for what its bodies are.</summary>
        internal static string LabelOf(CorpseKind kind)
        {
            switch (kind)
            {
                case CorpseKind.Ours: return "Ours";
                case CorpseKind.Guests: return "Guests and allies";
                case CorpseKind.Hostiles: return "Hostiles";
                case CorpseKind.Animals: return "Animals";
                default: return "Mechanoids";
            }
        }

        /// <summary>
        /// The line under a name: what they were, how old, and how long ago they died.
        ///
        /// <b>Time since death rather than a date,</b> because every decision on this screen is against a clock
        /// and nobody converts a quadrum and a day into "two days ago" in their head.
        /// </summary>
        internal static string Subline(Corpse corpse, Pawn pawn, CorpseKind kind)
        {
            return UIGuard.Try<string>("Corpses.Subline", () =>
            {
                string died = "died " + AgeOf(corpse).ToStringTicksToPeriodVague();

                if (kind == CorpseKind.Animals)
                {
                    string owner = pawn.Faction != null && pawn.Faction.IsPlayer ? "Ours" : "Wild";

                    return pawn.Name != null ? owner + " - " + pawn.Name.ToStringShort + " - " + died : owner + " - " + died;
                }

                if (kind == CorpseKind.Mechanoids)
                    return died;

                string who = kind == CorpseKind.Ours
                    ? pawn.story != null ? pawn.story.TitleShortCap.ToString() : null
                    : pawn.Faction != null ? pawn.Faction.Name : null;

                string age = pawn.ageTracker != null ? pawn.ageTracker.AgeBiologicalYears.ToString() : null;

                string line = who.NullOrEmpty() ? string.Empty : who + " - ";

                if (!age.NullOrEmpty())
                    line += age + " - ";

                return line + died;
            }, null, null);
        }

        /// <summary>Ticks since death, floored at zero so a clock skew cannot print a negative age.</summary>
        internal static int AgeOf(Corpse corpse)
        {
            return UIGuard.Try("Corpses.Age", () => Mathf.Max(0, corpse.Age), 0, null);
        }

        // ---------------------------------------------------------------------------------------
        // Rot
        // ---------------------------------------------------------------------------------------

        /// <summary>The stage, defaulting to fresh for anything that does not rot at all.</summary>
        internal static RotStage StageOf(Corpse corpse)
        {
            return UIGuard.Try("Corpses.Stage", () =>
            {
                CompRottable rot = corpse.TryGetComp<CompRottable>();

                return rot == null ? RotStage.Fresh : rot.Stage;
            }, RotStage.Fresh, null);
        }

        /// <summary>
        /// How far through the current stage the body is, nought to one.
        ///
        /// Within the stage rather than across the whole of decay, because that is what the number is being read
        /// for: a body 90 percent of the way through Fresh is about to stop being butcherable, and one 90 percent
        /// of the way through Rotting is about to stop taking damage. One scale across both would flatten the
        /// distinction that matters.
        /// </summary>
        internal static float ProgressOf(Corpse corpse)
        {
            return UIGuard.Try("Corpses.Progress", () =>
            {
                CompRottable rot = corpse.TryGetComp<CompRottable>();

                if (rot == null)
                    return 0f;

                float start = rot.PropsRot.TicksToRotStart;
                float dry = rot.PropsRot.TicksToDessicated;

                if (rot.Stage == RotStage.Fresh)
                    return start <= 0f ? 0f : Mathf.Clamp01(rot.RotProgress / start);

                if (rot.Stage == RotStage.Rotting)
                    return dry <= start ? 1f : Mathf.Clamp01((rot.RotProgress - start) / (dry - start));

                return 1f;
            }, 0f, null);
        }

        /// <summary>Days of decay on the clock, which is the figure a resurrection's side effects scale off.</summary>
        internal static float DaysRotted(Corpse corpse)
        {
            return UIGuard.Try("Corpses.DaysRotted", () =>
            {
                CompRottable rot = corpse.TryGetComp<CompRottable>();

                return rot == null ? 0f : rot.RotProgress / GenDate.TicksPerDay;
            }, 0f, null);
        }

        /// <summary>Whether the body is cold enough that its clock has effectively stopped.</summary>
        internal static bool Frozen(Corpse corpse)
        {
            return UIGuard.Try("Corpses.Frozen", () =>
            {
                CompRottable rot = corpse.TryGetComp<CompRottable>();

                if (rot == null || !rot.Active)
                    return true;

                return GenTemperature.RotRateAtTemperature(Mathf.RoundToInt(corpse.AmbientTemperature))
                       < FrozenRate;
            }, false, null);
        }

        /// <summary>The word on the pill.</summary>
        internal static string StageTag(RotStage stage)
        {
            switch (stage)
            {
                case RotStage.Fresh: return "FRESH";
                case RotStage.Rotting: return "ROTTING";
                default: return "DESICCATED";
            }
        }

        internal static Color StageColor(RotStage stage, UIColorPaletteDef palette)
        {
            switch (stage)
            {
                case RotStage.Fresh: return palette.Success;
                case RotStage.Rotting: return palette.Warning;
                default: return palette.TextDisabled;
            }
        }

        /// <summary>
        /// What happens next to this body and when, at the temperature it is actually lying at.
        ///
        /// <b>The time to the next stage rather than to the end of decay.</b> Fresh to rotting is when the meat
        /// stops being worth taking; rotting to desiccated is when the corpse stops damaging itself and settles.
        /// Both are decisions; the total is not.
        /// </summary>
        internal static string RotNote(Corpse corpse)
        {
            return UIGuard.Try<string>("Corpses.RotNote", () =>
            {
                CompRottable rot = corpse.TryGetComp<CompRottable>();

                if (rot == null || !rot.Active)
                    return "Does not rot";

                if (rot.Stage == RotStage.Dessicated)
                    return "No longer rotting";

                float rate = GenTemperature.RotRateAtTemperature(Mathf.RoundToInt(corpse.AmbientTemperature));

                if (rate < FrozenRate)
                    return rot.Stage == RotStage.Fresh ? "Frozen, staying fresh" : "Frozen where it is";

                if (rot.Stage == RotStage.Fresh)
                    return "Rots in " + rot.TicksUntilRotAtCurrentTemp.ToStringTicksToPeriod();

                // No vanilla helper for this leg: TicksUntilRotAtCurrentTemp only ever counts down to the start
                // of rotting and returns zero once past it. The arithmetic is the same as the one it does.
                float left = rot.PropsRot.TicksToDessicated - rot.RotProgress;

                if (left <= 0f)
                    return "No longer rotting";

                return "Desiccates in " + Mathf.RoundToInt(left / rate).ToStringTicksToPeriod();
            }, null, null);
        }

        // ---------------------------------------------------------------------------------------
        // What was lost
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The skills worth reading, best first.
        ///
        /// <b>Not sentiment.</b> In a colony that can resurrect, this column is the whole reason to open the tab:
        /// fourteen Medicine with a burning passion is worth a serum and a raider with Shooting 5 is not. Disabled
        /// skills are left out because a zero the pawn could never have raised says nothing about them.
        /// </summary>
        internal static void Skills(Pawn pawn, List<CorpseSkill> into, int count)
        {
            into.Clear();

            UIGuard.Try("Corpses.Skills", () =>
            {
                if (pawn.skills == null || pawn.skills.skills == null)
                    return;

                List<SkillRecord> records = pawn.skills.skills;

                for (int i = 0; i < records.Count; i++)
                {
                    SkillRecord record = records[i];

                    if (record == null || record.TotallyDisabled || record.Level <= 0)
                        continue;

                    into.Add(new CorpseSkill
                    {
                        Label = record.def != null ? record.def.skillLabel.CapitalizeFirst() : "?",
                        Level = record.Level,
                        Passion = record.passion
                    });
                }

                into.Sort((a, b) => b.Level.CompareTo(a.Level));

                if (into.Count > count)
                    into.RemoveRange(count, into.Count - count);
            }, null);
        }

        /// <summary>The highest skill this pawn had, or zero. Decides whether a body is worth its own row.</summary>
        internal static int TopSkill(Pawn pawn)
        {
            return UIGuard.Try("Corpses.TopSkill", () =>
            {
                if (pawn.skills == null || pawn.skills.skills == null)
                    return 0;

                int best = 0;

                for (int i = 0; i < pawn.skills.skills.Count; i++)
                {
                    SkillRecord record = pawn.skills.skills[i];

                    if (record != null && !record.TotallyDisabled && record.Level > best)
                        best = record.Level;
                }

                return best;
            }, 0, null);
        }

        internal static void Traits(Pawn pawn, List<string> into)
        {
            into.Clear();

            UIGuard.Try("Corpses.Traits", () =>
            {
                if (pawn.story == null || pawn.story.traits == null)
                    return;

                List<Trait> traits = pawn.story.traits.allTraits;

                if (traits == null)
                    return;

                for (int i = 0; i < traits.Count; i++)
                {
                    Trait trait = traits[i];

                    if (trait == null || trait.Suppressed)
                        continue;

                    into.Add(trait.LabelCap);
                }
            }, null);
        }

        // ---------------------------------------------------------------------------------------
        // Gear
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// How many things are still on the body and what they are worth.
        ///
        /// <b>Money already spent.</b> A raid nobody stripped is the commonest way a colony leaves two thousand
        /// silver lying in the mud until the weather takes it, and no screen in the game adds it up.
        /// </summary>
        internal static void Gear(Pawn pawn, out int count, out int value)
        {
            int items = 0;
            float worth = 0f;

            UIGuard.Try("Corpses.Gear", () =>
            {
                if (pawn.apparel != null)
                {
                    List<Apparel> worn = pawn.apparel.WornApparel;

                    for (int i = 0; worn != null && i < worn.Count; i++)
                        Count(worn[i], ref items, ref worth);
                }

                if (pawn.equipment != null)
                {
                    List<ThingWithComps> held = pawn.equipment.AllEquipmentListForReading;

                    for (int i = 0; held != null && i < held.Count; i++)
                        Count(held[i], ref items, ref worth);
                }

                if (pawn.inventory == null || pawn.inventory.innerContainer == null)
                    return;

                ThingOwner<Thing> carried = pawn.inventory.innerContainer;

                for (int i = 0; i < carried.Count; i++)
                    Count(carried[i], ref items, ref worth);
            }, null);

            count = items;
            value = Mathf.RoundToInt(worth);
        }

        private static void Count(Thing thing, ref int items, ref float worth)
        {
            if (thing == null)
                return;

            items += thing.stackCount > 0 ? thing.stackCount : 1;
            worth += thing.MarketValue * Mathf.Max(1, thing.stackCount);
        }

        // ---------------------------------------------------------------------------------------
        // Yield
        // ---------------------------------------------------------------------------------------

        /// <summary>How much meat a butcher would get, or zero when this body yields none.</summary>
        internal static int Meat(Pawn pawn)
        {
            return UIGuard.Try("Corpses.Meat", () =>
            {
                if (pawn.RaceProps == null || pawn.RaceProps.meatDef == null)
                    return 0;

                return Mathf.RoundToInt(pawn.GetStatValue(StatDefOf.MeatAmount));
            }, 0, null);
        }

        /// <summary>The leather this body yields, and what it is called. Zero and null when there is none.</summary>
        internal static int Leather(Pawn pawn, out string label)
        {
            string found = null;

            int amount = UIGuard.Try("Corpses.Leather", () =>
            {
                if (pawn.RaceProps == null || pawn.RaceProps.leatherDef == null)
                    return 0;

                found = pawn.RaceProps.leatherDef.LabelCap;

                return Mathf.RoundToInt(pawn.GetStatValue(StatDefOf.LeatherAmount));
            }, 0, null);

            label = found;

            return amount;
        }

        // ---------------------------------------------------------------------------------------
        // Where
        // ---------------------------------------------------------------------------------------

        /// <summary>The grave this body is in, or null when it is not in one.</summary>
        internal static Building_Grave GraveOf(Corpse corpse)
        {
            return UIGuard.Try("Corpses.Grave", () => corpse.ParentHolder as Building_Grave, null, null);
        }

        /// <summary>
        /// Where the body is, in the two lines the column has.
        ///
        /// <b>"On the floor" is a finding, not a location.</b> It is the state that costs mood, blocks a
        /// resurrection from being tidy and leaves gear in the weather, so it gets said plainly rather than
        /// hidden behind the name of whichever room the body happens to be lying in.
        /// </summary>
        internal static void Where(Corpse corpse, out string where, out string note)
        {
            string place = "Unknown";
            string detail = null;

            UIGuard.Try("Corpses.Where", () =>
            {
                Building_Grave grave = corpse.ParentHolder as Building_Grave;

                if (grave != null)
                {
                    place = grave.def != null ? grave.def.LabelCap.ToString() : "Grave";
                    detail = RoomLabel(grave.GetRoom());

                    return;
                }

                Thing holder = corpse.ParentHolder as Thing;

                if (holder != null)
                {
                    place = holder.LabelShortCap;
                    detail = RoomLabel(holder.GetRoom());

                    return;
                }

                if (!corpse.Spawned)
                {
                    place = "Carried";

                    return;
                }

                place = corpse.IsInAnyStorage() ? "In storage" : "On the floor";
                detail = RoomLabel(corpse.GetRoom());
            }, null);

            where = place;
            note = detail;
        }

        /// <summary>A room's own name for itself, or the plain truth about being outdoors.</summary>
        internal static string RoomLabel(Room room)
        {
            return UIGuard.Try<string>("Corpses.Room", () =>
            {
                if (room == null || room.Dereferenced)
                    return null;

                if (room.PsychologicallyOutdoors)
                    return "Outside";

                string label = room.GetRoomRoleLabel();

                return label.NullOrEmpty() ? null : label.CapitalizeFirst();
            }, null, null);
        }

        // ---------------------------------------------------------------------------------------
        // The mood clock
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Whether this is one of the bodies vanilla counts for the unburied thought.
        ///
        /// <b>Asked of the game rather than reimplemented.</b> <c>Alert_ColonistLeftUnburied.IsCorpseOfColonist</c>
        /// is public and applies four exclusions -- quest lodgers, slaves, subhumans and shamblers -- that a
        /// second copy of this test would drift away from the first time any of them moved.
        /// </summary>
        internal static bool CountsAsUnburied(Corpse corpse)
        {
            return UIGuard.Try("Corpses.Unburied", () => Alert_ColonistLeftUnburied.IsCorpseOfColonist(corpse),
                false, null);
        }

        /// <summary>
        /// Ticks until this body starts costing the colony mood, negative once it already is.
        ///
        /// <see cref="int.MaxValue"/> when it never will, which is every body vanilla does not count.
        /// </summary>
        internal static int UnburiedIn(Corpse corpse)
        {
            if (!CountsAsUnburied(corpse))
                return int.MaxValue;

            return UnburiedGraceTicks - AgeOf(corpse);
        }
    }
}
