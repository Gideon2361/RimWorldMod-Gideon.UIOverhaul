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
        /// Typeface for the label. <see cref="UIFace.Game"/> uses RimWorld's own, which is what almost every
        /// rail wants; the font picker sets this per row so each entry previews itself.
        /// </summary>
        internal UIFace Face = UIFace.Game;

        internal GameFont Font = GameFont.Small;

        /// <summary>Bold or italic. Only honored for a bundled <see cref="Face"/>, which is where real weights live.</summary>
        internal FontStyle Style = FontStyle.Normal;

        /// <summary>
        /// Suppresses the click tick. Rails feel wrong without it -- every other clickable thing in the suite
        /// answers -- so the sound is on by default and this is for a caller that has its own.
        /// </summary>
        internal bool Silent;

        internal bool Disabled;

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

            Color content = TextColor ?? (Disabled ? palette.TextDisabled : palette.TextPrimary);
            float x = rect.x + 6f;

            if (Swatch.HasValue)
            {
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 2f, SwatchWidth, rect.height - 4f),
                    Swatch.Value);

                x = rect.x + SwatchWidth + 6f;
            }

            if (Glyph != null || Icon != null)
            {
                Rect slot = new Rect(x, rect.y + (rect.height - IconSize) / 2f, IconSize, IconSize);

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
            string count = Count >= 0 ? Count.ToString() : null;

            if (count != null)
            {
                Text.Font = GameFont.Tiny;

                countWidth = Text.CalcSize(count).x + 6f;
            }

            Rect label = new Rect(x, rect.y, Mathf.Max(0f, rect.xMax - 6f - countWidth - x), rect.height);

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = content;

            if (!Label.NullOrEmpty())
            {
                if (Face == UIFace.Game)
                {
                    Text.Font = Font;

                    Widgets.LabelEllipses(label, Label);
                }
                else
                {
                    UITextControl.LabelEllipses(label, Label, Face, Font, Style);
                }
            }

            if (count != null)
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = CountColor ?? (Disabled ? palette.TextDisabled : palette.TextSecondary);

                Widgets.Label(new Rect(rect.xMax - 6f - countWidth, rect.y, countWidth, rect.height), count);
            }

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (Disabled || !Widgets.ButtonInvisible(rect))
                return false;

            if (!Silent)
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();

            return true;
        }
    }
}
