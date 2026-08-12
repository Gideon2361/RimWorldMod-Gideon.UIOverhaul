using System;
using Gideon.UIFramework.Defs;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// The pawns tab's window.
    ///
    /// A window of ours rather than a patch on a vanilla one, because there is no vanilla tab this replaces --
    /// which also means no <c>MainTabWindow_PawnTable</c> to inherit, and no vanilla table to fall back to if
    /// this fails. <see cref="DoWindowContents"/> therefore catches on its own behalf: a thrown exception inside
    /// OnGUI repeats every frame, so an unguarded fault would fill the log and leave an unusable window on
    /// screen with no way to read the error behind it.
    /// </summary>
    public class MainTabWindow_Pawns : MainTabWindow
    {
        private static bool failed;

        public override Vector2 RequestedTabSize => new Vector2(PawnsPanel.WindowWidth, PawnsPanel.WindowHeight);

        /// <summary>Zero, because the panel does its own insetting -- the same arrangement the work tab uses.</summary>
        protected override float Margin => 0f;

        public override void DoWindowContents(Rect inRect)
        {
            if (failed)
            {
                DrawFailureNotice(inRect);
                return;
            }

            try
            {
                Widgets.DrawBoxSolid(inRect, UIColorPaletteDef.Active.WindowBackground);
                PawnsPanel.Draw(inRect);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Gideon.UIOverhaul] The pawns tab failed to draw and has been switched off for "
                              + "this session.\n" + ex, 0x17C0_10D4);
                failed = true;
            }
        }

        /// <summary>
        /// Says what happened, rather than leaving an empty window that looks like a different bug.
        /// </summary>
        private static void DrawFailureNotice(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Widgets.DrawBoxSolid(inRect, palette.WindowBackground);

            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = palette.TextSecondary;

            Widgets.Label(inRect.ContractedBy(24f),
                "The pawns tab hit an error and has been switched off for this session. The details are in the "
                + "log.");

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
        }
    }
}
