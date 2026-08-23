using Gideon.UIFramework.Helpers;
using HarmonyLib;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Notifications
{
    /// <summary>
    /// Watches a mental break start, so <see cref="MentalBreakLetters"/> can say how long it will last.
    ///
    /// <b>A prefix and a postfix around the whole method, rather than a patch on the letter itself.</b> The
    /// decision to send is several conditions deep inside <c>TryStartMentalState</c> and the text comes from
    /// <c>MentalState.GetBeginLetterText</c>, which subclasses override -- patching that would reach the states
    /// that inherit it and silently miss the ones that do not. Counting the letter stack either side of the call
    /// asks the only question that matters, "did one arrive", and cannot drift from whatever the game decides.
    ///
    /// <c>transitionSilently</c> is honoured, so a state swapping straight into another one stays quiet the way
    /// RimWorld intends.
    /// </summary>
    [HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
    internal static class Patch_MentalBreakLetter
    {
        public static void Prefix(out int __state)
        {
            __state = MentalBreakLetters.LetterCount();
        }

        public static void Postfix(bool __result, MentalStateHandler __instance, bool transitionSilently,
            int __state)
        {
            if (!__result || transitionSilently)
                return;

            UIGuard.Try("Breaks.Letter", () => MentalBreakLetters.Announce(__instance, __state),
                "Mental break letters are not being sent for the rest of this session.");
        }
    }
}
