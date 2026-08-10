using HarmonyLib;
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
    /// No SettingsCategory override yet: returning a non-empty string from it puts a Settings button
    /// on the mod list, and without a DoSettingsWindowContents to go with it that button opens an
    /// empty page. Add both together.
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
            new Harmony(HarmonyId).PatchAll();

            // Nothing to configure for the loading screen here. It is described by LoadingScreen.xml
            // at this mod's root, which UILoadingScreenConfig reads off disk on first use -- early
            // enough that the screen is already correct on the first frame it draws. That is the whole
            // reason it is not a Def: defs do not exist until three quarters of the way through the
            // load, long after the screen has started drawing.
        }
    }
}
