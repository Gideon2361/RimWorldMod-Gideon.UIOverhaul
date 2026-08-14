using System;

namespace Gideon.UIFramework.Caching
{
    /// <summary>
    /// Thrown by <see cref="UICache{TKey,TValue}.Get"/> when the thing being asked about no longer exists.
    ///
    /// <b>This is a message to the calling code, not a runtime condition to swallow.</b> It means the caller asked
    /// for a value derived from something that has ceased to exist: a pawn who died or was destroyed, a zone that
    /// was deleted, a map that was unloaded. The cache cannot answer, and the honest answer is not a blank or a
    /// zero -- it is that the question was wrong.
    ///
    /// Getting this back should prompt the caller to re-examine where its key came from. A panel iterating a list of
    /// pawns that includes a destroyed one is working from a roster it should have rebuilt; a row holding a
    /// reference across frames is holding one it should have dropped. Catching this and drawing a blank hides that,
    /// and the roster stays wrong.
    ///
    /// <b>Handling it.</b> Three reasonable responses, in order of preference:
    ///
    /// <list type="bullet">
    /// <item>Fix the assumption -- rebuild the collection the key came from, or drop the stale reference.</item>
    /// <item>Ask with <see cref="UICache{TKey,TValue}.TryGet"/> where liveness genuinely cannot be guaranteed
    /// ahead of time, which is the non-throwing form of the same question.</item>
    /// <item>Catch it around the smallest possible unit of work -- one row, one card -- so a single dead subject
    /// costs that one row and nothing else.</item>
    /// </list>
    ///
    /// <b>What not to do is let it reach a window's draw method.</b> Panel drawing goes through
    /// <c>UIGuardedPanel</c>, which retires a panel for the rest of the session on its first failure, because a
    /// panel that threw part way through has left Unity's clip stack unbalanced. One dead pawn must not cost the
    /// whole tab, so this has to be handled nearer than that.
    /// </summary>
    public class InvalidCacheRequest : Exception
    {
        public InvalidCacheRequest(string cacheName, string subject, Exception cause)
            : base(BuildMessage(cacheName, subject), cause)
        {
            CacheName = cacheName;
            Subject = subject;
        }

        /// <summary>Which cache was asked. Matches the name used for its guard reports.</summary>
        public string CacheName { get; }

        /// <summary>The key, as text. Best effort: a destroyed object's ToString can itself fail.</summary>
        public string Subject { get; }

        private static string BuildMessage(string cacheName, string subject)
        {
            return "Cache '" + cacheName + "' was asked about '" + subject
                   + "', which no longer exists. The caller is working from a stale reference and should rebuild "
                   + "whatever collection this key came from, or ask with TryGet instead.";
        }
    }
}
