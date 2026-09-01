using System;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using RimWorld;

namespace Gideon.UIOverhaul.Features.Mods
{
    /// <summary>
    /// The parts of <see cref="Page_ModsConfig"/> that are private, reached without copying any of them.
    ///
    /// <b>Why this page is redrawn rather than replaced, for the same reason as the caravan dialog.</b> The
    /// window is the model. It holds the transaction: the hash of the active list from the moment it opened,
    /// the untouched copy of that list to restore on discard, and the two flags that decide, after the window
    /// has already gone, whether the player's <c>ModsConfig.xml</c> is written, rolled back, or written and
    /// followed by a restart. <c>PreOpen</c> takes the snapshot, <c>OnCloseRequest</c> decides whether escape
    /// needs a confirmation, and <c>PostClose</c> commits. Standing up our own window would mean
    /// reimplementing all of that, and the thing it protects is the player's mod setup.
    ///
    /// So a Harmony prefix takes over <c>DoWindowContents</c> and nothing else. The instance in the stack is
    /// RimWorld's, with its own snapshot and its own commit path; we draw inside it. <b>Not one line of the
    /// transaction is reimplemented here.</b>
    ///
    /// <b>Two fields, both flags, both written exactly where vanilla writes them.</b> <c>saveChanges</c> is set
    /// by vanilla's own save button and <c>discardChanges</c> by its own discard path; ours set the same flags
    /// from the same kind of press and then close, which puts the commit back in <c>PostClose</c> where it
    /// belongs. <c>modListsDirty</c> is vanilla's cache invalidation, set after anything that would change what
    /// its own list would draw, so that a fall back to vanilla mid-session never shows a stale list.
    /// </summary>
    internal static class ModsReflection
    {
        private static bool resolved;

        private static bool usable;

        private static FieldInfo saveChanges;
        private static FieldInfo discardChanges;
        private static FieldInfo modListsDirty;

        /// <summary>
        /// Whether the page can be taken over at all.
        ///
        /// <b>The two commit flags are required; the cache flag is not.</b> Without them a player could arrange
        /// their mods on our screen, press save, and have nothing written, which is worse than the old screen by
        /// any measure. <c>modListsDirty</c> only matters if vanilla draws again in the same session, so a
        /// missing one costs nothing while we are the ones drawing.
        /// </summary>
        internal static bool Available
        {
            get { return Ready(); }
        }

        private static bool Ready()
        {
            if (resolved)
                return usable;

            resolved = true;

            usable = UIGuard.Try("Mods.Resolve", () =>
            {
                Type type = typeof(Page_ModsConfig);

                const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

                saveChanges = type.GetField("saveChanges", Instance);
                discardChanges = type.GetField("discardChanges", Instance);
                modListsDirty = type.GetField("modListsDirty", Instance);

                return saveChanges != null && discardChanges != null;
            }, false, "The mods screen fell back to RimWorld's own.");

            return usable;
        }

        /// <summary>
        /// Marks the page as saving, which makes <c>PostClose</c> write <c>ModsConfig.xml</c> and, if the active
        /// list actually changed while the page was open, restart the game.
        /// </summary>
        internal static void MarkSaving(Page_ModsConfig page)
        {
            if (page == null || !Ready())
                return;

            UIGuard.Try("Mods.MarkSaving", () => saveChanges.SetValue(page, true));
        }

        /// <summary>
        /// Marks the page as discarding, which makes <c>PostClose</c> put every mod back the way it was when the
        /// page opened. This is also what stops vanilla logging that the page closed without being told which.
        /// </summary>
        internal static void MarkDiscarding(Page_ModsConfig page)
        {
            if (page == null || !Ready())
                return;

            UIGuard.Try("Mods.MarkDiscarding", () => discardChanges.SetValue(page, true));
        }

        /// <summary>
        /// Invalidates vanilla's own cached lists. Called after anything that changes what is active or in what
        /// order, so that vanilla's screen is correct if it ever draws again in this session.
        /// </summary>
        internal static void MarkListsDirty(Page_ModsConfig page)
        {
            if (page == null || modListsDirty == null || !Ready())
                return;

            UIGuard.Try("Mods.MarkListsDirty", () => modListsDirty.SetValue(page, true));
        }
    }
}
