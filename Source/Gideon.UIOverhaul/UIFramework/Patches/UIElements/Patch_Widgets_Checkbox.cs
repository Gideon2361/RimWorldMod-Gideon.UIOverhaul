using System;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIFramework.Patches.UIElements
{
    /// <summary>
    /// Draws RimWorld's labelled checkboxes as toggle switches, with the control before the label.
    ///
    /// Vanilla writes the label from the left edge and pins the box to the right, so a column of them is a
    /// ragged left edge of text with the boxes stranded past the longest line. Reading a column of settings
    /// means matching each box to its label across that gap, and the wider the panel the further the eye has
    /// to travel. Control first puts every one of them in a single column at a fixed distance from its text.
    ///
    /// The control itself is painted by <see cref="UIElementPainter.PaintCheckbox"/>, which is also what
    /// <c>UICheckboxControl</c> uses, so a vanilla checkbox and one of ours cannot come to look different.
    ///
    /// <b>Replaced rather than nudged, because the layout is four lines with no seam between them.</b>
    /// <c>CheckboxLabeled</c> sets the anchor, optionally shrinks the rect to hug the text, draws the label,
    /// takes the click, and draws the box. There is nothing to patch that would move the box without owning
    /// the rest, so the whole method is reimplemented -- faithfully, including the parts that are easy to lose.
    ///
    /// The pieces kept deliberately:
    /// <list type="bullet">
    /// <item>The click goes through <c>ToggleInvisibleDraggable</c> over the whole rect, which is what makes a
    /// checkbox draggable across a column and what plays the toggle sounds. Reimplementing it as a
    /// <c>ButtonInvisible</c> would silently drop paint-dragging.</item>
    /// <item><c>disabled</c> still takes no click at all, rather than taking one and ignoring it.</item>
    /// <item>The anchor is restored in a finally, since this sets it before anything that can throw.</item>
    /// <item><c>placeCheckboxNearText</c> still shrinks the rect to the text, and the arithmetic is adjusted to
    /// match: vanilla reserves the box on the right, this reserves it on the left, and the total is the same.</item>
    /// </list>
    ///
    /// This reaches every labelled checkbox in the game, <c>Listing_Standard.CheckboxLabeled</c> included, and
    /// that is the point -- a mod's settings page shown in our window should not have its checkboxes laid out
    /// the other way round from ours.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.CheckboxLabeled))]
    public static class Patch_Widgets_CheckboxLabeled
    {
        /// <summary>
        /// The slot reserved for the control, which is a switch rather than a box.
        ///
        /// Vanilla reserves 24 square. A switch of the reference art's proportions is wider than that, so the
        /// slot is widened and the label starts after it. The height stays vanilla's, so a row is exactly as
        /// tall as it always was and nothing above or below it moves.
        /// </summary>
        private const float BoxSize = 24f;

        private const float SlotWidth = UICheckboxControl.BoxWidth;


        /// <summary>
        /// Space between the box and its label.
        ///
        /// The same 10 vanilla adds in its <c>placeCheckboxNearText</c> measurement, so a rect sized by that
        /// path fits the text exactly rather than by luck.
        /// </summary>
        internal const float Gap = 10f;

        public static bool Prefix(Rect rect, string label, ref bool checkOn, bool disabled,
            Texture2D texChecked, Texture2D texUnchecked, bool placeCheckboxNearText, bool paintable)
        {
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Anchor = TextAnchor.MiddleLeft;

                // <b>The switch is one size everywhere and does not negotiate.</b>
                //
                // Two earlier attempts traded its width away to win room for the label -- first a flat cap at
                // half the row, then measuring the text and shrinking to fit. Both produced switches of
                // different sizes on different rows, which is worse than a shortened label: a control that
                // changes size between one row and the next reads as a rendering fault, while a label ending
                // in an ellipsis reads as a label too long for its row, which is what it is.
                //
                // Clamped to the row only so nothing is ever drawn outside the rect the caller owns. A row that
                // cannot hold 40px was not going to hold a checkbox and a label either way.
                float slot = Mathf.Min(SlotWidth, rect.width);

                if (placeCheckboxNearText)
                {
                    // <b>A gap's worth of slack past the text, and the reason is arithmetic rather than taste.</b>
                    //
                    // Vanilla shrinks to text + box + gap here and then lays its label out subtracting only the
                    // box, so the gap survives as the label's breathing room. Subtracting the gap as well --
                    // which is what this did, because the switch sits where the gap used to be -- handed the
                    // label a rect exactly as wide as its own text, and LabelEllipses trims at that boundary.
                    // That is the whole of why "Show messages" became "Show mess..." on the History tab: the row
                    // was 200 wide and had room for all of it.
                    //
                    // Measured with wrapping off because that is how it is about to be drawn; CalcSize answers
                    // differently when wrapping is on.
                    Text.WordWrap = false;
                    rect.width = Mathf.Min(rect.width, Text.CalcSize(label).x + slot + Gap * 2f);
                }

                // <b>Wrapping only where two lines actually fit.</b> Some of vanilla's rects are deliberately
                // tall enough for a wrapped label, so this does not forbid wrapping outright -- it forbids it
                // where the second line would be drawn outside the row. That is what produced the clipped
                // "DEV: / Show all" with its first line cut off against the top edge: a two-line label centered
                // in a one-line rect loses half of itself at each end.
                Text.WordWrap = rect.height >= Text.LineHeight * 1.8f;

                // The gap closes up when the label is short of room. It is the only thing here that can be given:
                // the row is the caller's and the switch is a fixed size, so see UICheckboxControl.FitLabel for
                // what that leaves. Only for single-line rows -- a row tall enough to wrap has a second line to
                // use instead, which is a better answer than a tighter gap.
                float gap = Text.WordWrap
                    ? Gap
                    : UICheckboxControl.FitLabel(label, Mathf.Max(0f, rect.width - slot), Gap);

                Rect labelRect = rect;
                labelRect.xMin += slot + gap;

                // <b>Ellipsized only when the text genuinely does not fit, and that condition is the fix.</b>
                //
                // Widgets.LabelEllipses defers to Text.ClampTextWithEllipsis, which keeps the text only while it
                // fits in width *minus 13* -- room reserved for the "..." it may be about to add. So it shortens
                // any label that comes within 13px of its rect, and once it starts it strips characters until the
                // text plus an ellipsis fits, which costs several more. That is the whole of why "Show messages"
                // arrived as "Show mess...": the row was 200 wide, the label had been given its own width plus 10,
                // and 10 is less than 13.
                //
                // Two earlier attempts widened that slack, first to 0 and then to 10, both chasing a threshold
                // neither of them knew the value of. Asking whether the text fits removes the guess: vanilla
                // reaches for a plain Label here and never reserves anything, so a label with room draws in full
                // and only one that is really too long pays for the ellipsis. It also repairs every fixed-width
                // caller, not just the ones that size themselves to their text.
                if (Text.WordWrap || Text.CalcSize(label).x <= labelRect.width)
                    Widgets.Label(labelRect, label);
                else
                    Widgets.LabelEllipses(labelRect, label);

                // Over the whole row, as vanilla does, so the label is clickable and dragging still paints down
                // a column.
                if (!disabled)
                    Widgets.ToggleInvisibleDraggable(rect, ref checkOn, true, paintable);

                // Painted rather than handed to Widgets.CheckboxDraw, so a vanilla checkbox and one of ours are
                // the same pixels. texChecked and texUnchecked are deliberately ignored: they are vanilla's box
                // artwork, and there is nowhere in a switch for a tick mark to go. A caller passing them wanted
                // a different-looking checkbox, and gets a switch like every other one instead of a lone box.
                //
                // Height clamped to the row as well: a caller with a rect shorter than 24 would otherwise get a
                // negative offset and a switch drawn above its own label.
                float height = Mathf.Min(BoxSize, rect.height);

                UIElementPainter.PaintCheckbox(
                    new Rect(rect.x, rect.y + (rect.height - height) / 2f, slot, height),
                    checkOn ? MultiCheckboxState.On : MultiCheckboxState.Off, null, disabled);

                return false;
            }
            catch (Exception ex)
            {
                UIGuard.Report("Framework.CheckboxLabeled", ex,
                    "That checkbox is drawn the vanilla way round, with its box after the label.");

                // Letting the original run means the row may be drawn twice for one frame. That is a great deal
                // better than a settings page with a checkbox missing from it.
                return true;
            }
            finally
            {
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWrap;
            }
        }
    }

    /// <summary>
    /// Draws RimWorld's unlabelled checkboxes as switches too, so the game does not show two kinds.
    ///
    /// <b>One prefix covers three entry points.</b> Both <c>Widgets.Checkbox</c> overloads funnel through
    /// <c>CheckboxDraw</c> for their drawing and keep their own click handling, so replacing the draw restyles
    /// them without this having to know anything about clicks, dragging or sounds. That is the whole reason to
    /// patch here rather than at <c>Checkbox</c>: the parts easiest to get wrong are not in this method.
    ///
    /// <b>The switch is fitted inside the square, not stretched to fill it.</b> Callers pass a size and expect
    /// a box of exactly that size -- these are thing-filter rows, assign-tab cells, architect options, all laid
    /// out tightly around a 24px square. <see cref="UIElementPainter.SwitchFrame"/> puts the largest correctly
    /// proportioned switch inside that square and centers it, so a 24px slot yields a 24 by 12 switch and
    /// nothing overlaps a neighbour. It is visibly smaller than the switch on a labelled row, which is the
    /// price of not overflowing a slot whose surroundings we cannot see.
    ///
    /// <b>What this does not cover:</b> <c>Widgets.CheckboxMulti</c>, the tri-state control. It draws and takes
    /// its click in the same call, so it could not be restyled by replacing a draw method and needed a patch of
    /// its own -- see <see cref="Patch_Widgets_CheckboxMulti"/> below.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.CheckboxDraw))]
    public static class Patch_Widgets_CheckboxDraw
    {
        public static bool Prefix(float x, float y, bool active, bool disabled, float size)
        {
            try
            {
                // Ambient GUI.color is honored by PaintCheckbox, so the InactiveColor that Widgets.Checkbox
                // sets before calling this still dims the result -- on top of the disabled colors the painter
                // picks. Dimmer than a row that only greys once, and correct for a control that is genuinely
                // unavailable.
                UIElementPainter.PaintCheckbox(new Rect(x, y, size, size),
                    active ? MultiCheckboxState.On : MultiCheckboxState.Off, null, disabled);

                return false;
            }
            catch (Exception ex)
            {
                UIGuard.Report("Framework.CheckboxDraw", ex,
                    "That checkbox is drawn with RimWorld's own box artwork.");

                return true;
            }
        }
    }

    /// <summary>
    /// Draws the tri-state checkbox as a switch, with the knob mid-travel for Partial.
    ///
    /// This is the control the Partial state was drawn for. Thing filter trees are where a player actually meets
    /// "some of these", and until now they were the one place still showing vanilla's boxes -- so the state was
    /// implemented and unreachable.
    ///
    /// <b>The click is vanilla's, not a reimplementation.</b> <c>CheckboxMulti</c> draws through
    /// <c>ButtonImageDraggable</c>, which paints the texture and returns the drag result in one call, so there
    /// is no seam that separates the two. Rather than rewrite the drag handling -- which is the mistake that
    /// cost the pause key -- this calls the same method with <c>BaseContent.ClearTex</c>: nothing is painted,
    /// and <c>ButtonInvisibleDraggable</c> behind it still tracks the press and the drag through the private
    /// state it owns. The switch is then painted in its place.
    ///
    /// <b>Everything after that is copied deliberately, not approximated.</b> The state flip, the paint-drag
    /// continuation, which sound plays and the rule that an unchanged state returns the original are vanilla's
    /// own, in vanilla's order. Two private statics carry paint-dragging across frames and are reached by
    /// reference rather than duplicated, so a drag started on one row still paints the next.
    ///
    /// The hover tint is reproduced as well. <c>ButtonImageDraggable</c> tints its texture with
    /// <c>GenUI.MouseoverColor</c> when the pointer is over it, and that is the only hover feedback these have;
    /// drawing a transparent texture would have thrown it away.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.CheckboxMulti))]
    public static class Patch_Widgets_CheckboxMulti
    {
        public static bool Prefix(Rect rect, MultiCheckboxState state, bool paintable,
            ref MultiCheckboxState __result)
        {
            try
            {
                MouseoverSounds.DoRegion(rect);

                MultiCheckboxState next = state != MultiCheckboxState.Off
                    ? MultiCheckboxState.Off
                    : MultiCheckboxState.On;

                bool changed = false;

                Widgets.DraggableResult drag = Widgets.ButtonImageDraggable(rect, BaseContent.ClearTex);

                if (paintable && drag == Widgets.DraggableResult.Dragged)
                {
                    AccessTools.StaticFieldRefAccess<bool>(typeof(Widgets), "checkboxPainting") = true;
                    AccessTools.StaticFieldRefAccess<bool>(typeof(Widgets), "checkboxPaintingState") =
                        next == MultiCheckboxState.On;

                    changed = true;
                }
                // DraggableResultUtility.AnyPressed is internal, so its one line is copied rather than called.
                else if (drag == Widgets.DraggableResult.Pressed
                         || drag == Widgets.DraggableResult.DraggedThenPressed)
                {
                    changed = true;
                }
                else if (paintable
                         && AccessTools.StaticFieldRefAccess<bool>(typeof(Widgets), "checkboxPainting")
                         && Mouse.IsOver(rect))
                {
                    next = AccessTools.StaticFieldRefAccess<bool>(typeof(Widgets), "checkboxPaintingState")
                        ? MultiCheckboxState.On
                        : MultiCheckboxState.Off;

                    if (state != next)
                        changed = true;
                }

                // Painted from the state on the way in, as vanilla does: it chooses its texture before deciding
                // anything, so a click shows its result on the following frame rather than under the cursor.
                Color previous = GUI.color;

                if (Mouse.IsOver(rect))
                    GUI.color = GenUI.MouseoverColor;

                UIElementPainter.PaintCheckbox(rect, state, null, false);

                GUI.color = previous;

                if (changed)
                {
                    (next == MultiCheckboxState.On
                        ? SoundDefOf.Checkbox_TurnedOn
                        : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();

                    __result = next;
                }
                else
                {
                    __result = state;
                }

                return false;
            }
            catch (Exception ex)
            {
                UIGuard.Report("Framework.CheckboxMulti", ex,
                    "That tri-state checkbox is drawn with RimWorld's own box artwork.");

                return true;
            }
        }
    }
}
