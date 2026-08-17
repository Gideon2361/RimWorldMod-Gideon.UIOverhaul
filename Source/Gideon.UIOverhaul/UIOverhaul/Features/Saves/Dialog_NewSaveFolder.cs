using System;
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
    /// Naming a new save folder.
    ///
    /// A window of ours rather than <c>Dialog_Rename</c> or a text entry box, for the reason every text
    /// input in this mod is: <see cref="UITextBoxControl"/> is what keeps the movement keys off the camera
    /// while somebody types, and vanilla's entry dialogs do not.
    ///
    /// <b>The failure is shown here rather than as a message toast.</b> A name that collides or that the
    /// drive will not accept is something to correct in the box that is still open, not something to read
    /// after the window has closed.
    /// </summary>
    public class Dialog_NewSaveFolder : Window
    {
        /// <summary>The title row, which is also the only part of this window that drags it.</summary>
        private const float TitleHeight = 30f;

        private static readonly UITextBoxControl Name = new UITextBoxControl
        {
            Placeholder = "Milestones",
            MaxLength = 48
        };

        private readonly Action<string> onCreated;
        private string problem;

        public Dialog_NewSaveFolder(Action<string> created)
        {
            onCreated = created;

            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
            draggable = true;

            Name.Text = string.Empty;
        }

        public override Vector2 InitialSize => new Vector2(420f, 172f);

        public override void DoWindowContents(Rect inRect)
        {
            UIWindowDrag.TitleBarOnly(this, inRect.y + TitleHeight);

            UIGuardedPanel.Draw("Saves.NewFolder", inRect, () => Contents(inRect),
                "The folder window shows a failure notice. Close it and make the folder in Explorer.");
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

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, TitleHeight), "New folder");

                Text.Font = GameFont.Small;

                Name.Draw(new Rect(inRect.x, inRect.y + 36f, inRect.width, 32f), palette);

                Text.Font = GameFont.Tiny;
                GUI.color = problem.NullOrEmpty() ? palette.TextDisabled : palette.Danger;

                Widgets.Label(new Rect(inRect.x, inRect.y + 72f, inRect.width, 34f),
                    problem.NullOrEmpty()
                        ? "Created inside your Saves folder. You can also make one in Explorer."
                        : problem);

                Rect create = new Rect(inRect.xMax - 120f, inRect.yMax - 32f, 120f, 30f);
                Rect cancel = new Rect(create.x - 96f, inRect.yMax - 32f, 90f, 30f);

                if (SavesChrome.Button(cancel, "Cancel", palette))
                    Close();

                if (SavesChrome.Button(create, "Create", palette, true))
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

            if (!SaveFolders.Create(wanted, out failure))
            {
                problem = failure;

                return;
            }

            problem = null;
            SoundDefOf.Click.PlayOneShotOnCamera();

            if (onCreated != null)
                UIGuard.Try("Saves.FolderCreated", () => onCreated(wanted), null);

            Close();
        }

    }
}
