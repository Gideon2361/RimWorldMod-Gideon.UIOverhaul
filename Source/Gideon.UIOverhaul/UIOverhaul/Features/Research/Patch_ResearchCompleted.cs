using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// Turns the research completion popup into a letter.
    ///
    /// <b>What the popup costs.</b> <c>ResearchManager.FinishProject</c> ends in a <c>Dialog_NodeTree</c>, which
    /// is modal: it takes the keyboard, it stops every other window responding until it is dismissed, and it
    /// arrives on the game's clock rather than the player's. A colony with three benches finishes something
    /// several times a day, and none of those moments is chosen. Nothing in the dialog is urgent -- it names the
    /// project and repeats the description already written on that project's page.
    ///
    /// <b>Why a letter of ours rather than the one the game already has.</b> Vanilla does send a letter on
    /// completion, but only for a project carrying a <c>discoveredLetterTitle</c>, and eight of the hundred and
    /// sixty-four projects in the game and its expansions carry one. Suppressing the dialog and leaning on that
    /// would leave the other hundred and fifty-six finishing in silence, which is a worse outcome than the popup
    /// this replaces. So the popup is suppressed and a letter is sent in its place.
    ///
    /// <b>A plain letter and not a <c>ChoiceLetter</c> subclass.</b> Letters live in the save. A letter class of
    /// ours sitting in the letter stack becomes a missing type the moment somebody removes this mod, which turns
    /// a cosmetic setting into a broken save. The dialog's one useful control was its Research Screen button, and
    /// the cost of dropping it is one click on a tab that is always on the bar.
    ///
    /// <b>The suppression is a prefix and the letter is a postfix, on purpose.</b> Sending the letter before the
    /// body runs would announce a project whose unlocks are not applied yet, and the body can still fail. It is
    /// also why the flag is stashed rather than read again: the body is free to look at its own argument, and the
    /// postfix has to know what the argument was <em>before</em> we changed it.
    ///
    /// <b>The recursion is left alone.</b> <c>FinishProject</c> finishes any unfinished prerequisite first,
    /// passing the same flag down, so completing a deep project through dev mode produces one letter per project.
    /// That is the same count of announcements vanilla makes -- it stacks that many dialogs -- and a stack of
    /// letters is the better end of that trade.
    /// </summary>
    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
    internal static class Patch_ResearchCompleted
    {
        /// <summary>
        /// Takes the dialog away, and remembers that there was one to take.
        ///
        /// Not guarded: it reads one setting and writes one argument, and <see cref="UIGuard"/> around a prefix
        /// that assigns a ref parameter would have to decide what the parameter is on failure. Leaving the flag
        /// as the game set it is what happens if this never runs at all, which is the right failure.
        /// </summary>
        private static void Prefix(ref bool doCompletionDialog, out bool __state)
        {
            __state = doCompletionDialog;

            if (doCompletionDialog && Quiet())
                doCompletionDialog = false;
        }

        private static void Postfix(ResearchProjectDef proj, bool __state)
        {
            if (!__state || !Quiet())
                return;

            UIGuard.Try("Research.CompletionLetter", () => Announce(proj),
                "One finished research project was not announced. The project is still finished, and the "
                + "research tab shows it as done.");
        }

        /// <summary>
        /// Whether the setting is on. Null-checked, the way every other reader of the file is: the settings come
        /// off disk, and a file that failed to load should leave the game behaving exactly as RimWorld does.
        /// </summary>
        private static bool Quiet()
        {
            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

            return settings != null && settings.quietResearchCompletion;
        }

        private static void Announce(ResearchProjectDef proj)
        {
            if (proj == null)
                return;

            // The project's own letter is bespoke prose written for that discovery, so ours stands down rather
            // than doubling it. The difficulty test is vanilla's: it is what decides whether that letter is sent
            // a few lines further down the method we are standing in.
            if (!proj.discoveredLetterTitle.NullOrEmpty()
                && Find.Storyteller.difficulty.AllowedBy(proj.discoveredLetterDisabledWhen))
                return;

            // The game's own string for this, so the wording matches everywhere else it appears and follows the
            // player's language without a key of ours to translate.
            string label = "ResearchFinished".Translate(proj.LabelCap);
            string text = proj.description.NullOrEmpty() ? label : proj.description;

            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.PositiveEvent);
        }
    }
}
