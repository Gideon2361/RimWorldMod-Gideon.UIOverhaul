using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Gideon.UIFramework.Helpers;
using Gideon.UIFramework.Stages;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Patches.Stages.LoadingScreen
{
    /// <summary>
    /// Runs every mod's startup code a few milliseconds at a time, so the game keeps drawing through the part of
    /// loading that otherwise looks like a crash.
    ///
    /// <b>What actually freezes.</b> <c>PlayDataLoader.LoadAllPlayData</c> is queued with
    /// <c>doAsynchronously: true</c>, so parsing defs, resolving cross-references and giving short hashes all
    /// happen on a worker thread while the main thread keeps drawing -- which is why the loading screen animates
    /// happily right up until it stops. The last stretch is different: <c>DoPlayLoad</c> registers a
    /// <c>LongEventHandler.ExecuteWhenFinished</c> callback that runs back on the main thread and calls
    /// <c>StaticConstructorOnStartupUtility.CallAll()</c>, a bare loop over every type in every mod carrying
    /// <c>[StaticConstructorOnStartup]</c>. It has to be on the main thread, because those constructors build
    /// Unity textures and materials. Nothing repaints until the loop returns, so on a large mod list the window
    /// is marked unresponsive for as long as the slowest mods take.
    ///
    /// <b>Why this pre-warms instead of replacing that loop.</b> A type initializer runs at most once -- the CLR
    /// guarantees it -- so <c>RuntimeHelpers.RunClassConstructor</c> on an already-initialized type does nothing
    /// at all. Running them early therefore leaves vanilla's <c>CallAll</c> exactly where it is, doing exactly
    /// what it did, and simply finding the work already done. Nothing is reordered, nothing is skipped, and the
    /// three things that follow it in that same callback -- <c>FloatMenuMakerMap.Init</c>, atlas baking and the
    /// forced collection -- still happen after the constructors, which is the ordering they depend on.
    ///
    /// <b>Why here and nowhere earlier.</b> <c>DoPlayLoad</c>'s own body ends by registering that callback, so a
    /// postfix on it sits at the first moment where every def is copied, cross-referenced, implied-generated and
    /// DefOf-bound, hashes are given and language data is injected -- and the constructors have still not run.
    /// Earlier is not merely less convenient, it is wrong: startup constructors routinely read <c>DefOf</c>
    /// fields and the def database, and pre-warming before those exist would break mods that work today.
    ///
    /// <b>It cannot make loading hang.</b> The worker waits on a timeout; if the pump never runs, or dies, or
    /// simply takes too long, the worker gives up and returns, and vanilla's <c>CallAll</c> does the work as it
    /// always has. Losing this feature costs the progress text, not the load.
    ///
    /// This does not make loading faster. It is the same work, made visible.
    /// </summary>
    internal static class StaticConstructorPrewarm
    {
        /// <summary>
        /// How long each frame may be spent on constructors.
        ///
        /// Small enough that the frame still lands inside a comfortable budget with our loading screen drawn on
        /// top of it, large enough that the pass is not spread over thousands of frames. One slow constructor can
        /// overshoot this on its own and nothing can be done about that: they are not interruptible.
        /// </summary>
        private const int FrameBudgetMs = 8;

        /// <summary>Long enough that a genuinely slow mod list finishes; short enough to not look like a hang.</summary>
        private const int WorkerTimeoutSeconds = 180;

        /// <summary>A constructor slower than this is worth naming in the loading console.</summary>
        private const long SlowMs = 100;

        private static Type[] pending;
        private static Dictionary<Assembly, string> owners;

        private static int next;
        private static int slowCount;

        private static volatile bool active;
        private static volatile bool finished;
        private static bool started;

        /// <summary>
        /// Which thread Unity runs on, learned by being called on it.
        ///
        /// Needed because the worker must never wait for a pump that can only be driven by itself.
        /// <c>Root.Start</c> has a path that runs the load synchronously on the main thread, and there the wait
        /// below would be a deadlock rather than a delay. By the time the load finishes this has been set for
        /// thousands of frames, so an unset value means the pump is not running and pre-warming is skipped.
        /// </summary>
        private static int mainThread;

        /// <summary>Called on the worker thread once every def is ready. Blocks until the pump is done.</summary>
        internal static void RunFromWorker()
        {
            if (started)
                return;

            started = true;

            UIGuard.Try("Stages.Prewarm.Start", () =>
            {
                // No pump, or we are the pump: either way waiting would be waiting on ourselves.
                if (mainThread == 0 || mainThread == Thread.CurrentThread.ManagedThreadId)
                    return;

                List<Type> types = new List<Type>();

                foreach (Type type in GenTypes.AllTypesWithAttribute<StaticConstructorOnStartup>())
                {
                    if (type != null)
                        types.Add(type);
                }

                if (types.Count == 0)
                    return;

                owners = BuildOwners();
                pending = types.ToArray();
                next = 0;
                slowCount = 0;
                finished = false;

                UILoadingLog.BeginSection("Mod startup code");
                UILoadingLog.Record(UILoadingLogKind.Stage, "Running startup code for " + pending.Length + " types.");

                active = true;

                Stopwatch waited = Stopwatch.StartNew();

                // Polled rather than signalled. A wait handle would be tidier, but this runs once per session on
                // a thread with nothing else to do, and a millisecond poll cannot deadlock or leak a handle.
                while (!finished && waited.Elapsed.TotalSeconds < WorkerTimeoutSeconds)
                    Thread.Sleep(1);

                active = false;

                if (!finished)
                {
                    UILoadingLog.Record(UILoadingLogKind.Warning,
                        "Startup code was still running after " + WorkerTimeoutSeconds
                        + " seconds. The rest runs the way it normally does, without progress being shown.");

                    return;
                }

                UILoadingLog.Record(UILoadingLogKind.Stage,
                    "Startup code finished. " + slowCount + " took longer than " + SlowMs + "ms.");
            }, "Startup code runs the way it normally does; the loading screen will not move while it does.");
        }

        /// <summary>Called on the main thread every frame. Spends a slice of the frame and returns.</summary>
        internal static void Pump()
        {
            // Recorded unconditionally, and this is the only place it can be learned: this method is called from
            // Root.Update, which is Unity's own thread by definition.
            mainThread = Thread.CurrentThread.ManagedThreadId;

            if (!active || pending == null)
                return;

            Stopwatch frame = Stopwatch.StartNew();

            while (next < pending.Length && frame.ElapsedMilliseconds < FrameBudgetMs)
            {
                Type type = pending[next];

                next++;

                Report(type);
                Run(type);
            }

            if (next >= pending.Length)
                finished = true;
        }

        /// <summary>
        /// Runs one type's initializer.
        ///
        /// <b>Deliberately identical to vanilla's own handling,</b> including the message: a mod whose startup
        /// code throws should produce the same line in the log whether or not this mod is installed, so nobody
        /// goes looking for a fault here. Catching is what keeps one bad mod from stopping the pass.
        /// </summary>
        private static void Run(Type type)
        {
            Stopwatch watch = Stopwatch.StartNew();

            try
            {
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            }
            catch (Exception ex)
            {
                Log.Error("Error in static constructor of " + type + ": " + ex);
            }

            watch.Stop();

            if (watch.ElapsedMilliseconds < SlowMs)
                return;

            slowCount++;

            // The whole point of the exercise for anybody diagnosing a slow load: which mod, and how long.
            UILoadingLog.Record(UILoadingLogKind.Step,
                Owner(type) + ": " + type.Name + " took " + watch.ElapsedMilliseconds + "ms.");
        }

        /// <summary>
        /// Names what is running and how far through it is.
        ///
        /// <b>Reported to our own loading screen rather than only to the event text.</b> The pre-warm happens
        /// before RimWorld's "Static constructor calls" profiler label is reached, so without this the stage
        /// heading would still read whatever the last def phase was while the longest stretch of the load ran
        /// underneath it. Driving the fraction from the position in the list is the one place on the whole
        /// loading screen where progress is genuinely known rather than estimated.
        /// </summary>
        private static void Report(Type type)
        {
            UIGuard.Try("Stages.Prewarm.Report", () =>
            {
                float through = pending.Length > 0 ? next / (float) pending.Length : 0f;

                // Spans the room the milestone table gives "Running mod startup code" and stops short of the
                // step after it, so the bar moves throughout rather than jumping at the end.
                UILoadingScreen.Report("Running mod startup code",
                    Owner(type) + "  (" + next + " of " + pending.Length + ")",
                    Mathf.Lerp(0.96f, 0.985f, through));
            }, null);
        }

        private static string Owner(Type type)
        {
            string name;

            if (owners != null && type?.Assembly != null && owners.TryGetValue(type.Assembly, out name))
                return name;

            return "RimWorld";
        }

        /// <summary>
        /// Which mod each loaded assembly belongs to.
        ///
        /// Built once on the worker rather than looked up per type, because this is asked several thousand times
        /// and the answer cannot change while the game is running.
        /// </summary>
        private static Dictionary<Assembly, string> BuildOwners()
        {
            Dictionary<Assembly, string> map = new Dictionary<Assembly, string>();

            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;

            if (mods == null)
                return map;

            foreach (ModContentPack mod in mods)
            {
                List<Assembly> loaded = mod?.assemblies?.loadedAssemblies;

                if (loaded == null)
                    continue;

                foreach (Assembly assembly in loaded)
                {
                    if (assembly != null && !map.ContainsKey(assembly))
                        map[assembly] = mod.Name;
                }
            }

            return map;
        }
    }

    /// <summary>
    /// Starts the pre-warm at the last moment before the constructors would have run.
    ///
    /// Patched by name because <c>DoPlayLoad</c> is private. That is fine to depend on: it is where the whole
    /// load is written, so it does not move without the load being rewritten around it.
    /// </summary>
    [HarmonyPatch(typeof(PlayDataLoader), "DoPlayLoad")]
    internal static class Patch_DoPlayLoad_Prewarm
    {
        public static void Postfix()
        {
            StaticConstructorPrewarm.RunFromWorker();
        }
    }

    /// <summary>
    /// Drives the pre-warm from the main thread.
    ///
    /// <c>LongEventsUpdate</c> is called from <c>Root.Update</c> every frame and is already running throughout
    /// the asynchronous load, which is what makes it the pump: no new update loop, and no question about which
    /// thread it is on.
    /// </summary>
    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsUpdate))]
    internal static class Patch_LongEventsUpdate_Prewarm
    {
        public static void Postfix()
        {
            StaticConstructorPrewarm.Pump();
        }
    }
}
