using System.Collections.Generic;
using System.IO;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// Saving a game, with a folder to put it in.
    ///
    /// <b>What vanilla's dialog cannot do.</b> <c>Dialog_SaveFileList_Save</c> is a name box over a flat list
    /// of every save ever made, in one directory, with no way to tell which colony a name belongs to and no
    /// way to put a save anywhere. On a long-running game that list is the problem rather than the interface
    /// to it.
    ///
    /// <b>Folders are real directories.</b> See <see cref="SaveFolders"/>: this is not a scheme of ours laid
    /// over the files, it is the game reading the folders a player has always been able to make and which it
    /// has always ignored.
    ///
    /// <b>The dead-data sweep is deliberately absent.</b> It is the remaining half of the design and it is
    /// not built yet. A checkbox that does nothing is worse than a missing feature, because the first one is
    /// a lie the player only discovers after trusting it.
    /// </summary>
    public class Dialog_SaveGame : Window
    {
        private const float Pad = 16f;
        private const float TitleHeight = 32f;
        private const float FieldHeight = 32f;
        private const float RowHeight = 30f;
        private const float FooterHeight = SavesChrome.FooterHeight;

        /// <summary>Height of a folder heading inside the list.</summary>
        private const float GroupHeight = 20f;

        /// <summary>
        /// The name to save under.
        ///
        /// <c>UITextBoxControl</c> rather than a raw field, which is not a style preference: anything else
        /// lets the movement keys reach the camera while a colony name is being typed. See the control.
        /// </summary>
        private static readonly UITextBoxControl Name = new UITextBoxControl
        {
            Placeholder = "Name this save",
            ShowClearButton = false,
            MaxLength = 64
        };

        /// <summary>Null means the Saves folder itself.</summary>
        private string folder;

        /// <summary>
        /// Whether this save gets compressed.
        ///
        /// Seeded from the remembered setting rather than from a constant, so somebody who compresses
        /// everything is not re-ticking a box every time they save.
        /// </summary>
        private bool compress = UIOverhaulSettingsFile.Current.compressSaves;

        private Vector2 scroll;
        private List<FileInfo> existing = new List<FileInfo>();
        private string problem;

        /// <summary>
        /// The existing save last clicked, which the action row acts on.
        ///
        /// Clicking a row already fills in the name and folder as an overwrite target, so the row that was
        /// clicked is the one being talked about and needs no separate selection gesture.
        /// </summary>
        private FileInfo chosen;

        private readonly SavesChrome.ArmedDelete armedDelete = new SavesChrome.ArmedDelete();

        public Dialog_SaveGame()
        {
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
            draggable = true;

            Name.Text = DefaultName();

            // <b>The folder follows the name the box was given, and this is a data loss fix.</b>
            //
            // DefaultName is the colony's name, which is very often the name of a save that already exists. The
            // folder used to open on the Saves root regardless. So for anyone keeping their saves in a folder, the
            // window opened already describing a MOVE: overwrite Northern Hibum, and take it out of "Aaron and
            // Andrew" on the way. The footer said so and the button still read Overwrite, and pressing it wrote the
            // new file to the root and removed the original from the folder. To somebody looking at that folder,
            // their save had been deleted.
            //
            // Resolving the folder from the existing save makes the opening state an overwrite in place, which is
            // what the pre-filled name means. Moving a save is still possible and still stated in the footer; it
            // just has to be asked for now by choosing a different folder.
            //
            // Only where no such save exists does the last folder apply, since then there is nothing to follow.
            FileInfo existing = SaveFolders.Find(Name.Text);

            folder = existing != null
                ? SaveFolders.FolderOf(existing)
                : SaveFolders.LastFolder.NullOrEmpty()
                    ? null
                    : SaveFolders.LastFolder;
        }

        public override Vector2 InitialSize =>
            new Vector2(Mathf.Min(720f, UI.screenWidth - 80f), Mathf.Min(560f, UI.screenHeight - 80f));

        public override void PostOpen()
        {
            base.PostOpen();

            SavesChrome.CloseSettingsWindow();

            Refresh();
        }

        public override void PostClose()
        {
            base.PostClose();

            // Recorded on the way out rather than at every place the folder changes: there are five of those in
            // this window -- the picker's two options, a new folder, clicking a save to overwrite, and following a
            // rename -- and one of them would eventually be added without a matching line here.
            SaveFolders.LastFolder = folder ?? string.Empty;
        }

        /// <summary>
        /// The name offered on opening, which is vanilla's rule.
        ///
        /// The faction's name when it has one, and otherwise the first unused numbering of its label. Kept
        /// identical to <c>Dialog_SaveFileList_Save</c> so replacing that dialog does not quietly change what
        /// somebody's saves end up called.
        /// </summary>
        private static string DefaultName()
        {
            return UIGuard.Try("Saves.DefaultName", () =>
            {
                Faction player = Faction.OfPlayer;

                if (player == null)
                    return "Colony";

                return player.HasName
                    ? player.Name
                    : SaveGameFilesUtility.UnusedDefaultFileName(player.def.LabelCap);
            }, "Colony", null);
        }

        private void Refresh()
        {
            existing = UIGuard.Try("Saves.ListExisting", SaveFolders.AllSaves, new List<FileInfo>(),
                "Existing saves are not listed.");
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIWindowDrag.TitleBarOnly(this, inRect.y + TitleHeight);

            UIGuardedPanel.Draw("Saves.SaveDialog", inRect, () => Contents(inRect),
                "The save window could not finish drawing. Nothing has been written.");
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

                Rect title = new Rect(inRect.x, inRect.y, inRect.width - SavesModeBar.Width - 44f,
                    TitleHeight);
                Widgets.Label(title, "Saves");

                SavesModeBar.Draw(new Rect(inRect.xMax - SavesModeBar.Width - 30f, inRect.y + 2f,
                    SavesModeBar.Width, 26f), true, this, palette);

                Text.Font = GameFont.Small;

                float y = title.yMax + 6f;

                y = DrawNameAndFolder(new Rect(inRect.x, y, inRect.width, FieldHeight + 20f), palette);

                y = DrawCompression(new Rect(inRect.x, y + 8f, inRect.width, RowHeight), palette);

                float listTop = y + 12f;
                float actionsTop = inRect.yMax - FooterHeight - SavesChrome.ActionRowHeight - 8f;

                DrawExisting(new Rect(inRect.x, listTop, inRect.width,
                    Mathf.Max(0f, actionsTop - listTop - 6f)), palette);

                DrawActions(new Rect(inRect.x, actionsTop, inRect.width, SavesChrome.ActionRowHeight),
                    palette);

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

        /// <summary>
        /// The two things being chosen, side by side, because they are one decision.
        /// </summary>
        private float DrawNameAndFolder(Rect rect, UIColorPaletteDef palette)
        {
            float pickerWidth = Mathf.Min(220f, rect.width * 0.34f);
            float nameWidth = Mathf.Max(120f, rect.width - pickerWidth - 8f);

            SavesChrome.Caption(new Rect(rect.x, rect.y, nameWidth, 16f), "NAME", palette);
            SavesChrome.Caption(new Rect(rect.x + nameWidth + 8f, rect.y, pickerWidth, 16f), "FOLDER", palette);

            float boxY = rect.y + 18f;

            Name.Draw(new Rect(rect.x, boxY, nameWidth, FieldHeight), palette);

            Rect picker = new Rect(rect.x + nameWidth + 8f, boxY, pickerWidth, FieldHeight);

            if (SavesChrome.Picker(picker, folder ?? SaveFolders.RootLabel, palette))
                OpenFolderMenu();

            return boxY + FieldHeight;
        }

        /// <summary>
        /// The compression choice, and what it costs.
        ///
        /// <b>The sentence beside it changes with the box, and says the awkward part.</b> A compressed save is
        /// still a <c>.rws</c> file and RimWorld cannot read it without this mod, so somebody who uninstalls
        /// later finds an empty load list with nothing to connect it to. That belongs in front of the person
        /// ticking the box, not in a tooltip they have no reason to open.
        ///
        /// <b>In this window rather than only in the options,</b> because the moment to think about how a save
        /// is written is while writing one. The setting exists to remember the answer, not to be the place it
        /// is given.
        /// </summary>
        private float DrawCompression(Rect rect, UIColorPaletteDef palette)
        {
            float boxWidth = Mathf.Min(230f, rect.width * 0.45f);

            if (UICheckboxControl.Draw(new Rect(rect.x, rect.y, boxWidth, rect.height), ref compress, palette,
                    "Compress with LZMA", CompressionTip))
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                settings.compressSaves = compress;
                settings.Save();
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = compress ? palette.TextSecondary : palette.TextDisabled;

            Rect note = new Rect(rect.x + boxWidth + 8f, rect.y,
                Mathf.Max(0f, rect.width - boxWidth - 10f), rect.height);

            if (note.width >= 24f)
            {
                Widgets.LabelEllipses(note, compress
                    ? "Much smaller, and only opens while this mod is installed."
                    : "Written as plain XML, exactly as the game writes it.");
            }

            GUI.color = palette.TextPrimary;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            return rect.yMax;
        }

        private const string CompressionTip =
            "Rewrites the save with LZMA once it has been written, which on a large colony is typically "
            + "around fifteen times smaller.\n\n"
            + "The compressed copy is decompressed and checked against the original before it replaces "
            + "anything, so a compression that goes wrong leaves the plain save untouched.\n\n"
            + "A compressed save can only be opened while this mod is installed.";

        /// <summary>
        /// Choosing a folder, and making one.
        ///
        /// A float menu rather than a control of our own, because this is exactly the shape a float menu is
        /// good at and the list is short. If somebody ends up with thirty folders that judgement changes.
        /// </summary>
        private void OpenFolderMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(SaveFolders.RootLabel,
                    UIGuard.Wrap("Saves.PickRoot", () => folder = null))
            };

            foreach (string name in SaveFolders.Names())
            {
                string captured = name;

                options.Add(new FloatMenuOption(name,
                    UIGuard.Wrap("Saves.PickFolder", () => folder = captured)));
            }

            options.Add(new FloatMenuOption("New folder...",
                UIGuard.Wrap("Saves.NewFolder", OpenNewFolder)));

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenNewFolder()
        {
            Find.WindowStack.Add(new Dialog_NewSaveFolder(created =>
            {
                folder = created;
                Refresh();
            }));
        }

        /// <summary>
        /// The saves that already exist, so an overwrite is chosen rather than stumbled into.
        ///
        /// <b>Grouped by folder, with each group's count and total size in its heading.</b> That heading is
        /// the reason to have folders at all: it is where somebody finds out that Autosaves is nine files and
        /// most of their disk. Sorted with the chosen folder first, since that is the one being written to.
        ///
        /// <b>Given every pixel between the fields and the footer.</b> An earlier version fixed this at a
        /// couple of hundred pixels and left the bottom half of the window empty, which is the sort of thing
        /// that makes a window look unfinished no matter how well the rest of it behaves.
        /// </summary>
        private void DrawExisting(Rect rect, UIColorPaletteDef palette)
        {
            long total = 0;

            foreach (FileInfo file in existing)
                total += file.Length;

            SavesChrome.Caption(new Rect(rect.x, rect.y, rect.width, 16f),
                (existing.Count == 1 ? "1 EXISTING SAVE" : existing.Count + " EXISTING SAVES")
                + (existing.Count == 0 ? string.Empty : "   " + SavesChrome.Size(total) + " ON DISK"), palette);

            Rect body = new Rect(rect.x, rect.y + 18f, rect.width, Mathf.Max(0f, rect.height - 18f));

            UIElementPainter.OutlineRounded(body, palette.Border, palette.PanelBackground);

            Rect inner = body.ContractedBy(5f);

            if (existing.Count == 0)
            {
                GUI.color = palette.TextDisabled;
                Widgets.Label(inner.ContractedBy(6f), "No saves yet. This will be the first.");
                GUI.color = palette.TextPrimary;

                return;
            }

            List<string> order = GroupOrder();

            float height = order.Count * (GroupHeight + 2f);

            foreach (FileInfo file in existing)
                height += RowHeight + 3f;

            Rect view = new Rect(0f, 0f, inner.width - 18f, height);

            Widgets.BeginScrollView(inner, ref scroll, view);

            try
            {
                float y = 0f;

                foreach (string group in order)
                {
                    y = DrawGroupHeading(new Rect(0f, y, view.width, GroupHeight), group, palette);

                    foreach (FileInfo file in existing)
                    {
                        if (!string.Equals(SaveFolders.FolderOf(file) ?? string.Empty, group,
                                System.StringComparison.OrdinalIgnoreCase))
                            continue;

                        DrawExistingRow(new Rect(0f, y, view.width, RowHeight), file, palette);

                        y += RowHeight + 3f;
                    }
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        /// <summary>
        /// The folders that have saves in them, the one being written to first.
        ///
        /// Empty string is the Saves root. Putting the chosen folder at the top means the saves an overwrite
        /// could collide with are the ones already on screen.
        /// </summary>
        private List<string> GroupOrder()
        {
            List<string> order = new List<string>();
            string chosen = folder ?? string.Empty;

            foreach (FileInfo file in existing)
            {
                string where = SaveFolders.FolderOf(file) ?? string.Empty;

                if (!order.Contains(where))
                    order.Add(where);
            }

            order.Sort((a, b) =>
            {
                if (string.Equals(a, chosen, System.StringComparison.OrdinalIgnoreCase))
                    return -1;

                if (string.Equals(b, chosen, System.StringComparison.OrdinalIgnoreCase))
                    return 1;

                return string.Compare(a, b, System.StringComparison.OrdinalIgnoreCase);
            });

            return order;
        }

        private float DrawGroupHeading(Rect rect, string group, UIColorPaletteDef palette)
        {
            int count = 0;
            long bytes = 0;

            foreach (FileInfo file in existing)
            {
                if (!string.Equals(SaveFolders.FolderOf(file) ?? string.Empty, group,
                        System.StringComparison.OrdinalIgnoreCase))
                    continue;

                count++;
                bytes += file.Length;
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerLeft;
            GUI.color = palette.TextSecondary;

            Rect name = new Rect(rect.x + 3f, rect.y, Mathf.Max(0f, rect.width * 0.55f), rect.height);

            if (name.width >= 24f)
                Widgets.LabelEllipses(name, (group.NullOrEmpty() ? SaveFolders.RootLabel : group).ToUpper());

            Text.Anchor = TextAnchor.LowerRight;
            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(rect.x, rect.y, rect.width - 4f, rect.height),
                count + (count == 1 ? " save   " : " saves   ") + SavesChrome.Size(bytes));

            GUI.color = palette.TextPrimary;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            return rect.yMax + 2f;
        }

        /// <summary>
        /// One existing save: name, when, and how big.
        ///
        /// <b>The size is here because it is the whole reason somebody opens this window twice.</b> A list of
        /// names and dates cannot tell you that nine autosaves are eating most of a gigabyte, and that is the
        /// fact this feature exists to surface.
        /// </summary>
        private void DrawExistingRow(Rect row, FileInfo file, UIColorPaletteDef palette)
        {
            string saveName = Path.GetFileNameWithoutExtension(file.Name);

            bool targeted = string.Equals(saveName, Name.Text ?? string.Empty,
                System.StringComparison.OrdinalIgnoreCase);

            UIElementPainter.OutlineRounded(row, targeted ? palette.Warning : palette.Border,
                palette.SurfaceRaised);

            if (targeted)
                Widgets.DrawBoxSolid(row, palette.SelectionOverlay);
            else if (Mouse.IsOver(row))
                Widgets.DrawBoxSolid(row, palette.HoverOverlay);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Anchor = TextAnchor.MiddleLeft;

            // The name at full strength, everything about it quieter. A row is scanned for the name.
            GUI.color = targeted ? palette.Warning : palette.TextPrimary;

            Rect name = new Rect(row.x + 10f, row.y, Mathf.Max(0f, row.width * 0.5f), row.height);

            if (name.width >= 24f)
                Widgets.LabelEllipses(name, saveName);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(row.x, row.y, Mathf.Max(0f, row.width - 84f), row.height),
                SavesChrome.Ago(file.LastWriteTime));

            GUI.color = targeted ? palette.Warning : palette.TextSecondary;

            Widgets.Label(new Rect(row.x, row.y, Mathf.Max(0f, row.width - 10f), row.height),
                SavesChrome.Size(file.Length));

            // The exact stamp on the tooltip, since "3 days ago" is the right answer to the question being
            // asked of this column and the wrong one when somebody needs to be certain which file this is.
            if (Mouse.IsOver(row))
                TooltipHandler.TipRegion(row, (TipSignal) file.LastWriteTime.ToString("f"));

            GUI.color = palette.TextPrimary;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (!Widgets.ButtonInvisible(row))
                return;

            // Clicking a save is how an overwrite is chosen: it takes both the name and the folder, so the
            // footer immediately says it will overwrite and the file lands exactly where it already is. It is
            // also what the action row below acts on.
            Name.Text = saveName;
            folder = SaveFolders.FolderOf(file);
            chosen = file;

            // Both belong to whichever save was being looked at a moment ago.
            armedDelete.Disarm();
            problem = null;

            SoundDefOf.Click.PlayOneShotOnCamera();
        }


        /// <summary>
        /// Rename, Move and Delete for the save last clicked in the list.
        ///
        /// <b>The same control the load window draws,</b> from <see cref="SavesChrome"/>. Housekeeping belongs
        /// here as much as there: the moment somebody notices nine autosaves eating a gigabyte is while they
        /// are looking at this list deciding what to overwrite.
        ///
        /// <b>It acts on the clicked row rather than on the name in the box,</b> which is a real distinction.
        /// The name box is where the *new* save is going and is freely edited; deleting whatever happens to
        /// match what has been typed would be a trap.
        /// </summary>
        private void DrawActions(Rect row, UIColorPaletteDef palette)
        {
            if (chosen != null && !File.Exists(chosen.FullName))
                chosen = null;

            if (chosen == null)
            {
                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextDisabled;

                if (row.width >= 24f)
                    Widgets.LabelEllipses(row, "Click a save above to rename, move or delete it.");

                GUI.color = palette.TextPrimary;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;

                return;
            }

            string path = chosen.FullName;
            string name = Path.GetFileNameWithoutExtension(chosen.Name);
            FileInfo acting = chosen;

            switch (SavesChrome.ActionRow(row, path, name, armedDelete, palette,
                        SaveActions.Blocked(chosen)))
            {
                case SavesChrome.SaveAction.Rename:
                    Find.WindowStack.Add(new Dialog_RenameSave(acting, After));

                    break;

                case SavesChrome.SaveAction.Move:
                    OpenMoveMenu(acting, name);

                    break;

                case SavesChrome.SaveAction.Sweep:
                    Find.WindowStack.Add(new Dialog_SaveSweep(acting));

                    break;

                case SavesChrome.SaveAction.Delete:
                    Remove(acting);

                    break;
            }
        }

        private void OpenMoveMenu(FileInfo file, string name)
        {
            string current = SaveFolders.FolderOf(file);

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            if (current != null)
            {
                options.Add(new FloatMenuOption(SaveFolders.RootLabel,
                    UIGuard.Wrap("Saves.MoveToRoot", () => MoveTo(file, null, name))));
            }

            foreach (string folderName in SaveFolders.Names())
            {
                if (string.Equals(folderName, current, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                string captured = folderName;

                options.Add(new FloatMenuOption(captured,
                    UIGuard.Wrap("Saves.MoveToFolder", () => MoveTo(file, captured, name))));
            }

            if (options.Count == 0)
            {
                problem = "There is nowhere else to move it. Make a folder first.";

                return;
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void MoveTo(FileInfo file, string destination, string name)
        {
            string failure;

            problem = SaveActions.Move(file, destination, out failure) ? null : failure;

            After(name);
        }

        private void Remove(FileInfo file)
        {
            string failure;

            if (SaveActions.Delete(file, out failure))
            {
                problem = null;
                chosen = null;

                Refresh();
                SoundDefOf.Click.PlayOneShotOnCamera();

                return;
            }

            problem = failure;
        }

        /// <summary>Rereads the list and keeps pointing at the same save under whatever name it now has.</summary>
        private void After(string name)
        {
            Refresh();

            chosen = name.NullOrEmpty() ? null : SaveFolders.Find(name);

            // The name box follows a rename, since it was showing the old name as an overwrite target and that
            // name no longer exists.
            if (chosen != null)
            {
                Name.Text = Path.GetFileNameWithoutExtension(chosen.Name);
                folder = SaveFolders.FolderOf(chosen);
            }
        }

        /// <summary>
        /// What is about to happen, and the button that does it.
        ///
        /// <b>The sentence is the point.</b> A save dialog whose button says only "Save" leaves overwriting
        /// somebody's 140 hour colony and creating a new file looking identical. This states which one, and
        /// where it lands, in the place the eye is already going.
        /// </summary>
        private void DrawFooter(Rect rect, UIColorPaletteDef palette)
        {
            string cleaned = GenFile.SanitizedFileName(Name.Text ?? string.Empty).Trim();
            FileInfo clash = cleaned.NullOrEmpty() ? null : SaveFolders.Find(cleaned);

            bool ready = !cleaned.NullOrEmpty();
            string where = folder ?? SaveFolders.RootLabel;

            Rect save = new Rect(rect.xMax - 140f, rect.y + 8f, 140f, 30f);
            Rect cancel = new Rect(save.x - 106f, rect.y + 8f, 100f, 30f);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            string sentence;
            Color tone;

            if (!problem.NullOrEmpty())
            {
                sentence = problem;
                tone = palette.Danger;
            }
            else if (!ready)
            {
                sentence = "Type a name to save.";
                tone = palette.TextDisabled;
            }
            else if (clash == null)
            {
                sentence = "Creates " + cleaned + " in " + where + ".";
                tone = palette.TextSecondary;
            }
            else
            {
                string was = SaveFolders.FolderOf(clash) ?? SaveFolders.RootLabel;

                // Moving is stated separately, because "overwrite" and "overwrite and move it somewhere
                // else" are different enough that somebody would want to know before pressing the button.
                sentence = string.Equals(was, where, System.StringComparison.OrdinalIgnoreCase)
                    ? "Overwrites " + cleaned + " in " + where + "."
                    : "Overwrites " + cleaned + " and moves it from " + was + " to " + where + ".";

                tone = palette.Warning;
            }

            GUI.color = tone;

            // Guarded rather than trusted to fit. LabelEllipses refuses to draw text without 13 pixels to
            // spare and throws out of Substring below that, which on a window this narrow is reachable.
            Rect said = new Rect(rect.x + 2f, rect.y + 11f, Mathf.Max(0f, cancel.x - rect.x - 14f), 30f);

            if (said.width >= 24f)
                Widgets.LabelEllipses(said, sentence);

            GUI.color = palette.TextPrimary;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (SavesChrome.Button(cancel, "Cancel", palette))
                Close();

            if (ready)
            {
                if (SavesChrome.Button(save, clash == null ? "Save" : "Overwrite", palette, true))
                    Commit(cleaned);
            }
            else
            {
                SavesChrome.Disabled(save, "Save", palette);
            }
        }

        private void Commit(string cleaned)
        {
            problem = null;

            bool saved = UIGuard.Try("Saves.Commit", () => SaveWriter.Save(cleaned, folder, compress),
                "The game was not saved.");

            if (!saved)
            {
                problem = "That save could not be written. Nothing on disk has changed.";

                return;
            }

            SoundDefOf.Click.PlayOneShotOnCamera();
            Close();
        }

    }
}
