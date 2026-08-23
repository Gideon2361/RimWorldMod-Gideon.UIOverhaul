using System;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Integrations;
using Verse;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>
    /// What the two hospital mods know that we do not: which pawns are paying visitors rather than colonists.
    ///
    /// <b>Both are soft dependencies read by reflection, and neither is required.</b> Nothing in this file is
    /// referenced at compile time and every lookup is resolved once and cached, so an install with neither mod
    /// pays one failed type lookup on the first frame the hospital tab is opened and nothing after that.
    ///
    /// <b>Two mods, two completely different models, one question.</b> Colony Hospital
    /// (<c>Jianyuan.ColonyHospital</c>) keeps a <c>PatientRecord</c> per pawn with a status enum and a running
    /// bill; Hospital (<c>Adamas.Hospital</c>) keeps a <c>PatientData</c> with a diagnosis and a cure. All the tab
    /// needs from either is "is this person a patient of ours" plus a line of text to show, so that is all this
    /// asks for -- the less of somebody else's model we reach into, the less of it can break under us.
    ///
    /// <b>Written against what they made public.</b> Colony Hospital exposes
    /// <c>ColonyHospitalExtensions.IsHospitalPatient(Pawn)</c> and Hospital exposes
    /// <c>PatientUtility.IsPatient(Pawn, out HospitalMapComponent, bool)</c>. Both are public static methods
    /// their own code calls, so they are the supported way in rather than a private field we happened to find.
    /// </summary>
    internal static class HospitalIntegrations
    {
        internal const string ColonyHospitalPackageId = "jianyuan.colonyhospital";

        internal const string HospitalPackageId = "adamas.hospital";

        /// <summary>Colony Hospital's own tab, which ours takes over from when both are installed.</summary>
        internal const string ColonyHospitalTabDefName = "CH_Hospital";

        private static bool resolved;

        private static MethodInfo colonyHospitalIsPatient;

        private static MethodInfo colonyHospitalComponentFor;

        private static MethodInfo colonyHospitalGetRecord;

        private static MethodInfo colonyHospitalStatusLabel;

        private static MethodInfo colonyHospitalDetermineStatus;

        private static PropertyInfo colonyHospitalCurrentBill;

        private static MethodInfo hospitalIsPatient;

        /// <summary>Whether either mod is loaded, so the tab knows whether visiting patients exist at all.</summary>
        internal static bool AnyHospitalMod
        {
            get
            {
                return ModIntegrations.Loaded(ColonyHospitalPackageId)
                       || ModIntegrations.Loaded(HospitalPackageId);
            }
        }

        internal static bool ColonyHospitalLoaded
        {
            get { return ModIntegrations.Loaded(ColonyHospitalPackageId); }
        }

        /// <summary>
        /// Whether this pawn is a hospital mod's patient: somebody who came here to be treated.
        ///
        /// <b>This is the whole reason the integration exists.</b> A visiting patient is usually healthy by the
        /// time you look at them and belongs to another faction, so every rule the tab uses to decide who is
        /// worth listing would drop them -- and they are the one person on the map who is unambiguously the
        /// hospital's business.
        /// </summary>
        internal static bool IsVisitingPatient(Pawn pawn)
        {
            if (pawn == null || !AnyHospitalMod)
                return false;

            return UIGuard.Try("Hospital.IsVisitingPatient", () =>
            {
                Resolve();

                if (colonyHospitalIsPatient != null
                    && (bool) colonyHospitalIsPatient.Invoke(null, new object[] { pawn }))
                    return true;

                if (hospitalIsPatient == null)
                    return false;

                // The out parameter is the map component, which we do not want; the array is only here because
                // Invoke insists on a slot for it.
                object[] arguments = { pawn, null, false };

                return (bool) hospitalIsPatient.Invoke(null, arguments);
            }, false, "Visiting patients from the hospital mods are not listed in the hospital tab.");
        }

        /// <summary>
        /// A visiting patient's status in one word, from whichever mod owns them, or null.
        ///
        /// Only Colony Hospital has a status worth showing; Hospital's <c>PatientData</c> carries a diagnosis and
        /// a bill rather than a state machine, and inventing a status for it would be putting words in its mouth.
        /// </summary>
        internal static string StatusOf(Pawn pawn)
        {
            if (pawn == null || !ColonyHospitalLoaded)
                return null;

            return UIGuard.Try<string>("Hospital.StatusOf", () =>
            {
                Resolve();

                object record = RecordFor(pawn);

                if (record == null || colonyHospitalDetermineStatus == null
                                   || colonyHospitalStatusLabel == null)
                    return null;

                object status = colonyHospitalDetermineStatus.Invoke(record, null);

                return colonyHospitalStatusLabel.Invoke(null, new[] { status }) as string;
            }, null, null);
        }

        /// <summary>What this patient owes so far, or a negative number when nobody is billing them.</summary>
        internal static int BillOf(Pawn pawn)
        {
            if (pawn == null || !ColonyHospitalLoaded)
                return -1;

            return UIGuard.Try("Hospital.BillOf", () =>
            {
                Resolve();

                object record = RecordFor(pawn);

                if (record == null || colonyHospitalCurrentBill == null)
                    return -1;

                return (int) colonyHospitalCurrentBill.GetValue(record, null);
            }, -1, null);
        }

        /// <summary>Colony Hospital's record for a pawn, through its own component lookup.</summary>
        private static object RecordFor(Pawn pawn)
        {
            if (colonyHospitalComponentFor == null || colonyHospitalGetRecord == null)
                return null;

            object component = colonyHospitalComponentFor.Invoke(null, new object[] { pawn });

            return component == null ? null : colonyHospitalGetRecord.Invoke(component, new object[] { pawn });
        }

        /// <summary>
        /// Finds everything once.
        ///
        /// <b>Every member is looked up independently and a missing one costs only itself.</b> These mods update,
        /// and a renamed status helper should cost the status column rather than the whole integration -- which
        /// is why this does not bail on the first null.
        /// </summary>
        private static void Resolve()
        {
            if (resolved)
                return;

            resolved = true;

            if (ModIntegrations.Loaded(ColonyHospitalPackageId))
            {
                Type extensions = GenTypes.GetTypeInAnyAssembly("ColonyHospital.ColonyHospitalExtensions");
                Type utility = GenTypes.GetTypeInAnyAssembly("ColonyHospital.HospitalUtility");
                Type text = GenTypes.GetTypeInAnyAssembly("ColonyHospital.CHText");
                Type component = GenTypes.GetTypeInAnyAssembly("ColonyHospital.HospitalMapComponent");
                Type record = GenTypes.GetTypeInAnyAssembly("ColonyHospital.PatientRecord");

                if (extensions != null)
                    colonyHospitalIsPatient = extensions.GetMethod("IsHospitalPatient",
                        BindingFlags.Public | BindingFlags.Static);

                if (utility != null)
                    colonyHospitalComponentFor = utility.GetMethod("ComponentFor",
                        BindingFlags.Public | BindingFlags.Static);

                // StatusLabel lives on CHText rather than on HospitalUtility, which is where 14157 looked for it
                // and quietly found nothing. Their text and their logic are separate classes, and the status
                // column is text.
                if (text != null)
                    colonyHospitalStatusLabel = text.GetMethod("StatusLabel",
                        BindingFlags.Public | BindingFlags.Static);

                if (component != null)
                    colonyHospitalGetRecord = component.GetMethod("GetRecord",
                        BindingFlags.Public | BindingFlags.Instance);

                if (record != null)
                {
                    colonyHospitalDetermineStatus = record.GetMethod("DetermineStatus",
                        BindingFlags.Public | BindingFlags.Instance);

                    colonyHospitalCurrentBill = record.GetProperty("CurrentBill",
                        BindingFlags.Public | BindingFlags.Instance);
                }
            }

            if (!ModIntegrations.Loaded(HospitalPackageId))
                return;

            Type patients = GenTypes.GetTypeInAnyAssembly("Hospital.Utilities.PatientUtility");

            if (patients == null)
                return;

            MethodInfo[] candidates = patients.GetMethods(BindingFlags.Public | BindingFlags.Static);

            for (int i = 0; i < candidates.Length; i++)
            {
                // Matched on shape rather than on an exact signature: the out parameter's type is one of theirs,
                // which we cannot name here, and the third argument has a default that a future version may drop.
                if (candidates[i].Name != "IsPatient" || candidates[i].GetParameters().Length != 3)
                    continue;

                hospitalIsPatient = candidates[i];

                break;
            }
        }
    }
}
