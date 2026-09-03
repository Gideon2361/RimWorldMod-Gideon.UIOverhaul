using System.Diagnostics;
using System.Text;
using Gideon.UIFramework.Helpers;
using UnityEngine;

namespace Gideon.UIOverhaul.Features.Mods
{
    /// <summary>
    /// Timing sentinels for the mods screen, silent unless <c>debugLogging</c> is on.
    ///
    /// <b>Written because the screen's own caches could not explain a stall.</b> Switching rail sections
    /// lagged on every switch with the search box empty, and every candidate in our code was already
    /// handled: the list is virtualised to the visible rows, the filter is cached on roster version, scope
    /// and search, and the detail pane caches its requirements and its measured description. Reading the
    /// code further was going to keep producing theories, so this produces figures instead.
    ///
    /// <b>The decisive sentinel is <see cref="Font.textureRebuilt"/>.</b> The bundled faces are dynamic
    /// fonts, so Unity rasterises a glyph the first time it is drawn and rebuilds its atlas texture when it
    /// has to grow. That event fires exactly when it does, which turns "the fonts might be it" into an
    /// observation with a frame number on it. If a switch logs a rebuild, that is the stall; if it does not,
    /// the theory is dead and the phase timings say where to look instead.
    ///
    /// <b>Nothing here runs with logging off.</b> Every entry point returns on <see cref="UIDebug.Enabled"/>
    /// before touching the clock, and the one always-live cost is a static event subscription that checks a
    /// bool. A probe that has to be paid for by everybody is a probe that gets removed before it is useful.
    ///
    /// <b>It does not log every frame.</b> Sixty lines a second is not a diagnostic, it is a way to lose the
    /// one line that mattered. A frame is reported when something asked for it -- a scope change, a font
    /// rebuild -- or when it took longer than <see cref="SlowFrame"/>.
    /// </summary>
    internal static class ModsProbe
    {
        /// <summary>
        /// Milliseconds past which a frame reports itself uninvited.
        ///
        /// Eight, because sixty frames a second is sixteen and a half: a frame over eight has spent more
        /// than half its budget in one screen and is on its way to being visible.
        /// </summary>
        private const double SlowFrame = 8d;

        private static readonly Stopwatch Clock = new Stopwatch();

        private static readonly StringBuilder Line = new StringBuilder();

        private static double lastMark;

        /// <summary>Why this frame is worth reporting, or null when nothing has asked.</summary>
        private static string reason;

        /// <summary>Whether the screen is open, so font rebuilds elsewhere are not attributed to it.</summary>
        private static bool open;

        private static bool subscribed;

        /// <summary>Atlas rebuilds seen this frame, and since the screen opened.</summary>
        private static int rebuiltThisFrame;

        private static int rebuiltTotal;

        // -------------------------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------------------------

        internal static void Opened()
        {
            open = true;
            rebuiltTotal = 0;

            if (!UIDebug.Enabled)
                return;

            Subscribe();

            UIDebug.Warning("Mods probe armed. Phase timings and font atlas rebuilds will be reported for "
                            + "scope changes, atlas rebuilds, and any frame over " + SlowFrame + "ms.");
        }

        internal static void Closed()
        {
            if (open && UIDebug.Enabled)
            {
                UIDebug.Warning("Mods probe closing. Font atlas rebuilt " + rebuiltTotal
                                + (rebuiltTotal == 1 ? " time" : " times") + " while the screen was open.");
            }

            open = false;
        }

        /// <summary>
        /// Subscribed once and never dropped, because the handler is two comparisons and unsubscribing from
        /// a static event is the kind of thing that goes wrong quietly.
        /// </summary>
        private static void Subscribe()
        {
            if (subscribed)
                return;

            subscribed = true;

            UIGuard.Try("Mods.Probe.Subscribe", () => Font.textureRebuilt += Rebuilt,
                "Font atlas rebuilds are not reported this session. The phase timings still are.");
        }

        private static void Rebuilt(Font font)
        {
            if (!open || !UIDebug.Enabled)
                return;

            rebuiltThisFrame++;
            rebuiltTotal++;

            Ask("font atlas rebuilt: " + (font == null ? "?" : font.name));
        }

        // -------------------------------------------------------------------------------------------
        // Per frame
        // -------------------------------------------------------------------------------------------

        internal static void FrameStart()
        {
            if (!UIDebug.Enabled)
                return;

            Line.Length = 0;
            reason = null;
            rebuiltThisFrame = 0;

            Clock.Reset();
            Clock.Start();

            lastMark = 0d;
        }

        /// <summary>Records how long the phase just finished took, and starts the next one.</summary>
        internal static void Mark(string phase)
        {
            if (!UIDebug.Enabled || !Clock.IsRunning)
                return;

            double now = Clock.Elapsed.TotalMilliseconds;

            Line.Append(' ').Append(phase).Append('=').Append((now - lastMark).ToString("0.00"));

            lastMark = now;
        }

        /// <summary>Marks this frame as one worth reporting whatever it cost.</summary>
        internal static void Ask(string why)
        {
            if (!UIDebug.Enabled)
                return;

            reason = reason == null ? why : reason + "; " + why;
        }

        /// <summary>
        /// Closes the frame and reports it, if anything asked or it ran long.
        ///
        /// The counts come last because they are what the timings have to be read against: three
        /// milliseconds of filtering is nothing over eleven hundred rows and a great deal over nine.
        /// </summary>
        internal static void FrameEnd(string scope, int shown, int rows)
        {
            if (!UIDebug.Enabled || !Clock.IsRunning)
                return;

            Clock.Stop();

            double total = Clock.Elapsed.TotalMilliseconds;

            if (reason == null && total < SlowFrame)
                return;

            string message = "Mods frame " + total.ToString("0.00") + "ms  scope=" + (scope ?? "?")
                             + " shown=" + shown + " of " + rows
                             + (rebuiltThisFrame > 0 ? "  atlasRebuilds=" + rebuiltThisFrame : string.Empty)
                             + " |" + Line
                             + (reason == null ? string.Empty : "  <- " + reason);

            // Warning rather than Message: these are the lines somebody is waiting for, and a Message
            // scrolls past in the noise. Same reasoning as UIDebug's own two levels.
            if (reason != null || total >= SlowFrame * 2d)
                UIDebug.Warning(message);
            else
                UIDebug.Log(message);
        }

        // -------------------------------------------------------------------------------------------
        // One-shot findings
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// What the glyph warm actually did, which is the other half of the font question.
        ///
        /// The atlas size before and after says whether the request grew it, and the distinct character
        /// count says whether a thousand mod names really are a few hundred glyphs once deduplicated. Both
        /// are guesses in the note on <c>ModsScreen.Warm</c> until somebody reads them.
        /// </summary>
        internal static void Warmed(Font font, int characters, int size, long micros)
        {
            if (!UIDebug.Enabled)
                return;

            string atlas = "?";

            UIGuard.Try("Mods.Probe.Atlas", () =>
            {
                Texture texture = font == null || font.material == null ? null : font.material.mainTexture;

                if (texture != null)
                    atlas = texture.width + "x" + texture.height;
            }, null);

            UIDebug.Warning("Mods glyph warm: " + characters + " distinct characters at " + size + "px in "
                            + (micros / 1000d).ToString("0.00") + "ms. Atlas now " + atlas + ".");
        }
    }
}
