using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Threats
{
    /// <summary>What kind of thing a toggle switches off, which decides where it is intercepted.</summary>
    internal enum ThreatKind
    {
        /// <summary>A <c>RaidStrategyDef</c>: how a raid behaves once it is on the map.</summary>
        Strategy,

        /// <summary>A <c>PawnsArrivalModeDef</c>: how it got there.</summary>
        Arrival,

        /// <summary>An <c>IncidentDef</c>: an event that is not a raid at all.</summary>
        Incident
    }

    /// <summary>One switch, and the defs it covers.</summary>
    internal sealed class ThreatToggle
    {
        /// <summary>
        /// The stored name of this switch.
        ///
        /// <b>These are No Way Jose's own keys, kept letter for letter including the inconsistent capitals.</b>
        /// Nothing in our code needs them to look like anything, and keeping them means a player's existing Raid
        /// and Event Manager settings can be read across without a translation table if that is ever wanted.
        /// </summary>
        internal string Key;

        internal string Label;

        internal string Tooltip;

        internal ThreatKind Kind;

        internal string Group;

        /// <summary>The defs this covers by name. Any that the running game does not have are skipped.</summary>
        internal string[] DefNames;

        /// <summary>
        /// A worker type whose defs this also covers, for the one switch that is about a family rather than a
        /// list.
        ///
        /// Breaching is several defs in vanilla and any number in a mod, and they have nothing in common in XML
        /// except the abstract parent they inherit -- which does not exist at runtime. What they do have is the
        /// worker class that makes them breach.
        /// </summary>
        internal System.Type WorkerClass;
    }

    /// <summary>
    /// Which raids and incidents the player has switched off, and what that covers.
    ///
    /// <b>This is Raid and Event Manager, reimplemented.</b> Aaron took that mod over from No Way Jose on
    /// 2026-08-23 and asked for our own patches with our own settings, depending on nothing. It was a pure XML
    /// mod: twenty <c>XmlExtensions.OptionalPatch</c> operations that zero a def's selection weight curves at load
    /// time, which is why it needed XML Extensions and why its own settings page says "IMPORTANT! Restart or
    /// reload game for any changes to take effect."
    ///
    /// <b>Doing it in code buys three things.</b> No dependency. No restart -- a switch takes effect on the next
    /// raid the storyteller rolls. And nothing is written into the def database, so a def another mod reads is
    /// still the def its author shipped.
    ///
    /// <b>Everything is off by default, and off means the game is untouched.</b> With no switch set,
    /// <see cref="Any"/> is false and every patch returns on its first line. A player who never opens this
    /// section is running unpatched RimWorld.
    ///
    /// <b>A switch never removes the last option.</b> Vanilla has a hard fallback at both places this
    /// intercepts: <c>ResolveRaidStrategy</c> logs "No raid strategy found, defaulting to ImmediateAttack" and
    /// <c>ResolveRaidArriveMode</c> logs "Could not resolve arrival mode for raid. Defaulting to EdgeWalkIn."
    /// Refusing everything therefore does not stop the raid -- it produces a red error and then the raid anyway.
    /// So the last surviving strategy and the last surviving arrival mode are allowed through. The original mod
    /// has this fault: zeroing every weight makes <c>TryRandomElementByWeight</c> fail and vanilla shouts.
    /// </summary>
    internal static class ThreatToggles
    {
        /// <summary>Group captions, in the order they are drawn.</summary>
        internal const string RaidsGroup = "Raid strategies";

        internal const string ArrivalGroup = "How raiders arrive";

        internal const string IncidentGroup = "Incidents";

        /// <summary>
        /// Every switch this mod offers.
        ///
        /// The same twenty the original had, which is deliberate: this replaces that mod for people who were
        /// using it, and a switch it had that we do not is a feature somebody loses by taking our version.
        /// </summary>
        internal static readonly ThreatToggle[] All =
        {
            new ThreatToggle
            {
                Key = "immediateAttack", Kind = ThreatKind.Strategy, Group = RaidsGroup,
                Label = "Walk-in raids that attack on arrival",
                DefNames = new[] { "ImmediateAttack" },
                Tooltip = "Raiders who head straight for the colony the moment they arrive. This is the "
                          + "commonest raid in the game."
            },
            new ThreatToggle
            {
                Key = "immediateSmart", Kind = ThreatKind.Strategy, Group = RaidsGroup,
                Label = "Walk-in raids that pick their targets",
                DefNames = new[] { "ImmediateAttackSmart" },
                Tooltip = "The same, except they go for turrets, mortars and whatever else is worth breaking "
                          + "first rather than the nearest colonist."
            },
            new ThreatToggle
            {
                Key = "stagethenattack", Kind = ThreatKind.Strategy, Group = RaidsGroup,
                Label = "Raids that gather before attacking",
                DefNames = new[] { "StageThenAttack" },
                Tooltip = "Raiders who assemble at the map edge for a while first, which is the raid that gives "
                          + "you time to prepare."
            },
            new ThreatToggle
            {
                Key = "breach", Kind = ThreatKind.Strategy, Group = RaidsGroup,
                Label = "Breach raids",
                WorkerClass = typeof(RaidStrategyWorker_ImmediateAttackBreaching),
                Tooltip = "Raiders who cut through your walls rather than using a door. Matched by what makes "
                          + "them breach rather than by name, so a mod's breaching raid is covered too."
            },
            new ThreatToggle
            {
                Key = "sapper", Kind = ThreatKind.Strategy, Group = RaidsGroup,
                Label = "Sapper raids",
                DefNames = new[] { "ImmediateAttackSappers" },
                Tooltip = "Raiders who mine their own way in, ignoring your killbox entirely."
            },
            new ThreatToggle
            {
                Key = "siege", Kind = ThreatKind.Strategy, Group = RaidsGroup,
                Label = "Siege raids",
                DefNames = new[] { "Siege" },
                Tooltip = "Raiders who build mortars at a distance and shell you from there."
            },
            new ThreatToggle
            {
                Key = "mechwater", Kind = ThreatKind.Strategy, Group = RaidsGroup,
                Label = "Mechanoids emerging from water",
                DefNames = new[] { "EmergeFromWater" },
                Tooltip = "Mechanoids that surface inside your colony from any water on the map."
            },
            new ThreatToggle
            {
                Key = "psychicsiege", Kind = ThreatKind.Strategy, Group = RaidsGroup,
                Label = "Psychic ritual sieges",
                DefNames = new[] { "PsychicRitualSiege" },
                Tooltip = "Anomaly's cultists, who camp at a distance and run a ritual at you instead of "
                          + "attacking."
            },
            new ThreatToggle
            {
                Key = "ShamblerAssault", Kind = ThreatKind.Strategy, Group = RaidsGroup,
                Label = "Shambler assaults",
                DefNames = new[] { "ShamblerAssault" },
                Tooltip = "Anomaly's shambler hordes."
            },

            new ThreatToggle
            {
                Key = "droppodedge", Kind = ThreatKind.Arrival, Group = ArrivalGroup,
                Label = "Drop pods at the map edge",
                DefNames = new[] { "EdgeDrop", "EdgeDropGroups" },
                Tooltip = "Raiders who arrive by pod at the edge of the map rather than walking in. Turning "
                          + "this off does not stop the raid; it arrives some other way."
            },
            new ThreatToggle
            {
                Key = "droppodcenter", Kind = ThreatKind.Arrival, Group = ArrivalGroup,
                Label = "Drop pods on top of the colony",
                DefNames = new[] { "CenterDrop" },
                Tooltip = "Pods that land in the middle of your base. The single most disliked arrival in the "
                          + "game, and the reason this mod exists."
            },
            new ThreatToggle
            {
                Key = "droppodhaywire", Kind = ThreatKind.Arrival, Group = ArrivalGroup,
                Label = "Drop pods scattered at random",
                DefNames = new[] { "RandomDrop" },
                Tooltip = "Pods strewn anywhere on the map, so the raid arrives in pieces in unpredictable "
                          + "places."
            },

            new ThreatToggle
            {
                Key = "infestation", Kind = ThreatKind.Incident, Group = IncidentGroup,
                Label = "Infestations",
                DefNames = new[] { "Infestation" },
                Tooltip = "Insect hives erupting inside your mountain base."
            },
            new ThreatToggle
            {
                Key = "deepdrillinfestation", Kind = ThreatKind.Incident, Group = IncidentGroup,
                Label = "Deep drill infestations",
                DefNames = new[] { "DeepDrillInfestation" },
                Tooltip = "The insects a deep drill wakes up. Blocked wherever it is fired from, not only when "
                          + "the storyteller picks it."
            },
            new ThreatToggle
            {
                Key = "WastepackInfestation", Kind = ThreatKind.Incident, Group = IncidentGroup,
                Label = "Wastepack infestations",
                DefNames = new[] { "WastepackInfestation" },
                Tooltip = "Biotech's insects, drawn by a stockpile of toxic wastepacks."
            },
            new ThreatToggle
            {
                Key = "MechCluster", Kind = ThreatKind.Incident, Group = IncidentGroup,
                Label = "Mech clusters",
                DefNames = new[] { "MechCluster" },
                Tooltip = "A mechanoid structure dropped onto your map to be dismantled on your own time."
            },
            new ThreatToggle
            {
                Key = "mechshippart", Kind = ThreatKind.Incident, Group = IncidentGroup,
                Label = "Crashed ship parts",
                DefNames = new[] { "DefoliatorShipPartCrash", "PsychicEmanatorShipPartCrash" },
                Tooltip = "The psychic emanator and the defoliator: a mechanoid part that lands and then has to "
                          + "be dealt with before it ruins the map."
            },
            new ThreatToggle
            {
                Key = "ShortCircuit", Kind = ThreatKind.Incident, Group = IncidentGroup,
                Label = "Short circuits",
                DefNames = new[] { "ShortCircuit" },
                Tooltip = "A battery discharging into a fire, which is the event most likely to burn down a "
                          + "colony nobody was watching."
            },
            new ThreatToggle
            {
                Key = "blight", Kind = ThreatKind.Incident, Group = IncidentGroup,
                Label = "Crop blight",
                DefNames = new[] { "CropBlight" },
                Tooltip = "Blight on your fields."
            },
            new ThreatToggle
            {
                Key = "Manhunters", Kind = ThreatKind.Incident, Group = IncidentGroup,
                Label = "Manhunter animals",
                DefNames = new[] { "ManhunterPack", "AnimalInsanitySingle" },
                Tooltip = "Both the pack that wanders in and the single animal that goes mad on its own."
            }
        };

        private static readonly HashSet<RaidStrategyDef> strategies = new HashSet<RaidStrategyDef>();

        private static readonly HashSet<PawnsArrivalModeDef> arrivals = new HashSet<PawnsArrivalModeDef>();

        private static readonly HashSet<IncidentDef> incidents = new HashSet<IncidentDef>();

        /// <summary>The setting string the three sets above were built from.</summary>
        private static string builtFrom;

        private static bool anything;

        /// <summary>
        /// Re-entrancy flag for the alternative checks.
        ///
        /// <b>Not optional.</b> Deciding whether refusing a strategy would leave nothing means asking the other
        /// strategies whether they can be used, which goes through the same <c>CanUseWith</c> this feature is a
        /// postfix on. Without the flag that recurses until the stack runs out; with it, the inner questions get
        /// vanilla's own answers, which is exactly what "would anything else work" means.
        /// </summary>
        private static bool asking;

        /// <summary>
        /// Whether anything at all is switched off.
        ///
        /// Every patch tests this first. With nothing set the whole feature costs one string comparison per call
        /// and touches nothing.
        /// </summary>
        internal static bool Any
        {
            get
            {
                Rebuild();

                return anything;
            }
        }

        /// <summary>Whether this switch is set.</summary>
        internal static bool IsOff(ThreatToggle toggle)
        {
            if (toggle == null)
                return false;

            return Keys().Contains(toggle.Key);
        }

        /// <summary>
        /// Sets or clears one switch and saves.
        ///
        /// <b>Stored as a list of the switches that are set, not as twenty fields.</b> One setting to read, one to
        /// write, and adding a switch later touches this file and nothing else -- the settings reader is a flat
        /// switch over element names and twenty entries in it would be twenty chances to mistype one.
        /// </summary>
        internal static void Set(ThreatToggle toggle, bool off)
        {
            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

            if (toggle == null || settings == null)
                return;

            HashSet<string> keys = new HashSet<string>(Keys());

            if (off)
                keys.Add(toggle.Key);
            else
                keys.Remove(toggle.Key);

            List<string> ordered = new List<string>();

            // Written in catalogue order rather than hash order, so the config file is stable between saves and
            // a diff of it is readable.
            for (int i = 0; i < All.Length; i++)
            {
                if (keys.Contains(All[i].Key))
                    ordered.Add(All[i].Key);
            }

            settings.disabledThreats = string.Join(",", ordered.ToArray());
            settings.Save();

            builtFrom = null;
        }

        /// <summary>Whether this switch has anything to act on in the running game.</summary>
        internal static bool Available(ThreatToggle toggle)
        {
            return UIGuard.Try("Threats.Available", () => Resolve(toggle).Count > 0, false, null);
        }

        /// <summary>Whether an incident is switched off.</summary>
        internal static bool Disabled(IncidentDef def)
        {
            if (def == null)
                return false;

            return UIGuard.Try("Threats.Incident", () =>
            {
                Rebuild();

                return incidents.Contains(def);
            }, false, null);
        }

        /// <summary>
        /// Whether to refuse this raid strategy, which is not the same question as whether it is switched off.
        ///
        /// The last one standing is allowed through: see the note on the class about vanilla's two hard
        /// fallbacks.
        /// </summary>
        internal static bool Refuse(RaidStrategyDef def, IncidentParms parms, PawnGroupKindDef groupKind)
        {
            if (def == null || asking)
                return false;

            return UIGuard.Try("Threats.Strategy", () =>
            {
                Rebuild();

                if (!strategies.Contains(def))
                    return false;

                asking = true;

                try
                {
                    List<RaidStrategyDef> all = DefDatabase<RaidStrategyDef>.AllDefsListForReading;

                    for (int i = 0; i < all.Count; i++)
                    {
                        if (all[i] == def || strategies.Contains(all[i]))
                            continue;

                        if (all[i].Worker != null && all[i].Worker.CanUseWith(parms, groupKind))
                            return true;
                    }

                    return false;
                }
                finally
                {
                    asking = false;
                }
            }, false, null);
        }

        /// <summary>
        /// Whether to refuse this arrival mode.
        ///
        /// Checked against the strategy's own list when there is one, because that is the set vanilla is about to
        /// pick from: refusing the last mode a siege can use produces the red error and an edge walk-in, which is
        /// neither what the player asked for nor a siege.
        /// </summary>
        internal static bool Refuse(PawnsArrivalModeDef def, IncidentParms parms)
        {
            if (def == null || asking)
                return false;

            return UIGuard.Try("Threats.Arrival", () =>
            {
                Rebuild();

                if (!arrivals.Contains(def))
                    return false;

                asking = true;

                try
                {
                    List<PawnsArrivalModeDef> pool = parms?.raidStrategy?.arriveModes;

                    if (pool == null || pool.Count == 0)
                        pool = DefDatabase<PawnsArrivalModeDef>.AllDefsListForReading;

                    for (int i = 0; i < pool.Count; i++)
                    {
                        if (pool[i] == def || arrivals.Contains(pool[i]))
                            continue;

                        if (pool[i].Worker != null && pool[i].Worker.CanUseWith(parms))
                            return true;
                    }

                    return false;
                }
                finally
                {
                    asking = false;
                }
            }, false, null);
        }

        private static HashSet<string> keyCache = new HashSet<string>();

        private static string keyCacheFrom;

        private static HashSet<string> Keys()
        {
            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;
            string stored = settings == null || settings.disabledThreats == null
                ? string.Empty
                : settings.disabledThreats;

            if (keyCacheFrom == stored)
                return keyCache;

            keyCacheFrom = stored;
            keyCache = new HashSet<string>();

            string[] parts = stored.Split(',');

            for (int i = 0; i < parts.Length; i++)
            {
                string key = parts[i].Trim();

                if (key.Length > 0)
                    keyCache.Add(key);
            }

            return keyCache;
        }

        /// <summary>
        /// Turns the stored key list into three sets of defs.
        ///
        /// Rebuilt when the string changes and not otherwise, so the steady state is a string comparison. The
        /// defs are resolved here rather than at load because a def database exists by the time anything asks,
        /// and because this is also what makes a switch take effect without a restart.
        /// </summary>
        private static void Rebuild()
        {
            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;
            string stored = settings == null || settings.disabledThreats == null
                ? string.Empty
                : settings.disabledThreats;

            if (builtFrom == stored)
                return;

            builtFrom = stored;

            strategies.Clear();
            arrivals.Clear();
            incidents.Clear();

            HashSet<string> keys = Keys();

            for (int i = 0; i < All.Length; i++)
            {
                ThreatToggle toggle = All[i];

                if (!keys.Contains(toggle.Key))
                    continue;

                List<Def> targets = Resolve(toggle);

                for (int t = 0; t < targets.Count; t++)
                {
                    RaidStrategyDef strategy = targets[t] as RaidStrategyDef;

                    if (strategy != null)
                    {
                        strategies.Add(strategy);

                        continue;
                    }

                    PawnsArrivalModeDef arrival = targets[t] as PawnsArrivalModeDef;

                    if (arrival != null)
                    {
                        arrivals.Add(arrival);

                        continue;
                    }

                    IncidentDef incident = targets[t] as IncidentDef;

                    if (incident != null)
                        incidents.Add(incident);
                }
            }

            anything = strategies.Count > 0 || arrivals.Count > 0 || incidents.Count > 0;
        }

        private static readonly Dictionary<string, List<Def>> resolved = new Dictionary<string, List<Def>>();

        /// <summary>
        /// The defs one switch covers in this install, resolved once.
        ///
        /// A name the running game does not have is simply skipped, which is what makes the expansion-only
        /// switches look after themselves: without Anomaly there is no ShamblerAssault def, so that switch has
        /// nothing to cover and <see cref="Available"/> reports false, and the options section leaves it out.
        /// </summary>
        private static List<Def> Resolve(ThreatToggle toggle)
        {
            List<Def> known;

            if (resolved.TryGetValue(toggle.Key, out known))
                return known;

            known = new List<Def>();

            if (toggle.DefNames != null)
            {
                for (int i = 0; i < toggle.DefNames.Length; i++)
                {
                    Def def = Named(toggle.Kind, toggle.DefNames[i]);

                    if (def != null)
                        known.Add(def);
                }
            }

            if (toggle.WorkerClass != null)
            {
                List<RaidStrategyDef> all = DefDatabase<RaidStrategyDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].workerClass != null && toggle.WorkerClass.IsAssignableFrom(all[i].workerClass)
                                                   && !known.Contains(all[i]))
                        known.Add(all[i]);
                }
            }

            resolved[toggle.Key] = known;

            return known;
        }

        private static Def Named(ThreatKind kind, string defName)
        {
            switch (kind)
            {
                case ThreatKind.Strategy:
                    return DefDatabase<RaidStrategyDef>.GetNamedSilentFail(defName);

                case ThreatKind.Arrival:
                    return DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail(defName);

                default:
                    return DefDatabase<IncidentDef>.GetNamedSilentFail(defName);
            }
        }
    }
}
