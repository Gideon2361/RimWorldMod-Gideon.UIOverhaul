using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Ideoligions
{
    /// <summary>
    /// Sends vanilla's ideoligions tab to ours, wherever it is opened from.
    ///
    /// <b>Patched where the tab is chosen, not where the button is drawn,</b> for the reason recorded on the
    /// animals redirect: <c>MainTabsRoot.ToggleTab</c> is the one funnel that the button bar, the hotkey,
    /// <c>SetCurrentTab</c> and any mod calling in all arrive through. Substituting the def rather than
    /// intercepting the window keeps the game's own bookkeeping intact, so the open tab, the highlighted button
    /// and the escape key still agree afterwards.
    ///
    /// <b>Nothing about this patch names an Ideology type,</b> which is deliberate.
    /// <c>MainTabsRoot.ToggleTab</c> is in the base game, so the attribute resolves in an install without the
    /// expansion and cannot take <c>PatchAll</c> down with it. The expansion question is asked at runtime, inside
    /// the guard, where the answer is allowed to be no.
    ///
    /// <b>The comparison is on the def name rather than the window class.</b> The animals redirect tests
    /// assignability from <c>MainTabWindow_Animals</c>, which it can only do because that type is in the base
    /// game; <c>MainTabWindow_Ideos</c> is not, and a field of that type in this class would have to resolve
    /// before the guard could decline to use it. The def name is stable, and a mod that replaces vanilla's
    /// ideoligion tab with its own def is left alone -- which is the right answer, since two mods rewriting the
    /// same screen should not silently fight over it.
    /// </summary>
    [HarmonyPatch(typeof(MainTabsRoot), nameof(MainTabsRoot.ToggleTab))]
    internal static class Patch_MainTabsRoot_ToggleTab_Ideoligions
    {
        public static void Prefix(ref MainButtonDef newTab)
        {
            MainButtonDef replacement = Replacement(newTab);

            if (replacement != null)
                newTab = replacement;
        }

        /// <summary>
        /// Our tab when this one should be redirected to it, or null to leave the argument alone.
        ///
        /// Split out from the prefix because a <c>ref</c> parameter cannot be touched inside a lambda, and the
        /// guard is a lambda. Deciding here and assigning there keeps the whole decision inside the guard, which
        /// is what matters: nothing may throw out of a prefix into RimWorld's tab handling.
        /// </summary>
        private static MainButtonDef Replacement(MainButtonDef newTab)
        {
            return UIGuard.Try("Ideoligions.Redirect", () =>
            {
                if (newTab == null || newTab.defName != IdeoTabs.VanillaDefName)
                    return null;

                return IdeoTabs.Available ? IdeoTabs.Ours() : null;
            }, null, null);
        }
    }
}
