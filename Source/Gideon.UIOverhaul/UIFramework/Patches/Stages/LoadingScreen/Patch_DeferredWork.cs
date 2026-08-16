using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using Gideon.UIFramework.Stages;
using HarmonyLib;
using Verse;

namespace Gideon.UIFramework.Patches.Stages.LoadingScreen
{
    /// <summary>
    /// Names the deferred callbacks that run at the end of loading, and times each one.
    ///
    /// <b>This is where a modded load actually goes.</b> <c>LongEventHandler.ExecuteWhenFinished</c> is how a mod
    /// says "do this once loading is done", and every mod that needs a texture, an atlas or a resolved def uses
    /// it. They all queue during their own constructors and then drain, one after another, in a single
    /// uninterrupted pass -- reported as one word on the loading screen, because from the outside it is one
    /// method call. Measured on a heavy list this stretch was over two minutes, against three and a half seconds
    /// for every static constructor in every mod combined.
    ///
    /// <b>Wrapping the registration rather than replacing the drain.</b> The obvious approach is to patch
    /// <c>ExecuteToExecuteWhenFinished</c> and run its loop ourselves, which means owning vanilla's control flow
    /// and its exception handling forever. This does the opposite: a prefix on <c>ExecuteWhenFinished</c> takes
    /// the action by reference and substitutes a wrapper around it. Vanilla's queue, ordering, error handling and
    /// re-entrancy are all untouched -- it simply invokes a delegate that happens to start a stopwatch first.
    ///
    /// <b>Attribution comes free with the delegate.</b> An <c>Action</c> carries the method it points at, and a
    /// method carries its declaring type and therefore its assembly, so the owning mod can be named without any
    /// cooperation from it.
    ///
    /// <b>Only while a load is being recorded.</b> <c>ExecuteWhenFinished</c> is used during play as well, and
    /// wrapping every call for the life of a colony would be a permanent cost for an answer nobody is reading.
    /// </summary>
    internal static class DeferredWork
    {
        /// <summary>A callback slower than this is worth a line of its own in the console.</summary>
        private const long SlowMs = 100;

        private static readonly Dictionary<Assembly, string> Owners = new Dictionary<Assembly, string>();

        private static bool ownersBuilt;

        /// <summary>
        /// The callbacks too fast to be worth a line each, counted rather than listed.
        ///
        /// <b>A row per callback would bury the ones that matter.</b> A heavy list registers several hundred of
        /// these and nearly all of them are instant, so listing them turns the phase that owns most of the load
        /// into a wall of noise with three interesting rows hidden in it. Counted and summed, they become one
        /// line that still answers the question they can answer: whether the time is in a few slow callbacks or
        /// spread thinly across all of them.
        /// </summary>
        private static int fastCount;

        private static long fastMilliseconds;

        /// <summary>Wraps one callback so it reports itself when it runs.</summary>
        internal static Action Wrap(Action action)
        {
            if (action == null)
                return null;

            return delegate
            {
                string owner = OwnerOf(action);

                // <b>Only reported when a mod owns it, and that is not cosmetic.</b> UILoadingScreen records
                // whatever step text it is given straight into the log, so reporting "RimWorld" for the base
                // game's own callbacks wrote a row for every one of them. Deduplication turned those into
                // "RimWorld x4385" lines that buried the mod rows this exists to surface. Leaving the previous
                // label up during vanilla's callbacks costs nothing: they are fast and nameless anyway.
                if (owner != null)
                {
                    UIGuard.Try("Stages.DeferredWork.Report",
                        () => UILoadingScreen.Report(null, owner), null);
                }

                Stopwatch watch = Stopwatch.StartNew();

                try
                {
                    action();
                }
                finally
                {
                    watch.Stop();

                    // <b>Only a mod's callback earns a line.</b> A great many of these belong to RimWorld
                    // itself and are compiler generated lambdas -- BuildableDef.&lt;PostLoad&gt;b__78_0 and its
                    // kind -- which name nothing a reader can act on and, being numerous, push the handful of
                    // mod rows that do matter off the screen. Their time is still counted in the summary below,
                    // so the total stays honest; it simply is not itemized.
                    bool worthListing = owner != null && watch.ElapsedMilliseconds >= SlowMs;

                    if (worthListing)
                    {
                        UIGuard.Try("Stages.DeferredWork.Record",
                            () => UILoadingLog.Record(UILoadingLogKind.Step,
                                owner + " took " + Duration(watch.ElapsedMilliseconds)), null);
                    }
                    else
                    {
                        fastCount++;
                        fastMilliseconds += watch.ElapsedMilliseconds;
                    }
                }
            };
        }

        /// <summary>
        /// Closes off a drain by reporting whatever was too fast to list.
        ///
        /// Emitted at the end rather than the start, so the line lands under the slow callbacks it is the
        /// remainder of, and reads as the tail of that group.
        /// </summary>
        internal static void Summarize()
        {
            if (fastCount <= 0)
                return;

            int count = fastCount;
            long total = fastMilliseconds;

            fastCount = 0;
            fastMilliseconds = 0;

            UIGuard.Try("Stages.DeferredWork.Summarize",
                () => UILoadingLog.Record(UILoadingLogKind.Step,
                    count + " further callbacks not itemized (fast ones, and the game's own), totalling "
                    + Duration(total)), null);
        }

        private static string Duration(long milliseconds)
        {
            return milliseconds >= 1000
                ? (milliseconds / 1000f).ToString("F2") + "s"
                : milliseconds + "ms";
        }

