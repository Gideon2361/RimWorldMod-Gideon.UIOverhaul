using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Inspector;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// The character editor as a dialog, opened on one pawn or on a supplied group of them.
    ///
    /// <b>This is the host for "edit this person".</b> Reached from a pawn's bio panel or from a corpse, so the
    /// subject is already decided and there is no roster column -- a list of eleven other colonists in a window
    /// opened on one of them is an invitation to edit the wrong one. The tab is the host for the other case.
    ///
    /// <b>And it is the host for the starting characters,</b> added 2026-08-23, where there is no tab because
    /// there is no game yet: the button lives on RimWorld's own page and hands over the whole starting party. That
    /// one does get a roster column, because the party is the subject rather than any one of them, and the column
    /// reads from the page rather than from the colony -- see <see cref="OpenGroup"/>.
    ///
    /// Everything the window does lives in <see cref="CharacterEditorPanel"/>.
    /// </summary>
    internal sealed class Dialog_CharacterEditor : Window
    {
        private readonly CharacterEditorPanel panel;

        private Dialog_CharacterEditor(Pawn pawn)
            : this(pawn, null)
        {
        }

        private Dialog_CharacterEditor(Pawn pawn, Func<List<Pawn>> source)
        {
            panel = new CharacterEditorPanel(pawn, source != null, source);

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

        /// <summary>
        /// Opens the editor on a group the caller owns, with a roster column that reads from that group.
        ///
        /// <b>The group is a function, not a list.</b> The one caller is the starting characters page, whose
        /// Randomize button replaces a pawn with a newly generated one; a list captured at open time would leave
        /// the column and the panels holding an object the game has already thrown away.
        ///
        /// <paramref name="first"/> is who to start on, and it is allowed to be null or stale -- the panel checks
        /// the group each frame and moves to whoever is first when its subject is not in it.
        /// </summary>
        internal static void OpenGroup(Func<List<Pawn>> source, Pawn first)
        {
            if (!EditorGate.Enabled || source == null)
                return;

            Window existing = Find.WindowStack.WindowOfType<Dialog_CharacterEditor>();

            if (existing != null)
                existing.Close(false);

            Find.WindowStack.Add(new Dialog_CharacterEditor(first, source));
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
