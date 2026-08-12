using Gideon.UIFramework.Components.Images;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIFramework.Controls
{
    /// <summary>How a button presents itself.</summary>
    public enum UIButtonType
    {
        /// <summary>A button: themed fill, state wash, optional border. The default.</summary>
        Classic,

        /// <summary>A bare texture that happens to be clickable, with no button surface behind it.</summary>
        Image,

        /// <summary>Underlined text in the palette's Info color, Accent under the cursor.</summary>
        Link
    }

    /// <summary>When a button's pulse effect runs.</summary>
    public enum UIButtonPulseEffectCondition
    {
        /// <summary>Continuously, for as long as the button is drawn.</summary>
        Always,

        /// <summary>Switched on by a click and off by the next one.</summary>
        Toggle,

        /// <summary>While the cursor is over the button.</summary>
        Hover,

        /// <summary>One full cycle when the button is first drawn, and not again for this control's lifetime.</summary>
        Once
    }

    /// <summary>
    /// A button with more presentation than a plain one: three visual forms, an optional border, and an
    /// optional slow color pulse used to draw the eye.
    ///
    /// An object rather than a static call, for the same reason as <see cref="UICardControl"/>: the pulse
    /// needs somewhere to keep its phase and its toggle between frames, and a static helper has nowhere to
    /// put per-button state without a dictionary keyed on something fragile like a rect.
    ///
    /// <code>
    /// // Held as a field on the window, not built inside the draw call.
    /// private readonly UIRichButtonControl save = new UIRichButtonControl { Label = "Save" };
    ///
    /// if (save.Draw(rect))
    ///     Save();
    /// </code>
    ///
    /// One consequence of holding state on the instance: a control created fresh with the window it lives in
    /// gets a fresh <see cref="UIButtonPulseEffectCondition.Once"/> pulse when that window reopens, which is
    /// the behavior that condition describes. A control cached beyond its window's lifetime will not, and
    /// should call <see cref="ResetPulse"/> when the window opens.
    /// </summary>
    public class UIRichButtonControl
    {
        /// <summary>Seconds to travel from dark to light. A full cycle is twice this.</summary>
        public const float PulseHalfCycleSeconds = 3f;

        public const float PulseCycleSeconds = PulseHalfCycleSeconds * 2f;

        // ---------------------------------------------------------------------------------------
        // Content
        // ---------------------------------------------------------------------------------------

        public string Label;

        /// <summary>Drawn on a Classic button beside its label, or as the whole of an Image button.</summary>
        public Texture Icon;

        public UIImageFit IconFit = UIImageFit.Contain;

        /// <summary>Null tints an Image button's texture from the palette. Set it to keep the art's own colors.</summary>
        public Color? IconTint;

        public string Tooltip;

        public GameFont Font = GameFont.Small;

        public TextAnchor Anchor = TextAnchor.MiddleCenter;

        /// <summary>Drawn dimmed, reports no clicks, and does not pulse.</summary>
        public bool Disabled;

        /// <summary>Played on a click. Null is silent.</summary>
        public SoundDef ClickSound = SoundDefOf.Click;

        // ---------------------------------------------------------------------------------------
        // Presentation
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Whether the themed border is drawn. Only meaningful on <see cref="UIButtonType.Classic"/> -- an
        /// image or a link has no surface for a border to outline.
        /// </summary>
        public bool HasBorder = true;

        public UIButtonType ButtonType = UIButtonType.Classic;

        // ---------------------------------------------------------------------------------------
        // Pulse
        // ---------------------------------------------------------------------------------------

        public bool ButtonEffectPulse;

        public UIButtonPulseEffectCondition ButtonEffectPulseCondition = UIButtonPulseEffectCondition.Always;

        /// <summary>The low end of the pulse. Null uses the palette's SurfaceSunken.</summary>
        public Color? ButtonEffectPulseDark;

        /// <summary>The high end of the pulse. Null uses the palette's SurfaceRaised.</summary>
        public Color? ButtonEffectPulseLight;

        /// <summary>
        /// A halo outside the button that brightens with the pulse and fades as it recedes.
        ///
        /// On by default, because a pulse confined to the button's own fill is easy to miss against a busy
        /// panel -- the glow is what makes it read as attention-seeking rather than as a slow recolor.
        /// </summary>
        public bool ButtonEffectPulseGlow = true;

        /// <summary>The halo's color. Null uses the pulse's light end, so the glow reads as that color bleeding out.</summary>
        public Color? ButtonEffectPulseGlowColor;

        /// <summary>How far the halo reaches beyond the button, in pixels. Zero disables it.</summary>
        public float ButtonEffectPulseGlowSize = 8f;

        /// <summary>Rings the halo is built from. More is smoother and costs a draw call each.</summary>
        private const int GlowLayers = 4;

        /// <summary>Alpha of the innermost ring at full brightness. The outer rings scale down from here.</summary>
        private const float GlowMaxAlpha = 0.5f;

        /// <summary>Set by a click while the condition is Toggle.</summary>
        private bool pulseToggled;

        /// <summary>
        /// When the current pulse began, or -1 when it is not running.
        ///
        /// Reset to -1 whenever the pulse stops, so every run starts from dark rather than resuming from
        /// wherever the wave happened to be. That matters most for Hover, where picking up mid-cycle would
        /// make the button flash to a random brightness the instant the cursor arrives.
        /// </summary>
        private float pulseOrigin = -1f;

        /// <summary>When the Once pulse began, or -1 before the first draw.</summary>
        private float onceOrigin = -1f;

        /// <summary>Puts the pulse back to its initial state, including re-arming a Once pulse.</summary>
        public void ResetPulse()
        {
            pulseToggled = false;
            pulseOrigin = -1f;
            onceOrigin = -1f;
        }

        /// <summary>Whether a Toggle pulse is currently on. Also settable, to drive it from outside.</summary>
        public bool PulseToggled
        {
            get => pulseToggled;
            set => pulseToggled = value;
        }

        // ---------------------------------------------------------------------------------------
        // Drawing
        // ---------------------------------------------------------------------------------------

        /// <summary>Draws the button and reports whether it was clicked.</summary>
        public bool Draw(Rect rect, UIColorPaletteDef palette = null)
        {
            palette = palette ?? UIColorPaletteDef.Active;

            bool over = !Disabled && Mouse.IsOver(rect);
            bool held = over && Input.GetMouseButton(0);

            // Armed on the first draw regardless of condition, so a Once pulse is measured from when the
            // button appeared rather than from when the control was constructed -- a control built in a
            // field initializer may exist long before its window is shown.
            if (onceOrigin < 0f)
                onceOrigin = Time.realtimeSinceStartup;

            bool pulsing = PulseRunning(over);
            UpdatePulseOrigin(pulsing);

            float phase = PulsePhase();
            Color pulse = PulseColor(palette, phase);

            // Behind everything, and outside the button's own rect, so the surface drawn next does not cover
            // the inner rings.
            if (pulsing && ButtonEffectPulseGlow)
                DrawGlow(rect, palette, phase);

            switch (ButtonType)
            {
                case UIButtonType.Image:
                    DrawImage(rect, palette, pulsing, pulse);
                    break;

                case UIButtonType.Link:
                    DrawLink(rect, palette, over, pulsing, pulse);
                    break;

                default:
                    DrawClassic(rect, palette, over, held, pulsing, pulse);
                    break;
            }

            if (!Tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, (TipSignal) Tooltip);

            if (Disabled)
            {
                // Consumes the click rather than letting it fall through to whatever is underneath, which is
                // what a disabled control is expected to do.
                Widgets.ButtonInvisible(rect);
                return false;
            }

            if (!Widgets.ButtonInvisible(rect))
                return false;

            if (ButtonEffectPulseCondition == UIButtonPulseEffectCondition.Toggle)
                pulseToggled = !pulseToggled;

            if (ClickSound != null)
                ClickSound.PlayOneShotOnCamera();

            return true;
        }

        private void DrawClassic(Rect rect, UIColorPaletteDef palette, bool over, bool held,
            bool pulsing, Color pulse)
        {
            if (pulsing)
            {
                // The pulse replaces the surface rather than washing over it, so the two colors the caller
                // gave are the colors that actually appear. Layering it over the normal fill would mean
                // neither end of the pulse matched what was asked for.
                Widgets.DrawBoxSolid(rect, pulse);

                if (HasBorder)
                {
                    Color previous = GUI.color;
                    GUI.color = over ? palette.BorderFocused : palette.Border;
                    Widgets.DrawBox(rect, 1);
                    GUI.color = previous;
                }
            }
            else
            {
                UIElementPainter.PaintButton(rect, palette, over, held, HasBorder);
            }

            Rect content = rect.ContractedBy(4f);

            if (Icon != null)
            {
                float size = Mathf.Min(content.height, 24f);
                Rect iconRect = Label.NullOrEmpty()
                    ? new Rect(content.center.x - size * 0.5f, content.center.y - size * 0.5f, size, size)
                    : new Rect(content.x, content.center.y - size * 0.5f, size, size);

                DrawTexture(iconRect, Icon, IconTint ?? Color.white);

                if (!Label.NullOrEmpty())
                    content = new Rect(iconRect.xMax + 6f, content.y, content.xMax - iconRect.xMax - 6f,
                        content.height);
            }

            if (Label.NullOrEmpty())
                return;

            DrawText(content, Label, Disabled ? palette.TextDisabled : palette.TextPrimary, Anchor);
        }

        private void DrawImage(Rect rect, UIColorPaletteDef palette, bool pulsing, Color pulse)
        {
            // The pulse sits behind the art as a plate. Tinting the texture itself would fight whatever
            // colors the art already has, and an image button is usually chosen precisely to keep those.
            if (pulsing)
                Widgets.DrawBoxSolid(rect, pulse);

            if (Icon == null)
                return;

            Color tint = IconTint ?? (Disabled ? palette.TextDisabled : Color.white);
            DrawTexture(rect, Icon, tint);
        }

        private void DrawLink(Rect rect, UIColorPaletteDef palette, bool over, bool pulsing, Color pulse)
        {
            if (Label.NullOrEmpty())
                return;

            // On a link the pulse drives the text, since there is no surface to carry it. A caller pulsing a
            // link will want to override the default colors, which are surface tones and read as muddy text.
            Color color = Disabled ? palette.TextDisabled
                : pulsing ? pulse
                : over ? palette.Accent
                : palette.Info;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = Font;
            Text.Anchor = Anchor;
            GUI.color = color;

            Widgets.Label(rect, Label);

            // The rule is drawn under the text's own width rather than the whole rect, so a centered or
            // right-aligned link is not underlined across empty space.
            Vector2 size = Text.CalcSize(Label);
            float width = Mathf.Min(size.x, rect.width);
            float x = Anchor == TextAnchor.MiddleCenter || Anchor == TextAnchor.UpperCenter
                      || Anchor == TextAnchor.LowerCenter
                ? rect.center.x - width * 0.5f
                : Anchor == TextAnchor.MiddleRight || Anchor == TextAnchor.UpperRight
                  || Anchor == TextAnchor.LowerRight
                    ? rect.xMax - width
                    : rect.x;

            float y = Mathf.Min(rect.yMax - 1f, rect.center.y + size.y * 0.5f);
            Widgets.DrawBoxSolid(new Rect(x, y, width, 1f), color);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        private void DrawTexture(Rect rect, Texture texture, Color tint)
        {
            Color previous = GUI.color;
            GUI.color = tint;

            GUI.DrawTexture(rect, texture,
                IconFit == UIImageFit.Cover ? ScaleMode.ScaleAndCrop
                : IconFit == UIImageFit.Stretch ? ScaleMode.StretchToFill
                : ScaleMode.ScaleToFit);

            GUI.color = previous;
        }

        private void DrawText(Rect rect, string text, Color color, TextAnchor anchor)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = Font;
            Text.Anchor = anchor;
            GUI.color = color;

            Widgets.Label(rect, text);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        // ---------------------------------------------------------------------------------------
        // Pulse math
        // ---------------------------------------------------------------------------------------

        private bool PulseRunning(bool over)
        {
            if (!ButtonEffectPulse || Disabled)
                return false;

            switch (ButtonEffectPulseCondition)
            {
                case UIButtonPulseEffectCondition.Toggle:
                    return pulseToggled;

                case UIButtonPulseEffectCondition.Hover:
                    return over;

                case UIButtonPulseEffectCondition.Once:
                    return Time.realtimeSinceStartup - onceOrigin < PulseCycleSeconds;

                default:
                    return true;
            }
        }

        private void UpdatePulseOrigin(bool pulsing)
        {
            if (!pulsing)
            {
                pulseOrigin = -1f;
                return;
            }

            if (pulseOrigin < 0f)
                pulseOrigin = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// The pulse's current color: a triangle wave between the two ends, three seconds each way.
        ///
        /// realtimeSinceStartup rather than Time.time, because Time.time does not advance while the game is
        /// paused and a paused game is exactly when a player is sitting in a dialog reading it.
        ///
        /// PingPong is linear on purpose. The brief specifies the timing, and easing the ends would keep the
        /// same period while changing where the color actually is at a given moment; SmoothStep on the phase
        /// is a one-line change if a softer turnaround reads better.
        /// </summary>
        private Color PulseColor(UIColorPaletteDef palette, float phase)
        {
            Color dark = ButtonEffectPulseDark ?? palette.SurfaceSunken;
            Color light = ButtonEffectPulseLight ?? palette.SurfaceRaised;

            return Color.Lerp(dark, light, phase);
        }

        /// <summary>0 at the dark end, 1 at the light end. Zero whenever the pulse is not running.</summary>
        private float PulsePhase()
        {
            if (pulseOrigin < 0f)
                return 0f;

            return Mathf.PingPong(
                (Time.realtimeSinceStartup - pulseOrigin) / PulseHalfCycleSeconds, 1f);
        }

        /// <summary>
        /// The halo: concentric outlines stepping outward from the button, each fainter than the one inside
        /// it, with the whole thing scaled by the pulse phase so it swells and fades with the color.
        ///
        /// Built from outlines rather than a soft-edged texture because that needs no art and no shader, and
        /// four rings at two pixels apart is indistinguishable from a real gradient at this size. Drawn
        /// outside-in so the brighter inner rings overlay the fainter outer ones.
        ///
        /// DrawBox rather than DrawBoxSolid: DrawBoxSolid resets GUI.color to white, which would discard the
        /// per-ring alpha this depends on.
        /// </summary>
        private void DrawGlow(Rect rect, UIColorPaletteDef palette, float phase)
        {
            if (phase <= 0f || ButtonEffectPulseGlowSize <= 0f)
                return;

            Color color = ButtonEffectPulseGlowColor
                          ?? ButtonEffectPulseLight
                          ?? palette.SurfaceRaised;

            int thickness = Mathf.Max(1, Mathf.RoundToInt(ButtonEffectPulseGlowSize / GlowLayers));
            Color previous = GUI.color;

            for (int layer = GlowLayers; layer >= 1; layer--)
            {
                float spread = ButtonEffectPulseGlowSize * layer / GlowLayers;
                float strength = 1f - (layer - 1f) / GlowLayers;

                GUI.color = new Color(color.r, color.g, color.b, phase * GlowMaxAlpha * strength);
                Widgets.DrawBox(rect.ContractedBy(-spread), thickness);
            }

            GUI.color = previous;
        }
    }
}
