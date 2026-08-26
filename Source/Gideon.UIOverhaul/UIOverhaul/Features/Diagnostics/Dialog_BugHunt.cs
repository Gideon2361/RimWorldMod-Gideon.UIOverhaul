using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Diagnostics
{
    /// <summary>
    /// The progress window a bug hunt runs behind: a bar, the file being read, and a way out.
    ///
    /// <b>This window is what drives the scan.</b> The hunt does its work in slices small enough to fit inside
    /// a frame and has no thread of its own, so something on the main loop has to keep asking it for the next
    /// slice. <c>WindowUpdate</c> is that something: it runs once per frame per window, unlike
    /// <c>DoWindowContents</c>, which runs again for every input event in the same frame and would make the
    /// scan's speed depend on how much the mouse was moving.
    ///
    /// <b>Modal, and that is the honest arrangement.</b> The workbench underneath is reading the same document
    /// the scan is walking, and inheritance is resolved for the duration, so letting somebody run an XPath in
    /// the middle would be answering from a state that exists only during the scan. Cancel is always there, and
    /// what has been found up to that point is kept.
    ///
    /// <b>Named per file rather than per definition.</b> A definition goes by in well under a millisecond, so a
    /// label that followed them would be an unreadable blur; a file is a unit somebody recognises and stays up
    /// long enough to read.
    /// </summary>
    public class Dialog_BugHunt : Window
    {
        /// <summary>
        /// How long the scan may run inside one frame.
        ///
        /// The same budget the static constructor pre-warm uses, and chosen the same way: enough that the scan
        /// is not being starved by the frame rate, short enough that the window still draws smoothly and the
        /// cancel button responds the moment it is pressed. A game sitting at the main menu has nothing else to
        /// do with the frame, but taking all of it would freeze the very window meant to show progress.
        /// </summary>
        private const float BudgetMs = 8f;

        private const float LineGap = 4f;

        public Dialog_BugHunt()
        {
            doCloseX = false;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = false;
            draggable = true;
            preventCameraMotion = false;
        }

        public override Vector2 InitialSize => new Vector2(560f, 210f);

        /// <summary>
        /// Advances the scan and closes when there is nothing left to do.
        ///
        /// Closing from here rather than from the draw means the window goes as soon as the last slice finishes,
        /// instead of sitting completed for one more frame.
        /// </summary>
        public override void WindowUpdate()
        {
            base.WindowUpdate();

            UIGuard.Try("Diagnostics.BugHuntPumpWindow", () =>
            {
                if (XmlBugHunt.Pump(BudgetMs))
                    Close(false);
            }, "The bug hunt window stays open. Cancel closes it.");
        }

        /// <summary>Cancelling through Escape as well as the button, since a scan is not something to be trapped in.</summary>
        public override void OnCancelKeyPressed()
        {
            Stop();
        }

        /// <summary>
        /// Stops the scan if this window goes for any reason other than the scan finishing.
        ///
        /// <b>The safety net that matters most in this feature.</b> This window is the only thing pumping the
        /// scan, and a scan in progress has the game's inheritance registry populated from a document the
        /// workbench built. Left that way, the next real definition load would resolve against nodes belonging
        /// to a document that no longer exists. Cancel and the button both come through here already; this
        /// covers everything else that can close a window.
        /// </summary>
        public override void PostClose()
        {
            base.PostClose();

            UIGuard.Try("Diagnostics.BugHuntWindowClosed", XmlBugHunt.Cancel,
                "Restart RimWorld before loading a save, as a precaution.");
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Matches the title rect Contents builds. Set out here rather than in there so a failure inside
            // the guarded draw cannot leave the window stuck to the cursor.
            UIWindowDrag.TitleBarOnly(this, inRect.y + UIFonts.LineHeightOf(GameFont.Small) + 8f);

            UIGuardedPanel.Draw("Diagnostics.BugHuntProgress", inRect, () => Contents(inRect),
                "The bug hunt shows a failure notice. The scan itself is unaffected.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.WordWrap = false;

                float small = UIFonts.LineHeightOf(GameFont.Small);
                float tiny = UIFonts.LineHeightOf(GameFont.Tiny);

                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextPrimary;

                Rect title = new Rect(inRect.x, inRect.y, inRect.width, small + 8f);
                Widgets.Label(title, "Hunting for XML problems");

                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextSecondary;

                Rect scope = new Rect(inRect.x, title.yMax, inRect.width, tiny);
                Widgets.LabelEllipses(scope, "Reading every definition in " + XmlBugHunt.ScopeName
                                             + " the way the game reads them.");

                Rect bar = new Rect(inRect.x, scope.yMax + LineGap * 2f, inRect.width, 22f);
                UIProgressBarControl.DrawWithPercent(bar, XmlBugHunt.Fraction, palette);

                // The file, or what the scan is doing when it is between files. Resolving inheritance is one
                // uninterruptible step in the middle, and a bar that stopped moving with no explanation is
                // exactly what this window exists to avoid.
                string doing = XmlBugHunt.CurrentFile;

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.Accent;

                Rect file = new Rect(inRect.x, bar.yMax + LineGap, inRect.width, tiny + 4f);
                Widgets.LabelEllipses(file, doing.NullOrEmpty() ? "Starting..." : doing);

                GUI.color = palette.TextDisabled;

                Rect counts = new Rect(inRect.x, file.yMax + 2f, inRect.width, tiny + 4f);

                Widgets.Label(counts, XmlBugHunt.Done + " of " + XmlBugHunt.Total + " definitions, "
                                      + XmlBugHunt.Findings.Count + " problems in " + XmlBugHunt.BrokenDefs
                                      + " of them");

                Rect stop = new Rect(inRect.center.x - 60f, inRect.yMax - 30f, 120f, 28f);

                if (Button(stop, "Cancel", palette))
                    Stop();
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private void Stop()
        {
            UIGuard.Try("Diagnostics.BugHuntCancel", XmlBugHunt.Cancel,
                "The bug hunt could not be stopped cleanly. Close the workbench to release it.");

            SoundDefOf.Click.PlayOneShotOnCamera();
            Close(false);
        }

        private static bool Button(Rect rect, string label, UIColorPaletteDef palette)
        {
            return UIActionButtonControl.Draw(rect, label, palette, false, true,
                GameFont.Small);
        }
    }
}
