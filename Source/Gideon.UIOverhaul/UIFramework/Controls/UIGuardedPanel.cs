using System;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// A window or panel body that is drawn behind a guard, showing a notice in its place if it ever fails.
    ///
    /// This is the UI half of the arrangement that <see cref="UIGuard"/> is the other half of.
    /// <see cref="UIGuard"/> catches and reports and knows nothing about drawing; this decides what the player
    /// sees once it has. Keeping them apart is the point -- the guard is used by patches, static constructors and
    /// gameplay code that have no rect to draw into and no business referencing <c>Widgets</c>.
    ///
    /// <b>Why a notice rather than nothing.</b> RimWorld does wrap <c>DoWindowContents</c>, so a failure there is
    /// already contained -- but the window is then simply empty, which looks like a different bug entirely, and
    /// looks like one to a player who has no reason to go and read a log nobody has pointed them at. The notice
    /// names the log entry to search for, which is what turns "something in this mod broke" into a report that can
    /// be acted on.
    /// </summary>
    public static class UIGuardedPanel
    {
        /// <summary>
        /// Draws <paramref name="body"/>, or the notice if that site has failed.
        /// </summary>
        /// <param name="site">
        /// The guard site name, which is also printed in the notice. It is the string to search the log for, so it
        /// should be the same one passed to <see cref="UIGuard"/> everywhere else this panel reports.
        /// </param>
        /// <param name="consequence">What the player will notice, for the log entry. See <see cref="UIGuard"/>.</param>
        public static void Draw(string site, Rect inRect, Action body, string consequence = null)
        {
            if (UIGuard.TryOnce(site, body,
                    consequence ?? "This panel is replaced by a failure notice until the game is restarted."))
                return;

            DrawNotice(inRect, site);
        }

        /// <summary>
        /// Says what happened, in the space the content would have filled.
        ///
        /// Guarded in turn, because by definition this runs at a moment when drawing has already proved unreliable.
        /// If even this fails the window is left blank, which is what it would have been anyway.
        /// </summary>
        public static void DrawNotice(Rect inRect, string site)
        {
            try
            {
                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;

                UIColorPaletteDef palette = UIColorPaletteDef.Active;

                // Painted over, so the notice does not read on top of half-drawn content left by the frame that
                // failed. The palette can be null before defs are loaded, hence the plain fallbacks.
                Widgets.DrawBoxSolid(inRect,
                    palette != null ? palette.WindowBackground : new Color(0.13f, 0.13f, 0.13f));

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = palette != null ? palette.TextSecondary : Color.gray;

                Widgets.Label(inRect.ContractedBy(24f),
                    "This part of Gideon's UI Overhaul hit an error and has been switched off for the rest of "
                    + "the session.\n\nThe details are in the log, under " + site + ".");

                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
            catch
            {
                // Nothing further to try, and nothing worth reporting: the failure that brought us here has
                // already been logged by the guard.
            }
        }
    }
}
