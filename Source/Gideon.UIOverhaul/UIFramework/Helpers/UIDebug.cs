using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// The switch every piece of diagnostic instrumentation in the framework reads.
    ///
    /// Diagnostics earn their place -- the search field's focus loss was only ever going to be explained by
    /// instrumenting it, because the cause sits inside Unity's control-id allocation where no amount of reading
    /// the code reveals it. What they must not do is cost anything, or say anything, when nobody asked.
    ///
    /// This lives in the framework rather than in the mod because the instrumentation does. The framework cannot
    /// read <c>Gideon.UIOverhaul</c>'s settings file -- that dependency runs the wrong way -- so the mod pushes
    /// its preference in here instead, and any other consumer of the framework can do the same.
    ///
    /// Off by default, so a consumer that never sets it gets silence.
    /// </summary>
    public static class UIDebug
    {
        /// <summary>
        /// Whether diagnostic logging is wanted. Set by the consuming mod from its own settings.
        ///
        /// Takes effect immediately for logging. Instrumentation that allocates control ids follows
        /// <see cref="InstrumentControlIds"/> instead, which deliberately does not.
        /// </summary>
        public static bool Enabled { get; set; }

        private static bool? latched;

        /// <summary>
        /// Whether instrumentation that has to allocate a control id should do so.
        ///
        /// This is <see cref="Enabled"/> as it stood on the first frame after launch, and it is a separate
        /// property because the obvious implementation is a bug. IMGUI derives a control's id from draw order,
        /// so an id that is allocated only while a setting is on <i>becomes</i> a draw-order change the moment
        /// that setting is toggled -- shifting every id after it and dropping keyboard focus. Instrumentation
        /// built to investigate that exact fault must not be able to cause it.
        ///
        /// Latching keeps the allocation stable for the whole session: turning debug logging on mid-game starts
        /// the logging immediately, but id-allocating probes wait until the next launch. That is the right trade
        /// -- the alternative is a setting that can break focus while you flip it.
        ///
        /// Read during drawing, after settings have loaded, so the latched value is the launch value.
        /// </summary>
        public static bool InstrumentControlIds => (latched ?? (latched = Enabled)).Value;

        /// <summary>A diagnostic message, or nothing at all when debug logging is off.</summary>
        public static void Log(string message)
        {
            if (Enabled)
                Verse.Log.Message(Prefix(message));
        }

        /// <summary>
        /// A diagnostic finding worth standing out in the log.
        ///
        /// Warning rather than Message for things a developer is waiting to see -- a confirmed fault, a verdict
        /// from a probe -- since a Message scrolls past in the noise of a normal startup.
        /// </summary>
        public static void Warning(string message)
        {
            if (Enabled)
                Verse.Log.Warning(Prefix(message));
        }

        /// <summary>
        /// Frame-stamped, because most of what gets instrumented here is about *when* something changed
        /// relative to something else, and two lines from the same frame mean something different from two
        /// lines a frame apart.
        /// </summary>
        private static string Prefix(string message)
        {
            return $"[Gideon.UIFramework] [debug f{Time.frameCount}] {message}";
        }
    }
}
