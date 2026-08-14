using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// The single place an exception from our code is caught, described and stopped.
    ///
    /// <b>The rule this exists to enforce:</b> nothing we write may throw into RimWorld. Every method vanilla or
    /// Harmony can call into -- a patch, a window override, a static constructor, a delegate we handed over --
    /// is a boundary, and a boundary that lets an exception out is not our bug alone any more. It becomes a
    /// failed def load, an aborted save, a window stack left mid-update, or a per-frame error that fills the log.
    ///
    /// <b>Why a helper rather than a try/catch per site.</b> Two things are hard to get right site by site, and
    /// both matter more than the catch itself:
    ///
    /// <i>Flood control.</i> Almost every boundary here is called every frame. A plain <c>Log.Error</c> on a
    /// draw path writes tens of thousands of identical lines, and RimWorld stops logging entirely after a
    /// thousand messages -- so the naive catch destroys the diagnostics it was added to produce. Vanilla's
    /// <c>Log.ErrorOnce</c> solves the flood by never reporting again, which loses the other half: whether the
    /// fault is still happening, and whether it is still the *same* fault.
    ///
    /// <i>Context.</i> A stack trace alone rarely says which of the four maps was loaded, whether a long event
    /// was running, or which version of the mod produced it. That is what turns a report into something
    /// actionable, and it has to be gathered the same way every time or it is not comparable.
    ///
    /// <b>What a report looks like.</b> The first failure at a site is logged in full. Repeats of the same
    /// failure are counted and re-reported at 10, 100, 1000 and so on, so a persistent fault stays visible while
    /// the log grows by a line or two rather than by a megabyte. A *different* exception at a site that has
    /// already failed is new information and is reported in full immediately.
    ///
    /// <b>This class cannot throw.</b> Everything it does to build a report is itself guarded, down to a bare
    /// last-resort line, because a guard that can fail is worse than no guard: it turns one contained fault into
    /// an escaping one, at the exact moment something is already wrong.
    ///
    /// <b>This class does not draw anything.</b> It catches, it reports, and it remembers which sites have failed.
    /// What a window should put in the space where its content should have been is a UI decision, and it lives in the
    /// UI layer -- see <c>Gideon.UIFramework.Controls.UIGuardedPanel</c>, which is a caller of this class rather than
    /// a part of it.
    ///
    /// <b>One thing is deliberately left unguarded: Scribe calls in <c>ExposeData</c>.</b> Scribe tracks node depth
    /// across an entire save, so swallowing an exception halfway through a write and continuing would keep writing at
    /// the wrong depth -- producing a save file that looks complete and is not. There, letting the exception reach
    /// vanilla's handler is the safe behavior, and guarding it would be the dangerous one. Logic that merely runs
    /// alongside serialization, such as a post-load migration, has no such constraint and is guarded normally.
    /// </summary>
    public static class UIGuard
    {
        /// <summary>
        /// What we know about one boundary that has failed. Absent from <see cref="sites"/> until it does, so a
        /// healthy game carries no bookkeeping at all.
        /// </summary>
        private sealed class Site
        {
            public int Failures;
            public int ReportAt;
            public string Signature;
        }

        private static readonly Dictionary<string, Site> sites = new Dictionary<string, Site>();

        /// <summary>
        /// Guards <see cref="sites"/> only.
        ///
        /// Needed because two of these boundaries are not on the main thread: <c>DeepProfiler.Start</c> and
        /// <c>ModContentPack.AddDef</c> run on whichever thread is loading. The lock is taken only on the failure
        /// path, so the cost of a healthy frame is zero.
        /// </summary>
        private static readonly object bookkeeping = new object();

        private static string cachedVersion;

        /// <summary>
        /// Runs <paramref name="body"/>, containing and reporting any exception.
        ///
        /// Returns whether it completed, for callers that need to know whether to fall back. Callers that do not
        /// can ignore it: the point of the method is that execution continues either way.
        /// </summary>
        public static bool Try(string site, Action body, string consequence = null)
        {
            try
            {
                body();
                return true;
            }
            catch (Exception ex)
            {
                Report(site, ex, consequence);
                return false;
            }
        }

        /// <summary>
        /// Runs <paramref name="body"/> for its value, falling back to <paramref name="fallback"/> if it throws.
        ///
        /// For the places a boundary has to answer with something -- a width, a label, a list -- where there is
        /// no option of simply not running.
        /// </summary>
        public static T Try<T>(string site, Func<T> body, T fallback, string consequence = null)
        {
            try
            {
                return body();
            }
            catch (Exception ex)
            {
                Report(site, ex, consequence);
                return fallback;
            }
        }

        /// <summary>
        /// Runs <paramref name="body"/> and answers as a Harmony prefix that replaces its original: false when we
        /// did the work, true to hand the method back to vanilla because we could not.
        ///
        /// The inversion is worth stating plainly, because getting it backwards would suppress vanilla's drawing
        /// *and* ours: a prefix returns false to skip the original, so success is false and failure is true.
        /// </summary>
        public static bool Replaced(string site, Action body, string consequence = null)
        {
            try
            {
                body();
                return false;
            }
            catch (Exception ex)
            {
                Report(site, ex, consequence);
                return true;
            }
        }

        /// <summary>
        /// Wraps a delegate we are about to hand to vanilla, so it is still guarded whenever vanilla gets round
        /// to calling it.
        ///
        /// This is the case a try/catch at the call site cannot cover. A <c>FloatMenuOption</c>'s action, a
        /// gizmo's action, a callback queued with <c>ExecuteWhenFinished</c> -- all of them run long after the
        /// method that built them returned, on a stack that belongs entirely to vanilla.
        /// </summary>
        public static Action Wrap(string site, Action body, string consequence = null)
        {
            if (body == null)
                return null;

            return () => Try(site, body, consequence);
        }

        /// <summary>
        /// The same for a delegate vanilla calls for an answer: a gizmo's <c>isActive</c>, a menu option's
        /// enabled test. These are called while drawing, on vanilla's stack, so they need the same treatment as
        /// the action forms.
        /// </summary>
        public static Func<T> Wrap<T>(string site, Func<T> body, T fallback, string consequence = null)
        {
            if (body == null)
                return null;

            return () => Try(site, body, fallback, consequence);
        }

        /// <summary>
        /// Runs <paramref name="body"/> unless this site has already failed, in which case it is not attempted
        /// again for the rest of the session. Answers whether it ran and completed.
        ///
        /// <b>Why a draw path wants this rather than plain <see cref="Try"/>.</b> A panel that threw halfway
        /// through has left Unity's clip stack unbalanced -- a BeginGroup or BeginScrollView that never reached its
        /// End -- and that corruption is not confined to the window that caused it; it disturbs whatever draws
        /// next. Retrying every frame repeats that indefinitely, so the site is retired on its first failure.
        ///
        /// The trade is that a fault which would have cleared on its own also retires the site until the game is
        /// restarted. For drawing that is the right way round, because the alternative is doing damage on every
        /// frame in the hope that the next one is better.
        ///
        /// <b>The false return is the caller's to act on.</b> This deliberately draws nothing itself: what belongs
        /// in the empty space is a UI decision, and it is made in the UI layer by
        /// <c>Gideon.UIFramework.Controls.UIGuardedPanel</c>.
        /// </summary>
        public static bool TryOnce(string site, Action body, string consequence = null)
        {
            if (HasFailed(site))
                return false;

            return Try(site, body, consequence);
        }

        /// <summary>
        /// Whether this site has ever failed. For callers that want to stop offering something after it broke
        /// once, rather than retrying it every frame.
        /// </summary>
        public static bool HasFailed(string site)
        {
            lock (bookkeeping)
            {
                return sites.ContainsKey(site);
            }
        }

        /// <summary>
        /// Reports a contained failure.
        ///
        /// Public because several boundaries keep their own fallback state -- the button bar reverts to vanilla's
        /// bar, a widget switches itself off -- and want their own catch block while still reporting through the
        /// one mechanism, so every failure in this mod reads the same way in the log.
        /// </summary>
        /// <param name="consequence">
        /// What the player will notice, in their terms, if there is anything: "the vanilla bar is back",
        /// "this widget is switched off for the session". Optional, and worth supplying wherever the answer is
        /// not "nothing" -- a report that says only what broke leaves the reader to guess whether the thing
        /// they are looking at is a symptom or unrelated.
        /// </param>
        public static void Report(string site, Exception ex, string consequence = null)
        {
            // Nothing below may throw. A guard that fails while reporting a failure converts a contained fault
            // into an escaping one, which is the single outcome this whole class exists to prevent.
            try
            {
                string signature = SignatureOf(ex);
                int failures;
                bool report;
                bool novel;

                lock (bookkeeping)
                {
                    Site record;

                    if (!sites.TryGetValue(site, out record))
                    {
                        record = new Site { ReportAt = 10, Signature = signature };
                        sites[site] = record;
                    }

                    record.Failures++;
                    failures = record.Failures;

                    // A different exception at a site that already failed is not a repeat. Reporting it in full
                    // is the point: the second fault is often the one that explains the first, and treating it as
                    // another tick of the same counter is how it stays invisible.
                    novel = record.Signature != signature;

                    if (novel)
                    {
                        record.Signature = signature;
                        record.ReportAt = failures * 10;
                        report = true;
                    }
                    else if (failures == 1 || failures >= record.ReportAt)
                    {
                        // Decade by decade: 1, 10, 100, 1000. A fault that recurs every frame for an hour costs
                        // six lines, and each one still says how many times it has happened.
                        if (failures >= record.ReportAt)
                            record.ReportAt = failures * 10;

                        report = true;
                    }
                    else
                    {
                        report = false;
                    }
                }

                if (report)
                    Log.Error(Describe(site, ex, failures, novel, consequence));
            }
            catch
            {
                // Deliberately bare. If describing the failure failed, the failure itself is still worth a line,
                // and there is nothing left to fall back to after this.
                try
                {
                    Log.Error(UILogTag.Prefix + "Contained an exception at " + site
                              + ", and then failed to describe it.");
                }
                catch
                {
                    // Even logging is gone. Returning quietly is the only remaining way to keep our promise that
                    // this method does not throw.
                }
            }
        }

        /// <summary>
        /// The report. One line of what and where, the context, then the exception.
        /// </summary>
        private static string Describe(string site, Exception ex, int failures, bool novel, string consequence)
        {
            StringBuilder text = new StringBuilder();

            text.Append(UILogTag.Prefix + "Contained an exception at ").Append(site).Append(".");

            if (novel && failures > 1)
                text.Append(" This is a new fault at a site that has already failed ")
                    .Append(failures - 1).Append(" time(s) for another reason.");
            else if (failures > 1)
                text.Append(" This has now happened ").Append(failures).Append(" times; the next report is at ")
                    .Append(failures * 10).Append(".");

            // Deliberately does not claim the fault is ours. The site is where the exception was caught, and a guard
            // wraps calls into vanilla and into whatever has patched it, so the throw may well have come from another
            // mod's code reached through ours. Asserting otherwise sent readers looking in the wrong place, and it
            // also promised that nothing else was affected, which containment does not establish either.
            text.Append("\nThe site above is where this was caught, which is not necessarily where it came from: the "
                        + "throw may be in this mod's own code, or in vanilla or another mod's code called from "
                        + "inside it. Whatever was in progress at that point did not finish.");

            if (!consequence.NullOrEmpty())
                text.Append("\nEffect: ").Append(consequence);

            text.Append("\nContext: ").Append(Context());
            text.Append("\n").Append(ex);

            return text.ToString();
        }

        /// <summary>
        /// What was going on when it failed.
        ///
        /// Every read here is one that has been worth having in a bug report at least once.
        ///
        /// <b>Gathered in two guarded halves rather than one.</b> Two of our boundaries run on a loading thread,
        /// and <c>Time.frameCount</c> is one of the Unity properties that throws when it is read off the main
        /// thread. Guarding the whole block together would mean those two sites -- the ones whose failures are
        /// hardest to reproduce -- reported no context at all, losing the mod version with it. So what cannot
        /// fail is appended first and committed, and only the frame counter risks being dropped.
        /// </summary>
        private static string Context()
        {
            StringBuilder text = new StringBuilder();

            try
            {
                text.Append("version ").Append(Version());
                text.Append(", ").Append(Current.ProgramState);

                if (LongEventHandler.AnyEventNowOrWaiting)
                    text.Append(", a long event is running");

                Map map = Current.ProgramState == ProgramState.Playing ? Find.CurrentMap : null;

                if (map != null)
                    text.Append(", map ").Append(map.uniqueID)
                        .Append(map.IsPocketMap ? " (pocket)" : string.Empty);
            }
            catch
            {
                // Reached when the game is too early or too broken to answer. Saying so is more useful than
                // omitting it, because "we could not read the game state" is itself a clue.
                text.Append(" (the game state could not be read)");
            }

            try
            {
                text.Append(", frame ").Append(Time.frameCount);
            }
            catch
            {
                // Main-thread only, so its absence says something too: this failed on a loading thread.
                text.Append(", off the main thread");
            }

            return text.ToString();
        }

        /// <summary>
        /// This assembly's version, which is the one thing a bug report cannot be reconstructed without.
        /// </summary>
        private static string Version()
        {
            if (cachedVersion != null)
                return cachedVersion;

            try
            {
                Version version = Assembly.GetExecutingAssembly().GetName().Version;
                cachedVersion = version != null ? version.ToString() : "unknown";
            }
            catch
            {
                cachedVersion = "unknown";
            }

            return cachedVersion;
        }

        /// <summary>
        /// What makes two failures "the same" for flood control: the exception type and where it was thrown.
        ///
        /// The type alone is too coarse -- a mod list will produce NullReferenceExceptions from several unrelated
        /// places -- and the full stack is too fine, since a stack that differs only in a line number is the same
        /// bug and would defeat the counting.
        /// </summary>
        private static string SignatureOf(Exception ex)
        {
            try
            {
                if (ex == null)
                    return "none";

                string trace = ex.StackTrace;

                if (trace == null)
                    return ex.GetType().FullName;

                int end = trace.IndexOf('\n');
                string frame = end < 0 ? trace : trace.Substring(0, end);

                return ex.GetType().FullName + " at " + frame.Trim();
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
