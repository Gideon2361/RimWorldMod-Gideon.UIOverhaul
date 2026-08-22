using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Orders
{
    /// <summary>
    /// Sends a colonist to bed on command.
    ///
    /// <b>Vanilla has no such order,</b> which is the gap Aaron named on 2026-08-22 alongside research. Rest is
    /// scheduled rather than ordered: the pawn goes when the timetable and the rest need agree, and the only bed
    /// option in the right click menu is <c>Building_Bed.GetBedRestFloatMenuOption</c>, which is for medical beds
    /// and only when the pawn is injured enough to want one. There is no way to say "go to bed now", which is
    /// exactly what somebody does at dusk before a raid is due.
    ///
    /// <b>A provider rather than a patch.</b> <c>FloatMenuMakerMap.Init</c> instantiates every non-abstract
    /// subclass of <c>FloatMenuOptionProvider</c> in every loaded assembly, so this class appearing in the menu is
    /// the game's own extension point rather than something forced on it. Nothing is patched and no vanilla option
    /// is displaced.
    ///
    /// <b>No forced sleep.</b> <c>Job.forceSleep</c> exists and would make the pawn drop off whatever their rest
    /// level is, but it also makes <c>Toils_LayDown</c> ignore every wake condition, so they would lie there
    /// through the day until something interrupted them. Without it the job is exactly vanilla's rest job: a tired
    /// pawn sleeps and wakes when rested, and one who is not tired lies down and rests until they are, which is
    /// what the medical bed option does too.
    ///
    /// <b>Guarded at every seam,</b> because all three overrides are called by RimWorld rather than by us. A throw
    /// out of one of them lands in the float menu's own construction, where the whole menu is lost rather than one
    /// line of it.
    /// </summary>
    public class FloatMenuOptionProvider_Sleep : FloatMenuOptionProvider
    {
        /// <summary>A drafted pawn is holding a position, and standing down to sleep is what undrafting is for.</summary>
        protected override bool Drafted => false;

        protected override bool Undrafted => true;

        /// <summary>
        /// One pawn at a time.
        ///
        /// A bed holds one sleeper, or two who are lovers, so an order aimed at a squad would put one of them in
        /// bed and silently drop the rest.
        /// </summary>
        protected override bool Multiselect => false;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            return UIGuard.Try("Orders.SleepApplies", () =>
            {
                if (!base.AppliesInt(context))
                    return false;

                Pawn pawn = context.FirstSelectedPawn;

                // The rest need is the real test rather than the race: anything without one has nothing to gain
                // from a bed, which covers mechanoids and most of Anomaly's entities.
                return pawn?.needs?.rest != null && pawn.IsColonistPlayerControlled;
            }, false, "The sleep order is not offered.");
        }

        protected override FloatMenuOption GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
        {
            return UIGuard.Try("Orders.SleepOption", () => Option(clickedThing as Building_Bed, context), null,
                "The sleep order is not offered for that bed.");
        }

        /// <summary>
        /// The option for one bed, or null when this bed is not a thing this pawn could ever sleep in.
        ///
        /// <b>Refusals are worded rather than hidden, once the bed is one they could use in principle.</b> A
        /// forbidden bed or an unreachable one is a situation the player can fix, and a greyed line saying why is
        /// how they find out; a bed of a kind the pawn can never use, such as an animal bed under a colonist, is
        /// not worth a line at all.
        /// </summary>
        private static FloatMenuOption Option(Building_Bed bed, FloatMenuContext context)
        {
            Pawn pawn = context?.FirstSelectedPawn;

            if (bed == null || pawn == null)
                return null;

            // Medical beds already have vanilla's own option, which carries the treatment bookkeeping this one
            // does not. Two lines about lying in the same bed would be worse than either alone.
            if (bed.Medical)
                return null;

            if (!RestUtility.CanUseBedEver(pawn, bed.def))
                return null;

            string label = bed.def.building != null && bed.def.building.bed_humanlike && bed.SleepingSlotsCount > 0
                ? "Sleep in " + bed.LabelShort
                : "Sleep here";

            if (bed.IsForbidden(pawn))
            {
                return Refused(label, bed.Position.InAllowedArea(pawn)
                    ? "ForbiddenLower".Translate()
                    : "ForbiddenOutsideAllowedAreaLower".Translate());
            }

            if (!pawn.CanReach(bed, PathEndMode.OnCell, Danger.Deadly))
                return Refused(label, "NoPath".Translate());

            // Vanilla's own composite test: ownership, sharing, social properness, prisoner and slave rules, and
            // whether a slot is free. Reproducing any part of it here would be a second opinion about who may
            // sleep where, and there is no reason for this mod to have one.
            if (!RestUtility.IsValidBedFor(bed, pawn, pawn, true))
                return Refused(label, Taken(bed));

            FloatMenuOption option = new FloatMenuOption(label, UIGuard.Wrap("Orders.TakeSleepJob",
                () => Order(pawn, bed)));

            option.tooltip = (TipSignal) ("Lies down now. A tired pawn falls asleep and wakes when rested; one "
                                          + "who is not tired rests until they are.");

            return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, bed,
                bed.AnyUnoccupiedSleepingSlot ? "ReservedBy" : "SomeoneElseSleeping");
        }

        private static FloatMenuOption Refused(string label, string reason)
        {
            return new FloatMenuOption(label + " (" + reason + ")", null);
        }

        /// <summary>
        /// Why a usable bed is not available: who is in it, or whose it is.
        ///
        /// <b>Named rather than left as "not available",</b> because both answers tell the player what to do about
        /// it, and they are the two cases that actually come up: somebody is asleep in it, or it belongs to
        /// somebody who will not share it.
        /// </summary>
        private static string Taken(Building_Bed bed)
        {
            foreach (Pawn occupant in bed.CurOccupants)
            {
                if (occupant != null)
                    return "SomeoneElseSleeping".Translate(occupant.LabelShort, occupant);
            }

            List<Pawn> owners = bed.OwnersForReading;

            if (owners != null && owners.Count > 0 && owners[0] != null)
                return owners[0].LabelShort + "'s bed";

            return "not available";
        }

        /// <summary>
        /// Takes the job.
        ///
        /// <b>Reserved and reached before the job is made,</b> which is the shape vanilla's medical bed option
        /// uses: the click happens a frame or more after the option was built, and in between somebody else can
        /// have claimed the bed.
        ///
        /// <b>The disturbance tick is reset for the same reason vanilla resets it.</b>
        /// <c>RestUtility.CanFallAsleep</c> refuses for 400 ticks after anything disturbed the pawn, and being
        /// shot at, woken, or ordered around all count: without this, an order given in the middle of a fight puts
        /// the pawn in bed awake and looking like the order half worked.
        /// </summary>
        private static void Order(Pawn pawn, Building_Bed bed)
        {
            if (!pawn.CanReserveAndReach(bed, PathEndMode.ClosestTouch, Danger.Deadly, bed.SleepingSlotsCount, -1,
                    null, true))
                return;

            Job job = JobMaker.MakeJob(JobDefOf.LayDown, bed);

            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            pawn.mindState.ResetLastDisturbanceTick();
        }
    }
}
