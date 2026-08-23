using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// The character editor as a tab, opened on nobody in particular.
    ///
    /// <b>A tab because the editor is a place as well as an action.</b> Asked for 2026-08-23. Reached from a pawn
    /// it edits that pawn; reached from the bar there is no subject yet, so the first thing it does is list the
    /// colony and ask -- and that list is then the fastest way to move between people, which is the whole reason
    /// for the tab rather than eleven trips through the inspect pane.
    ///
    /// <b>The button is suppressed rather than absent when the editor is switched off.</b> A MainButtonDef cannot
    /// be conditionally undefined, so <c>UIButtonBarConfig.Suppressed</c> hides it -- the same mechanism that
    /// hides the two vanilla animal tabs our animals tab replaced. With the setting off there is no button and no
    /// way to reach the window, which is what "absent, not greyed" means here.
    ///
    /// <b>No Done button in this host.</b> A tab closes the way every other tab closes, and a Done that did the
    /// same thing as escape would be a second door with a different name on it.
    /// </summary>
    public class MainTabWindow_CharacterEditor : MainTabWindow
    {
        private CharacterEditorPanel panel;

        /// <summary>Zero, because the panel insets itself. The same arrangement as the other tabs.</summary>
        protected override float Margin
        {
            get { return 0f; }
        }

        public override Vector2 RequestedTabSize
        {
            get
            {
                return UIGuard.Try("Editor.TabSize", () =>
                    new Vector2(Panel().WantedWidth, Mathf.Min(760f, UI.screenHeight * 0.85f)),
                    new Vector2(1100f, 680f), null);
            }
        }

        /// <summary>
        /// The panel, made on first use and kept.
        ///
        /// Kept rather than remade per open, so closing the tab and reopening it does not throw away the change
        /// log for edits already made. It is remade only when the pawn it was about has gone.
        /// </summary>
        private CharacterEditorPanel Panel()
        {
            if (panel == null)
                panel = new CharacterEditorPanel(null, true);

            return panel;
        }

        public override void PreOpen()
        {
            base.PreOpen();

            UIGuard.Try("Editor.TabOpen", () =>
            {
                CharacterEditorPanel current = Panel();

                // Whoever is selected on the map is the likeliest subject, so the tab opens on them rather than
                // on the empty state. Nothing selected leaves the roster to ask.
                if (current.Pawn != null && !current.Pawn.Destroyed)
                    return;

                Pawn selected = Find.Selector == null
                    ? null
                    : Inspector.InspectBodies.PawnOf(Find.Selector.SingleSelectedThing);

                if (selected != null)
                    current.Switch(selected);
            }, null);
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Editor.Tab", inRect, () =>
            {
                Widgets.DrawBoxSolid(inRect, UIColorPaletteDef.Active.WindowBackground);

                Panel().Draw(inRect.ContractedBy(8f));
            }, "The character editor tab could not finish drawing. Any change you had already made is part of "
               + "your colony and was not lost.");
        }

        public override void PostClose()
        {
            base.PostClose();

            if (panel != null)
                panel.Closed();
        }
    }
}
