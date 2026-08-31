using Gideon.UIFramework.Defs;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// A caption naming the group of entries beneath it.
    ///
    /// <b>Dim, small and unclickable on purpose.</b> A caption that looked like an entry would invite a click
    /// that does nothing, and a caption that looked like a heading would compete with the panel's own title.
    /// Bottom-aligned so it sits close to the entries it introduces rather than floating between two groups.
    /// </summary>
    internal sealed class UIRailSectionHeaderControl : UIRailElement
    {
        internal string Label;

        /// <summary>Null takes the palette's secondary text color, which is what keeps it quiet.</summary>
        internal Color? Color;

        /// <summary>A count, badge or hint drawn dim against the right edge. Null draws nothing.</summary>
        internal string Trailing;

        /// <summary>
        /// Draws the label upper cased. The trade screens set this; it reads as a category marker rather than a
        /// heading, which is what a caption inside a list wants.
        /// </summary>
        internal bool Uppercase;

        internal float Rise = 24f;

        internal UIRailSectionHeaderControl()
        {
        }

        internal UIRailSectionHeaderControl(string label)
        {
            Label = label;
        }

        internal override float Height
        {
            get { return Rise; }
        }

        internal override bool Draw(Rect rect, UIColorPaletteDef palette, bool selected)
        {
            if (Label.NullOrEmpty() && Trailing.NullOrEmpty())
                return false;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerLeft;
            GUI.color = Color ?? palette.TextSecondary;

            Rect inner = new Rect(rect.x + 6f, rect.y, rect.width - 12f, rect.height);

            if (!Trailing.NullOrEmpty())
            {
                float width = Text.CalcSize(Trailing).x;

                Text.Anchor = TextAnchor.LowerRight;

                Widgets.Label(new Rect(inner.xMax - width, inner.y, width, inner.height), Trailing);

                Text.Anchor = TextAnchor.LowerLeft;

                inner.width -= width + 6f;
            }

            if (!Label.NullOrEmpty())
                Widgets.LabelEllipses(inner, Uppercase ? Label.ToUpperInvariant() : Label);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            return false;
        }
    }
}
