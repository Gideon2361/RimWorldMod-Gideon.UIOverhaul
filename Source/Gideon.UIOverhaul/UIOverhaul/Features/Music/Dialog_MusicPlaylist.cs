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
    /// <summary>
    /// Naming a playlist and giving it a picture, whether it is new or being renamed.
    ///
    /// <b>The icon is a grid, not a menu.</b> Eight pictures fit on one line and a grid shows all of them at
    /// once, where a dropdown would show one and hide seven behind a click.
    ///
    /// <b>Save is refused rather than disabled quietly.</b> An empty name or one already taken says which, on the
    /// line under the box, because a greyed button with no explanation is the failure mode this mod keeps out.
    /// </summary>
    internal sealed class Dialog_MusicPlaylist : Window
    {
        private const float Pad = 10f;

        private const float RowHeight = 30f;

        private const float IconSize = 34f;

        private readonly UITextBoxControl name = new UITextBoxControl
        {
            Placeholder = "Playlist name", MaxLength = 40
        };

        private readonly MusicPlaylist editing;

        private readonly Action<MusicPlaylist> done;

        private int icon;

        private Dialog_MusicPlaylist(MusicPlaylist editing, Action<MusicPlaylist> done)
        {
            this.editing = editing;
            this.done = done;

            if (editing != null)
            {
                name.Text = editing.Name;
                icon = editing.Icon;
            }

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
            drawShadow = true;
        }

        internal static void OpenNew(Action<MusicPlaylist> done)
        {
            UIGuard.Try("Music.OpenNewPlaylist",
                () => Find.WindowStack.Add(new Dialog_MusicPlaylist(null, done)),
                "The new playlist window could not be opened.");
        }

        internal static void OpenRename(MusicPlaylist list, Action<MusicPlaylist> done)
        {
            if (list == null)
                return;

            UIGuard.Try("Music.OpenRenamePlaylist",
                () => Find.WindowStack.Add(new Dialog_MusicPlaylist(list, done)),
                "The rename window could not be opened.");
        }

        /// <summary>Opens the picker that chooses which playlist something goes into.</summary>
        internal static void OpenPick(Action<MusicPlaylist> chosen)
        {
            UIGuard.Try("Music.OpenPickPlaylist",
                () => Find.WindowStack.Add(new Dialog_MusicPlaylistPick(chosen)),
                "The playlist picker could not be opened.");
        }

        public override Vector2 InitialSize => new Vector2(420f, 226f);

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Music.PlaylistWindow", inRect, () => Contents(inRect),
                "This window could not finish drawing. Close it and try again.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            float y = inRect.y;

            y = TabParts.Heading(inRect, y, editing != null ? "Rename playlist" : "New playlist", palette);

            y += 4f;

            name.Draw(new Rect(inRect.x, y, inRect.width - 30f, UITextBoxControl.DefaultHeight), palette);

            y += UITextBoxControl.DefaultHeight + 4f;

            string problem = Problem();

            if (problem != null)
            {
                GUI.color = palette.Danger;
                Text.Font = GameFont.Tiny;

                Widgets.Label(new Rect(inRect.x, y, inRect.width, 18f), problem);

                Text.Font = GameFont.Small;
                GUI.color = palette.TextPrimary;
            }

            y += 20f;

            Icons(new Rect(inRect.x, y, inRect.width, IconSize), palette);

            y += IconSize + Pad;

            float saveWidth = TabParts.ButtonWidth("Save");
            float cancelWidth = TabParts.ButtonWidth("Cancel");

            if (TabParts.Button(new Rect(inRect.xMax - saveWidth, inRect.yMax - RowHeight, saveWidth, RowHeight),
                    "Save", palette, problem == null, true))
            {
                Commit();
            }

            if (TabParts.Button(new Rect(inRect.xMax - saveWidth - cancelWidth - Pad, inRect.yMax - RowHeight,
                    cancelWidth, RowHeight), "Cancel", palette))
            {
                Close();
            }
        }

        private void Icons(Rect rect, UIColorPaletteDef palette)
        {
            int count = MusicGlyphs.PlaylistIconCount;

            if (count == 0)
                return;

            float step = IconSize + TabParts.SegmentGap;

            for (int i = 0; i < count; i++)
            {
                float x = rect.x + i * step;

                if (x + IconSize > rect.xMax)
                    break;

                int chosen = i;

                TabParts.Segment(new Rect(x, rect.y, IconSize, IconSize), MusicGlyphs.PlaylistIcon(i),
                    icon == i, palette, () => icon = chosen, null);
            }
        }

        /// <summary>Why Save is not available, or null when it is.</summary>
        private string Problem()
        {
            string typed = name.Text != null ? name.Text.Trim() : string.Empty;

            if (typed.NullOrEmpty())
                return "Give it a name.";

            if (!MusicStore.NameAvailable(typed, editing))
                return "There is already a playlist called that.";

            return null;
        }

        private void Commit()
        {
            UIGuard.Try("Music.CommitPlaylist", () =>
            {
                string typed = name.Text.Trim();

                MusicPlaylist result = editing;

                if (result == null)
                    result = MusicStore.Create(typed, icon);
                else
                    MusicStore.Rename(result, typed, icon);

                Close();

                if (done != null)
                    done(result);
            }, "The playlist was not saved.");
        }
    }

    /// <summary>
    /// Choosing which playlist something goes into.
    ///
    /// <b>A list rather than a float menu,</b> which is the rule across this mod: a list shows every playlist, how
    /// many songs each holds, and its picture, and it can be searched when there are a dozen. A float menu shows
    /// names alone and only while the mouse stays inside it.
    ///
    /// <b>New playlist is the first row.</b> Somebody adding songs to a playlist that does not exist yet should
    /// not have to close this, make one, and start again.
    /// </summary>
    internal sealed class Dialog_MusicPlaylistPick : Window
    {
        private const float RowHeight = 30f;

        private const float Pad = 10f;

        private readonly UITextBoxControl search = new UITextBoxControl
        {
            Placeholder = "Search", Icon = TexButton.Search, MaxLength = 40
        };

        private readonly Action<MusicPlaylist> chosen;

        private Vector2 scroll;

        internal Dialog_MusicPlaylistPick(Action<MusicPlaylist> chosen)
        {
            this.chosen = chosen;

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
            drawShadow = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(400f, Mathf.Min(480f, UI.screenHeight - 120f)); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Music.PlaylistPicker", inRect, () => Contents(inRect),
                "The playlist picker could not finish drawing.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            float y = inRect.y;

            y = TabParts.Heading(inRect, y, "Add to playlist", palette);

            y += 4f;

            List<MusicPlaylist> lists = MusicStore.Playlists;

            if (lists.Count > 6)
            {
                search.Draw(new Rect(inRect.x, y, inRect.width - 30f, UITextBoxControl.DefaultHeight), palette);
                y += UITextBoxControl.DefaultHeight + 6f;
            }

            if (TabParts.Button(new Rect(inRect.x, y, inRect.width, RowHeight), "New playlist", palette, true,
                    true))
            {
                Close();
                Dialog_MusicPlaylist.OpenNew(chosen);

                return;
            }

            y += RowHeight + Pad;

            Rect area = new Rect(inRect.x, y, inRect.width, Mathf.Max(40f, inRect.yMax - y));

            UIElementPainter.OutlineRounded(area, palette.Border, palette.SurfaceSunken);

            List<MusicPlaylist> shown = new List<MusicPlaylist>();
            string query = search.Text;

            for (int i = 0; i < lists.Count; i++)
            {
                if (query.NullOrEmpty()
                    || lists[i].Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    shown.Add(lists[i]);
                }
            }

            Rect inner = area.ContractedBy(1f);

            if (shown.Count == 0)
            {
                GUI.color = palette.TextDisabled;
                Text.Anchor = TextAnchor.MiddleCenter;

                Widgets.Label(inner, lists.Count == 0 ? "No playlists yet" : "No playlist matches");

                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextPrimary;

                return;
            }

            Rect view = new Rect(0f, 0f, inner.width - 16f, shown.Count * RowHeight);

            Widgets.BeginScrollView(inner, ref scroll, view);

            for (int i = 0; i < shown.Count; i++)
            {
                MusicPlaylist list = shown[i];
                Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight);

                if (Mouse.IsOver(row))
                    Widgets.DrawBoxSolid(row, palette.HoverOverlay);

                Texture2D icon = MusicGlyphs.PlaylistIcon(list.Icon);

                if (icon != null)
                {
                    GUI.color = palette.TextSecondary;
                    GUI.DrawTexture(new Rect(row.x + 8f, row.y + 7f, 16f, 16f), icon);
                    GUI.color = palette.TextPrimary;
                }

                TabParts.RowLabel(new Rect(row.x + 30f, row.y, row.width - 80f, row.height), list.Name,
                    palette.TextPrimary);

                Text.Font = GameFont.Tiny;
                TextAnchor previousAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(row.xMax - 44f, row.y, 40f, row.height), list.TrackIds.Count.ToString());

                Text.Anchor = previousAnchor;
                GUI.color = palette.TextPrimary;
                Text.Font = GameFont.Small;

                if (!Widgets.ButtonInvisible(row))
                    continue;

                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();

                if (chosen != null)
                    chosen(list);

                break;
            }

            Widgets.EndScrollView();
        }
    }
}
