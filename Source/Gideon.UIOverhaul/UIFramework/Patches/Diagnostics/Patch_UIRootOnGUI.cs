using System;
using System.Collections.Generic;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIFramework.Patches.Diagnostics
{
    /// <summary>
    /// Watches for exceptions leaving RimWorld's UI root and names who was involved.
    ///
    /// <b>A finalizer, which is the only Harmony patch kind that can see this.</b> A prefix runs before the fault
    /// and a postfix does not run at all once one has happened. A finalizer runs on both paths and is handed the
    /// exception if there was one, which is exactly and only what is wanted here.
    ///
    /// <b>The exception is passed straight through.</b> Returning null from a finalizer swallows it, and that is
    /// the one thing this must not do: Unity's own handler is what puts the error and its stack in the log, and a
    /// diagnostic that suppresses the fault it is describing has removed the evidence. Returning the exception
    /// unchanged is a rethrow, so the frame fails exactly as it would have and gains one line of commentary.
    ///
    /// <b>Why the UI root rather than a hundred individual seams.</b> Every piece of interface RimWorld draws
    /// passes through here -- the map overlays, the main tabs, every window on the stack, every mod's addition to
    /// any of them. One patch at the top sees all of it. The cost is that the attribution is coarse, which is why
    /// <see cref="UIExceptionAttribution"/> is careful to present its list as somewhere to look rather than as a
    /// culprit.
    ///
    /// <b>All three implementations are patched, and they share a site name on purpose.</b>
    /// <c>UIRoot_Play.UIRootOnGUI</c> calls <c>base.UIRootOnGUI</c>, so an exception thrown in the base method
    /// passes through two finalizers and would otherwise be reported twice per frame. One site plus
    /// <c>UIGuard</c>'s signature matching makes the second pass a repeat of the first and silences it.
    ///
    /// <b>This costs nothing while nothing is wrong.</b> A finalizer with no exception to handle returns
    /// immediately; the stack walking, the mod list lookup and the Harmony queries all sit behind that check.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_UIRootOnGUI
    {
        /// <summary>
        /// One name for all three overrides, so a single throw is one report rather than one per inherited call.
        /// </summary>
        private const string Site = "Framework.UIRoot";

        /// <summary>
        /// Every <c>UIRootOnGUI</c> that declares a body: the base, the in-game root and the menu root.
        ///
        /// <b>Named through <c>typeof</c> rather than looked up by string,</b> which was the first version and was
        /// quietly wrong: <c>UIRoot_Play</c> is in <c>RimWorld</c> and <c>UIRoot_Entry</c> is in <c>Verse</c>, and
        /// a lookup by the wrong namespace returns null and skips that root without a word. All three types are
        /// public, so the compiler can check them, and a rename in a future RimWorld becomes a build error rather
        /// than a diagnostic that silently stopped covering the main menu.
        ///
        /// Only the three the game ships. A mod is free to add its own <c>UIRoot</c>, and patching types this mod
        /// has never heard of is not a thing to go looking for while building a diagnostic.
        /// </summary>
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type type in new[] { typeof(UIRoot), typeof(UIRoot_Play), typeof(UIRoot_Entry) })
            {
                // DeclaredMethod rather than Method: the inherited one resolves to the base for both subclasses,
                // which would hand Harmony the same method three times.
                MethodBase method = AccessTools.DeclaredMethod(type, "UIRootOnGUI");

                if (method != null)
                    yield return method;
            }
        }

        public static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
                UIExceptionAttribution.Note(Site, __exception);

            // Unchanged, which rethrows. See the class notes: swallowing this would delete the error report it
            // exists to annotate.
            return __exception;
        }
    }
}
