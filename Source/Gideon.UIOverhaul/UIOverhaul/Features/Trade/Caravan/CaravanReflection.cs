using System;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Gideon.UIOverhaul.Features.Trade.Caravan
{
    /// <summary>
    /// The parts of <see cref="Dialog_FormCaravan"/> that are private, reached without copying any of them.
    ///
    /// <b>Why this window is redrawn rather than replaced, unlike the trade window.</b> Trading is a view over
    /// <c>TradeSession</c>, which is a public static holding a public deal -- so owning the window there costs
    /// nothing, because the model is somewhere else entirely. The caravan dialog is the opposite: the window
    /// <i>is</i> the model. Route choosing, exit-spot finding, the error checks, the travel-supply selection, the
    /// mass and food and visibility projections and the actual formation of the caravan are all private members
    /// of that one class, a thousand lines of them, and every one is a game rule.
    ///
    /// So this feature draws over vanilla's window instead of standing up its own. The instance in the stack is
    /// RimWorld's, with its own constructor, its own <c>PostOpen</c>, its own route planner registration and its
    /// own state; a Harmony prefix takes over <c>DoWindowContents</c> and nothing else. <b>Not one caravan rule
    /// is reimplemented here</b>, which was the condition the whole design rests on, and the alternative --
    /// transcribing a thousand lines of decompiled logic into our own window -- would have broken it on the first
    /// line.
    ///
    /// <b>What it costs is this file.</b> Fifteen private members, each resolved once and each guarded
    /// independently, so a rename in a future RimWorld costs the readout that member fed rather than the window.
    /// <see cref="Ready"/> is the one gate: if the two that matter -- sending and the change notification --
    /// cannot be found, the prefix stands down and vanilla draws its own window, which is a working caravan
    /// screen.
    ///
    /// <b>Everything here is a read or a call, never a write to a rule.</b> The one field written is
    /// <c>autoSelectTravelSupplies</c>, which is a checkbox's value, and it is followed by the same
    /// <c>Notify_TransferablesChanged</c> vanilla calls after touching it.
    /// </summary>
    internal static class CaravanReflection
    {
        private static bool resolved;

        private static bool usable;

        private static MethodInfo trySend;
        private static MethodInfo notifyChanged;
        private static MethodInfo recache;
        private static MethodInfo sendEverything;

        private static PropertyInfo tilesPerDay;
        private static PropertyInfo daysWorthOfFood;
        private static PropertyInfo foragedFoodPerDay;
        private static PropertyInfo visibility;
        private static PropertyInfo ticksToArrive;
        private static PropertyInfo mustChooseRoute;
        private static PropertyInfo showCancelButton;

        private static FieldInfo reform;
        private static FieldInfo map;
        private static FieldInfo destinationTile;
        private static FieldInfo canChooseRoute;
        private static FieldInfo autoSelect;

        /// <summary>
        /// Whether the window can be taken over at all.
        ///
        /// <b>Only the two that would leave a player stuck are required.</b> Sending is the point of the window
        /// and the change notification is what keeps every projection honest; without either, our screen would be
        /// a dead end or a liar. The rest are readouts, and a missing one costs a line.
        /// </summary>
        internal static bool Available
        {
            get { return Ready(); }
        }

        private static bool Ready()
        {
            if (resolved)
                return usable;

            resolved = true;

            usable = UIGuard.Try("Caravan.Resolve", () =>
            {
                Type type = typeof(Dialog_FormCaravan);

                const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

                trySend = type.GetMethod("TrySend", Instance, null, Type.EmptyTypes, null);
                notifyChanged = type.GetMethod("Notify_TransferablesChanged", Instance, null, Type.EmptyTypes, null);
                recache = type.GetMethod("CalculateAndRecacheTransferables", Instance, null, Type.EmptyTypes, null);
                sendEverything = type.GetMethod("SetToSendEverything", Instance, null, Type.EmptyTypes, null);

                tilesPerDay = type.GetProperty("TilesPerDay", Instance);
                daysWorthOfFood = type.GetProperty("DaysWorthOfFood", Instance);
                foragedFoodPerDay = type.GetProperty("ForagedFoodPerDay", Instance);
                visibility = type.GetProperty("Visibility", Instance);
                ticksToArrive = type.GetProperty("TicksToArrive", Instance);
                mustChooseRoute = type.GetProperty("MustChooseRoute", Instance);
                showCancelButton = type.GetProperty("ShowCancelButton", Instance);

                reform = type.GetField("reform", Instance);
                map = type.GetField("map", Instance);
                destinationTile = type.GetField("destinationTile", Instance);
                canChooseRoute = type.GetField("canChooseRoute", Instance);
                autoSelect = type.GetField("autoSelectTravelSupplies", Instance);

                return trySend != null && notifyChanged != null;
            }, false,
                "The caravan window could not be taken over, so RimWorld's own is drawn instead. Forming a "
                + "caravan works exactly as it always did.");

            return usable;
        }

        // -----------------------------------------------------------------------------------------------
        // Calls
        // -----------------------------------------------------------------------------------------------

        internal static void TrySend(Dialog_FormCaravan dialog)
        {
            Call(trySend, dialog, "Caravan.TrySend", "The caravan was not sent.");
        }

        /// <summary>
        /// Tells the dialog its counts moved.
        ///
        /// <b>Every projection on this screen goes stale without it.</b> Mass, capacity, tiles per day, days of
        /// food, foraging, visibility and the arrival estimate are all cached behind dirty flags that only this
        /// method sets -- so a count changed without it leaves seven numbers describing a caravan nobody is
        /// packing. It also re-runs the travel supply selection when that is switched on, and enforces the
        /// Biotech rule about a mech following its overseer.
        /// </summary>
        internal static void NotifyChanged(Dialog_FormCaravan dialog)
        {
            Call(notifyChanged, dialog, "Caravan.NotifyChanged",
                "The caravan's projections did not update. Closing and reopening the window recalculates them.");
        }

        internal static void Recache(Dialog_FormCaravan dialog)
        {
            Call(recache, dialog, "Caravan.Recache", "The caravan list was not reset.");
        }

        internal static bool CanSendEverything
        {
            get { return Ready() && sendEverything != null; }
        }

        internal static void SendEverything(Dialog_FormCaravan dialog)
        {
            Call(sendEverything, dialog, "Caravan.SendEverything", "Nothing was selected.");
        }

        private static void Call(MethodInfo method, Dialog_FormCaravan dialog, string site, string consequence)
        {
            if (!Ready() || method == null || dialog == null)
                return;

            UIGuard.Try(site, () => method.Invoke(dialog, null), consequence);
        }

        // -----------------------------------------------------------------------------------------------
        // Readouts
        // -----------------------------------------------------------------------------------------------

        internal static float TilesPerDay(Dialog_FormCaravan dialog)
        {
            return Number(tilesPerDay, dialog, "Caravan.TilesPerDay");
        }

        internal static float Visibility(Dialog_FormCaravan dialog)
        {
            return Number(visibility, dialog, "Caravan.Visibility");
        }

        /// <summary>
        /// Days of food aboard, and how long before the first of it rots.
        ///
        /// <b>Two numbers that answer two different questions,</b> which is why vanilla computes them together:
        /// a caravan with nine days of food and three days before it spoils has three days of food.
        /// </summary>
        internal static void Food(Dialog_FormCaravan dialog, out float days, out float tillRot)
        {
            days = 0f;
            tillRot = 0f;

            if (!Ready() || daysWorthOfFood == null || dialog == null)
                return;

            ValueTuple<float, float> pair = UIGuard.Try("Caravan.Food", () =>
            {
                object value = daysWorthOfFood.GetValue(dialog, null);

                return value is ValueTuple<float, float>
                    ? (ValueTuple<float, float>) value
                    : new ValueTuple<float, float>(0f, 0f);
            }, new ValueTuple<float, float>(0f, 0f), null);

            days = pair.Item1;
            tillRot = pair.Item2;
        }

        /// <summary>What the route forages per day, and what it forages.</summary>
        internal static void Foraged(Dialog_FormCaravan dialog, out ThingDef food, out float perDay)
        {
            food = null;
            perDay = 0f;

            if (!Ready() || foragedFoodPerDay == null || dialog == null)
                return;

            ValueTuple<ThingDef, float> pair = UIGuard.Try("Caravan.Foraged", () =>
            {
                object value = foragedFoodPerDay.GetValue(dialog, null);

                return value is ValueTuple<ThingDef, float>
                    ? (ValueTuple<ThingDef, float>) value
                    : new ValueTuple<ThingDef, float>(null, 0f);
            }, new ValueTuple<ThingDef, float>(null, 0f), null);

            food = pair.Item1;
            perDay = pair.Item2;
        }

        /// <summary>
        /// How long the journey takes, or zero when no destination has been chosen.
        ///
        /// <b>Asked only once a destination exists,</b> because vanilla's own getter is only read behind that
        /// test -- <c>TicksToArrive</c> walks the route and has nothing to walk without one.
        /// </summary>
        internal static int TicksToArrive(Dialog_FormCaravan dialog)
        {
            if (!Ready() || ticksToArrive == null || dialog == null || !HasDestination(dialog))
                return 0;

            return UIGuard.Try("Caravan.TicksToArrive", () =>
            {
                object value = ticksToArrive.GetValue(dialog, null);

                return value is int ? (int) value : 0;
            }, 0, null);
        }

        internal static bool HasDestination(Dialog_FormCaravan dialog)
        {
            if (!Ready() || destinationTile == null || dialog == null)
                return false;

            return UIGuard.Try("Caravan.Destination", () =>
            {
                object value = destinationTile.GetValue(dialog);

                return value is PlanetTile && ((PlanetTile) value).Valid;
            }, false, null);
        }

        internal static bool MustChooseRoute(Dialog_FormCaravan dialog)
        {
            return Flag(mustChooseRoute, dialog, "Caravan.MustChooseRoute", false);
        }

        internal static bool ShowCancelButton(Dialog_FormCaravan dialog)
        {
            // True when it cannot be read: the alternative is a window with no way out, which is a worse failure
            // than an extra button on the rare map that is about to be removed.
            return Flag(showCancelButton, dialog, "Caravan.ShowCancel", true);
        }

        internal static bool CanChooseRoute(Dialog_FormCaravan dialog)
        {
            return Field(canChooseRoute, dialog, "Caravan.CanChooseRoute", false);
        }

        internal static bool Reform(Dialog_FormCaravan dialog)
        {
            return Field(reform, dialog, "Caravan.Reform", false);
        }

        internal static Map Map(Dialog_FormCaravan dialog)
        {
            if (!Ready() || map == null || dialog == null)
                return null;

            return UIGuard.Try("Caravan.Map", () => map.GetValue(dialog) as Map, null, null);
        }

        // -----------------------------------------------------------------------------------------------
        // The one field written
        // -----------------------------------------------------------------------------------------------

        internal static bool AutoSelectSupplies(Dialog_FormCaravan dialog)
        {
            return Field(autoSelect, dialog, "Caravan.AutoSelectRead", false);
        }

        /// <summary>
        /// Sets whether travel supplies are chosen automatically.
        ///
        /// <b>Written to the field and then announced, which is exactly what vanilla's own checkbox does.</b> The
        /// selection itself happens inside <c>Notify_TransferablesChanged</c>, so writing the flag without the
        /// call would leave the box ticked and nothing packed.
        /// </summary>
        internal static void SetAutoSelectSupplies(Dialog_FormCaravan dialog, bool value)
        {
            if (!Ready() || autoSelect == null || dialog == null)
                return;

            UIGuard.Try("Caravan.AutoSelectWrite", () =>
            {
                autoSelect.SetValue(dialog, value);

                NotifyChanged(dialog);
            }, "Automatic travel supplies were not switched.");
        }

        // -----------------------------------------------------------------------------------------------

        private static float Number(PropertyInfo property, Dialog_FormCaravan dialog, string site)
        {
            if (!Ready() || property == null || dialog == null)
                return 0f;

            return UIGuard.Try(site, () =>
            {
                object value = property.GetValue(dialog, null);

                return value is float ? (float) value : 0f;
            }, 0f, null);
        }

        private static bool Flag(PropertyInfo property, Dialog_FormCaravan dialog, string site, bool fallback)
        {
            if (!Ready() || property == null || dialog == null)
                return fallback;

            return UIGuard.Try(site, () =>
            {
                object value = property.GetValue(dialog, null);

                return value is bool ? (bool) value : fallback;
            }, fallback, null);
        }

        private static bool Field(FieldInfo field, Dialog_FormCaravan dialog, string site, bool fallback)
        {
            if (!Ready() || field == null || dialog == null)
                return fallback;

            return UIGuard.Try(site, () =>
            {
                object value = field.GetValue(dialog);

                return value is bool ? (bool) value : fallback;
            }, fallback, null);
        }
    }
}
