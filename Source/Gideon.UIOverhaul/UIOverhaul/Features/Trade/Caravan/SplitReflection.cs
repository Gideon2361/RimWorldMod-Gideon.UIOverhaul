using System;
using System.Collections.Generic;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using RimWorld;
using RimWorld.Planet;
using Verse;
using PlanetCaravan = RimWorld.Planet.Caravan;

namespace Gideon.UIOverhaul.Features.Trade.Caravan
{
    /// <summary>
    /// The parts of <see cref="Dialog_SplitCaravan"/> that are private, which is all of them.
    ///
    /// <b>Even more closed than the form dialog.</b> There, at least <c>transferables</c>, <c>MassUsage</c> and
    /// <c>MassCapacity</c> were public; here the class exposes nothing but <c>InitialSize</c> and the overrides
    /// it inherits. So the same decision applies with less room to argue about it: the window is drawn over
    /// rather than replaced, and every caravan rule -- the split itself, the two colonist checks, the inventory
    /// hand-off -- stays exactly where RimWorld put it. See <see cref="CaravanReflection"/> for the reasoning at
    /// length.
    ///
    /// <b>The list is read fresh every time, never cached.</b> <c>CalculateAndRecacheTransferables</c> assigns a
    /// brand new <c>List&lt;TransferableOneWay&gt;</c> rather than clearing the old one, so a reference held
    /// across a reset would be a list of dead rows that still drew, still took counts, and affected nothing.
    /// That is the one trap in this file.
    ///
    /// <b>Two of everything, because this screen has two caravans.</b> Mass, speed, food, foraging and visibility
    /// all exist twice -- once for what stays and once for what goes -- and that duplication is the reason this
    /// window is worth redrawing at all. Vanilla computes both sets and shows them as two rows of small numbers
    /// above a tab strip; ours puts them side by side, which is the comparison the player came to make.
    /// </summary>
    internal static class SplitReflection
    {
        private static bool resolved;

        private static bool usable;

        private static MethodInfo trySplit;
        private static MethodInfo recache;
        private static MethodInfo countChanged;

        private static FieldInfo caravanField;
        private static FieldInfo transferablesField;

        private static PropertyInfo sourceMassUsage;
        private static PropertyInfo sourceMassCapacity;
        private static PropertyInfo sourceTilesPerDay;
        private static PropertyInfo sourceFood;
        private static PropertyInfo sourceForaged;
        private static PropertyInfo sourceVisibility;

        private static PropertyInfo destMassUsage;
        private static PropertyInfo destMassCapacity;
        private static PropertyInfo destTilesPerDay;
        private static PropertyInfo destFood;
        private static PropertyInfo destForaged;
        private static PropertyInfo destVisibility;

        private static PropertyInfo ticksToArrive;

        /// <summary>
        /// Whether the window can be taken over.
        ///
        /// <b>Three members are required and the rest are readouts.</b> Without the transferables there is
        /// nothing to draw; without the split there is no way to finish; without the change notification every
        /// projection on the screen would describe a caravan nobody is packing. A missing readout costs a line.
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

            usable = UIGuard.Try("Split.Resolve", () =>
            {
                Type type = typeof(Dialog_SplitCaravan);

                const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

                trySplit = type.GetMethod("TrySplitCaravan", Instance, null, Type.EmptyTypes, null);
                recache = type.GetMethod("CalculateAndRecacheTransferables", Instance, null, Type.EmptyTypes, null);
                countChanged = type.GetMethod("CountToTransferChanged", Instance, null, Type.EmptyTypes, null);

                caravanField = type.GetField("caravan", Instance);
                transferablesField = type.GetField("transferables", Instance);

                sourceMassUsage = type.GetProperty("SourceMassUsage", Instance);
                sourceMassCapacity = type.GetProperty("SourceMassCapacity", Instance);
                sourceTilesPerDay = type.GetProperty("SourceTilesPerDay", Instance);
                sourceFood = type.GetProperty("SourceDaysWorthOfFood", Instance);
                sourceForaged = type.GetProperty("SourceForagedFoodPerDay", Instance);
                sourceVisibility = type.GetProperty("SourceVisibility", Instance);

                destMassUsage = type.GetProperty("DestMassUsage", Instance);
                destMassCapacity = type.GetProperty("DestMassCapacity", Instance);
                destTilesPerDay = type.GetProperty("DestTilesPerDay", Instance);
                destFood = type.GetProperty("DestDaysWorthOfFood", Instance);
                destForaged = type.GetProperty("DestForagedFoodPerDay", Instance);
                destVisibility = type.GetProperty("DestVisibility", Instance);

                ticksToArrive = type.GetProperty("TicksToArrive", Instance);

                return trySplit != null && countChanged != null && transferablesField != null;
            }, false,
                "The split-caravan window could not be taken over, so RimWorld's own is drawn instead. Splitting "
                + "a caravan works exactly as it always did.");

