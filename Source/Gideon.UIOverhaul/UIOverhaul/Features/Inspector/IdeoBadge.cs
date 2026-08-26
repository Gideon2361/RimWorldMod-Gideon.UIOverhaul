using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// A pawn's ideoligion, as its own icon in the inspect pane's corner.
    ///
    /// <b>It answers a question the pane could not.</b> Which ideoligion somebody follows changes what they will
    /// eat, wear, work at and be upset by, and finding it out meant opening the Ideoligion tab and reading a
    /// member list. The icon is the one glyph that says it at a glance, and it is the icon the player already
    /// associates with that ideoligion from every other place the game draws it. Asked for on 2026-08-26.
    ///
    /// <b>In the ideoligion's own colour,</b> unlike every other icon in this corner. Those are flat greyscale
    /// marks that take the palette; this one is an identity, and two ideoligions with the same symbol in
    /// different colours are two different ideoligions. It is the one icon here that must not be tinted.
    ///
    /// <b>Absent rather than blank in the three cases where it would say nothing:</b> without Ideology, in classic
    /// mode where the whole colony shares one ideoligion, and on a pawn who has none.
    /// </summary>
    internal static class IdeoBadge
    {
        private const float Size = InspectPaneUtility.CornerButtonsSize;

        /// <summary>
        /// Draws the badge and returns the width it took, so the header can lay the name around it.
        ///
        /// The same contract as the editor and rename buttons beside it: measure from the right edge inwards
        /// using what has already been used, and report what was consumed.
        /// </summary>
        internal static float Draw(Rect header, float usedAlready, Pawn pawn, UIColorPaletteDef palette)
        {
            if (pawn == null || !ModsConfig.IdeologyActive)
                return 0f;

            return UIGuard.Try("Inspector.IdeoBadge", () => Badge(header, usedAlready, pawn, palette), 0f, null);
        }

        private static float Badge(Rect header, float usedAlready, Pawn pawn, UIColorPaletteDef palette)
        {
            if (Find.IdeoManager != null && Find.IdeoManager.classicMode)
                return 0f;

            Ideo ideo = pawn.Ideo;

            if (ideo == null || ideo.Icon == null)
                return 0f;

            Rect button = new Rect(header.width - usedAlready - Size, 0f, Size, Size);

            MouseoverSounds.DoRegion(button);

            bool over = Mouse.IsOver(button);

            Color previous = GUI.color;

            try
            {
                // The ideoligion's own colour, brightened slightly under the pointer. Not the palette's, for the
                // reason on the class: the colour is part of which ideoligion this is.
                GUI.color = over ? ideo.Color : new Color(ideo.Color.r, ideo.Color.g, ideo.Color.b, 0.85f);

                GUI.DrawTexture(button.ContractedBy(2f), ideo.Icon, ScaleMode.ScaleToFit);
            }
            finally
            {
                GUI.color = previous;
            }

            TooltipHandler.TipRegion(button, (TipSignal) Tip(pawn, ideo));

            if (Widgets.ButtonInvisible(button))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();

                // Vanilla's own ideoligion window, so this is a shortcut to a screen the player knows rather
                // than a second place ideoligions are described.
                UIGuard.Try("Inspector.OpenIdeo", () => IdeoUIUtility.OpenIdeoInfo(ideo),
                    "That ideoligion's page did not open.");
            }

            return Size;
        }

        /// <summary>
        /// The name, and how sure of it they are.
        ///
        /// <b>Certainty is the half that is worth hovering for.</b> The icon already says which ideoligion; what
        /// it cannot say is whether this pawn is about to stop believing it, which is the thing that decides
        /// whether a conversion attempt is worth a warden's day.
        /// </summary>
        private static string Tip(Pawn pawn, Ideo ideo)
        {
            string tip = ideo.name;

            float certainty = UIGuard.Try("Inspector.IdeoCertainty", () => pawn.ideo?.Certainty ?? -1f, -1f, null);

            if (certainty >= 0f)
                tip += "\n\n" + "Certainty".Translate() + ": " + certainty.ToStringPercent();

            return tip + "\n\n" + "Click to open this ideoligion.";
        }
    }
}
