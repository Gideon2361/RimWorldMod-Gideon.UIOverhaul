using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// Confines a window's drag handle to its title bar.
    ///
    /// <b>What vanilla does, and why it is wrong for these windows.</b> <c>Window</c> ends every frame with
    /// <c>if (draggable) GUI.DragWindow()</c> and no arguments, which makes the entire window one drag
    /// surface. On RimWorld's own dialogs -- mostly a message and two buttons -- that is fine and even
    /// generous. On a window with lists, text fields and a scroll view in it, every press that misses a
    /// control instead moves the window, so a missed click is never nothing: it is always a small unwanted
    /// nudge, and on a busy panel that happens constantly.
    ///
    /// <b>Assigning <c>draggable</c> per frame is the whole mechanism.</b> <c>DoWindowContents</c> runs before
    /// <c>Window</c> reaches that check, so a value written from the pointer's position during drawing is
    /// already correct by the time it is read. No patch is involved and nothing is reimplemented.
    ///
    /// <b>Clearing it does not leak the click through to the map.</b> That was the thing worth checking before
    /// doing this at all, and vanilla's own <c>else</c> branch handles it: when <c>draggable</c> is false a
    /// <c>MouseDown</c> is consumed instead, which is exactly what a modal window wants. Turning dragging off
    /// costs the absorption nothing.
    ///
    /// <b>The strip reaches the window's real top edge, not the content rect's.</b> <c>DoWindowContents</c> is
    /// handed a rect already inset by <c>Window.Margin</c>, so measuring from there would leave the outermost
    /// band of pixels -- the part of a title bar somebody actually aims at -- unable to drag anything.
    /// </summary>
    public static class UIWindowDrag
    {
        /// <summary>
        /// Lets only the band above <paramref name="titleBottom"/> drag the window.
        /// </summary>
        /// <param name="titleBottom">
        /// The bottom of the title bar, in the same coordinates <c>DoWindowContents</c> is given -- so
        /// normally <c>title.yMax</c>, or <c>inRect.y</c> plus the header height.
        /// </param>
        public static void TitleBarOnly(Window window, float titleBottom)
        {
            if (window == null)
                return;

            Event current = Event.current;

            if (current == null)
                return;

            // Full width and from the very top, so the margin around the title drags with it. Buttons living
            // in the title bar are unaffected: they are drawn during the contents pass and take the press
            // first, and GUI.DragWindow only ever sees an event nothing else wanted.
            Rect strip = new Rect(0f, 0f, window.windowRect.width, titleBottom);

            window.draggable = strip.Contains(current.mousePosition);
        }
    }
}
