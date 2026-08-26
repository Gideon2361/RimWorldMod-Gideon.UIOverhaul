using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Orders
{
    /// <summary>
    /// Carrying a badly hurt pawn to a bed while they are still on their feet.
    ///
    /// <b>The gap.</b> Vanilla will only rescue somebody who has already collapsed:
    /// <c>HealthAIUtility.WantsToBeRescued</c> opens with <c>if (!pawn.Downed) return false</c>, so
    /// <c>FloatMenuOptionProvider_RescuePawn</c> offers nothing for a pawn at 43 percent mobility who is bleeding
    /// out in six hours. That pawn will walk to a bed at less than half speed, or collapse on the way and be
    /// rescued then, by which time the six hours may have gone. Asked for by Aaron on 2026-08-25 from exactly
    /// that screenshot.
    ///
    /// <b>The threshold is the Moving capacity, not a health total.</b> Health summary counts every scratch;
    /// what decides whether walking home is a bad plan is how fast they can walk, and that is one number the
    /// game already publishes. Under <see cref="MobilityCeiling"/> the walk is slow enough to be worth somebody
    /// else's time.
    ///
    /// <b>A provider rather than a patch,</b> the same extension point <see cref="FloatMenuOptionProvider_Sleep"/>
    /// uses: <c>FloatMenuMakerMap.Init</c> instantiates every non-abstract subclass of
    /// <c>FloatMenuOptionProvider</c> in every loaded assembly. Nothing is patched and vanilla's own rescue
    /// option is untouched, which is also why this refuses a downed pawn: that is vanilla's line, and two
    /// rescue options on one pawn would be worse than either.
    ///
    /// <b>The job is vanilla's driver behind our own def,</b> which is what avoids reimplementing a carry. See
    /// Defs/Jobs_Rescue.xml.
    ///
    /// <b>They can be picked up again the moment they get up, and that is deliberate.</b> Once tucked in, the
    /// pawn runs their own AI like any patient; <c>ShouldSeekMedicalRest</c> keeps somebody who is bleeding or
    /// needs tending in the bed, but a pawn hurt enough to qualify and well enough to walk will get up and go
    /// back to work. Nothing here stops them being carried straight back: the only positional refusals are
    /// downed, dead and already in a bed, so a pawn on their feet is eligible again as soon as they leave it.
    ///
    /// <b>Carrying somebody who is awake and walking is a path the game already runs constantly.</b> Arrest
    /// uses this same <c>JobDriver_TakeToBed</c> against a conscious pawn who is actively trying to leave, which
    /// is why the driver's downed check names only Rescue and Capture and not Arrest. The chase is
    /// <c>Toils_Goto.GotoThing</c> re-pathing to a moving target, and the pickup is
    /// <c>Pawn_CarryTracker.TryStartCarry</c>, whose dead-or-downed guard is about the carrier rather than the
    /// one being carried. So none of this asks the engine for anything it does not already do.
    /// </summary>
    public class FloatMenuOptionProvider_RescueImpaired : FloatMenuOptionProvider
    {
        /// <summary>
        /// How slow counts as too slow, as a fraction of the Moving capacity.
        ///
        /// Aaron's number. Vanilla has nothing to compare it against: it recognises downed and nothing between
        /// that and healthy, so any line here is a judgement rather than a value being matched.
        /// </summary>
        private const float MobilityCeiling = 0.75f;

        /// <summary>Carrying a casualty out of a firefight is exactly when this is wanted.</summary>
        protected override bool Drafted => true;

        protected override bool Undrafted => true;

        /// <summary>One at a time: a carrier has one pair of arms and an order aimed at a squad would drop all but one.</summary>
        protected override bool Multiselect => false;

        /// <summary>Carrying takes hands.</summary>
        protected override bool RequiresManipulation => true;

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            return UIGuard.Try("Orders.RescueImpairedOption", () => Option(clickedPawn, context), null,
                "The carry-to-bed order is not offered for that pawn.");
        }

        /// <summary>
        /// The option for one pawn, or null when this is not somebody worth carrying.
        ///
        /// Refusals are silent here rather than worded, unlike the sleep order. The reason is the volume: this
        /// runs against every pawn under the cursor, and a greyed line on every healthy colonist explaining that
        /// they can walk perfectly well would bury the menu.
        /// </summary>
        private static FloatMenuOption Option(Pawn victim, FloatMenuContext context)
        {
            Pawn rescuer = context?.FirstSelectedPawn;

            if (victim == null || rescuer == null || victim == rescuer)
                return null;

            // Vanilla's line, left where it is. A downed pawn already has its own option, and that one carries
            // bookkeeping this does not.
            if (victim.Downed || victim.Dead)
                return null;

            // Already where this order would put them.
            if (victim.InBed())
                return null;

            if (victim.IsPrisonerOfColony || victim.IsSlaveOfColony || victim.IsColonyMech)
                return null;

            if (victim.Faction != null && victim.Faction.HostileTo(Faction.OfPlayer))
                return null;

            // A baby is carried through ChildcareUtility, which has its own rules about who may pick one up.
            if (ChildcareUtility.CanSuckle(victim, out _))
                return null;

            float mobility = Mobility(victim);

            if (mobility < 0f || mobility >= MobilityCeiling)
                return null;

            if (!rescuer.CanReserveAndReach(victim, PathEndMode.OnCell, Danger.Deadly, 1, -1, null, true))
                return null;

            // The figure is in the label because it is the answer to "why is this being offered": somebody
            // walking at 43 percent is the whole reason to carry them.
            string label = "Carry " + victim.LabelShortCap + " to bed (" + mobility.ToStringPercent() + " mobile)";

            FloatMenuOption option = new FloatMenuOption(label,
                UIGuard.Wrap("Orders.TakeRescueImpairedJob", () => Order(rescuer, victim)),
                MenuOptionPriority.RescueOrCapture, null, victim);

            option.tooltip = (TipSignal) ("Picks them up and carries them to a bed instead of leaving them to "
                                          + "walk it at " + mobility.ToStringPercent() + " speed.\n\nVanilla only "
                                          + "offers a rescue once somebody has collapsed. This is for the state "
                                          + "before that, where the walk home is the thing that kills them.");

            option = FloatMenuUtility.DecoratePrioritizedTask(option, rescuer, victim);

            // Vanilla's own bed check, so a missing bed reads the same here as it does on a real rescue: the
            // option is disabled and says which kind of bed is missing.
            FloatMenuUtility.ValidateTakeToBedOption(rescuer, victim, option, Cannot(victim));

            return option;
        }

        /// <summary>
        /// How well this pawn can still walk, or negative when there is nothing to read.
        ///
        /// <c>GetLevel</c> rather than a health summary, because a limp is what matters and a scarred ear is not.
        /// </summary>
        private static float Mobility(Pawn pawn)
        {
            if (pawn.health == null || pawn.health.capacities == null)
                return -1f;

            return pawn.health.capacities.GetLevel(PawnCapacityDefOf.Moving);
        }

        private static string Cannot(Pawn victim)
        {
            string key = victim.RaceProps != null && victim.RaceProps.Animal ? "NoAnimalBed" : "NoNonPrisonerBed";

            return "CannotRescue".Translate() + ": " + key.Translate().CapitalizeFirst();
        }

        /// <summary>
        /// Takes the job.
        ///
        /// <b>The bed is found again at click time,</b> which is the shape vanilla's own rescue option uses: the
        /// option was built a frame or more ago and somebody can have claimed the bed since. The second search
        /// ignoring reservations is vanilla's too, and it is what lets a rescue take a bed somebody has merely
        /// reserved rather than refusing outright.
        /// </summary>
        private static void Order(Pawn rescuer, Pawn victim)
        {
            Building_Bed bed = RestUtility.FindBedFor(victim, rescuer, false)
                               ?? RestUtility.FindBedFor(victim, rescuer, false, true);

            if (bed == null)
            {
                Messages.Message(Cannot(victim), victim, MessageTypeDefOf.RejectInput, false);

                return;
            }

            Job job = JobMaker.MakeJob(OrdersDefOf.Gideon_RescueImpaired, victim, bed);
            job.count = 1;

            rescuer.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}
