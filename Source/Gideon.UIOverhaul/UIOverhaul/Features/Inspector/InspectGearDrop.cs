using System;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// Dropping something a pawn is carrying, from the gear body.
    ///
    /// <b>Every rule here is RimWorld's, reproduced rather than reinvented.</b> Dropping is not one action but
    /// three -- taking off apparel is a job, dropping a weapon is a different job, and putting down an inventory
    /// item is neither -- and there are four separate reasons the game refuses. Getting any of that wrong means
    /// either a button that does nothing or one that strips a quest lodger of gear the quest said they keep, so
    /// this follows <c>ITab_Pawn_Gear.InterfaceDrop</c> and its guard clause line for line.
    ///
    /// <b>Its own file because it is the one part of the gear body that changes the world.</b> Everything else
    /// there reads and draws; this issues jobs, and a reader deciding whether the pane is safe should be able to
    /// find all of that in one place.
    /// </summary>
    internal static class InspectGearDrop
    {
        /// <summary>
        /// Whether this pawn's gear may be touched at all.
        ///
        /// <c>ITab_Pawn_Gear.CanControl</c>, exactly: not while downed, in a mental state or being carried; only
        /// for our own faction or our own prisoners; not for a prisoner with no free colonist on the map, nor one
        /// currently breaking out or leaving.
        /// </summary>
        internal static bool CanControl(Pawn pawn)
        {
            return UIGuard.Try("Inspector.CanControlGear", () =>
            {
                if (pawn == null || pawn.Dead)
                    return false;

                if (pawn.Downed || pawn.InMentalState || pawn.CarriedBy != null)
                    return false;

                if (pawn.Faction != Faction.OfPlayer && !pawn.IsPrisonerOfColony)
                    return false;

                if (pawn.IsPrisonerOfColony && pawn.Spawned && !pawn.Map.mapPawns.AnyFreeColonistSpawned)
                    return false;

                if (pawn.IsPrisonerOfColony
                    && (PrisonBreakUtility.IsPrisonBreaking(pawn)
                        || (pawn.CurJob != null && pawn.CurJob.exitMapOnArrival)))
                    return false;

                return true;
            }, false, "Items cannot be dropped from the inspect pane.");
        }

        /// <summary>
        /// The drop button for one item, dead with a reason where the game refuses.
        ///
        /// <b>Refusals are drawn grey with the game's own explanation on hover rather than hidden.</b> A missing
        /// button reads as an oversight; a greyed one that says "this apparel is locked" answers the question.
        /// </summary>
        internal static void Button(Rect rect, Pawn pawn, Thing thing, bool inventory, UIColorPaletteDef palette)
        {
            if (thing == null)
                return;

            string refusal = Refusal(pawn, thing, inventory);
            bool refused = !refusal.NullOrEmpty();

            if (Mouse.IsOver(rect))
                TooltipHandler.TipRegion(rect, (TipSignal) (refused ? refusal : "DropThing".Translate()));

            Color color = refused ? palette.TextDisabled : palette.TextSecondary;
            Color over = refused ? color : palette.Accent;

            if (!Widgets.ButtonImage(rect, TexButton.Drop, color, over, !refused) || refused)
                return;

            UIGuard.Try("Inspector.Drop", () =>
            {
                Action drop = () =>
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();

                    Drop(pawn, thing);
                };

                // Biotech's confirmation, kept: dropping a mech link band loses bandwidth, and the game asks
                // first. It returns true when it has put a dialog up, in which case the drop is its to run.
                if (!ModsConfig.BiotechActive
                    || !MechanitorUtility.TryConfirmBandwidthLossFromDroppingThing(pawn, thing, drop))
                    drop();
            }, "That item was not dropped.");
        }

        /// <summary>
        /// Why the game will not drop this, or null when it will.
        ///
        /// The three refusals vanilla checks, in its own order: a quest lodger's gear, apparel the pawn cannot
        /// remove, and a pawn kind whose gear is destroyed rather than dropped.
        /// </summary>
        private static string Refusal(Pawn pawn, Thing thing, bool inventory)
        {
            return UIGuard.Try<string>("Inspector.DropRefusal", () =>
            {
                Apparel apparel = thing as Apparel;

                if (apparel != null && pawn.apparel != null && pawn.apparel.IsLocked(apparel))
                    return "DropThingLocked".Translate();

                if (pawn.IsQuestLodger()
                    && (inventory || !EquipmentUtility.QuestLodgerCanUnequip(thing, pawn)))
                    return "DropThingLodger".Translate();

                if (!inventory && pawn.kindDef != null && pawn.kindDef.destroyGearOnDrop)
                    return "DropThingLodger".Translate();

                return null;
            }, null, null);
        }

        /// <summary>
        /// The drop itself, which is three different things depending on where the item is.
        ///
        /// Worn apparel and a wielded weapon are ordered jobs, so the pawn walks through taking them off and the
        /// action can be cancelled like any other; an inventory item is simply put on the ground, which is what
        /// vanilla does and the reason a dropped meal appears instantly while a dropped duster does not.
        /// </summary>
        private static void Drop(Pawn pawn, Thing thing)
        {
            Apparel apparel = thing as Apparel;

            if (apparel != null && pawn.apparel != null && pawn.apparel.WornApparel.Contains(apparel))
            {
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.RemoveApparel, apparel), JobTag.Misc);

                return;
            }

            ThingWithComps equipment = thing as ThingWithComps;

            if (equipment != null && pawn.equipment != null
                                  && pawn.equipment.AllEquipmentListForReading.Contains(equipment))
            {
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.DropEquipment, equipment), JobTag.Misc);

                return;
            }

            // destroyOnDrop is checked because some things cannot exist on the ground at all, and TryDrop would
            // quietly destroy them. Vanilla refuses the same way.
            if (thing.def.destroyOnDrop || pawn.inventory == null)
                return;

            Thing dropped;

            pawn.inventory.innerContainer.TryDrop(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near, out dropped);
        }
    }
}
