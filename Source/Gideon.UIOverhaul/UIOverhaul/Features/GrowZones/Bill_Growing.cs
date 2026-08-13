using System;
using Gideon.UIFramework.Helpers;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones
{
    /// <summary>
    /// A growing-zone bill. Rides on Bill_Production for targetCount, playerCustomName, paused and
    /// persistence, but supplies its own label, satisfaction test and row controls because the
    /// vanilla ones are driven by the recipe's product counter, which a plant bill has no use for.
    /// </summary>
    public class Bill_Growing : Bill_Production
    {
        public ThingDef plantDef;

        public Bill_Growing()
        {
        }

        public Bill_Growing(ThingDef plantDef)
            : base(GZPDefOf.GZP_GrowPlant, null)
        {
            this.plantDef = plantDef;
            repeatMode = BillRepeatModeDefOf.Forever;
        }

        public Zone_GrowingPlus Zone => billStack?.billGiver as Zone_GrowingPlus;

        /// <summary>
        /// Guarded because vanilla's bill UI reads this while drawing, and a null plant def is not the only way a
        /// def reference can go wrong -- a mod removed mid-save leaves one that resolves to an object whose label
        /// throws.
        /// </summary>
        public override string Label => UIGuard.Try("GrowZones.BillLabel",
            () => plantDef == null ? "Grow (missing plant)" : $"Grow {plantDef.label}",
            "Grow (unreadable plant)");

        /// <summary>Copy/paste shares one global clipboard with workbench bills, so a grow bill
        /// could be pasted onto a worktable. Disabled until the tab owns its own copy path.</summary>
        protected override bool CanCopy => false;

        public override bool CompletableEver => repeatMode != BillRepeatModeDefOf.Forever;

        /// <summary>
        /// Guarded because it counts colony stock, which is a good deal more than a field read, and because vanilla
        /// asks for it while drawing the bill.
        /// </summary>
        protected override string StatusString => UIGuard.Try("GrowZones.BillStatus", () =>
        {
            if (repeatMode == BillRepeatModeDefOf.Forever || plantDef == null)
                return null;

            Zone_GrowingPlus zone = Zone;
            return zone == null ? null : $"{CurrentCountCached(zone)} / {targetCount}";
        }, null, "A bill shows no progress figure. The bill itself still runs normally.");

        protected override float StatusLineMinHeight => StatusString.NullOrEmpty() ? 0f : 24f;

        /// <summary>
        /// Edit buffer for the typed target field, as Widgets.TextFieldNumeric requires. UI state
        /// only -- deliberately not saved, and cleared whenever targetCount changes by any route
        /// other than typing: TextFieldNumeric renders from the buffer while it is non-null, so
        /// stale text would overwrite the new value on the next frame.
        ///
        /// One field, shared by every drawing path. There were briefly two, and the row in
        /// <see cref="UI.GrowBillRow"/> kept rendering from its own stale copy after a repeat-mode
        /// change cleared only the other one.
        /// </summary>
        public string targetCountBuffer;

        /// <summary>
        /// Refresh interval in real seconds. Measured against the wall clock rather than game
        /// ticks so the rate holds steady across every game speed: RimWorld speeds up by running
        /// more ticks per frame, not by scaling Unity's clock, so a tick budget would rescan three
        /// times as often at 3x and not at all while paused.
        /// </summary>
        private const float CountCacheSeconds = 5f;

        private bool countCached;
        private int cachedCount;
        private float elapsedSinceScan;
        private float lastObservedTime;

        /// <summary>
        /// <see cref="CurrentCount"/> throttled to one rescan per <see cref="CountCacheTicks"/>.
        /// The nutrition modes walk every counted resource on the map, and the row reads this twice
        /// a frame, so an unthrottled call is hundreds of full scans per second.
        ///
        /// Display only. <see cref="ShouldDoNow"/> deliberately uses the uncached
        /// <see cref="CurrentCount"/> so sowing decisions are never made on stale numbers.
        /// </summary>
        public int CurrentCountCached(Zone_GrowingPlus zone)
        {
            float now = Time.realtimeSinceStartup;

            if (!countCached)
            {
                Rescan(zone, now);
                return cachedCount;
            }

            float delta = now - lastObservedTime;
            lastObservedTime = now;

            // Accumulate only while the game is running, so pausing freezes the interval rather
            // than draining it. Checking TickManager.Paused rather than comparing tick counts
            // matters: at high frame rates several frames share a tick, and treating those as
            // paused would stretch the interval well past its nominal length.
            // delta <= 0 guards a clock that ran backwards.
            if (delta > 0f && !GamePaused)
                elapsedSinceScan += delta;

            if (elapsedSinceScan >= CountCacheSeconds)
                Rescan(zone, now);

            return cachedCount;
        }

        private static bool GamePaused => Find.TickManager == null || Find.TickManager.Paused;

        private void Rescan(Zone_GrowingPlus zone, float now)
        {
            cachedCount = CurrentCount(zone);
            elapsedSinceScan = 0f;
            lastObservedTime = now;
            countCached = true;
        }

        /// <summary>
        /// Forces the next <see cref="CurrentCountCached"/> to rescan. Needed whenever the meaning
        /// of the count changes rather than its value -- switching between harvested stock and the
        /// nutrition modes, for instance.
        /// </summary>
        public void InvalidateCountCache() => countCached = false;

        /// <summary>
        /// The quantity this bill watches. Which quantity depends on the repeat mode: harvested
        /// stock for TargetCount, map-wide nutrition for the two custom modes.
        /// </summary>
        public int CurrentCount(Zone_GrowingPlus zone)
        {
            if (repeatMode == GZPDefOf.GZP_NutritionBelow)
                return zone.CountTotalNutrition(zone.Map);

            if (repeatMode == GZPDefOf.GZP_PlantNutritionBelow)
                return zone.CountTotalNutritionFromPlants(zone.Map);

            ThingDef harvested = plantDef?.plant?.harvestedThingDef;
            return harvested == null ? 0 : zone.CountPlayerStockOf(harvested, zone.Map);
        }

        /// <summary>
        /// <b>Guarded, and it answers false when it fails.</b> This is on the sowing path, so a fault here would
        /// otherwise escape into pawn job assignment.
        ///
        /// False rather than true is the deliberate choice: it parks this one bill, leaving the colony working and
        /// every other bill unaffected. Answering true would let a bill whose target count could not be read sow
        /// without limit, which is a silent gameplay change rather than a visible missing one.
        ///
        /// Written out rather than through <c>UIGuard.Try</c> because this is asked once per bill per work scan, and
        /// passing a method group as a <c>Func</c> allocates a delegate on every call. A try block that does not
        /// throw costs nothing.
        /// </summary>
        public override bool ShouldDoNow()
        {
            try
            {
                return ShouldDoNowInner();
            }
            catch (Exception ex)
            {
                UIGuard.Report("GrowZones.ShouldDoNow", ex,
                    "This bill is treated as satisfied and nothing is sown for it. Other bills are unaffected.");
                return false;
            }
        }

        private bool ShouldDoNowInner()
        {
            if (suspended || plantDef == null)
                return false;

            if (repeatMode == BillRepeatModeDefOf.Forever)
                return true;

            Zone_GrowingPlus zone = Zone;
            if (zone == null)
                return false;

            if (CurrentCount(zone) >= targetCount)
            {
                paused = true;
                return false;
            }

            // Auto-Unsuspend off means a satisfied bill stays parked until the player resumes it.
            if (paused && !zone.AutoUnsuspendActive)
                return false;

            paused = false;
            return true;
        }

        // No DoConfigInterface override. Bill.DoInterface is not virtual and its chrome is fixed, so
        // the whole row is drawn by UI.GrowBillRow instead and ITab_GrowthZoneBills never calls
        // DoInterface -- an override here would be a second copy of the target-field layout that
        // nothing renders and that would silently drift from the one that does. If some other code
        // path ever does reach DoInterface, Bill_Production's own config UI draws.
        //
        // RepeatModeLabel and RepeatModeOptions below are still live: GrowBillRow and
        // MainTabWindow_GrowZones both read them.

        public string RepeatModeLabel
        {
            get
            {
                if (repeatMode == BillRepeatModeDefOf.Forever) return "Grow Forever";
                if (repeatMode == GZPDefOf.GZP_NutritionBelow) return "Nutrition < X";
                if (repeatMode == GZPDefOf.GZP_PlantNutritionBelow) return "Plant-Nut < X";
                return "Grow Until X";
            }
        }

        public List<FloatMenuOption> RepeatModeOptions()
        {
            // Wrapped because a FloatMenuOption's action runs when the player picks it, from inside the menu's own
            // drawing -- not from here. There is no frame of ours on that stack, so this is the only place the
            // guard can be attached.
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Grow Forever", UIGuard.Wrap("GrowZones.SetRepeatMode",
                    () => SetRepeatMode(BillRepeatModeDefOf.Forever),
                    "The bill keeps the repeat mode it already had."))
            };

            bool harvestable = plantDef?.plant != null && plantDef.plant.harvestYield > 0f;
            if (!harvestable)
                return options;

            options.Add(new FloatMenuOption("Grow Until X", UIGuard.Wrap("GrowZones.SetRepeatMode",
                () => SetRepeatMode(BillRepeatModeDefOf.TargetCount),
                "The bill keeps the repeat mode it already had.")));

            if (plantDef.plant.harvestedThingDef != null &&
                plantDef.plant.harvestedThingDef.IsNutritionGivingIngestible)
            {
                options.Add(new FloatMenuOption("Grow If Nutrition < X", UIGuard.Wrap(
                    "GrowZones.SetRepeatMode", () => SetRepeatMode(GZPDefOf.GZP_NutritionBelow),
                    "The bill keeps the repeat mode it already had.")));
                options.Add(new FloatMenuOption("Grow If Nutrition From Plants < X", UIGuard.Wrap(
                    "GrowZones.SetRepeatMode", () => SetRepeatMode(GZPDefOf.GZP_PlantNutritionBelow),
                    "The bill keeps the repeat mode it already had.")));
            }

            return options;
        }

        private void SetRepeatMode(BillRepeatModeDef mode)
        {
            if (repeatMode == mode)
                return;
            repeatMode = mode;
            paused = false;
            // Units differ between modes (harvested stock vs. nutrition), so drop any typed text.
            targetCountBuffer = null;
            // Same reason: the cached figure is not merely stale, it measures the wrong quantity.
            InvalidateCountCache();
            Zone?.UpdatePlantDefToGrow();
        }

        public override Bill Clone()
        {
            Bill_Growing clone = (Bill_Growing)base.Clone();
            clone.plantDef = plantDef;
            return clone;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref plantDef, "plantDef");
        }
    }
}
