using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// Whether the character editor exists at all.
    ///
    /// <b>Absent rather than greyed, which is the whole of the setting.</b> Every way into the editor asks this
    /// before it draws anything: the icon in the inspect pane's corner, the main tab's button on the bar, and the
    /// Bring back action on the corpses tab. With the switch off none of them is drawn -- not disabled, absent. A
    /// greyed control is an advertisement for a feature, and this is a feature a player should have to go and ask
    /// for.
    ///
    /// <b>Asked every frame rather than cached.</b> The settings window is open at the same time as the game, and
    /// somebody who turns this on expects the icon to appear without reloading. Reading a bool off an already
    /// loaded settings object costs nothing.
    /// </summary>
    internal static class EditorGate
    {
        internal static bool Enabled
        {
            get
            {
                return UIGuard.Try("Editor.Enabled", () =>
                {
                    UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                    return settings != null && settings.characterEditor;
                }, false, null);
            }
        }
    }
}
