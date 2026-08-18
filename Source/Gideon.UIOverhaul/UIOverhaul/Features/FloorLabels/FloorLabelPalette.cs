using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using UnityEngine;

namespace Gideon.UIOverhaul.Features.FloorLabels
{
    /// <summary>
    /// The colors a floor label may be, and the one the highlight uses.
    ///
    /// <b>Six choices rather than a color picker.</b> A picker is a bigger control than this needs and invites
    /// choices that do not work: a label has to stay legible against wood, stone, carpet, soil and snow, and most
    /// of a color wheel fails at least one of those. These are the theme's own accents, which are already chosen
    /// to read against dark surfaces.
    ///
    /// <b>Taken from the active theme rather than hardcoded,</b> so a label recolored under one palette still
    /// belongs to the mod's look after switching to another. What is stored is the color itself, so an existing
    /// label keeps exactly what was picked -- the swatches move, the choices already made do not.
    /// </summary>
    internal static class FloorLabelPalette
    {
        /// <summary>
        /// What an unrecolored label is drawn in: near black, and made transparent by the drawer.
        ///
        /// <b>Dark text inside a white outline, not the other way round.</b> The first version was pale text with
        /// a white outline, which put two light tones together -- so the label had no internal contrast and read
        /// as a bright caption stamped over the room rather than a mark on its floor. Inverting it does both jobs
        /// at once: the dark letters read against their own white halo whatever the floor is, and being dark they
        /// recede instead of competing with the colony.
        ///
        /// Opaque here. The transparency belongs to the drawer, so there is one place that decides how faint a
        /// watermark is rather than every color carrying its own answer.
        /// </summary>
        internal static Color Default => new Color(0.05f, 0.06f, 0.07f, 1f);

        /// <summary>The outline the labels window draws around whichever row is under the cursor.</summary>
        internal static Color Highlight
        {
            get
            {
                UIColorPaletteDef palette = UIColorPaletteDef.Active;
                Color accent = palette == null ? new Color(0.45f, 0.75f, 1f) : palette.Accent;

                return new Color(accent.r, accent.g, accent.b, 0.75f);
            }
        }

        /// <summary>
        /// The swatches offered, default first.
        ///
        /// Built per call rather than cached, because it is read while drawing one small window and a cache would
        /// have to be invalidated when the theme changes -- more machinery than the six allocations are worth.
        /// </summary>
        internal static List<Color> Swatches()
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            List<Color> colors = new List<Color> { Default };

            if (palette == null)
                return colors;

            colors.Add(palette.Accent);
            colors.Add(palette.Warning);
            colors.Add(palette.Success);
            colors.Add(palette.Danger);
            colors.Add(palette.Mood);

            return colors;
        }

        /// <summary>
        /// Whether two label colors are the same choice.
        ///
        /// Compared with a tolerance because a color that has been through a save is not bit-identical to the
        /// palette value it came from: scribe writes it as text and parses it back, and an exact comparison would
        /// leave the swatch that was picked looking unselected.
        /// </summary>
        internal static bool Same(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f
                                                && Mathf.Abs(a.b - b.b) < 0.01f;
        }
    }
}
