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
        internal static void PaintButton(Rect rect, UIColorPaletteDef palette, bool over, bool held,
            bool border = true)
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

            Widgets.DrawBoxSolid(rect, palette.SurfaceRaised);
            PaintStateWash(rect, palette, over, held);

            if (border)
            {
                GUI.color = over ? palette.BorderFocused : palette.Border;
                Widgets.DrawBox(rect, 1);
            }

            GUI.color = previous;
        }

        /// <summary>
        /// The hover or pressed wash. HoverOverlay and PressedOverlay carry alpha as part of their
        /// value: washes over whatever is beneath, not replacements for it. The palette decides how
        /// strong its own feedback is.
        /// </summary>
        private static void PaintStateWash(Rect rect, UIColorPaletteDef palette, bool over, bool held)
        {
            if (held)
                Widgets.DrawBoxSolid(rect, palette.PressedOverlay);
            else if (over)
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);
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

            // Every color is multiplied by the ambient one, which is how vanilla's checkbox textures behaved:
            // they were drawn at GUI.color, so a caller that dimmed a whole row dimmed its checkbox with it.
            // Painting at full strength instead would leave a bright box on a greyed-out row.
            Color ambient = GUI.color;
            Color previous = ambient;

            // The rect handed over may be square -- a grid cell, or a vanilla caller that still thinks in
            // 24 by 24 boxes. The switch is drawn to its own proportions inside whatever it is given and
            // centered there, rather than stretched to fill: a squashed track with an oval knob reads as a
            // rendering fault rather than as a control.
            Rect frame = SwitchFrame(box);

            Color track = TrackColor(state, palette, disabled);

            Widgets.DrawBoxSolid(frame, track * ambient);

            // DrawBox honors GUI.color, unlike DrawBoxSolid which resets it to white -- hence the assignment
            // here and the restore after, rather than one color set up front.
            GUI.color = BorderColor(state, track, palette, disabled) * ambient;
            Widgets.DrawBox(frame, 1);
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

            Widgets.DrawBoxSolid(knob, KnobColor(state, palette, disabled) * ambient);

            GUI.color = previous;
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
        /// <b>An unlit switch takes <c>Border</c> instead of a multiple of its own fill,</b> and that is a fix
        /// rather than a preference. The reference art multiplies both states -- the unchecked track #2F3337
        /// carries #1F2124 -- but the art was drawn against a mid-grey mockup background, and #1F2124 is darker
        /// than <c>PanelBackground</c> #1B1F23 is light. On a real panel the edge vanished, the track sat barely
        /// twenty levels above the surface behind it, and an off switch read as a pale knob floating in a smudge
        /// with no control around it. Nobody could see it was a switch, let alone which end the knob was parked at.
        ///
        /// <c>Border</c> is the existing role for exactly this -- the edge of a control -- so the unlit switch now
        /// reads as an empty slot with a defined outline, and no new palette entry was needed to say it. The lit
        /// states keep the derived rim: a bright track has all the contrast it needs, and <c>Border</c>'s blue
        /// would fight the amber of Partial.
        /// </summary>
        private static Color BorderColor(MultiCheckboxState state, Color track, UIColorPaletteDef palette,
            bool disabled)
        {
            if (!disabled && state != MultiCheckboxState.Off)
                return new Color(track.r * SwitchBorderFactor, track.g * SwitchBorderFactor,
                    track.b * SwitchBorderFactor, track.a);

            Color edge = palette.Border;

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
            if (disabled)
                return palette.SurfaceRaised;

            switch (state)
            {
                case MultiCheckboxState.On: return palette.Accent;
                case MultiCheckboxState.Partial: return palette.Warning;
                default: return palette.ControlBackgroundFaded;
            }
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
