using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Music
{
    /// <summary>How the song list is ordered.</summary>
    internal enum MusicSort
    {
        /// <summary>The playlist's own sequence, which is the only order the player arranged.</summary>
        Order,

        Name,

        Source,

        Length
    }

    /// <summary>
    /// The music player: sources on the left, their songs in the middle, playback along the bottom.
    ///
    /// <b>One window rather than a tab.</b> Music is not colony data -- it has no map, no pawns and no ticks --
    /// and it has to be reachable from the main menu, where tabs do not exist. So it is a window, opened from the
    /// corner strip, from the play settings row and from the menu.
    ///
    /// <b>Selecting a source and playing it are two different clicks.</b> Clicking a playlist shows its songs;
    /// the play control on its row is what makes it the thing supplying music. This is the same rule as the
    /// colonist bar, and for the same reason: reading something should not commit you to it.
    ///
    /// <b>No context menus.</b> Every action is a button that is visible when it applies and dimmed when it does
    /// not, which is the standing rule in this mod. RimTunes puts add, remove, rename, reorder and copy behind
    /// right-clicks, and none of them can be found by looking.
    /// </summary>
    internal sealed class Dialog_Music : Window
    {
        private const float TitleHeight = 38f;

        private const float SidebarWidth = 236f;

        private const float ToolbarHeight = 44f;

        private const float HeaderHeight = 24f;

        private const float FooterHeight = 34f;

        private const float TransportHeight = 70f;

        private const float RowHeight = 29f;

        private const float SourceRowHeight = 27f;

        private const float ControlHeight = 24f;

        private const float Pad = 8f;

        /// <summary>Column widths, right to left. The name column takes whatever is left.</summary>
        private const float StateColumn = 22f;

        private const float FavouriteColumn = 20f;

        private const float SourceColumn = 196f;

        private const float LengthColumn = 52f;

        private const float OrdinalColumn = 34f;

        private readonly UITextBoxControl search = new UITextBoxControl
        {
            Placeholder = "Search", Icon = TexButton.Search, MaxLength = 40
        };

        /// <summary>Track ids ticked in the list. Cleared when the browsed source changes.</summary>
        private readonly HashSet<string> selected = new HashSet<string>();

        private Vector2 sidebarScroll;

        private Vector2 listScroll;

        /// <summary>
        /// The source whose songs are on screen, which is not necessarily the one playing.
        ///
        /// Starts at whatever is playing, so opening the window shows what you are listening to.
        /// </summary>
        private string browsing;

        private MusicSort sort = MusicSort.Order;

        private bool modsExpanded = true;

        /// <summary>Set while the seek bar is being dragged, so the readout follows the mouse.</summary>
        private bool seeking;

        private float seekTo;

        internal Dialog_Music()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            draggable = true;
            drawShadow = true;
            preventCameraMotion = false;

            browsing = MusicStore.Source == MusicStore.SourceGame ? MusicStore.SourceAll : MusicStore.Source;
        }

        /// <summary>Opens the window, or closes it if it is already up, and picks up new files on the way in.</summary>
        internal static void Toggle()
        {
            UIGuard.Try("Music.ToggleWindow", () =>
            {
                Window existing = Find.WindowStack.WindowOfType<Dialog_Music>();

                if (existing != null)
                {
                    existing.Close(false);

                    return;
                }

                int added = MusicLibrary.Rescan();

                if (added > 0)
                {
                    Messages.Message(added == 1
                            ? "One new file was found in a watched folder."
                            : added + " new files were found in watched folders.",
                        MessageTypeDefOf.SilentInput, false);
                }

                Find.WindowStack.Add(new Dialog_Music());
            }, "The music window could not be opened.");
        }

        public override Vector2 InitialSize
        {
            get
            {
                return new Vector2(Mathf.Min(1104f, UI.screenWidth - 40f),
                    Mathf.Min(660f, UI.screenHeight - 80f));
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Music.Window", inRect, () => Contents(inRect),
                "The music window could not finish drawing. Playback is unaffected.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                Title(new Rect(inRect.x, inRect.y, inRect.width, TitleHeight), palette);

                float top = inRect.y + TitleHeight;
                float bodyHeight = inRect.height - TitleHeight - TransportHeight - Pad;

                Sidebar(new Rect(inRect.x, top, SidebarWidth, bodyHeight), palette);

                Rect centre = new Rect(inRect.x + SidebarWidth + Pad, top,
                    inRect.width - SidebarWidth - Pad, bodyHeight);

                Centre(centre, palette);

                Transport(new Rect(inRect.x, inRect.yMax - TransportHeight, inRect.width, TransportHeight),
                    palette);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }
        }

        // -------------------------------------------------------------------------------------------
        // Title
        // -------------------------------------------------------------------------------------------

        private void Title(Rect rect, UIColorPaletteDef palette)
        {
            Text.Font = GameFont.Medium;
            GUI.color = palette.TextPrimary;

            const string heading = "Music";
            float width = UIRichText.WidthOf(heading);

            Widgets.Label(new Rect(rect.x, rect.y, width + 4f, rect.height), heading);

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;

            // The counts are the answer to "is my music actually in here", which is the first thing anybody
            // opening this window wants to know.
            string summary = MusicLibrary.TotalCount + " songs   "
                             + MusicLibrary.Mods.Count + " mods   "
                             + MusicLibrary.Drive.Count + " files from your drive";

            Rect summaryRect = new Rect(rect.x + width + 14f, rect.y + 12f,
                rect.width - width - 50f, rect.height - 12f);

            UIRichText.Label(summaryRect, summary);

            Text.Font = GameFont.Small;
            GUI.color = palette.TextPrimary;
        }

        // -------------------------------------------------------------------------------------------
        // Sidebar
        // -------------------------------------------------------------------------------------------

        private void Sidebar(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            Rect inner = rect.ContractedBy(1f);
            float height = SidebarHeight();

            Widgets.BeginScrollView(inner, ref sidebarScroll,
                new Rect(0f, 0f, inner.width - 16f, height));

            float y = 0f;
            float width = inner.width - 16f;

            SidebarHeader(new Rect(0f, y, width, 22f), "Sources", palette, null);
            y += 22f;

            y = SourceRow(y, width, MusicStore.SourceGame, "Let the game choose", MusicGlyphs.Dice, -1, palette);
            y = SourceRow(y, width, MusicStore.SourceAll, "All music", MusicGlyphs.Note,
                MusicLibrary.TotalCount, palette);
            y = SourceRow(y, width, MusicStore.SourceFavourites, "Favourites", MusicGlyphs.Star,
                MusicLibrary.Count(MusicStore.SourceFavourites), palette);
            y = SourceRow(y, width, MusicStore.SourceDrive, "From my drive", MusicGlyphs.Folder,
                MusicLibrary.Drive.Count, palette);

            y += 6f;

            SidebarHeader(new Rect(0f, y, width, 22f), "Playlists", palette, () => NewPlaylist());
            y += 22f;

            List<MusicPlaylist> lists = MusicStore.Playlists;

            for (int i = 0; i < lists.Count; i++)
            {
                MusicPlaylist list = lists[i];

                y = SourceRow(y, width, MusicStore.SourceListPrefix + list.Name, list.Name,
                    MusicGlyphs.PlaylistIcon(list.Icon), list.TrackIds.Count, palette);
            }

            if (lists.Count == 0)
            {
                GUI.color = palette.TextDisabled;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(12f, y, width - 16f, 20f), "None yet");
                Text.Font = GameFont.Small;
                GUI.color = palette.TextPrimary;
                y += 20f;
            }

            y += 6f;

            List<MusicSource> mods = MusicLibrary.Mods;

            SidebarHeader(new Rect(0f, y, width, 22f), "Mods (" + mods.Count + ")", palette,
                () => modsExpanded = !modsExpanded, modsExpanded);

            y += 22f;

            if (modsExpanded)
            {
                for (int i = 0; i < mods.Count; i++)
                {
                    y = SourceRow(y, width, MusicStore.SourceModPrefix + mods[i].Name, mods[i].Name,
                        MusicGlyphs.Package, mods[i].Tracks.Count, palette);
                }
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// How tall the sidebar's content is.
        ///
        /// Counted from the same lists the drawing walks rather than from a figure written down beside them: a
        /// playlist added while the window is open changes this, and a predicted height would clip the last row
        /// or leave a gap under it.
        /// </summary>
        private float SidebarHeight()
        {
            float height = 22f + SourceRowHeight * 4f + 6f + 22f;

            int lists = MusicStore.Playlists.Count;

            height += lists > 0 ? lists * SourceRowHeight : 20f;
            height += 6f + 22f;

            if (modsExpanded)
                height += MusicLibrary.Mods.Count * SourceRowHeight;

            return height;
        }

        private void SidebarHeader(Rect rect, string label, UIColorPaletteDef palette, Action action,
            bool? expanded = null)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(rect.x + 12f, rect.y + 4f, rect.width - 40f, rect.height), label);

            GUI.color = palette.TextPrimary;
            Text.Font = GameFont.Small;

            if (action == null)
                return;

            Rect button = new Rect(rect.xMax - 24f, rect.y + 2f, 18f, 18f);

            if (TabParts.Button(button, expanded.HasValue ? (expanded.Value ? "-" : "+") : "+", palette, true,
                    false, expanded.HasValue ? "Show or hide this group" : "New playlist"))
            {
                action();
            }
        }

        /// <summary>
        /// One row of the sidebar. Returns the next y.
        ///
        /// The play control is a separate hit target from the row, which is the whole point: the row selects the
        /// source for reading, the control commits to it for listening.
        /// </summary>
        private float SourceRow(float y, float width, string id, string label, Texture2D icon, int count,
            UIColorPaletteDef palette)
        {
            Rect row = new Rect(0f, y, width, SourceRowHeight);
            bool browsingThis = browsing == id;
            bool playingThis = MusicStore.Source == id;

            if (browsingThis)
            {
                Widgets.DrawBoxSolid(row, palette.SelectionOverlay);
                Widgets.DrawBoxSolid(new Rect(row.x, row.y + 2f, 2f, row.height - 4f), palette.Accent);
            }
            else if (Mouse.IsOver(row))
            {
                Widgets.DrawBoxSolid(row, palette.HoverOverlay);
            }

            if (icon != null)
            {
                GUI.color = browsingThis || playingThis ? palette.Accent : palette.TextSecondary;
                GUI.DrawTexture(new Rect(row.x + 10f, row.y + 6f, 15f, 15f), icon);
                GUI.color = palette.TextPrimary;
            }

            float right = row.xMax - 6f;

            if (count >= 0)
            {
                Text.Font = GameFont.Tiny;
                TextAnchor previousAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(right - 34f, row.y, 34f, row.height), count.ToString());

                Text.Anchor = previousAnchor;
                GUI.color = palette.TextPrimary;
                Text.Font = GameFont.Small;

                right -= 38f;
            }

            // The playing marker doubles as the button that starts this source, so there is one control rather
            // than a badge beside a button that mean the same thing.
            Rect play = new Rect(right - 20f, row.y + 4f, 19f, 19f);
            bool over = Mouse.IsOver(play);

            if (MusicGlyphs.Play != null)
            {
                GUI.color = playingThis ? palette.Accent : over ? palette.TextPrimary : palette.TextDisabled;
                GUI.DrawTexture(play.ContractedBy(3f), playingThis && !MusicEngine.Paused
                    ? MusicGlyphs.Speaker
                    : MusicGlyphs.Play);
                GUI.color = palette.TextPrimary;
            }

            if (Mouse.IsOver(play))
            {
                TooltipHandler.TipRegion(play, (TipSignal) (playingThis
                    ? "This is what is playing."
                    : "Play from " + label + "."));
            }

            if (Widgets.ButtonInvisible(play))
            {
                if (!playingThis)
                {
                    MusicEngine.PlaySource(id);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
            }
            else if (Widgets.ButtonInvisible(row))
            {
                if (browsing != id)
                {
                    browsing = id;
                    selected.Clear();
                    listScroll = Vector2.zero;

                    // The game's own choice has no track list to sort, so Order would show nothing meaningful.
                    sort = id == MusicStore.SourceGame ? MusicSort.Name : MusicSort.Order;
                }

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            Rect band = new Rect(row.x + 31f, row.y, Mathf.Max(20f, right - row.x - 54f), row.height);

            TabParts.RowLabel(band, label, browsingThis ? palette.TextPrimary : palette.TextSecondary);

            if (Mouse.IsOver(band))
                TooltipHandler.TipRegion(band, (TipSignal) label);

            return y + SourceRowHeight;
        }

        // -------------------------------------------------------------------------------------------
        // Centre: toolbar, list, selection footer
        // -------------------------------------------------------------------------------------------

        private void Centre(Rect rect, UIColorPaletteDef palette)
        {
            List<MusicTrack> tracks = Visible();

            Toolbar(new Rect(rect.x, rect.y, rect.width, ToolbarHeight), palette);

            float y = rect.y + ToolbarHeight;

            ListHeader(new Rect(rect.x, y, rect.width, HeaderHeight), palette);

            y += HeaderHeight;

            bool anySelected = selected.Count > 0;
            float listHeight = rect.yMax - y - (anySelected ? FooterHeight : 0f);

            List(new Rect(rect.x, y, rect.width, listHeight), tracks, palette);

            if (anySelected)
            {
                SelectionFooter(new Rect(rect.x, rect.yMax - FooterHeight, rect.width, FooterHeight), tracks,
                    palette);
            }
        }

        private void Toolbar(Rect rect, UIColorPaletteDef palette)
        {
            float y = rect.y + (rect.height - ControlHeight) * 0.5f;
            float x = rect.x;

            if (search.Draw(new Rect(x, y, 212f, ControlHeight), palette))
                listScroll = Vector2.zero;

            x += 212f + Pad;

            // Order is absent for a source that has no order of its own: a mod's songs and the favourites are
            // sets, not sequences, and offering a sort that means nothing is worse than offering three.
            bool ordered = browsing != null
                           && browsing.StartsWith(MusicStore.SourceListPrefix, StringComparison.Ordinal);

            if (ordered)
            {
                TabParts.Segment(new Rect(x, y, 56f, ControlHeight), "Order", sort == MusicSort.Order, palette,
                    () => sort = MusicSort.Order);

                x += 56f + TabParts.SegmentGap;
            }
            else if (sort == MusicSort.Order)
            {
                sort = MusicSort.Name;
            }

            TabParts.Segment(new Rect(x, y, 52f, ControlHeight), "Name", sort == MusicSort.Name, palette,
                () => sort = MusicSort.Name);

            x += 52f + TabParts.SegmentGap;

            TabParts.Segment(new Rect(x, y, 60f, ControlHeight), "Source", sort == MusicSort.Source, palette,
                () => sort = MusicSort.Source);

            x += 60f + TabParts.SegmentGap;

            TabParts.Segment(new Rect(x, y, 60f, ControlHeight), "Length", sort == MusicSort.Length, palette,
                () => sort = MusicSort.Length);

            // Right hand end, laid out from the right so a button never overlaps the sort segments.
            float right = rect.xMax;

            MusicPlaylist list = BrowsedPlaylist();

            if (list != null)
            {
                float deleteWidth = TabParts.ButtonWidth("Delete");
                right -= deleteWidth;

                if (TabParts.Button(new Rect(right, y, deleteWidth, ControlHeight), "Delete", palette, true,
                        false, "Delete this playlist. The songs in it are not touched."))
                {
                    DeletePlaylist(list);
                }

                right -= Pad;

                float renameWidth = TabParts.ButtonWidth("Rename");
                right -= renameWidth;

                if (TabParts.Button(new Rect(right, y, renameWidth, ControlHeight), "Rename", palette, true,
                        false, "Change this playlist's name or icon."))
                {
                    RenamePlaylist(list);
                }

                right -= Pad;
            }

            float addWidth = TabParts.ButtonWidth("Add music");
            right -= addWidth;

            if (TabParts.Button(new Rect(right, y, addWidth, ControlHeight), "Add music", palette, true, false,
                    "Bring in ogg, wav, mp3, mp4 or m4a files from your own drive."))
            {
                Dialog_MusicImport.Open(list, () => Refresh());
            }
        }

        private void ListHeader(Rect rect, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextDisabled;

            float x = rect.x + StateColumn + FavouriteColumn + 4f;
            float nameWidth = NameWidth(rect.width);

            Widgets.Label(new Rect(x, rect.y + 4f, nameWidth, rect.height), "Song");

            x += nameWidth;

            Widgets.Label(new Rect(x, rect.y + 4f, SourceColumn, rect.height), "Source");

            x += SourceColumn;

            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(x, rect.y + 4f, LengthColumn, rect.height), "Length");

            Text.Anchor = previousAnchor;
            GUI.color = palette.TextPrimary;
            Text.Font = previousFont;

            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), palette.Border);
        }

        /// <summary>What is left for the song's name after the fixed columns and the scrollbar.</summary>
        private float NameWidth(float total)
        {
            return Mathf.Max(80f, total - StateColumn - FavouriteColumn - SourceColumn - LengthColumn
                                  - OrdinalColumn - 4f - 16f);
        }

        private void List(Rect rect, List<MusicTrack> tracks, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(1f);

            if (tracks.Count == 0)
            {
                GUI.color = palette.TextDisabled;
                Text.Anchor = TextAnchor.MiddleCenter;

                Widgets.Label(inner, Empty());

                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextPrimary;

                return;
            }

            Rect view = new Rect(0f, 0f, inner.width - 16f, tracks.Count * RowHeight);

            Widgets.BeginScrollView(inner, ref listScroll, view);

            MusicTrack playing = MusicEngine.NowPlaying;

            for (int i = 0; i < tracks.Count; i++)
            {
                Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight);

                // Rows outside the visible band are skipped, which matters here: All music on a heavily modded
                // game is several hundred rows and every one of them would otherwise measure a label.
                if (row.yMax < listScroll.y - RowHeight || row.y > listScroll.y + inner.height + RowHeight)
                    continue;

                Row(row, tracks[i], i + 1, playing, palette);
            }

            Widgets.EndScrollView();
        }

        private string Empty()
        {
            if (!search.Text.NullOrEmpty())
                return "No songs match " + search.Text;

            if (browsing == MusicStore.SourceGame)
                return "The game chooses from every song it thinks fits the moment.";

            if (browsing == MusicStore.SourceFavourites)
                return "Star a song to keep it here.";

            if (browsing == MusicStore.SourceDrive)
                return "Add music to bring in files from your own drive.";

            return "This playlist is empty. Add music, or add songs from another source.";
        }

        private void Row(Rect row, MusicTrack track, int ordinal, MusicTrack playing, UIColorPaletteDef palette)
        {
            bool isPlaying = playing != null && track != null && playing.Id == track.Id;
            bool isSelected = track != null && selected.Contains(track.Id);

            if (isPlaying)
            {
                Widgets.DrawBoxSolid(row, palette.SelectionOverlay);
                Widgets.DrawBoxSolid(new Rect(row.x, row.y, 2f, row.height), palette.Accent);
            }
            else if (isSelected)
            {
                Widgets.DrawBoxSolid(row, palette.PressedOverlay);
            }
            else if (Mouse.IsOver(row))
            {
                Widgets.DrawBoxSolid(row, palette.HoverOverlay);
            }

            if (track == null)
                return;

            float x = row.x;

            // ---- play / state ----
            Rect state = new Rect(x, row.y + 4f, StateColumn, 21f);

            if (MusicGlyphs.Play != null)
            {
                GUI.color = isPlaying
                    ? palette.Accent
                    : track.Missing
                        ? palette.Danger
                        : Mouse.IsOver(state)
                            ? palette.TextPrimary
                            : palette.TextDisabled;

                GUI.DrawTexture(state.ContractedBy(4f), track.Missing
                    ? MusicGlyphs.Warning
                    : isPlaying && !MusicEngine.Paused
                        ? MusicGlyphs.Speaker
                        : MusicGlyphs.Play);

                GUI.color = palette.TextPrimary;
            }

            bool playClicked = Widgets.ButtonInvisible(state);

            x += StateColumn;

            // ---- favourite ----
            Rect favourite = new Rect(x, row.y + 4f, FavouriteColumn, 21f);
            bool starred = MusicStore.Favourite(track.Id);

            if (MusicGlyphs.Star != null)
            {
                GUI.color = starred ? palette.Warning : Mouse.IsOver(favourite)
                    ? palette.TextSecondary
                    : palette.TextDisabled;

                GUI.DrawTexture(favourite.ContractedBy(4f), starred
                    ? MusicGlyphs.Star
                    : MusicGlyphs.StarOutline);

                GUI.color = palette.TextPrimary;
            }

            if (Mouse.IsOver(favourite))
            {
                TooltipHandler.TipRegion(favourite,
                    (TipSignal) (starred ? "In your favourites." : "Add to your favourites."));
            }

            bool favouriteClicked = Widgets.ButtonInvisible(favourite);

            x += FavouriteColumn + 4f;

            // ---- name ----
            float nameWidth = NameWidth(row.width);

            float labelWidth = nameWidth;

            if (track.Missing)
            {
                float pillWidth = TabParts.PillWidth("File moved or deleted") + 6f;
                labelWidth = Mathf.Max(40f, nameWidth - pillWidth - 6f);

                TabParts.Pill(row, x + labelWidth + 4f, row.y + 5f, "File moved or deleted", palette.Danger,
                    palette);
            }

            Rect nameBand = new Rect(x, row.y, labelWidth, row.height);

            TabParts.RowLabel(nameBand, track.Label, track.Missing ? palette.Danger : palette.TextPrimary);

            x += nameWidth;

            // ---- source ----
            string source = track.Kind == MusicTrackKind.File
                ? track.SourceLabel + "   " + track.Extension.TrimStart('.')
                : track.SourceLabel;

            TabParts.RowLabel(new Rect(x, row.y, SourceColumn - 6f, row.height), source,
                track.Kind == MusicTrackKind.File ? palette.TextDisabled : palette.TextSecondary,
                GameFont.Tiny);

            x += SourceColumn;

            // ---- length ----
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(x, row.y, LengthColumn, row.height), MusicTrack.Duration(Length(track)));

            x += LengthColumn;

            // ---- ordinal ----
            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(x, row.y, OrdinalColumn - 4f, row.height), ordinal.ToString());

            Text.Anchor = previousAnchor;
            GUI.color = palette.TextPrimary;
            Text.Font = GameFont.Small;

            // The name and the source both ellipse in their columns, and a song called "Depths Of The
            // Multiverse" in a mod with a long name loses the end of both. The tooltip is where the whole of
            // each is readable, so it carries them in full whether or not they fitted.
            if (Mouse.IsOver(nameBand))
                TooltipHandler.TipRegion(nameBand, (TipSignal) Tooltip(track));

            // Handled after the drawing so the two controls above have already claimed their clicks. The row's
            // own hit test is everything the controls did not take.
            if (playClicked)
            {
                MusicEngine.PlayTrack(browsing == MusicStore.SourceGame ? MusicStore.SourceAll : browsing, track);
                SoundDefOf.Click.PlayOneShotOnCamera();

                return;
            }

            if (favouriteClicked)
            {
                MusicStore.ToggleFavourite(track.Id);
                MusicEngine.Invalidate();
                SoundDefOf.Click.PlayOneShotOnCamera();

                return;
            }

            if (!Widgets.ButtonInvisible(row))
                return;

            if (!selected.Remove(track.Id))
                selected.Add(track.Id);

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Everything about a track, for the row's tooltip.
        ///
        /// The name first and on its own line, because that is the thing the column was too narrow for and the
        /// reason anybody is hovering. For a file the full path follows, which is the only place in this
        /// interface it can be read in full and the thing somebody needs when a file has gone missing.
        /// </summary>
        private static string Tooltip(MusicTrack track)
        {
            string text = track.Label;

            if (track.Kind == MusicTrackKind.File)
            {
                text += "\n\n" + track.FilePath;

                if (track.Missing)
                    text += "\n\nThis file is no longer there. Move it back, or remove it from the library.";
            }
            else
            {
                text += "\n\nFrom " + track.SourceLabel + ".";
            }

            float length = Length(track);

            if (length > 0f)
                text += "\n\n" + MusicTrack.Duration(length);
            else if (!track.Missing)
                text += "\n\nIts length is not known until it has played once.";

            return text;
        }

        /// <summary>The length to show: the clip's own for a song, the learned one for a file.</summary>
        private static float Length(MusicTrack track)
        {
            if (track.Length > 0f)
                return track.Length;

            return MusicStore.KnownLength(track.Id);
        }

        private void SelectionFooter(Rect rect, List<MusicTrack> tracks, UIColorPaletteDef palette)
        {
            float y = rect.y + (rect.height - ControlHeight) * 0.5f;

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(rect.x, rect.y + 8f, 160f, rect.height),
                selected.Count + (selected.Count == 1 ? " song selected" : " songs selected"));

            GUI.color = palette.TextPrimary;
            Text.Font = GameFont.Small;

            float right = rect.xMax;
            MusicPlaylist list = BrowsedPlaylist();

            float clearWidth = TabParts.ButtonWidth("Clear");
            right -= clearWidth;

            if (TabParts.Button(new Rect(right, y, clearWidth, ControlHeight), "Clear", palette))
                selected.Clear();

            right -= Pad;

            // Reordering only means anything inside a playlist, and only in the playlist's own order: moving a
            // row up while sorted by name would move it somewhere the list does not show.
            if (list != null && sort == MusicSort.Order)
            {
                right -= ControlHeight;

                // Icon segments rather than labelled buttons: an arrow is what a reorder control looks like
                // everywhere, and two of them fit where "Move down" alone would not.
                TabParts.Segment(new Rect(right, y, ControlHeight, ControlHeight), MusicGlyphs.Down, false,
                    palette, () => Move(list, 1), "Move the selected songs down.");

                right -= TabParts.SegmentGap + ControlHeight;

                TabParts.Segment(new Rect(right, y, ControlHeight, ControlHeight), MusicGlyphs.Up, false,
                    palette, () => Move(list, -1), "Move the selected songs up.");

                right -= Pad;
            }

            if (list != null)
            {
                float removeWidth = TabParts.ButtonWidth("Remove from playlist");
                right -= removeWidth;

                if (TabParts.Button(new Rect(right, y, removeWidth, ControlHeight), "Remove from playlist",
                        palette, true, false, "Take these out of this playlist. Nothing is deleted."))
                {
                    RemoveFromPlaylist(list);
                }

                right -= Pad;
            }
            else if (browsing == MusicStore.SourceDrive)
            {
                float forgetWidth = TabParts.ButtonWidth("Remove from library");
                right -= forgetWidth;

                if (TabParts.Button(new Rect(right, y, forgetWidth, ControlHeight), "Remove from library",
                        palette, true, false,
                        "Forget these files. The files themselves are left where they are on your drive."))
                {
                    Forget();
                }

                right -= Pad;
            }

            float addWidth = TabParts.ButtonWidth("Add to playlist");
            right -= addWidth;

            if (TabParts.Button(new Rect(right, y, addWidth, ControlHeight), "Add to playlist", palette, true,
                    true, "Put these songs in one of your playlists."))
            {
                AddToPlaylist();
            }
        }

        // -------------------------------------------------------------------------------------------
        // Transport
        // -------------------------------------------------------------------------------------------

        private void Transport(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            Rect inner = rect.ContractedBy(8f);
            float x = inner.x;

            MusicTrack playing = MusicEngine.NowPlaying;
            bool paused = MusicEngine.Paused;

            // ---- buttons ----
            if (TransportButton(new Rect(x, inner.y + 14f, 26f, 26f), MusicGlyphs.Previous, palette, false))
                MusicEngine.Previous();

            x += 30f;

            if (TransportButton(new Rect(x, inner.y + 9f, 36f, 36f),
                    paused ? MusicGlyphs.Play : MusicGlyphs.Pause, palette, true))
            {
                MusicEngine.TogglePause();
            }

            x += 42f;

            if (TransportButton(new Rect(x, inner.y + 14f, 26f, 26f), MusicGlyphs.Next, palette, false))
                MusicEngine.Next();

            x += 36f;

            // ---- now playing ----
            string title = playing != null
                ? playing.Label
                : MusicEngine.Loading
                    ? "Loading"
                    : MusicEngine.Problem ?? "Nothing playing";

            Rect titleBand = new Rect(x, inner.y + 3f, 240f, 22f);

            TabParts.RowLabel(titleBand, title, palette.TextPrimary);

            TabParts.RowLabel(new Rect(x, inner.y + 25f, 240f, 20f),
                playing != null ? playing.SourceLabel : SourceLabel(), palette.TextDisabled, GameFont.Tiny);

            if (playing != null && Mouse.IsOver(titleBand))
                TooltipHandler.TipRegion(titleBand, (TipSignal) Tooltip(playing));

            x += 250f;

            // ---- seek and the playing-from line ----
            float rightBlock = 118f + 8f + TabParts.IconSegmentsWidth(3, ControlHeight) + 12f;
            Rect bar = new Rect(x, inner.y + 12f, Mathf.Max(80f, inner.xMax - x - rightBlock), 24f);

            SeekBar(bar, palette);

            x = bar.xMax + 12f;

            // ---- shuffle and repeat ----
            // Toggles, not segments. Each of these three has an off state reached by pressing it again, which is
            // the one thing a segment refuses to do.
            TabParts.IconToggle(new Rect(x, inner.y + 14f, ControlHeight, ControlHeight), MusicGlyphs.Shuffle,
                MusicStore.Shuffle, palette, ToggleShuffle,
                MusicStore.Shuffle ? "Shuffle is on. Click to play in order." : "Shuffle.");

            x += ControlHeight + TabParts.SegmentGap;

            TabParts.IconToggle(new Rect(x, inner.y + 14f, ControlHeight, ControlHeight), MusicGlyphs.Repeat,
                MusicStore.Repeat == MusicRepeat.All, palette, () => SetRepeat(MusicRepeat.All),
                MusicStore.Repeat == MusicRepeat.All
                    ? "Repeating. Click to stop at the end."
                    : "Start the source again at the end.");

            x += ControlHeight + TabParts.SegmentGap;

            TabParts.IconToggle(new Rect(x, inner.y + 14f, ControlHeight, ControlHeight), MusicGlyphs.RepeatOne,
                MusicStore.Repeat == MusicRepeat.One, palette, () => SetRepeat(MusicRepeat.One),
                MusicStore.Repeat == MusicRepeat.One
                    ? "Repeating this track. Click to stop."
                    : "Repeat this track.");

            x += ControlHeight + 12f;

            Volume(new Rect(x, inner.y + 14f, Mathf.Max(60f, inner.xMax - x), ControlHeight), palette);
        }

        private static bool TransportButton(Rect rect, Texture2D icon, UIColorPaletteDef palette, bool primary)
        {
            bool over = Mouse.IsOver(rect);

            if (primary)
                UIElementPainter.FillRounded(rect, over ? palette.Accent : palette.AccentMuted);
            else
                UIElementPainter.OutlineRounded(rect, palette.Border, palette.WindowBackground);

            if (icon != null)
            {
                GUI.color = primary
                    ? over ? palette.WindowBackground : palette.Accent
                    : over ? palette.TextPrimary : palette.TextSecondary;

                GUI.DrawTexture(rect.ContractedBy(primary ? 10f : 7f), icon);
                GUI.color = palette.TextPrimary;
            }

            return Widgets.ButtonInvisible(rect);
        }

        /// <summary>
        /// The progress bar, draggable only where dragging works.
        ///
        /// While the game is choosing, its manager holds a wall-clock stamp for when the song ends, so moving the
        /// playhead would leave that stamp wrong and cut the track off. The handle is simply absent there rather
        /// than present and ignoring the drag.
        /// </summary>
        private void SeekBar(Rect rect, UIColorPaletteDef palette)
        {
            float duration = MusicEngine.Duration;
            float position = seeking ? seekTo : MusicEngine.Position;
            float fraction = duration > 0f ? Mathf.Clamp01(position / duration) : 0f;

            Rect track = new Rect(rect.x, rect.y + 4f, rect.width, 4f);

            UIElementPainter.FillRounded(track, palette.SurfaceSunken);

            if (fraction > 0f)
                UIElementPainter.FillRounded(new Rect(track.x, track.y, track.width * fraction, 4f), palette.Accent);

            bool canSeek = MusicEngine.CanSeek && duration > 0f;

            if (canSeek)
            {
                Rect handle = new Rect(track.x + track.width * fraction - 5f, track.y - 3f, 10f, 10f);

                UIElementPainter.FillRounded(handle, palette.TextPrimary);

                Rect grab = new Rect(rect.x, rect.y, rect.width, 12f);

                if (Mouse.IsOver(grab) && Input.GetMouseButton(0))
                {
                    seeking = true;
                    seekTo = Mathf.Clamp01((Event.current.mousePosition.x - track.x) / track.width) * duration;
                }
                else if (seeking)
                {
                    seeking = false;
                    MusicEngine.Seek(seekTo);
                }
            }
            else
            {
                seeking = false;
            }

            Text.Font = GameFont.Tiny;
            TextAnchor previousAnchor = Text.Anchor;
            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(rect.x, rect.y + 8f, 60f, 16f), MusicTrack.Elapsed(position));

            Text.Anchor = TextAnchor.UpperCenter;

            Widgets.Label(new Rect(rect.x + 60f, rect.y + 8f, Mathf.Max(20f, rect.width - 120f), 16f),
                Middle());

            Text.Anchor = TextAnchor.UpperRight;

            Widgets.Label(new Rect(rect.xMax - 60f, rect.y + 8f, 60f, 16f), MusicTrack.Duration(duration));

            Text.Anchor = previousAnchor;
            GUI.color = palette.TextPrimary;
            Text.Font = GameFont.Small;
        }

        /// <summary>What sits between the two times: where the music is coming from, or why there is none.</summary>
        private string Middle()
        {
            float silence = MusicEngine.SilenceRemaining;

            if (silence > 0.5f)
                return "Next in " + Mathf.CeilToInt(silence) + "s";

            if (MusicEngine.Stopped)
                return "Reached the end";

            return "Playing from " + SourceLabel();
        }

        private static string SourceLabel()
        {
            string id = MusicStore.Source;

            if (id == MusicStore.SourceGame)
                return "the game's own choice";

            if (id == MusicStore.SourceAll)
                return "all music";

            if (id == MusicStore.SourceFavourites)
                return "your favourites";

            if (id == MusicStore.SourceDrive)
                return "your drive";

            if (id.StartsWith(MusicStore.SourceModPrefix, StringComparison.Ordinal))
                return id.Substring(MusicStore.SourceModPrefix.Length);

            if (id.StartsWith(MusicStore.SourceListPrefix, StringComparison.Ordinal))
                return id.Substring(MusicStore.SourceListPrefix.Length);

            return id;
        }

        /// <summary>
        /// RimWorld's own music volume, not a second slider of ours.
        ///
        /// One volume that the game's audio options and this bar both write is the only arrangement that cannot
        /// disagree with itself, and it means somebody who turns the music down in the options finds it down here.
        /// </summary>
        private void Volume(Rect rect, UIColorPaletteDef palette)
        {
            if (MusicGlyphs.Speaker != null)
            {
                GUI.color = palette.TextSecondary;
                GUI.DrawTexture(new Rect(rect.x, rect.y + 5f, 14f, 14f), MusicGlyphs.Speaker);
                GUI.color = palette.TextPrimary;
            }

            Rect slider = new Rect(rect.x + 20f, rect.y + 4f, Mathf.Max(30f, rect.width - 20f), 16f);

            float before = Prefs.VolumeMusic;
            float after = Widgets.HorizontalSlider(slider, before, 0f, 1f);

            if (!Mathf.Approximately(before, after))
            {
                Prefs.VolumeMusic = after;

                // Written now rather than on window close: the options window does the same, and a crash after
                // dragging a slider should not lose the setting.
                Prefs.Save();
            }

            if (Mouse.IsOver(rect))
                TooltipHandler.TipRegion(rect, (TipSignal) "The game's own music volume.");
        }

        // -------------------------------------------------------------------------------------------
        // Actions
        // -------------------------------------------------------------------------------------------

        /// <summary>The songs on screen: the browsed source, filtered by the search, in the chosen order.</summary>
        private List<MusicTrack> Visible()
        {
            List<MusicTrack> tracks = MusicLibrary.Tracks(browsing == MusicStore.SourceGame
                ? MusicStore.SourceAll
                : browsing);

            string query = search.Text;

            if (!query.NullOrEmpty())
            {
                List<MusicTrack> matched = new List<MusicTrack>();

                for (int i = 0; i < tracks.Count; i++)
                {
                    MusicTrack track = tracks[i];

                    if (track == null)
                        continue;

                    if (track.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                        || track.SourceLabel.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matched.Add(track);
                    }
                }

                tracks = matched;
            }

            if (sort == MusicSort.Order)
                return tracks;

            UIGuard.Try("Music.Sort", () => tracks.Sort(Compare), null);

            return tracks;
        }

        private int Compare(MusicTrack a, MusicTrack b)
        {
            if (a == null || b == null)
                return 0;

            if (sort == MusicSort.Source)
            {
                int source = string.Compare(a.SourceLabel, b.SourceLabel, StringComparison.OrdinalIgnoreCase);

                if (source != 0)
                    return source;
            }
            else if (sort == MusicSort.Length)
            {
                // Unknown lengths last rather than first: a column of dashes above the songs somebody can
                // actually sort by would make the sort look broken.
                float left = Length(a);
                float right = Length(b);

                if (left <= 0f != right <= 0f)
                    return left <= 0f ? 1 : -1;

                int length = left.CompareTo(right);

                if (length != 0)
                    return length;
            }

            return string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The playlist being browsed, or null when the source is not one.</summary>
        private MusicPlaylist BrowsedPlaylist()
        {
            if (browsing == null || !browsing.StartsWith(MusicStore.SourceListPrefix, StringComparison.Ordinal))
                return null;

            return MusicStore.Playlist(browsing.Substring(MusicStore.SourceListPrefix.Length));
        }

        private void Refresh()
        {
            MusicLibrary.Invalidate();
            MusicEngine.Invalidate();
        }

        private void NewPlaylist()
        {
            Dialog_MusicPlaylist.OpenNew(list =>
            {
                browsing = MusicStore.SourceListPrefix + list.Name;
                selected.Clear();
                sort = MusicSort.Order;
                Refresh();
            });
        }

        private void RenamePlaylist(MusicPlaylist list)
        {
            Dialog_MusicPlaylist.OpenRename(list, renamed =>
            {
                browsing = MusicStore.SourceListPrefix + renamed.Name;
                Refresh();
            });
        }

        private void DeletePlaylist(MusicPlaylist list)
        {
            UIGuard.Try("Music.DeletePlaylist", () =>
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Delete the playlist " + list.Name + "? The songs in it are not touched.",
                    () =>
                    {
                        MusicStore.Delete(list);

                        browsing = MusicStore.SourceAll;
                        selected.Clear();
                        Refresh();
                    }, true));
            }, "The playlist was not deleted.");
        }

        private void AddToPlaylist()
        {
            List<string> ids = new List<string>(selected);

            Dialog_MusicPlaylist.OpenPick(list =>
            {
                int added = 0;

                for (int i = 0; i < ids.Count; i++)
                {
                    if (list.TrackIds.Contains(ids[i]))
                        continue;

                    list.TrackIds.Add(ids[i]);
                    added++;
                }

                MusicStore.Save();
                Refresh();
                selected.Clear();

                Messages.Message(added == 0
                        ? "Already in " + list.Name + "."
                        : added + (added == 1 ? " song added to " : " songs added to ") + list.Name + ".",
                    MessageTypeDefOf.TaskCompletion, false);
            });
        }

        private void RemoveFromPlaylist(MusicPlaylist list)
        {
            UIGuard.Try("Music.RemoveFromPlaylist", () =>
            {
                foreach (string id in selected)
                    list.TrackIds.Remove(id);

                MusicStore.Save();
                selected.Clear();
                Refresh();
            }, "Those songs were not removed.");
        }

        private void Forget()
        {
            UIGuard.Try("Music.ForgetTracks", () =>
            {
                List<string> ids = new List<string>(selected);

                for (int i = 0; i < ids.Count; i++)
                    MusicStore.Forget(ids[i]);

                selected.Clear();
                Refresh();
            }, "Those files were not removed from the library.");
        }

        /// <summary>
        /// Moves the selected rows one place up or down inside a playlist.
        ///
        /// Walked from the end when moving down and from the start when moving up, so a block of selected rows
        /// moves as a block instead of collapsing into one place.
        /// </summary>
        private void Move(MusicPlaylist list, int direction)
        {
            UIGuard.Try("Music.Reorder", () =>
            {
                List<string> ids = list.TrackIds;

                if (direction < 0)
                {
                    for (int i = 1; i < ids.Count; i++)
                    {
                        if (selected.Contains(ids[i]) && !selected.Contains(ids[i - 1]))
                            Swap(ids, i, i - 1);
                    }
                }
                else
                {
                    for (int i = ids.Count - 2; i >= 0; i--)
                    {
                        if (selected.Contains(ids[i]) && !selected.Contains(ids[i + 1]))
                            Swap(ids, i, i + 1);
                    }
                }

                MusicStore.Save();
                Refresh();
            }, "The order was not changed.");
        }

        private static void Swap(List<string> ids, int a, int b)
        {
            string held = ids[a];

            ids[a] = ids[b];
            ids[b] = held;
        }

        private void ToggleShuffle()
        {
            MusicStore.Shuffle = !MusicStore.Shuffle;
            MusicEngine.Invalidate();
        }

        /// <summary>
        /// Sets repeat, or turns it off if the mode chosen is already on.
        ///
        /// Three states down two buttons: pressing the lit one is how Off is reached, which is what a pair of
        /// toggles means and saves a third segment for a state nobody picks deliberately.
        /// </summary>
        private static void SetRepeat(MusicRepeat mode)
        {
            MusicStore.Repeat = MusicStore.Repeat == mode ? MusicRepeat.Off : mode;
        }
    }
}
