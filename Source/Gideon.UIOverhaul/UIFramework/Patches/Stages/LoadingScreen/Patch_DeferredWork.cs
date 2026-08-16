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

        /// <summary>Wraps one callback so it reports itself when it runs.</summary>
        internal static Action Wrap(Action action)
        {
            if (action == null)
                return null;

            return delegate
            {
                string owner = OwnerOf(action);

                // Named before it runs, not after: this is the line that is on screen for however long the
                // callback takes, which is the whole point of showing it.
                UIGuard.Try("Stages.DeferredWork.Report",
                    () => UILoadingScreen.Report(null, owner), null);

                Stopwatch watch = Stopwatch.StartNew();

                try
                {
                    action();
                }
                finally
                {
                    watch.Stop();

                    if (watch.ElapsedMilliseconds >= SlowMs)
                    {
                        UIGuard.Try("Stages.DeferredWork.Record",
                            () => UILoadingLog.Record(UILoadingLogKind.Step,
                                owner + " took " + Duration(watch.ElapsedMilliseconds)), null);
                    }
                }
            };
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
        private static string OwnerOf(Action action)
        {
            return UIGuard.Try("Stages.DeferredWork.Owner", () =>
            {
                MethodInfo method = action.Method;
                Type declaring = method?.DeclaringType;

                if (declaring == null)
                    return "Unknown";

                return ModOf(declaring.Assembly) + ": " + declaring.Name + "." + method.Name;
            }, "Unknown", null);
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

            return assembly != null && Owners.TryGetValue(assembly, out name) ? name : "RimWorld";
        }
    }

    /// <summary>
    /// Substitutes a self-reporting wrapper for every deferred callback registered during a load.
    ///
    /// A <c>ref</c> parameter on a prefix is Harmony's supported way to change what the original method receives,
    /// so vanilla adds our wrapper to its queue and is otherwise none the wiser.
    /// </summary>
    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.ExecuteWhenFinished))]
    internal static class Patch_ExecuteWhenFinished_Attribute
    {
        public static void Prefix(ref Action action)
        {
            if (action == null || !UILoadingLog.Active)
                return;

            // Copied out of the ref parameter first: C# will not let a lambda close over one.
            Action original = action;

            Action wrapped = UIGuard.Try("Stages.DeferredWork.Wrap", () => DeferredWork.Wrap(original), null, null);

            // Only substituted when the wrapper was actually built. A failure here must leave the original
            // callback in the queue, because dropping one is a mod that never finishes initializing.
            if (wrapped != null)
                action = wrapped;
        }
    }
}
