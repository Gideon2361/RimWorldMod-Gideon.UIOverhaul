using System;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.ButtonBar
{
    /// <summary>
    /// Draws one bar widget. Subclass this and name the subclass in a <see cref="UIBarWidgetDef"/>.
    ///
    /// One instance per def, created on first use and kept for the session, so a worker may cache. It is
    /// asked for its width and then drawn once per frame, on the main thread, inside the button bar's own
    /// OnGUI pass.
    ///
    /// <b>The rect is yours.</b> The bar paints a sunken tray behind the widget and absorbs any click the
    /// widget did not take, then hands over the whole slot. Readouts are expected to draw text; a widget
    /// with controls draws its own buttons.
    ///
    /// <b>Failures are contained.</b> Anything thrown from <see cref="Draw"/> or <see cref="MeasureWidth"/>
    /// disables that one widget for the rest of the session and leaves the bar working. A widget can come
    /// from any mod, and one bad frame must not cost the player the bar they navigate the game with.
    ///
    /// <b>The implementations live in <c>BarWidgets</c>, not <c>Widgets</c>.</b> A child namespace called
    /// <c>Widgets</c> shadows <c>Verse.Widgets</c> for every file in the parent namespace, so the moment that
    /// folder existed, <c>Widgets.Label</c> in the bar's renderer and editor stopped resolving. The awkward
    /// name is deliberate; renaming it back breaks the whole feature at compile time.
    /// </summary>
    public abstract class UIBarWidgetWorker
    {
        /// <summary>The def that created this worker. Assigned before first use.</summary>
        public UIBarWidgetDef def;

        /// <summary>
        /// Widths are rounded up to a multiple of this before use.
        ///
        /// Text width changes as the text does: an hour ticks over, a temperature loses a digit, the weather
        /// turns from "Clear" to "Foggy". Measured exactly, every one of those would shift the widget and
        /// every button to its right by a pixel or two, which reads as the bar twitching. Rounding to a step
        /// absorbs the small changes, and <see cref="highWater"/> absorbs the rest.
        /// </summary>
        private const float WidthStep = 8f;

        /// <summary>
        /// The widest this widget has ever needed, which is what it keeps asking for.
        ///
        /// Monotonic on purpose. A widget that shrank back would move the bar around whenever its content
        /// got shorter, and reclaiming eight pixels is not worth a bar that never settles. It converges
        /// within the first few seconds of play and then stops changing.
        /// </summary>
        private float highWater;

        /// <summary>Set once this widget has thrown. It is not drawn or measured again.</summary>
        private bool broken;

        /// <summary>
        /// Whether the bar should give this widget a slot. False leaves no gap: the slot is dropped and the
        /// remaining buttons share the width.
        /// </summary>
        public bool Visible => !broken && ShouldShowSafely();

        /// <summary>Override to hide the widget when there is nothing for it to report.</summary>
        protected virtual bool ShouldShow => true;

        /// <summary>
        /// Whether this widget opens something else when clicked: a menu, a window, a tab.
        ///
        /// This decides the color of the rule along the top of the widget's slot, and it is the only thing
        /// that does. The bar's grammar is that the accent color means "this leads somewhere", so a widget
        /// that opens a menu earns the accent and everything else takes the disabled gray.
        ///
        /// <b>Handling a click is not the same as opening something.</b> The speed controls take clicks all
        /// day and stay gray, because pressing one changes the speed and nothing else -- the widget only ever
        /// acts on itself. Answer true only where a click puts something new on screen, or the accent stops
        /// meaning anything.
        /// </summary>
        public virtual bool OpensMenu => false;

        /// <summary>The width the bar reserves for this widget, quantized and never shrinking.</summary>
        public float Width
        {
            get
            {
                if (broken)
                    return def?.minWidth ?? 40f;

                float measured;

                try
                {
                    measured = MeasureWidth();
                }
                catch (Exception ex)
                {
                    Fail("measure", ex);
                    return def?.minWidth ?? 40f;
                }

                float stepped = Mathf.Ceil(measured / WidthStep) * WidthStep;
                highWater = Mathf.Max(highWater, stepped);

                return Mathf.Max(highWater, def?.minWidth ?? 0f);
            }
        }

        /// <summary>
        /// Forgets the widest this widget has ever been, so the next frame measures from nothing.
        ///
        /// For a setting that changes what the widget shows. <see cref="highWater"/> never shrinks on its own,
        /// which is right for an hour ticking over and wrong for a player switching the clock from "14h" to
        /// "14:30" and back -- without this, the slot would keep the wider form's width until the next launch.
        /// </summary>
        public void ResetWidth()
        {
            highWater = 0f;
        }

        /// <summary>How much room this widget wants right now, before quantizing.</summary>
        protected abstract float MeasureWidth();

        /// <summary>Draws the widget into its slot.</summary>
        public abstract void Draw(Rect rect, UIColorPaletteDef palette);

        /// <summary>
        /// Draws with failures contained. Called by the bar; subclasses override <see cref="Draw"/>.
        /// </summary>
        public void DrawSafely(Rect rect, UIColorPaletteDef palette)
        {
            if (broken)
                return;

            try
            {
                Draw(rect, palette);
            }
            catch (Exception ex)
            {
                Fail("draw", ex);
            }
        }

        private bool ShouldShowSafely()
        {
            try
            {
                return ShouldShow;
            }
            catch (Exception ex)
            {
                Fail("decide whether to show", ex);
                return false;
            }
        }

        private void Fail(string what, Exception ex)
        {
            broken = true;

            // Keyed by def name, so a mod's widget and ours are counted separately -- and a report says which
            // widget it was without the reader having to work it out from the stack.
            UIGuard.Report($"ButtonBar.Widget.{def?.defName ?? GetType().Name}.{what}", ex,
                "This widget is switched off for the rest of the session. The rest of the bar is unaffected.");
        }

        // ---------------------------------------------------------------------------------------
        // Helpers for the common case: a line of text with a tooltip
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Width of a string at the font the readouts use.
        ///
        /// Sets the font before measuring rather than trusting the ambient one. Text.CalcSize reports for
        /// whatever font is current, and this is called during the bar's layout pass where that is whatever
        /// the last thing drawn happened to leave behind.
        /// </summary>
        protected static float TextWidth(string text)
        {
            if (text.NullOrEmpty())
                return 0f;

            GameFont previous = Text.Font;
            Text.Font = GameFont.Small;
            float width = Text.CalcSize(text).x;
            Text.Font = previous;

            return width;
        }

        /// <summary>Side of the icon a readout may carry, and the gap between it and the text.</summary>
        protected const float IconSize = 16f;

        protected const float IconGap = 5f;

        /// <summary>Width of a readout drawn as an icon followed by text.</summary>
        protected static float IconReadoutWidth(Texture2D icon, string text)
        {
            float width = TextWidth(text);

            if (icon != null)
                width += IconSize + IconGap;

            return width;
        }

        /// <summary>
        /// An icon and a line of text, together, centered in the slot as one group.
        ///
        /// Centered as a group rather than the icon being pinned left and the text centered in what remains:
        /// the slot is wider than the content whenever the high water mark is holding room for a longer
        /// reading, and a pinned icon would drift away from its own text as that happened.
        ///
        /// A null icon draws the text alone, so a widget whose glyph failed to resolve is still readable.
        /// </summary>
        protected static void DrawIconReadout(Rect rect, Texture2D icon, string text, Color color,
            string tooltip = null)
        {
            if (icon == null)
            {
                DrawReadout(rect, text, color, tooltip);
                return;
            }

            float textWidth = TextWidth(text);
            float total = IconSize + IconGap + textWidth;
            float x = rect.x + Mathf.Max(0f, (rect.width - total) * 0.5f);

            Color previousColor = GUI.color;
            GUI.color = color;

            GUI.DrawTexture(new Rect(x, rect.y + (rect.height - IconSize) * 0.5f, IconSize, IconSize),
                icon, ScaleMode.ScaleToFit);

            GUI.color = previousColor;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = color;

            Widgets.Label(new Rect(x + IconSize + IconGap, rect.y, textWidth + 2f, rect.height), text);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (!tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, (TipSignal) tooltip);
        }

        /// <summary>Centered single line of text, with an optional tooltip over the whole slot.</summary>
        protected static void DrawReadout(Rect rect, string text, Color color, string tooltip = null)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = color;

            Widgets.Label(rect, text);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (!tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, (TipSignal) tooltip);
        }
    }
}
