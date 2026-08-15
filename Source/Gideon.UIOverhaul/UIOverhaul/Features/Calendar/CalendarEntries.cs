using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Notifications;
using Gideon.UIOverhaul.Features.Options;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Calendar
{
    /// <summary>What a calendar entry stands for, which decides how it is drawn and how certain it is.</summary>
    internal enum CalendarEntryKind
    {
        /// <summary>Something that happened, taken from the archive. Certain, and clickable.</summary>
        Happened,

        /// <summary>A colonist's birthday. Certain.</summary>
        Birthday,

        /// <summary>A quest offer running out. Certain.</summary>
        QuestExpiry,

        /// <summary>An active game condition ending. Certain.</summary>
        ConditionEnd,

        /// <summary>An ideoligion ritual falling due, or an obligation for one running out. Certain.</summary>
        Ritual,

        /// <summary>Something the storyteller has scheduled but not yet chosen. See <see cref="CalendarForecast"/>.</summary>
        Forecast
    }

    /// <summary>One thing on one day.</summary>
    internal sealed class CalendarEntry
    {
        public int Tick;
        public CalendarEntryKind Kind;
        public string Label;
        public string Tooltip;
        public Texture2D Icon;
        public Color Tint;

        /// <summary>Set for <see cref="CalendarEntryKind.Happened"/>, so the row can reopen the original letter.</summary>
        public IArchivable Archived;

        public ForecastKind Forecast;
    }

    /// <summary>
    /// Everything the calendar knows about a span of days.
    ///
    /// <b>The past and the future are gathered from different places, and are not equally full.</b> Behind today
    /// sits <c>Find.Archive</c>, which holds every letter and message the colony ever raised with the tick it
    /// happened on -- raids, births, deaths, parties, finished research. Ahead of today there is only what has a
    /// real fire tick attached: birthdays, quest offers running out, conditions lifting, and the storyteller marks
    /// worked out by <see cref="CalendarForecast"/>.
    ///
    /// That asymmetry is honest rather than a gap. RimWorld does not decide most of what is coming until it
    /// arrives, so a calendar that filled its right-hand days as densely as its left would be making things up.
    /// The left is a record; the right is the short list of things that are genuinely already true.
    /// </summary>
    internal static class CalendarEntries
    {
        /// <summary>
        /// A monotonic day number, so days can be compared across a year boundary.
        ///
        /// Year times sixty plus day of year, rather than dividing ticks by a day: the local day boundary moves
        /// with longitude, and two colonies on opposite sides of the planet do not turn over at the same moment.
        /// Vanilla's own readouts take longitude for the same reason.
        /// </summary>
        internal static int DayIndex(long absTicks, float longitude)
        {
            return GenDate.Year(absTicks, longitude) * GenDate.DaysPerYear
                   + GenDate.DayOfYear(absTicks, longitude);
        }

        /// <summary>Converts a game tick, which is what most of the game hands out, to the absolute one GenDate wants.</summary>
        internal static long ToAbsolute(int gameTick)
        {
            return gameTick + (GenTicks.TicksAbs - Find.TickManager.TicksGame);
        }

        /// <summary>
        /// Every entry falling between <paramref name="firstDay"/> and <paramref name="lastDay"/> inclusive,
        /// bucketed by day index.
        ///
        /// Each source is gathered under its own guard. A mod that breaks one of them -- a quest type that throws
        /// on its own expiry, an archivable with no label -- costs that row rather than the whole calendar.
        /// </summary>
        internal static Dictionary<int, List<CalendarEntry>> Gather(Map map, int firstDay, int lastDay,
            int daysAhead)
        {
            Dictionary<int, List<CalendarEntry>> byDay = new Dictionary<int, List<CalendarEntry>>();

            if (map == null)
                return byDay;

            float longitude = Find.WorldGrid.LongLatOf(map.Tile).x;
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            UIGuard.Try("Calendar.Archive",
                () => GatherArchive(byDay, longitude, firstDay, lastDay, palette),
                "The calendar shows no past events.");

            UIGuard.Try("Calendar.Birthdays",
                () => GatherBirthdays(byDay, map, longitude, firstDay, lastDay, palette),
                "The calendar shows no birthdays.");

            UIGuard.Try("Calendar.Quests",
                () => GatherQuests(byDay, longitude, firstDay, lastDay, palette),
                "The calendar shows no quest deadlines.");

            UIGuard.Try("Calendar.Conditions",
                () => GatherConditions(byDay, map, longitude, firstDay, lastDay, palette),
                "The calendar shows no condition end times.");

            UIGuard.Try("Calendar.Rituals",
                () => GatherRituals(byDay, longitude, firstDay, lastDay, palette),
                "The calendar shows no ideoligion rituals.");

            UIGuard.Try("Calendar.ForecastEntries",
                () => GatherForecast(byDay, longitude, firstDay, lastDay, daysAhead, palette),
                "The calendar shows no storyteller marks.");

            return byDay;
        }

        private static void Add(Dictionary<int, List<CalendarEntry>> byDay, int day, CalendarEntry entry)
        {
            if (!byDay.TryGetValue(day, out List<CalendarEntry> list))
            {
                list = new List<CalendarEntry>();
                byDay[day] = list;
            }

            list.Add(entry);
        }

        /// <summary>
        /// What happened, from the archive.
        ///
        /// <c>IArchivable</c> already carries a label, a tooltip, an icon and a way to reopen itself, so a past
        /// day costs almost nothing to fill and its entries stay clickable -- the same letter opens as it would
        /// from the message history.
        /// </summary>
        private static void GatherArchive(Dictionary<int, List<CalendarEntry>> byDay, float longitude,
            int firstDay, int lastDay, UIColorPaletteDef palette)
        {
            List<IArchivable> archivables = Find.Archive?.ArchivablesListForReading;

            if (archivables == null)
                return;

            for (int i = 0; i < archivables.Count; i++)
            {
                IArchivable archivable = archivables[i];

                if (archivable == null)
                    continue;

                int day = DayIndex(ToAbsolute(archivable.CreatedTicksGame), longitude);

                if (day < firstDay || day > lastDay)
                    continue;

                Add(byDay, day, new CalendarEntry
                {
                    Tick = archivable.CreatedTicksGame,
                    Kind = CalendarEntryKind.Happened,
                    Label = archivable.ArchivedLabel,
                    Tooltip = archivable.ArchivedTooltip,
                    Icon = archivable.ArchivedIcon as Texture2D,
                    Tint = archivable.ArchivedIconColor,
                    Archived = archivable
                });
            }
        }

        /// <summary>
        /// Colonist birthdays, which recur on the same day of the year.
        ///
        /// Matched by day of year rather than by a tick, since a birthday is a date rather than a moment. The
        /// window can straddle a year boundary, so every day in it is tested rather than the pawn's day being
        /// projected forward.
        /// </summary>
        private static void GatherBirthdays(Dictionary<int, List<CalendarEntry>> byDay, Map map, float longitude,
            int firstDay, int lastDay, UIColorPaletteDef palette)
        {
            List<Pawn> colonists = map.mapPawns?.FreeColonists;

            if (colonists == null)
                return;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];

                if (pawn?.ageTracker == null)
                    continue;

                int birthDay = pawn.ageTracker.BirthDayOfYear;

                for (int day = firstDay; day <= lastDay; day++)
                {
                    if (day % GenDate.DaysPerYear != birthDay)
                        continue;

                    int age = pawn.ageTracker.AgeBiologicalYears + (day > CurrentDay(longitude) ? 1 : 0);

                    Add(byDay, day, new CalendarEntry
                    {
                        Kind = CalendarEntryKind.Birthday,
                        Label = pawn.LabelShortCap,
                        Tooltip = pawn.LabelShortCap + " turns " + age + ".",
                        Icon = NotificationIcons.Bell,
                        Tint = palette.Mood
                    });
                }
            }
        }

        private static int CurrentDay(float longitude)
        {
            return DayIndex(GenTicks.TicksAbs, longitude);
        }

        /// <summary>Quest offers running out, which is the deadline a player most often misses.</summary>
        private static void GatherQuests(Dictionary<int, List<CalendarEntry>> byDay, float longitude,
            int firstDay, int lastDay, UIColorPaletteDef palette)
        {
            List<Quest> quests = Find.QuestManager?.QuestsListForReading;

            if (quests == null)
                return;

            for (int i = 0; i < quests.Count; i++)
            {
                Quest quest = quests[i];

                if (quest == null || quest.hidden || quest.State != QuestState.NotYetAccepted)
                    continue;

                int ticksLeft = quest.TicksUntilExpiry;

                if (ticksLeft <= 0)
                    continue;

                int tick = Find.TickManager.TicksGame + ticksLeft;
                int day = DayIndex(ToAbsolute(tick), longitude);

                if (day < firstDay || day > lastDay)
                    continue;

                Add(byDay, day, new CalendarEntry
                {
                    Tick = tick,
                    Kind = CalendarEntryKind.QuestExpiry,
                    Label = quest.name,
                    Tooltip = "Quest offer expires: " + quest.name,
                    Icon = NotificationIcons.Envelope,
                    Tint = palette.Warning
                });
            }
        }

        /// <summary>When the weather stops being someone else's idea: eclipses, fallout, flares lifting.</summary>
        private static void GatherConditions(Dictionary<int, List<CalendarEntry>> byDay, Map map, float longitude,
            int firstDay, int lastDay, UIColorPaletteDef palette)
        {
            List<GameCondition> conditions = map.gameConditionManager?.ActiveConditions;

            if (conditions == null)
                return;

            for (int i = 0; i < conditions.Count; i++)
            {
                GameCondition condition = conditions[i];

                if (condition == null || condition.Permanent)
                    continue;

                int tick = Find.TickManager.TicksGame + condition.TicksLeft;
                int day = DayIndex(ToAbsolute(tick), longitude);

                if (day < firstDay || day > lastDay)
                    continue;

                Add(byDay, day, new CalendarEntry
                {
                    Tick = tick,
                    Kind = CalendarEntryKind.ConditionEnd,
                    Label = condition.LabelCap + " ends",
                    Tooltip = condition.TooltipString,
                    Icon = NotificationIcons.Hazard,
                    Tint = palette.TextSecondary
                });
            }
        }

        /// <summary>
        /// Ideoligion rituals: the annual ones falling due, and obligations already running out.
        ///
        /// <b>Both are certain, unlike the storyteller marks beside them.</b> A dated ritual carries
        /// <c>triggerDaysSinceStartOfYear</c> -- a day of the year fixed when the ideoligion was generated and
        /// saved with it -- so a festival on day 34 is on day 34 every year until the ideo changes. An active
        /// obligation carries a real expiry tick. Neither is a forecast, and neither is dimmed like one.
        ///
        /// <b>The expiring half is the one worth having.</b> A dated ritual announces itself with a letter; an
        /// obligation quietly counts down and costs the colony mood when it lapses, and nothing in the game puts
        /// that deadline on a timeline. Matched by day of year like a birthday, since a ritual date recurs rather
        /// than being a moment.
        /// </summary>
        private static void GatherRituals(Dictionary<int, List<CalendarEntry>> byDay, float longitude,
            int firstDay, int lastDay, UIColorPaletteDef palette)
        {
            if (!ModsConfig.IdeologyActive)
                return;

            IEnumerable<Ideo> ideos = Faction.OfPlayer?.ideos?.AllIdeos;

            if (ideos == null)
                return;

            foreach (Ideo ideo in ideos)
            {
                List<Precept> precepts = ideo?.PreceptsListForReading;

                if (precepts == null)
                    continue;

                for (int p = 0; p < precepts.Count; p++)
                {
                    if (!(precepts[p] is Precept_Ritual ritual))
                        continue;

                    GatherRitualDates(byDay, ritual, longitude, firstDay, lastDay, palette);
                    GatherRitualObligations(byDay, ritual, longitude, firstDay, lastDay, palette);
                }
            }
        }

        private static void GatherRitualDates(Dictionary<int, List<CalendarEntry>> byDay, Precept_Ritual ritual,
            float longitude, int firstDay, int lastDay, UIColorPaletteDef palette)
        {
            if (ritual.obligationTriggers == null)
                return;

            for (int t = 0; t < ritual.obligationTriggers.Count; t++)
            {
                if (!(ritual.obligationTriggers[t] is RitualObligationTrigger_Date dated))
                    continue;

                for (int day = firstDay; day <= lastDay; day++)
                {
                    if (day % GenDate.DaysPerYear != dated.triggerDaysSinceStartOfYear)
                        continue;

                    Add(byDay, day, new CalendarEntry
                    {
                        Kind = CalendarEntryKind.Ritual,
                        Label = ritual.LabelCap,
                        Tooltip = ritual.LabelCap + " falls due on this day every year.",
                        Icon = NotificationIcons.Bell,
                        Tint = palette.Info
                    });
                }
            }
        }

        private static void GatherRitualObligations(Dictionary<int, List<CalendarEntry>> byDay,
            Precept_Ritual ritual, float longitude, int firstDay, int lastDay, UIColorPaletteDef palette)
        {
            if (ritual.activeObligations == null)
                return;

            for (int o = 0; o < ritual.activeObligations.Count; o++)
            {
                RitualObligation obligation = ritual.activeObligations[o];

                if (obligation == null || !obligation.expires)
                    continue;

                int tick = Find.TickManager.TicksGame + obligation.TicksUntilExpiration;
                int day = DayIndex(ToAbsolute(tick), longitude);

                if (day < firstDay || day > lastDay)
                    continue;

                Add(byDay, day, new CalendarEntry
                {
                    Tick = tick,
                    Kind = CalendarEntryKind.Ritual,
                    Label = ritual.LabelCap + " due",
                    Tooltip = "The obligation for " + ritual.LabelCap
                              + " expires here. Letting it lapse upsets everyone who follows this ideoligion.",
                    Icon = NotificationIcons.Hazard,
                    Tint = palette.Warning
                });
            }
        }

        /// <summary>
        /// The storyteller's scheduled intervals, as a kind and nothing more.
        ///
        /// Deliberately unlabelled beyond its category. What actually fires is picked at the moment it fires, so
        /// naming a specific incident here would be a guess dressed as a fact -- see <see cref="CalendarForecast"/>.
        /// </summary>
        private static void GatherForecast(Dictionary<int, List<CalendarEntry>> byDay, float longitude,
            int firstDay, int lastDay, int daysAhead, UIColorPaletteDef palette)
        {
            List<ForecastMark> marks = CalendarForecast.Upcoming(daysAhead);

            bool explicitEvents = UIOverhaulSettingsFile.Current?.showExplicitStoryEvents ?? false;

            for (int i = 0; i < marks.Count; i++)
            {
                ForecastMark mark = marks[i];
                int day = DayIndex(ToAbsolute(mark.FireTick), longitude);

                if (day < firstDay || day > lastDay)
                    continue;

                string label = ForecastLabel(mark.Kind);

                if (explicitEvents)
                {
                    string detail = ExplicitDetail(mark.Comp);

                    if (!detail.NullOrEmpty())
                        label = label + ": " + detail;
                }

                Add(byDay, day, new CalendarEntry
                {
                    Tick = mark.FireTick,
                    Kind = CalendarEntryKind.Forecast,
                    Forecast = mark.Kind,
                    Label = label,
                    Tooltip = explicitEvents ? ExplicitTooltip(mark) : null,
                    Icon = ForecastIcon(mark.Kind),
                    Tint = ForecastTint(mark.Kind, palette)
                });
            }
        }

        /// <summary>
        /// The most specific true thing about a scheduled incident, for the explicit setting.
        ///
        /// <b>Sometimes that really is the exact event.</b> A <c>StorytellerCompProperties_OnOffCycle</c> may name
        /// a single <c>incident</c> rather than a category, and when it does there is nothing left to roll -- the
        /// component fires that incident and no other, so naming it is a fact rather than a guess.
        ///
        /// Where the component works from a category, the incident is chosen by a weighted pick at fire time and
        /// genuinely does not exist yet. There the honest answer is the category, and nothing further.
        /// </summary>
        private static string ExplicitDetail(StorytellerComp comp)
        {
            if (!(comp?.props is StorytellerCompProperties_OnOffCycle props))
                return null;

            if (props.incident != null)
                return props.incident.LabelCap;

            return props.IncidentCategory?.defName;
        }

        private static string ExplicitTooltip(ForecastMark mark)
        {
            if (!(mark.Comp?.props is StorytellerCompProperties_OnOffCycle props))
                return null;

            if (props.incident != null)
                return "Scheduled: " + props.incident.LabelCap
                       + "\n\nThis component fires one specific incident, so this is exact. Its duration is "
                       + "decided when it fires.";

            return "Scheduled by " + mark.Comp.GetType().Name
                   + "\n\nThe timing is already settled, but which incident fires is chosen from the "
                   + (props.IncidentCategory?.defName ?? "category")
                   + " pool at the moment it happens. It will be named here once it has.";
        }

        internal static string ForecastLabel(ForecastKind kind)
        {
            switch (kind)
            {
                case ForecastKind.MajorThreat: return "Major threat";
                case ForecastKind.MinorThreat: return "Minor threat";
                case ForecastKind.Disease: return "Disease";
                case ForecastKind.Quest: return "Quest offer";
                default: return "Event";
            }
        }

        private static Texture2D ForecastIcon(ForecastKind kind)
        {
            switch (kind)
            {
                case ForecastKind.MajorThreat:
                case ForecastKind.MinorThreat:
                    return NotificationIcons.Hazard;

                case ForecastKind.Disease: return NotificationIcons.Toxic;
                case ForecastKind.Quest: return NotificationIcons.Envelope;
                default: return NotificationIcons.Bell;
            }
        }

        internal static Color ForecastTint(ForecastKind kind, UIColorPaletteDef palette)
        {
            switch (kind)
            {
                case ForecastKind.MajorThreat: return palette.Danger;
                case ForecastKind.MinorThreat: return palette.Warning;
                case ForecastKind.Disease: return palette.Warning;
                case ForecastKind.Quest: return palette.Info;
                default: return palette.TextSecondary;
            }
        }
    }
}
