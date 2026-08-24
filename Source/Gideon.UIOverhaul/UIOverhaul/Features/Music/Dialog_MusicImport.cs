using System;
using System.Collections.Generic;
using System.IO;
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
    /// Bringing music in off the player's own drive.
    ///
    /// <b>We have to draw the file browser ourselves.</b> RimWorld has no file dialog and Unity's belongs to the
    /// editor, so this is drives across the top, folders on the left and audio files on the right, filtered to the
    /// five formats.
    ///
    /// <b>The destination is stated, not asked.</b> This opens from a playlist, so the title says which one and
    /// there is no target to choose. Opened from anywhere else, files land in the drive library and can be put in
    /// a playlist afterwards.
    ///
    /// <b>Unsupported files are shown greyed with the reason.</b> Hiding them makes a folder full of flac look
    /// empty, which sends somebody looking for a file that is sitting right there.
    ///
    /// <b>Nothing is copied.</b> A playlist entry is the path to the file where it already sits. Copying would put
    /// a second gigabyte of somebody's music library inside RimWorld's config folder, and it would go stale the
    /// first time they retagged anything. The cost is that a moved file has to be found again, which the song list
    /// says plainly when it happens.
    /// </summary>
    internal sealed class Dialog_MusicImport : Window
    {
        private const float TitleHeight = 34f;

        private const float ToolbarHeight = 40f;

        private const float TreeWidth = 214f;

        private const float FooterHeight = 48f;

        private const float RowHeight = 27f;

        private const float TreeRowHeight = 24f;

        private const float ControlHeight = 24f;

        private const float Pad = 8f;

        /// <summary>Indent per level of the folder tree.</summary>
        private const float Indent = 14f;

        private readonly UITextBoxControl search = new UITextBoxControl
        {
            Placeholder = "Search this folder", Icon = TexButton.Search, MaxLength = 40
        };

        private readonly MusicPlaylist target;

        private readonly Action done;

        /// <summary>Folders the player has opened, by full path.</summary>
        private readonly HashSet<string> expanded = new HashSet<string>();

        /// <summary>Files ticked for import, by full path.</summary>
        private readonly HashSet<string> picked = new HashSet<string>();

        /// <summary>
        /// Audio counts per folder, so the tree can say how much is in a branch before it is opened.
        ///
        /// Cached because it is read every frame for every visible row and answered by a directory listing. A
        /// folder whose contents change while this window is open keeps its old count, which is a fair trade for
        /// not listing a hundred folders per frame.
        /// </summary>
        private readonly Dictionary<string, int> counts = new Dictionary<string, int>();

        private readonly List<string> drives = new List<string>();

        private string root = string.Empty;

        private string folder = string.Empty;

        private Vector2 treeScroll;

        private Vector2 fileScroll;

        private bool watch;

        private Dialog_MusicImport(MusicPlaylist target, Action done)
        {
            this.target = target;
            this.done = done;

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;
            drawShadow = true;

            UIGuard.Try("Music.ListDrives", () =>
            {
                string[] logical = Directory.GetLogicalDrives();

                for (int i = 0; i < logical.Length; i++)
                {
                    // Ready only: an empty optical drive or a disconnected network letter throws on the first
                    // listing, and a segment that cannot be opened is worse than one that is not offered.
                    if (MusicFolders.Exists(logical[i]))
                        drives.Add(logical[i]);
                }
            }, "The drive list could not be read.");

            Start();
        }

        internal static void Open(MusicPlaylist target, Action done)
        {
            UIGuard.Try("Music.OpenImport", () => Find.WindowStack.Add(new Dialog_MusicImport(target, done)),
                "The import window could not be opened.");
        }

        /// <summary>
        /// Opens where the player was last time, which is nearly always where they want to be again.
        ///
        /// The last watched folder is the best guess available: it is the only folder this mod knows they chose
        /// deliberately. Failing that, the first drive.
        /// </summary>
        private void Start()
        {
            UIGuard.Try("Music.ImportStart", () =>
            {
                List<MusicFolder> watched = MusicStore.Folders;

                for (int i = watched.Count - 1; i >= 0; i--)
                {
                    if (!watched[i].Path.NullOrEmpty() && MusicFolders.Exists(watched[i].Path))
                    {
                        Reveal(watched[i].Path);

                        return;
                    }
                }

                if (drives.Count > 0)
                {
                    root = drives[0];
                    folder = drives[0];
                    expanded.Add(root);
                }
            }, null);
        }

        /// <summary>Opens a path and every folder above it, so the tree shows where we are.</summary>
        private void Reveal(string path)
        {
            root = UIGuard.Try("Music.ImportRoot", () => Path.GetPathRoot(path), string.Empty, null);
            folder = path;

            string walk = path;

            while (!walk.NullOrEmpty())
            {
                expanded.Add(walk);

                string parent = UIGuard.Try("Music.ImportParent", () =>
                {
                    DirectoryInfo info = Directory.GetParent(walk);

                    return info != null ? info.FullName : null;
                }, null, null);

                if (parent == null || parent == walk)
                    break;

                walk = parent;
            }

            if (!root.NullOrEmpty())
                expanded.Add(root);
        }

        public override Vector2 InitialSize
        {
            get
            {
                return new Vector2(Mathf.Min(864f, UI.screenWidth - 40f),
                    Mathf.Min(520f, UI.screenHeight - 80f));
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Music.ImportWindow", inRect, () => Contents(inRect),
                "The import window could not finish drawing. Nothing has been added.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            float y = inRect.y;

            y = TabParts.Heading(inRect, y, target != null
                ? "Add music to " + target.Name
                : "Add music to your library", palette);

            Toolbar(new Rect(inRect.x, y, inRect.width, ToolbarHeight), palette);

            y += ToolbarHeight;

            float bodyHeight = inRect.yMax - FooterHeight - y - Pad;

            Tree(new Rect(inRect.x, y, TreeWidth, bodyHeight), palette);

            Files(new Rect(inRect.x + TreeWidth + Pad, y, inRect.width - TreeWidth - Pad, bodyHeight), palette);

            Footer(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight), palette);
        }

        private void Toolbar(Rect rect, UIColorPaletteDef palette)
        {
            float y = rect.y + (rect.height - ControlHeight) * 0.5f;
            float x = rect.x;

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(x, y + 5f, 34f, ControlHeight), "Drive");

            Text.Font = GameFont.Small;
            GUI.color = palette.TextPrimary;

            x += 38f;

            for (int i = 0; i < drives.Count; i++)
            {
                string drive = drives[i];
                string label = drive.TrimEnd('\\', '/');

                if (label.NullOrEmpty())
                    label = drive;

                float width = Mathf.Max(34f, TabParts.ButtonWidth(label, 10f));

                TabParts.Segment(new Rect(x, y, width, ControlHeight), label, root == drive, palette, () =>
                {
                    root = drive;
                    folder = drive;
                    expanded.Add(drive);
                    picked.Clear();
                    fileScroll = Vector2.zero;
                });

                x += width + TabParts.SegmentGap;
            }

            float searchWidth = 190f;

            search.Draw(new Rect(rect.xMax - searchWidth, y, searchWidth, ControlHeight), palette);
        }

        // -------------------------------------------------------------------------------------------
        // Folder tree
        // -------------------------------------------------------------------------------------------

        private void Tree(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            Rect inner = rect.ContractedBy(1f);

            List<string> rows = new List<string>();
            List<int> depths = new List<int>();

            Flatten(root, 0, rows, depths);

            Rect view = new Rect(0f, 0f, inner.width - 16f, rows.Count * TreeRowHeight);

            Widgets.BeginScrollView(inner, ref treeScroll, view);

            for (int i = 0; i < rows.Count; i++)
            {
                Rect row = new Rect(0f, i * TreeRowHeight, view.width, TreeRowHeight);

                if (row.yMax < treeScroll.y - TreeRowHeight
                    || row.y > treeScroll.y + inner.height + TreeRowHeight)
                {
                    continue;
                }

                TreeRow(row, rows[i], depths[i], palette);
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Turns the open branches into a flat list of rows.
        ///
        /// Only expanded folders are walked, so a drive with a hundred thousand folders on it costs one listing
        /// per open branch rather than a full recursion.
        /// </summary>
        private void Flatten(string path, int depth, List<string> rows, List<int> depths)
        {
            if (path.NullOrEmpty() || depth > 12)
                return;

            rows.Add(path);
            depths.Add(depth);

            if (!expanded.Contains(path))
                return;

            List<string> children = Children(path);

            for (int i = 0; i < children.Count; i++)
                Flatten(children[i], depth + 1, rows, depths);
        }

        private List<string> Children(string path)
        {
            List<string> result = new List<string>();
            string[] found = MusicFolders.Directories(path);

            for (int i = 0; i < found.Length; i++)
            {
                // Hidden and system folders are skipped: nobody keeps their music in one, and Windows puts
                // several at the root of every drive that cannot be opened at all.
                FileAttributes attributes = MusicFolders.Attributes(found[i]);

                if ((attributes & FileAttributes.Hidden) != 0 || (attributes & FileAttributes.System) != 0)
                    continue;

                result.Add(found[i]);
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);

            return result;
        }

        private void TreeRow(Rect row, string path, int depth, UIColorPaletteDef palette)
        {
            bool here = folder == path;
            bool open = expanded.Contains(path);

            if (here)
                Widgets.DrawBoxSolid(row, palette.SelectionOverlay);
            else if (Mouse.IsOver(row))
                Widgets.DrawBoxSolid(row, palette.HoverOverlay);

            float x = row.x + 6f + depth * Indent;

            Rect caret = new Rect(x, row.y + 4f, 16f, 16f);

            GUI.color = palette.TextDisabled;
            Text.Font = GameFont.Tiny;

            Widgets.Label(caret, open ? "-" : "+");

            Text.Font = GameFont.Small;
            string label = Leaf(path);
            int count = Count(path);
            float countWidth = count > 0 ? 34f : 0f;

            Rect band = new Rect(x + 18f, row.y, Mathf.Max(20f, row.xMax - x - 24f - countWidth), row.height);

            TabParts.RowLabel(band, label, here ? palette.TextPrimary : palette.TextSecondary);

            if (Mouse.IsOver(band))
                TooltipHandler.TipRegion(band, (TipSignal) path);

            if (count > 0)
            {
                Text.Font = GameFont.Tiny;
                TextAnchor previousAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(row.xMax - 38f, row.y, 34f, row.height), count.ToString());

                Text.Anchor = previousAnchor;
                Text.Font = GameFont.Small;
            }

            GUI.color = palette.TextPrimary;

            if (Widgets.ButtonInvisible(caret))
            {
                if (!expanded.Remove(path))
                    expanded.Add(path);

                return;
            }

            if (!Widgets.ButtonInvisible(row))
                return;

            folder = path;
            picked.Clear();
            fileScroll = Vector2.zero;
            watch = MusicStore.Watching(path);

            // A folder clicked shut stays shut; one clicked open opens, which is what a single click on a folder
            // name means everywhere else.
            if (!expanded.Contains(path))
                expanded.Add(path);
        }

        /// <summary>The last part of a path, or the whole thing for a drive root.</summary>
        private static string Leaf(string path)
        {
            return UIGuard.Try("Music.ImportLeaf", () =>
            {
                string trimmed = path.TrimEnd('\\', '/');
                string name = Path.GetFileName(trimmed);

                return name.NullOrEmpty() ? path : name;
            }, path, null);
        }

        /// <summary>How many supported audio files sit directly in a folder.</summary>
        private int Count(string path)
        {
            int cached;

            if (counts.TryGetValue(path, out cached))
                return cached;

            string[] files = MusicFolders.Files(path);
            int found = 0;

            for (int i = 0; i < files.Length; i++)
            {
                if (MusicTrack.Supported(files[i]))
                    found++;
            }

            counts[path] = found;

            return found;
        }

        // -------------------------------------------------------------------------------------------
        // File list
        // -------------------------------------------------------------------------------------------

        private void Files(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(1f);

            List<string> files = Listing();

            if (files.Count == 0)
            {
                GUI.color = palette.TextDisabled;
                Text.Anchor = TextAnchor.MiddleCenter;

                Widgets.Label(inner, folder.NullOrEmpty()
                    ? "Pick a folder on the left"
                    : "No audio files in this folder");

                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextPrimary;

                return;
            }

            Rect view = new Rect(0f, 0f, inner.width - 16f, files.Count * RowHeight);

            Widgets.BeginScrollView(inner, ref fileScroll, view);

            for (int i = 0; i < files.Count; i++)
            {
                Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight);

                if (row.yMax < fileScroll.y - RowHeight || row.y > fileScroll.y + inner.height + RowHeight)
                    continue;

                FileRow(row, files[i], palette);
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Every file in the folder, supported first, then the ones we cannot open.
        ///
        /// Both kinds are listed. The unsupported ones are the whole reason somebody would otherwise think the
        /// folder was empty.
        /// </summary>
        private List<string> Listing()
        {
            return UIGuard.Try("Music.ImportListing", () =>
            {
                List<string> supported = new List<string>();
                List<string> rejected = new List<string>();

                if (folder.NullOrEmpty())
                    return supported;

                string[] files = MusicFolders.Files(folder);
                string query = search.Text;

                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileName(files[i]);

                    if (!query.NullOrEmpty() && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (MusicTrack.Supported(files[i]))
                        supported.Add(files[i]);
                    else if (Audio(files[i]))
                        rejected.Add(files[i]);
                }

                supported.Sort(StringComparer.OrdinalIgnoreCase);
                rejected.Sort(StringComparer.OrdinalIgnoreCase);
                supported.AddRange(rejected);

                return supported;
            }, new List<string>(), "This folder could not be read.");
        }

        /// <summary>
        /// Whether a file is audio we happen not to support, as opposed to something unrelated.
        ///
        /// Without this the list would show every text file and executable in the folder. These are the formats
        /// people actually have music in and would reasonably expect to work.
        /// </summary>
        private static bool Audio(string path)
        {
            string extension = UIGuard.Try("Music.ImportExtension",
                () => Path.GetExtension(path).ToLowerInvariant(), string.Empty, null);

            return extension == ".flac" || extension == ".wma" || extension == ".aac" || extension == ".aiff"
                   || extension == ".alac" || extension == ".opus" || extension == ".mid"
                   || extension == ".midi";
        }

        private void FileRow(Rect row, string path, UIColorPaletteDef palette)
        {
            bool supported = MusicTrack.Supported(path);
            bool already = MusicStore.Imported.Contains("file:" + path);
            bool ticked = picked.Contains(path);

            if (Mouse.IsOver(row) && supported)
                Widgets.DrawBoxSolid(row, palette.HoverOverlay);

            Rect box = new Rect(row.x + 10f, row.y + 6f, 15f, 15f);

            if (supported)
            {
                UICheckboxControl.DrawBox(box, ticked, palette, already);
            }
            else
            {
                GUI.color = palette.TextDisabled;
                UIElementPainter.OutlineRounded(box, palette.TextDisabled, Color.clear);
                GUI.color = palette.TextPrimary;
            }

            string extension = UIGuard.Try("Music.RowExtension",
                () => Path.GetExtension(path).TrimStart('.').ToLowerInvariant(), string.Empty, null);

            GUI.color = supported ? palette.TextPrimary : palette.TextDisabled;

            float nameWidth = row.width - 34f - 60f - 76f;

            if (!supported)
            {
                float pill = TabParts.PillWidth(extension + " is not supported") + 8f;
                nameWidth -= pill;

                TabParts.Pill(row, row.x + 34f + Mathf.Max(40f, nameWidth) + 6f, row.y + 5f,
                    extension + " is not supported", palette.TextDisabled, palette);
            }
            else if (already)
            {
                float pill = TabParts.PillWidth("Already added") + 8f;
                nameWidth -= pill;

                TabParts.Pill(row, row.x + 34f + Mathf.Max(40f, nameWidth) + 6f, row.y + 5f, "Already added",
                    palette.TextSecondary, palette);
            }

            Rect nameBand = new Rect(row.x + 34f, row.y, Mathf.Max(40f, nameWidth), row.height);

            TabParts.RowLabel(nameBand,
                UIGuard.Try("Music.RowName", () => Path.GetFileNameWithoutExtension(path), path, null),
                supported ? palette.TextPrimary : palette.TextDisabled);

            if (Mouse.IsOver(nameBand))
                TooltipHandler.TipRegion(nameBand, (TipSignal) path);

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(row.xMax - 130f, row.y + 6f, 54f, 18f), extension);

            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;

            // Size rather than length. A duration means decoding the file, and doing that for two hundred files
            // to fill a column would freeze the window on every folder change.
            Widgets.Label(new Rect(row.xMax - 76f, row.y, 70f, row.height), Size(path));

            Text.Anchor = previousAnchor;
            GUI.color = palette.TextPrimary;
            Text.Font = GameFont.Small;

            if (!supported || already)
                return;

            if (!Widgets.ButtonInvisible(row))
                return;

            if (!picked.Remove(path))
                picked.Add(path);

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private static string Size(string path)
        {
            return UIGuard.Try("Music.ImportSize", () =>
            {
                long bytes = MusicFolders.Length(path);

                if (bytes < 0L)
                    return "-";

                if (bytes < 1024L)
                    return bytes + " B";

                if (bytes < 1024L * 1024L)
                    return (bytes / 1024f).ToString("0") + " KB";

                if (bytes < 1024L * 1024L * 1024L)
                    return (bytes / (1024f * 1024f)).ToString("0.0") + " MB";

                return (bytes / (1024f * 1024f * 1024f)).ToString("0.00") + " GB";
            }, "-", null);
        }

        // -------------------------------------------------------------------------------------------
        // Footer
        // -------------------------------------------------------------------------------------------

        private void Footer(Rect rect, UIColorPaletteDef palette)
        {
            float y = rect.y + (rect.height - ControlHeight) * 0.5f;

            if (!folder.NullOrEmpty())
            {
                string label = "Keep watching " + folder;
                float width = Mathf.Min(rect.width * 0.5f, UICheckboxControl.WidthFor(label));
                bool value = watch;

                if (UICheckboxControl.Draw(new Rect(rect.x, y, width, ControlHeight), ref value, palette, label,
                        "New files in this folder join by themselves, next time the music window opens."))
                {
                    watch = value;

                    if (watch)
                        MusicStore.AddFolder(folder, target != null ? target.Name : string.Empty);
                    else
                        MusicStore.RemoveFolder(folder);
                }
            }

            float right = rect.xMax;

            string add = picked.Count == 1 ? "Add 1 file" : "Add " + picked.Count + " files";
            float addWidth = TabParts.ButtonWidth(add);

            right -= addWidth;

            if (TabParts.Button(new Rect(right, y, addWidth, ControlHeight), add, palette, picked.Count > 0,
                    true))
            {
                Commit();
            }

            right -= Pad;

            float cancelWidth = TabParts.ButtonWidth("Cancel");
            right -= cancelWidth;

            if (TabParts.Button(new Rect(right, y, cancelWidth, ControlHeight), "Cancel", palette))
                Close();

            right -= Pad + 12f;

            Text.Font = GameFont.Tiny;
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = palette.TextSecondary;

            int available = 0;
            List<string> files = Listing();

            for (int i = 0; i < files.Count; i++)
            {
                if (MusicTrack.Supported(files[i]))
                    available++;
            }

            Widgets.Label(new Rect(right - 140f, rect.y, 140f, rect.height),
                picked.Count + " of " + available + " selected");

            Text.Anchor = previousAnchor;
            GUI.color = palette.TextPrimary;
            Text.Font = GameFont.Small;
        }

        private void Commit()
        {
            UIGuard.Try("Music.Import", () =>
            {
                int added = 0;

                foreach (string path in picked)
                {
                    string id = "file:" + path;

                    MusicStore.NoteImported(id);

                    if (target != null && !target.TrackIds.Contains(id))
                        target.TrackIds.Add(id);

                    added++;
                }

                MusicStore.Save();
                MusicLibrary.Invalidate();
                MusicEngine.Invalidate();

                Close();

                if (done != null)
                    done();

                Messages.Message(added == 1
                        ? "One file added" + (target != null ? " to " + target.Name : "") + "."
                        : added + " files added" + (target != null ? " to " + target.Name : "") + ".",
                    MessageTypeDefOf.TaskCompletion, false);
            }, "Those files were not added.");
        }
    }
}
