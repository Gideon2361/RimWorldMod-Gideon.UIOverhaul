using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Ideoligions
{
    /// <summary>
    /// Makes Keep the doctrine work at world generation as well as in our own designer.
    ///
    /// <b>The case this exists for.</b> A player loads an ideoligion from a file while setting a game up, then
    /// changes one meme in it. RimWorld's answer is to throw the whole doctrine away and roll a new one --
    /// <c>RandomizePrecepts</c> opens with <c>ideo.ClearPrecepts()</c> -- so the file they carefully built is
    /// gone the moment they adjust it. That happens in <c>Dialog_ChooseMemes</c>, which is vanilla's window and
    /// not one this mod replaces, so the only way to reach it is a patch.
    ///
    /// <b>Three conditions, and the third is the whole safety of it.</b> The randomizers are skipped only when
    /// the switch is on, only while a meme change is actually being accepted, and only when the ideoligion
    /// <i>already has precepts</i>. That last one is what the player asked for and it is not a detail: an
    /// ideoligion with an empty precept list has nothing to preserve, and skipping generation for it is exactly
    /// how you end up with a faith that believes nothing. <c>Persistent Precepts</c>, the mod this feature was
    /// suggested from, warns about that failure in its own description -- leave its toggle on, start a new game,
    /// and your faction generates with no precepts at all. Asking whether there is anything to keep before
    /// keeping it makes that outcome unreachable rather than merely unlikely.
    ///
    /// <b>Vanilla already branches on the same question,</b> which is what makes this safe rather than clever.
    /// <c>Dialog_ChooseMemes.DoAcceptChanges</c> calls <c>RandomizePrecepts(init: true)</c> when precepts exist
    /// and runs a full generation path when they do not. Reading <c>PreceptsListForReading</c> on the foundation's
    /// own ideoligion asks that same question directly, rather than inferring it from the <c>init</c> flag.
    ///
    /// <b>Scoped to the accept, not left switched on.</b> The flag is raised in a prefix and dropped in a
    /// finalizer, so it comes down even if the method it wraps throws. Nothing outside that call ever sees it,
    /// which is what keeps ordinary world generation, quest ideoligions and faction generation untouched.
    ///
    /// <b>Our own designer does not go through any of this.</b> It never calls <c>RandomizePrecepts</c> at all --
    /// reforming is the short path -- and reads the switch directly in <see cref="IdeoDraft"/>.
    /// </summary>
    internal static class PreserveDoctrine
    {
        /// <summary>True only while <c>Dialog_ChooseMemes</c> is accepting a meme change.</summary>
        private static bool accepting;

        /// <summary>
        /// Whether a randomizer called right now would be destroying something worth keeping.
        ///
        /// The foundation's ideoligion rather than a parameter, because both patched methods are on
        /// <c>IdeoFoundation</c> and neither is handed the ideoligion it belongs to.
        /// </summary>
        internal static bool Skip(IdeoFoundation foundation)
        {
            return UIGuard.Try("Ideoligions.PreserveSkip", () =>
            {
                if (!accepting || !IdeoDraft.Preserve)
                    return false;

                Ideo ideo = foundation?.ideo;

                // Nothing generated or loaded yet: there is no doctrine to preserve, and skipping here is what
                // produces a faith with no precepts. Generate normally, switch or no switch.
                return ideo != null && ideo.PreceptsListForReading.Count > 0;
            }, false, null);
        }

        internal static void Enter()
        {
            accepting = true;
        }

        internal static void Leave()
        {
            accepting = false;
        }
    }

    /// <summary>
    /// Marks the window in which a meme change is being committed.
    ///
    /// <b>A finalizer rather than a postfix for the second half,</b> because a postfix does not run when the
    /// method throws, and a flag left raised by an exception would silently disable precept generation for the
    /// rest of the session.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ChooseMemes), "DoAcceptChanges")]
    internal static class Patch_ChooseMemes_Accept
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            UIGuard.Try("Ideoligions.PreserveEnter", PreserveDoctrine.Enter, null);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            UIGuard.Try("Ideoligions.PreserveLeave", PreserveDoctrine.Leave, null);
        }
    }

    /// <summary>
    /// Leaves the doctrine alone when the player has asked for it and there is a doctrine to leave alone.
    ///
    /// Skipping the original is what preserves it: <c>RandomizePrecepts</c> clears every precept before it rolls
    /// new ones, so there is no way to let it run and keep anything.
    /// </summary>
    [HarmonyPatch(typeof(IdeoFoundation), nameof(IdeoFoundation.RandomizePrecepts))]
    internal static class Patch_Foundation_RandomizePrecepts
    {
        [HarmonyPrefix]
        public static bool Prefix(IdeoFoundation __instance)
        {
            return !PreserveDoctrine.Skip(__instance);
        }
    }

    /// <summary>
    /// The same for the style categories, which a loaded ideoligion also carries.
    ///
    /// <c>DoAcceptChanges</c> re-rolls these on the same breath as the precepts, so preserving one and not the
    /// other would keep a faith's doctrine and still hand it somebody else's architecture and apparel.
    /// </summary>
    [HarmonyPatch(typeof(IdeoFoundation), nameof(IdeoFoundation.RandomizeStyles))]
    internal static class Patch_Foundation_RandomizeStyles
    {
        [HarmonyPrefix]
        public static bool Prefix(IdeoFoundation __instance)
        {
            return !PreserveDoctrine.Skip(__instance);
        }
    }
}
