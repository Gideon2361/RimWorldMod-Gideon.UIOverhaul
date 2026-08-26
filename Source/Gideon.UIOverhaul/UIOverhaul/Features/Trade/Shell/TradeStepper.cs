using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Trade.Shell
{
    /// <summary>
    /// The count control: type it, step it, or take all or half of what is there.
    ///
    /// <b>Every change goes through <c>Transferable.AdjustTo</c>,</b> which is the decision this whole feature
    /// rests on. That method clamps to the transferable's own minimum and maximum, and <c>CanAdjustTo</c> hands
    /// back vanilla's own sentence for why a step was refused -- "the trader has no more", "the colony has no
    /// more". So the stepper never learns a trade rule, a caravan mass rule or a stack limit, and cannot fall out
    /// of step with one when the game changes. The count's setter is protected precisely so that nobody does this
    /// any other way, and reaching around it with reflection would be trading a working guarantee for nothing.
    ///
    /// <b>Typing is the headline.</b> Vanilla's cluster is five arrows in 240 pixels, so setting four hundred of
    /// something means holding a button down or discovering that the number in the middle is editable. Here the
    /// number is a real field, first-class, with the arrows beside it for the small adjustments they are good at.
    ///
    /// <b>Sign is the caller's business, magnitude is the player's.</b> A trade screen showing its buy view and
    /// the same screen showing its sell view are one list read in two directions, and the count underneath is one
    /// signed number: positive buys, negative sells. Making the player think in signs would be exposing a
    /// storage detail, so the caller passes the direction it is displaying and this control only ever shows a
    /// count of zero or more.
    /// </summary>
    internal static class TradeStepper
    {
        /// <summary>Total width this control needs, so a table can subtract it before laying out anything else.</summary>
        internal const float Width = 148f;

        private const float ArrowWidth = 22f;
        private const float BoxWidth = 52f;
        private const float Gap = 3f;

        /// <summary>
        /// One box per transferable, made on demand.
        ///
        /// <b>Keyed on the transferable itself rather than on the row's position,</b> which is what keeps a caret
        /// where the player put it. These lists re-sort as a search box is typed into and as a category is
        /// picked, so an index-keyed pool would hand the box you are typing in to a different item the moment the
        /// list moved. That fault was already found and fixed once, in the taming bill's species boxes.
        ///
        /// Reference identity is the right key here: a <c>Tradeable</c> lives as long as the deal does, and when
        /// the deal is rebuilt every one of them is a new object, so the old entries are dead by definition. See
        /// <see cref="Forget"/>.
        /// </summary>
        private static readonly Dictionary<Transferable, UITextBoxControl> Boxes =
            new Dictionary<Transferable, UITextBoxControl>();

        /// <summary>
        /// Drops every box.
        ///
        /// Called when a deal is reset or a window closes. Without it the dictionary would hold the whole of a
        /// dead deal alive, and -- worse than the memory -- a box still holding the number the last deal ended on
        /// would write that number into the new one the first time it was touched.
        /// </summary>
        internal static void Forget()
        {
            Boxes.Clear();
        }

        /// <summary>
        /// Draws the control and reports whether the count changed.
        /// </summary>
        /// <param name="sign">
        /// Which direction the view is showing: <c>+1</c> when a positive count is what the player is being asked
        /// about, <c>-1</c> when it is the negative side of the same number. See the class remarks.
        /// </param>
        /// <param name="allowHalf">
        /// Kept for the call sites that still pass it and no longer used. The <c>half</c> button went when the
        /// control became five symmetrical marks; halving a stack is a thing somebody does once a session and it
        /// was costing forty pixels on every row of every screen.
        /// </param>
        internal static bool Draw(Rect rect, Transferable transferable, int sign, UIColorPaletteDef palette,
            bool allowHalf = true)
        {
            if (transferable == null)
                return false;

            return UIGuard.Try("Trade.Stepper", () => Body(rect, transferable, sign, palette, allowHalf), false,
                "This row's count control did not draw. The deal is unchanged.");
        }

        private static bool Body(Rect rect, Transferable transferable, int sign, UIColorPaletteDef palette,
            bool allowHalf)
        {
            int current = Magnitude(transferable, sign);
            int ceiling = Ceiling(transferable, sign);

            bool changed = false;

            float x = rect.x;

            // <b>Five controls around the number, symmetrical about it.</b> The double marks jump to an end and
            // the single ones step by one, so the pair on each side of the field means the same thing in the same
            // order on both sides -- which is what makes the row readable without labels. It replaced a
            // "- box + all half" line whose two word buttons sat only on the right and made the field look like
            // the left end of the control rather than its middle.
            if (Arrow(new Rect(x, rect.y, ArrowWidth, rect.height), "«", current > 0, palette))
                changed |= Set(transferable, sign, 0);

            x += ArrowWidth + Gap;

            if (Arrow(new Rect(x, rect.y, ArrowWidth, rect.height), "‹", current > 0, palette))
                changed |= Set(transferable, sign, current - 1);

            x += ArrowWidth + Gap;

            UITextBoxControl box = BoxFor(transferable);

            // Re-seeded from the transferable every frame the player is not typing in it. The count moves for
            // reasons that have nothing to do with this box -- the all button, a standing want filling itself in,
            // the silver row being recomputed from the rest of the deal -- and a box that only ever wrote would
            // sit there showing a number that is no longer true.
            if (!box.Focused)
                box.Text = current > 0 ? current.ToStringCached() : string.Empty;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;

                if (box.Draw(new Rect(x, rect.y, BoxWidth, rect.height), palette))
                    changed |= Typed(transferable, sign, box);
            }
            finally
            {
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            x += BoxWidth + Gap;

            if (Arrow(new Rect(x, rect.y, ArrowWidth, rect.height), "›", current < ceiling, palette))
                changed |= Set(transferable, sign, current + 1);

            x += ArrowWidth + Gap;

            if (Arrow(new Rect(x, rect.y, ArrowWidth, rect.height), "»", current < ceiling, palette))
                changed |= Set(transferable, sign, ceiling);

            if (changed)
                SoundDefOf.DragSlider.PlayOneShotOnCamera();

            return changed;
        }

        /// <summary>
        /// How many of this the player is asking for, in the direction being displayed.
        ///
        /// Zero rather than a negative number when the count is pointing the other way. A row showing a buy count
        /// while the player has actually queued a sale of the same thing is not a contradiction to resolve here:
        /// the sell view is where that number belongs, and showing it as a negative buy would be arithmetic
        /// standing in for a fact.
        /// </summary>
        internal static int Magnitude(Transferable transferable, int sign)
        {
            return Mathf.Max(0, transferable.CountToTransfer * sign);
        }

        /// <summary>
        /// The largest count this direction allows, which is vanilla's own limit read through the sign.
        ///
        /// <c>GetMaximumToTransfer</c> and <c>GetMinimumToTransfer</c> are the two ends of one range that spans
        /// zero -- buying up to what the trader has, selling down to what the colony has -- so the ceiling for the
        /// direction being shown is whichever end the sign points at.
        /// </summary>
        internal static int Ceiling(Transferable transferable, int sign)
        {
            return sign > 0
                ? Mathf.Max(0, transferable.GetMaximumToTransfer())
                : Mathf.Max(0, -transferable.GetMinimumToTransfer());
        }

        /// <summary>
        /// Writes a magnitude back, refusing quietly when vanilla says no.
        ///
        /// <b><c>AdjustTo</c> logs an error if it is handed something out of range,</b> so the report is asked for
        /// first rather than after the fact. That matters: a player holding the plus key at the top of a stack
        /// would otherwise fill the log with our name on it.
        /// </summary>
        private static bool Set(Transferable transferable, int sign, int magnitude)
        {
            int destination = Mathf.Max(0, magnitude) * sign;

            if (destination == transferable.CountToTransfer)
                return false;

            if (!transferable.CanAdjustTo(destination).Accepted)
                return false;

            transferable.AdjustTo(destination);

            return true;
        }

        /// <summary>
        /// Takes what was typed.
        ///
        /// <b>Anything unparseable reads as zero rather than being refused.</b> A half typed number is a number on
        /// its way somewhere, and rejecting it fights the person typing it -- the same call the taming bill's
        /// number boxes make. The clamp is vanilla's, by way of <c>ClampAmount</c>, so a player who types a
        /// thousand into a row holding forty gets forty rather than an error.
        /// </summary>
        private static bool Typed(Transferable transferable, int sign, UITextBoxControl box)
        {
            int value;

            if (!int.TryParse(box.Text, out value) || value < 0)
                value = 0;

            int clamped = Mathf.Min(value, Ceiling(transferable, sign));

            return Set(transferable, sign, clamped);
        }

        private static UITextBoxControl BoxFor(Transferable transferable)
        {
            UITextBoxControl box;

            if (Boxes.TryGetValue(transferable, out box))
                return box;

            box = new UITextBoxControl
            {
                Placeholder = "0",
                MaxLength = 7,
                ShowClearButton = false
            };

            Boxes[transferable] = box;

            return box;
        }

        /// <summary>
        /// A step button.
        ///
        /// Silent, because the caller plays one sound for whatever the frame's change turned out to be. A sound
        /// here as well would double up every time a click actually moved the count.
        /// </summary>
        private static bool Arrow(Rect rect, string glyph, bool enabled, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect) && enabled;

            Widgets.DrawBoxSolid(rect, palette.SurfaceRaised);

            if (over)
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = enabled ? palette.TextPrimary : palette.TextDisabled;

                Widgets.Label(rect, glyph);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            return enabled && Widgets.ButtonInvisible(rect);
        }

        /// <summary>
        /// The sentence in place of a stepper on a row that cannot be adjusted at all.
        ///
        /// <b>The row stays and the control goes.</b> A negotiator who will not sell slaves is a fact about this
        /// trade that a player needs, and it applies to a handful of rows, so the row keeps its place in the list
        /// with the reason where its count would be.
        ///
        /// A trader refusing a whole kind of goods is the other case and is no longer drawn here: it applies to
        /// most of what a colony owns, so those rows are gathered into their own view instead. See
        /// <c>TradeCatalog.Refused</c>.
        /// </summary>
        internal static void Refused(Rect rect, string reason, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = palette.TextDisabled;

                Widgets.Label(rect, reason ?? string.Empty);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// The star that marks a favourite. Returns true on a click, and the caller flips the flag.
        ///
        /// A filled star and a hollow one rather than a coloured and a grey one: the difference has to survive a
        /// theme where the accent is close to the text colour, and a shape survives that where a hue does not.
        /// </summary>
        internal static bool Star(Rect rect, bool on, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = on ? palette.Warning : over ? palette.TextSecondary : palette.TextDisabled;

                Widgets.Label(rect, on ? "★" : "☆");
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (over)
            {
                TooltipHandler.TipRegion(rect, (TipSignal) (on
                    ? "Stop keeping this at the top of its category."
                    : "Keep this at the top of its category, in every trade."));
            }

            if (!Widgets.ButtonInvisible(rect))
                return false;

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();

            return true;
        }

        /// <summary>
        /// A small square button carrying a glyph, for the remove crosses in the spine.
        ///
        /// Shares <see cref="GzpPalette.IconButton"/>'s silence for the same reason: what a caller does on click
        /// decides what it should sound like.
        /// </summary>
        internal static bool Glyph(Rect rect, string glyph, string tooltip, UIColorPaletteDef palette,
            Color? color = null)
        {
            bool over = Mouse.IsOver(rect);

            if (over)
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = over ? palette.TextPrimary : color ?? palette.TextDisabled;

                Widgets.Label(rect, glyph);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (!tooltip.NullOrEmpty() && over)
                TooltipHandler.TipRegion(rect, (TipSignal) tooltip);

            return Widgets.ButtonInvisible(rect);
        }
    }
}
