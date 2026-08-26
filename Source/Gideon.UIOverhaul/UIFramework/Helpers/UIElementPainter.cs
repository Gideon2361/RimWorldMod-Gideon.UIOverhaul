using System;
using Gideon.UIFramework.Defs;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// The palette-driven backgrounds the patches in this folder paint.
    ///
    /// It exists because more than one vanilla method has to end up looking the same. A button and a
    /// selectable row in the options list are drawn by entirely unrelated code -- Widgets
    /// .DrawButtonGraphic against the button atlases, Widgets.DrawOptionUnselected against four static
    /// Color fields -- and if each patch carried its own copy of "our button look" the two would drift
    /// apart the first time one of them was adjusted.
    ///
    /// Not API, and not a control: it is shared between sibling patches, nothing more. Ask for a button
    /// control if our own windows need to draw one, and this moves to Controls.
    /// </summary>
    internal static class UIElementPainter
    {
        /// <summary>
        /// Set once any of our element painting has thrown, after which every patch in this folder
        /// hands its method back to vanilla for the rest of the session.
        ///
        /// Shared rather than one flag per patch: a failure here means palette drawing is broken, which
        /// is not a condition that applies to buttons but not to option rows. Reverting all of it keeps
        /// the UI internally consistent instead of half restyled.
        /// </summary>
        internal static bool Failed { get; private set; }

        /// <summary>
        /// A filled rounded rectangle, at the shared corner radius.
        ///
        /// <b>The one place rounding is drawn, so every surface that has it agrees.</b> Windows, buttons,
        /// checkboxes and the toggle track all call this; a square-cornered checkbox on a rounded window is the
        /// kind of inconsistency nobody can name but everybody sees.
        ///
        /// Falls back to a square fill if the shape could not be built, which is what <see cref="UIShapes"/>
        /// promises when its static constructor fails. Square corners are a cosmetic loss; drawing nothing would
        /// be an invisible control.
        /// </summary>
        /// <summary>
        /// Text the player can select and copy from, drawn in a color we choose.
        ///
        /// <b><c>GUI.color</c> does not survive a text field, which is what this exists for.</b>
        /// <c>Widgets.TextArea</c> draws through <c>GUI.TextArea</c> with a <c>GUIStyle</c>, and a style carries
        /// its own <c>textColor</c> for each state it can be in: normal, hover, active, focused, and the "on"
        /// variants. Unity paints with the state's color, so the moment the field takes focus -- which is to say
        /// the moment the player clicks it to select anything -- the text switched to the style's focused color
        /// and went black on our dark panel.
        ///
        /// So the color is set on every state of the style instead, and <c>GUI.color</c> is held at white so the
        /// two do not multiply into something darker again.
        ///
        /// <b>The style is copied from whatever vanilla is currently using rather than built from nothing,</b>
        /// so it keeps the font, padding and background of the real one, and follows <c>Text.Font</c> changing
        /// underneath it. It is rebuilt only when the font actually moves, because this draws every frame.
        /// </summary>
        internal static void SelectableText(Rect rect, string text, Color color)
        {
            GUIStyle basis = Text.CurTextAreaReadOnlyStyle;

            if (selectableText == null
                || selectableText.font != basis.font
                || selectableText.fontSize != basis.fontSize)
            {
                selectableText = new GUIStyle(basis);
            }

            Paint(selectableText.normal, color);
            Paint(selectableText.onNormal, color);
            Paint(selectableText.hover, color);
            Paint(selectableText.onHover, color);
            Paint(selectableText.active, color);
            Paint(selectableText.onActive, color);
            Paint(selectableText.focused, color);
            Paint(selectableText.onFocused, color);

            Color previous = GUI.color;
            GUI.color = Color.white;

            try
            {
                GUI.TextArea(rect, text ?? string.Empty, selectableText);
            }
            finally
            {
                GUI.color = previous;
            }
        }

        private static GUIStyle selectableText;

        private static void Paint(GUIStyleState state, Color color)
        {
            if (state != null)
                state.textColor = color;
        }

        internal static void FillRounded(Rect rect, Color color)
        {
            if (UIShapes.RoundedRect == null)
            {
                Widgets.DrawBoxSolid(rect, color);

                return;
            }

            Color previous = GUI.color;
            GUI.color = color;

            Widgets.DrawAtlas(rect, UIShapes.RoundedRect);

            GUI.color = previous;
        }

        /// <summary>
        /// A rounded outline, drawn as a filled rounded rect with a smaller one punched out of it in the color
        /// behind.
        ///
        /// IMGUI has no rounded stroke and no way to mask, so an outline is two fills -- the same trick
        /// <see cref="UIShapes.DiscCutout"/> exists for. The caller supplies what is behind because only the
        /// caller knows: a border around a button sits on the button's own fill, one around a window sits on
        /// whatever the window is over.
        /// </summary>
        internal static void OutlineRounded(Rect rect, Color color, Color inside, float thickness = 1f)
        {
            FillRounded(rect, color);
            FillRounded(rect.ContractedBy(thickness), inside);
        }

        /// <summary>
        /// One translucent color laid over an opaque one, as an opaque color.
        ///
        /// <b>This exists because <see cref="OutlineRounded"/> cannot take a translucent inside and the mistake
        /// is an easy one to make.</b> The outline is painted as two fills, so anything translucent handed to it
        /// as the inside is composited over the <i>border colour</i> rather than over the surface: passing a
        /// selection overlay produces a row filled almost solid with the accent, which is what happened in the
        /// operation picker and in two rows of the options window.
        ///
        /// The palette's overlay roles are all translucent by design, so the fix is to composite them here
        /// against whatever is actually behind the control and hand the result over opaque.
        /// </summary>
        /// <summary>
        /// A square outline: two filled rects, the inner one in the color behind.
        ///
        /// The square sibling of <see cref="OutlineRounded"/>, and it takes the inside color for the same reason
        /// -- it is painted as two fills, so anything translucent handed to it composites over the border rather
        /// than over the surface. Run it through <see cref="Composite"/> first if what you have is an overlay
        /// role.
        ///
        /// <b>Not everything wants a rounded corner.</b> A card in a dense grid reads as a cell when its corners
        /// are square and as a pill when they are not. Added 2026-08-23 for the research nodes, on Aaron's
        /// instruction.
        /// </summary>
        internal static void Outline(Rect rect, Color color, Color inside, float thickness = 1f)
        {
            Widgets.DrawBoxSolid(rect, color);
            Widgets.DrawBoxSolid(rect.ContractedBy(thickness), inside);
        }

        internal static Color Composite(Color background, Color overlay)
        {
            Color solid = new Color(overlay.r, overlay.g, overlay.b, 1f);

            return Color.Lerp(background, solid, Mathf.Clamp01(overlay.a));
        }

        /// <summary>
        /// Runs <paramref name="paint"/>, reporting once and disabling all of our element painting if it
        /// throws. Returns what a Harmony prefix should return: false when we painted, true to let the
        /// original method run.
        /// </summary>
        internal static bool Paint(Action paint, string what)
        {
            if (Failed)
                return true;

            try
            {
                paint();
                return false;
            }
            catch (Exception ex)
            {
                UIGuard.Report("Framework.Paint." + what, ex,
                    "Every restyled UI element -- buttons, checkboxes, radio buttons, option rows and window "
                    + "backgrounds -- goes back to its vanilla look for the rest of the session.");
                Failed = true;
                return true;
            }
        }

        /// <summary>
        /// A button background: fill, state wash, border.
        ///
        /// Leaves GUI.color as it found it, which is what vanilla's DrawAtlas does. The label is drawn
        /// afterwards by ButtonTextWorker, which does not set a color on this path -- it captures the
        /// ambient one and restores it -- so anything left behind here tints the text. Widgets
        /// .DrawBoxSolid resets GUI.color to white rather than to the previous value, which is exactly
        /// how that would happen.
        /// </summary>
        /// <param name="border">
        /// False to leave the outline off, keeping the fill and the state wash.
        ///
        /// For buttons that sit in a continuous strip rather than standing alone. Where buttons abut, each
        /// one's outline doubles up against its neighbor's into a heavy double rule, and the strip reads as
        /// a grid of boxes instead of as one bar. The main button bar separates its buttons with a gap and
        /// an accent rule instead.
        /// </param>
        /// <param name="rounded">
        /// False keeps square corners.
        ///
        /// <b>For the main button bar, and it is not a taste preference.</b> Those tabs sit edge to edge in a
        /// strip with an accent rule drawn along the top of the active one. Rounding a tab pulls its corners away
        /// from that rule and from its neighbours, leaving a lit arc floating above the tab -- which is exactly
        /// what it looked like. A strip of abutting controls has no corners to round; only the strip does.
        /// </param>
        /// <summary>
        /// <b>The hover border is the accent, matching <c>UIActionButtonControl</c> exactly.</b> It used to be
        /// <see cref="UIColorPaletteDef.BorderFocused"/>, which the palette defines as a <i>dimmed</i> accent for
        /// field borders -- correct for a text box, too quiet for a button, and different from what the mod's own
        /// button control does. Every icon button in the mod paints through here, so one line puts the button bar,
        /// the schedule brushes, the work tools and the pawn tools on the same hover as everything else. Changed
        /// 2026-08-25 on Aaron's report that buttons were not answering the pointer consistently.
        /// </summary>
        internal static void PaintButton(Rect rect, UIColorPaletteDef palette, bool over, bool held,
            bool border = true, bool rounded = true)
        {
            Color previous = GUI.color;

            if (palette.HasButtonTexture)
            {
                // A palette that supplies its own artwork gets no flat fill and no border drawn over
                // it -- the image is the button. DrawAtlas is vanilla's own 9-slice, so an atlas
                // authored for RimWorld's buttons behaves here exactly as it does there.
                GUI.color = Color.white;
                Widgets.DrawAtlas(rect, palette.ButtonTexture(over, held));
                GUI.color = previous;

                if (palette.buttonTextureUsesStateWash)
                    PaintStateWash(rect, palette, over, held);

                GUI.color = previous;
                return;
            }

            if (!rounded)
            {
                Widgets.DrawBoxSolid(rect, palette.SurfaceRaised);
                PaintStateWash(rect, palette, over, held, false);

                if (border)
                {
                    GUI.color = over ? palette.Accent : palette.Border;
                    Widgets.DrawBox(rect, 1);
                }

                GUI.color = previous;

                return;
            }

            // The border is drawn as a rounded outline rather than with DrawBox, which only knows how to draw
            // square corners and would leave four hard ticks poking past the curve.
            if (border)
                OutlineRounded(rect, over ? palette.Accent : palette.Border, palette.SurfaceRaised);
            else
                FillRounded(rect, palette.SurfaceRaised);

            PaintStateWash(rect, palette, over, held);

            GUI.color = previous;
        }

        /// <summary>
        /// The hover or pressed wash. HoverOverlay and PressedOverlay carry alpha as part of their
        /// value: washes over whatever is beneath, not replacements for it. The palette decides how
        /// strong its own feedback is.
        /// </summary>
        private static void PaintStateWash(Rect rect, UIColorPaletteDef palette, bool over, bool held,
            bool rounded = true)
        {
            Color wash = held ? palette.PressedOverlay : over ? palette.HoverOverlay : default(Color);

            if (!held && !over)
                return;

            // Shaped to match the fill underneath. A square wash over a rounded button paints its own corners
            // back on, which looks worse than no wash at all; a rounded wash over a square one leaves four
            // unlit notches, which is the fault that showed up on the tab bar.
            if (rounded)
                FillRounded(rect, wash);
            else
                Widgets.DrawBoxSolid(rect, wash);
        }

        /// <summary>
        /// A selectable row in an option list -- the mod list in Options, the category rows beside it,
        /// scenario and storyteller selection, and the rest.
        ///
        /// Painted with the button fill on purpose. These rows read as buttons and sit next to real
        /// ones, so giving them their own color is what made the options screen look untouched while
        /// every actual button had been restyled.
        ///
        /// No hover wash here: the caller, Widgets.DrawOptionBackground, follows this with
        /// DrawHighlightIfMouseover, and adding our own would light the row up twice.
        ///
        /// Ends on white rather than restoring the previous color, matching the postcondition of the
        /// vanilla methods this replaces -- both of them finish with GUI.color = Color.white, so callers
        /// are entitled to assume it.
        /// </summary>
        internal static void PaintOption(Rect rect, UIColorPaletteDef palette, bool selected)
        {
            Widgets.DrawBoxSolid(rect, palette.SurfaceRaised);

            if (selected)
                Widgets.DrawBoxSolid(rect, palette.SelectionOverlay);

            GUI.color = selected ? palette.BorderFocused : palette.Border;
            Widgets.DrawBox(rect, 1);

            GUI.color = Color.white;
        }

        /// <summary>
        /// A checkbox box, in every state vanilla has one for.
        ///
        /// The single definition of what a checkbox looks like in this mod. Three unrelated callers share it:
        /// <c>UICheckboxControl</c> for our own windows, the prefix on <c>Widgets.CheckboxDraw</c> for every
        /// vanilla and modded boolean checkbox, and the postfix on <c>Widgets.CheckboxMulti</c> for the
        /// tri-state ones in thing filter trees. Any change to the look happens here and reaches all three.
        ///
        /// Vanilla's <c>MultiCheckboxState</c> is the state parameter rather than an enum of our own, so the
        /// two vanilla seams need no translation and there is one vocabulary for a checkbox state rather than
        /// two that have to be kept in step.
        /// </summary>
        /// <param name="disabled">
        /// Drawn in the disabled text color rather than the accent. Not a separate wash: a checkbox is small
        /// enough that dimming the mark is the only legible way to say it cannot be changed.
        /// </param>
        internal static void PaintCheckbox(Rect box, MultiCheckboxState state, UIColorPaletteDef palette,
            bool disabled)
        {
            palette = palette ?? UIColorPaletteDef.Active;

            // <b>The ambient color tints nothing here but transparency.</b> This used to multiply every channel by
            // GUI.color, imitating vanilla's checkbox textures, which were drawn at whatever the ambient color
            // happened to be. The cost was that a switch never showed its own color: a caller that tinted a row for
            // any reason of its own restated that tint on the control, and the state colors stopped being
            // recognizable. Partial is the case that showed it, since #CCA633 shifted far enough under a tint to
            // read as green rather than as the warning amber it is.
            //
            // Alpha still comes through, because that is a different question. A window mid-fade lowers the ambient
            // alpha and everything in it has to fade together, or the switches float over a dissolving panel. Hue
            // and value do not work that way: those say what the control means, and they are not the caller's to
            // change.
            //
            // Nothing is lost in the dimming case that motivated the multiply. A switch that cannot be used is told
            // so through the disabled parameter, which picks its own colors for exactly that, rather than inheriting
            // a shade and hoping it reads as unavailable.
            Color ambient = GUI.color;
            Color previous = ambient;

            // The rect handed over may be square -- a grid cell, or a vanilla caller that still thinks in
            // 24 by 24 boxes. The switch is drawn to its own proportions inside whatever it is given and
            // centered there, rather than stretched to fill: a squashed track with an oval knob reads as a
            // rendering fault rather than as a control.
            Rect frame = SwitchFrame(box);

            Color track = TrackColor(state, palette, disabled);

            // Rounded, at the shared radius. On a track this short the radius clamps to half its height, so it
            // comes out very close to a capsule -- which is what a switch should look like anyway, and it means
            // the switch, the buttons and the window it sits in are all rounded by the same rule.
            OutlineRounded(frame, Opacity(BorderColor(state, track, palette, disabled), ambient),
                Opacity(track, ambient));

            GUI.color = previous;

            // A pixel of track shows all the way around the knob, which is what stops it reading as a block
            // that has slid off the end of its own control.
            Rect interior = frame.ContractedBy(1f);
            float knobSize = Mathf.Max(2f, interior.height - 2f);
            float knobX;

            switch (state)
            {
                case MultiCheckboxState.On:
                    knobX = interior.xMax - 1f - knobSize;
                    break;

                case MultiCheckboxState.Partial:
                    // Centered, which is most of why a switch suits a tri-state: "some of these" becomes a
                    // position rather than a different shape, and position reads from across a row.
                    knobX = interior.center.x - knobSize * 0.5f;
                    break;

                default:
                    knobX = interior.x + 1f;
                    break;
            }

            Rect knob = new Rect(knobX, interior.center.y - knobSize * 0.5f, knobSize, knobSize);

            // The knob is square, so the shared radius clamps to half its size and it comes out a disc. That is
            // the right answer for a switch and it costs no special case.
            FillRounded(knob, Opacity(KnobColor(state, palette, disabled), ambient));

            GUI.color = previous;
        }

        /// <summary>
        /// A color at its own hue and value, carrying only the ambient alpha.
        ///
        /// The seam between "this control means something" and "this whole panel is fading", which are the two
        /// things the old multiply had conflated.
        /// </summary>
        private static Color Opacity(Color color, Color ambient)
        {
            return new Color(color.r, color.g, color.b, color.a * ambient.a);
        }

        /// <summary>
        /// How much darker a lit track's border is than the track itself.
        ///
        /// Measured off the reference art, where the checked track #73BFFF carries a #4B7CA6 edge: the fill at
        /// 0.65. Deriving it rather than naming it means a palette that changes the accent gets a matching edge
        /// for free. Applies to the lit states only -- see <see cref="BorderColor"/> for why the unlit one cannot
        /// use it.
        /// </summary>
        private const float SwitchBorderFactor = 0.65f;

        /// <summary>How much of the border is left on a switch that cannot be changed.</summary>
        private const float DisabledBorderAlpha = 0.45f;

        /// <summary>
        /// The rim around the track.
        ///
        /// <b>An unlit switch takes <c>SurfaceSunken</c> rather than a multiple of its own fill.</b> Multiplying
        /// works for the lit states, where a bright track has contrast to spare, but the unlit track is now
        /// <c>Border</c> #2F3337 itself: 0.65 of that is #1F2124, which is darker than the #1B1F23 panel is light,
        /// so the edge disappeared into the surface behind it and the switch read as a knob floating in a smudge.
        ///
        /// <c>SurfaceSunken</c> #0E1013 is the darkest surface in the ramp and reads as a recess, which is exactly
        /// what an empty slot should look like. It also cannot collide with the track, which naming <c>Border</c>
        /// here now would: that became the track's own color when the unlit state moved to it.
        /// </summary>
        private static Color BorderColor(MultiCheckboxState state, Color track, UIColorPaletteDef palette,
            bool disabled)
        {
            if (!disabled && state != MultiCheckboxState.Off)
                return new Color(track.r * SwitchBorderFactor, track.g * SwitchBorderFactor,
                    track.b * SwitchBorderFactor, track.a);

            Color edge = palette.SurfaceSunken;

            // Dimmed rather than dropped. A disabled switch still has to look like a switch, or it becomes the
            // same unreadable smudge for a different reason.
            if (disabled)
                edge.a *= DisabledBorderAlpha;

            return edge;
        }

        /// <summary>
        /// The switch's proportions, taken from the reference art: 40 wide by 20 tall.
        ///
        /// Two to one, from the original textures. The PNGs measured while working this out were 38 wide, but
        /// that was the round trip through Paint.NET losing two columns, not the authored size.
        /// </summary>
        private const float SwitchAspect = 2f;

        /// <summary>
        /// The largest switch of the right shape that fits a rect, centered in it.
        ///
        /// Bounded on both axes rather than scaled from the height alone, because callers hand this everything
        /// from a tall narrow grid cell to a wide row.
        /// </summary>
        internal static Rect SwitchFrame(Rect box)
        {
            float height = Mathf.Min(box.height, box.width / SwitchAspect);
            float width = height * SwitchAspect;

            return new Rect(box.center.x - width * 0.5f, box.center.y - height * 0.5f, width, height);
        }

        /// <summary>
        /// The track color for a state.
        ///
        /// The lit states are existing palette roles and were taken straight from the reference art, which was
        /// authored from the palette: the checked track is <c>Accent</c> to the byte, the knobs are
        /// <c>SurfaceSunken</c> and <c>TextSecondary</c>, and Partial takes the amber of <c>Warning</c>.
        ///
        /// <b>The unlit track is the one place the art could not be followed.</b> It was authored as
        /// <c>SurfaceRaised</c>, on the assumption that a raised surface sits above the panel. In this mod's own
        /// dark theme it does not: that palette sets <c>surfaceRaised</c> to #15191D against a #1B1F23 panel, so
        /// the switch was drawn <i>darker</i> than the surface behind it and had no visible extent at all. The
        /// mistake was reusing a chrome color for a control body -- see
        /// <see cref="UIColorRole.ControlBackgroundFaded"/>, which is the role that now means this and which a
        /// theme can tune without disturbing its cards and headers.
        ///
        /// Disabled deliberately keeps the raised surface, so a switch that cannot be changed stays quieter than
        /// one that is merely off.
        /// </summary>
        private static Color TrackColor(MultiCheckboxState state, UIColorPaletteDef palette, bool disabled)
        {
            // <b>Border, not SurfaceRaised and not ControlBackgroundFaded, and both of those were tried.</b>
            //
            // SurfaceRaised came first and vanished: this ramp's raised surface is #15191D, the same value as the
            // window behind it, so an unlit switch had no visible extent at all. ControlBackgroundFaded replaced it
            // in 14089 and fixed the visibility by overshooting -- #434A53 is a light blue grey, near enough the
            // accent's own hue that on and off became two brightnesses of one blue rather than lit and unlit. A
            // whole column of disallowed items read as allowed.
            //
            // Border #2F3337 is the neutral in the middle: a clear step up from the #1B1F23 panel, so the slot is
            // plainly there, and no hue of its own, so nothing about it suggests the switch is doing something. It
            // is also the role whose job this is -- the extent of a control -- and it is the value the reference
            // art used for the unchecked track before the palette moved that number onto Border.
            if (disabled || state == MultiCheckboxState.Off)
                return palette.Border;

            return state == MultiCheckboxState.On ? palette.Accent : palette.Warning;
        }

        /// <summary>
        /// The knob color for a state: dark on a lit track, light on an unlit one.
        ///
        /// The knob is always the high-contrast element against whatever it sits on, which is what keeps its
        /// position readable at a glance -- and position is the entire signal a switch carries.
        /// </summary>
        private static Color KnobColor(MultiCheckboxState state, UIColorPaletteDef palette, bool disabled)
        {
            if (disabled)
                return palette.TextDisabled;

            return state == MultiCheckboxState.Off ? palette.TextSecondary : palette.SurfaceSunken;
        }

        /// <summary>
        /// A radio button: a hollow outer circle, and a solid one inside it when selected.
        ///
        /// The single definition of the look, as <see cref="PaintCheckbox"/> is for checkboxes. Shared by
        /// <c>UIRadioButtonControl</c> and by the prefix on <c>Widgets.RadioButtonDraw</c>, so a vanilla radio
        /// button and one of ours are the same pixels.
        ///
        /// Drawn as three concentric discs rather than as a stroked circle, because IMGUI cannot stroke: the
        /// outer disc is the ring color, the next one covers all but a rim of it, and the innermost is the
        /// mark. Each is a tint of one generated texture -- see <see cref="UIShapes.Disc"/>.
        /// </summary>
        /// <summary>How far a hovered but unselected rim is lifted toward the accent. See the notes on rim color.</summary>
        private const float HoverRingStrength = 0.55f;

        /// <param name="over">
        /// Whether the pointer is over the thing that would be clicked, which is not always this circle. A
        /// labeled radio button's whole row is clickable, so the caller decides what counts as over rather
        /// than this testing the circle itself.
        /// </param>
        internal static void PaintRadioButton(Rect box, bool selected, UIColorPaletteDef palette,
            bool disabled, bool over)
        {
            palette = palette ?? UIColorPaletteDef.Active;

            // Square and centered: the rect a caller hands over may be a row-height cell, and an oval radio
            // button is worse than a small one.
            float size = Mathf.Min(box.width, box.height);
            Rect circle = new Rect(box.center.x - size * 0.5f, box.center.y - size * 0.5f, size, size);

            // As with the checkbox, every color is multiplied by the ambient one, because vanilla's radio
            // textures were drawn at GUI.color and greyed with it.
            Color ambient = GUI.color;
            Color previous = ambient;

            // The same colors and the same proportions as the toggle switch, on a circle instead of a track.
            // A radio button has to stay round -- round means one of several, square means independent, and
            // that distinction is worth more than shape consistency -- but nothing else about it needs to
            // differ, and everything else about it used to. It was drawn from Border, AccentMuted and
            // WindowBackground, none of which the switch uses, so the two read as parts of different themes.
            // Unselected takes ControlBackgroundFaded, the same role as an off switch, and for the same reason:
            // this used to be SurfaceRaised, which in this mod's own dark theme is darker than the panel behind
            // it, leaving an unselected radio button as invisible as an off toggle was. The two controls are
            // deliberately kept on one role so a theme cannot fix one and leave the other unreadable.
            Color fill = selected && !disabled ? palette.Accent : palette.ControlBackgroundFaded;

            // The switch's own rim rule rather than a copy of it, so the unselected radio button picks up the
            // Border edge an off switch gets and the two cannot drift apart again. Selected still derives from
            // the accent fill, which is what the reference art does.
            Color rim = BorderColor(selected ? MultiCheckboxState.On : MultiCheckboxState.Off, fill, palette,
                disabled);

            // Hover lifts the rim toward the accent rather than recoloring the whole control. Selection
            // outranks it: a button that is already selected has nothing to promise, and the pointer being
            // somewhere is worth less than the state being set.
            if (over && !selected && !disabled)
                rim = Color.Lerp(rim, palette.Accent, HoverRingStrength);

            Color mark = disabled ? palette.TextDisabled : palette.SurfaceSunken;

            // The switch's own proportions, read off the reference art and expressed as fractions of the
            // control's height so they carry across to a circle: a 20px switch is 1px of border, 2px of track,
            // a 14px knob, then track and border again. On a disc that is a rim, a gap, and a mark of 70%.
            float border = Mathf.Max(1f, size * 0.05f);
            float gap = Mathf.Max(1f, size * 0.10f);

            Disc(circle, rim * ambient);
            Disc(circle.ContractedBy(border), fill * ambient);

            // Absent entirely when not selected: the fill above is the whole of the unselected state, the same
            // way an unlit switch is a bare track.
            if (selected)
                Disc(circle.ContractedBy(border + gap), mark * ambient);

            GUI.color = previous;
        }

        /// <summary>
        /// A striped wash across a rect: the marking this mod uses to flag a row or a cell as needing
        /// attention, as the grow-zone plant cards do for a hazard.
        ///
        /// The color carries its own alpha -- it is a wash over whatever is beneath, not a fill -- so a caller
        /// passes something like <c>Danger</c> at a fifth strength rather than a solid role.
        ///
        /// Tiled rather than stretched, at a constant pitch. A stretched pattern puts eight fat stripes on a
        /// wide row and one on a narrow cell, and the two stop reading as the same marking.
        ///
        /// The pattern is also anchored to where the rect is, not to the rect itself. Starting the tex coords
        /// at zero restarts the pattern at every rect's own left edge, so two washed cells side by side broke
        /// the diagonal at the boundary between them. Offsetting by the position instead makes every rect a
        /// window onto one continuous field: adjacent cells continue each other, and a cell wash lines up with
        /// the row wash underneath it.
        /// </summary>
        internal static void PaintStripeWash(Rect rect, Color wash)
        {
            if (rect.width <= 0f || rect.height <= 0f)
                return;

            Color previous = GUI.color;

            const float pitch = UIShapes.StripePitch;

            // V runs the opposite way to screen Y -- DrawTextureWithTexCoords maps the bottom of the rect to
            // the lower coordinate -- so the vertical offset is negated and taken from yMax. Getting that
            // backwards is what makes vertically adjacent rows step instead of continue.
            GUI.color = wash;
            GUI.DrawTextureWithTexCoords(rect, UIShapes.Stripes,
                new Rect(rect.x / pitch, -rect.yMax / pitch, rect.width / pitch, rect.height / pitch));

            GUI.color = previous;
        }

        private static void Disc(Rect rect, Color color)
        {
            if (rect.width <= 0f || rect.height <= 0f)
                return;

            GUI.color = color;
            GUI.DrawTexture(rect, UIShapes.Disc);
        }
    }
}
