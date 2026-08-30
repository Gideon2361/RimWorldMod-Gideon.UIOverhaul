using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Beds
{
    /// <summary>
    /// Beds their owner is willing to share.
    ///
    /// <b>RimWorld has no way to say "mine, but help yourself when I am not in it".</b> Once a bed has an owner,
    /// nobody but a love partner may lie in it, which is right for a private bedroom and wrong for a spare bunk,
    /// the bed beside the workshop somebody naps in, or a bunk worked in shifts. Asked for on 2026-08-25.
    ///
    /// <b>Kept on the map rather than on the bed.</b> A bed is a <c>ThingWithComps</c> and the tidy answer would
    /// be a comp -- but a comp has to be injected into every bed def in the game, including the ones other mods
    /// add and the ones added after us, and a bed whose def missed the injection would silently lose its mark. A
    /// list on the map component is a list; it holds whatever is put in it, and it goes away with the map.
    ///
    /// <b>References, not ids.</b> Scribed with <c>LookMode.Reference</c> so a deconstructed bed resolves to null
    /// and is dropped on load rather than leaving an id that a later bed could be given.
    /// </summary>
    internal class MapComponent_CommunalBeds : MapComponent
    {
        private HashSet<Building_Bed> beds = new HashSet<Building_Bed>();

        private List<Building_Bed> scribe;

        public MapComponent_CommunalBeds(Map map) : base(map)
        {
        }

        internal static MapComponent_CommunalBeds For(Map map)
        {
            return map?.GetComponent<MapComponent_CommunalBeds>();
        }

        internal bool IsCommunal(Building_Bed bed)
        {
            return bed != null && beds.Contains(bed);
        }

        internal void Set(Building_Bed bed, bool communal)
        {
            if (bed == null)
                return;

            if (communal)
                beds.Add(bed);
            else
                beds.Remove(bed);
        }

        public override void ExposeData()
        {
            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                scribe = new List<Building_Bed>(beds);

                // Nothing destroyed goes out. A bed deconstructed this session would otherwise be written as a
                // reference that resolves to null on the next load and has to be swept up there instead.
                scribe.RemoveAll(bed => bed == null || bed.Destroyed);
            }

            Scribe_Collections.Look(ref scribe, "communalBeds", LookMode.Reference);

            if (Scribe.mode != LoadSaveMode.PostLoadInit)
                return;

            beds = new HashSet<Building_Bed>();

            if (scribe == null)
                return;

            for (int i = 0; i < scribe.Count; i++)
            {
                if (scribe[i] != null)
                    beds.Add(scribe[i]);
            }

            scribe = null;
        }
    }

    /// <summary>Reading the mark, from anywhere, without every caller knowing where it is kept.</summary>
    internal static class CommunalBeds
    {
        /// <summary>Whether the feature is switched on at all. Off, the marks stay in the save and are ignored.</summary>
        internal static bool Enabled
        {
            get
            {
                // True on both fallbacks, matching the setting's own default: a settings file that has not loaded
                // yet should not read as "the player turned this off".
                return UIGuard.Try("Beds.ReadSetting",
                    () => UIOverhaulSettingsFile.Current?.allowCommunalBeds ?? true, true, null);
            }
        }

        internal static bool IsCommunal(Building_Bed bed)
        {
            if (bed == null || !bed.Spawned)
                return false;

            return UIGuard.Try("Beds.IsCommunal",
                () => MapComponent_CommunalBeds.For(bed.Map)?.IsCommunal(bed) ?? false, false, null);
        }

        /// <summary>
        /// Whether this bed is one the mark makes sense on.
        ///
        /// A prisoner or slave bed is already pooled among its own kind by vanilla's rules, and a medical bed is
        /// assigned by need rather than owned -- so the switch is offered on ordinary colonist beds and nowhere
        /// else, rather than being offered everywhere and doing nothing in most places.
        /// </summary>
        internal static bool Applies(Building_Bed bed)
        {
            if (bed == null || !bed.Spawned || bed.Faction != Faction.OfPlayer)
                return false;

            return UIGuard.Try("Beds.Applies",
                () => !bed.ForPrisoners && !bed.ForSlaves && !bed.Medical && bed.def.building.bed_humanlike,
                false, null);
        }
    }

    /// <summary>
    /// The one refusal this feature lifts.
    ///
    /// <b>It is the whole feature, in one postfix.</b> <c>RestUtility.BedOwnerWillShare</c> is where vanilla says
    /// no to a pawn eyeing somebody else's bed, and it is asked once per bed per search from
    /// <c>CanUseBedNow</c>. Everything else about sleeping stays vanilla's.
    ///
    /// <b>The unoccupied test is not ours and is not touched.</b> <c>CanUseBedNow</c> checks
    /// <c>AnyUnoccupiedSleepingSlot</c> before it ever reaches this, so a communal bed with somebody in it is as
    /// unavailable as any other bed. The mark lets a pawn consider a bed; it does not let two pawns share a slot.
    ///
    /// <b>Only ever widening.</b> The postfix can turn a no into a yes and does nothing else, so no pawn stops
    /// being able to sleep somewhere because this loaded.
    /// </summary>
    [HarmonyPatch(typeof(RestUtility), nameof(RestUtility.BedOwnerWillShare))]
    internal static class Patch_CommunalBedSharing
    {
        [HarmonyPostfix]
        public static void Postfix(Building_Bed bed, ref bool __result)
        {
            if (__result || !CommunalBeds.Enabled)
                return;

            bool communal = false;

            if (UIGuard.Try("Beds.WillShare", () => communal = CommunalBeds.IsCommunal(bed)))
                __result = communal;
        }
    }

    /// <summary>
    /// Sleeping in a communal bed does not make it yours.
    ///
    /// <b>Reported the first time one was used.</b> RimWorld claims an unowned bed for whoever lies down in it,
    /// which is a kindness in a barracks and the opposite of the point here: the first pawn to get tired took the
    /// spare bunk permanently and the bed stopped being communal by doing exactly what it was marked to do.
    ///
    /// <b>Patched at the toil, not at <c>Pawn_Ownership.ClaimBedIfNonMedical</c>.</b> That method is the choke
    /// point every claim goes through -- which is what makes it the wrong one. Two of the things going through it
    /// are deliberate: the Assign button on the bed, and the repair pass that re-adds a pawn whose owned bed lost
    /// them. A communal bed is still allowed an owner, so blocking claims there would have broken assigning one,
    /// and blocking the repair would quietly drop an assignment on load. <c>Toils_Bed.ClaimBedIfNonMedical</c> is
    /// only ever reached by a pawn getting into a bed, which is precisely the case to refuse.
    ///
    /// <b>The original action is wrapped rather than replaced.</b> Everything the toil does about mutants and
    /// claimant indices stays vanilla's; this only decides whether to run it.
    ///
    /// <b>One route is deliberately left alone.</b> <c>JobGiver_DeliverPawnToBed</c> claims a bed directly when a
    /// ritual or an arrival sends somebody to one. It is not a pawn choosing where to sleep, it is a duty putting
    /// them somewhere, and the ownership it grants is what keeps them there afterwards.
    /// </summary>
    [HarmonyPatch(typeof(Toils_Bed), nameof(Toils_Bed.ClaimBedIfNonMedical))]
    internal static class Patch_CommunalBedClaiming
    {
        [HarmonyPostfix]
        public static void Postfix(Toil __result, TargetIndex ind)
        {
            if (__result == null)
                return;

            Action original = __result.initAction;

            __result.initAction = () =>
            {
                bool skip = false;

                UIGuard.Try("Beds.SkipClaim", () => skip = Communal(__result, ind));

                if (skip)
                    return;

                if (original != null)
                    original();
            };
        }

        private static bool Communal(Toil toil, TargetIndex ind)
        {
            if (!CommunalBeds.Enabled)
                return false;

            Pawn actor = toil.GetActor();

            Building_Bed bed = actor?.CurJob?.GetTarget(ind).Thing as Building_Bed;

            return CommunalBeds.IsCommunal(bed);
        }
    }

    /// <summary>
    /// What a communal bed calls itself on the map.
    ///
    /// <b>"Unowned" is true and unhelpful once the mark exists.</b> Every bed nobody has claimed says it, so the
    /// one word a player needs -- which of these is the shared one -- is the word missing. Reported on
    /// 2026-08-25.
    ///
    /// <b>Only the unowned case is taken over.</b> A communal bed that does have an owner keeps showing their
    /// name, because who sleeps there by right is the more useful fact and replacing it with "Communal" would
    /// lose it. The label is a single line over a bed; it can carry one of the two.
    ///
    /// The three conditions vanilla checks first are repeated rather than skipped, or the label would appear at
    /// zoom levels where no other bed label does.
    /// </summary>
    [HarmonyPatch(typeof(Building_Bed), nameof(Building_Bed.DrawGUIOverlay))]
    internal static class Patch_CommunalBedLabel
    {
        [HarmonyPrefix]
        public static bool Prefix(Building_Bed __instance)
        {
            if (!CommunalBeds.Enabled)
                return true;

            bool drawn = false;

            UIGuard.Try("Beds.Label", () => drawn = Draw(__instance));

            return !drawn;
        }

        private static bool Draw(Building_Bed bed)
        {
            if (bed.Medical || Find.CameraDriver.CurrentZoom != CameraZoomRange.Closest)
                return false;

            CompAssignableToPawn comp = bed.CompAssignableToPawn;

            if (comp == null || !comp.PlayerCanSeeAssignments)
                return false;

            if (bed.OwnersForReading.Any() || !CommunalBeds.IsCommunal(bed))
                return false;

            GenMapUI.DrawThingLabel(bed, "Communal", GenMapUI.DefaultThingLabelColor);

            return true;
        }
    }

    /// <summary>
    /// The switch itself, on the bed.
    ///
    /// <b>A gizmo rather than a line in our inspect pane,</b> because it is a command and commands live in the
    /// command row. It sits beside vanilla's own bed toggles -- for prisoners, for slaves, medical -- which is
    /// where somebody looking to change what a bed is for will already be.
    ///
    /// The gizmo is absent when the setting is off rather than present and disabled: a control that cannot do
    /// anything is worse than no control, because it invites a click and then explains itself.
    /// </summary>
    [HarmonyPatch(typeof(Building_Bed), nameof(Building_Bed.GetGizmos))]
    internal static class Patch_CommunalBedGizmo
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Building_Bed __instance)
        {
            foreach (Gizmo gizmo in gizmos)
                yield return gizmo;

            if (!CommunalBeds.Enabled || !CommunalBeds.Applies(__instance))
                yield break;

            Command_Toggle toggle = null;

            // Built inside the guard because reading the map component can fail; yielded outside it, since a
            // yield cannot cross a lambda.
            UIGuard.Try("Beds.Gizmo", () => toggle = Build(__instance),
                "The communal switch is missing from this bed.");

            if (toggle != null)
                yield return toggle;
        }

        private static Command_Toggle Build(Building_Bed bed)
        {
            MapComponent_CommunalBeds store = MapComponent_CommunalBeds.For(bed.Map);

            if (store == null)
                return null;

            return new Command_Toggle
            {
                defaultLabel = "Communal",
                defaultDesc = "Anyone who needs a bed may sleep here while a slot is free, whether or not this "
                              + "bed has an owner.\n\nOwnership is unaffected: whoever this bed is assigned to "
                              + "still gets the bedroom they are owed. Somebody already asleep in it still makes "
                              + "it unavailable.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/AssignOwner", false)
                       ?? BaseContent.BadTex,
                isActive = () => store.IsCommunal(bed),
                toggleAction = () => store.Set(bed, !store.IsCommunal(bed))
            };
        }
    }

    /// <summary>
    /// Gives a non-owner lying in a communal bed the slot they are actually in.
    ///
    /// <b>This is the fix for a pawn who lies in a communal bed awake while their sleep drains.</b> Reported on
    /// 2026-08-29. <c>CanUseBedNow</c> asks two ownership questions, not one, and widening
    /// <c>BedOwnerWillShare</c> only answers the first:
    ///
    /// <code>
    /// bool flag  = bed.IsOwner(sleeper, out var assignedSleepingSlot);
    /// bool flag2 = sleeper.CurrentBed(out var sleepingSlot) == bed;
    /// ...
    /// if (!flag &amp;&amp; !BedOwnerWillShare(...)) return false;      // our other postfix passes this
    /// if (flag2 &amp;&amp; sleepingSlot != assignedSleepingSlot) return false;
    /// </code>
    ///
    /// Both are <c>int?</c>. A non-owner gets <c>null</c> from <c>IsOwner</c>, and the moment they lie down
    /// <c>CurrentBed</c> gives <c>0</c> -- so <c>0 != null</c> and the bed becomes unusable <i>because they got
    /// into it</i>. <c>JobDriver_LayDown</c> carries <c>FailOnBedNoLongerUsable</c>, so the job ends, the rest
    /// job giver hands it straight back (out of the bed, <c>flag2</c> is false again and the test passes), and
    /// the pawn loops between lying down and failing. Reported as "Resting", never reaching asleep.
    ///
    /// <b>The slot is filled in but the answer stays no.</b> Returning true would make <c>flag</c> true, which
    /// skips <c>BedOwnerWillShare</c> altogether and would tell the rest of the game this pawn owns a bed they
    /// do not -- the label, the bedroom thoughts and the assign list all read that. Only the out parameter is
    /// touched, which is the one thing the slot test looks at.
    ///
    /// <b>Nothing else reads it on a false answer.</b> The other caller,
    /// <c>RestUtility.GetBedSleepingSlotPosFor</c>, uses the slot only inside its <c>if (IsOwner(...))</c>
    /// branch, so a filled-in slot beside a false return reaches nothing but the test this exists for.
    ///
    /// <b>Only while they are in this bed.</b> Away from it the slot stays null, because <c>flag2</c> is false
    /// then and the test does not run -- inventing a slot for a pawn who is elsewhere would be answering a
    /// question nobody asked.
    ///
    /// <b>The argument types are not optional.</b> <c>IsOwner</c> has two overloads and a name alone throws
    /// <c>AmbiguousMatchException</c>; the out parameter needs <c>ArgumentType.Out</c> beside its type, since an
    /// attribute cannot call <c>MakeByRefType</c>.
    /// </summary>
    [HarmonyPatch(typeof(Building_Bed), nameof(Building_Bed.IsOwner),
        new[] { typeof(Pawn), typeof(int?) },
        new[] { ArgumentType.Normal, ArgumentType.Out })]
    internal static class Patch_CommunalBedSlot
    {
        [HarmonyPostfix]
        public static void Postfix(Building_Bed __instance, Pawn p, ref int? assignedSleepingSlot, bool __result)
        {
            // A real owner already has the right answer, and a slot that came back filled is not ours to move.
            if (__result || assignedSleepingSlot.HasValue || p == null)
                return;

            // <b>Two cheap tests before any of the expensive ones,</b> because this runs for every bed a search
            // walks past rather than once per sleeper. A pawn not lying in a bed cannot be the case this exists
            // for -- the slot test in CanUseBedNow is behind flag2, which is false for them -- and finding that
            // out costs an enum read. Reading the setting and the map's mark both allocate a closure apiece, so
            // the order here is what keeps a bed search from paying for this feature on every candidate.
            if (!p.Spawned || !p.GetPosture().InBed())
                return;

            if (!CommunalBeds.Enabled || !CommunalBeds.IsCommunal(__instance))
                return;

            int? occupied = null;

            if (UIGuard.Try("Beds.OccupiedSlot", () =>
                {
                    int? slot;

                    if (p.CurrentBed(out slot) == __instance)
                        occupied = slot;
                }))
                assignedSleepingSlot = occupied;
        }
    }

    /// <summary>
    /// The claim a pawn never makes for themselves, made for them by whoever carried them.
    ///
    /// <b><c>JobGiver_DeliverPawnToBed</c> claims directly, before the job exists,</b> so the wrap on
    /// <c>Toils_Bed.ClaimBedIfNonMedical</c> never sees it:
    ///
    /// <code>
    /// pawn2.ownership.ClaimBedIfNonMedical(building_Bed);
    /// Job job = JobMaker.MakeJob(JobDefOf.DeliverToBed, pawn2, building_Bed);
    /// </code>
    ///
    /// That is the route for somebody <i>carried</i> to bed -- rescued while downed, hauled to a medical bed,
    /// put there by a ritual. It was left alone deliberately at first, on the reasoning that a duty putting
    /// somebody in a bed should grant the ownership that keeps them there. <b>Aaron reversed that on
    /// 2026-08-29 for communal beds specifically:</b> the mark exists so nobody takes the bed permanently, and
    /// being carried into one is no more a claim than walking into it.
    ///
    /// <b>Scoped by a flag rather than by patching the claim outright.</b> <c>Pawn_Ownership</c> is the choke
    /// point every claim goes through, and two of the things going through it are meant to: the Assign button
    /// and the post-load repair that re-adds a pawn whose bed lost them. Refusing there unconditionally would
    /// break assigning a communal bed and would silently drop an assignment on load. The flag is up only inside
    /// this one method, so nothing else can be caught by it.
    ///
    /// <b>Cleared by a finalizer, not a postfix.</b> A postfix does not run when the original throws, and a
    /// flag stuck up would go on refusing claims the player did ask for -- for the rest of the session.
    ///
    /// Only the claim is refused. The delivery job is untouched, so they are still carried there and still
    /// sleep in it.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_DeliverPawnToBed), "TryGiveJob")]
    internal static class Patch_CommunalBedDelivery
    {
        /// <summary>True only while <c>TryGiveJob</c> is on the stack. Single-threaded, and it does not recurse.</summary>
        internal static bool Delivering;

        [HarmonyPrefix]
        public static void Prefix()
        {
            Delivering = true;
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            Delivering = false;
        }
    }

    /// <summary>
    /// Refuses the delivery claim on a communal bed, and only that one.
    ///
    /// See <see cref="Patch_CommunalBedDelivery"/> for why this is gated on a flag rather than applied to every
    /// claim. <c>false</c> is what vanilla itself returns when a claim does not happen, so the caller is being
    /// told something it already knows how to hear.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_Ownership), nameof(Pawn_Ownership.ClaimBedIfNonMedical))]
    internal static class Patch_CommunalBedDeliveryClaim
    {
        [HarmonyPrefix]
        public static bool Prefix(Building_Bed newBed, ref bool __result)
        {
            if (!Patch_CommunalBedDelivery.Delivering)
                return true;

            bool communal = false;

            if (!UIGuard.Try("Beds.DeliveryClaim",
                    () => communal = CommunalBeds.Enabled && CommunalBeds.IsCommunal(newBed)))
                return true;

            if (!communal)
                return true;

            __result = false;

            return false;
        }
    }
}
