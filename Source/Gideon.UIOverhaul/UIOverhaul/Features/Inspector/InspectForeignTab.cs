using System.Reflection;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// Drawing somebody else's inspect tab inside our pane instead of as a window over it.
    ///
    /// <b>Because half the tabs rendering in the pane and half popping out of it is worse than either.</b> The
    /// six we rebuilt draw in place; a modded tab, a workbench's bills, an animal's training and a prisoner's
    /// settings all opened their own floating window, so clicking along the chip row made the panel jump between
    /// two completely different shapes. That is the complaint, and the fix is to give every chip the same
    /// behaviour rather than to give ours the popup back.
    ///
    /// <b>The pane grows to the tab rather than the tab being squeezed into the pane.</b> An <c>ITab</c> declares
    /// its own size and lays itself out at that size, so a 630 by 510 bills tab crammed into a 300 pixel pane
    /// would be a scroll view showing a third of it -- genuinely worse than the window it replaced. While a
    /// foreign tab is showing, the pane asks for exactly the height and width that tab wants, and goes back to
    /// the dragged height the moment one of ours is chosen again. See <c>InspectPaneMetrics.HeightFor</c>.
    ///
    /// <b>Reflection, because <c>FillTab</c> and <c>size</c> are protected.</b> There is no public way to draw an
    /// <c>InspectTabBase</c> anywhere but where <c>DoTabGUI</c> puts it. Both members are declared on
    /// <c>InspectTabBase</c> itself rather than on any subclass, so one lookup covers every tab in the game
    /// including ones from mods, and invoking the base method dispatches to the override.
    /// </summary>
    internal static class InspectForeignTab
    {
        private const BindingFlags Hidden =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static MethodInfo fillTab;

        private static MethodInfo updateSize;

        private static FieldInfo sizeField;

        private static bool resolved;

        private static bool available;

        /// <summary>The tab's own idea of how big it wants to be, or zero when it cannot be read.</summary>
        internal static Vector2 SizeOf(InspectTabBase tab)
        {
            if (tab == null || !Resolve())
                return Vector2.zero;

            return UIGuard.Try("Inspector.ForeignSize", () =>
            {
                // Asked before reading, exactly as TabRect does: several tabs size themselves from their current
                // contents, and a bills tab that grew a row is the wrong size until this has run.
                updateSize.Invoke(tab, null);

                return (Vector2) sizeField.GetValue(tab);
            }, Vector2.zero, null);
        }

        /// <summary>
        /// Draws the tab into <paramref name="rect"/>, scrolling if the pane could not grow far enough.
        ///
        /// <b>A group at the tab's own size, not at the rect's.</b> Every tab lays out from zero to its declared
        /// size and clips nothing itself, so handing it a smaller space would overlap its rows rather than
        /// shorten its list. The scroll view is the honest way to show a tab too big for the screen, and on any
        /// normal screen it never appears because the pane has already grown.
        /// </summary>
        internal static void Draw(Rect rect, InspectTabBase tab)
        {
            if (tab == null || !Resolve() || rect.width <= 0f || rect.height <= 0f)
                return;

            Vector2 size = SizeOf(tab);

            // <b>A zero dimension means "not measured yet", not "nothing to draw".</b> ITab_Pawn_Visitor -- and
            // so the prisoner and slave tabs -- is constructed with a height of zero and sets its real height at
            // the <i>end</i> of FillTab, from the listing it just drew. Vanilla gets away with that because
            // DoTabGUI opens a zero-height window and the tab fixes itself on the next frame; refusing to draw
            // at zero height instead is a deadlock, because the height is only ever learned by drawing. The
            // body's own rect stands in until the tab has told us better.
            float width = size.x > 1f ? size.x : rect.width;
            float height = size.y > 1f ? size.y : rect.height;

            Rect view = new Rect(0f, 0f, Mathf.Max(width, rect.width - 18f), height);

            bool scrolling = view.height > rect.height || view.width > rect.width;

            if (scrolling)
                Widgets.BeginScrollView(rect, ref InspectPaneState.Scroll, view);
            else
                Widgets.BeginGroup(rect);

            try
            {
                // The inner group is what puts the tab's own zero where it expects it. With a scroll view that is
                // the scrolled origin; without one it is the rect's corner.
                Widgets.BeginGroup(scrolling ? view : rect.AtZero());

                try
                {
                    // Arbitrary code from whichever mod owns the tab, so a throw costs the tab's contents and
                    // nothing else. The chip stays lit and the pane keeps its header and its inspect string.
                    UIGuard.Try("Inspector.ForeignTab", () => fillTab.Invoke(tab, null), null,
                        "That tab could not be drawn inside the inspect pane.");
                }
                finally
                {
                    Widgets.EndGroup();
                }
            }
            finally
            {
                if (scrolling)
                    Widgets.EndScrollView();
                else
                    Widgets.EndGroup();
            }
        }

        /// <summary>
        /// Finds the three hidden members once.
        ///
        /// <b>If any of them is missing, nothing here is used and the tabs go back to opening their own
        /// windows.</b> That is not a fallback to vanilla for one of our panels -- these are RimWorld's panels
        /// and always were -- it is the difference between drawing somebody else's tab in a new place and not
        /// drawing it at all.
        /// </summary>
        private static bool Resolve()
        {
            if (resolved)
                return available;

            resolved = true;

            available = UIGuard.Try("Inspector.ResolveTabMembers", () =>
            {
                fillTab = typeof(InspectTabBase).GetMethod("FillTab", Hidden);
                updateSize = typeof(InspectTabBase).GetMethod("UpdateSize", Hidden);
                sizeField = typeof(InspectTabBase).GetField("size", Hidden);

                return fillTab != null && updateSize != null && sizeField != null;
            }, false, "Inspect tabs from other mods open in their own window rather than inside the pane.");

            return available;
        }
    }
}
