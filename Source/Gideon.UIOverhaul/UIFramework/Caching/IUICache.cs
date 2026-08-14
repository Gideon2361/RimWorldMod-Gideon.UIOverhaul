namespace Gideon.UIFramework.Caching
{
    /// <summary>
    /// What <see cref="UICacheController"/> needs of a cache, without knowing what it holds.
    ///
    /// Exists so the controller can keep one list of every cache in the mod despite each being a different closed
    /// generic type. Everything here is bookkeeping the controller does on the whole set: clearing on a load,
    /// dropping entries for things that no longer exist, and reporting what is registered.
    /// </summary>
    public interface IUICache
    {
        /// <summary>Name this cache reports itself as. For diagnostics only.</summary>
        string Name { get; }

        /// <summary>How long a value is reused before it is rebuilt, in real seconds.</summary>
        float IntervalSeconds { get; }

        /// <summary>How many entries are held right now.</summary>
        int Count { get; }

        /// <summary>Drops everything. For a save load, a def reload, or a settings change.</summary>
        void Clear();

        /// <summary>
        /// Drops entries whose key has gone away: a pawn who died, a zone that was deleted.
        ///
        /// Separate from <see cref="Clear"/> because it is the routine one. A per-pawn cache in a long colony
        /// would otherwise hold a row for every colonist who ever lived.
        /// </summary>
        void Prune();

        /// <summary>
        /// Drops whatever is held for one subject, if this cache is keyed by that kind of thing at all.
        ///
        /// <b>Deliberately typed as object.</b> The controller holds caches of every key type and has no way to know
        /// which of them are keyed by pawns, so the subject arrives untyped and each cache decides whether it is
        /// being spoken to. A cache keyed by something else ignores the call.
        ///
        /// This is the proactive counterpart to <see cref="Prune"/>. Pruning finds dead keys eventually, by asking
        /// every key whether it is still valid; this is told, at the moment it happens, which is what keeps anything
        /// from asking about a destroyed subject in the first place.
        /// </summary>
        void Forget(object subject);
    }
}
