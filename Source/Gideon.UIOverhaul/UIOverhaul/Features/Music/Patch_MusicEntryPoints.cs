using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using Gideon.UIOverhaul.Shared;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Music
{
    /// <summary>
    /// A speaker in RimWorld's own play settings row, which opens the music window.
    ///
    /// <b>There rather than on this mod's button bar,</b> because that row is where every other on-screen toggle
    /// already lives and a player looking for a control over the map looks there first. It is also the one strip
    /// of chrome that is present on every map and in the world view.
    ///
    /// <b>A button, not a toggle.</b> The icons around it turn something on and off; this opens a window, and
    /// drawing it as a toggle would promise a state it does not have. It lights up while music is playing, which
    /// is a readout rather than a switch.
    /// </summary>
    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    internal static class Patch_PlaySettings_MusicButton
    {
        private static void Postfix(WidgetRow row, bool worldView)
        {
            UIGuard.Try("Music.PlaySettingsButton", () =>
            {
                if (row == null || worldView)
                    return;

                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                if (settings != null && !settings.musicPlayer)
                    return;

                if (MusicRivals.Any)
                    return;

                Texture2D icon = MusicEngine.NowPlaying != null && !MusicEngine.Paused
                    ? MusicGlyphs.Speaker
                    : MusicGlyphs.Note;

                if (icon == null)
                    return;

                if (row.ButtonIcon(icon, "Open the music window."))
                    Dialog_Music.Toggle();
            }, "The music button is missing from the play settings row. The corner strip still opens it.");
        }
    }

    /// <summary>
    /// A Music button on the main menu.
    ///
    /// <b>Because the menu is where a library gets built.</b> Somebody who has just installed this and wants to
    /// point it at their music folder should not have to start a colony first, and the menu's own music plays
    /// through our player too, so there is something to hear while they do it.
    ///
    /// <b>Drawn in the corner rather than added to the menu list.</b> Vanilla builds that list as a local
    /// collection inside <c>DoMainMenuControls</c>, so there is nothing to append to without rewriting the method
    /// -- and a mod button in the middle of Load, Save and Quit is a mod button in the way. A small control in
    /// the corner is discoverable without displacing anything.
    ///
    /// <b>Above the expansion icons, and that figure is vanilla's own.</b> <c>DoExpansionIcons</c> takes a
    /// 96 pixel band eight from the bottom left, which is exactly where a bottom left button wants to be; the
    /// offset below is derived from the same two numbers rather than guessed, so it cannot drift from what it is
    /// avoiding. The left side above it is clear -- the translation notice stops at 500 and the version string is
    /// in the top corner.
    /// </summary>
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    internal static class Patch_MainMenu_MusicButton
    {
        private const float Width = 132f;

        private const float Height = 28f;

        private const float Margin = 8f;

        /// <summary>The band <c>DoExpansionIcons</c> occupies: 96 tall, 8 up from the bottom.</summary>
        private const float ExpansionBand = 96f + 8f;

        private static void Postfix()
        {
            UIGuard.Try("Music.MainMenuButton", () =>
            {
                if (Current.ProgramState == ProgramState.Playing)
                    return;

                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                if (settings != null && !settings.musicPlayer)
                    return;

                if (MusicRivals.Any)
                    return;

                UIColorPaletteDef palette = UIColorPaletteDef.Active;

                Rect rect = new Rect(Margin, UI.screenHeight - ExpansionBand - Margin - Height, Width, Height);

                MusicTrack playing = MusicEngine.NowPlaying;

                if (TabParts.Button(rect, playing != null ? "Music: on" : "Music", palette, true, false,
                        playing != null
                            ? "Playing " + playing.Label + ".\n\nOpen the music window."
                            : "Playlists, and music from your own drive."))
                {
                    Dialog_Music.Toggle();
                }
            }, "The main menu music button is missing. It is still reachable once a colony is loaded.");
        }
    }
}
