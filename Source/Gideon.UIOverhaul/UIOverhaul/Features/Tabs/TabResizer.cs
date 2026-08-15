using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Tabs
{
    /// <summary>
    /// Lets an open main tab be dragged to a different size, from the corner that is free to move.
    ///
    /// <b>Vanilla has a window resizer and it is the wrong shape for these windows.</b> <c>WindowResizer</c> puts
    /// its grip at the bottom right and grows the window from its top left, which is correct for a floating
    /// dialog and wrong for a main tab in two ways at once. A tab is anchored to the <i>bottom</i> of the screen
    /// -- <c>SetInitialSizeAndPosition</c> sets <c>windowRect.y</c> to the bar's top minus the height -- so the
    /// bottom right grip sits on the edge nearest the main button bar, and dragging it downward would grow the
    /// window straight off the screen instead of upward into the space that is actually free.
    ///
    /// <b>So the grip goes on the corner diagonally opposite the anchor.</b> A left anchored tab is pinned at its
    /// bottom left and grows from the top right; a right anchored one is pinned at its bottom right and grows
    /// from the top left. The two fixed edges never move, which is what makes the tab stay welded to the corner
    /// of the screen it belongs to while its size changes.
    ///
    /// <b>The drag is computed in screen coordinates, and it has to be.</b> Inside a <c>GUI.Window</c> callback
    /// the mouse position is relative to the window's own origin -- and that origin <i>moves</i> as the window
    /// resizes, since the top edge travels when the height changes. Working in local coordinates would feed the
    /// window's movement back into the mouse delta and the drag would accelerate away from the cursor.
    /// <c>UI.MousePositionOnUIInverted</c> is the same position in screen space, which does not move.
    ///
    /// <b>Vanilla's deferral is reused rather than reinvented.</b> <c>Window</c> does not apply a resize
    /// immediately: <c>InnerWindowOnGUI</c> is already running inside a <c>GUI.Window</c> that was opened with
    /// the old rect, so it stores the new one and swaps it in at the top of the next frame. This returns its rect
    /// through the same method vanilla calls, so that machinery does the applying and nothing here touches
    /// <c>windowRect</c> mid-draw.
    /// </summary>
    internal static class TabResizer
    {
        /// <summary>Size of the grab corner. Large enough to hit without hunting, small enough to stay out of the way.</summary>
        internal const float GripSize = 20f;

        /// <summary>
        /// The floor a tab can be dragged to.
        ///
        /// Not applied blindly: a tab whose own requested size is already smaller keeps that as its floor
        /// instead, so a compact tab -- the architect menu, or a small modded one -- can still be dragged back
        /// down to the size its author chose rather than being forced to this.
        /// </summary>
        private const float MinWidth = 220f;

        private const float MinHeight = 180f;

        /// <summary>
        /// The gap vanilla leaves between the bottom of a tab and the bottom of the screen, which is the main
        /// button bar.
        ///
        /// <c>MainTabWindow.SetInitialSizeAndPosition</c> writes this as a literal 35, but the same number is
        /// declared as <c>MainButtonDef.ButtonHeight</c>, so it is taken from there rather than copied. Every
        /// anchored edge in this file measures from it, and a bar that changed height while this said 35 would
        /// put every resized tab either over the buttons or floating above them.
        /// </summary>
        private const float BarHeight = MainButtonDef.ButtonHeight;

        /// <summary>
        /// The tab being dragged, or null. One at a time is enough: <c>MainTabsRoot</c> keeps a single tab open,
        /// and the one window that can sit alongside it is excluded below.
        /// </summary>
        private static MainTabWindow dragging;

        /// <summary>
        /// Where the grabbed corner sat relative to the cursor when the drag began.
        ///
        /// Kept so the corner does not jump to the cursor on the first frame. Without it, clicking anywhere in
        /// the grip that is not exactly the corner point snaps the window by that much.
        /// </summary>
        private static Vector2 grabOffset;

        /// <summary>Whether the player has this switched on.</summary>
        private static bool Enabled =>
            UIGuard.Try("Tabs.ReadResizable", () => UIOverhaulSettingsFile.Current?.resizableTabs ?? true, true,
                "Tabs can be resized, which is the default.");

        /// <summary>
        /// Whether this tab takes a resize grip.
        ///
        /// <b>The inspect pane is excluded, and not for tidiness.</b> It sizes itself from what is selected --
        /// <c>MainTabWindow_Inspect</c> calls <c>SetInitialSizeAndPosition</c> again every time the selection
        /// changes or its requested size does -- so pinning it to a stored size would freeze it at whatever suited
        /// one particular thing and clip the pane for everything taller.
        /// </summary>
        internal static bool Handles(MainTabWindow tab)
        {
            return tab != null && Enabled && !(tab is MainTabWindow_Inspect);
        }

        /// <summary>
        /// Applies the stored size, and marks the window resizable or not.
        ///
        /// Called from a postfix on <c>SetInitialSizeAndPosition</c>, which is the one place a tab's rect is
        /// established -- on open, on a resolution change, and for the two tabs that re-run it during a session.
        /// Nothing overrides that method, so every tab passes through here.
        ///
        /// <b>Clamped on the way in rather than trusted.</b> A size stored on a larger monitor, or hand-edited,
        /// must not open a tab wider than the screen -- the far edge would be unreachable, and with it the grip
        /// needed to drag it back.
        /// </summary>
        internal static void Apply(MainTabWindow tab)
        {
            bool resizable = Handles(tab);

            // Assigned in both directions. Leaving it true after the setting is switched off would hand the
            // window back to vanilla's resizer, which would draw its own grip on the bottom edge -- the exact
            // arrangement this exists to avoid.
            tab.resizeable = resizable;

            Vector2 stored;

            if (!resizable || tab.def == null || !TabSizes.TryGet(tab.def.defName, out stored))
                return;

            float bottom = UI.screenHeight - BarHeight;

            // Rounded here as well as in Resized, and for the same reason: a size that arrives fractional from a
            // hand-edited file would otherwise put the window on a half pixel, and Rounded() truncates the
            // position and the size separately, which is what lets an anchored edge slip.
            float width = Mathf.Round(Mathf.Clamp(stored.x, MinWidthFor(tab), UI.screenWidth));
            float height = Mathf.Round(Mathf.Clamp(stored.y, MinHeightFor(tab), bottom));

            float x = tab.Anchor == MainTabWindowAnchor.Left ? 0f : UI.screenWidth - width;

            // No Rounded() call: every value above is already whole, and applying it would only reintroduce the
            // truncation this is avoiding.
            tab.windowRect = new Rect(x, bottom - height, width, height);
        }

        /// <summary>
        /// One frame of the resize control: the grip, and the drag if one is running.
        ///
        /// Returns the rect the window should end up with, which is the contract
        /// <c>WindowResizer.DoResizeControl</c> already has with <c>Window</c>.
        ///
        /// <paramref name="windowRect"/> arrives in screen space, but this is called from inside the window's own
        /// GUI callback, so the grip is placed in local coordinates -- the same mixture vanilla's resizer works
        /// in, for the same reason: the size half of the rect is what positions the grip and the position half is
        /// what the caller needs back.
        /// </summary>
        internal static Rect Control(MainTabWindow tab, Rect windowRect)
        {
            bool left = tab.Anchor == MainTabWindowAnchor.Left;

            // The corner opposite the anchor. A left anchored tab is pinned bottom left, so the top right is what
            // moves; a right anchored one is pinned bottom right, so the top left moves.
            Rect grip = left
                ? new Rect(windowRect.width - GripSize, 0f, GripSize, GripSize)
                : new Rect(0f, 0f, GripSize, GripSize);

            bool over = Mouse.IsOver(grip);
            bool active = dragging == tab;

            // Repaint carries no input and is the pass that happens after the window's contents are drawn, which
            // is why the grip is painted there and nowhere else: anything drawn before the contents would be
            // covered by them.
            if (Event.current.type == EventType.Repaint)
            {
                Paint(grip, over || active, left);

                return windowRect;
            }

            Vector2 corner = new Vector2(left ? windowRect.xMax : windowRect.x, windowRect.y);

            if (!active && over && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                dragging = tab;
                grabOffset = corner - UI.MousePositionOnUIInverted;

                // Consumed so the tab's own contents do not also see this click. This runs before the contents
                // are drawn, so the event is still there to take.
                Event.current.Use();

                return windowRect;
            }

            if (!active)
                return windowRect;

            // Ended on the button being up rather than on a MouseUp event alone. A drag that finishes with the
            // cursor outside the window never delivers MouseUp here, and the window would stay glued to the
            // mouse afterwards.
            if (Event.current.type == EventType.MouseUp || !Input.GetMouseButton(0))
            {
                dragging = null;

                if (tab.def != null)
                {
                    TabSizes.Set(tab.def.defName, new Vector2(windowRect.width, windowRect.height));
                    TabSizes.SaveIfNeeded();
                }

                return windowRect;
            }

            // The current rect is deliberately not passed. Everything the new one needs comes from the screen and
            // the cursor; feeding it its own last answer is what made it drift. See Resized.
            return Resized(tab, UI.MousePositionOnUIInverted + grabOffset, left);
        }

        /// <summary>
        /// The new rect for a corner dragged to <paramref name="corner"/>, keeping the two anchored edges where
        /// they are.
        ///
        /// <b>The anchored edges are measured from the screen, never from the rect being resized.</b> That
        /// distinction is the whole of a bug worth recording, because the obvious version reads better and
        /// silently destroys itself.
        ///
        /// It used to take the fixed bottom as <c>rect.yMax</c> and return
        /// <c>new Rect(x, bottom - height, (int) width, (int) height)</c>. Truncating the height while deriving
        /// the top from the untruncated one moves the bottom edge up by the fractional part, and
        /// <c>Window.WindowOnGUI</c> then applies <c>Rounded()</c> -- which is <c>(int)</c>, a truncation rather
        /// than a rounding -- to the position and the size independently, biasing it up again. The next frame
        /// read that lowered <c>yMax</c> back as the anchor and did it once more. The result was a tab that
        /// climbed the screen about a pixel a frame while its top followed the cursor, so it shrank as it rose:
        /// a resize that made the window smaller the longer it was dragged.
        ///
        /// Pinning to <c>UI.screenHeight - BarHeight</c> and to the screen edges instead makes every frame
        /// recompute from a fixed point rather than from its own last answer, so nothing can accumulate. It also
        /// makes the drag self-correcting: if another mod nudges the rect between frames, the next frame puts it
        /// back where it belongs instead of building on the displacement.
        ///
        /// Every value here is whole, and deliberately. The anchors are integers, the size is rounded once, and
        /// the position is derived from the rounded size, so <c>Rounded()</c> has nothing left to change and the
        /// rect is a fixed point when the mouse stops moving.
        /// </summary>
        private static Rect Resized(MainTabWindow tab, Vector2 corner, bool left)
        {
            // The same anchor Apply uses, and the same one vanilla positions tabs against. The two disagreeing
            // was what made this wrong.
            float bottom = UI.screenHeight - BarHeight;

            // The ceiling is the top of the screen, which is what bottom measures down from. A tab cannot be
            // taller than the space above the button bar because that space is all there is.
            float height = Mathf.Round(Mathf.Clamp(bottom - corner.y, MinHeightFor(tab), bottom));

            float width = Mathf.Round(left
                ? Mathf.Clamp(corner.x, MinWidthFor(tab), UI.screenWidth)
                : Mathf.Clamp(UI.screenWidth - corner.x, MinWidthFor(tab), UI.screenWidth));

            float x = left ? 0f : UI.screenWidth - width;

            return new Rect(x, bottom - height, width, height);
        }

        /// <summary>
        /// The smallest width this tab may be dragged to: our floor, or the tab's own requested width if that is
        /// already smaller.
        ///
        /// Guarded because <c>RequestedTabSize</c> is a property any mod may override with anything.
        /// </summary>
        private static float MinWidthFor(MainTabWindow tab)
        {
            return Mathf.Min(MinWidth, UIGuard.Try("Tabs.ReadRequestedSize",
                () => tab.RequestedTabSize.x, MinWidth,
                "One tab uses the standard minimum size."));
        }

        private static float MinHeightFor(MainTabWindow tab)
        {
            return Mathf.Min(MinHeight, UIGuard.Try("Tabs.ReadRequestedSize",
                () => tab.RequestedTabSize.y, MinHeight,
                "One tab uses the standard minimum size."));
        }

        /// <summary>
        /// The grip: three diagonal strokes hugging the corner, in the theme rather than vanilla's texture.
        ///
        /// Angled to lie along the corner they belong to, so the mark itself says which way it is dragged. Drawn
        /// only on the corner that moves, which is the other half of saying so.
        /// </summary>
        private static void Paint(Rect grip, bool highlighted, bool left)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            Color color = highlighted ? palette.Accent : palette.TextDisabled;

            float x = left ? grip.xMax : grip.x;
            float direction = left ? -1f : 1f;

            for (int i = 0; i < 3; i++)
            {
                float reach = 5f + i * 5f;

                Widgets.DrawLine(new Vector2(x + direction * reach, grip.y),
                    new Vector2(x, grip.y + reach), color, 1.5f);
            }
        }
    }
}
