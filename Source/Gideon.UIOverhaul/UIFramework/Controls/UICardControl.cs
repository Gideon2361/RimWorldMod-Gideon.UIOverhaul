using System.Collections.Generic;
using Gideon.UIFramework.Components.Images;
using Gideon.UIFramework.Defs;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// Something drawn inside a card. Positioned in card-content space, so an element does not need to
    /// know where its card ended up on screen.
    /// </summary>
    public abstract class UICardElement
    {
        /// <summary>
        /// Position and size relative to the card's content rect. A zero-size rect fills the content
        /// rect, which is what a single-element card almost always wants.
        /// </summary>
        public Rect Bounds;

        /// <summary>Set false to keep an element in the card but not draw it.</summary>
        public bool Visible = true;

        /// <summary>Hover text for this element alone. Null uses the card's.</summary>
        public string Tooltip;

        internal Rect Resolve(Rect content)
        {
            if (Bounds.width <= 0f && Bounds.height <= 0f)
                return content;

            return new Rect(content.x + Bounds.x, content.y + Bounds.y, Bounds.width, Bounds.height);
        }

        public abstract void Draw(Rect rect, UIColorPaletteDef palette);
    }

    /// <summary>Text inside a card.</summary>
    public class UICardLabel : UICardElement
    {
        public string Text;

        /// <summary>Null takes the palette's primary text color, so a card follows the theme by default.</summary>
        public Color? Color;

        public GameFont Font = GameFont.Small;
        public TextAnchor Anchor = TextAnchor.MiddleLeft;
        public bool WrapText;

        /// <summary>
        /// Cuts text that will not fit and marks the cut with an ellipsis, instead of clipping it mid-letter.
        ///
        /// For text whose length is not ours to control -- a name from another mod, a colonist's nickname. A
        /// hard clip leaves a word ending in half a character, which reads as a rendering fault rather than as
        /// a name too long for the space; an ellipsis says the same thing on purpose.
        ///
        /// Ignored when <see cref="WrapText"/> is set, since text that wraps has no single line to shorten.
        /// </summary>
        public bool Ellipses;

        public override void Draw(Rect rect, UIColorPaletteDef palette)
        {
            if (Text.NullOrEmpty())
                return;

            GameFont previousFont = Verse.Text.Font;
            TextAnchor previousAnchor = Verse.Text.Anchor;
            bool previousWrap = Verse.Text.WordWrap;
            Color previousColor = GUI.color;

            Verse.Text.Font = Font;
            Verse.Text.Anchor = Anchor;
            Verse.Text.WordWrap = WrapText;
            GUI.color = Color ?? palette.TextPrimary;

            if (Ellipses && !WrapText)
                Widgets.LabelEllipses(rect, Text);
            else
                Widgets.Label(rect, Text);

            GUI.color = previousColor;
            Verse.Text.WordWrap = previousWrap;
            Verse.Text.Anchor = previousAnchor;
            Verse.Text.Font = previousFont;
        }
    }

    /// <summary>An image inside a card: an icon, a thumbnail, a piece of artwork.</summary>
    public class UICardImage : UICardElement
    {
        public Texture Texture;
        public UIImageFit Fit = UIImageFit.Contain;

        /// <summary>
        /// Null draws untinted. That is the default because most card images are full-color art rather
        /// than silhouettes, and tinting those to a theme color destroys them.
        /// </summary>
        public Color? Tint;

        public override void Draw(Rect rect, UIColorPaletteDef palette)
        {
            if (Texture == null)
                return;

            Color previous = GUI.color;
            GUI.color = Tint ?? UnityEngine.Color.white;

            switch (Fit)
            {
                case UIImageFit.Cover:
                    GUI.DrawTexture(rect, Texture, ScaleMode.ScaleAndCrop);
                    break;
                case UIImageFit.Stretch:
                    GUI.DrawTexture(rect, Texture, ScaleMode.StretchToFill);
                    break;
                default:
                    GUI.DrawTexture(rect, Texture, ScaleMode.ScaleToFit);
                    break;
            }

            GUI.color = previous;
        }
    }

    /// <summary>A horizontal meter inside a card, for a progress or ratio value.</summary>
    public class UICardMeter : UICardElement
    {
        /// <summary>0 to 1, clamped when drawn.</summary>
        public float Fraction;

        public Color? FillColor;

        public override void Draw(Rect rect, UIColorPaletteDef palette)
        {
            UIProgressBarControl.Draw(rect, Fraction, palette, FillColor);
        }
    }

    /// <summary>
    /// A card: a filled panel with an optional accent stripe down its left edge, an optional background
    /// image, and any number of elements drawn inside it.
    ///
    /// Configured through properties and reused across frames rather than rebuilt each one, so a caller
    /// sets a card up once and then only assigns what changed -- an element's Text, a meter's Fraction.
    /// Every color defaults to null meaning "ask the palette", so a card follows the theme unless it is
    /// deliberately overridden.
    ///
    /// Content can come from either direction. Add <see cref="UICardElement"/>s and the card lays them
    /// out and draws them, or ignore Elements entirely and use <see cref="ContentRect"/> to draw inside
    /// the card by hand. Mixing the two is fine; the elements draw first.
    /// </summary>
    public class UICardControl
    {
        /// <summary>Preferred size. Height is what a scrolling list needs in order to lay cards out.</summary>
        public float Width = 0f;

        public float Height = 130f;

        /// <summary>Inset between the card's edge and its content, the accent stripe included.</summary>
        public float Padding = 8f;

        /// <summary>
        /// The stripe down the left edge, used to categorize a card at a glance. Null draws no stripe,
        /// which is what a card that has nothing to categorize wants.
        /// </summary>
        public Color? AccentColor;

        public float AccentWidth = 3f;

        /// <summary>Fill behind everything. Null uses the palette's panel background.</summary>
        public Color? BackgroundColor;

        /// <summary>
        /// Optional image over the fill and under the content: a texture, a wash, a pattern. Drawn with
        /// <see cref="BackgroundTint"/>, which is how the growing-zone cards get their striped notice
        /// wash from one grey texture.
        /// </summary>
        public Texture BackgroundTexture;

        public UIImageFit BackgroundFit = UIImageFit.Stretch;
        public Color? BackgroundTint;

        /// <summary>Single-pixel border. Null draws none.</summary>
        public Color? BorderColor;

        /// <summary>Draws the palette's selection wash over the card.</summary>
        public bool Selected;

        /// <summary>Draws the palette's hover wash when the cursor is over the card.</summary>
        public bool HoverHighlight = true;

        /// <summary>Hover text for the card as a whole.</summary>
        public string Tooltip;

        /// <summary>
        /// Content, drawn in order. Public so a caller can hold references and assign to them between
        /// frames rather than rebuilding the list, which is the point of the card being an object.
        /// </summary>
        public readonly List<UICardElement> Elements = new List<UICardElement>();

        public T Add<T>(T element) where T : UICardElement
        {
            Elements.Add(element);
            return element;
        }

        /// <summary>Where content goes: the card inset by <see cref="Padding"/> and the accent stripe.</summary>
        public Rect ContentRect(Rect card)
        {
            float left = Padding + (AccentColor.HasValue ? AccentWidth : 0f);
            return new Rect(card.x + left, card.y + Padding,
                Mathf.Max(0f, card.width - left - Padding),
                Mathf.Max(0f, card.height - Padding * 2f));
        }

        /// <summary>
        /// Draws the card and reports whether it was clicked.
        /// </summary>
        /// <param name="palette">Palette to draw from. Defaults to the active one.</param>
        public bool Draw(Rect card, UIColorPaletteDef palette = null)
        {
            DrawChrome(card, palette);
            return Widgets.ButtonInvisible(card);
        }

        /// <summary>
        /// Draws the card but does not claim the click.
        ///
        /// For a card that contains controls of its own. <see cref="Draw"/> ends with ButtonInvisible, which
        /// consumes the event, so any button drawn inside the card afterwards would never see its own click.
        /// A row of icon buttons on a card needs this; a card that is itself one big button wants Draw.
        /// </summary>
        public void DrawChrome(Rect card, UIColorPaletteDef palette = null)
        {
            palette = palette ?? UIColorPaletteDef.Active;

            bool over = Mouse.IsOver(card);
            Color previousColor = GUI.color;

            Widgets.DrawBoxSolid(card, BackgroundColor ?? palette.PanelBackground);

            if (BackgroundTexture != null)
            {
                GUI.color = BackgroundTint ?? Color.white;
                GUI.DrawTexture(card, BackgroundTexture,
                    BackgroundFit == UIImageFit.Cover ? ScaleMode.ScaleAndCrop
                    : BackgroundFit == UIImageFit.Contain ? ScaleMode.ScaleToFit
                    : ScaleMode.StretchToFill);
                GUI.color = previousColor;
            }

            if (AccentColor.HasValue)
                Widgets.DrawBoxSolid(new Rect(card.x, card.y, AccentWidth, card.height), AccentColor.Value);

            if (Selected)
                Widgets.DrawBoxSolid(card, palette.SelectionOverlay);
            else if (over && HoverHighlight)
                Widgets.DrawBoxSolid(card, palette.HoverOverlay);

            Rect content = ContentRect(card);

            foreach (UICardElement element in Elements)
            {
                if (!element.Visible)
                    continue;

                Rect elementRect = element.Resolve(content);
                element.Draw(elementRect, palette);

                if (!element.Tooltip.NullOrEmpty())
                    TooltipHandler.TipRegion(elementRect, (TipSignal) element.Tooltip);
            }

            if (BorderColor.HasValue)
            {
                GUI.color = BorderColor.Value;
                Widgets.DrawBox(card, 1);
                GUI.color = previousColor;
            }

            GUI.color = previousColor;

            if (!Tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(card, (TipSignal) Tooltip);
        }
    }
}
