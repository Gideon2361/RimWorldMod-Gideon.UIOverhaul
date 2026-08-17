using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Stages
{
    /// <summary>What a logged line was. Ordered loosely by how much it matters.</summary>
    public enum UILoadingLogKind
    {
        /// <summary>A boundary between loads, e.g. startup ending and map generation beginning.</summary>
        Section,

        /// <summary>A named phase of the load, matching a milestone on the progress bar.</summary>
        Stage,

        /// <summary>Detail inside a phase: a mod name, a node count, a generation step.</summary>
        Step,

        /// <summary>One definition, with the XML file it was read from.</summary>
        Def,

        Warning,

        Error
    }

    /// <summary>One line of what happened during a load.</summary>
    public struct UILoadingLogEntry
    {
        public UILoadingLogKind Kind;

        /// <summary>The text as the player saw it, or the defName, or the logged message.</summary>
        public string Text;

        /// <summary>
        /// The full path of the XML file a definition came from. Null on everything else.
        ///
        /// A shared reference rather than a string per entry: every definition in one file points at the same
        /// instance. See <see cref="UILoadingLog.RecordDef"/>.
        /// </summary>
        public string Path;

        /// <summary>Seconds since logging began, which is the number worth having.</summary>
        public float Seconds;

        /// <summary>How long this line stayed current before the next one replaced it.</summary>
        public float Duration;

        /// <summary>
        /// Whether <see cref="Duration"/> was given by the caller rather than measured from the next line.
        ///
        /// <b>Some lines describe work that did not happen while they were on screen.</b> A line summarizing
        /// thirty thousand callbacks is written once they have all run, so measuring how long it stayed current
        /// gives the gap until the next line -- under a second -- while the text beside it says forty-eight.
        /// The two numbers disagreed because they were measuring different things, and only one of them was the
        /// one worth reading.
        /// </summary>
        public bool DurationFixed;

        /// <summary>How many times in a row this same line was reported. One for an ordinary line.</summary>
        public int Repeats;

        /// <summary>Whether this line is a problem rather than a description of progress.</summary>
        public bool IsProblem => Kind == UILoadingLogKind.Error || Kind == UILoadingLogKind.Warning;
    }

    /// <summary>
    /// Keeps what happened during a load, so it can be read back afterwards.
    ///
    /// <b>The loading screen is the one part of the UI that destroys its own output.</b> Everything it says goes
    /// past at whatever speed the load runs, one line at a time, and then the screen is gone. If a mod took forty
    /// seconds in one phase, or a definition failed to parse, the only trace afterwards is whatever the player
    /// happened to be looking at and whatever survived in the log among ten thousand other lines. This keeps the
    /// whole sequence, in order, with a timestamp on each entry, definitions attributed to the file they came
    /// from, and any errors and warnings raised along the way sitting in position between them.
    ///
    /// <b>Recording is unconditional while it is active, and that is deliberate.</b> Gating it on the player's
    /// setting is the obvious design and the wrong one twice over. Practically, a diagnostic you have to switch on
    /// and then reproduce the problem for is never on when it is wanted; the value is being able to turn the panel
    /// on <i>after</i> noticing something and read the load that already happened. Architecturally, this lives in
    /// the framework and the framework cannot see <c>Gideon.UIOverhaul</c>'s settings; that dependency runs the
    /// wrong way, which is the same reason <c>UIDebug</c> has its flag pushed into it rather than reading one.
    ///
    /// <b>It stops and frees everything the moment a game starts.</b> Per-definition logging is tens of thousands
    /// of entries on a large mod list, which is a few megabytes, and that is a fair price for a panel being read
    /// at the main menu and no price at all worth paying for the rest of a colony's life. <see cref="Deactivate"/>
    /// is called when a game finalizes: the list is emptied, its capacity released, and recording stops until the
    /// main menu comes back. Nothing here is meant to be readable during play.
    ///
    /// <b>Timed with a Stopwatch rather than Unity's clock.</b> Almost every call arrives on the loading thread,
    /// and <c>Time.realtimeSinceStartup</c> is one of the Unity properties that throws when read off the main
    /// thread. Same trap <c>UIGuard.Context</c> documents for <c>Time.frameCount</c>.
    ///
    /// Written from the loading thread and read from OnGUI, so everything goes through one lock, and reads hand
    /// back a copy rather than the live list.
    /// </summary>
    public static class UILoadingLog
    {
        /// <summary>
        /// Hard cap on lines kept.
        ///
        /// Sized for per-definition logging on a heavy mod list, which is the case that sets it: a hundred
        /// thousand definitions is a large but real load. Past this, lines are counted rather than stored and the
        /// panel says so, so a pathological case stops growing instead of filling memory. The cap only has to
        /// hold until a game starts, at which point everything is released anyway.
        /// </summary>
        private const int MaxEntries = 300000;

        private static readonly object Lock = new object();
        private static List<UILoadingLogEntry> entries = new List<UILoadingLogEntry>();
        private static readonly Stopwatch clock = new Stopwatch();

        private static int dropped;

        /// <summary>
        /// Whether anything is being recorded.
        ///
        /// Starts true, because the load this exists to describe is already running by the time anything could
        /// switch it on.
        /// </summary>
        private static bool active = true;

        /// <summary>Lines that were reported after the cap was reached.</summary>
        public static int Dropped
        {
            get
            {
                lock (Lock)
                    return dropped;
            }
        }

        public static int Count
        {
            get
            {
                lock (Lock)
                    return entries.Count;
            }
        }

        public static bool Active
        {
            get
            {
                lock (Lock)
                    return active;
            }
        }

        /// <summary>Seconds since the first line was recorded.</summary>
        public static float TotalSeconds
        {
            get
            {
                lock (Lock)
                    return Elapsed();
            }
        }

        /// <summary>
        /// A copy of every line, oldest first.
        ///
        /// A copy rather than the list itself: this is read while drawing and written from a loading thread, and
        /// handing out the live list would put the caller's iteration a frame away from a collection modified
        /// exception the first time a load ran while the panel was open.
        /// </summary>
        public static List<UILoadingLogEntry> Snapshot()
        {
            lock (Lock)
                return new List<UILoadingLogEntry>(entries);
        }

        /// <summary>Starts recording again, without clearing. Called when the main menu comes back.</summary>
        public static void Activate()
        {
            lock (Lock)
                active = true;
        }

        /// <summary>
        /// Stops recording and releases everything held.
        ///
        /// <b>The list is replaced rather than cleared,</b> and that is the point of the method. <c>Clear()</c>
        /// empties a list without giving back the array behind it, so a list that grew to a hundred thousand
        /// entries would keep that array for the rest of the session -- which is precisely the memory this is
        /// supposed to hand back when the player stops looking at it.
        /// </summary>
        public static void Deactivate()
        {
            lock (Lock)
            {
                active = false;
                entries = new List<UILoadingLogEntry>();
                dropped = 0;
                clock.Reset();

                // Released with the entries. This holds a reference per definition, which on a heavy mod list is
                // tens of thousands, and keeping it after the console has been handed back is a leak.
                defPaths.Clear();
            }
        }

        /// <summary>
        /// Records a line, unless it repeats the one before it.
        ///
        /// Consecutive identical lines are counted rather than appended, which matters more than it looks: a phase
        /// that reports the same label for every item it handles would otherwise bury everything around it, and
        /// "this happened 900 times" is the more useful reading of that anyway.
        /// </summary>
        /// <param name="measured">
        /// How long the work this line describes actually took, when the caller knows and the log cannot. Leave
        /// it out for an ordinary line, whose duration is the time until the next one.
        /// </param>
        public static void Record(UILoadingLogKind kind, string text, string path = null,
            float measured = -1f)
        {
            if (text == null)
                text = string.Empty;

            lock (Lock)
            {
                if (!active)
                    return;

                if (!clock.IsRunning)
                    clock.Start();

                float now = Elapsed();

                if (entries.Count > 0)
                {
                    UILoadingLogEntry last = entries[entries.Count - 1];

                    if (last.Kind == kind && last.Text == text)
                    {
                        last.Repeats++;

                        if (!last.DurationFixed)
                            last.Duration = now - last.Seconds;

                        entries[entries.Count - 1] = last;

                        return;
                    }

                    // Closed off now rather than when the load ends, so the last line of an abandoned load still
                    // carries however long it ran for. A line that came with its own figure keeps it: measuring
                    // the gap to the next line would overwrite the only number that meant anything.
                    if (!last.DurationFixed)
                        last.Duration = now - last.Seconds;

                    entries[entries.Count - 1] = last;
                }

                if (entries.Count >= MaxEntries)
                {
                    dropped++;

                    return;
                }

                entries.Add(new UILoadingLogEntry
                {
                    Kind = kind,
                    Text = text,
                    Path = path,
                    Seconds = now,
                    Repeats = 1,
                    Duration = measured >= 0f ? measured : 0f,
                    DurationFixed = measured >= 0f
                });
            }
        }

        /// <summary>
        /// One definition, and the XML file it came from.
        ///
        /// <b><paramref name="path"/> must be a shared instance, not a freshly built string.</b> This is called
        /// once per definition, so a path composed per call would be tens of thousands of near-identical strings
        /// for what is really a few thousand distinct files. The caller caches one string per source file and
        /// hands the same reference to every definition in it; see the def source patch.
        /// </summary>
        public static void RecordDef(string defName, string path)
        {
            Record(UILoadingLogKind.Def, defName.NullOrEmpty() ? "(unnamed def)" : defName, path);

            if (defName.NullOrEmpty() || path == null)
                return;

            lock (Lock)
            {
                // Last writer wins, which is the right answer: a def defined twice is resolved by whichever
                // file loaded last, so that is the file somebody looking for it should be sent to.
                defPaths[defName] = path;
            }
        }

        /// <summary>
        /// Which file defined each definition, for messages that name one and no file.
        ///
        /// Kept as well as the log entries because looking a name up by walking thousands of entries, for every
        /// message captured, is not a lookup. The paths are the same shared instances the entries hold, so this
        /// costs the dictionary and no strings.
        /// </summary>
        private static readonly Dictionary<string, string> defPaths =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Shortest name this will match on.
        ///
        /// Four characters, because below that a defName stops being distinctive and starts colliding with
        /// ordinary English. "Wall" is a real defName and would claim any message containing the word.
        /// </summary>
        private const int MinDefNameChars = 4;

        /// <summary>
        /// The file behind a definition named anywhere in <paramref name="text"/>, or null.
        ///
        /// <b>This is a deduction and is deliberately shaped to fail rather than mislead.</b> Plenty of messages
        /// name a def and no file -- config errors are the whole category, and mods raise their own -- so the
        /// only handle available is the name sitting in the text. Every identifier-shaped token is looked up and
        /// nothing else is guessed at.
        ///
        /// <b>The longest match wins when several tokens are defNames,</b> which happens more often than it
        /// sounds: "FactionDef DE_Mycelyss must have at least one pawnGroupMaker with kindDef 'Peaceful'" names
        /// two, since <c>Peaceful</c> is itself a def. The longer name is the more specific one and, in
        /// practice, the mod's rather than the base game's. It is a heuristic; it is also the difference between
        /// an entry somebody can act on and one they cannot.
        /// </summary>
        public static string PathMentionedIn(string text)
        {
            if (text.NullOrEmpty())
                return null;

            string bestPath = null;
            int bestLength = 0;

            lock (Lock)
            {
                if (defPaths.Count == 0)
                    return null;

                int i = 0;

                while (i < text.Length)
                {
                    if (!IsNameChar(text[i]))
                    {
                        i++;

                        continue;
                    }

                    int start = i;

                    while (i < text.Length && IsNameChar(text[i]))
                        i++;

                    int length = i - start;

                    if (length < MinDefNameChars || length <= bestLength)
                        continue;

                    string token = text.Substring(start, length);
                    string path;

                    if (defPaths.TryGetValue(token, out path))
                    {
                        bestPath = path;
                        bestLength = length;
                    }
                }
            }

            return bestPath;
        }

        /// <summary>What a defName is allowed to contain, which is what makes tokenising it possible.</summary>
        private static bool IsNameChar(char c)
        {
            return c == '_' || (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        /// <summary>
        /// Marks the start of a separate load, such as a map generation after startup has finished.
        ///
        /// A separator rather than a clear: several loads can run before a game actually starts, and clearing on
        /// each would leave only the last one reviewable.
        /// </summary>
        public static void BeginSection(string name)
        {
            Record(UILoadingLogKind.Section, name);
        }

        /// <summary>Empties the log but keeps recording. For the panel's own clear button.</summary>
        public static void Clear()
        {
            lock (Lock)
            {
                entries = new List<UILoadingLogEntry>();
                dropped = 0;
                clock.Reset();
            }
        }

        /// <summary>Caller must hold the lock.</summary>
        private static float Elapsed()
        {
            return (float) clock.Elapsed.TotalSeconds;
        }

        /// <summary>
        /// Lines as text, for pasting into a bug report.
        ///
        /// Takes the lines to write rather than reading them itself, so the panel can hand over exactly what the
        /// reader is looking at. Copying the unfiltered log when somebody has narrowed it to four errors would be
        /// answering a question they did not ask.
        /// </summary>
        public static string AsText(List<UILoadingLogEntry> lines, string description)
        {
            if (lines == null)
                return string.Empty;

            StringBuilder text = new StringBuilder(lines.Count * 64);

            text.Append("Loading console: ").Append(description).Append('\n')
                .Append(lines.Count).Append(" lines, ")
                .Append(TotalSeconds.ToString("F1")).Append("s total.\n\n");

            foreach (UILoadingLogEntry entry in lines)
            {
                text.Append(entry.Seconds.ToString("F2").PadLeft(9)).Append("s  ");
                text.Append(Tag(entry.Kind));
                text.Append(entry.Text);

                if (entry.Repeats > 1)
                    text.Append("  x").Append(entry.Repeats);

                if (entry.Duration >= 0.05f)
                    text.Append("  (").Append(entry.Duration.ToString("F2")).Append("s)");

                if (!entry.Path.NullOrEmpty())
                    text.Append("\n                  ").Append(entry.Path);

                text.Append('\n');
            }

            int lost = Dropped;

            if (lost > 0)
                text.Append("\n... and ").Append(lost).Append(" further lines that were not kept.\n");

            return text.ToString();
        }

        private static string Tag(UILoadingLogKind kind)
        {
            switch (kind)
            {
                case UILoadingLogKind.Error:
                    return "ERROR   ";

                case UILoadingLogKind.Warning:
                    return "WARN    ";

                case UILoadingLogKind.Def:
                    return "  def   ";

                case UILoadingLogKind.Step:
                    return "  ";

                default:
                    return string.Empty;
            }
        }

        /// <summary>Formats a duration the way the panel and the copied text both want it.</summary>
        public static string Duration(float seconds)
        {
            return seconds >= 1f ? seconds.ToString("F2") + "s" : Mathf.RoundToInt(seconds * 1000f) + "ms";
        }

        /// <summary>
        /// The longest a row's text is allowed to be before this cuts it.
        ///
        /// <b>This bound is not cosmetic, it is what stops the game hanging.</b> Rows are drawn with
        /// <c>Widgets.LabelEllipses</c>, and <c>Text.ClampTextWithEllipsis</c> shortens an over-wide string by
        /// removing <i>one character at a time</i>, calling <c>CalcSize</c> on the whole remainder after each
        /// one. That is quadratic in the length of the string, and RimWorld logs single-line messages that are
        /// tens of thousands of characters long: "Could not find class X while resolving node li. Full node:"
        /// is followed by the entire XML node, cells and all, with no newline anywhere in it.
        ///
        /// One such row is on the order of a hundred million character measurements per frame, plus a string
        /// allocation per iteration. Twenty of them in view at once is a frozen main thread, which is exactly
        /// what happened: scrolling the log down until the "Full node" errors came into view stopped the game
        /// dead, with nothing in the player log because nothing had failed.
        ///
        /// 512 is far more than any row can display -- at three pixels a character, the narrowest plausible,
        /// that is still fifteen hundred pixels of text -- so the cut is never the reason an ellipsis appears.
        /// </summary>
        private const int MaxRowChars = 512;

        /// <summary>
        /// The first line of a logged message, cut to something a row can actually draw.
        ///
        /// An error carries its whole stack trace, which is what makes it useful in the copied text and useless in
        /// a row. The full text is still in the entry.
        ///
        /// <b>Length is capped as well as the line being taken,</b> because a newline is not the only thing that
        /// makes a message too long for a row and, on the messages that matter most, there is no newline at all.
        /// See <see cref="MaxRowChars"/> for why an uncapped row is a hang rather than an untidy one.
        /// </summary>
        public static string FirstLine(string text)
        {
            if (text.NullOrEmpty())
                return string.Empty;

            int end = text.IndexOf('\n');

            string line = end < 0 ? text : text.Substring(0, end).TrimEnd('\r');

            return line.Length <= MaxRowChars ? line : line.Substring(0, MaxRowChars);
        }
    }
}