        /// <summary>
        /// Which mod a callback belongs to, and what it does.
        ///
        /// The method name is included because a mod frequently queues several of these, and "which of Vanilla
        /// Expanded Framework's six callbacks" is the next question after "which mod". Compiler generated names
        /// are ugly but they are still distinct, which is what matters here.
        /// </summary>
        /// <returns>The mod and method, or null when the callback does not belong to a mod.</returns>
        private static string OwnerOf(Action action)
        {
            return UIGuard.Try("Stages.DeferredWork.Owner", () =>
            {
                MethodInfo method = action.Method;
                Type declaring = method?.DeclaringType;

                if (declaring == null)
                    return null;

                string mod = ModOf(declaring.Assembly);

                if (mod == null)
                    return null;

                // Climb out of the compiler's closure classes. A lambda that captures a local is emitted into a
                // nested type named <>c__DisplayClass1_0, and one that captures nothing into <>c; neither says
                // anything, and the type the reader wants is the one they are nested inside.
                while (declaring != null && declaring.Name.StartsWith("<>", StringComparison.Ordinal)
                                         && declaring.DeclaringType != null)
                {
                    declaring = declaring.DeclaringType;
                }

                return mod + ": " + declaring.Name + "." + Readable(method.Name);
            }, null, null);
        }

        /// <summary>
        /// A method name a person can read.
        ///
        /// Most of these are lambdas, and the compiler names those <c>&lt;PostLoad&gt;b__78_0</c>: the useful
        /// half is the enclosing method in the angle brackets, and the rest is a counter. Taking the inner name
        /// turns that into <c>PostLoad</c>, which says what the callback is for.
        /// </summary>
        private static string Readable(string name)
        {
            if (name == null)
                return string.Empty;

            int open = name.IndexOf('<');
            int close = name.IndexOf('>');

            return open == 0 && close > 1 ? name.Substring(1, close - 1) : name;
        }

        private static string ModOf(Assembly assembly)
        {
            if (!ownersBuilt)
            {
                ownersBuilt = true;

                List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;

                if (mods != null)
                {
                    foreach (ModContentPack mod in mods)
                    {
                        List<Assembly> loaded = mod?.assemblies?.loadedAssemblies;

                        if (loaded == null)
                            continue;

                        foreach (Assembly loadedAssembly in loaded)
                        {
                            if (loadedAssembly != null && !Owners.ContainsKey(loadedAssembly))
                                Owners[loadedAssembly] = mod.Name;
                        }
                    }
                }
            }

            string name;

            // Null rather than "RimWorld", so the caller can tell "this belongs to a mod" from "this is the
            // base game" and list only the former.
            return assembly != null && Owners.TryGetValue(assembly, out name) ? name : null;
        }
    }

    /// <summary>
    /// Wraps the whole queue immediately before it drains.
    ///
    /// <b>Wrapping at registration did not work, and the reason is load order.</b> The first version prefixed
    /// <c>ExecuteWhenFinished</c> and substituted a wrapper as each callback was queued. That only caught
    /// callbacks registered after our own Harmony patches were applied, and mods register theirs from their
    /// constructors, most of which run before ours. On a large list nearly every callback was already sitting in
    /// the queue by the time we were watching, so almost nothing was attributed.
    ///
    /// Wrapping here catches all of them however early they were queued, and still does not take ownership of
    /// vanilla's loop: the list is rewritten in place and the drain then runs exactly as written.
    ///
    /// <b>Rewritten, not replaced.</b> Vanilla walks the field by index and appends to it when a callback
    /// registers another during the drain, so swapping in a different list would silently lose those.
    /// </summary>
    [HarmonyPatch(typeof(LongEventHandler), "ExecuteToExecuteWhenFinished")]
    internal static class Patch_ExecuteToExecuteWhenFinished_Wrap
    {
        // The FieldInfo overload rather than the name one, because it returns a delegate that can be tested for
        // null. The name overload returns a ref, which cannot be checked before it is used.
        private static readonly AccessTools.FieldRef<List<Action>> Queue = BuildAccessor();

        private static AccessTools.FieldRef<List<Action>> BuildAccessor()
        {
            FieldInfo field = AccessTools.Field(typeof(LongEventHandler), "toExecuteWhenFinished");

            return field == null ? null : AccessTools.StaticFieldRefAccess<List<Action>>(field);
        }

        public static void Prefix()
        {
            if (Queue == null || !UILoadingLog.Active)
                return;

            UIGuard.Try("Stages.DeferredWork.WrapQueue", () =>
            {
                List<Action> queue = Queue();

                if (queue == null)
                    return;

                for (int i = 0; i < queue.Count; i++)
                {
                    Action wrapped = DeferredWork.Wrap(queue[i]);

                    // Left alone when wrapping failed. Dropping an entry is a mod that never finishes
                    // initializing, which is far worse than losing a line of attribution.
                    if (wrapped != null)
                        queue[i] = wrapped;
                }
            }, "Deferred mod callbacks are not attributed. The load itself is unaffected.");
        }
    }

    /// <summary>
    /// Reports the fast remainder once a drain has finished.
    ///
    /// <b>Patched by name because the method is private,</b> which is fine to depend on here: it is the only
    /// thing that runs the deferred queue, so it does not move without the queue being rewritten around it. The
    /// public <c>ForceExecuteToExecuteWhenFinished</c> calls straight into it, so both routes are covered.
    ///
    /// A postfix only. Nothing about the drain changes; this simply notices that it ended.
    /// </summary>
    [HarmonyPatch(typeof(LongEventHandler), "ExecuteToExecuteWhenFinished")]
    internal static class Patch_ExecuteToExecuteWhenFinished_Summary
    {
        public static void Postfix()
        {
            if (UILoadingLog.Active)
                DeferredWork.Summarize();
        }
    }
}
