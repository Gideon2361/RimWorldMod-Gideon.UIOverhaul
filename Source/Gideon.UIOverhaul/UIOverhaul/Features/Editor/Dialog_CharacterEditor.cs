using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Inspector;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// The character editor as a dialog, opened on one pawn.
    ///
    /// <b>This is the host for "edit this person".</b> Reached from a pawn's bio panel or from a corpse, so the
    /// subject is already decided and there is no roster column -- a list of eleven other colonists in a window
    /// opened on one of them is an invitation to edit the wrong one. The tab is the host for the other case.
    ///
    /// Everything the window does lives in <see cref="CharacterEditorPanel"/>.
    /// </summary>
    internal sealed class Dialog_CharacterEditor : Window
    {
        private readonly CharacterEditorPanel panel;

        private Dialog_CharacterEditor(Pawn pawn)
        {
            panel = new CharacterEditorPanel(pawn, false);

            panel.Closer = () => Close();

            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;
            drawShadow = true;
            resizeable = true;
        }

        /// <summary>
        /// Opens the editor on a pawn, or on the person inside a corpse.
        ///
        /// <b>One entry point, and it is the one that unwraps a corpse.</b> Everything that can reach this window
        /// -- the bio panel, a corpse, the corpses tab -- has a <c>Thing</c> and not necessarily a pawn, and having
        /// each of them remember to unwrap is how one of them ends up not doing it.
        /// </summary>
        internal static void Open(Thing thing)
        {
            if (!EditorGate.Enabled)
                return;

            Pawn pawn = UIGuard.Try("Editor.Open", () => InspectBodies.PawnOf(thing), null, null);

            if (pawn == null)
                return;

            // Reopening would stack two windows with two independent change logs, and reverting in one would not
            // know what the other had done.
            Window existing = Find.WindowStack.WindowOfType<Dialog_CharacterEditor>();

            if (existing != null)
                existing.Close(false);

            Find.WindowStack.Add(new Dialog_CharacterEditor(pawn));
        }

        public override Vector2 InitialSize
        {
            get
            {
                return new Vector2(Mathf.Min(panel.WantedWidth, UI.screenWidth - 40f),
                    Mathf.Min(700f, UI.screenHeight - 40f));
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Editor.Window", inRect, () =>
            {
                if (!panel.Draw(inRect))
                    Close();
            }, "The character editor could not finish drawing. Any change you had already made is part of your "
               + "colony and was not lost.");
        }

        public override void PostClose()
        {
            base.PostClose();

            panel.Closed();
        }
    }
}
