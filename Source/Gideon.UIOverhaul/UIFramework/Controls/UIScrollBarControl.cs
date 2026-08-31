using Gideon.UIFramework.Defs;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// A slim draggable scrollbar in place of RimWorld's chunky one.
    ///
    /// Pair it with <c>Widgets.BeginScrollView(rect, ref scroll, view, showScrollbars: false)</c>, size the view
    /// rect with <see cref="ContentWidth"/>, and call <see cref="Draw"/> after <c>EndScrollView</c> in the same
    /// coordinate space as the out rect. The mouse wheel still works, because Unity's scroll view handles it
    /// regardless of the scrollbar styles it was given.
    ///
    /// <b>Stateless, like every control here.</b> The caller keeps the scroll offset and the two drag fields,
    /// which is what lets one window own several independent scrolling regions.
    ///
    /// <b>This began life inside a feature's palette class</b> and was promoted once a fifth caller wanted it.
    /// The framework cannot reference a feature, so anything the framework's own controls need -- and the rail
    /// needs this -- has to live here.
    /// </summary>
    public static class UIScrollBarControl
    {
        /// <summary>Drawn width of the bar.</summary>
        public const float ScrollBarWidth = 6f;

        /// <summary>Clear space between the content and the bar.</summary>
        public const float ScrollBarGutter = 4f;

        /// <summary>
        /// Six pixels is a fair target for the eye and a poor one for a mouse; the extra width reaches back
        /// over the gutter, which holds nothing clickable.
        /// </summary>
        private const float ScrollBarHitWidth = 14f;

        private const float ScrollThumbMin = 24f;

        /// <summary>
        /// The width a view rect should be, leaving room for the bar and its gutter.
        /// </summary>
        public static float ContentWidth(Rect outRect)
        {
            return outRect.width - ScrollBarWidth - ScrollBarGutter;
        }

        /// <summary>
        /// <paramref name="viewHeight"/> must be the height actually laid out, not an upper bound: it sets both
        /// the thumb's size and how far the content can travel, so an over-estimate scrolls into empty space
        /// below the content.
        /// </summary>
        public static void Draw(Rect outRect, float viewHeight, ref Vector2 scroll, ref bool dragging,
            ref float dragOffset, UIColorPaletteDef palette = null)
        {
            palette = palette ?? UIColorPaletteDef.Active;

            if (palette == null)
                return;

            float maxScroll = Mathf.Max(0f, viewHeight - outRect.height);

            if (maxScroll <= 0f)
            {
                // Content that no longer overflows -- a list a search box has just filtered down -- leaves the
                // old offset stranded, showing blank space until something scrolls it back.
                scroll.y = 0f;
                dragging = false;

                return;
            }

            // Same reason, for content that shrank but still overflows.
            scroll.y = Mathf.Clamp(scroll.y, 0f, maxScroll);

            Rect track = new Rect(outRect.xMax - ScrollBarWidth, outRect.y, ScrollBarWidth, outRect.height);

            Widgets.DrawBoxSolid(track, palette.SurfaceSunken);

            float thumbHeight = Mathf.Max(ScrollThumbMin, outRect.height * (outRect.height / viewHeight));
            float travel = outRect.height - thumbHeight;
            Rect thumb = new Rect(track.x, track.y + travel * (scroll.y / maxScroll), ScrollBarWidth,
                thumbHeight);

            Rect hit = new Rect(outRect.xMax - ScrollBarHitWidth, outRect.y, ScrollBarHitWidth, outRect.height);

            Event current = Event.current;
            bool over = Mouse.IsOver(hit);

            // Read before anything calls Use(): Use() rewrites Event.current.type to Used, so the drag block
            // below cannot ask what kind of event this was once the click is consumed.
            bool positional = current.type == EventType.MouseDown || current.type == EventType.MouseDrag;
            float mouseY = current.mousePosition.y;

            if (current.type == EventType.MouseDown && current.button == 0 && over)
            {
                // Grabbing the thumb keeps the grab point; clicking the bare track centers it. Only the thumb's
                // vertical span is tested -- horizontally the cursor is already known to be in the hit column,
                // which is wider than the drawn bar, so a click in the gutter beside the thumb grabs it instead
                // of jumping it under the cursor.
                bool onThumb = mouseY >= thumb.y && mouseY <= thumb.yMax;

                dragOffset = onThumb ? mouseY - thumb.y : thumbHeight * 0.5f;
                dragging = true;

                current.Use();
            }
            else if (dragging && (current.type == EventType.MouseUp || current.rawType == EventType.MouseUp))
            {
                // rawType as well as type: a button released outside the window, or over something that
                // consumed the event, arrives here as Ignore and would leave the drag stuck on.
                dragging = false;

                current.Use();
            }

            // Only the events that carry a new cursor position move the content. Layout and Repaint reuse the
            // stored offset, so the thumb still renders where the last drag left it.
            //
            // The drag is consumed because a draggable Window calls GUI.DragWindow, which otherwise treats an
            // unclaimed drag as an instruction to move the whole window.
            if (dragging && travel > 0f && positional)
            {
                float local = mouseY - dragOffset - track.y;

                scroll.y = Mathf.Clamp01(local / travel) * maxScroll;
                thumb.y = track.y + travel * (scroll.y / maxScroll);

                if (current.type != EventType.Used)
                    current.Use();
            }

            // The thumb has to stay visible against the track, so it comes from a text role rather than a
            // translucent white wash: on a light theme white-on-light left it invisible. The two alphas are the
            // original ones, keeping the same at-rest and grabbed weights.
            Color thumbColor = palette.TextSecondary;

            thumbColor.a = dragging || over ? 0.7f : 0.45f;

            Widgets.DrawBoxSolid(thumb, thumbColor);
        }
    }
}
