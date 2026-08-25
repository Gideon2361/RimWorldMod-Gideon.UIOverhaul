using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.ColonyBar
{
    /// <summary>When a colonist's weapon is shown under their meters.</summary>
    public enum BarWeaponDisplay
    {
        /// <summary>Never. The bar keeps the height it had before this setting existed.</summary>
        Never,

        /// <summary>Only while the pawn is drafted, which is when what they are holding matters most.</summary>
        Drafted,

        /// <summary>Always, for anybody carrying something.</summary>
        Always
    }

    /// <summary>
    /// The weapon strip under a tile's meters.
    ///
    /// <b>The row is reserved by the setting, not by the pawn.</b> Whether a given colonist shows an icon changes
    /// the moment they draft, and a tile that grew and shrank on that would make the whole bar jump every time
    /// somebody picked up a gun. So anything other than <see cref="BarWeaponDisplay.Never"/> reserves the row for
    /// every tile and leaves it empty where there is nothing to draw. Never reserves nothing at all, which is what
    /// keeps this setting free for anybody who does not want it.
    /// </summary>
    internal static class BarWeapon
    {
        /// <summary>
        /// How tall the row is.
        ///
        /// Slightly more than the two meters together. A weapon icon is a picture rather than a bar, and at the
        /// meters' three pixels it would be a smudge.
        /// </summary>
        internal const float RowHeight = 14f;

        /// <summary>The gap between the mood bar and the icon.</summary>
        internal const float RowGap = 2f;

        /// <summary>How much taller a tile is for the current setting. Zero when the row is off.</summary>
        internal static float Reserve(BarWeaponDisplay display)
        {
            return display == BarWeaponDisplay.Never ? 0f : RowGap + RowHeight;
        }

        /// <summary>What this pawn should show in the row, or null for an empty row.</summary>
        internal static Thing WeaponOf(Pawn pawn, BarWeaponDisplay display)
        {
            if (display == BarWeaponDisplay.Never || pawn == null || pawn.equipment == null)
                return null;

            // Drafted mode asks the drafter rather than inferring from a stance, so a pawn standing still while
            // drafted still counts. A pawn with no drafter at all is never drafted, which is the right answer
            // for a colony animal or a guest that wandered into the bar.
            if (display == BarWeaponDisplay.Drafted && (pawn.drafter == null || !pawn.drafter.Drafted))
                return null;

            return pawn.equipment.Primary;
        }

        /// <summary>
        /// Draws the row: the icon centered, or nothing.
        ///
        /// <b>Widgets.ThingIcon rather than the def's uiIcon.</b> The vanilla helper already handles stuff color,
        /// graphic index and the icon scale a def can ask for, so a plasteel sword comes out the right color and a
        /// modded weapon that overrides its icon is drawn the way its author meant.
        /// </summary>
        internal static void Draw(Rect row, Pawn pawn, BarWeaponDisplay display)
        {
            Thing weapon = WeaponOf(pawn, display);

            if (weapon == null)
                return;

            Rect icon = new Rect(row.center.x - RowHeight * 0.5f, row.y, RowHeight, RowHeight);

            Color previous = GUI.color;

            // White first: GUI.DrawTexture multiplies by GUI.color, and this runs inside vanilla's OnGUI where
            // the color is whatever the last caller left. Same trap the portrait above it carries a note about.
            GUI.color = Color.white;

            Widgets.ThingIcon(icon, weapon);

            GUI.color = previous;

            if (Mouse.IsOver(icon))
                TooltipHandler.TipRegion(icon, weapon.LabelCap);
        }
    }
}
