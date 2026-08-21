using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.FloorLabels
{
    /// <summary>
    /// Which typeface the labels are currently drawn with, and the glyphs it provides.
    ///
    /// <b>Both faces are baked atlases shipped with the mod,</b> a PNG and a metrics table each. RimWorld's own
    /// dynamic font was a third source until 2026-08-21; see <see cref="FloorLabelFace"/> for why it had to go
    /// before these labels could draw under the colony rather than over it.
    ///
    /// <b>Still a facade, because the caller should not care which face is active.</b> Everything that draws a
    /// label asks here and never learns which atlas answered, which is what lets one fall back to the other.
    ///
    /// <b>Changing the face throws away every mesh.</b> A mesh's UVs address one specific atlas, so a mesh built
    /// from Oswald is meaningless against Hammersmith. The setting is watched rather than trusted to notify,
    /// because it can be edited in the config file while the game runs.
    /// </summary>
    internal static class FloorLabelFont
    {
        private static readonly FloorLabelAtlas Oswald = new FloorLabelAtlas("OswaldBold");
        private static readonly FloorLabelAtlas Hammersmith = new FloorLabelAtlas("HammersmithOne");

        private static FloorLabelFace lastFace = (FloorLabelFace) (-1);

        /// <summary>
        /// Raised when the glyphs move, which now means only a face change.
        ///
        /// It also fired when RimWorld's dynamic font repacked its atlas, since every UV taken before a repack is
        /// wrong after it. Baked atlases never move, so that half of the problem left with that source.
        /// </summary>
        internal static event Action Invalidated;

        /// <summary>
        /// The source for the chosen face, falling back to the other atlas when one will not load.
        ///
        /// <b>The fallback used to be the game's own font, and its removal is what needed replacing.</b> That font
        /// was always available, so a corrupt atlas cost the look and not the feature. The other shipped atlas
        /// serves the same purpose: two files, and only both failing takes the labels away.
        ///
        /// <b>When both fail the unavailable source is returned rather than null,</b> so callers keep asking one
        /// object and reading <c>Available</c>, and the atlas has already reported the reason once.
        /// </summary>
        internal static IFloorGlyphSource Source
        {
            get
            {
                FloorLabelFace wanted = UIOverhaulSettingsFile.Current.roomLabelFace;

                if (wanted != lastFace)
                {
                    lastFace = wanted;
                    Raise();
                }

                IFloorGlyphSource chosen = For(wanted);

                if (chosen != null && chosen.Available)
                    return chosen;

                IFloorGlyphSource other = wanted == FloorLabelFace.HammersmithOne ? Oswald : Hammersmith;

                return other.Available ? other : chosen;
            }
        }

        /// <summary>The source for one named face, whether or not it is the chosen one. For the preview.</summary>
        internal static IFloorGlyphSource For(FloorLabelFace face)
        {
            switch (face)
            {
                case FloorLabelFace.HammersmithOne: return Hammersmith;
                default: return Oswald;
            }
        }

        internal static bool Available => Source.Available;

        /// <summary>The unit a mesh is built in: the size the active face's glyphs were measured at.</summary>
        internal static float EmSize => Source.EmSize;

        internal static void Request(string text)
        {
            Source.Request(text);
        }

        internal static bool TryGlyph(char c, out FloorGlyph glyph)
        {
            return Source.TryGlyph(c, out glyph);
        }

        /// <summary>
        /// A material for one label colour, from whichever atlas is answering.
        ///
        /// <b>No render queue is set, deliberately.</b> Forcing one was tried twice on 2026-08-21 and failed both
        /// ways: the labels drew over the colony at the shader's own 3000, and vanished under the floors at 2200.
        /// The queue was never the knob -- the shader was. See <see cref="FloorLabelAtlas.MaterialFor"/>.
        /// </summary>
        internal static Material MaterialFor(Color color)
        {
            return Source.MaterialFor(color);
        }

        internal static void Raise()
        {
            Action invalidated = Invalidated;

            if (invalidated != null)
                invalidated();
        }
    }
}
