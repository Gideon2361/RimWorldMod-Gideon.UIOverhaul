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
            }
        }

        /// <summary>
        /// Records a line, unless it repeats the one before it.
        ///
        /// Consecutive identical lines are counted rather than appended, which matters more than it looks: a phase
        /// that reports the same label for every item it handles would otherwise bury everything around it, and
        /// "this happened 900 times" is the more useful reading of that anyway.
        /// </summary>
        public static void Record(UILoadingLogKind kind, string text, string path = null)
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
                        last.Duration = now - last.Seconds;
                        entries[entries.Count - 1] = last;

                        return;
                    }

                    // Closed off now rather than when the load ends, so the last line of an abandoned load still
                    // carries however long it ran for.
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
                    Repeats = 1
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
        /// The first line of a logged message, for the panel's single-line rows.
        ///
        /// An error carries its whole stack trace, which is what makes it useful in the copied text and useless in
        /// a row. The full text is still in the entry.
        /// </summary>
        public static string FirstLine(string text)
        {
            if (text.NullOrEmpty())
                return string.Empty;

            int end = text.IndexOf('\n');

            return end < 0 ? text : text.Substring(0, end).TrimEnd('\r');
        }
    }
}
