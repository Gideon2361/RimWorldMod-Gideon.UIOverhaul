using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Gravships
{
    /// <summary>
    /// The three numbers that decide how big a gravship may be, and the switch that hands them to the player.
    ///
    /// <b>Written onto the defs, because that is where the game reads them from.</b> Each of the three is read
    /// live, in a different place, by code with no seam worth patching:
    /// <c>GravshipUtility.InsideFootprint</c> asks every footprint comp for <c>Props.radius</c> to decide which
    /// cells may hold substructure; <c>Building_GravEngine.UpdateSubstructureIfNeeded</c> passes
    /// <c>GetStatValue(SubstructureSupport)</c> to the flood fill as its cell budget; and
    /// <c>CompFacility</c> counts against <c>maxSimultaneous</c> when it decides which extenders are linked.
    /// Patching all three would be three transpilers into hot code for what is a change of value.
    ///
    /// <b>Every baseline is captured once, before anything is written.</b> Reading a def to find out what vanilla
    /// said is only true the first time; after that it reads back whatever we last wrote, and a settings change
    /// would compound instead of replace. Captured at startup, which is also after every other mod's XML patches
    /// have run -- so "vanilla" here means "what this install had before we touched it", which is the honest
    /// baseline for a mod that has to give it back.
    ///
    /// <b>Off means given back, not left alone.</b> Turning the master switch off rewrites all three from those
    /// baselines rather than skipping the write, so a save carries on with the game's own numbers the moment the
    /// switch moves. There is no state to unwind and nothing is stored in the save.
    ///
    /// <b>An extender is anything that offsets <c>SubstructureSupport</c>,</b> not the one def by name. That
    /// offset is what makes a thing an extender, so a mod that adds another one is covered without knowing about
    /// it, and a thruster or a fuel tank -- which have their own <c>maxSimultaneous</c> worth leaving alone -- is
    /// not caught by accident.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class GravshipTuning
    {
        /// <summary>What the tile cap becomes when the cap is switched off. Aaron's number, 2026-08-23.</summary>
        internal const float Unlimited = 99999f;

        /// <summary>How far past vanilla the radius slider goes. Quadruple, asked for in those words.</summary>
        internal const float RadiusCeilingFactor = 4f;

        /// <summary>
        /// The smallest radius the slider offers.
        ///
        /// Not zero. A radius of zero is a grav engine that cannot sit on its own substructure, so the ship
        /// cannot be built at all -- a setting whose only effect is to break the feature it configures. One cell
        /// is already far past unusable and is a floor rather than a suggestion.
        /// </summary>
        internal const float RadiusFloor = 1f;

        internal const int ExtenderCeiling = 20;

        private sealed class Extender
        {
            /// <summary>Kept because comp properties do not know which def owns them, and the relink needs it.</summary>
            internal ThingDef Def;

            internal CompProperties_GravshipFacility Facility;

            internal StatModifier Support;

            internal int BaseMax;

            internal float BaseSupport;
        }

        private static bool captured;

        private static CompProperties_SubstructureFootprint engineFootprint;

        private static StatModifier engineSupport;

        private static float baseRadius;

        private static float baseSupport;

        private static readonly List<Extender> Extenders = new List<Extender>();

        static GravshipTuning()
        {
            UIGuard.Try("Gravships.Startup", Apply,
                "Gravship settings are not applied this session. The game's own numbers are in force.");
        }

        /// <summary>Vanilla's engine radius, for the slider's range and its readout. Zero before capture.</summary>
        internal static float VanillaRadius
        {
            get { return baseRadius; }
        }

        /// <summary>Vanilla's extender limit, so the slider can say which notch is the game's own.</summary>
        internal static int VanillaExtenders
        {
            get { return Extenders.Count > 0 ? Extenders[0].BaseMax : 0; }
        }

        /// <summary>Whether there is anything here to configure. False without Odyssey.</summary>
        internal static bool Available
        {
            get { return engineFootprint != null; }
        }

        internal static float RadiusCeiling
        {
            get { return baseRadius * RadiusCeilingFactor; }
        }

        /// <summary>
        /// Writes the current settings onto the defs, and tells anything already built to look again.
        ///
        /// Called at startup, whenever a setting on the gravship page changes, and when the config file is
        /// reloaded from disk. Cheap and idempotent: it writes the same four numbers every time rather than
        /// tracking what it changed last.
        /// </summary>
        internal static void Apply()
        {
            Capture();

            if (!Available)
                return;

            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

            bool on = settings != null && settings.gravshipOverrides;

            engineFootprint.radius = on ? Radius(settings) : baseRadius;

            bool uncapped = on && settings.gravshipUnlimitedTiles;

            if (engineSupport != null)
                engineSupport.value = uncapped ? Unlimited : baseSupport;

            for (int i = 0; i < Extenders.Count; i++)
            {
                Extender extender = Extenders[i];

                // Zero rather than removed. The entry is a live object the def hands to the stat system, and
                // taking it out of the list is a change that cannot be undone from a baseline -- the list order
                // is somebody else's business by then.
                extender.Support.value = uncapped ? 0f : extender.BaseSupport;

                extender.Facility.maxSimultaneous = on ? ExtenderLimit(settings, extender.BaseMax) : extender.BaseMax;
            }

            Refresh();
        }

        /// <summary>
        /// The radius to write, with the unset value meaning vanilla's.
        ///
        /// <b>The default cannot be written into the settings field,</b> because it is not known until the defs
        /// are loaded and it is not the same on every install -- another mod may have patched the engine. So the
        /// field starts at zero, zero means "whatever the game says", and only a number the player has actually
        /// chosen displaces it. The clamp is against this install's own ceiling for the same reason.
        /// </summary>
        internal static float Radius(UIOverhaulSettingsFile settings)
        {
            if (settings == null || settings.gravEngineRadius <= 0f)
                return baseRadius;

            return Mathf.Clamp(settings.gravEngineRadius, RadiusFloor, RadiusCeiling);
        }

        /// <summary>The extender limit to write. Negative means the game's own, for the same reason as above.</summary>
        internal static int ExtenderLimit(UIOverhaulSettingsFile settings, int vanilla)
        {
            if (settings == null || settings.gravExtenderMax < 0)
                return vanilla;

            return Mathf.Clamp(settings.gravExtenderMax, 0, ExtenderCeiling);
        }

        /// <summary>
        /// Reads the baselines, once, before this class has written anything.
        ///
        /// <b>The one-shot flag is the whole point.</b> A second capture would read our own numbers back as
        /// vanilla's, and from then on there would be no way home: turning the switch off would restore whatever
        /// the player last set. The flag is set before the reads rather than after, so a throw halfway through
        /// cannot leave the door open for a partial recapture on the next call.
        /// </summary>
        private static void Capture()
        {
            if (captured)
                return;

            captured = true;

            if (!ModsConfig.OdysseyActive)
                return;

            ThingDef engine = ThingDefOf.GravEngine;
            StatDef support = StatDefOf.SubstructureSupport;

            if (engine == null || support == null)
                return;

            engineFootprint = engine.GetCompProperties<CompProperties_SubstructureFootprint>();

            if (engineFootprint == null)
                return;

            baseRadius = engineFootprint.radius;

            engineSupport = Modifier(engine.statBases, support);
            baseSupport = engineSupport != null ? engineSupport.value : 0f;

            List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;

            for (int i = 0; i < all.Count; i++)
            {
                CompProperties_GravshipFacility facility =
                    all[i].GetCompProperties<CompProperties_GravshipFacility>();

                if (facility == null)
                    continue;

                StatModifier offset = Modifier(facility.statOffsets, support);

                if (offset == null)
                    continue;

                Extenders.Add(new Extender
                {
                    Def = all[i],
                    Facility = facility,
                    Support = offset,
                    BaseMax = facility.maxSimultaneous,
                    BaseSupport = offset.value
                });
            }
        }

        private static StatModifier Modifier(List<StatModifier> modifiers, StatDef stat)
        {
            for (int i = 0; modifiers != null && i < modifiers.Count; i++)
            {
                if (modifiers[i] != null && modifiers[i].stat == stat)
                    return modifiers[i];
            }

            return null;
        }

        /// <summary>
        /// Makes what is already built agree with the numbers that were just written.
        ///
        /// <b>Two separate staleness problems, and only one of them is obvious.</b> An engine caches its
        /// substructure and recomputes it when something dirties it, so a radius or a cap changed mid-game shows
        /// nothing until a wall is built; <c>ForceSubstructureDirty</c> is the game's own way of saying look
        /// again. The second is that extenders past the old limit are not merely inactive, they are <i>unlinked</i>
        /// -- raising the limit does not link them, because linking is decided when something changes near them.
        /// <c>Notify_ThingChanged</c> is <c>CompFacility</c>'s public route to <c>RelinkAll</c>, which is exactly
        /// that reconsideration.
        ///
        /// Extenders are relinked before engines are dirtied, so the engine recomputes against the links it will
        /// actually have rather than the ones it had a moment ago.
        /// </summary>
        private static void Refresh()
        {
            if (Current.ProgramState != ProgramState.Playing)
                return;

            List<Map> maps = Find.Maps;

            for (int m = 0; maps != null && m < maps.Count; m++)
            {
                Map map = maps[m];

                if (map == null || map.listerThings == null)
                    continue;

                Relink(map);
                Dirty(map);
            }
        }

        private static void Relink(Map map)
        {
            for (int i = 0; i < Extenders.Count; i++)
            {
                List<Thing> things = map.listerThings.ThingsOfDef(Extenders[i].Def);

                for (int t = 0; things != null && t < things.Count; t++)
                {
                    CompFacility comp = things[t].TryGetComp<CompFacility>();

                    if (comp != null)
                        comp.Notify_ThingChanged();
                }
            }
        }

        private static void Dirty(Map map)
        {
            List<Thing> engines = map.listerThings.ThingsOfDef(ThingDefOf.GravEngine);

            for (int i = 0; engines != null && i < engines.Count; i++)
            {
                Building_GravEngine engine = engines[i] as Building_GravEngine;

                if (engine != null)
                    engine.ForceSubstructureDirty();
            }
        }
    }
}
