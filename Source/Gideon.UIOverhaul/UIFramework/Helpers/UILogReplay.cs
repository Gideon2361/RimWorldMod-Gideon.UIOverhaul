using System;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// Marks a stretch of work that deliberately re-runs something the game already did, so that anything logged
    /// while it runs is diverted rather than written.
    ///
    /// <b>Why a diagnostic needs this at all.</b> RimWorld's XML deserializer does not throw when a value is
    /// wrong: it writes to <c>Log.Error</c> and carries on. That is the only account of what went wrong, and it
    /// is the only way a tool can find out. But re-parsing the definitions to collect those messages means
    /// raising every one of them a second time, and the second time they are not news -- the player already had
    /// them during the load. Left alone, pressing a "find my broken XML" button would fill the log with hundreds
    /// of red lines, pop the debug window open, and bump the error counter, all reporting faults that were
    /// already reported hours earlier.
    ///
    /// <b>So suppression here is not hiding anything.</b> Nothing that has not already been logged once is lost;
    /// the messages are handed to whoever asked for the re-run, which is a better place for them anyway, since
    /// that caller knows which file and which definition provoked each one and the log does not.
    ///
    /// <b>Per thread, and deliberately narrow.</b> Only the thread doing the replay is diverted, so a background
    /// worker or the main loop reporting a genuine fault at the same moment still reaches the log normally. The
    /// window is meant to be a single parse call wide: open it, run the one thing, close it in a finally.
    /// </summary>
    internal static class UILogReplay
    {
        [ThreadStatic] private static Action<bool, string> sink;

        /// <summary>
        /// Guards against a sink that logs.
        ///
        /// Not a theoretical worry: a sink is collector code like anything else, and if it faults, the natural
        /// way to say so is <c>Log.Error</c> -- which would arrive back through this class and into the sink
        /// that just failed. The flag turns that into one lost line rather than a stack overflow.
        /// </summary>
        [ThreadStatic] private static bool diverting;

        /// <summary>Whether this thread is replaying. Read by anything that would otherwise record log output.</summary>
        internal static bool Active => sink != null;

        /// <summary>
        /// Starts diverting this thread's errors and warnings into <paramref name="into"/>.
        ///
        /// The first argument the sink is given is whether the line was an error rather than a warning.
        /// </summary>
        internal static void Begin(Action<bool, string> into)
        {
            sink = into;
        }

        /// <summary>Stops diverting. Belongs in a finally, or a fault leaves the log silenced for good.</summary>
        internal static void End()
        {
            sink = null;
        }

        /// <summary>
        /// Offers a line to the replay in progress.
        /// </summary>
        /// <returns>True when the line was taken, meaning the caller should not write it.</returns>
        internal static bool Take(bool error, string text)
        {
            Action<bool, string> into = sink;

            if (into == null || diverting)
                return false;

            try
            {
                diverting = true;
                into(error, text);
            }
            catch
            {
                // Bare and silent for the reason above: the only way to report this is the log, and the log is
                // currently pointed at the thing that just threw. The line is lost; nothing else is.
            }
            finally
            {
                diverting = false;
            }

            return true;
        }
    }
}
