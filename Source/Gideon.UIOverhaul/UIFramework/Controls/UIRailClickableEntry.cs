using System;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// A selectable row on a rail: the thing a player actually clicks.
    ///
    /// <b>Everything past the label is optional and off by default,</b> so the common case is two lines at the
    /// call site and a rail that needs a color chip, a drawn glyph and a right-aligned count can have all three
    /// without a second control. Ordered left to right: swatch, icon or glyph, label, count.
    ///
    /// <b>Colors are nullable and mean "ask the palette".</b> A row that hard-codes its text color stops
    /// following the theme, and this suite lets players change theme, so the default has to come from the
    /// palette rather than from the call site.
    ///
    /// <b>Disabled dims rather than hides.</b> Removing a row that has nothing behind it makes the rail change
    /// shape while a search box is typed into, so the thing the player is reaching for moves while they reach.
    /// </summary>
    internal sealed class UIRailClickableEntry : UIRailElement
    {
        private string key;

        internal string Label;

        /// <summary>How many things are behind this row. Negative hides the number.</summary>
        internal int Count = -1;

        /// <summary>
        /// Text against the right edge, for a rail whose rows report a value rather than a tally -- a power
        /// grid balance, a deadline, a status word. Wins over <see cref="Count"/> when both are set.
        /// </summary>
        internal string Trailing;

        /// <summary>Null takes the palette's secondary text color.</summary>
        internal Color? CountColor;

        /// <summary>Null takes the palette's primary text color, or the disabled color when dimmed.</summary>
        internal Color? TextColor;

        /// <summary>
        /// A solid color chip against the leading edge, for a rail whose rows *are* colors -- a theme picker, a
        /// palette editor, an ideoligion's banner color.
        /// </summary>
        internal Color? Swatch;

        internal float SwatchWidth = 4f;

        /// <summary>
        /// A bar down the leading edge of this row while it is the selected one, in the tab's own color.
        ///
        /// <b>For a rail whose rows already carry colors of their own.</b> <c>SelectionOverlay</c> alone is
        /// enough on a rail of plain names, and it is what every row still gets; but the research contents rail
        /// is twelve colored blocks, and a faint wash over one of twelve tinted rows is not a mark anybody can
        /// find. The bar is a second channel, in a color no row can be, which is the whole reason a tab has an
        /// identity color at all.
        ///
        /// Null on every rail that does not need it, which is all of them but two, so no existing row moves.
        /// </summary>
        internal Color? SelectionBar;

        /// <summary>Matched to the accent width every other selected thing in the mod is marked with.</summary>
        internal const float SelectionBarWidth = 3f;

        /// <summary>Drawn between the swatch and the label. Ignored when <see cref="Glyph"/> is set.</summary>
        internal Texture2D Icon;

        /// <summary>
        /// Draws a vector glyph in the same slot as <see cref="Icon"/>, given the rect and the color the row
        /// resolved to. Takes precedence over an icon.
        ///
        /// A delegate rather than a glyph enum, because the framework must not know the names of the shapes a
        /// feature happens to draw -- <c>UIGlyphCanvas</c> and <c>UIIconCanvas</c> both fit this signature.
        /// </summary>
        internal Action<Rect, Color> Glyph;

        internal float IconSize = 20f;

        /// <summary>
        /// Clear space before the swatch or glyph. Lowered by a row that needs the width for its label more
        /// than it needs the breathing room.
        /// </summary>
        internal float LeadPad = 6f;

        /// <summary>
        /// Drawn hard against the trailing edge, right of the count, for a row that carries its own action --
        /// a remove cross, a pin, a lock.
        ///
        /// <b>It may consume the click.</b> Same contract as <see cref="Glyph"/>: the delegate hit tests and
        /// calls <c>Event.current.Use()</c> itself, and a consumed event stops the row reporting a plain click,
        /// so removing a row does not also select it.
        /// </summary>
        internal Action<Rect, Color> TrailingGlyph;

        internal float TrailingGlyphSize = 16f;

        /// <summary>
        /// Called with the whole row rect once the row has drawn, for a caller that needs the row as a region
        /// rather than as a click -- a drop target, an overlay, a drag highlight.
        ///
        /// The escape hatch that keeps this control from having to know about drag and drop: the research queue
        /// reorders by testing a mouse release against the row it lands on, which is its own logic and not
        /// something every rail should carry.
        /// </summary>
        internal Action<Rect> Decorate;

        /// <summary>
        /// Typeface for the label. <see cref="UIFace.Game"/> uses RimWorld's own, which is what almost every
        /// rail wants; the font picker sets this per row so each entry previews itself.
        /// </summary>
        internal UIFace Face = UIFace.Game;

        internal GameFont Font = GameFont.Small;

        /// <summary>
        /// Point size for a bundled face, which is how this suite sizes anything that is not RimWorld own
        /// font -- a GameFont is a bucket, and a bundled face line box rarely matches the bucket it lands in.
        /// Zero falls back to <see cref="Font"/>.
        /// </summary>
        internal float Points;

        /// <summary>Face for the count, so a rail with mono figures keeps them mono.</summary>
        internal UIFace CountFace = UIFace.Game;

        /// <summary>Point size for the count when <see cref="CountFace"/> is a bundled face.</summary>
        internal float CountPoints;

        /// <summary>Bold or italic. Only honored for a bundled <see cref="Face"/>, which is where real weights live.</summary>
        internal FontStyle Style = FontStyle.Normal;

        /// <summary>
        /// Suppresses the click tick. Rails feel wrong without it -- every other clickable thing in the suite
        /// answers -- so the sound is on by default and this is for a caller that has its own.
        /// </summary>
        internal bool Silent;

        internal bool Disabled;

        /// <summary>
        /// Completion from 0 to 1, drawn as a hairline under the label. Negative draws nothing.
        ///
        /// Drawn even at zero, because a row with no track under it reads as a row that does not have progress
        /// rather than one that has none yet.
        /// </summary>
        internal float Progress = -1f;

        /// <summary>Null takes the palette accent for the filled part.</summary>
        internal Color? ProgressColor;

        /// <summary>Row height. Raise it for a face drawn large enough to preview properly.</summary>
        internal float Rise = 26f;

        internal UIRailClickableEntry()
        {
        }

        internal UIRailClickableEntry(string key, string label)
        {
            this.key = key;
            Label = label;
        }

        internal override string Key
        {
            get { return key; }
        }

        internal void SetKey(string value)
        {
            key = value;
        }

        internal override float Height
        {
            get { return Rise; }
        }

        internal override bool Draw(Rect rect, UIColorPaletteDef palette, bool selected)
        {
            bool over = !Disabled && Mouse.IsOver(rect);

            if (selected)
            {
                // SelectionOverlay rather than a wash of the accent: it is the palette role for exactly this,
                // and the trade rails have been using it since before this control existed.
                Widgets.DrawBoxSolid(rect, palette.SelectionOverlay);
            }
            else if (over)
            {
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);
            }

            // The bar's lane is reserved on every row of a rail that uses one, selected or not, so a swatch
            // beside it does not jump sideways as the selection moves. Inset top and bottom for the same
            // reason the swatch is: a bar running the full height would join up with its neighbours into one
            // continuous stripe.
            float lead = SelectionBar.HasValue ? SelectionBarWidth + 3f : 0f;

            if (selected && SelectionBar.HasValue)
            {
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 2f, SelectionBarWidth, rect.height - 4f),
                    SelectionBar.Value);
            }

            Color content = TextColor ?? (Disabled ? palette.TextDisabled : palette.TextPrimary);
            float x = rect.x + lead + LeadPad;

            if (Swatch.HasValue)
            {
                Widgets.DrawBoxSolid(new Rect(rect.x + lead, rect.y + 2f, SwatchWidth, rect.height - 4f),
                    Swatch.Value);

                x = rect.x + lead + SwatchWidth + 6f;
            }

            if (Glyph != null || Icon != null)
            {
                // Square only while it fits. A slot taller than its row would hang into the rows above and
                // below, and the row that drew first would take clicks meant for this one.
                float tall = Mathf.Min(IconSize, rect.height);

                Rect slot = new Rect(x, rect.y + (rect.height - tall) / 2f, IconSize, tall);

                if (Glyph != null)
                {
                    Glyph(slot, content);
                }
                else
                {
                    Color previous = GUI.color;

                    GUI.color = Disabled ? palette.TextDisabled : Color.white;

                    GUI.DrawTexture(slot, Icon, ScaleMode.ScaleToFit);

                    GUI.color = previous;
                }

                x = slot.xMax + 6f;
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            // Measured before the label is placed so the text is trimmed around the count rather than drawn
            // underneath it.
            float countWidth = 0f;
            string count = !Trailing.NullOrEmpty() ? Trailing : Count >= 0 ? Count.ToString() : null;

            if (count != null)
            {
                if (CountFace != UIFace.Game && CountPoints > 0f)
                {
                    countWidth = UITextControl.Width(count, CountFace, CountPoints) + 8f;
                }
                else
                {
                    Text.Font = GameFont.Tiny;

                    countWidth = Text.CalcSize(count).x + 6f;
                }
            }

            float rightEdge = rect.xMax - 6f;

            if (TrailingGlyph != null)
            {
                Rect slot = new Rect(rightEdge - TrailingGlyphSize,
                    rect.y + (rect.height - TrailingGlyphSize) / 2f, TrailingGlyphSize, TrailingGlyphSize);

                TrailingGlyph(slot, content);

                rightEdge = slot.x - 4f;
            }

            Rect label = new Rect(x, rect.y, Mathf.Max(0f, rightEdge - countWidth - x), rect.height);

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = content;

            if (!Label.NullOrEmpty())
            {
                if (Face == UIFace.Game)
                {
                    Text.Font = Font;

                    Widgets.LabelEllipses(label, Label);
                }
                else if (Points > 0f)
                {
                    UITextControl.LabelEllipses(label, Label, Face, Points, Style);
                }
                else
                {
                    UITextControl.LabelEllipses(label, Label, Face, Font, Style);
                }
            }

            if (count != null)
            {
                Rect box = new Rect(rightEdge - countWidth, rect.y, countWidth, rect.height);

                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = CountColor ?? (Disabled ? palette.TextDisabled : palette.TextSecondary);

                if (CountFace == UIFace.Game)
                {
                    Text.Font = GameFont.Tiny;

                    Widgets.Label(box, count);
                }
                else if (CountPoints > 0f)
                {
                    UITextControl.Label(box, count, CountFace, CountPoints);
                }
                else
                {
                    UITextControl.Label(box, count, CountFace, GameFont.Tiny);
                }
            }

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (Progress >= 0f)
            {
                Rect track = new Rect(x, rect.yMax - 3f, Mathf.Max(0f, rect.xMax - 6f - x), 1f);

                Widgets.DrawBoxSolid(track, palette.SurfaceSunken);
                Widgets.DrawBoxSolid(new Rect(track.x, track.y, track.width * Mathf.Clamp01(Progress), 1f),
                    ProgressColor ?? palette.Accent);
            }

            if (Decorate != null)
                Decorate(rect);

            if (Disabled || !Widgets.ButtonInvisible(rect))
                return false;

            if (!Silent)
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();

            return true;
        }
    }
}
