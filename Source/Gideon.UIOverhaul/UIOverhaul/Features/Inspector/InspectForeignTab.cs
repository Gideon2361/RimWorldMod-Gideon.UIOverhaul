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

        /// <summary>
        /// The size a foreign tab is being drawn at, while one is being drawn. Zero the rest of the time.
        ///
        /// <b>Published for the tabs that cannot be told any other way.</b> Stretching <c>size</c> reaches every
        /// tab that lays out from it, which is most of them -- but several of vanilla's, <c>ITab_Storage</c>
        /// among them, keep a private <c>WinSize</c> constant and lay out from that instead, so the field they
        /// were handed is never read. Those need a patch of their own, and this is how such a patch learns how
        /// much room there is. See <c>Patch_StorageTabSize</c>.
        /// </summary>
        internal static Vector2 Hosting { get; private set; }

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
        /// Draws the tab into <paramref name="rect"/>, stretching it to fill and scrolling if it will not fit.
        ///
        /// <b>Never smaller than the tab asked for.</b> Every tab lays out from zero to its declared size and
        /// clips nothing itself, so handing it a smaller space would overlap its rows rather than shorten its
        /// list. The scroll view is the honest way to show a tab too big for the screen, and on any normal screen
        /// it never appears because the pane has already grown.
        ///
        /// <b>And never smaller than the pane either, which needed the tab's own field to be written.</b> Asked
        /// for on 2026-08-23, against a stockpile whose filter sat in a 300 pixel column inside a much wider pane
        /// and a growing zone whose two bill rows floated above four hundred pixels of nothing. <c>FillTab</c>
        /// takes no rect -- a tab reads its own <c>size</c> field and lays out to that -- so the only way to
        /// offer a tab more room is to tell it it is bigger. Written just before the call and put back
        /// immediately after, because that field is also what <c>InspectPaneMetrics</c> measures the pane from:
        /// left stretched, a tab that had been shown in a tall pane would go on claiming that height forever and
        /// the pane could never be dragged smaller again.
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

            // Height first, because whether the tab overflows vertically is what decides if a scrollbar takes
            // eighteen pixels off the width it is then stretched to.
            height = Mathf.Max(height, rect.height);

            width = Mathf.Max(width, height > rect.height ? rect.width - 18f : rect.width);

            Rect view = new Rect(0f, 0f, width, height);

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
                    // Stretched for the length of the call only; the restore is in the finally for the same
                    // reason the group's is, since this one throws through arbitrary code.
                    UIGuard.Try("Inspector.ForeignTab", () =>
                    {
                        Stretch(tab, view.size);
                        Hosting = view.size;

                        try
                        {
                            fillTab.Invoke(tab, null);
                        }
                        finally
                        {
                            Hosting = Vector2.zero;

                            Restore(tab, view.size, size);
                        }
                    }, "That tab could not be drawn inside the inspect pane.");
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

        /// <summary>Tells the tab it is as big as the space it has been given, for the length of one draw.</summary>
        private static void Stretch(InspectTabBase tab, Vector2 size)
        {
            if (size.x > 1f && size.y > 1f)
                sizeField.SetValue(tab, size);
        }

        /// <summary>
        /// Puts the tab's own size back, unless the tab set one itself while it was drawing.
        ///
        /// <b>That exception is <c>ITab_Pawn_Visitor</c> and everything shaped like it.</b> Those measure
        /// themselves at the <i>end</i> of <c>FillTab</c>, from the listing they have just drawn, rather than in
        /// <c>UpdateSize</c> -- so a blind restore would overwrite this frame's measurement with last frame's and
        /// leave the tab permanently one frame behind its own contents. Comparing against what was written is
        /// the exact test for "did it answer": unchanged means the tab never looked, and anything else is its
        /// own answer and is left alone.
        /// </summary>
        private static void Restore(InspectTabBase tab, Vector2 written, Vector2 original)
        {
            Vector2 now = (Vector2) sizeField.GetValue(tab);

            if (now == written)
                sizeField.SetValue(tab, original);
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
