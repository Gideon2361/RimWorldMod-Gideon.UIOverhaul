using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Mods
{
    /// <summary>
    /// What is wrong with one mod, at most one thing, ordered so the worst wins.
    ///
    /// <b>Three states rather than one red.</b> Vanilla renders a missing dependency, a wrong game version and
    /// an ordering conflict as red-ish text in the same column, which is why none of them reads as more urgent
    /// than the others. They are not the same kind of problem: one cannot be fixed here at all, one is fixed by
    /// a single button, and one is the game telling you what its author last tested against.
    /// </summary>
    internal enum ModTrouble
    {
        /// <summary>Nothing to say.</summary>
        None,

        /// <summary>Built for a different version of RimWorld. A caution, not a fault.</summary>
        WrongVersion,

        /// <summary>Loaded in the wrong place relative to something it declared a rule about.</summary>
        OrderIssue,

        /// <summary>Active alongside a mod it declares itself incompatible with.</summary>
        Incompatible,

        /// <summary>Needs something that is not active, which cannot be fixed on this screen alone.</summary>
        MissingDependency
    }

    /// <summary>Where a mod came from, as one word for the list's source column.</summary>
    internal enum ModOrigin
    {
        /// <summary>Core itself.</summary>
        Game,

        /// <summary>An official expansion.</summary>
        Expansion,

        /// <summary>Subscribed through the Steam workshop.</summary>
        Workshop,

        /// <summary>A folder in Mods, put there by hand.</summary>
        Local
    }

    /// <summary>One row of the list: a mod, and everything the screen says about it.</summary>
    internal sealed class ModRow
    {
        internal ModMetaData Mod;

        internal string Name;

        internal string PackageId;

        internal ModOrigin Origin;

        internal bool Active;

        /// <summary>One-based position in the load order, or -1 when the mod is not active.</summary>
        internal int Order = -1;

        internal ModTrouble Trouble;

        /// <summary>Core, which cannot be turned off.</summary>
        internal bool Locked;

        /// <summary>The version this mod says it was built for, for the state pill.</summary>
        internal string BuiltFor;
    }

    /// <summary>
    /// The list behind the mods page: every installed mod, active ones first in load order and the rest under
    /// them, with each one's trouble worked out once.
    ///
    /// <b>Rebuilt on demand, never per frame.</b> Working out trouble asks the game about requirements and
    /// ordering for every installed mod, which is cheap once and ruinous sixty times a second. Every mutation
    /// on this screen goes through a method that rebuilds afterwards, so nothing can change without the counts
    /// changing with it.
    /// </summary>
    internal static class ModsRoster
    {
        internal static readonly List<ModRow> Rows = new List<ModRow>();

        internal static int ActiveCount;

        internal static int InstalledCount;

        internal static int MissingCount;

        internal static int OrderCount;

        internal static int IncompatibleCount;

        internal static int WrongVersionCount;

        internal static int WorkshopCount;

        internal static int LocalCount;

        internal static int OfficialCount;

        /// <summary>Blocking problems: the ones that stop the list being sound, version drift aside.</summary>
        internal static int ProblemCount => MissingCount + OrderCount + IncompatibleCount;

        internal static void Rebuild()
        {
            UIGuard.Try("Mods.Rebuild", RebuildInt, "The mod list could not be read.");
        }

        private static void RebuildInt()
        {
            Rows.Clear();

            ActiveCount = 0;
            InstalledCount = 0;
            MissingCount = 0;
            OrderCount = 0;
            IncompatibleCount = 0;
            WrongVersionCount = 0;
            WorkshopCount = 0;
            LocalCount = 0;
            OfficialCount = 0;

            // The active ones first, in the order the game will actually load them, because that order is the
            // entire reason this screen exists. ActiveModsInLoadOrder is the game's own answer rather than ours.
            Dictionary<string, int> order = new Dictionary<string, int>();

            int position = 0;

            foreach (ModMetaData active in ModsConfig.ActiveModsInLoadOrder)
            {
                if (active == null)
                    continue;

                position++;

                if (!order.ContainsKey(active.PackageId))
                    order.Add(active.PackageId, position);
            }

            List<ModRow> inactive = new List<ModRow>();

            foreach (ModMetaData mod in ModLister.AllInstalledMods)
            {
                if (mod == null)
                    continue;

                InstalledCount++;

                ModRow row = Build(mod, order);

                if (row.Active)
                {
                    ActiveCount++;
                    Rows.Add(row);
                }
                else
                {
                    inactive.Add(row);
                }

                switch (row.Origin)
                {
                    case ModOrigin.Game:
                    case ModOrigin.Expansion:
                        OfficialCount++;
                        break;
                    case ModOrigin.Workshop:
                        WorkshopCount++;
                        break;
                    default:
                        LocalCount++;
                        break;
                }

                switch (row.Trouble)
                {
                    case ModTrouble.MissingDependency: MissingCount++; break;
                    case ModTrouble.Incompatible: IncompatibleCount++; break;
                    case ModTrouble.OrderIssue: OrderCount++; break;
                    case ModTrouble.WrongVersion: WrongVersionCount++; break;
                }
            }

            Rows.Sort((a, b) => a.Order.CompareTo(b.Order));

            // Inactive mods have no load order to sort on, so they take the only order that means anything for
            // them, which is the one you would look them up in.
            inactive.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));

            Rows.AddRange(inactive);
        }

        private static ModRow Build(ModMetaData mod, Dictionary<string, int> order)
        {
            ModRow row = new ModRow
            {
                Mod = mod,
                Name = mod.Name,
                PackageId = mod.PackageId,
                Active = mod.Active,
                Locked = mod.IsCoreMod,
                Origin = OriginOf(mod),
                BuiltFor = BuiltForOf(mod)
            };

            int position;

            if (row.Active && order.TryGetValue(mod.PackageId, out position))
                row.Order = position;

            row.Trouble = TroubleOf(mod, row.Active);

            return row;
        }

        private static ModOrigin OriginOf(ModMetaData mod)
        {
            if (mod.IsCoreMod)
                return ModOrigin.Game;

            if (mod.Official)
                return ModOrigin.Expansion;

            return mod.OnSteamWorkshop ? ModOrigin.Workshop : ModOrigin.Local;
        }

        /// <summary>
        /// The newest version the mod claims to support, which is what the state pill shows.
        ///
        /// The list is what the author typed into About.xml, so it can be empty or malformed; the game has
        /// already parsed and discarded the bad ones by the time this reads it.
        /// </summary>
        private static string BuiltForOf(ModMetaData mod)
        {
            List<System.Version> versions = mod.SupportedVersionsReadOnly;

            if (versions == null || versions.Count == 0)
                return null;

            System.Version best = versions[0];

            for (int i = 1; i < versions.Count; i++)
            {
                if (versions[i] > best)
                    best = versions[i];
            }

            return best.Major + "." + best.Minor;
        }

        /// <summary>
        /// The worst thing true of this mod, or None.
        ///
        /// <b>Only active mods can be in trouble.</b> An inactive mod's unmet dependency is not a problem with
        /// the list, it is a description of what turning it on would cost, and reporting it as a fault would
        /// bury the handful that are actually wrong under two hundred that are not.
        /// </summary>
        private static ModTrouble TroubleOf(ModMetaData mod, bool active)
        {
            if (!active)
                return ModTrouble.None;

            bool missing = false;
            bool clash = false;

            foreach (ModRequirement requirement in mod.GetRequirements())
            {
                if (requirement == null || requirement.IsSatisfied)
                    continue;

                if (requirement is ModIncompatibility)
                    clash = true;
                else
                    missing = true;
            }

            if (missing)
                return ModTrouble.MissingDependency;

            if (clash)
                return ModTrouble.Incompatible;

            if (ModsConfig.ModHasAnyOrderingIssues(mod))
                return ModTrouble.OrderIssue;

            // Last, and deliberately the weakest claim on the row: a mod built for the previous version usually
            // works, and saying so as loudly as a missing dependency trains the player to ignore both.
            return mod.VersionCompatible ? ModTrouble.None : ModTrouble.WrongVersion;
        }
    }
}
