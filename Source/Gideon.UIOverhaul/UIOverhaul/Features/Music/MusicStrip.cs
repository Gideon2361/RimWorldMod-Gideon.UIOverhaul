using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Music
{
    /// <summary>
    /// The now playing readout, docked with the other corner readouts.
    ///
    /// <b>Fixed, not draggable.</b> It sits in the stack the date and the weather are already in, so it is where
    /// the player's eye already goes for a readout and it cannot end up under the letter stack. A floating player
    /// needs a lock setting, a scale setting, a remembered position and its own colours before it is usable;
    /// this needs none of that and takes the palette the player already chose.
    ///
    /// <b>Three states, because there are three things worth saying.</b> Something is playing; the game is
    /// counting down to its next song; or nothing is playing and here is why. The third one is the reason this
    /// exists at all -- RimWorld's music can be silent for a hundred seconds and nothing on screen says that is
    /// deliberate.
    /// </summary>
    internal static class MusicStrip
    {
        /// <summary>Matches the panel it docks with, so the corner stays one column wide.</summary>
        private const float Width = 240f;

        private const float Height = 40f;

        /// <summary>The progress line along the bottom edge. Thin: it is a readout, not a control.</summary>
        private const float ProgressHeight = 3f;

        private const float ButtonSize = 20f;

        private const float Pad = 6f;

        /// <summary>Total height including the progress line, so the panel above knows where to sit.</summary>
        internal static float TotalHeight => Height + ProgressHeight;

        /// <summary>
        /// Draws the strip at the bottom of the space it is given and returns the new top.
        ///
        /// The same shape as the other blocks in the corner: they stack upward from the button bar, each one
        /// taking what it needs and handing back where it stopped.
        /// </summary>
        internal static float Draw(float x, float y, UIColorPaletteDef palette, UIOverhaulSettingsFile settings)
        {
            if (settings != null && (!settings.musicPlayer || !settings.showMusicWidget))
                return y;

            if (MusicRivals.Any)
                return y;

            float top = y - TotalHeight;

            UIGuard.Try("Music.Strip", () => Body(new Rect(x, top, Width, TotalHeight), palette),
                "The now playing strip is missing from the corner. The music window still works.");

            return top;
        }

        private static void Body(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.HudBackground);

            Color previousColor = GUI.color;
            GameFont previousFont = Text.Font;

            GUI.color = palette.Border;
            Widgets.DrawBox(rect, 1);
            GUI.color = previousColor;

            try
            {
                Rect row = new Rect(rect.x, rect.y, rect.width, Height);
                float x = row.x + Pad;

                MusicTrack playing = MusicEngine.NowPlaying;
                float silence = MusicEngine.SilenceRemaining;
                bool paused = MusicEngine.Paused;

                // ---- state icon ----
                Texture2D icon = playing != null && !paused
                    ? MusicGlyphs.Speaker
                    : silence > 0.5f
                        ? MusicGlyphs.Note
                        : MusicEngine.Intercepting
                            ? MusicGlyphs.Note
                            : MusicGlyphs.Dice;

                if (icon != null)
                {
                    GUI.color = playing != null && !paused ? palette.Accent : palette.TextDisabled;
                    GUI.DrawTexture(new Rect(x, row.y + 13f, 14f, 14f), icon);
                    GUI.color = palette.TextPrimary;
                }

                x += 14f + Pad;

                // ---- controls, laid out from the right so the label never overruns them ----
                float right = row.xMax - Pad;

                Rect caret = new Rect(right - 12f, row.y + 14f, 12f, 12f);

                if (MusicGlyphs.Down != null)
                {
                    GUI.color = Mouse.IsOver(caret) ? palette.TextPrimary : palette.TextDisabled;
                    GUI.DrawTexture(caret, MusicGlyphs.Down);
                    GUI.color = palette.TextPrimary;
                }

                if (Mouse.IsOver(caret))
                    TooltipHandler.TipRegion(caret, (TipSignal) "Choose what to play.");

                if (Widgets.ButtonInvisible(caret))
                    Dialog_MusicSwitch.Open();

                right -= 12f + Pad;

                // In the silence between vanilla's songs there is nothing to go back to and nothing to pause, so
                // the only control offered is the one that ends the wait.
                bool full = playing != null || MusicEngine.Intercepting;

                right -= ButtonSize;

                if (Button(new Rect(right, row.y + 10f, ButtonSize, ButtonSize), MusicGlyphs.Next, palette,
                        silence > 0.5f ? "Skip the wait and play now." : "Next track."))
                {
                    MusicEngine.Next();
                }

                if (full)
                {
                    right -= ButtonSize + 3f;

                    if (Button(new Rect(right, row.y + 10f, ButtonSize, ButtonSize),
                            paused ? MusicGlyphs.Play : MusicGlyphs.Pause, palette,
                            paused ? "Resume." : "Pause."))
                    {
                        MusicEngine.TogglePause();
                    }

                    right -= ButtonSize + 3f;

                    if (Button(new Rect(right, row.y + 10f, ButtonSize, ButtonSize), MusicGlyphs.Previous,
                            palette, "Previous track."))
                    {
                        MusicEngine.Previous();
                    }
                }

                right -= Pad;

                // ---- the two lines of text ----
                float width = Mathf.Max(30f, right - x);

                string title;
                string under;

                if (silence > 0.5f)
                {
                    title = "Next in " + Mathf.CeilToInt(silence) + "s";
                    under = "the game is choosing";
                }
                else if (playing != null)
                {
                    title = playing.Label;
                    under = MusicEngine.Intercepting ? PlayingFrom() : "the game is choosing";
                }
                else if (MusicEngine.Loading)
                {
                    title = "Loading";
                    under = PlayingFrom();
                }
                else
                {
                    title = MusicEngine.Problem ?? (MusicEngine.Stopped ? "Reached the end" : "Nothing playing");
                    under = MusicEngine.Intercepting ? PlayingFrom() : "the game is choosing";
                }

                TabParts.RowLabel(new Rect(x, row.y + 2f, width, 20f), title,
                    paused ? palette.TextSecondary : palette.TextPrimary);

                TabParts.RowLabel(new Rect(x, row.y + 20f, width, 18f), under, palette.TextDisabled,
                    GameFont.Tiny);

                // ---- progress ----
                float duration = MusicEngine.Duration;
                float fraction = duration > 0f ? Mathf.Clamp01(MusicEngine.Position / duration) : 0f;

                Rect bar = new Rect(rect.x + 1f, rect.yMax - ProgressHeight - 1f, rect.width - 2f,
                    ProgressHeight);

                Widgets.DrawBoxSolid(bar, palette.SurfaceSunken);

                if (fraction > 0f)
                {
                    Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * fraction, bar.height),
                        palette.Accent);
                }

                // The label opens the window, which is the whole strip's second job. Claimed last so the
                // controls above have already taken their clicks.
                Rect label = new Rect(x, row.y, width, Height);

                if (Mouse.IsOver(label))
                {
                    // The strip is 240 wide and the title gets about half of that, so a track name of any
                    // length is ellipsed here as a matter of course. The tooltip carries the whole of it, plus
                    // where it came from, which is the only place either can be read without opening the window.
                    string tip = playing != null
                        ? playing.Label + "\n\n" + playing.SourceLabel + "\n\nOpen the music window."
                        : "Open the music window.";

                    TooltipHandler.TipRegion(label, (TipSignal) tip);
                }

                if (Widgets.ButtonInvisible(label))
                    Dialog_Music.Toggle();
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }
        }

        private static string PlayingFrom()
        {
            string id = MusicStore.Source;

            if (id.StartsWith(MusicStore.SourceListPrefix, StringComparison.Ordinal))
                return id.Substring(MusicStore.SourceListPrefix.Length);

            if (id.StartsWith(MusicStore.SourceModPrefix, StringComparison.Ordinal))
                return id.Substring(MusicStore.SourceModPrefix.Length);

            if (id == MusicStore.SourceAll)
                return "all music";

            if (id == MusicStore.SourceFavourites)
                return "your favourites";

            if (id == MusicStore.SourceDrive)
                return "your drive";

            return "the game is choosing";
        }

        private static bool Button(Rect rect, Texture2D icon, UIColorPaletteDef palette, string tooltip)
        {
            bool over = Mouse.IsOver(rect);

            if (over)
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            if (icon != null)
            {
                GUI.color = over ? palette.TextPrimary : palette.TextSecondary;
                GUI.DrawTexture(rect.ContractedBy(4f), icon);
                GUI.color = palette.TextPrimary;
            }

            if (over && !tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, (TipSignal) tooltip);

            return Widgets.ButtonInvisible(rect);
        }
    }

    /// <summary>
    /// The compact source switcher the strip's caret opens.
    ///
    /// <b>A window rather than a float menu,</b> for the reason every other picker in this mod is: it shows the
    /// count beside each source and which one is playing, and it stays put while the mouse moves. It is small and
    /// it closes on a click outside, which is the behaviour a menu was wanted for.
    /// </summary>
    internal sealed class Dialog_MusicSwitch : Window
    {
        private const float RowHeight = 28f;

        private Vector2 scroll;

        internal Dialog_MusicSwitch()
        {
            doCloseX = false;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = true;
            draggable = false;
            drawShadow = true;
        }

        internal static void Open()
        {
            UIGuard.Try("Music.OpenSwitch", () =>
            {
                Window existing = Find.WindowStack.WindowOfType<Dialog_MusicSwitch>();

                if (existing != null)
                {
                    existing.Close(false);

                    return;
                }

                Find.WindowStack.Add(new Dialog_MusicSwitch());
            }, "The source switcher could not be opened.");
        }

        public override Vector2 InitialSize
        {
            get
            {
                int rows = 4 + MusicStore.Playlists.Count;

                return new Vector2(280f,
                    Mathf.Min(RowHeight * rows + 56f, UI.screenHeight * 0.6f));
            }
        }

        /// <summary>
        /// Anchored above the corner rather than centred on the screen.
        ///
        /// A picker opened from a control in the bottom right that appears in the middle of the map has lost the
        /// player's place, and this one is meant to be a two-click detour.
        /// </summary>
        protected override float Margin => 8f;

        public override void PreOpen()
        {
            base.PreOpen();

            windowRect.x = UI.screenWidth - windowRect.width - 8f;
            windowRect.y = Mathf.Max(8f, UI.screenHeight - windowRect.height - 220f);
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Music.Switch", inRect, () => Contents(inRect),
                "The source switcher could not finish drawing.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            float y = inRect.y;

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 18f), "Play from");

            GUI.color = palette.TextPrimary;
            Text.Font = GameFont.Small;

            y += 20f;

            List<string[]> rows = new List<string[]>
            {
                new[] { MusicStore.SourceGame, "Let the game choose" },
                new[] { MusicStore.SourceAll, "All music" },
                new[] { MusicStore.SourceFavourites, "Favourites" },
                new[] { MusicStore.SourceDrive, "From my drive" }
            };

            List<MusicPlaylist> lists = MusicStore.Playlists;

            for (int i = 0; i < lists.Count; i++)
                rows.Add(new[] { MusicStore.SourceListPrefix + lists[i].Name, lists[i].Name });

            Rect area = new Rect(inRect.x, y, inRect.width, Mathf.Max(40f, inRect.yMax - y - 30f));
            Rect view = new Rect(0f, 0f, area.width - 16f, rows.Count * RowHeight);

            Widgets.BeginScrollView(area, ref scroll, view);

            for (int i = 0; i < rows.Count; i++)
            {
                Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight);
                bool playing = MusicStore.Source == rows[i][0];

                if (playing)
                    Widgets.DrawBoxSolid(row, palette.SelectionOverlay);
                else if (Mouse.IsOver(row))
                    Widgets.DrawBoxSolid(row, palette.HoverOverlay);

                TabParts.RowLabel(new Rect(row.x + 8f, row.y, row.width - 50f, row.height), rows[i][1],
                    playing ? palette.Accent : palette.TextPrimary);

                if (rows[i][0] != MusicStore.SourceGame)
                {
                    Text.Font = GameFont.Tiny;
                    TextAnchor previousAnchor = Text.Anchor;
                    Text.Anchor = TextAnchor.MiddleRight;
                    GUI.color = palette.TextDisabled;

                    Widgets.Label(new Rect(row.xMax - 40f, row.y, 36f, row.height),
                        MusicLibrary.Count(rows[i][0]).ToString());

                    Text.Anchor = previousAnchor;
                    Text.Font = GameFont.Small;
                }

                GUI.color = palette.TextPrimary;

                if (!Widgets.ButtonInvisible(row))
                    continue;

                MusicEngine.PlaySource(rows[i][0]);
                SoundDefOf.Click.PlayOneShotOnCamera();
                Close();

                break;
            }

            Widgets.EndScrollView();

            if (TabParts.Button(new Rect(inRect.x, inRect.yMax - 26f, inRect.width, 26f), "Open music window",
                    palette))
            {
                Close();
                Dialog_Music.Toggle();
            }
        }
    }
}
