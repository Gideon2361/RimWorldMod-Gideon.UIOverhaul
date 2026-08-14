using Gideon.UIFramework.Defs;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// The bracketed name every line this mod writes to the log begins with.
    ///
    /// <b>One place, because it used to be three.</b> Lines went out under <c>[Gideon]</c>, <c>[Gideon.UIOverhaul]</c>
    /// and <c>[Gideon.UIFramework]</c> depending on which file wrote them, which is an internal distinction dressed up
    /// as information: a player reading their log wants to know which *mod* is talking, and all three are this one.
    /// Now they all read <c>[UI Overhaul]</c>, and the name lives here so it cannot drift apart again.
    ///
    /// <b>Colored, because a log is something people scan rather than read.</b> The name is wrapped in the palette's
    /// accent color, so this mod's lines can be picked out of a wall of startup text at a glance -- and in the same
    /// color the rest of the mod uses to mean "this is us", so the log matches the UI it is reporting on.
    ///
    /// Through vanilla's own <c>Colorize</c>, which is the reason this is known to work rather than assumed to.
    /// <c>Colorize</c> produces a <c>&lt;color&gt;</c> tag and vanilla uses it throughout its own labels, which are
    /// drawn with <c>Text.CurFontStyle</c> -- and that is the same style the dev log window's <c>DevGUI.Label</c>
    /// uses. If the tag did not render there, vanilla's UI would be showing literal markup everywhere. Writing the
    /// tag by hand would have been the same bet with none of the evidence.
    ///
    /// The raw <c>Player.log</c> on disk has no renderer, so the tag appears there as text. That is the accepted
    /// cost, and the reason the brackets and the name are left outside the tag: the line is still readable as
    /// <c>[UI Overhaul]</c> once the markup around the name is ignored.
    ///
    /// <b>Only the name is colored, not the brackets or the message.</b> Coloring a whole line would fight the log's
    /// own use of color -- red for errors, yellow for warnings -- and lose the severity that vanilla is already
    /// communicating.
    /// </summary>
    public static class UILogTag
    {
        /// <summary>
        /// The mod's name as a player would say it, with no markup.
        ///
        /// Deliberately the display name rather than the package id. The id belongs in About.xml and in dependency
        /// declarations; a log line is prose.
        /// </summary>
        public const string Name = "UI Overhaul";

        private static string cached;
        private static Color cachedFrom;

        /// <summary>
        /// The prefix, ending in a space, ready to be concatenated in front of a message.
        ///
        /// <b>Falls back to no color rather than to no tag.</b> A good deal of what this mod logs happens while defs
        /// are still loading, which is before any palette exists to ask -- and a line that cannot be colored is worth
        /// far more than no line. So a missing palette costs the color and nothing else.
        /// </summary>
        public static string Prefix
        {
            get
            {
                UIColorPaletteDef palette = Palette;

                if (palette == null)
                    return "[" + Name + "] ";

                Color accent = palette.Accent;

                // Rebuilt only when the color actually changes, which in practice means once per theme switch. The
                // comparison is against the color rather than against a "have we built it" flag, so switching themes
                // recolors the tag without anything having to remember to invalidate it.
                if (cached == null || accent != cachedFrom)
                {
                    cachedFrom = accent;
                    cached = "[" + Name.Colorize(accent) + "] ";
                }

                return cached;
            }
        }

        /// <summary>
        /// The active palette, or null if there is not one yet.
        ///
        /// Guarded because this is read from logging, and logging is what runs when things are already going wrong.
        /// A tag that threw while reporting a failure would replace a useful report with a confusing one.
        /// </summary>
        private static UIColorPaletteDef Palette
        {
            get
            {
                try
                {
                    return UIColorPaletteDef.Active;
                }
                catch
                {
                    return null;
                }
            }
        }

    }
}
