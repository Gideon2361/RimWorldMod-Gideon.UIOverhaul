using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Notifications
{
    /// <summary>
    /// Draws each letter in the stack as a card instead of a bare icon.
    ///
    /// <b>The seam is one letter, not the whole readout.</b> <c>LettersOnGUI</c> was the obvious target and is the
    /// wrong one: it owns the stack's layout, the bundling of old letters into a single pile once they no longer
    /// fit, the mouseover pass, and <c>lastTopYInt</c> -- which is what the alerts column and this mod's own alert
    /// cards anchor against. Replacing it would mean reproducing all of that, including a private bundle letter, for
    /// the sake of changing how one button looks. Patching <c>DrawButtonAt</c> instead leaves every one of those
    /// vanilla and changes only the drawing.
    ///
    /// <b>The consequence of that choice, stated plainly:</b> letters keep vanilla's 38 by 30 footprint, so these
    /// are cards the size of an icon rather than the labelled rows the messages and alerts get. Widening them means
    /// taking over the layout, which is worth doing once the column's geometry is settled and not before.
    ///
    /// <b>Every behavior is vanilla's</b>, read out of <c>Letter.DrawButtonAt</c> and reproduced: the slide-in and
    /// fade over the first second, the periodic bounce for letters whose def asks for it, the flash for urgent ones,
    /// left click to open, right click to dismiss where the letter permits it. The label plate on hover is vanilla's
    /// too, and is left entirely alone -- it is drawn by <c>CheckForMouseOverTextAt</c> in a separate pass this does
    /// not touch.
    ///
    /// <b>Icons are the part that is deliberately unfinished.</b> Until an event icon set exists, every letter shows
    /// the drawn envelope and says what it is through its edge color, which comes from the letter's own def. That is
    /// the honest version of "we do not have art yet": a plain envelope with the right tone, rather than a guessed
    /// glyph that means nothing.
    /// </summary>
    internal static class LetterCards
    {
        /// <summary>Vanilla's letter button footprint, and its inset from the right edge.</summary>
        private const float Width = 38f;

        private const float Height = 30f;
        private const float RightInset = 12f;

        /// <summary>How long the arrival slide and fade take, in seconds. Vanilla's value.</summary>
        private const float ArrivalSeconds = 1f;

        /// <summary>How far above its resting place a letter starts. Vanilla's value.</summary>
        private const float ArrivalRise = 200f;

        private static readonly UINotificationCard Card = new UINotificationCard
        {
            EdgeWidth = 3f,
            ContentInset = 3f,
            VerticalPad = 2f
        };

        internal static void Draw(Letter letter, float topY)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            float x = UI.screenWidth - Width - RightInset;
            Rect resting = new Rect(x, topY, Width, Height);
            Rect card = resting;

            float age = Time.time - letter.arrivalTime;
            float alpha = 1f;

            // Arrival: the letter drops into place while fading up. Reproduced rather than dropped, because a stack
            // where new letters simply appear gives no cue that something arrived.
            if (age < ArrivalSeconds)
            {
                card.y -= (1f - age) * ArrivalRise;
                alpha = age / ArrivalSeconds;
            }

            card.x -= BounceOffset(letter, resting, age);

            if (Event.current.type == EventType.Repaint)
            {
                Flash(letter, x, topY, age);
                Paint(letter, card, palette, alpha);
            }

            Handle(letter, card, resting);
        }

        /// <summary>
        /// How far left the letter has swung, for defs that ask to be noticed again after a while.
        ///
        /// Vanilla's curve exactly: a half-second arc every five seconds, starting fifteen seconds after arrival,
        /// suppressed while the pointer is on it. The shape is <c>1 - x squared</c> over minus one to one, which is
        /// a smooth out-and-back rather than a jump.
        /// </summary>
        private static float BounceOffset(Letter letter, Rect resting, float age)
        {
            if (Mouse.IsOver(resting) || !letter.def.bounce || age <= 15f || age % 5f >= 1f)
                return 0f;

            float swing = 2f * (age % 1f) - 1f;

            return UI.screenWidth * 0.06f * (1f - swing * swing);
        }

        /// <summary>Vanilla's urgent flash, for letter defs that set a flash interval.</summary>
        private static void Flash(Letter letter, float x, float topY, float age)
        {
            if (letter.def.flashInterval <= 0f)
                return;

            float since = age - ArrivalSeconds;

            if (since <= 0f || since % letter.def.flashInterval >= 1f)
                return;

            GenUI.DrawFlash(x, topY, UI.screenWidth * 0.6f,
                Pulser.PulseBrightness(1f, 1f, since) * 0.55f, letter.def.flashColor);
        }

        private static void Paint(Letter letter, Rect card, UIColorPaletteDef palette, float alpha)
        {
            // The def's own color, not a palette role. A letter def is authored with a color that already says what
            // kind of thing it is -- and every mod that adds letters picks one -- so this is the one notification
            // surface where the source has a better answer than our palette does.
            Color edge = letter.def.color;

            Card.DrawChrome(card, palette, edge, alpha, Mouse.IsOver(card));

            Texture2D icon = NotificationIcons.Envelope;

            if (icon == null)
                return;

            Rect glyph = new Rect(card.x + Card.EdgeWidth + 3f, card.y + 5f,
                card.width - Card.EdgeWidth - 8f, card.height - 10f);

            Color previous = GUI.color;

            // The envelope in the letter's own color rather than plain white, so the tone reads from the glyph as
            // well as from the edge at this size, where the edge is only three pixels wide.
            GUI.color = new Color(edge.r, edge.g, edge.b, alpha);
            GUI.DrawTexture(glyph, icon, ScaleMode.ScaleToFit);
            GUI.color = previous;
        }

        /// <summary>
        /// Click handling, on the resting rect for dismissal and the drawn rect for opening.
        ///
        /// That split is vanilla's and is not an oversight in either direction: dismissing tests where the letter
        /// belongs, so a right click during a bounce still lands, while opening tests where it actually is, so a
        /// click follows what the player can see.
        /// </summary>
        private static void Handle(Letter letter, Rect card, Rect resting)
        {
            if (letter.CanDismissWithRightClick && Event.current.type == EventType.MouseDown
                                                && Event.current.button == 1 && Mouse.IsOver(resting))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                Find.LetterStack.RemoveLetter(letter);
                Event.current.Use();

                return;
            }

            if (!Widgets.ButtonInvisible(card))
                return;

            letter.OpenLetter();
            Event.current.Use();
        }
    }

    /// <summary>
    /// Hands one letter's button over to <see cref="LetterCards"/>.
    ///
    /// <b>Only letters that use the base implementation are restyled.</b> <c>DrawButtonAt</c> is virtual, so a
    /// letter type that overrides it -- vanilla's bundle letter does -- dispatches to its own body and never reaches
    /// this patch. That is a limitation rather than a bug, and the failure mode is mild: the odd letter draws in the
    /// vanilla style beside the restyled ones. Patching every override would mean finding them at runtime and
    /// patching types this mod has never heard of.
    /// </summary>
    [HarmonyPatch(typeof(Letter), nameof(Letter.DrawButtonAt))]
    public static class Patch_Letter_DrawButtonAt
    {
        /// <summary>Not applied at all when another mod already owns this surface.</summary>
        public static bool Prepare() => NotificationCompatibility.ShouldPatch();

        public static bool Prefix(Letter __instance, float topY)
        {
            // A letter with no def has nothing to take a color or a flash from, and is vanilla's problem rather than
            // ours. Handing it back is cheaper than defending every read below against it.
            if (__instance?.def == null)
                return true;

            return UIGuard.Replaced("Notifications.Letter", () => LetterCards.Draw(__instance, topY),
                "Letters are drawn in the vanilla style for the rest of the session.");
        }
    }
}
