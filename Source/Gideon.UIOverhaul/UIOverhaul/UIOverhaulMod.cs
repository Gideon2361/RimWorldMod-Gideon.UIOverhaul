using Gideon.UIOverhaul.Features.GrowZones;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul
{
    /// <summary>
    /// Entry point. RimWorld constructs one Mod instance per mod folder during startup, before defs
    /// are loaded, which makes the constructor the earliest place patches can be applied.
    ///
    /// Lives on the UIOverhaul side rather than in UIFramework: applying our patches is a direct
    /// change to the game, and a framework consumed by other mods must not carry an entry point that
    /// patches on their behalf.
    ///
    /// This is deliberately the *only* Mod subclass in the assembly. RimWorld instantiates every one it
    /// finds, so a feature bringing its own -- as Growing Zones Plus did before it was ported in --
    /// would produce two settings entries for one mod and call PatchAll twice.
    /// </summary>
    public class UIOverhaulMod : Mod
    {
        /// <summary>
        /// Harmony instance id. Shows up in Harmony's own logs and in other mods' conflict reports,
        /// so it matches the packageId in About.xml rather than being invented separately.
        /// </summary>
        public const string HarmonyId = "gideon.uioverhaul";

        public UIOverhaulMod(ModContentPack content)
            : base(content)
        {
            // One call covers every patch in the assembly. PatchAll resolves the calling assembly
            // rather than a namespace, so the framework's patches, the UI element patches and the
            // growing-zone feature's patches are all picked up here.
            new Harmony(HarmonyId).PatchAll();

            GrowZonesFeature.Settings = GetSettings<GzpSettings>();

            // Nothing to configure for the loading screen here. It is described by LoadingScreen.xml
            // at this mod's root, which UILoadingScreenConfig reads off disk on first use -- early
            // enough that the screen is already correct on the first frame it draws. That is the whole
            // reason it is not a Def: defs do not exist until three quarters of the way through the
            // load, long after the screen has started drawing.
        }

        public override string SettingsCategory() => "Gideon's UI Overhaul";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Only inRect is ours -- the window frame, title and close button belong to
            // Dialog_ModSettings and would need a patch of their own to restyle.
            GrowZonesFeature.DoSettingsContents(inRect);
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            GrowZonesFeature.Settings?.Write();
        }
    }
}
