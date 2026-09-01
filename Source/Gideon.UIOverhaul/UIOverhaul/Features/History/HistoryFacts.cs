using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.History
{
    /// <summary>One recorded quantity, unpacked from its recorder so the chart never touches a def.</summary>
    internal sealed class HistorySeries
    {
        internal string Label;

        /// <summary>The recorded values, oldest first. Index times <see cref="DaysPerRecord"/> is its day.</summary>
        internal List<float> Records;

        internal float DaysPerRecord;

        /// <summary>The recorder's own format string, <c>${0}</c> for wealth and <c>{0}%</c> for mood.</summary>
        internal string ValueFormat;

        /// <summary>The last value recorded, or zero when nothing has been.</summary>
        internal float Latest
        {
            get { return Records == null || Records.Count == 0 ? 0f : Records[Records.Count - 1]; }
        }

        /// <summary>
        /// The value on a given day, interpolated between the two records either side of it.
        ///
        /// <b>Interpolated rather than nearest, because the plot is drawn per pixel column.</b> At three hundred
        /// days across a thousand pixels there are two records per column and nearest is fine; at thirty days
        /// there are thirty records across the same thousand and nearest draws a staircase.
        /// </summary>
        internal float At(float day)
        {
            if (Records == null || Records.Count == 0 || DaysPerRecord <= 0f)
                return 0f;

            float position = day / DaysPerRecord;

            if (position <= 0f)
                return Records[0];

            if (position >= Records.Count - 1)
                return Records[Records.Count - 1];

            int low = (int) position;

            return Mathf.Lerp(Records[low], Records[low + 1], position - low);
        }

        /// <summary>The last day this series has a record for.</summary>
        internal float LastDay
        {
            get { return Records == null || Records.Count <= 1 ? 0f : (Records.Count - 1) * DaysPerRecord; }
        }
    }

    /// <summary>One thing that happened, at a tick, for the ribbon and the archive list.</summary>
    internal sealed class HistoryMoment
    {
        internal int TicksGame;

        internal string Label;

        internal Color Tint;

        internal Texture Icon;

        /// <summary>Set for an archived letter or message; null for a tale or a battle.</summary>
        internal IArchivable Archived;

        /// <summary>True for a permanent historical tale, which is drawn as a labelled mark.</summary>
        internal bool Tale;

        /// <summary>Set for a battle, which is drawn heavier than a letter and lighter than a tale.</summary>
        internal Battle Battle;

        internal float Day
        {
            get { return TicksGame / (float) GenDate.TicksPerDay; }
        }
    }

    /// <summary>
    /// Everything the history tab reads, gathered in one place so the drawing never reaches into the game.
    ///
    /// <b>The four sources do not remember the same amount, and that asymmetry is the interesting fact about
    /// this screen.</b> The recorder curves are appended every half day and never pruned, so they are complete
    /// back to day one. Permanent historical tales are never culled either. The archive keeps the last two
    /// hundred unpinned entries and drops the rest on every <c>Archive.Add</c>. The battle log keeps twenty.
    /// Drawing all four on one axis without saying so would make a long colony look as though nothing happened
    /// in its first year, which is why <see cref="ArchiveHorizonDay"/> exists and why the ribbon hatches
    /// everything left of it.
    ///
    /// <b><c>HistoryEventsManager</c> is deliberately not one of the sources.</b> It looks like the ideal one --
    /// it stores ticks per event def -- but <c>CheckRemoveOldEvents</c> cuts each faction down to twenty records
    /// and then drops anything scoring below 0.5. It is a scratchpad for goodwill arithmetic, not a history, and
    /// anything built on it would quietly lose most of what it showed.
    /// </summary>
    internal static class HistoryFacts
    {
        /// <summary>How many entries the archive keeps before it starts dropping the oldest unpinned one.</summary>
        internal const int ArchiveCap = 200;

        /// <summary>The groups a player may plot, which is every group but the dev-only one.</summary>
        internal static List<HistoryAutoRecorderGroup> Groups()
        {
            return UIGuard.Try("History.Groups", () =>
            {
                List<HistoryAutoRecorderGroup> found = new List<HistoryAutoRecorderGroup>();
                List<HistoryAutoRecorderGroup> all = Find.History != null ? Find.History.Groups() : null;

                if (all == null)
                    return found;

                for (int i = 0; i < all.Count; i++)
                {
                    HistoryAutoRecorderGroup group = all[i];

                    if (group?.def == null || group.recorders == null)
                        continue;

                    if (group.def.devModeOnly && !Prefs.DevMode)
                        continue;

                    found.Add(group);
                }

                return found;
            }, new List<HistoryAutoRecorderGroup>(), "The history tab plots nothing this session.");
        }

        /// <summary>A group's recorders, unpacked. Recorders with nothing in them are left out.</summary>
        internal static List<HistorySeries> SeriesOf(HistoryAutoRecorderGroup group)
        {
            List<HistorySeries> series = new List<HistorySeries>();

            if (group?.recorders == null)
                return series;

            for (int i = 0; i < group.recorders.Count; i++)
            {
                HistoryAutoRecorder recorder = group.recorders[i];

                if (recorder?.def == null || recorder.records == null || recorder.records.Count == 0)
                    continue;

                // Guarded rather than trusted: recordTicksFrequency is an XML field and a modded recorder that
                // sets it to zero would make every day map to the same record and every division a divide by
                // nought.
                float perRecord = recorder.def.recordTicksFrequency > 0
                    ? recorder.def.recordTicksFrequency / (float) GenDate.TicksPerDay
                    : 0.5f;

                series.Add(new HistorySeries
                {
                    Label = recorder.def.LabelCap,
                    Records = recorder.records,
                    DaysPerRecord = perRecord,
                    ValueFormat = recorder.def.valueFormat
                });
            }

            return series;
        }

        /// <summary>
        /// Which series, if any, is the sum of all the others: the one to draw as an outline over stacked bands.
        ///
        /// <b>Detected rather than named.</b> Hardcoding "the group called Wealth stacks" would be right for the
        /// base game and wrong for the first mod that adds a group of its own, and summing is a property the
        /// numbers either have or do not. Eight samples across the run is enough to tell a real total from a
        /// coincidence, and a group that only sums for part of its life is one this should decline anyway.
        ///
        /// Returns -1 when nothing sums, which is every group but wealth, and those draw as plain lines.
        /// </summary>
        internal static int TotalIndex(List<HistorySeries> series)
        {
            if (series == null || series.Count < 3)
                return -1;

            for (int candidate = 0; candidate < series.Count; candidate++)
            {
                if (SumsToCandidate(series, candidate))
                    return candidate;
            }

            return -1;
        }

        private static bool SumsToCandidate(List<HistorySeries> series, int candidate)
        {
            float last = series[candidate].LastDay;

            if (last <= 0f)
                return false;

            for (int sample = 1; sample <= 8; sample++)
            {
                float day = last * sample / 8f;
                float whole = series[candidate].At(day);
                float parts = 0f;

                for (int i = 0; i < series.Count; i++)
                {
                    if (i != candidate)
                        parts += series[i].At(day);
                }

                // Two percent of the whole, with an absolute floor so an early colony worth almost nothing does
                // not fail on rounding. A relative test alone divides by zero on day one.
                float slack = Mathf.Max(Mathf.Abs(whole) * 0.02f, 1f);

                if (Mathf.Abs(whole - parts) > slack)
                    return false;
            }

            return true;
        }

        /// <summary>Today, in days since the colony started.</summary>
        internal static float Today
        {
            get
            {
                return Find.TickManager == null
                    ? 0f
                    : Find.TickManager.TicksGame / (float) GenDate.TicksPerDay;
            }
        }

        // -------------------------------------------------------------------------------------------
        // The archive
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The earliest day the archive still holds anything for, or zero when it holds everything.
        ///
        /// The list is kept sorted by <c>CreatedTicksGame</c> by <c>Archive.Add</c>, so this is the head of it
        /// rather than a walk.
        /// </summary>
        internal static float ArchiveHorizonDay
        {
            get
            {
                return UIGuard.Try("History.Horizon", () =>
                {
                    List<IArchivable> all = Find.Archive?.ArchivablesListForReading;

                    return all == null || all.Count == 0
                        ? 0f
                        : all[0].CreatedTicksGame / (float) GenDate.TicksPerDay;
                }, 0f, null);
            }
        }

        /// <summary>How many things the archive is holding, and how many of those are pinned.</summary>
        internal static void ArchiveCounts(out int total, out int pinned, out int letters, out int messages)
        {
            int held = 0;
            int kept = 0;
            int letterCount = 0;
            int messageCount = 0;

            UIGuard.Try("History.ArchiveCounts", () =>
            {
                Archive archive = Find.Archive;
                List<IArchivable> all = archive?.ArchivablesListForReading;

                if (all == null)
                    return;

                held = all.Count;

                for (int i = 0; i < all.Count; i++)
                {
                    if (archive.IsPinned(all[i]))
                        kept++;

                    if (IsMessage(all[i]))
                        messageCount++;
                    else
                        letterCount++;
                }
            }, null);

            total = held;
            pinned = kept;
            letters = letterCount;
            messages = messageCount;
        }

        /// <summary>
        /// Whether this entry is a transient message rather than a letter.
        ///
        /// The same test vanilla's own filter makes, kept in one place because the counts and the list both ask
        /// it and two copies of it would be two chances to disagree about what a chip's number means.
        /// </summary>
        internal static bool IsMessage(IArchivable archivable)
        {
            return archivable is Message;
        }

        /// <summary>
        /// The archive, newest first, filtered the way the chips say.
        /// </summary>
        internal static List<HistoryMoment> Archive(bool letters, bool messages, bool pinnedOnly, string search)
        {
            return UIGuard.Try("History.Archive", () =>
            {
                List<HistoryMoment> rows = new List<HistoryMoment>();
                Archive archive = Find.Archive;
                List<IArchivable> all = archive?.ArchivablesListForReading;

                if (all == null)
                    return rows;

                bool filtering = !search.NullOrEmpty();

                for (int i = all.Count - 1; i >= 0; i--)
                {
                    IArchivable archivable = all[i];

                    if (archivable == null)
                        continue;

                    if (IsMessage(archivable) ? !messages : !letters)
                        continue;

                    if (pinnedOnly && !archive.IsPinned(archivable))
                        continue;

                    string label = archivable.ArchivedLabel;

                    if (filtering && (label.NullOrEmpty()
                                      || label.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0))
                        continue;

                    rows.Add(new HistoryMoment
                    {
                        TicksGame = archivable.CreatedTicksGame,
                        Label = label,
                        Tint = archivable.ArchivedIconColor,
                        Icon = archivable.ArchivedIcon,
                        Archived = archivable
                    });
                }

                return rows;
            }, new List<HistoryMoment>(), "The archive list is empty this session.");
        }

        // -------------------------------------------------------------------------------------------
        // The ribbon
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Everything worth a mark between two days: tales, battles and archived letters.
        ///
        /// <b>Filtered to the span before anything is built,</b> because this runs every frame the chart is on
        /// screen and the archive alone is two hundred objects.
        /// </summary>
        internal static List<HistoryMoment> Moments(float fromDay, float toDay)
        {
            List<HistoryMoment> moments = new List<HistoryMoment>();

            int from = Mathf.FloorToInt(fromDay * GenDate.TicksPerDay);
            int to = Mathf.CeilToInt(toDay * GenDate.TicksPerDay);

            UIGuard.Try("History.Tales", () =>
            {
                List<Tale> tales = Find.TaleManager?.AllTalesListForReading;

                if (tales == null)
                    return;

                for (int i = 0; i < tales.Count; i++)
                {
                    Tale tale = tales[i];

                    if (tale?.def == null || tale.hidden || tale.def.type != TaleType.PermanentHistorical)
                        continue;

                    // Tales carry an absolute date; everything else on this axis is a game tick.
                    int tick = GenDate.TickAbsToGame(tale.date);

                    if (tick < from || tick > to)
                        continue;

                    moments.Add(new HistoryMoment
                    {
                        TicksGame = tick,
                        Label = tale.ShortSummary,
                        Tint = tale.def.historyGraphColor,
                        Tale = true
                    });
                }
            }, null);

            UIGuard.Try("History.BattleMarks", () =>
            {
                List<Battle> battles = Find.BattleLog?.Battles;

                if (battles == null)
                    return;

                for (int i = 0; i < battles.Count; i++)
                {
                    Battle battle = battles[i];

                    if (battle == null || battle.AbsorbedBy != null)
                        continue;

                    if (battle.CreationTimestamp < from || battle.CreationTimestamp > to)
                        continue;

                    moments.Add(new HistoryMoment
                    {
                        TicksGame = battle.CreationTimestamp,
                        Label = battle.GetName(),
                        Battle = battle
                    });
                }
            }, null);

            UIGuard.Try("History.LetterMarks", () =>
            {
                List<IArchivable> all = Find.Archive?.ArchivablesListForReading;

                if (all == null)
                    return;

                for (int i = 0; i < all.Count; i++)
                {
                    IArchivable archivable = all[i];

                    if (archivable == null || archivable.CreatedTicksGame < from
                                           || archivable.CreatedTicksGame > to)
                        continue;

                    moments.Add(new HistoryMoment
                    {
                        TicksGame = archivable.CreatedTicksGame,
                        Label = archivable.ArchivedLabel,
                        Tint = archivable.ArchivedIconColor,
                        Archived = archivable
                    });
                }
            }, null);

            return moments;
        }

        // -------------------------------------------------------------------------------------------
        // Battles
        // -------------------------------------------------------------------------------------------

        /// <summary>The battle log, newest first, absorbed battles left out.</summary>
        internal static List<Battle> Battles()
        {
            return UIGuard.Try("History.Battles", () =>
            {
                List<Battle> found = new List<Battle>();
                List<Battle> all = Find.BattleLog?.Battles;

                if (all == null)
                    return found;

                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] != null && all[i].AbsorbedBy == null)
                        found.Add(all[i]);
                }

                return found;
            }, new List<Battle>(), "The battles list is empty this session.");
        }

        /// <summary>The colonists a battle concerns, as a readable list, or a dash when it concerns none.</summary>
        internal static string WhoWasIn(Battle battle, int max = 3)
        {
            return UIGuard.Try("History.BattleWho", () =>
            {
                if (battle == null)
                    return "-";

                List<Pawn> colonists = PawnsFinder.AllMaps_FreeColonistsSpawned;

                if (colonists == null || colonists.Count == 0)
                    return "-";

                List<string> names = new List<string>();
                int more = 0;

                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn pawn = colonists[i];

                    if (pawn?.Name == null || !battle.Concerns(pawn))
                        continue;

                    if (names.Count < max)
                        names.Add(pawn.Name.ToStringShort);
                    else
                        more++;
                }

                if (names.Count == 0)
                    return "-";

                string joined = string.Join(", ", names.ToArray());

                return more > 0 ? joined + ", +" + more : joined;
            }, "-", null);
        }

        /// <summary>How long a battle ran, as a short duration, or a dash when it was over instantly.</summary>
        internal static string Lasted(Battle battle)
        {
            if (battle == null)
                return "-";

            int ticks = battle.LastEntryTimestamp - battle.CreationTimestamp;

            if (ticks <= 0)
                return "-";

            int hours = ticks / 2500;
            int minutes = Mathf.RoundToInt(ticks % 2500 / 2500f * 60f);

            return hours > 0 ? hours + "h " + minutes + "m" : minutes + "m";
        }

        // -------------------------------------------------------------------------------------------
        // Dates
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// A game tick as the date it fell on, at the current map's longitude.
        ///
        /// Longitude rather than a bare division, for the reason vanilla's own readouts take it: the local day
        /// boundary moves with it, and two colonies on opposite sides of the planet do not turn over together.
        /// </summary>
        internal static string DateOf(int ticksGame)
        {
            return UIGuard.Try("History.Date", () =>
            {
                Vector2 location = Find.CurrentMap != null
                    ? Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile)
                    : Vector2.zero;

                return GenDate.DateShortStringAt(GenDate.TickGameToAbs(ticksGame), location);
            }, "-", null);
        }

        /// <summary>A figure with thousands separators, which is what <c>ToString("F0")</c> never gave.</summary>
        internal static string Figure(float value)
        {
            return Mathf.RoundToInt(value).ToString("N0");
        }

        /// <summary>A wealth figure, with the game's own silver mark in front of it.</summary>
        internal static string Silver(float value)
        {
            return "$" + Figure(value);
        }

        /// <summary>A wealth figure shortened for a rail or a header, where the column is narrow.</summary>
        internal static string ShortSilver(float value)
        {
            return value >= 1000f
                ? "$" + (value / 1000f).ToString("0.#") + "k"
                : "$" + Figure(value);
        }
    }
}
