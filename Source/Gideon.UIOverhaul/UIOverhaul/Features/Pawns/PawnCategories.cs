using System;
using System.Collections.Generic;
using System.Reflection;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>Which kind of person a row is about.</summary>
    internal enum PawnCategory
    {
        Colonist,
        Prisoner,
        Slave,

        /// <summary>Someone being treated, when a hospital mod is providing them.</summary>
        Patient,

        /// <summary>A visitor lodging with the colony, when Hospitality is providing them.</summary>
        Guest,

        /// <summary>
        /// Something one of the colony's necromancers is controlling, when One with Death is providing them.
        ///
        /// Not necessarily a person: a raised animal is undead too, which is why this category's members are
        /// gathered from the necromancer's own list rather than sifted out of the humanlike pawns like the rest.
        /// </summary>
        Undead
    }

    /// <summary>
    /// The pawn categories the tab can list, which of them are switched on, and how to tell them apart.
    ///
    /// <b>Two of the five come from other mods,</b> and both are asked for through their own public API by
    /// reflection rather than reimplemented. Guessing at "a non-colonist humanlike in a medical bed" would
    /// disagree with the mod that owns the concept the moment it changed its mind, and the disagreement would
    /// show up as pawns appearing and vanishing from a filter for no visible reason. Resolved once and cached;
    /// absent when the mod is not loaded, which is what makes those two filters disappear rather than sit there
    /// permanently empty.
    ///
    /// <b>The filters persist,</b> because a filter is a view preference and re-hiding prisoners every time the
    /// tab opens is the sort of thing that makes somebody stop using the filter at all.
    /// </summary>
    internal static class PawnCategories
    {
        /// <summary>Every category, in the order the filter bar shows them.</summary>
        internal static readonly PawnCategory[] All =
        {
            PawnCategory.Colonist,
            PawnCategory.Prisoner,
            PawnCategory.Slave,
            PawnCategory.Patient,
            PawnCategory.Guest,
            PawnCategory.Undead
        };

        /// <summary>
        /// Hospitality's own guest test, or null when it is not loaded.
        ///
        /// <c>Hospitality.Utilities.GuestUtility.IsGuest</c> is a public static predicate over a pawn, which is
        /// exactly the shape needed, so nothing here has to know how Hospitality decides.
        /// </summary>
        private static Func<Pawn, bool> isGuest;

        /// <summary>
        /// Colony Hospital's own patient test, or null when it is not loaded.
        ///
        /// <c>ColonyHospital.ColonyHospitalExtensions.IsHospitalPatient</c>, which asks the map's own hospital
        /// component whether it is holding this pawn.
        /// </summary>
        private static Func<Pawn, bool> isPatient;

        private static bool resolved;

        /// <summary>
        /// Binds the two modded predicates, once.
        ///
        /// <b>Deferred rather than done in a static constructor,</b> because the assemblies these live in are
        /// loaded by RimWorld alongside ours and the order between mods is not something to depend on. The first
        /// draw of this tab is long after every assembly is in memory.
        /// </summary>
        private static void Resolve()
        {
            if (resolved)
                return;

            resolved = true;

            isGuest = Bind("Hospitality.Utilities.GuestUtility", "IsGuest");
            isPatient = Bind("ColonyHospital.ColonyHospitalExtensions", "IsHospitalPatient");
        }

        /// <summary>
        /// A one-pawn predicate from another mod, as a delegate, or null if anything about it is not as expected.
        ///
        /// Built into a delegate rather than invoked reflectively per call: this is asked once per pawn per
        /// rebuild, and <c>MethodInfo.Invoke</c> at that rate allocates an argument array every time.
        /// </summary>
        private static Func<Pawn, bool> Bind(string typeName, string method)
        {
            return UIGuard.Try("Pawns.BindCategory." + method, () =>
            {
                Type owner = GenTypes.GetTypeInAnyAssembly(typeName);

                if (owner == null)
                    return null;

                MethodInfo found = owner.GetMethod(method, BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(Pawn) }, null);

                if (found == null || found.ReturnType != typeof(bool))
                    return null;

                return (Func<Pawn, bool>) Delegate.CreateDelegate(typeof(Func<Pawn, bool>), found);
            }, null, "One pawn category is unavailable and its filter is hidden.");
        }

        /// <summary>
        /// Whether this category can appear at all, which is what decides if its filter button is drawn.
        ///
        /// A button for a category nothing can ever fill is worse than no button: it reads as a filter that is
        /// switched on and matching nothing, which sends somebody looking for the pawns it is hiding.
        /// </summary>
        internal static bool Available(PawnCategory category)
        {
            Resolve();

            switch (category)
            {
                case PawnCategory.Slave:
                    return ModsConfig.IdeologyActive;

                case PawnCategory.Patient:
                    return isPatient != null;

                case PawnCategory.Guest:
                    return isGuest != null;

                case PawnCategory.Undead:
                    return Integrations.OneWithDeathIntegration.Available;

                default:
                    return true;
            }
        }

        internal static string Label(PawnCategory category)
        {
            switch (category)
            {
                case PawnCategory.Colonist: return "Colonists";
                case PawnCategory.Prisoner: return "Prisoners";
                case PawnCategory.Slave: return "Slaves";
                case PawnCategory.Patient: return "Patients";
                case PawnCategory.Undead: return "Undead";
                default: return "Guests";
            }
        }

        /// <summary>
        /// The colour a category is marked with, from palette roles so a theme can restate them.
        ///
        /// <b>Slaves share <c>Mood</c> with the mental break badge,</b> rather than a second purple being added
        /// beside it. One purple in the theme means a palette that retunes it stays internally consistent; two
        /// would drift the first time either was adjusted.
        ///
        /// <b>Undead takes <c>Info</c>,</b> which was the last unused role in the palette. A grey green would suit
        /// the subject better and would mean adding a role to every palette, including the ones other people
        /// write, to colour one filter chip. Reusing a role costs nothing and stays correct under a retheme.
        /// </summary>
        internal static Color Color(PawnCategory category, UIColorPaletteDef palette)
        {
            switch (category)
            {
                case PawnCategory.Colonist: return palette.Accent;
                case PawnCategory.Prisoner: return palette.Warning;
                case PawnCategory.Slave: return palette.Mood;
                case PawnCategory.Patient: return palette.Danger;
                case PawnCategory.Undead: return palette.Info;
                default: return palette.Success;
            }
        }

        /// <summary>
        /// Which category a pawn belongs to.
        ///
        /// <b>Ordered most specific first, because the tests overlap.</b> A slave and a prisoner are both held
        /// by the colony, and the modded predicates answer about pawns of other factions who may also satisfy a
        /// looser vanilla test. Asking in this order means each pawn lands in exactly one bucket and no pawn is
        /// listed twice.
        /// </summary>
        internal static PawnCategory Of(Pawn pawn)
        {
            Resolve();

            // First, because it is the most definite test here and the only one that is not an inference. The
            // others read a pawn's state and conclude something; this one asks a necromancer's own list whether it
            // is holding this pawn. A raised colonist would otherwise answer to Colonist and never reach Undead.
            if (Integrations.OneWithDeathIntegration.IsControlledUndead(pawn))
                return PawnCategory.Undead;

            if (pawn.IsSlaveOfColony)
                return PawnCategory.Slave;

            if (pawn.IsPrisonerOfColony)
                return PawnCategory.Prisoner;

            if (isPatient != null && Ask(isPatient, pawn))
                return PawnCategory.Patient;

            if (isGuest != null && Ask(isGuest, pawn))
                return PawnCategory.Guest;

            return PawnCategory.Colonist;
        }

        /// <summary>
        /// Runs another mod's predicate, treating a fault as "no".
        ///
        /// <b>Guarded per call rather than per rebuild.</b> These run against pawns in states their author may
        /// not have anticipated -- a corpse being carried, a pawn mid-teleport between maps -- and one throwing
        /// must cost that pawn's category rather than the whole tab.
        /// </summary>
        private static bool Ask(Func<Pawn, bool> predicate, Pawn pawn)
        {
            return UIGuard.Try("Pawns.AskCategory", () => predicate(pawn), false,
                "One pawn is listed as a colonist because another mod could not classify them.");
        }

        // -------------------------------------------------------------------------------------------
        // Which are shown
        // -------------------------------------------------------------------------------------------

        private const char Separator = ',';

        private static HashSet<string> hidden;
        private static string builtFrom;

        /// <summary>
        /// The categories currently switched off, read from settings.
        ///
        /// <b>Stored as what is hidden rather than what is shown,</b> so the default -- an empty setting -- means
        /// everything is visible. Storing the shown set would make a fresh install and a player who had hidden
        /// everything look identical, and the fresh install would open to an empty tab.
        /// </summary>
        private static HashSet<string> Hidden
        {
            get
            {
                string stored = UIGuard.Try("Pawns.ReadFilters",
                    () => UIOverhaulSettingsFile.Current?.hiddenPawnCategories ?? string.Empty, string.Empty,
                    "Every pawn category is shown this session.");

                if (hidden != null && stored == builtFrom)
                    return hidden;

                builtFrom = stored;
                hidden = new HashSet<string>();

                foreach (string entry in stored.Split(Separator))
                {
                    string trimmed = entry.Trim();

                    if (!trimmed.NullOrEmpty())
                        hidden.Add(trimmed);
                }

                return hidden;
            }
        }

        internal static bool Shown(PawnCategory category)
        {
            return Available(category) && !Hidden.Contains(category.ToString());
        }

        /// <summary>Switches a category on or off and writes the file.</summary>
        internal static void Toggle(PawnCategory category)
        {
            UIGuard.Try("Pawns.WriteFilters", () =>
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                if (settings == null)
                    return;

                HashSet<string> current = Hidden;
                string key = category.ToString();

                if (!current.Remove(key))
                    current.Add(key);

                System.Text.StringBuilder text = new System.Text.StringBuilder();

                foreach (string name in current)
                {
                    if (text.Length > 0)
                        text.Append(Separator);

                    text.Append(name);
                }

                settings.hiddenPawnCategories = text.ToString();

                // Written back so the rebuild above does not fire on the next read and discard the edit.
                builtFrom = settings.hiddenPawnCategories;
                settings.Save();
            }, "That filter could not be saved and is forgotten when the game restarts.");
        }
    }
}
