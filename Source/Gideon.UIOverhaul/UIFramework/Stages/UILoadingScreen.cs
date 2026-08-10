using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Stages
{
    /// <summary>
    /// An immutable read of the loading state, taken under lock so the UI never sees a half-written
    /// stage paired with the wrong step.
    /// </summary>
    public struct UILoadingSnapshot
    {
        /// <summary>Human-readable phase, e.g. "Processing definitions". Never null; may be empty.</summary>
        public string Stage;

        /// <summary>What that phase is working on right now, e.g. a mod name or a defName. May be empty.</summary>
        public string Step;

        /// <summary>Overall progress, 0 to 1.</summary>
        public float Fraction;

        /// <summary>False before anything has reported, so a screen can avoid drawing an empty bar.</summary>
        public bool HasProgress;
    }

    /// <summary>
    /// Tracks how far the game has got through loading, so a loading screen has something to show
    /// beyond a spinner.
    ///
    /// The data comes from RimWorld's own instrumentation rather than a phase list of our own: every
    /// load phase is already bracketed by <c>DeepProfiler.Start(label)</c> / <c>End()</c>, and the
    /// nested labels carry the detail -- the mod being read, the node count, and via a separate hook
    /// the defName being processed. Feeding off vanilla's labels means the stage names stay correct
    /// as long as those calls exist, instead of silently describing a sequence the game no longer runs.
    ///
    /// Written from the loading thread and read from OnGUI, so everything goes through one lock. The
    /// snapshot is a struct copy, which keeps the lock held for a few field reads rather than for the
    /// duration of a draw.
    ///
    /// Nothing here assumes it is being driven by our patches. Any mod can call
    /// <see cref="Report"/> to drive the same display.
    /// </summary>
    public static class UILoadingScreen
    {
        private static readonly object Lock = new object();

        private static readonly List<string> stack = new List<string>();
        private static string stage = string.Empty;
        private static string step = string.Empty;
        private static float fraction;
        private static bool hasProgress;

        private static int defsSeen;
        private static int defsTotal;
        private static float defsBase;
        private static float defsCeiling;

        /// <summary>
        /// Depth beyond which pushes are ignored. DeepProfiler nests only a few levels in practice;
        /// a runaway stack would mean Start and End had stopped pairing up, and dropping the excess is
        /// better than growing a list forever.
        /// </summary>
        private const int MaxDepth = 64;

        /// <summary>
        /// The phases RimWorld reports, in the order it reports them, with the fraction complete when
        /// each begins and the wording to show the player.
        ///
        /// Keys are the literal strings passed to DeepProfiler.Start, read out of Assembly-CSharp
        /// 1.6.9676. A label that is not in this table is still shown as the step; it just does not
        /// move the bar. That is the intended failure mode for a future game version: the bar becomes
        /// coarser, nothing breaks.
        ///
        /// "XmlInheritance.Clear()" is deliberately absent. It is called both before and after the mod
        /// load, and a milestone that fires twice would drag the bar backwards.
        /// </summary>
        private static readonly Milestone[] Milestones =
        {
            new Milestone("GraphicDatabase.Clear()", "Starting up", 0.00f),
            new Milestone("InitializeMods()", "Initializing mods", 0.01f),
            new Milestone("LoadModContent()", "Loading mod content", 0.03f),
            new Milestone("CreateModClasses()", "Starting mods", 0.10f),
            new Milestone("LoadModXML()", "Reading XML", 0.12f),
            new Milestone("CombineIntoUnifiedXML()", "Combining XML", 0.30f),
            new Milestone("TKeySystem.Parse()", "Parsing translation keys", 0.36f),
            new Milestone("ErrorCheckPatches()", "Checking patches", 0.38f),
            new Milestone("ApplyPatches()", "Applying patches", 0.40f),
            new Milestone("ParseAndProcessXML()", "Processing definitions", 0.50f),
            new Milestone("ClearCachedPatches()", "Clearing patch cache", 0.72f),
            new Milestone("Load language metadata.", "Loading language data", 0.74f),
            new Milestone("Copy all Defs from mods to global databases.", "Registering definitions", 0.76f),
            new Milestone("Resolve cross-references between non-implied Defs.", "Resolving references", 0.79f),
            new Milestone("Rebind DefOfs (early).", "Binding definitions", 0.81f),
            new Milestone("TKeySystem.BuildMappings()", "Building translation maps", 0.82f),
            new Milestone("Legacy backstory translations.", "Loading backstories", 0.83f),
            new Milestone("Inject selected language data into game data (early pass).", "Applying translations", 0.84f),
            new Milestone("Global operations (early pass).", "Preparing game data", 0.85f),
            new Milestone("Generate implied Defs (pre-resolve).", "Generating definitions", 0.86f),
            new Milestone("Resolve cross-references between Defs made by the implied defs.", "Resolving references", 0.88f),
            new Milestone("Rebind DefOfs (final).", "Binding definitions", 0.89f),
            new Milestone("Other def binding, resetting and global operations (pre-resolve).", "Preparing game data", 0.90f),
            new Milestone("Resolve references.", "Resolving references", 0.91f),
            new Milestone("Generate implied Defs (post-resolve).", "Generating definitions", 0.95f),
            new Milestone("Other def binding, resetting and global operations (post-resolve).", "Finalizing game data", 0.96f),
            new Milestone("Error check all defs.", "Checking definitions", 0.97f),
            new Milestone("Load keyboard preferences.", "Loading preferences", 0.99f),
            new Milestone("Short hash giving.", "Assigning hashes", 0.995f)
        };

        private static Dictionary<string, int> milestoneIndex;

        private readonly struct Milestone
        {
            public readonly string Label;
            public readonly string Display;
            public readonly float Fraction;

            public Milestone(string label, string display, float fraction)
            {
                Label = label;
                Display = display;
                Fraction = fraction;
            }
        }

        /// <summary>A copy of the current state. Safe to call from OnGUI at any time.</summary>
        public static UILoadingSnapshot Snapshot()
        {
            lock (Lock)
            {
                return new UILoadingSnapshot
                {
                    Stage = stage,
                    Step = step,
                    Fraction = fraction,
                    HasProgress = hasProgress
                };
            }
        }

        /// <summary>
        /// Sets the display directly. For mods driving their own long operation, and for anything that
        /// wants a loading screen without RimWorld's profiler labels behind it. Pass a negative
        /// <paramref name="fractionOrNegative"/> to leave the bar where it is.
        /// </summary>
        public static void Report(string stageText, string stepText, float fractionOrNegative = -1f)
        {
            lock (Lock)
            {
                if (stageText != null)
                    stage = stageText;
                if (stepText != null)
                    step = stepText;
                if (fractionOrNegative >= 0f)
                    Advance(fractionOrNegative);
                hasProgress = true;
            }
        }

        /// <summary>Clears everything, ready for another load.</summary>
        public static void Reset()
        {
            lock (Lock)
            {
                stack.Clear();
                stage = string.Empty;
                step = string.Empty;
                fraction = 0f;
                hasProgress = false;
                defsSeen = 0;
                defsTotal = 0;
                defsBase = 0f;
                defsCeiling = 0f;
            }
        }

        /// <summary>
        /// A phase began. Called from the DeepProfiler.Start hook; the label is whatever RimWorld
        /// passed, including runtime-built ones such as "Core content".
        /// </summary>
        public static void PushStage(string label)
        {
            if (label == null)
                label = string.Empty;

            lock (Lock)
            {
                if (stack.Count < MaxDepth)
                    stack.Add(label);

                hasProgress = true;

                // A known phase sets both the wording and the bar. An unknown one is still worth
                // showing as the step: that is where the mod names and node counts come through.
                if (TryGetMilestone(label, out int index))
                {
                    stage = Milestones[index].Display;
                    step = string.Empty;
                    Advance(Milestones[index].Fraction);

                    defsSeen = 0;
                    defsTotal = 0;
                    defsBase = Milestones[index].Fraction;
                    defsCeiling = index + 1 < Milestones.Length ? Milestones[index + 1].Fraction : 1f;
                }
                else
                {
                    step = label;
                }
            }
        }

        /// <summary>A phase ended. Called from the DeepProfiler.End hook.</summary>
        public static void PopStage()
        {
            lock (Lock)
            {
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
            }
        }

        /// <summary>
        /// The number of def nodes about to be processed, taken from the unified XML document. Turns
        /// the longest phase of the load from a flat bar into a moving one.
        /// </summary>
        public static void SetDefTotal(int total)
        {
            lock (Lock)
            {
                defsTotal = Mathf.Max(0, total);
                defsSeen = 0;
            }
        }

        /// <summary>
        /// One def has been processed. Moves the bar between the def phase's start and the next
        /// milestone, and shows the defName as the step.
        /// </summary>
        public static void ReportDef(string defName)
        {
            lock (Lock)
            {
                defsSeen++;
                hasProgress = true;

                if (!defName.NullOrEmpty())
                    step = defName;

                if (defsTotal <= 0 || defsCeiling <= defsBase)
                    return;

                float within = Mathf.Clamp01((float) defsSeen / defsTotal);
                Advance(defsBase + (defsCeiling - defsBase) * within);
            }
        }

        /// <summary>
        /// Moves the bar forward only. Loading is not perfectly ordered -- a milestone can fire after
        /// def counting has already pushed past it -- and a bar that jumps backwards reads as a bug
        /// even when the underlying numbers are honest.
        ///
        /// Caller must hold the lock.
        /// </summary>
        private static void Advance(float value)
        {
            float clamped = Mathf.Clamp01(value);
            if (clamped > fraction)
                fraction = clamped;
        }

        private static bool TryGetMilestone(string label, out int index)
        {
            if (milestoneIndex == null)
            {
                milestoneIndex = new Dictionary<string, int>(Milestones.Length);
                for (int i = 0; i < Milestones.Length; i++)
                    milestoneIndex[Milestones[i].Label] = i;
            }

            return milestoneIndex.TryGetValue(label, out index);
        }
    }
}
