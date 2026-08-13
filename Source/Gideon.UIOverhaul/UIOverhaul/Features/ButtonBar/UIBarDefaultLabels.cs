using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.ButtonBar
{
    /// <summary>
    /// Clearer names this mod gives a few of the vanilla tabs.
    ///
    /// The counterpart of <see cref="UIBarDefaultIcons"/>, and it works the same way: a fallback resolved at
    /// draw time, consulted only where the player has not set a name of their own, and never written into
    /// their layout. Keeping it out of the file is what lets the list change between versions without needing
    /// a migration, and what keeps an untouched layout file meaning "never customized".
    ///
    /// <b>It overrides the def's own label</b>, unlike the icon fallback, which only fills a gap. That is the
    /// point: the name being replaced is not missing, it is just ambiguous on a bar this mod lets the player
    /// rearrange. A player rename still wins over this, because that is a decision rather than a default.
    ///
    /// Only vanilla defNames are listed. Renaming another mod's tab from here would be this mod deciding what
    /// somebody else's feature is called.
    /// </summary>
    public static class UIBarDefaultLabels
    {
        private static readonly Dictionary<string, string> Labels =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Vanilla calls this one "menu". That was unambiguous on a bar where nothing else could be a
                // menu; on this one the player can make menus of their own, so the bare word describes both
                // the pause screen and the thing they just built. It is the pause menu, so it says so.
                { "Menu", "Pause Menu" }
            };

        /// <summary>The name this mod prefers for a tab, or null where it has no opinion.</summary>
        public static string For(string defName)
        {
            if (defName.NullOrEmpty())
                return null;

            return Labels.TryGetValue(defName, out string label) ? label : null;
        }

        /// <summary>
        /// What a slot is called before any rename: this mod's name for it, then the def's own, then the bare
        /// defName for a tab whose mod is no longer installed.
        ///
        /// The single definition of that order. The bar, the menu popup, the editor's cards and the editor's
        /// name boxes all go through here, so a tab cannot be listed under one name and edited under another.
        /// </summary>
        public static string DefaultNameFor(UIButtonBarEntry entry, MainButtonDef def)
        {
            string ours = For(entry?.tab);
            if (!ours.NullOrEmpty())
                return ours;

            return def != null ? def.LabelCap.ToString() : entry?.tab;
        }

        /// <summary>As <see cref="DefaultNameFor"/>, for a def with no entry yet -- the available list.</summary>
        public static string NameOf(MainButtonDef def)
        {
            if (def == null)
                return "";

            return For(def.defName) ?? def.LabelCap.ToString();
        }
    }
}
