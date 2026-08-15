using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Notifications
{
    /// <summary>
    /// The letter stack, redrawn as labelled rows.
    ///
    /// <b>This takes over the whole stack, which the previous version deliberately did not.</b> That version
    /// patched <c>Letter.DrawButtonAt</c> -- one letter's own drawing -- because <c>LettersOnGUI</c> owns the
    /// layout, the bundling of old letters into a pile, the mouseover pass and <c>lastTopYInt</c>, and reproducing
    /// all of that to change how a button looks was not worth it. The trade was stated at the time: letters kept
    /// vanilla's 38 by 30 footprint, so they were cards the size of an icon while the messages and alerts beside
    /// them were labelled rows. Widening them was always going to mean owning the layout, and docking made it
    /// necessary anyway -- a stack that can move to another corner is a stack whose geometry is ours.
    ///
    /// <b>What the extra width buys.</b> Vanilla shows an icon and puts the letter's label in a plate that appears
    /// on hover, so reading the stack means pointing at each one in turn. A row shows the label outright, which
    /// turns "there are four letters" into "there is a raid, a trade caravan, a birthday and a breakdown" without
    /// moving the mouse.
    ///
    /// <b>Every behavior is still vanilla's</b>, read out of <c>LetterStack.LettersOnGUI</c> and
    /// <c>Letter.DrawButtonAt</c> and reproduced: the slide-in and fade over the first second, the periodic bounce
    /// for letters whose def asks for it, the flash for urgent ones, left click to open, right click to dismiss
    /// where the letter permits it, the mouseover text pane, and the bundling of everything that no longer fits
    /// into a single pile. A restyle that quietly drops the right-click dismiss is not a restyle.
    ///
    /// <b><c>lastTopYInt</c> is still written, and it matters more than it looks.</b> It is a private field with a
    /// public reader that vanilla's alerts readout and an unknown number of mods anchor against. This mod's own
    /// alerts no longer use it -- see <see cref="NotificationLayout"/> -- but writing something sensible into it is
    /// what keeps everyone else's arithmetic working.
    ///
    /// <b>Icons are the part that is still deliberately unfinished.</b> Until an event icon set exists, every
    /// letter shows the drawn envelope and says what it is through its edge color. That is the honest version of
    /// "we do not have art yet": a plain envelope with the right tone, rather than a guessed glyph that means
    /// nothing.
    /// </summary>
    internal static class LetterRows
    {
        /// <summary>Vanilla's letter height, which is also comfortably a row of <c>Small</c> text.</summary>
        private const float MinRowHeight = 30f;

        /// <summary>Gap between rows. Tighter than vanilla's 12, which was spacing icons rather than rows.</summary>
        private const float RowGap = 4f;

        /// <summary>Inset from the screen edge, matching what vanilla gives the letter buttons.</summary>
        private const float EdgeInset = 12f;

        /// <summary>How long the arrival slide and fade take, in seconds. Vanilla's value.</summary>
        private const float ArrivalSeconds = 1f;

        /// <summary>How far above its resting place a letter starts. Vanilla's value.</summary>
        private const float ArrivalRise = 200f;

        /// <summary>Width of the mouseover text pane. Vanilla's own.</summary>
        private const float PaneWidth = 330f;

        /// <summary>Salt for the mouseover pane's window id, so ours cannot collide with vanilla's 2768333.</summary>
        private const int PaneIdSalt = 0x4C54_5257;

        private static readonly UINotificationCard Card = new UINotificationCard
        {
            EdgeWidth = 3f,
            ContentInset = 6f,
            IconSize = 16f,
            IconGap = 6f,
            VerticalPad = 2f
        };

        /// <summary>
        /// Where the stack ended, which vanilla publishes through <c>LetterStack.LastTopY</c>.
        ///
        /// The two-argument overload of <c>StaticFieldRefAccess</c>' instance sibling returns a ref, so this reads
        /// and assigns like an ordinary field.
        /// </summary>
        private static readonly AccessTools.FieldRef<LetterStack, float> LastTopY =
            AccessTools.FieldRefAccess<LetterStack, float>("lastTopYInt");

        /// <summary>
        /// Whether this can run at all.
        ///
        /// Only the private field is required. Without it the stack would draw correctly and then leave every
        /// other mod's alerts anchored to a number that stopped moving, which is a worse failure than not
        /// restyling: vanilla draws instead, and everything downstream keeps working.
        /// </summary>
        internal static bool Available => LastTopY != null;

        /// <summary>
        /// Row width, as the player set it.
        ///
        /// <b>A setting rather than a constant, because the trade is genuinely the player's.</b> These rows draw
        /// over the map, so every pixel of width is a pixel of colony that is harder to see, and how much that
        /// costs depends on the screen and on how the person plays. 250 matches the corner panel below them, which
        /// is enough for most letter labels and lines the two columns up.
        ///
        /// Clamped rather than trusted. This is a hand-editable file, and a width of zero or of four thousand
        /// should give an odd looking stack rather than an unusable screen.
        /// </summary>
        private static float Width
        {
            get
            {
                float width = UIGuard.Try("Notifications.ReadLetterWidth",
                    () => UIOverhaulSettingsFile.Current.letterRowWidth, 250f,
                    "The letter rows are drawn at their default width.");

                return Mathf.Clamp(width, 150f, 520f);
            }
        }

        private static float RowHeight => Mathf.Max(MinRowHeight, UIFonts.RowHeight(GameFont.Small));

        /// <summary>
        /// Draws the whole stack.
        ///
        /// <paramref name="baseY"/> is what the corner hands over: the height its own readouts stopped at. It is
        /// passed to the layout rather than used directly, because it is the anchor for anything docked at the
        /// bottom right whether or not the letters are among them.
        /// </summary>
        internal static void Draw(LetterStack stack, float baseY)
        {
            // Reported first and unconditionally. Whoever computed baseY -- this mod's corner panel, or vanilla's
            // own if that has stood down -- it is the top of the bottom right corner, and the alerts column needs
            // it even when the letters have moved somewhere else entirely.
            NotificationLayout.Notify_CornerTop(baseY);

            List<Letter> letters = stack.LettersListForReading;

            if (letters == null)
            {
                LastTopY(stack) = baseY;
                NotificationLayout.Report(NotificationSurface.Letters, NotificationDock.BottomRight, 0f);

                return;
            }

            NotificationDock dock = NotificationLayout.DockOf(NotificationSurface.Letters);

            float width = Width;
            float rowHeight = RowHeight;
            float step = rowHeight + RowGap;

            // The inset belongs on whichever side faces the screen edge. A right docked column is asked for a
            // width one inset wider than it draws, which pulls it left by exactly that; a left docked one is
            // pushed right instead, since its column position ignores the width.
            float x = NotificationLayout.ColumnX(dock, width + EdgeInset)
                      + (dock == NotificationDock.TopLeft ? EdgeInset : 0f);

            float anchor = NotificationLayout.Anchor(NotificationSurface.Letters, dock);
            bool up = NotificationLayout.GrowsUp(dock);

            int shown = Capacity(dock, step);
            int bundled = Mathf.Max(letters.Count - shown, 0);

            // Vanilla's own adjustment: the pile takes a row of its own, so making room for it costs one more of
            // the letters that would otherwise have been visible.
            if (bundled > 0)
                bundled++;

            float cursor = anchor;
            float used = 0f;

            // Backwards, as vanilla walks it: the newest letter is the one nearest the anchor.
            for (int i = letters.Count - 1; i >= bundled; i--)
            {
                Letter letter = letters[i];

                if (letter?.def == null)
                    continue;

                DrawRow(letter, Row(ref cursor, x, width, rowHeight, up), dock, false);
                used += step;
            }

            if (bundled > 0)
            {
                UIGuard.Try("Notifications.LetterBundle", () =>
                    {
                        List<Letter> pile = new List<Letter>();

                        for (int i = 0; i < bundled && i < letters.Count; i++)
                            pile.Add(letters[i]);

                        BundleLetter bundle = stack.BundleLetter;
                        bundle.SetLetters(pile);

                        DrawRow(bundle, Row(ref cursor, x, width, rowHeight, up), dock, true);
                    },
                    "The pile of older letters is missing from the stack. The letters in it are still in the "
                    + "history tab.");

                used += step;
            }

            // What vanilla publishes. For a bottom right stack this is where the column really ended, which is
            // what anything anchoring above it expects. For a stack that has moved, the corner's own anchor is
            // handed back unchanged -- a mod stacking against the letters should not have its panel fly to the top
            // of the screen because this mod's owner moved their letters.
            LastTopY(stack) = up ? cursor : baseY;

            NotificationLayout.Report(NotificationSurface.Letters, dock, Mathf.Max(0f, used - RowGap));
        }

        /// <summary>
        /// The next row's rect, moving the cursor past it in whichever direction this dock stacks.
        /// </summary>
        private static Rect Row(ref float cursor, float x, float width, float height, bool up)
        {
            if (up)
            {
                cursor -= height;
                Rect rect = new Rect(x, cursor, width, height);
                cursor -= RowGap;

                return rect;
            }

            Rect down = new Rect(x, cursor, width, height);
            cursor += height + RowGap;

            return down;
        }

        /// <summary>
        /// How many rows fit before the rest have to be bundled.
        ///
        /// <b>The alerts' own height is subtracted, and only when they share this dock.</b> Vanilla does the same
        /// thing with <c>Find.Alerts.AlertsHeight</c> and has to, because letters and alerts always share a
        /// corner there. Here they need not, so the subtraction is conditional -- otherwise moving the alerts to
        /// the top right would silently shorten the letter stack to make room for a column that is no longer
        /// anywhere near it.
        ///
        /// The height used is what the alerts actually drew last frame rather than vanilla's estimate, since this
        /// mod sizes its alert cards to their labels and vanilla's figure assumes a fixed height per alert.
        /// </summary>
        private static int Capacity(NotificationDock dock, float step)
        {
            float room = NotificationLayout.Room(NotificationSurface.Letters, dock);

            if (NotificationLayout.DockOf(NotificationSurface.Alerts) == dock)
                room -= NotificationLayout.HeightOf(NotificationSurface.Alerts, dock);

            return Mathf.Max(0, Mathf.FloorToInt(room / Mathf.Max(1f, step)));
        }

        /// <summary>
        /// One letter: its glyph, its label, and everything vanilla does to make an arrival noticeable.
        /// </summary>
        private static void DrawRow(Letter letter, Rect resting, NotificationDock dock, bool isBundle)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Rect row = resting;

            float age = Time.time - letter.arrivalTime;
            float alpha = 1f;

            // Arrival: the letter drops into place while fading up. Reproduced rather than dropped, because a
            // stack where new letters simply appear gives no cue that something arrived.
            if (!isBundle && age < ArrivalSeconds)
            {
                row.y -= (1f - age) * ArrivalRise;
                alpha = age / ArrivalSeconds;
            }

            row.x += BounceOffset(letter, resting, age, dock);

            if (Event.current.type == EventType.Repaint)
            {
                Flash(letter, resting, age);
                Paint(letter, row, palette, alpha);
            }

            Handle(letter, row, resting, dock);
        }

        /// <summary>
        /// How far the letter has swung, for defs that ask to be noticed again after a while.
        ///
        /// Vanilla's curve exactly: a half-second arc every five seconds, starting fifteen seconds after arrival,
        /// suppressed while the pointer is on it. The shape is <c>1 - x squared</c> over minus one to one, which
        /// is a smooth out-and-back rather than a jump.
        ///
        /// <b>The direction follows the dock.</b> Vanilla always swings left, because its stack is always on the
        /// right and that is the direction that moves a letter <i>into</i> the map where it will be seen. A left
        /// docked stack has to swing the other way for the same reason.
        /// </summary>
        private static float BounceOffset(Letter letter, Rect resting, float age, NotificationDock dock)
        {
            if (Mouse.IsOver(resting) || !letter.def.bounce || age <= 15f || age % 5f >= 1f)
                return 0f;

            float swing = 2f * (age % 1f) - 1f;
            float distance = UI.screenWidth * 0.06f * (1f - swing * swing);

            return dock == NotificationDock.TopLeft ? distance : -distance;
        }

        /// <summary>Vanilla's urgent flash, for letter defs that set a flash interval.</summary>
        private static void Flash(Letter letter, Rect resting, float age)
        {
            if (letter.def.flashInterval <= 0f)
                return;

            float since = age - ArrivalSeconds;

            if (since <= 0f || since % letter.def.flashInterval >= 1f)
                return;

            GenUI.DrawFlash(resting.x, resting.y, UI.screenWidth * 0.6f,
                Pulser.PulseBrightness(1f, 1f, since) * 0.55f, letter.def.flashColor);
        }

        private static void Paint(Letter letter, Rect row, UIColorPaletteDef palette, float alpha)
        {
            Color edge = NotificationColors.For(letter.def, palette);

            Rect text = Card.DrawChrome(row, palette, edge, alpha, Mouse.IsOver(row));

            Texture2D icon = NotificationIcons.Envelope;

            if (icon != null)
            {
                Color previousIcon = GUI.color;

                // The envelope in the letter's own tone rather than plain white, so the color reads from the
                // glyph as well as from the three pixel edge beside it.
                GUI.color = new Color(edge.r, edge.g, edge.b, alpha);
                GUI.DrawTexture(Card.IconRect(row), icon, ScaleMode.ScaleToFit);
                GUI.color = previousIcon;
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;

                // Text at full strength against the card's own fade, so the last thing to disappear is the words.
                GUI.color = new Color(palette.TextPrimary.r, palette.TextPrimary.g, palette.TextPrimary.b, alpha);

                Widgets.LabelEllipses(text, LabelOf(letter));
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// The label, defensively.
        ///
        /// A letter's label is built by whoever raised it, and a modded one that throws while the stack is drawing
        /// would take the whole column with it. A named placeholder says which letter is misbehaving, which is
        /// worth more than a blank row.
        /// </summary>
        private static string LabelOf(Letter letter)
        {
            return UIGuard.Try("Notifications.LetterLabel", () => letter.Label.Resolve(),
                letter.def?.defName ?? "Letter",
                "One letter shows its def name instead of its label.");
        }

        /// <summary>
        /// Hovering, clicking and the mouseover pane.
        ///
        /// <b>Two rects, and the split is vanilla's rather than an oversight.</b> Dismissing tests where the
        /// letter belongs, so a right click during a bounce still lands, while opening tests where it actually is,
        /// so a click follows what the player can see.
        /// </summary>
        private static void Handle(Letter letter, Rect row, Rect resting, NotificationDock dock)
        {
            if (Mouse.IsOver(row))
            {
                // What makes hovering a letter highlight its subject on the map. Vanilla's LetterStackUpdate reads
                // the index this sets, so not calling it would silently drop that behavior.
                Find.LetterStack.Notify_LetterMouseover(letter);

                if (Event.current.type == EventType.Repaint)
                    DrawPane(letter, row, dock);
            }

            if (letter.CanDismissWithRightClick && Event.current.type == EventType.MouseDown
                                                && Event.current.button == 1 && Mouse.IsOver(resting))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                Find.LetterStack.RemoveLetter(letter);
                Event.current.Use();

                return;
            }

            if (!Widgets.ButtonInvisible(row))
                return;

            letter.OpenLetter();
            Event.current.Use();
        }

        /// <summary>
        /// The hover pane: the letter's full text, beside the row rather than over it.
        ///
        /// <b>Kept even though the row now shows the label.</b> The label is a headline and the pane is the letter
        /// -- who is raiding, what the caravan is selling, which colonist it is about. Vanilla shows this and it is
        /// most of what makes the stack readable without opening anything.
        ///
        /// <b>Read through <c>IArchivable</c> rather than by reflection.</b> <c>GetMouseoverText</c> is protected,
        /// but <c>Letter</c> implements the archive interface by forwarding to it, so the text is available
        /// through a public member with virtual dispatch intact. That matters for the pile, whose version lists
        /// what is in it.
        ///
        /// It opens on the side away from the screen edge, so it never runs off.
        /// </summary>
        private static void DrawPane(Letter letter, Rect row, NotificationDock dock)
        {
            UIGuard.Try("Notifications.LetterPane", () =>
            {
                string text = ((IArchivable) letter).ArchivedTooltip;

                if (text.NullOrEmpty())
                    return;

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;

                float height = Text.CalcHeight(text, PaneWidth - 20f) + 20f;

                float x = dock == NotificationDock.TopLeft
                    ? row.xMax + 10f
                    : row.x - PaneWidth - 10f;

                Rect pane = new Rect(x, Mathf.Clamp(row.y - height / 2f, 0f, Mathf.Max(0f,
                    UI.screenHeight - height)), PaneWidth, height);

                Find.WindowStack.ImmediateWindow(Gen.HashCombineInt(letter.ID, PaneIdSalt), pane,
                    WindowLayer.Super,
                    () => UIGuard.Try("Notifications.LetterPaneContents", () => PaneContents(pane, text)));
            }, "One letter shows no hover text.");
        }

        private static void PaneContents(Rect pane, string text)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            // ImmediateWindow puts the origin at the window's own top left, so everything inside is drawn against
            // the pane at zero rather than against where it sits on screen.
            Rect local = pane.AtZero();

            Widgets.DrawBoxSolid(local, palette.HudBackground);

            Color previousColor = GUI.color;
            GUI.color = palette.Border;
            Widgets.DrawBox(local, 1);

            GUI.color = palette.TextPrimary;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            Widgets.Label(local.ContractedBy(10f), text);

            GUI.color = previousColor;
        }
    }

    /// <summary>
    /// Hands the letter stack over to <see cref="LetterRows"/>.
    ///
    /// A replacing prefix rather than a postfix, because both would draw and the result would be our rows with
    /// vanilla's icons stacked through them.
    ///
    /// <b>Three ways this hands back to vanilla, and all three are real rather than defensive.</b> Another mod
    /// already owning the surface, the private field this needs having moved, and the player asking for vanilla
    /// letters in the settings. The last is checked every frame rather than at <c>Prepare</c>, so the switch takes
    /// effect while the settings window is still open.
    ///
    /// <b>The postfix exists for a combination that is easy to forget.</b> A player can switch the letters back to
    /// vanilla and keep this mod's alerts, and vanilla's letter drawing tells <see cref="NotificationLayout"/>
    /// nothing -- so the alerts would find no letters reserved beneath them and stack straight over the top. The
    /// postfix measures what vanilla drew, from the anchor it was handed to where it says it stopped, and reports
    /// that on its behalf. Every mixture of the two settings then stacks properly.
    /// </summary>
    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.LettersOnGUI))]
    public static class Patch_LetterStack_LettersOnGUI
    {
        /// <summary>Not applied at all when another mod already owns this surface.</summary>
        public static bool Prepare() => NotificationCompatibility.ShouldPatch();

        /// <summary>
        /// <paramref name="__state"/> carries one fact to the postfix: whether vanilla is doing the drawing. It
        /// cannot be recomputed there, because a draw that failed partway also falls back and the setting alone
        /// would not say so.
        /// </summary>
        public static bool Prefix(LetterStack __instance, float baseY, out bool __state)
        {
            bool vanilla = !LetterRows.Available
                           || !NotificationSettings.Restyle(NotificationSurface.Letters);

            if (vanilla)
            {
                // Still reported, because the corner ends here whoever draws the letters, and anything docked at
                // the bottom right is anchored to it.
                NotificationLayout.Notify_CornerTop(baseY);
            }
            else
            {
                vanilla = UIGuard.Replaced("Notifications.Letters", () => LetterRows.Draw(__instance, baseY),
                    "Letters are drawn in the vanilla style for the rest of the session.");
            }

            __state = vanilla;

            return vanilla;
        }

        public static void Postfix(LetterStack __instance, float baseY, bool __state)
        {
            if (!__state)
                return;

            UIGuard.Try("Notifications.MeasureVanillaLetters", () =>
                    NotificationLayout.Report(NotificationSurface.Letters, NotificationDock.BottomRight,
                        Mathf.Max(0f, baseY - __instance.LastTopY)),
                "This mod's alerts may overlap RimWorld's own letter stack.");
        }
    }
}
