using System.Collections.Generic;
using System.Linq;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Trade.Comms
{
    /// <summary>
    /// Replaces the comms console's float menu with one entry that opens our directory.
    ///
    /// <b>One option in place of many, rather than the menu suppressed.</b> A right-click on a building offers
    /// everything that building can do, and the console shares that menu with whatever else is under the cursor
    /// -- go here, haul that, a mod's own order. Returning nothing would take the console out of a menu the
    /// player is still using; returning one line keeps the interaction where it has always been and moves only
    /// what happens after the click.
    ///
    /// <b>Options that are not comms targets are passed through untouched.</b> <c>GetFloatMenuOptions</c> ends by
    /// yielding <c>base.GetFloatMenuOptions</c>, which is where a mod's own additions to this building arrive.
    /// Those are kept: they have nothing to do with who you can call, and swallowing them would break a mod that
    /// has never heard of us. Only the per-target lines are folded into the window, and they are recognised by
    /// asking the console for its own target list rather than by reading the strings.
    ///
    /// <b>A solar flare no longer empties the menu.</b> Vanilla's own <c>GetFailureReason</c> returns a single
    /// disabled line and abandons the rest, so during a flare the player cannot see who they would have been able
    /// to call. Ours opens anyway and says why nothing can be dialled, with the directory readable underneath.
    /// The one failure still passed straight through is having no targets at all, because a window listing nobody
    /// is worse than a menu line saying so.
    /// </summary>
    [HarmonyPatch(typeof(Building_CommsConsole), nameof(Building_CommsConsole.GetFloatMenuOptions))]
    internal static class Patch_CommsConsoleMenu
    {
        public static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> options,
            Building_CommsConsole __instance, Pawn myPawn)
        {
            if (!Replace(__instance, myPawn))
            {
                foreach (FloatMenuOption option in options)
                    yield return option;

                yield break;
            }

            yield return Ours(__instance, myPawn);

            // Everything vanilla offered that is not one of the per-target lines. Built outside the loop so the
            // set of labels to drop is computed once rather than per option.
            HashSet<string> targetLabels = Labels(__instance, myPawn);

            foreach (FloatMenuOption option in options)
            {
                if (option == null || targetLabels.Contains(option.Label))
                    continue;

                yield return option;
            }
        }

        /// <summary>
        /// Whether to take the menu over at all.
        ///
        /// Guarded, and false on anything unexpected: a throw here would leave a player unable to use a comms
        /// console, where falling through leaves them with RimWorld's own menu and a line in the log.
        /// </summary>
        private static bool Replace(Building_CommsConsole console, Pawn negotiator)
        {
            return UIGuard.Try("Comms.Redirect", () =>
            {
                if (!TradeWindowSettings.CustomCommsWindow || console == null || negotiator == null)
                    return false;

                // No targets is vanilla's own reason and it is a good one. A window that lists nobody says less
                // than a menu line that says there is nobody.
                return console.GetCommTargets(negotiator).Any();
            }, false, null);
        }

        private static FloatMenuOption Ours(Building_CommsConsole console, Pawn negotiator)
        {
            // Vanilla's own key for this interaction, so the line reads the way the player expects even though
            // what it opens has changed.
            FloatMenuOption option = new FloatMenuOption("OpenCommsConsole".Translate(),
                () => Find.WindowStack.Add(new Dialog_UIComms(console, negotiator)),
                MenuOptionPriority.InitiateSocial);

            return FloatMenuUtility.DecoratePrioritizedTask(option, negotiator, console);
        }

        /// <summary>
        /// The labels of the per-target lines, which are the ones our window replaces.
        ///
        /// <b>Asked of each target rather than pattern-matched on the text.</b> Every one of these strings is
        /// produced by a target's own <c>CommFloatMenuOption</c>, so asking the same targets the same question
        /// yields exactly the same set -- including a modded target whose wording we could not have guessed.
        /// Matching on "CallOnRadio" would have missed those and dropped somebody's unrelated option that
        /// happened to share a prefix.
        /// </summary>
        private static HashSet<string> Labels(Building_CommsConsole console, Pawn negotiator)
        {
            return UIGuard.Try("Comms.MenuLabels", () =>
            {
                HashSet<string> labels = new HashSet<string>();

                foreach (ICommunicable target in console.GetCommTargets(negotiator))
                {
                    FloatMenuOption option = target?.CommFloatMenuOption(console, negotiator);

                    if (option != null && !option.Label.NullOrEmpty())
                        labels.Add(option.Label);
                }

                return labels;
            }, new HashSet<string>(), null);
        }
    }
}
