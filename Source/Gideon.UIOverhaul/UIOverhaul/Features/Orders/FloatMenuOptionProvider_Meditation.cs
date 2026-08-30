using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Orders
{
    /// <summary>
    /// Sends a colonist to meditate at the thing you clicked: a meditation spot, a focus object, or a throne.
    ///
    /// <b>The gap this closes.</b> Backlog item 18 named recreation, sleep, research and meditation; the first
    /// three shipped in 14155 and this one did not, and the item was marked done on 2026-08-23 saying so. The note
    /// it was marked with is still accurate: vanilla has no meditation order either. There is no provider for it,
    /// <c>MeditationUtility</c> has no menu builder, and the only way to make a pawn meditate on purpose is to
    /// paint Meditate across their timetable and wait for the hour to come round. That is a schedule, not an
    /// order, and it is no use at all in the ten minutes before a raid lands when the psycaster is at 20% psyfocus.
    ///
    /// <b>A provider rather than a patch,</b> for the reason the sleep order is one: <c>FloatMenuMakerMap.Init</c>
    /// instantiates every non-abstract <c>FloatMenuOptionProvider</c> in every loaded assembly, so this appears on
    /// the game's own terms and displaces nothing.
    ///
    /// <b>Vanilla builds the job; this only chooses the target.</b> <c>MeditationUtility.GetMeditationJob</c>
    /// already knows how to turn a spot and a focus into a job, including the part most people miss -- that
    /// meditating at a throne is not a meditate job at all but a reign job, with a different driver and a
    /// different thought. The spot search is the one thing not reused, because it answers a different question:
    /// it picks the best spot on the map, and an order has already been pointed at a particular thing.
    ///
    /// <b>Three kinds of target, which is the whole of the surface.</b> A meditation spot is stood on, a focus
    /// object is sat beside, and a throne is reigned from. There is deliberately no option on bare ground: any
    /// standable cell is a legal meditation spot, so offering it would put a line in the menu on every right click
    /// anywhere on the map, and the order a player actually wants is aimed at the anima tree.
    ///
    /// <b>Absent without Royalty rather than greyed.</b> Meditation, psyfocus and every focus object are Royalty
    /// content, and <c>JobGiver_Meditate.TryGiveJob</c> gates on <c>ModsConfig.RoyaltyActive</c> before it does
    /// anything else. Calling into the search without the DLC is worse than useless:
    /// <c>MeditationUtility.FindMeditationSpot</c> opens with <c>ModLister.CheckRoyalty</c>, which logs an error
    /// and returns false, so an unguarded provider would write a line to the log on every right click.
    ///
    /// <b>Guarded at every seam,</b> because all of these are called by RimWorld while it is building a menu, and
    /// an exception out of one of them costs the player the whole menu rather than one line of it.
    /// </summary>
    public class FloatMenuOptionProvider_Meditation : FloatMenuOptionProvider
    {
        /// <summary>A drafted pawn is holding a position. Sitting down to meditate is what undrafting is for.</summary>
        protected override bool Drafted => false;

        protected override bool Undrafted => true;

        /// <summary>
        /// One pawn at a time.
        ///
        /// A meditation spot and a throne are each reserved by one pawn, and the cell beside a focus object is
        /// reserved too, so an order given to a squad would seat the first and refuse the rest one at a time.
        /// </summary>
        protected override bool Multiselect => false;

        /// <summary>
        /// Not required.
        ///
        /// Meditation needs no hands. A pawn with no arms at all can still sit in front of an anima tree and gain
        /// psyfocus, and refusing them here would be this mod inventing a rule the game does not have.
        /// </summary>
        protected override bool RequiresManipulation => false;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            return UIGuard.Try("Orders.MeditationApplies", () =>
            {
                if (!ModsConfig.RoyaltyActive || !base.AppliesInt(context))
                    return false;

                Pawn pawn = context.FirstSelectedPawn;

                if (pawn == null || !pawn.IsColonistPlayerControlled)
                    return false;

                // Vanilla's own composite for whether meditating is possible at this moment: too tired, starving,
                // downed, asleep, bleeding, or owed medical rest. It is asked here rather than reproduced as
                // worded refusals, because every state in it is already plain on the pawn -- and because a second
                // opinion about when meditation is allowed is exactly the kind of thing that drifts from the
                // game's after an update.
                return MeditationUtility.CanMeditateNow(pawn);
            }, false, "The meditation order is not offered.");
        }

        protected override FloatMenuOption GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
        {
            return UIGuard.Try("Orders.MeditationOption", () => Option(clickedThing, context), null,
                "The meditation order is not offered for that thing.");
        }

        /// <summary>
        /// The option for one clicked thing, or null when this thing is not somewhere anyone could meditate.
        ///
        /// The three branches are in the order <c>MeditationUtility.AllMeditationSpotCandidates</c> considers
        /// them, which is also most specific first: a throne is a building, and a meditation spot would otherwise
        /// fall through to the focus branch and find no comp.
        /// </summary>
        private static FloatMenuOption Option(Thing clicked, FloatMenuContext context)
        {
            Pawn pawn = context?.FirstSelectedPawn;

            if (clicked == null || pawn == null || !clicked.Spawned || clicked.Map == null)
                return null;

            if (clicked is Building_Throne throne)
                return AtThrone(throne, pawn);

            if (ThingDefOf.MeditationSpot != null && clicked.def == ThingDefOf.MeditationSpot)
                return AtSpot(clicked as Building, pawn);

            return AtFocus(clicked, pawn);
        }

        // ---------------------------------------------------------------------------------------
        // The throne
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Reigning, which is what meditation at a throne is.
        ///
        /// <b>Included even though the word on the option is not "meditate".</b> A throne is the first thing
        /// vanilla's own meditation search considers for a titled pawn, and the job it produces there is
        /// <c>JobDefOf.Reign</c>. Leaving it out would have meant writing code to exclude something the game's
        /// model already includes, and a titled pawn right clicking their own throne and being offered nothing is
        /// a hole rather than a tidy boundary.
        ///
        /// <b>The throne room's own verdict is used for the refusal.</b> <c>RoomRoleWorker_ThroneRoom.Validate</c>
        /// returns the reason a room does not qualify -- too small, no throne, not enclosed -- already worded and
        /// already translated, which is the same reason the royal title screen gives.
        /// </summary>
        private static FloatMenuOption AtThrone(Building_Throne throne, Pawn pawn)
        {
            // Only a titled pawn reigns. Vanilla reaches thrones through RoyalTitleUtility.FindBestUsableThrone,
            // which is not called here because it answers "which throne" and the player has already said which.
            if (pawn.royalty == null || pawn.royalty.AllTitlesInEffectForReading.Count == 0)
                return null;

            if (JobDefOf.Reign == null)
                return null;

            string label = "Reign at " + throne.LabelShort;

            FloatMenuOption blocked = Blocked(throne, pawn, label);

            if (blocked != null)
                return blocked;

            string roomFault = RoomRoleWorker_ThroneRoom.Validate(throne.GetRoom());

            if (roomFault != null)
                return Refused(label, roomFault);

            if (!MeditationUtility.IsValidMeditationBuildingForPawn(throne, pawn))
                return Refused(label, Claimed(throne));

            if (!MeditationUtility.SafeEnvironmentalConditions(pawn, throne.Position, throne.Map))
                return Refused(label, Unsafe);

            Job job = JobMaker.MakeJob(JobDefOf.Reign, throne, LocalTargetInfo.Invalid, throne);
            job.ignoreJoyTimeAssignment = true;

            return Offer(label, job, throne, pawn, throne);
        }

        // ---------------------------------------------------------------------------------------
        // The meditation spot
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The dedicated spot building, which is stood on rather than sat beside.
        ///
        /// <b>The focus is looked up rather than named,</b> through <c>BestFocusAt</c>, because the player clicked
        /// a place to sit and not a thing to look at. That is the one case where letting vanilla pick is right:
        /// the spot is fixed and the strongest focus within its ring is simply the best answer available from it.
        /// </summary>
        private static FloatMenuOption AtSpot(Building spot, Pawn pawn)
        {
            if (spot == null || JobDefOf.Meditate == null)
                return null;

            // Its own label is "meditation spot", so "Meditate at meditation spot" is what naming it would read as.
            const string label = "Meditate here";

            FloatMenuOption blocked = Blocked(spot, pawn, label);

            if (blocked != null)
                return blocked;

            if (!MeditationUtility.IsValidMeditationBuildingForPawn(spot, pawn))
                return Refused(label, Claimed(spot));

            if (!MeditationUtility.SafeEnvironmentalConditions(pawn, spot.Position, spot.Map))
                return Refused(label, Unsafe);

            LocalTargetInfo focus = MeditationUtility.BestFocusAt(spot, pawn);

            return Offer(label, Meditate(spot, focus), spot, pawn, focus.Thing);
        }

        // ---------------------------------------------------------------------------------------
        // A focus object
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Anything carrying a meditation focus this pawn can use: an anima tree, a sculpture, a nature shrine, a
        /// modded one nobody here has heard of.
        ///
        /// <b>The clicked thing is the focus, even when a stronger one is standing next to it.</b> Vanilla's
        /// search would re-pick the best focus in range of the cell it lands on, which is right when the game is
        /// choosing and wrong when the player has pointed at something: an order aimed at the sculpture that
        /// silently became an order aimed at the anima tree behind it would be a different order.
        ///
        /// <b>Silence rather than a greyed line for the refusals here.</b> A focus type the pawn cannot use, a
        /// strength of zero, somebody else's bedroom -- each of those would put a dead line under every statue in
        /// the colony for every colonist who is not a psycaster, which is most of them.
        /// </summary>
        private static FloatMenuOption AtFocus(Thing focus, Pawn pawn)
        {
            if (JobDefOf.Meditate == null)
                return null;

            CompMeditationFocus comp = focus.TryGetComp<CompMeditationFocus>();

            if (comp == null || !comp.CanPawnUse(pawn))
                return null;

            // Walls carry the comp and are skipped by vanilla's own candidate search for the obvious reason: a
            // colony is made of them, and every one would be an option.
            if (focus.def == ThingDefOf.Wall)
                return null;

            Room room = focus.GetRoom();

            // Somebody's bedroom, or a prison cell they are not a prisoner of.
            if (room != null && !MeditationUtility.CanUseRoomToMeditate(room, pawn))
                return null;

            // Strength is per pawn: a focus type the pawn cannot draw on reads as zero for them even though the
            // thing itself is a perfectly good focus for somebody else.
            if (focus.GetStatValueForPawn(StatDefOf.MeditationFocusStrength, pawn) <= 0f)
                return null;

            string label = "Meditate at " + focus.LabelShort;

            // Vanilla's own choice of where to sit: a cardinal cell within two of the thing that is in bounds,
            // standable, unforbidden, and reservable and reachable by this pawn. All four failures fold into one
            // invalid answer, so the refusal names the shape of the problem rather than guessing which it was.
            LocalTargetInfo spot = MeditationUtility.MeditationSpotForFocus(focus, pawn);

            if (!spot.IsValid)
                return Refused(label, "nowhere free to sit beside it");

            if (!MeditationUtility.SafeEnvironmentalConditions(pawn, spot.Cell, focus.Map))
                return Refused(label, Unsafe);

            return Offer(label, Meditate(spot, focus), spot, pawn, focus);
        }

        // ---------------------------------------------------------------------------------------
        // Shared
        // ---------------------------------------------------------------------------------------

        /// <summary>Toxic fallout overhead, noxious haze, vacuum, or a cell the pawn considers dangerous.</summary>
        private const string Unsafe = "not safe to meditate here";

        /// <summary>
        /// The two refusals worth wording, in the shape the sleep order uses.
        ///
        /// Both are invisible from the map and both are fixed by the player in one click, which is the test for
        /// whether a dead line earns its place in the menu.
        /// </summary>
        private static FloatMenuOption Blocked(Thing thing, Pawn pawn, string label)
        {
            if (thing.IsForbidden(pawn))
            {
                return Refused(label, thing.Position.InAllowedArea(pawn)
                    ? "ForbiddenLower".Translate()
                    : "ForbiddenOutsideAllowedAreaLower".Translate());
            }

            if (!pawn.CanReach(thing, PathEndMode.OnCell, Danger.Deadly))
                return Refused(label, "NoPath".Translate());

            return null;
        }

        /// <summary>
        /// Who has this spot, when the game's composite test refused it.
        ///
        /// <c>IsValidMeditationBuildingForPawn</c> folds assignment, the room, reservation and reach into one
        /// false, and by the time it is called here the first two of those are the only ones left. Assignment is
        /// the one a player can see the answer to and act on, so it is named; anything else falls back to a plain
        /// refusal rather than a guess.
        /// </summary>
        private static string Claimed(Building spot)
        {
            List<Pawn> assigned = spot.TryGetComp<CompAssignableToPawn>()?.AssignedPawnsForReading;

            if (assigned != null && assigned.Count > 0 && assigned[0] != null)
                return assigned[0].LabelShort + "'s";

            return "not available";
        }

        private static FloatMenuOption Refused(string label, string reason)
        {
            return new FloatMenuOption(label + " (" + reason + ")", null);
        }

        /// <summary>
        /// The meditate job, built the way <c>MeditationUtility.GetMeditationJob</c> builds it: the spot in A, the
        /// bed slot in B left invalid, and the focus in C.
        ///
        /// <b><c>ignoreJoyTimeAssignment</c> is set, which is what makes this an order rather than a suggestion.</b>
        /// It is the flag vanilla sets for the psyfocus path and clears for the recreation one, and with it clear
        /// the driver ends the job the moment the pawn's recreation need fills -- so an ordered meditation would
        /// stop early for a colonist who happened to be in a good mood. The prayer variant of the job is the other
        /// side of that same flag and is deliberately not used here: <c>JobDefOf.MeditatePray</c> is reached only
        /// on the recreation path, so this order meditates rather than prays even for a pawn whose ideoligion has
        /// deities.
        /// </summary>
        private static Job Meditate(LocalTargetInfo spot, LocalTargetInfo focus)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Meditate, spot, LocalTargetInfo.Invalid, focus);

            job.ignoreJoyTimeAssignment = true;

            return job;
        }

        private static FloatMenuOption Offer(string label, Job job, LocalTargetInfo spot, Pawn pawn, Thing focus)
        {
            if (job == null)
                return null;

            FloatMenuOption option = new FloatMenuOption(label, UIGuard.Wrap("Orders.TakeMeditationJob",
                () => Order(pawn, job, spot)));

            option.tooltip = (TipSignal) Tip(pawn, focus);

            return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, spot);
        }

        /// <summary>
        /// Takes the job.
        ///
        /// <b>Reserved and reached again here rather than trusted from the menu,</b> which is the test
        /// <c>GetMeditationJob</c> makes at the same point: the option was built at least a frame ago, and in
        /// between somebody else can have sat down in the only cell beside the tree.
        /// </summary>
        private static void Order(Pawn pawn, Job job, LocalTargetInfo spot)
        {
            if (!pawn.CanReserveAndReach(spot, PathEndMode.OnCell, pawn.NormalMaxDanger()))
                return;

            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        /// <summary>
        /// What this is worth, which is the one thing about the order a player cannot read off the map.
        ///
        /// Psyfocus per day is the number the meditation screens already show, from the game's own
        /// <c>PsyfocusGainPerTick</c> and its own translated line, so a focus object's contribution is priced the
        /// same here as it is on its info card. Without a psylink there is no psyfocus at all, and saying so is
        /// more use than a blank tooltip -- the pawn still learns while they sit, which is the honest reason to
        /// send somebody who will never cast anything.
        /// </summary>
        private static string Tip(Pawn pawn, Thing focus)
        {
            if (!pawn.HasPsylink || pawn.psychicEntropy == null)
                return "No psylink, so no psyfocus to gain. They will still learn Intellectual while they sit.";

            float perDay = MeditationUtility.PsyfocusGainPerTick(pawn, focus) * 60000f;

            return "PsyfocusPerDayOfMeditation".Translate(perDay.ToStringPercent()).CapitalizeFirst();
        }
    }
}
