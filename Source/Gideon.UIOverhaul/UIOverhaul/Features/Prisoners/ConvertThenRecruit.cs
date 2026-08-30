using System.Collections.Generic;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Prisoners
{
    /// <summary>
    /// A prisoner mode that converts first and recruits afterwards.
    ///
    /// <b>It is the two-step every ideoligion player already does by hand.</b> A prisoner of a hostile ideoligion
    /// resists recruitment far harder than a convert does, so the patient route is Convert, notice it took, switch
    /// to Recruit. The noticing is the problem: nothing tells you, the warden quietly stops having work, and the
    /// prisoner sits in a cell converted and unrecruited until you happen to click them. Asked for on 2026-08-25.
    ///
    /// <b>It is a real mode rather than a flag on Convert,</b> because the prisoner tab is a radio list and the
    /// two children already there -- reduce resistance only, reduce will only -- are modes. A checkbox hanging off
    /// Convert would have been a third kind of control in a list of one kind.
    ///
    /// <b>And it behaves exactly like Convert while it runs.</b> Nothing about conversion is reimplemented: the
    /// work giver, the job driver, the interaction worker and the certainty arithmetic are all vanilla's, reached
    /// by answering its own question -- <c>IsInteractionEnabled(Convert)</c> -- with yes. What this adds is the
    /// moment afterwards.
    /// </summary>
    [DefOf]
    public static class ConvertThenRecruitDefOf
    {
        /// <summary>
        /// Null without Ideology, and that is the arrangement rather than a hole in it.
        ///
        /// The def carries <c>MayRequire</c> for the same expansion, so it is genuinely absent in a game without
        /// it -- and <c>DefOfHelper</c> would log an error every startup for a field it could not fill if this
        /// attribute did not tell it the absence is expected. Every patch below reads the field rather than
        /// caching it, so null simply means none of them ever match.
        /// </summary>
        [MayRequireIdeology]
        public static PrisonerInteractionModeDef Gideon_ConvertThenRecruit;

        static ConvertThenRecruitDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ConvertThenRecruitDefOf));
        }
    }

    /// <summary>
    /// The two pawn back-references this feature needs, both of which their trackers keep private.
    ///
    /// <b>Reflected once and held, rather than looked up per call.</b> One of these is read inside a postfix on
    /// every ideoligion change in the game, so the lookup cost would be paid for every pawn in every ritual.
    /// </summary>
    internal static class PrisonerReflection
    {
        private static readonly FieldInfo GuestPawn = AccessTools.Field(typeof(Pawn_GuestTracker), "pawn");

        private static readonly FieldInfo IdeoPawn = AccessTools.Field(typeof(Pawn_IdeoTracker), "pawn");

        /// <summary>Whether both fields were found. Neither patch does anything without them.</summary>
        internal static bool Available => GuestPawn != null && IdeoPawn != null;

        internal static Pawn Of(Pawn_GuestTracker tracker)
        {
            return tracker == null || GuestPawn == null ? null : GuestPawn.GetValue(tracker) as Pawn;
        }

        internal static Pawn Of(Pawn_IdeoTracker tracker)
        {
            return tracker == null || IdeoPawn == null ? null : IdeoPawn.GetValue(tracker) as Pawn;
        }
    }

    /// <summary>
    /// Makes every question vanilla asks about the Convert mode answer yes for this one.
    ///
    /// <b>One patch covers the whole conversion pipeline,</b> because vanilla asks the same question in each
    /// place: <c>WorkGiver_Warden_Convert</c> twice, and the prisoner tab once when deciding whether to show the
    /// which-ideoligion picker. <c>JobDriver_ConvertPrisoner</c> needs nothing -- it hands the mode to
    /// <c>Toils_Interpersonal.GotoPrisoner</c>, which reads only <c>mustBeAwake</c> off it and never compares it
    /// to the prisoner's own.
    ///
    /// <b>Only ever widening, never narrowing.</b> The postfix can turn a no into a yes and does nothing else, so
    /// no prisoner stops doing something they were doing because this loaded.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.IsInteractionEnabled))]
    internal static class Patch_ConvertThenRecruitEnabled
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_GuestTracker __instance, PrisonerInteractionModeDef def, ref bool __result)
        {
            if (__result || def != PrisonerInteractionModeDefOf.Convert)
                return;

            bool ours = false;

            if (UIGuard.Try("Prisoners.ConvertEnabled",
                    () => ours = __instance.ExclusiveInteractionMode
                                 == ConvertThenRecruitDefOf.Gideon_ConvertThenRecruit))
            {
                __result = ours;
            }
        }
    }

    /// <summary>
    /// Gives the mode a target ideoligion the moment it is chosen, as Convert does.
    ///
    /// Without this the mode is picked, <c>ideoForConversion</c> stays null, and the work giver's
    /// <c>pawn.Ideo == pawn2.guest.ideoForConversion</c> is false for every warden alive -- so the prisoner sits
    /// and nobody can say why. Vanilla seeds the primary ideoligion for Convert in the tab's own private
    /// <c>InteractionModeChanged</c>; this does the same thing rather than a different one, so a player switching
    /// between the two modes does not find the target silently changed.
    ///
    /// <b>Hooked on the setter rather than on the tab.</b> <c>SetExclusiveInteraction</c> is where the mode
    /// actually changes, so this catches the tab, a dev tool, and any mod that sets it -- and it needs no access
    /// to a private UI method that exists to serve one window.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.SetExclusiveInteraction))]
    internal static class Patch_ConvertThenRecruitChosen
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_GuestTracker __instance, PrisonerInteractionModeDef def)
        {
            if (def != ConvertThenRecruitDefOf.Gideon_ConvertThenRecruit)
                return;

            UIGuard.Try("Prisoners.ConvertTarget", () =>
            {
                if (__instance.ideoForConversion == null && Faction.OfPlayer?.ideos != null)
                    __instance.ideoForConversion = Faction.OfPlayer.ideos.PrimaryIdeo;

                ConvertThenRecruitHandover.Apply(__instance, true);
            }, "This prisoner has no ideoligion to be converted to, so no warden will come.");
        }
    }

    /// <summary>
    /// Hands a prisoner over to recruitment when there is no conversion left to do.
    ///
    /// <b>Three moments need this, and the first version only had one.</b> The mode being chosen, an ideoligion
    /// changing, and a save being loaded. Picking the mode on a prisoner who already believes handed over
    /// correctly; loading a save in exactly that state did not, because nothing had happened to notice --
    /// the prisoner sat in a converted-and-unrecruited state that no warden will work on, and only toggling the
    /// mode by hand woke it up. Reported 2026-08-29.
    ///
    /// <b>Why the mode is switched rather than made to behave like Recruit.</b> <c>WorkGiver_Warden_Chat</c>
    /// compares <c>ExclusiveInteractionMode</c> against Recruit and Reduce resistance by reference, not through
    /// <c>IsInteractionEnabled</c>, so the trick that makes conversion work here cannot be repeated for
    /// recruitment. The only way to satisfy that comparison is to patch the property itself -- and the prisoner
    /// tab picks its radio button with the same property, so a prisoner would silently show Recruit selected
    /// instead of the mode the player chose. Switching the mode for real is the honest version of the same
    /// outcome, and it leaves the tab telling the truth.
    /// </summary>
    internal static class ConvertThenRecruitHandover
    {
        /// <summary>
        /// Switches one prisoner to recruitment if their ideoligion already matches the target.
        /// </summary>
        /// <param name="announce">
        /// Whether to say so. True when the player just picked the mode, because the radio button moves under
        /// their cursor and a control that changes itself without a word looks like a misclick. False on load,
        /// where a colony holding six such prisoners would open on six messages about a state that was already
        /// true when they saved.
        /// </param>
        internal static void Apply(Pawn_GuestTracker tracker, bool announce)
        {
            Pawn pawn = PrisonerReflection.Of(tracker);

            if (pawn == null || tracker.ideoForConversion == null || pawn.Ideo != tracker.ideoForConversion)
                return;

            tracker.SetExclusiveInteraction(PrisonerInteractionModeDefOf.AttemptRecruit);

            if (!announce)
                return;

            Messages.Message(
                pawn.LabelShortCap + " already follows your ideoligion, so wardens will go straight to recruiting.",
                new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>
        /// Walks every prisoner the colony holds and hands over the ones with nothing left to convert.
        ///
        /// <b>Spawned prisoners on loaded maps only.</b> A prisoner in a caravan has no warden working on them
        /// wherever their mode points, so correcting it there would be tidying a state nobody can act on -- and
        /// they pass through a map again before anyone can.
        /// </summary>
        internal static void Sweep()
        {
            UIGuard.Try("Prisoners.ConvertSweep", () =>
            {
                List<Map> maps = Find.Maps;

                for (int i = 0; maps != null && i < maps.Count; i++)
                {
                    List<Pawn> prisoners = maps[i]?.mapPawns?.PrisonersOfColonySpawned;

                    for (int j = 0; prisoners != null && j < prisoners.Count; j++)
                    {
                        Pawn pawn = prisoners[j];

                        if (pawn?.guest == null)
                            continue;

                        if (pawn.guest.ExclusiveInteractionMode
                            == ConvertThenRecruitDefOf.Gideon_ConvertThenRecruit)
                            Apply(pawn.guest, false);
                    }
                }
            }, "Prisoners set to convert then recruit may need setting to recruit by hand.");
        }
    }

    /// <summary>
    /// Runs the handover sweep once the game is up.
    ///
    /// <b><c>FinalizeInit</c> rather than a load hook,</b> because it fires for a loaded save and a new game
    /// alike and runs after references are resolved -- <c>ideoForConversion</c> is one, and reading it earlier
    /// would compare against a null that means "not yet" rather than "none".
    ///
    /// A <c>GameComponent</c> needs no def and is constructed automatically for every game, which is why this
    /// costs nothing to add and nothing to remove.
    /// </summary>
    public class ConvertThenRecruitLoad : GameComponent
    {
        /// <summary>Required by RimWorld: every GameComponent is constructed with the game it belongs to.</summary>
        public ConvertThenRecruitLoad(Game game)
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();

            ConvertThenRecruitHandover.Sweep();
        }
    }

    /// <summary>
    /// The handover: the conversion took, so start recruiting.
    ///
    /// <b>Hooked on the ideoligion actually changing, not on the conversation succeeding.</b>
    /// <c>Pawn_IdeoTracker.SetIdeo</c> is the one place a pawn's ideoligion changes, so every route in gets caught
    /// -- a warden's talk, a ritual, a mod's own conversion. Patching
    /// <c>InteractionWorker_ConvertIdeoAttempt</c> instead would have caught only the first of those.
    ///
    /// <b>It checks the new ideoligion is the one that was wanted.</b> A prisoner who converts to something else
    /// entirely -- which a ritual can do -- has not finished this job, and switching them to recruitment then
    /// would abandon a conversion the player asked for.
    ///
    /// <b>The message is worth as much as the switch.</b> The complaint this answers is not knowing when the
    /// conversion landed, so a silent handover would fix the clicking and not the knowing.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.SetIdeo))]
    internal static class Patch_ConvertThenRecruitDone
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_IdeoTracker __instance, Ideo ideo)
        {
            if (ideo == null || !PrisonerReflection.Available)
                return;

            UIGuard.Try("Prisoners.ConvertDone", () => Handover(__instance, ideo),
                "This prisoner was converted but not switched over to recruiting; set them to recruit yourself.");
        }

        private static void Handover(Pawn_IdeoTracker tracker, Ideo ideo)
        {
            Pawn pawn = PrisonerReflection.Of(tracker);

            if (pawn?.guest == null || !pawn.IsPrisonerOfColony)
                return;

            if (pawn.guest.ExclusiveInteractionMode != ConvertThenRecruitDefOf.Gideon_ConvertThenRecruit)
                return;

            // A conversion to something other than the ideoligion this mode was aiming at is not this job
            // finishing. Left alone, the wardens carry on trying for the one that was asked for.
            if (pawn.guest.ideoForConversion != null && pawn.guest.ideoForConversion != ideo)
                return;

            pawn.guest.SetExclusiveInteraction(PrisonerInteractionModeDefOf.AttemptRecruit);

            Messages.Message(pawn.LabelShortCap + " has converted, and wardens will now try to recruit them.",
                new LookTargets(pawn), MessageTypeDefOf.PositiveEvent, false);
        }
    }
}