            return usable;
        }

        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// The rows, read fresh.
        ///
        /// Never held between frames: see the note on the class. An empty list rather than null on any failure,
        /// so a caller can draw a table with nothing in it instead of testing first.
        /// </summary>
        internal static List<TransferableOneWay> Transferables(Dialog_SplitCaravan dialog)
        {
            if (!Ready() || transferablesField == null || dialog == null)
                return new List<TransferableOneWay>();

            return UIGuard.Try("Split.Transferables",
                () => transferablesField.GetValue(dialog) as List<TransferableOneWay>
                      ?? new List<TransferableOneWay>(),
                new List<TransferableOneWay>(), null);
        }

        internal static PlanetCaravan Caravan(Dialog_SplitCaravan dialog)
        {
            if (!Ready() || caravanField == null || dialog == null)
                return null;

            return UIGuard.Try("Split.Caravan", () => caravanField.GetValue(dialog) as PlanetCaravan, null, null);
        }

        /// <summary>Performs the split. True when it went through, which is when the window should close.</summary>
        internal static bool TrySplit(Dialog_SplitCaravan dialog)
        {
            if (!Ready() || trySplit == null || dialog == null)
                return false;

            return UIGuard.Try("Split.TrySplit", () =>
            {
                object result = trySplit.Invoke(dialog, null);

                return result is bool && (bool) result;
            }, false, "The caravan was not split.");
        }

        internal static void Recache(Dialog_SplitCaravan dialog)
        {
            Call(recache, dialog, "Split.Recache", "The list was not reset.");
        }

        /// <summary>
        /// Tells the dialog its counts moved.
        ///
        /// Thirteen dirty flags, all set here and nowhere else -- so a count changed without it leaves both
        /// caravans' mass, speed, food, foraging and visibility describing a split nobody is making.
        /// </summary>
        internal static void NotifyChanged(Dialog_SplitCaravan dialog)
        {
            Call(countChanged, dialog, "Split.NotifyChanged",
                "The two caravans' projections did not update. Closing and reopening the window recalculates "
                + "them.");
        }

        private static void Call(MethodInfo method, Dialog_SplitCaravan dialog, string site, string consequence)
        {
            if (!Ready() || method == null || dialog == null)
                return;

            UIGuard.Try(site, () => method.Invoke(dialog, null), consequence);
        }

        // -----------------------------------------------------------------------------------------------
        // The two sides
        // -----------------------------------------------------------------------------------------------

        /// <summary>Everything one side of the split projects, gathered so a caller reads it once.</summary>
        internal struct Side
        {
            internal float MassUsage;
            internal float MassCapacity;
            internal float TilesPerDay;
            internal float Days;
            internal float TillRot;
            internal float Foraged;
            internal ThingDef ForagedFood;
            internal float Visibility;

            internal bool Over
            {
                get { return MassUsage > MassCapacity; }
            }
        }

        /// <summary>What stays behind.</summary>
        internal static Side Staying(Dialog_SplitCaravan dialog)
        {
            return Read(dialog, sourceMassUsage, sourceMassCapacity, sourceTilesPerDay, sourceFood, sourceForaged,
                sourceVisibility, "Split.Staying");
        }

        /// <summary>What leaves.</summary>
        internal static Side Going(Dialog_SplitCaravan dialog)
        {
            return Read(dialog, destMassUsage, destMassCapacity, destTilesPerDay, destFood, destForaged,
                destVisibility, "Split.Going");
        }

        private static Side Read(Dialog_SplitCaravan dialog, PropertyInfo mass, PropertyInfo capacity,
            PropertyInfo speed, PropertyInfo food, PropertyInfo foraged, PropertyInfo visibility, string site)
        {
            Side side = new Side();

            if (!Ready() || dialog == null)
                return side;

            UIGuard.Try(site, () =>
            {
                side.MassUsage = Number(mass, dialog);
                side.MassCapacity = Number(capacity, dialog);
                side.TilesPerDay = Number(speed, dialog);
                side.Visibility = Number(visibility, dialog);

                if (food != null)
                {
                    object value = food.GetValue(dialog, null);

                    if (value is ValueTuple<float, float>)
                    {
                        ValueTuple<float, float> pair = (ValueTuple<float, float>) value;

                        side.Days = pair.Item1;
                        side.TillRot = pair.Item2;
                    }
                }

                if (foraged == null)
                    return;

                object forage = foraged.GetValue(dialog, null);

                if (!(forage is ValueTuple<ThingDef, float>))
                    return;

                ValueTuple<ThingDef, float> gathered = (ValueTuple<ThingDef, float>) forage;

                side.ForagedFood = gathered.Item1;
                side.Foraged = gathered.Item2;
            }, null);

            return side;
        }

        /// <summary>
        /// How long the caravan that stays has left on its journey, or zero when it is not travelling.
        ///
        /// Vanilla's own getter already answers zero for a stationary caravan, so this needs no test of its own.
        /// </summary>
        internal static int TicksToArrive(Dialog_SplitCaravan dialog)
        {
            if (!Ready() || ticksToArrive == null || dialog == null)
                return 0;

            return UIGuard.Try("Split.TicksToArrive", () =>
            {
                object value = ticksToArrive.GetValue(dialog, null);

                return value is int ? (int) value : 0;
            }, 0, null);
        }

        private static float Number(PropertyInfo property, Dialog_SplitCaravan dialog)
        {
            if (property == null)
                return 0f;

            object value = property.GetValue(dialog, null);

            return value is float ? (float) value : 0f;
        }
    }
}
