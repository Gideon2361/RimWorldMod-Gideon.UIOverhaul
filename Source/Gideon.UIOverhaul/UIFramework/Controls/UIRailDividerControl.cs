using Gideon.UIFramework.Defs;
using UnityEngine;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// A hairline across the rail, for separating groups that need no caption.
    ///
    /// <b>A divider says "different kind of thing" without naming the kind.</b> Where a caption is worth the
    /// words, use <see cref="UIRailSectionHeaderControl"/>; where the grouping is obvious from the entries
    /// themselves, a line costs four pixels and no reading.
    ///
    /// The line is one pixel and the rest is clear space, so stacking two dividers does not draw a thick rule.
    /// </summary>
    internal sealed class UIRailDividerControl : UIRailElement
    {
        /// <summary>Null takes the palette's border color.</summary>
        internal Color? Color;

        /// <summary>Clear space above and below the line.</summary>
        internal float Margin = 4f;

        /// <summary>Space left at each end, so the line does not run into the panel's own border.</summary>
        internal float Inset = 6f;

        internal override float Height
        {
            get { return Margin * 2f + 1f; }
        }

        internal override bool Draw(Rect rect, UIColorPaletteDef palette, bool selected)
        {
            Rect line = new Rect(rect.x + Inset, rect.y + Margin, Mathf.Max(0f, rect.width - Inset * 2f), 1f);

            Verse.Widgets.DrawBoxSolid(line, Color ?? palette.Border);

            return false;
        }
    }
}
