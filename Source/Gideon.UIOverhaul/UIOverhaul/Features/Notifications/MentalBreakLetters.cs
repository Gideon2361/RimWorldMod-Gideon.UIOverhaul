using System.Reflection;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Notifications
{
    /// <summary>
    /// Every mental break raises a letter, and every one of those letters says how long it is expected to last.
    ///
    /// <b>Two gaps, and they are different gaps.</b> RimWorld only writes a letter for a break whose state class
    /// returns something from <c>GetBeginLetterText</c>, so a colonist can wander off in a daze with nothing on
    /// the stack to say so. And none of the letters it does send says how long the break runs, which is the one
    /// thing you need to decide whether to arrest them, wait, or go and fix the room.
    ///
    /// <b>The duration is quoted as a range because it genuinely is one.</b> <c>MentalState.MentalStateTick</c>
    /// ends a state when the age passes <c>maxTicksBeforeRecovery</c>, or past <c>minTicksBeforeRecovery</c> on a
    /// mean-time-between roll against <c>recoveryMtbDays</c>, or at <c>forceRecoverAfterTicks</c> when something
    /// set one. So there is an earliest, a latest, and a coin flip in between, and saying "lasts four hours"
    /// would be inventing a certainty the game does not have.
    /// </summary>
    internal static class MentalBreakLetters
    {
        /// <summary>
        /// Beyond this a state has no meaningful ceiling, so the letter stops quoting one.
        ///
        /// Several states leave <c>maxTicksBeforeRecovery</c> at its default, which is far past any span worth
        /// putting in a sentence; "lasts between two hours and sixty days" is a worse answer than "no fixed end".
        /// </summary>
        private const int NoCeiling = GenDate.TicksPerYear;

        /// <summary>Reads the private pawn off the handler. Resolved once, and null if RimWorld renames it.</summary>
        private static FieldInfo handlerPawn;

        private static bool looked;

        internal static bool Enabled
        {
            get
            {
                return UIGuard.Try("Breaks.ReadEnabled",
                    () => UIOverhaulSettingsFile.Current?.mentalBreakLetters ?? true, true,
                    "Mental break letters are on, which is the default.");
            }
        }

        /// <summary>How many letters are on the stack, so the postfix can tell whether vanilla sent one.</summary>
        internal static int LetterCount()
        {
            return UIGuard.Try("Breaks.LetterCount",
                () => Find.LetterStack != null ? Find.LetterStack.LettersListForReading.Count : 0, 0, null);
        }

        /// <summary>
        /// Amends the letter vanilla just sent, or sends one where it sent nothing.
        /// </summary>
        /// <param name="before">
        /// The letter count taken in the prefix. <b>Counting is how this knows,</b> rather than guessing from the
        /// state def: the decision to send is several conditions deep inside
        /// <c>TryStartMentalState</c> and reproducing it here would be a copy that drifts. Nothing else runs
        /// between the prefix and the postfix, so a stack that grew grew by exactly this break's letter.
        /// </param>
        internal static void Announce(MentalStateHandler handler, int before)
        {
            if (!Enabled)
                return;

            UIGuard.Try("Breaks.Announce", () =>
            {
                Pawn pawn = PawnOf(handler);

                if (pawn == null)
                    return;

                MentalState state = pawn.MentalState;

                if (state == null || state.def == null)
                    return;

                // Vanilla's own audience test, reused rather than restated: it is what keeps a raider's tantrum
                // off the stack while a colonist's lands on it.
                if (!PawnUtility.ShouldSendNotificationAbout(pawn))
                    return;

                string duration = Duration(state);

                int after = LetterCount();

                if (after > before)
                {
                    Append(after - 1, duration);

                    return;
                }

                Send(pawn, state, duration);
            }, "Mental break letters are not being sent.");
        }

        /// <summary>Adds the duration line to a letter RimWorld has already put on the stack.</summary>
        private static void Append(int index, string duration)
        {
            Letter letter = Find.LetterStack.LettersListForReading[index];

            ChoiceLetter choice = letter as ChoiceLetter;

            // Only a ChoiceLetter carries body text; anything else is a label and a target, and there is nowhere
            // to put a sentence. The backing field is private and the property has a setter that also rebuilds
            // the letter's hyperlinks, so this goes through Text rather than round the back of it.
            if (choice == null)
                return;

            choice.Text = choice.Text + "\n\n" + duration;
        }

        /// <summary>
        /// Sends a letter for a break that stayed silent.
        ///
        /// <b>Labelled exactly the way RimWorld labels its own,</b> so the two kinds sit together on the stack
        /// rather than reading as a different feature: the state's begin-letter label or its own name, then the
        /// pawn. The letter def is the state's own where it has one, so a break the game considers a threat
        /// still arrives as a threat.
        /// </summary>
        private static void Send(Pawn pawn, MentalState state, string duration)
        {
            string label = (state.def.beginLetterLabel ?? state.def.LabelCap.ToString()).CapitalizeFirst()
                           + ": " + pawn.LabelShortCap;

            string body = pawn.LabelShortCap + " has broken down: " + state.def.LabelCap.ToString().UncapitalizeFirst()
                          + ".";

            string line = UIGuard.Try<string>("Breaks.InspectLine", () => state.InspectLine, null, null);

            if (!line.NullOrEmpty())
                body += "\n\n" + line;

            Find.LetterStack.ReceiveLetter(label, body + "\n\n" + duration,
                state.def.beginLetterDef ?? LetterDefOf.NegativeEvent, pawn);
        }

        /// <summary>
        /// How long this break has left, in the terms the game actually decides it by.
        ///
        /// Three shapes, because there are three cases: a state with a hard ceiling and no early exit lasts a
        /// known time; one with both bounds lasts somewhere between them; one with no ceiling ends on a roll and
        /// can only be described by its average.
        /// </summary>
        private static string Duration(MentalState state)
        {
            int age = Mathf.Max(0, state.Age);

            int ceiling = state.def.maxTicksBeforeRecovery;

            // Whatever set this wins, since the tick loop honours it regardless of the def's own ceiling.
            if (state.forceRecoverAfterTicks >= 0)
                ceiling = Mathf.Min(ceiling, state.forceRecoverAfterTicks);

            int earliest = Mathf.Max(0, state.def.minTicksBeforeRecovery - age);
            int latest = Mathf.Max(0, ceiling - age);

            if (ceiling >= NoCeiling)
            {
                float mtb = state.def.recoveryMtbDays;

                if (mtb <= 0f)
                    return "No fixed end. It passes when it passes.";

                return "No fixed end. Breaks like this last about "
                       + Mathf.RoundToInt(mtb * GenDate.TicksPerDay).ToStringTicksToPeriod(false, false, false)
                       + " on average.";
            }

            if (earliest >= latest)
                return "Ends in " + latest.ToStringTicksToPeriod(false, false, false) + ".";

            return "Ends in between " + earliest.ToStringTicksToPeriod(false, false, false) + " and "
                   + latest.ToStringTicksToPeriod(false, false, false) + ".";
        }

        private static Pawn PawnOf(MentalStateHandler handler)
        {
            if (!looked)
            {
                looked = true;

                handlerPawn = typeof(MentalStateHandler).GetField("pawn",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            }

            return handlerPawn == null ? null : handlerPawn.GetValue(handler) as Pawn;
        }
    }
}
