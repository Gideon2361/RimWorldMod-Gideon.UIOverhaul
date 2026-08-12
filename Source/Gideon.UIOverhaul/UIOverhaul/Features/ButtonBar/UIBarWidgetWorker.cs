using System;
using Gideon.UIFramework.Defs;
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
            Log.Error($"[Gideon.UIOverhaul] Bar widget '{def?.defName ?? GetType().Name}' failed to {what} "
                      + $"and has been switched off for this session.\n{ex}");
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
