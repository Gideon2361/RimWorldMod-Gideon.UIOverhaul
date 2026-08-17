using System;
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
    /// Giving a save a new name.
    ///
    /// <b>A window here, unlike the delete confirmation, and the difference is the point.</b> Deleting needed
    /// no window because there was nothing to say: it arms in place and the second click does it. Renaming
    /// needs somewhere to type, and a text field that appears inside a list row is a worse experience than a
    /// small window that opens where you are looking -- particularly since it also has to report why a name was
    /// refused.
    ///
    /// Modelled on <see cref="Dialog_NewSaveFolder"/> deliberately, down to the shape and the failure line, so
    /// the two naming windows in this feature behave identically.
    /// </summary>
    public class Dialog_RenameSave : Window
    {
        /// <summary>The title row, which is also the only part of this window that drags it.</summary>
        private const float TitleHeight = 30f;

        private static readonly UITextBoxControl Name = new UITextBoxControl
        {
            Placeholder = "Riverbend",
            MaxLength = 64
        };

        private readonly FileInfo file;

        /// <summary>Handed the new name, so the window behind can find the save again and keep it selected.</summary>
        private readonly Action<string> onRenamed;

        private string problem;

        public Dialog_RenameSave(FileInfo save, Action<string> renamed)
        {
            file = save;
            onRenamed = renamed;

            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
            draggable = true;

            // Seeded with the current name, since a rename is usually an edit rather than a fresh answer.
            Name.Text = save == null ? string.Empty : Path.GetFileNameWithoutExtension(save.Name);
        }

        public override Vector2 InitialSize => new Vector2(420f, 172f);

        public override void DoWindowContents(Rect inRect)
        {
            UIWindowDrag.TitleBarOnly(this, inRect.y + TitleHeight);

            UIGuardedPanel.Draw("Saves.RenameSave", inRect, () => Contents(inRect),
                "The save was not renamed. Nothing has changed.");
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

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, TitleHeight), "Rename save");

                Text.Font = GameFont.Small;

                Name.Draw(new Rect(inRect.x, inRect.y + 36f, inRect.width, 32f), palette);

                Text.Font = GameFont.Tiny;
                GUI.color = problem.NullOrEmpty() ? palette.TextDisabled : palette.Danger;

                Widgets.Label(new Rect(inRect.x, inRect.y + 72f, inRect.width, 34f),
                    problem.NullOrEmpty()
                        ? "Stays in the same folder. Save names are unique across all of them."
                        : problem);

                Rect rename = new Rect(inRect.xMax - 120f, inRect.yMax - 32f, 120f, 30f);
                Rect cancel = new Rect(rename.x - 96f, inRect.yMax - 32f, 90f, 30f);

                if (SavesChrome.Button(cancel, "Cancel", palette))
                    Close();

                if (SavesChrome.Button(rename, "Rename", palette, true))
                    Commit();
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private void Commit()
        {
            string wanted = GenFile.SanitizedFileName(Name.Text ?? string.Empty).Trim();
            string failure;

            if (!SaveActions.Rename(file, wanted, out failure))
            {
                problem = failure;

                return;
            }

            problem = null;
            SoundDefOf.Click.PlayOneShotOnCamera();

            if (onRenamed != null)
                UIGuard.Try("Saves.Renamed", () => onRenamed(wanted), null);

            Close();
        }
    }
}
