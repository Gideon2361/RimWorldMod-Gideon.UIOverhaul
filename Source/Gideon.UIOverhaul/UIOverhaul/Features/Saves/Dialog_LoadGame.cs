using System;
using System.Collections.Generic;
using System.IO;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// Browsing and loading saves, organized by folder.
    ///
    /// <b>What vanilla's dialog cannot do.</b> <c>Dialog_SaveFileList_Load</c> is one flat list of every save
    /// ever made, sorted by date, with a name and a timestamp per row. It cannot group, cannot filter, and
    /// cannot see a save in a folder at all.
    ///
    /// <b>The rail is the folders, and they are real directories.</b> See <see cref="SaveFolders"/>.
    ///
    /// <b>Grouping by colony is not here yet, and the reason is worth recording.</b> Nothing in a save's
    /// meta header names the colony: the header carries the game version and the mod list and stops there,
    /// and the faction's name lives deep inside the game node. Reading that means parsing into a 48 MB
    /// document per save, per listing, which is not something to do to open a menu. It needs either a cached
    /// index or a bounded partial read, and both are their own piece of work.
    ///
    /// <b>The health analysis is not here either.</b> It is not built. A tab that opened onto nothing would
    /// be worse than its absence.
    /// </summary>
    public class Dialog_LoadGame : Window
    {
        private const float TitleHeight = 34f;
        private const float RailWidth = 172f;
        private const float ListWidth = 330f;
        private const float Gap = 8f;
        private const float RowHeight = 46f;
        private const float ModRowHeight = 20f;
        private const float FooterHeight = SavesChrome.FooterHeight;

        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search saves",
            Icon = TexButton.Search,
            MaxLength = 40
        };

        /// <summary>Which folder is being shown. Null is everything; empty string is the Saves root itself.</summary>
        private string filter;

        private Vector2 listScroll;
        private List<FileInfo> saves = new List<FileInfo>();
        private FileInfo selected;

        /// <summary>
        /// The selected save's meta header, read once when it is selected.
        ///
        /// Reading a header opens the file, and for a compressed save decompresses it, so it is done for the
        /// one save being looked at rather than for every row. Vanilla's own dialog pushes the same work onto
        /// a background task for exactly that reason; reading only the selection avoids needing one.
        /// </summary>
        private SaveHeader header;

        /// <summary>
        /// How that save's mods compare with the ones running.
        ///
        /// <b>Worked out at selection, not while drawing.</b> This used to be a call to
        /// <c>LoadedModsMatchesActiveModsNoInfo()</c> in the middle of the paint, which reads static fields
        /// describing whichever save was inspected last, and at the main menu, no save at all. The panel
        /// therefore announced a mod mismatch for every save in the list.
        /// </summary>
        private SaveModDiff mods = new SaveModDiff();

        private Vector2 modScroll;

        /// <summary>
        /// How the selected save is stored, in words.
        ///
        /// Sniffed once at selection rather than while drawing: it is four bytes off the front of the file,
        /// which is nothing once per selection and a file opened every frame otherwise.
        /// </summary>
        private string format = string.Empty;

        /// <summary>Which save has its delete armed, if any. See <see cref="SavesChrome.ArmedDelete"/>.</summary>
        private readonly SavesChrome.ArmedDelete armedDelete = new SavesChrome.ArmedDelete();

        /// <summary>
        /// Why the last rename, move or delete did not happen, shown in the footer until something else does.
        ///
        /// In the window rather than as a message toast, for the same reason the folder window does it: a name
        /// that is already taken is something to correct here, not to read after this has closed.
        /// </summary>
        private string problem;

        /// <summary>
        /// The selected save's preview picture, or null when it has none.
        ///
        /// <b>Owned here and destroyed by hand.</b> A Texture2D is unmanaged memory the garbage collector
        /// will not reclaim, so one built per selection and forgotten leaks for as long as somebody browses
        /// the list. Exactly one is alive at a time and it goes when the selection changes or the window
        /// closes.
        /// </summary>
        private Texture2D preview;

        public Dialog_LoadGame()
        {
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
            draggable = true;
            resizeable = true;

            // Null here means every folder at once, which is also what null means in the shared field before
            // anything has been chosen, so this needs no translation the way the save window's does.
            filter = SaveFolders.LastFolder;
        }

        public override Vector2 InitialSize =>
            new Vector2(Mathf.Min(1000f, UI.screenWidth - 80f), Mathf.Min(620f, UI.screenHeight - 80f));

        public override void PostOpen()
        {
            base.PostOpen();

            SavesChrome.CloseSettingsWindow();

            Refresh();
        }

        private void Refresh()
        {
            saves = UIGuard.Try("Saves.LoadList", SaveFolders.AllSaves, new List<FileInfo>(),
                "Saves are not listed.");

            if (selected != null && !File.Exists(selected.FullName))
                Select(null);
        }

        private void Select(FileInfo file)
        {
            selected = file;
            header = null;
            mods = new SaveModDiff();
            modScroll = Vector2.zero;
            format = string.Empty;

            // A complaint about the last save stops applying the moment a different one is being looked at.
            problem = null;
            armedDelete.Disarm();

            ReleasePreview();

            if (file == null)
                return;

            header = SaveHeader.Of(file.FullName);
            mods = SaveModDiff.Compare(header);

            format = UIGuard.Try("Saves.SniffFormat",
                () => SaveArchive.Describe(SaveArchive.DetectFile(file.FullName)), string.Empty, null);

            preview = SaveThumbnails.Load(file.FullName);
        }

        /// <summary>Frees the preview texture. Safe to call when there is none.</summary>
        private void ReleasePreview()
        {
            if (preview == null)
                return;

            UIGuard.Try("Saves.ReleasePreview", () => UnityEngine.Object.Destroy(preview), null);

            preview = null;
        }

        public override void PostClose()
        {
            base.PostClose();

            ReleasePreview();

            SaveFolders.LastFolder = filter;
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIWindowDrag.TitleBarOnly(this, inRect.y + TitleHeight);

            UIGuardedPanel.Draw("Saves.LoadDialog", inRect, () => Contents(inRect),
                "The load window could not finish drawing. Your saves are untouched.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - SavesModeBar.Width - 44f,
                    TitleHeight), "Saves");

                SavesModeBar.Draw(new Rect(inRect.xMax - SavesModeBar.Width - 30f, inRect.y + 2f,
                    SavesModeBar.Width, 26f), false, this, palette);

                Text.Font = GameFont.Small;

                Rect searchBox = new Rect(inRect.x, inRect.y + TitleHeight + 2f,
                    Mathf.Min(280f, inRect.width * 0.32f), 28f);

                Search.Draw(searchBox, palette);

                float top = searchBox.yMax + 8f;
                float height = Mathf.Max(0f, inRect.yMax - top - FooterHeight - 6f);

                Rect rail = new Rect(inRect.x, top, RailWidth, height);
                Rect list = new Rect(rail.xMax + Gap, top, ListWidth, height);
                Rect detail = new Rect(list.xMax + Gap, top, Mathf.Max(0f, inRect.xMax - list.xMax - Gap),
                    height);

                DrawRail(rail, palette);
                DrawList(list, palette);
                DrawDetail(detail, palette);

                DrawFooter(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight),
                    palette);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>The folders, as a filter rail.</summary>
        private void DrawRail(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(6f);
            float y = inner.y;

            y = RailItem(new Rect(inner.x, y, inner.width, 24f), "All saves", saves.Count, filter == null,
                palette, () => filter = null);

            y = RailHeading(new Rect(inner.x, y + 6f, inner.width, 16f), "FOLDERS", palette);

            y = RailItem(new Rect(inner.x, y, inner.width, 24f), SaveFolders.RootLabel, CountIn(string.Empty),
                filter == string.Empty, palette, () => filter = string.Empty);

            foreach (string name in SaveFolders.Names())
            {
                string captured = name;

                y = RailItem(new Rect(inner.x, y, inner.width, 24f), name, CountIn(name), filter == name,
                    palette, () => filter = captured);
            }
        }

        private int CountIn(string folder)
        {
            int count = 0;

            foreach (FileInfo file in saves)
            {
                string where = SaveFolders.FolderOf(file) ?? string.Empty;

                if (string.Equals(where, folder, StringComparison.OrdinalIgnoreCase))
                    count++;
            }

            return count;
        }

        private static float RailHeading(Rect rect, string text, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextDisabled;

            Widgets.Label(rect, text);

            GUI.color = previousColor;
            Text.Font = previousFont;

            return rect.yMax + 2f;
        }

        private static float RailItem(Rect rect, string label, int count, bool on, UIColorPaletteDef palette,
            Action chosen)
        {
            if (on)
                UIElementPainter.FillRounded(rect, palette.AccentMuted);
            else if (Mouse.IsOver(rect))
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            GUI.color = on ? palette.TextPrimary : palette.TextSecondary;

            Rect text = new Rect(rect.x + 8f, rect.y, Mathf.Max(0f, rect.width - 46f), rect.height);

            if (text.width >= 24f)
                Widgets.LabelEllipses(text, label);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = on ? palette.Accent : palette.TextDisabled;

            Widgets.Label(new Rect(rect.x, rect.y, rect.width - 8f, rect.height), count.ToString());

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (Widgets.ButtonInvisible(rect))
            {
                chosen();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            return rect.yMax + 2f;
        }

        /// <summary>The saves matching the rail and the search.</summary>
        private void DrawList(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.PanelBackground);

            Rect inner = rect.ContractedBy(5f);

            List<FileInfo> shown = Matching();

            if (shown.Count == 0)
            {
                GUI.color = palette.TextDisabled;
                Widgets.Label(inner.ContractedBy(6f),
                    saves.Count == 0 ? "No saves yet." : "Nothing matches.");
                GUI.color = palette.TextPrimary;

                return;
            }

            Rect view = new Rect(0f, 0f, inner.width - 18f, shown.Count * (RowHeight + 3f));

            Widgets.BeginScrollView(inner, ref listScroll, view);

            try
            {
                for (int i = 0; i < shown.Count; i++)
                    DrawSaveCard(new Rect(0f, i * (RowHeight + 3f), view.width, RowHeight), shown[i],
                        palette);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private List<FileInfo> Matching()
        {
            List<FileInfo> shown = new List<FileInfo>();
            string needle = (Search.Text ?? string.Empty).Trim().ToLower();

            foreach (FileInfo file in saves)
            {
                if (filter != null)
                {
                    string where = SaveFolders.FolderOf(file) ?? string.Empty;

                    if (!string.Equals(where, filter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                if (needle.Length > 0
                    && Path.GetFileNameWithoutExtension(file.Name).ToLower().IndexOf(needle,
                        StringComparison.Ordinal) < 0)
                    continue;

                shown.Add(file);
            }

            return shown;
        }

        private void DrawSaveCard(Rect card, FileInfo file, UIColorPaletteDef palette)
        {
            bool chosen = selected != null
                          && string.Equals(selected.FullName, file.FullName,
                              StringComparison.OrdinalIgnoreCase);

            UIElementPainter.OutlineRounded(card, chosen ? palette.Accent : palette.Border,
                palette.SurfaceRaised);

            if (chosen)
                Widgets.DrawBoxSolid(card, palette.SelectionOverlay);
            else if (Mouse.IsOver(card))
                Widgets.DrawBoxSolid(card, palette.HoverOverlay);

            string saveName = Path.GetFileNameWithoutExtension(file.Name);
            string where = SaveFolders.FolderOf(file) ?? SaveFolders.RootLabel;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = palette.TextPrimary;

            Rect name = new Rect(card.x + 9f, card.y + 4f, Mathf.Max(0f, card.width - 18f), 20f);

            if (name.width >= 24f)
                Widgets.LabelEllipses(name, saveName);

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(card.x + 9f, card.y + 23f, card.width * 0.5f, 18f), where);

            Text.Anchor = TextAnchor.UpperRight;

            Widgets.Label(new Rect(card.x, card.y + 23f, card.width - 9f, 18f),
                SavesChrome.Ago(file.LastWriteTime) + "   " + SavesChrome.Size(file.Length));

            GUI.color = palette.TextPrimary;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (!Widgets.ButtonInvisible(card))
                return;

            Select(file);
            SoundDefOf.Click.PlayOneShotOnCamera();
        }


        /// <summary>
        /// What is known about the selected save.
        ///
        /// The version and the mod comparison come from vanilla's own <c>SaveFileInfo</c>, so the compatibility
        /// judgement here is the same one the game makes rather than a second opinion that could differ.
        /// </summary>
        private void DrawDetail(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceRaised);

            Rect inner = rect.ContractedBy(10f);

            if (selected == null)
            {
                GUI.color = palette.TextDisabled;
                Widgets.Label(inner, "Select a save to see what is in it.");
                GUI.color = palette.TextPrimary;

                return;
            }

            GameFont previousFont = Text.Font;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Medium;
                GUI.color = palette.TextPrimary;

                float y = inner.y;

                Rect title = new Rect(inner.x, y, inner.width, 28f);

                if (title.width >= 24f)
                    Widgets.LabelEllipses(title, Path.GetFileNameWithoutExtension(selected.Name));

                y = title.yMax + 6f;

                // The picture first, because it answers "which game is this" faster than any of the facts
                // under it. Drawn at the shape it was captured in rather than stretched to the pane.
                if (preview != null)
                {
                    // Capped below the pane width so the mod comparison underneath keeps usable room. A
                    // picture that filled the pane looked better on a save with nothing to report and pushed
                    // the list of missing mods off the bottom on the saves that needed it most.
                    float shot = Mathf.Min(inner.width, 360f);
                    Rect frame = new Rect(inner.x, y, shot,
                        shot * preview.height / Mathf.Max(1, preview.width));

                    GUI.DrawTexture(frame, preview, ScaleMode.ScaleToFit);

                    Color previousEdge = GUI.color;
                    GUI.color = palette.Border;
                    Widgets.DrawBox(frame, 1);
                    GUI.color = previousEdge;

                    y = frame.yMax + 8f;
                }

                // Under the picture and above the facts, because that is the reading order: this is the save
                // (name, picture), here is what you can do to it, and here are the details if you want them.
                y = DrawActions(new Rect(inner.x, y, inner.width, SavesChrome.ActionRowHeight), palette);

                // <b>Deleting happens inside that row and clears the selection, mid-frame.</b> Everything below
                // reads the selected save, so without this the next line dereferences null -- which is exactly
                // what happened the first time somebody pressed the delete button. Returning leaves the pane
                // blank for the rest of this frame and the next one draws the empty state properly.
                if (selected == null)
                    return;

                Text.Font = GameFont.Tiny;
                Text.WordWrap = true;

                y = Line(inner, y, "Folder", SaveFolders.FolderOf(selected) ?? SaveFolders.RootLabel, palette);
                y = Line(inner, y, "Saved", selected.LastWriteTime.ToString("f"), palette);
                y = Line(inner, y, "Size", SavesChrome.Size(selected.Length) + "   " + format, palette);
                y = Line(inner, y, "Game version",
                    header == null || header.GameVersion.NullOrEmpty() ? "Unknown" : header.GameVersion,
                    palette);

                y = DrawModVerdict(inner, y + 8f, palette);

                DrawModList(new Rect(inner.x, y, inner.width, Mathf.Max(0f, inner.yMax - y)), palette);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// Rename, Move and Delete for the selected save.
        ///
        /// The control itself lives in <see cref="SavesChrome"/> so the save window draws the identical thing;
        /// what is here is only what each action means in this window.
        /// </summary>
        private float DrawActions(Rect row, UIColorPaletteDef palette)
        {
            string path = selected.FullName;
            string name = Path.GetFileNameWithoutExtension(selected.Name);
            FileInfo acting = selected;

            switch (SavesChrome.ActionRow(row, path, name, armedDelete, palette,
                        SaveActions.Blocked(selected)))
            {
                case SavesChrome.SaveAction.Rename:
                    Find.WindowStack.Add(new Dialog_RenameSave(acting, Reselect));

                    break;

                case SavesChrome.SaveAction.Move:
                    OpenMoveMenu(acting, name);

                    break;

                case SavesChrome.SaveAction.Sweep:
                    Find.WindowStack.Add(new Dialog_SaveSweep(acting, Refresh));

                    break;

                case SavesChrome.SaveAction.Delete:
                    Remove(acting);

                    break;
            }

            return row.yMax + 6f;
        }

        /// <summary>Where a save can be moved to, which is every folder it is not already in.</summary>
        private void OpenMoveMenu(FileInfo file, string name)
        {
            string current = SaveFolders.FolderOf(file);

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            if (current != null)
            {
                options.Add(new FloatMenuOption(SaveFolders.RootLabel,
                    UIGuard.Wrap("Saves.MoveToRoot", () => MoveTo(file, null, name))));
            }

            foreach (string folder in SaveFolders.Names())
            {
                if (string.Equals(folder, current, StringComparison.OrdinalIgnoreCase))
                    continue;

                string captured = folder;

                options.Add(new FloatMenuOption(captured,
                    UIGuard.Wrap("Saves.MoveToFolder", () => MoveTo(file, captured, name))));
            }

            if (options.Count == 0)
            {
                // Said rather than shown as an empty menu, which reads as a broken control.
                problem = "There is nowhere else to move it. Make a folder from the save window first.";

                return;
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void MoveTo(FileInfo file, string folder, string name)
        {
            string failure;

            problem = SaveActions.Move(file, folder, out failure) ? null : failure;

            Reselect(name);
        }

        private void Remove(FileInfo file)
        {
            string failure;

            if (SaveActions.Delete(file, out failure))
            {
                problem = null;

                Refresh();
                Select(null);

                SoundDefOf.Click.PlayOneShotOnCamera();

                return;
            }

            problem = failure;
        }

        /// <summary>
        /// Rereads the list and keeps the same save selected under whatever name it now has.
        ///
        /// <b>Reselected by name rather than by holding the old <c>FileInfo</c>,</b> which now points at a path
        /// that does not exist. Going through <see cref="Select"/> also rereads the header and reloads the
        /// preview and, the part that matters, releases the texture the old selection owned.
        /// </summary>
        private void Reselect(string name)
        {
            Refresh();

            Select(name.NullOrEmpty() ? null : SaveFolders.Find(name));
        }

        /// <summary>
        /// The one sentence about mods, which is the sentence this window exists to deliver.
        ///
        /// <b>It replaces vanilla's interrupting dialog rather than repeating it.</b> That dialog appears
        /// after the Load button is pressed, when the choice has already been made. This says the same thing
        /// while the save is being chosen, which is when it can still change the answer.
        ///
        /// <b>Counted rather than only flagged,</b> so it stays useful when there is no room for the list
        /// below it: on a short window the numbers alone still say how much has changed.
        /// </summary>
        private float DrawModVerdict(Rect inner, float y, UIColorPaletteDef palette)
        {
            string sentence;
            Color tone;

            if (!mods.Known)
            {
                sentence = "This save's mod list could not be read.";
                tone = palette.TextDisabled;
            }
            else if (mods.Matches)
            {
                sentence = "Same mods as this save, in the same order.";
                tone = palette.Success;
            }
            else if (mods.Differences == 0)
            {
                sentence = "The same mods are loaded, in a different order. Patches apply in load order, so "
                           + "some things may not be where the save left them.";
                tone = palette.Warning;
            }
            else
            {
                sentence = Phrase(mods.Missing.Count, "missing") + Joiner(mods)
                           + Phrase(mods.Added.Count, "added") + " since this save.";

                // Missing is the half that loses things; added mods are usually harmless. Coloring on that
                // rather than on "anything differs" stops the common, safe case reading as an alarm.
                tone = mods.Missing.Count > 0 ? palette.Danger : palette.Warning;
            }

            GUI.color = tone;

            float height = Text.CalcHeight(sentence, inner.width);
            Rect said = new Rect(inner.x, y, inner.width, height);

            Widgets.Label(said, sentence);

            GUI.color = palette.TextPrimary;

            return said.yMax + 6f;
        }

        private static string Phrase(int count, string word)
        {
            if (count == 0)
                return string.Empty;

            return count + (count == 1 ? " mod " : " mods ") + word;
        }

        private static string Joiner(SaveModDiff diff)
        {
            return diff.Missing.Count > 0 && diff.Added.Count > 0 ? ", " : string.Empty;
        }

        /// <summary>
        /// Which mods differ, by name.
        ///
        /// <b>Scrollable, because this list has no natural size.</b> A save from a heavily modded colony can
        /// be missing dozens, and the alternative of showing the first few and a count hides exactly the
        /// entries somebody opened this panel to read.
        ///
        /// <b>Names as the save recorded them for what is missing.</b> A package id is not what anybody
        /// subscribed to, and the header carries the display names for precisely this purpose.
        /// </summary>
        private void DrawModList(Rect rect, UIColorPaletteDef palette)
        {
            if (mods.Differences == 0 || rect.height < 24f)
                return;

            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(4f);

            float height = mods.Differences * (ModRowHeight + 1f);
            Rect view = new Rect(0f, 0f, inner.width - 18f, height);

            Widgets.BeginScrollView(inner, ref modScroll, view);

            try
            {
                float y = 0f;

                foreach (string name in mods.Missing)
                    y = ModRow(new Rect(0f, y, view.width, ModRowHeight), "MISSING", name, palette.Danger,
                        palette);

                foreach (string name in mods.Added)
                    y = ModRow(new Rect(0f, y, view.width, ModRowHeight), "ADDED", name, palette.Accent,
                        palette);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private static float ModRow(Rect row, string tag, string name, Color color,
            UIColorPaletteDef palette)
        {
            if (Mouse.IsOver(row))
                Widgets.DrawBoxSolid(row, palette.HoverOverlay);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;

            float textX = UITagControl.DrawLeading(new Rect(row.x + 3f, row.y, row.width - 6f, row.height),
                tag, color, palette);

            GUI.color = palette.TextSecondary;

            Rect label = new Rect(textX, row.y, Mathf.Max(0f, row.xMax - textX - 4f), row.height);

            if (label.width >= 24f)
                Widgets.LabelEllipses(label, name);

            // The full name on hover, since a mod name is often long and the pane is narrow.
            if (Mouse.IsOver(row))
                TooltipHandler.TipRegion(row, (TipSignal) name);

            GUI.color = palette.TextPrimary;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            return row.yMax + 1f;
        }

        private static float Line(Rect inner, float y, string label, string value, UIColorPaletteDef palette)
        {
            Rect row = new Rect(inner.x, y, inner.width, 18f);

            GUI.color = palette.TextDisabled;
            Widgets.Label(new Rect(row.x, row.y, 96f, row.height), label);

            GUI.color = palette.TextSecondary;

            Rect text = new Rect(row.x + 100f, row.y, Mathf.Max(0f, row.width - 100f), row.height);

            if (text.width >= 24f)
                Widgets.LabelEllipses(text, value ?? "Unknown");

            GUI.color = palette.TextPrimary;

            return row.yMax + 2f;
        }

        private void DrawFooter(Rect rect, UIColorPaletteDef palette)
        {
            SavesChrome.Footer(rect, palette);

            string why;
            bool canLoad = SavesModeBar.CanLoad(out why) && selected != null;

            Rect load = new Rect(rect.xMax - 150f, rect.y + 8f, 150f, 30f);
            Rect cancel = new Rect(load.x - 106f, rect.y + 8f, 100f, 30f);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;

            // A failed rename or delete outranks the ordinary sentence, since it is the only thing here that
            // somebody has to act on.
            GUI.color = problem.NullOrEmpty() ? palette.TextDisabled : palette.Danger;

            Rect said = new Rect(rect.x, rect.y + 8f, Mathf.Max(0f, cancel.x - rect.x - 12f), 30f);

            if (said.width >= 24f)
            {
                Widgets.LabelEllipses(said, problem
                                            ?? (selected == null
                                                ? "Select a save."
                                                : why ?? "Loads "
                                                  + Path.GetFileNameWithoutExtension(selected.Name) + "."));
            }

            GUI.color = palette.TextPrimary;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (SavesChrome.Button(cancel, "Cancel", palette))
                Close();

            if (canLoad)
            {
                if (SavesChrome.Button(load, "Load", palette, true))
                    Commit();
            }
            else
            {
                SavesChrome.Disabled(load, "Load", palette);
            }
        }

        /// <summary>
        /// Hands the save to vanilla's own loader.
        ///
        /// <c>CheckVersionAndLoadGame</c> rather than loading directly, because it is what puts up the version
        /// mismatch confirmation. Skipping it to save a step would quietly remove the one guard the game has
        /// against loading a save from an incompatible build.
        ///
        /// <b>The mod mismatch dialog is suppressed for this call and only this call.</b> Everything it would
        /// have said is already on screen, beside the save, where it could still have changed the decision.
        /// The version confirmation is untouched. In fact it now appears in a case vanilla skips it, since
        /// vanilla returns after raising the mod dialog and never reaches the version check. See
        /// <see cref="Patch_ModMismatchDialog"/>.
        ///
        /// It takes a bare name, which is exactly why <see cref="Patch_FilePathForSavedGame"/> has to resolve
        /// names into folders. Without that, a save listed here would fail to open.
        /// </summary>
        private void Commit()
        {
            string name = Path.GetFileNameWithoutExtension(selected.Name);

            bool started = UIGuard.Try("Saves.Load", () =>
            {
                Patch_ModMismatchDialog.Suppress = true;

                try
                {
                    GameDataSaveLoader.CheckVersionAndLoadGame(name);
                }
                finally
                {
                    // The dialogs are decided synchronously inside that call, so the flag has done its work
                    // by the time it returns and must not outlive it.
                    Patch_ModMismatchDialog.Suppress = false;
                }
            }, "That save could not be loaded. Nothing has changed.");

            if (!started)
                return;

            SoundDefOf.Click.PlayOneShotOnCamera();
            Close();
        }

    }
}
