using System;
using Gideon.UIFramework.Helpers;
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
            ApplyPatches();

            // Guarded like everything else reachable from here. A throw in the settings loader would escape this
            // constructor, and RimWorld's response to that is to report the mod as failing to instantiate and
            // apply none of it -- the same total loss the patch loop above was split up to prevent. Leaving the
            // settings null is survivable: the only two places that read them tolerate it.
            UIGuard.Try("Mod.LoadSettings", () => GrowZonesFeature.Settings = GetSettings<GzpSettings>(),
                "The growing zone settings could not be read, so that feature uses its defaults.");

            // Nothing to configure for the loading screen here. It is described by LoadingScreen.xml
            // at this mod's root, which UILoadingScreenConfig reads off disk on first use -- early
            // enough that the screen is already correct on the first frame it draws. That is the whole
            // reason it is not a Def: defs do not exist until three quarters of the way through the
            // load, long after the screen has started drawing.
        }

        /// <summary>
        /// Applies every patch in the assembly, one class at a time, so a failure costs its own feature.
        ///
        /// <b>This replaces a single <c>PatchAll()</c>, and the reason is a real incident rather than a
        /// precaution.</b> A <c>HarmonyPatch</c> attribute that named an overloaded method without its argument
        /// types threw <c>AmbiguousMatchException</c> out of <c>PatchAll</c>. That exception escapes the mod's
        /// constructor, RimWorld reports the mod as failing to instantiate, and <i>none</i> of the patches are
        /// applied -- so one wrong attribute on a slider turned the entire mod off. Nothing in the framework's
        /// guarding could see it, because it happened before any of our code ran.
        ///
        /// <c>PatchAll</c> is a loop over the assembly's types calling <c>CreateClassProcessor(type).Patch()</c>,
        /// so doing that loop here and guarding each turn of it changes nothing about what gets patched and
        /// everything about what one failure costs. A class that cannot be applied is reported by name, and the
        /// rest of the mod loads.
        ///
        /// <b>Only annotated types are offered, and skipping that filter was a mistake worth recording.</b> The
        /// first version of this handed every type in the assembly to <c>PatchClassProcessor</c>, on the
        /// assumption that one with no Harmony attributes would be ignored. It is not: the processor looks for
        /// methods <i>named</i> <c>Prefix</c>, <c>Postfix</c> and so on, and throws "undefined target method" when
        /// it finds one with nothing to patch. <c>UIDebug.Prefix(string)</c> builds this mod's log prefix and has
        /// never had anything to do with Harmony, and it produced a reported failure on every launch.
        /// <c>PatchAll</c> avoids this by testing each type for a Harmony attribute first, which is the test
        /// reproduced below.
        ///
        /// <b>Not <c>UIGuard.TryOnce</c>.</b> This runs once per launch already, and each class is a separate
        /// site, so there is nothing to retire.
        /// </summary>
        private static void ApplyPatches()
        {
            Harmony harmony = new Harmony(HarmonyId);
            int failed = 0;

            // The assembly this type lives in, which is what PatchAll resolves too: the framework's patches, the
            // UI element patches and the growing-zone feature's patches are all in here.
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(UIOverhaulMod).Assembly))
            {
                Type patchClass = type;

                // HarmonyAttribute is the base of HarmonyPatch, HarmonyPatchAll and the rest, so this catches
                // every form of annotation including the bare [HarmonyPatch] used with TargetMethods.
                if (!UIGuard.Try("Framework.PatchAttributes." + patchClass.Name,
                        () => patchClass.GetCustomAttributes(typeof(HarmonyAttribute), true).Length > 0,
                        false, "That patch class is skipped."))
                    continue;

                if (!UIGuard.Try("Framework.ApplyPatch." + patchClass.Name,
                        () => harmony.CreateClassProcessor(patchClass).Patch(),
                        "That patch is not applied. The feature it belongs to is missing or drawn RimWorld's own "
                        + "way; everything else in this mod still works."))
                    failed++;
            }

            if (failed > 0)
                Log.Warning(UILogTag.Prefix + failed + " patch class(es) could not be applied. Each was reported "
                            + "above with its own name. The rest of the mod loaded normally.");
        }

        public override string SettingsCategory() => "Gideon's UI Overhaul";

        /// <summary>
        /// Only inRect is ours: the window frame, title and close button belong to <c>Dialog_ModSettings</c> and
        /// would need a patch of their own to restyle.
        ///
        /// Guarded because RimWorld calls this every frame the settings window is open, and a throw here would
        /// otherwise reach <c>Dialog_ModSettings</c> mid-draw.
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            UIGuard.Try("Mod.SettingsWindow", () => GrowZonesFeature.DoSettingsContents(inRect),
                "This mod's page in RimWorld's own settings window is blank. Its own options window is "
                + "unaffected.");
        }

        /// <summary>
        /// <b>Only our own write is guarded; <c>base.WriteSettings</c> deliberately is not.</b> That call goes
        /// through Scribe, and Scribe tracks node depth across the whole document -- swallowing a failure part
        /// way through and carrying on would keep writing at the wrong depth and produce a file that looks
        /// complete and is not. Letting that reach RimWorld's own handler is the safe behavior there, which is
        /// the same exception <c>UIGuard</c> documents for <c>ExposeData</c>.
        /// </summary>
        public override void WriteSettings()
        {
            base.WriteSettings();

            UIGuard.Try("Mod.WriteGrowZoneSettings", () => GrowZonesFeature.Settings?.Write(),
                "The growing zone settings were not saved. They are unchanged on disk.");
        }
    }
}
