using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// Any colour at all, through RimWorld's own colour picker.
    ///
    /// <b>A subclass of the game's picker rather than a wheel of ours,</b> asked for on 2026-08-23 as "like with
    /// the ideology styling station". <c>Dialog_ColorPickerBase</c> is the window behind the styling station's
    /// colour choice and the glower's: an HSV wheel, a value slider, red-green-blue and hue-saturation-value
    /// fields, a palette of suggestions, and Accept and Cancel. All of it is already written, already translated,
    /// and already the thing the player has used before. Five abstract members and a saved colour is the whole of
    /// this file.
    ///
    /// <b>The value channel is not forced,</b> which is what <c>-1</c> means here. The glower forces full
    /// brightness because a light cannot emit a dark colour and an allowed area forces a mid value so the overlay
    /// stays legible; a dye has no such restriction, and clamping one would quietly refuse black clothing.
    ///
    /// <b>The palette passed in is a starting point, not the choice.</b> It is the same dye list the swatch row
    /// offers, so the two agree about what is easy to reach, and the wheel is there for everything else.
    /// </summary>
    internal sealed class Dialog_PickColour : Dialog_ColorPickerBase
    {
        private readonly Action<Color> apply;

        private readonly List<Color> offered;

        private readonly Color fallback;

        private Dialog_PickColour(Color current, List<Color> offered, Color fallback, Action<Color> apply)
            : base(Widgets.ColorComponents.All, Widgets.ColorComponents.All)
        {
            this.apply = apply;
            this.offered = offered ?? new List<Color>();
            this.fallback = fallback;

            // Both, because the base draws the old colour beside the new one as a comparison and starts the
            // wheel wherever the current colour already is.
            color = current;
            oldColor = current;
        }

        internal static void Open(Color current, List<Color> offered, Color fallback, Action<Color> apply)
        {
            if (apply == null)
                return;

            Find.WindowStack.Add(new Dialog_PickColour(current, offered, fallback, apply));
        }

        protected override bool ShowDarklight
        {
            get { return false; }
        }

        /// <summary>What the reset control goes back to: the colour the item would be with no dye at all.</summary>
        protected override Color DefaultColor
        {
            get { return fallback; }
        }

        protected override List<Color> PickableColors
        {
            get { return offered; }
        }

        /// <summary>Negative means "do not force the value channel". See the class note.</summary>
        protected override float ForcedColorValue
        {
            get { return -1f; }
        }

        /// <summary>Warm-to-cool is a lighting idea. A dye is not a light.</summary>
        protected override bool ShowColorTemperatureBar
        {
            get { return false; }
        }

        protected override void SaveColor(Color color)
        {
            apply(color);
        }
    }
}
