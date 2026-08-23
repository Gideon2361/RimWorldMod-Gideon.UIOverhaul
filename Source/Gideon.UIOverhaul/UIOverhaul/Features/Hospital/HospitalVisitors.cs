using System;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Integrations;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>
    /// The hospital itself, as Colony Hospital keeps it: whether you are open, the hours you accept arrivals, the
    /// reputation you have earned and what your visitors owe.
    ///
    /// <b>Map level, where <see cref="HospitalIntegrations"/> is patient level.</b> The two are separated because
    /// they fail separately: a renamed patient status should cost a column, and a renamed reputation field should
    /// cost a readout, and neither should take the other with it.
    ///
    /// <b>Everything here is a public member of theirs.</b> <c>HospitalMapComponent</c> exposes reputation,
    /// receiving, the per-hour schedule and the default food policy as public properties and methods, and
    /// <c>HospitalUtility.CountBeds</c> is public too. That is a supported surface rather than a private field
    /// somebody found, which is what makes reading and writing it defensible at all.
    ///
    /// <b>With the mod absent, every member here answers "nothing" and nothing draws.</b> The strip is not empty
    /// in that case, it is gone.
    /// </summary>
    internal static class HospitalVisitors
    {
        private static bool resolved;

        private static MethodInfo componentOf;

        private static PropertyInfo reputation;

        private static PropertyInfo receiving;

        private static PropertyInfo patients;

        private static PropertyInfo foodPolicy;

        private static MethodInfo isReceivingHour;

        private static MethodInfo setReceivingHour;

        private static MethodInfo countBeds;

        /// <summary>Their static settings holder. A field rather than a property, which is theirs to choose.</summary>
        private static FieldInfo settings;

        private static FieldInfo defaultCare;

        private static PropertyInfo currentBill;

        internal static bool Available
        {
            get { return HospitalIntegrations.ColonyHospitalLoaded; }
        }

        /// <summary>Their component for a map, or null.</summary>
        private static object ComponentOf(Map map)
        {
            if (map == null || !Available)
                return null;

            Resolve();

            if (componentOf == null)
                return null;

            return componentOf.Invoke(map, null);
        }

        internal static int Reputation(Map map)
        {
            return UIGuard.Try("Hospital.Reputation", () =>
            {
                object component = ComponentOf(map);

                return component == null || reputation == null
                    ? 0
                    : (int) reputation.GetValue(component, null);
            }, 0, null);
        }

        internal static bool Receiving(Map map)
        {
            return UIGuard.Try("Hospital.Receiving", () =>
            {
                object component = ComponentOf(map);

                return component != null && receiving != null
                       && (bool) receiving.GetValue(component, null);
            }, false, null);
        }

        internal static void SetReceiving(Map map, bool value)
        {
            UIGuard.Try("Hospital.SetReceiving", () =>
            {
                object component = ComponentOf(map);

                if (component != null && receiving != null && receiving.CanWrite)
                    receiving.SetValue(component, value, null);
            }, "The hospital's receiving switch could not be changed from here. It can still be set in Colony "
               + "Hospital's own tab.");
        }

        internal static bool ReceivingHour(Map map, int hour)
        {
            return UIGuard.Try("Hospital.ReceivingHour", () =>
            {
                object component = ComponentOf(map);

                return component != null && isReceivingHour != null
                       && (bool) isReceivingHour.Invoke(component, new object[] { hour });
            }, false, null);
        }

        internal static void SetReceivingHour(Map map, int hour, bool value)
        {
            UIGuard.Try("Hospital.SetReceivingHour", () =>
            {
                object component = ComponentOf(map);

                if (component != null && setReceivingHour != null)
                    setReceivingHour.Invoke(component, new object[] { hour, value });
            }, "The hospital's receiving hours could not be changed from here.");
        }

        /// <summary>Beds designated as hospital beds, occupied against total.</summary>
        internal static void Beds(Map map, out int occupied, out int total)
        {
            occupied = 0;
            total = 0;

            if (!Available || map == null)
                return;

            int foundOccupied = 0;
            int foundTotal = 0;

            UIGuard.Try("Hospital.Beds", () =>
            {
                Resolve();

                if (countBeds == null)
                    return;

                object[] arguments = { map, 0, 0 };

                countBeds.Invoke(null, arguments);

                foundOccupied = (int) arguments[1];
                foundTotal = (int) arguments[2];
            }, null);

            occupied = foundOccupied;
            total = foundTotal;
        }

        /// <summary>Everything the current visitors owe between them, which is the number worth a readout.</summary>
        internal static int Owed(Map map)
        {
            return UIGuard.Try("Hospital.Owed", () =>
            {
                object component = ComponentOf(map);

                if (component == null || patients == null || currentBill == null)
                    return 0;

                System.Collections.IEnumerable records = patients.GetValue(component, null)
                    as System.Collections.IEnumerable;

                if (records == null)
                    return 0;

                int total = 0;

                foreach (object record in records)
                {
                    if (record != null)
                        total += (int) currentBill.GetValue(record, null);
                }

                return total;
            }, 0, null);
        }

        internal static FoodPolicy PatientFood(Map map)
        {
            return UIGuard.Try<FoodPolicy>("Hospital.PatientFood", () =>
            {
                object component = ComponentOf(map);

                return component == null || foodPolicy == null
                    ? null
                    : foodPolicy.GetValue(component, null) as FoodPolicy;
            }, null, null);
        }

        internal static void SetPatientFood(Map map, FoodPolicy policy)
        {
            UIGuard.Try("Hospital.SetPatientFood", () =>
            {
                object component = ComponentOf(map);

                if (component != null && foodPolicy != null && foodPolicy.CanWrite)
                    foodPolicy.SetValue(component, policy, null);
            }, "The patient food policy could not be changed from here.");
        }

        /// <summary>
        /// The medicine visitors are treated with by default.
        ///
        /// <b>A mod setting rather than a map one,</b> which is theirs to decide and worth knowing: changing it
        /// here changes it for every colony, and the strip says so rather than looking like a per-map control.
        /// </summary>
        internal static MedicalCareCategory DefaultCare()
        {
            return UIGuard.Try("Hospital.DefaultCare", () =>
            {
                Resolve();

                object holder = Settings();

                return holder == null || defaultCare == null
                    ? MedicalCareCategory.NormalOrWorse
                    : (MedicalCareCategory) defaultCare.GetValue(holder);
            }, MedicalCareCategory.NormalOrWorse, null);
        }

        internal static void SetDefaultCare(MedicalCareCategory care)
        {
            UIGuard.Try("Hospital.SetDefaultCare", () =>
            {
                Resolve();

                object holder = Settings();

                if (holder != null && defaultCare != null)
                    defaultCare.SetValue(holder, care);
            }, "Colony Hospital's default medical care could not be changed from here.");
        }

        private static object Settings()
        {
            return settings == null ? null : settings.GetValue(null);
        }

        /// <summary>
        /// Finds everything once, each member independently.
        ///
        /// A missing member costs only itself: their mod updates, and a renamed food policy should cost the food
        /// chip rather than the reputation, the beds and the receiving hours beside it.
        /// </summary>
        private static void Resolve()
        {
            if (resolved)
                return;

            resolved = true;

            if (!ModIntegrations.Loaded(HospitalIntegrations.ColonyHospitalPackageId))
                return;

            Type component = GenTypes.GetTypeInAnyAssembly("ColonyHospital.HospitalMapComponent");
            Type utility = GenTypes.GetTypeInAnyAssembly("ColonyHospital.HospitalUtility");
            Type record = GenTypes.GetTypeInAnyAssembly("ColonyHospital.PatientRecord");
            Type mod = GenTypes.GetTypeInAnyAssembly("ColonyHospital.ColonyHospitalMod");
            Type config = GenTypes.GetTypeInAnyAssembly("ColonyHospital.ColonyHospitalSettings");

            if (component != null)
            {
                // Their component is fetched through Map.GetComponent<T>, which is generic: the method has to be
                // closed over their type before it can be called at all.
                MethodInfo generic = typeof(Map).GetMethod("GetComponent", new Type[0]);

                if (generic != null)
                    componentOf = generic.MakeGenericMethod(component);

                reputation = component.GetProperty("Reputation", BindingFlags.Public | BindingFlags.Instance);
                receiving = component.GetProperty("ReceivingPatients",
                    BindingFlags.Public | BindingFlags.Instance);

                patients = component.GetProperty("Patients", BindingFlags.Public | BindingFlags.Instance);

                foodPolicy = component.GetProperty("DefaultFoodPolicy",
                    BindingFlags.Public | BindingFlags.Instance);

                isReceivingHour = component.GetMethod("IsReceivingHour",
                    BindingFlags.Public | BindingFlags.Instance);

                setReceivingHour = component.GetMethod("SetReceivingHour",
                    BindingFlags.Public | BindingFlags.Instance);
            }

            if (utility != null)
                countBeds = utility.GetMethod("CountBeds", BindingFlags.Public | BindingFlags.Static);

            if (record != null)
                currentBill = record.GetProperty("CurrentBill", BindingFlags.Public | BindingFlags.Instance);

            if (mod != null)
                settings = mod.GetField("Settings", BindingFlags.Public | BindingFlags.Static);

            if (config != null)
                defaultCare = config.GetField("defaultMedicalCare", BindingFlags.Public | BindingFlags.Instance);
        }
    }
}
