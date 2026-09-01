using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// Something stacked down a rail: an entry, a caption, a divider.
    ///
    /// <b>Modelled on <see cref="UICardElement"/>,</b> so a rail is populated the same way a card is -- build a
    /// list of elements and hand it over. It diverges in two ways, both because a rail is a list a player picks
    /// from rather than a static layout: an element declares its own <see cref="Height"/> instead of carrying a
    /// bounds rect, since the rail stacks them; and <see cref="Draw"/> reports whether it was clicked.
    ///
    /// An element with a null <see cref="Key"/> is decoration -- it draws, it takes vertical space, and it
    /// cannot be selected or clicked.
    /// </summary>
    internal abstract class UIRailElement
    {
        /// <summary>Set false to keep an element in the list but not draw it, and give it no height.</summary>
        internal bool Visible = true;

        /// <summary>Hover text, or null.</summary>
        internal string Tooltip;

        /// <summary>What the caller selects by. Null makes this decoration rather than a choice.</summary>
        internal virtual string Key
        {
            get { return null; }
        }

        internal abstract float Height { get; }

        /// <summary>Returns true when the element was clicked this frame.</summary>
        internal abstract bool Draw(Rect rect, UIColorPaletteDef palette, bool selected);
    }

    /// <summary>
    /// The list down the side of a screen: what you are looking at, and how much of it there is.
    ///
    /// <b>Promoted from the trade screens, where the design had already earned itself,</b> then reshaped into an
    /// element list so a rail can mix entries, captions and dividers without the container knowing what any of
    /// them are. Thirteen screens had each hand-rolled the same sunken panel, scroll view and hover wash.
    ///
    /// <b>Stateless.</b> The caller keeps the selection and the scroll offset, which is what lets one screen own
    /// two rails; the beacon screen's "which beacon" and "what to show" are the same code.
    ///
    /// <b>Captions are captions, not collapsibles.</b> A rail with eight entries in two groups does not need to
    /// fold, and something that folds is something a player can hide from themselves and then not find.
    /// </summary>
    internal static class UIRailControl
    {
        /// <summary>
        /// Draws the rail and returns the key the player picked, or null if they picked nothing this frame.
        /// </summary>
        internal static string Draw(Rect rect, List<UIRailElement> elements, string selected,
            ref Vector2 scroll, ref bool dragging, ref float dragOffset, UIColorPaletteDef palette = null,
            bool frame = true)
        {
            palette = palette ?? UIColorPaletteDef.Active;

            if (palette == null || elements == null || elements.Count == 0)
                return null;

            if (frame)
            {
                // The same call the header block beside it makes, rather than DrawBoxSolid plus
                // Widgets.DrawBox: that second call draws vanilla's own near-white outline, which took no
                // palette at all, so every rail in the mod was framed in a colour no theme had chosen and one
                // that did not match the title box it sits under.
                UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);
            }

            Rect inner = frame ? rect.ContractedBy(1f) : rect;

            float height = 0f;

            for (int i = 0; i < elements.Count; i++)
            {
                UIRailElement element = elements[i];

                if (element != null && element.Visible)
                    height += element.Height;
            }

            Rect view = new Rect(0f, 0f, UIScrollBarControl.ContentWidth(inner), height + 2f);

            string picked = null;

            Widgets.BeginScrollView(inner, ref scroll, view, false);

            float y = 0f;

            for (int i = 0; i < elements.Count; i++)
            {
                UIRailElement element = elements[i];

                if (element == null || !element.Visible)
                    continue;

                Rect row = new Rect(0f, y, view.width, element.Height);

                if (!element.Tooltip.NullOrEmpty())
                    TooltipHandler.TipRegion(row, element.Tooltip);

                bool isSelected = element.Key != null && element.Key == selected;

                if (element.Draw(row, palette, isSelected))
                    picked = element.Key;

                y += element.Height;
            }

            Widgets.EndScrollView();

            UIScrollBarControl.Draw(inner, height + 2f, ref scroll, ref dragging, ref dragOffset, palette);

            return picked;
        }

        /// <summary>
        /// The height a rail needs to show every element without scrolling, for a caller sizing a panel around
        /// one rather than fitting one into a panel.
        /// </summary>
        internal static float HeightOf(List<UIRailElement> elements)
        {
            if (elements == null)
                return 0f;

            float height = 2f;

            for (int i = 0; i < elements.Count; i++)
            {
                UIRailElement element = elements[i];

                if (element != null && element.Visible)
                    height += element.Height;
            }

            return height;
        }
    }
}
