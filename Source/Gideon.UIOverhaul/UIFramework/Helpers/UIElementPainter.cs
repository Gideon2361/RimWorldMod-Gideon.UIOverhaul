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

            Widgets.DrawBoxSolid(box, palette.SurfaceSunken * ambient);

            Color mark = disabled ? palette.TextDisabled : palette.Accent;

            // DrawBox honors GUI.color, unlike DrawBoxSolid which resets it to white -- hence the assignment
            // here and the restore after, rather than one color set up front.
            GUI.color = (state == MultiCheckboxState.Off ? palette.Border : mark) * ambient;
            Widgets.DrawBox(box, Mathf.Max(1, Mathf.RoundToInt(box.width / 20f)));
            GUI.color = previous;

            // Proportional rather than a fixed 4px, because these are drawn at anything from a 20px cell in
            // our own grids to whatever size a caller of Widgets.CheckboxDraw asks for.
            float inset = box.width * 0.2f;

            if (state == MultiCheckboxState.On)
            {
                Widgets.DrawBoxSolid(box.ContractedBy(inset), mark * ambient);
            }
            else if (state == MultiCheckboxState.Partial)
            {
                // A bar rather than a smaller square: "some of these" has to be distinguishable from "yes" at
                // a glance, and two nested squares differing only in size are not.
                float thickness = Mathf.Max(2f, box.height * 0.14f);

                Widgets.DrawBoxSolid(new Rect(box.x + inset, box.center.y - thickness * 0.5f,
                    box.width - inset * 2f, thickness), mark * ambient);
            }

            GUI.color = previous;
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
        /// <summary>How much of the accent a hovered but unselected ring carries. See the notes on ring color.</summary>
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

            // Ring color, in precedence order. Selection outranks hover: a hovered button that is already
            // selected has nothing to promise, and the pointer being somewhere is worth less than the state
            // being set.
            //
            // Hover is the accent at part strength rather than at full. Selected is what full accent means
            // here, and hovering an unselected button at the same strength made the two indistinguishable
            // until the pointer moved away -- which defeats the point of coloring the selected one at all.
            Color ring;

            if (disabled)
                ring = palette.TextDisabled;
            else if (selected)
                ring = palette.Accent;
            else if (over)
                ring = new Color(palette.Accent.r, palette.Accent.g, palette.Accent.b,
                    palette.Accent.a * HoverRingStrength);
            else
                ring = palette.Border;

            // Both derived from the 24px these are actually drawn at, where each comes out at the intended
            // 2px. Scaled rather than fixed so a caller drawing one small still gets a visible ring.
            float thickness = Mathf.Max(1f, size / 12f);
            float gap = Mathf.Max(1f, size / 12f);

            Disc(circle, ring * ambient);
            Disc(circle.ContractedBy(thickness),
                (selected ? palette.AccentMuted : palette.WindowBackground) * ambient);

            // Absent entirely when not selected: the interior above is the whole of the unselected state.
            if (selected)
                Disc(circle.ContractedBy(thickness + gap), palette.WindowBackground * ambient);

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
