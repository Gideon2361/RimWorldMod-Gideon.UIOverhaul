using Gideon.UIFramework.Defs;

namespace Gideon.UIFramework.Components.Colors
{
    /// <summary>
    /// One named color in a palette's <see cref="UIColorPaletteDef.custom"/> list.
    ///
    /// This is the escape hatch that keeps <see cref="UIColorRole"/> from growing a slot for every
    /// color any mod ever wants. A mod needing its own color adds an entry here, in its own palette
    /// or as a patch to someone else's, and reads it back by name -- no framework change, no recompile.
    ///
    /// Name your entries with a prefix you own, e.g. "MyMod.Highlight". Custom colors share one flat
    /// namespace per palette, and the last entry loaded for a given name wins.
    /// </summary>
    public class UIColorEntry
    {
        /// <summary>Lookup key. Compared case-insensitively.</summary>
        public string name;

        /// <summary>The color, in any form <see cref="UIColorParser"/> accepts.</summary>
        public string value;
    }
}
