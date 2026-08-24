using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// How big the inspect pane is, and the grip that changes it.
    ///
    /// <b>Resizing is the load-bearing decision of the whole rebuild,</b> which is why it is the first file
    /// rather than an afterthought. Vanilla fixes the pane at 165 pixels, a height chosen for a label and one
    /// line of text, and everything the rebuilt pane wants to say needs more than that. A player on a small
    /// screen has to be able to refuse it, so the floor is vanilla's own number: drag it all the way down and
    /// the pane is exactly the size RimWorld ships with, in our chrome and with nothing else added.
    ///
    /// <b>The tab resizer from item 26 could not be reused, and its own comments say why.</b>
    /// <c>TabResizer.Handles</c> excludes <c>MainTabWindow_Inspect</c> deliberately: that window re-runs
    /// <c>SetInitialSizeAndPosition</c> every time the selection changes, so a stored <c>windowRect</c> would be
    /// overwritten within a frame. The pane is sized through <c>RequestedTabSize</c> instead, which is the value
    /// that re-run reads, so the height survives being recomputed rather than fighting it.
    ///
    /// <b>Nothing here writes <c>windowRect</c>.</b> <c>MainTabWindow_Inspect.DoWindowContents</c> already
    /// compares <c>RequestedTabSize</c> against the last one it saw and re-positions the window when they
    /// differ, so changing the stored height is enough and the window moves on the next frame by itself. That is
    /// the same deferral vanilla's own resizer relies on, and doing it by hand mid-draw is how a window ends up
    /// drawing one frame at the old size and one at the new.
    /// </summary>
    internal static class InspectPaneMetrics
    {
        /// <summary>RimWorld's own pane height, which is also the floor a drag can reach.</summary>
        internal const float VanillaHeight = InspectPaneUtility.PaneHeight;

        /// <summary>RimWorld's own tab button width, which sets the pane's minimum width.</summary>
        internal const float VanillaTabWidth = InspectPaneUtility.TabWidth;

        /// <summary>Height of the tab strip above the pane. Vanilla's, because the ITab windows measure from it.</summary>
        internal const float TabStripHeight = InspectPaneUtility.TabHeight;

        /// <summary>
        /// What <c>RecentHeight</c> is set to while a tab is open in its own window.
        ///
        /// Vanilla's own number, written as a bare 700 in <c>DoTabs</c> beside the <c>DoTabGUI</c> call. It is
        /// how the pane tells the rest of the UI that something tall is on screen, and the two go together: the
        /// only place this mod calls <c>DoTabGUI</c> is for an excluded tab, and that call has to carry vanilla's
        /// other half with it or the pane reports the wrong height for as long as the tab is open.
        /// </summary>
        internal const float OpenTabRecentHeight = 700f;

        /// <summary>
        /// The gap vanilla leaves under every main tab, which is the main button bar.
        ///
        /// Taken from the def rather than copied as the literal 35 that <c>MainTabWindow</c> writes, so a bar
        /// that changed height does not leave the pane floating above it or sitting under it.
        /// </summary>
        internal const float BarHeight = MainButtonDef.ButtonHeight;

        /// <summary>
        /// The narrowest the pane is drawn.
        ///
        /// Two columns of facts need this much before either of them starts ellipsing everything in it. Vanilla's
        /// own minimum is 432, six tabs at 72, so this is a little wider and never narrower.
        /// </summary>
        internal const float MinimumWidth = 520f;

        /// <summary>How much screen the pane may never take, so the map is always partly visible.</summary>
        private const float TopMargin = 90f;

        /// <summary>The grab strip along the pane's top edge.</summary>
        internal const float GripHeight = 9f;

        /// <summary>Half the width of the grip's drawn mark. The strip itself spans the pane.</summary>
        private const float GripMarkHalfWidth = 22f;

        /// <summary>Whether a drag is running. Only one pane exists, so one flag is enough.</summary>
        private static bool dragging;

        /// <summary>
        /// Where the top edge sat relative to the cursor when the drag began, so the edge does not jump to the
        /// cursor on the first frame.
        /// </summary>
        private static float grabOffset;

        /// <summary>Set while a drag is running, so the height is written to disk once when it ends.</summary>
        private static bool unsaved;

        /// <summary>Whether the player has the rebuilt pane switched on.</summary>
        internal static bool Enabled
        {
            get
            {
                return UIGuard.Try("Inspector.ReadEnabled",
                    () => UIOverhaulSettingsFile.Current?.richInspectPane ?? true, true,
                    "The rebuilt inspect pane is on, which is the default.");
            }
        }

        /// <summary>The tallest the pane may be on this screen.</summary>
        private static float Ceiling
        {
            get { return Mathf.Max(VanillaHeight, UI.screenHeight - BarHeight - TopMargin); }
        }

        /// <summary>
        /// The pane's height: what the player last dragged it to, clamped to what this screen can hold.
        ///
        /// <b>Clamped on the way out rather than on the way in,</b> because the screen can change size while a
        /// height is stored. Someone who drags the pane tall on a large monitor and then plays windowed would
        /// otherwise get a pane taller than the space above the button bar, and the grip needed to drag it back
        /// would be off the top of the screen.
        /// </summary>
        internal static float Height
        {
            get
            {
                float stored = UIGuard.Try("Inspector.ReadHeight",
                    () => UIOverhaulSettingsFile.Current?.inspectPaneHeight ?? VanillaHeight, VanillaHeight,
                    "The pane opens at its default height.");

                return Mathf.Round(Mathf.Clamp(stored, VanillaHeight, Ceiling));
            }
        }

        /// <summary>
        /// Everything the pane puts around its body: the grip, the header, the gaps and the inspect string.
        ///
        /// <b>Asked of the frame rather than written down here.</b> This was a literal 112 on the reasoning that
        /// the frame's constants are private and are about laying a body out rather than reserving room for one.
        /// That was wrong twice over: the real figure is 153, so every foreign tab was given 41 pixels less than
        /// it asked for and drew itself into a scroll view -- and a number that has to agree with a layout it
        /// cannot see is a number that goes stale the first time either side is edited.
        /// </summary>
        private static float Chrome
        {
            get { return InspectPaneFrame.Chrome; }
        }

        /// <summary>
        /// The tallest a tab can ask to be and still be drawn whole.
        ///
        /// Offered so a tab of ours can size itself to its contents without having to know what the pane spends
        /// on chrome, and without guessing at a number that would go stale the same way the one above did.
        /// </summary>
        internal static float TallestTab
        {
            get { return Mathf.Max(VanillaHeight, Ceiling - Chrome); }
        }

        /// <summary>
        /// How tall the pane has to be for what it is currently showing.
        ///
        /// <b>A tab drawn from another mod gets the size it asked for.</b> An <c>ITab</c> lays itself out at its
        /// declared size and clips nothing, so showing one inside a pane shorter than that is a scroll view over
        /// a third of it. While one is open the pane grows to fit it and goes straight back to the dragged
        /// height afterwards, which is why this is a question about the pane's contents rather than a stored
        /// setting.
        /// </summary>
        internal static float HeightFor(IInspectPane pane)
        {
            float dragged = Height;

            return UIGuard.Try("Inspector.HeightForTab", () =>
            {
                InspectTabBase foreign = InspectTabStrip.OpenForeign(pane);

                if (foreign == null)
                    return dragged;

                float wanted = InspectForeignTab.SizeOf(foreign).y;

                if (wanted <= 0f)
                    return dragged;

                return Mathf.Round(Mathf.Clamp(wanted + Chrome, dragged, Ceiling));
            }, dragged, null);
        }

        /// <summary>How wide the pane has to be for a tab drawn inside it, or nothing when none is.</summary>
        internal static float WidthForTab(IInspectPane pane)
        {
            return UIGuard.Try("Inspector.WidthForTab", () =>
            {
                InspectTabBase foreign = InspectTabStrip.OpenForeign(pane);

                return foreign == null ? 0f : InspectForeignTab.SizeOf(foreign).x + 28f;
            }, 0f, null);
        }

        /// <summary>
        /// The pane's width: wide enough for its tabs, and never narrower than two columns of facts.
        ///
        /// Vanilla's number is the tab count times 72, and it is kept as the floor rather than replaced, because
        /// the tab strip is still laid out across the pane's width. A thing with ten tabs gets a wider pane here
        /// for the same reason it does in vanilla.
        /// </summary>
        internal static float WidthFor(float vanillaWidth)
        {
            return Mathf.Round(Mathf.Max(vanillaWidth, MinimumWidth));
        }

        /// <summary>
        /// The grip, and one frame of the drag if one is running.
        ///
        /// <paramref name="pane"/> is the window's own rect in local coordinates, so the strip is placed along
        /// its top edge. <b>The drag itself is computed in screen space and anchored to the button bar,</b>
        /// exactly as <c>TabResizer.Resized</c> is and for the recorded reason: the window's origin moves as the
        /// height changes, so a delta measured inside the window feeds its own movement back into the cursor
        /// position and the drag accelerates away.
        /// </summary>
        internal static void Grip(Rect pane, UIColorPaletteDef palette)
        {
            Rect strip = new Rect(pane.x, pane.y, pane.width, GripHeight);

            bool over = Mouse.IsOver(strip);

            if (Event.current.type == EventType.Repaint)
            {
                PaintGrip(strip, over || dragging, palette);

                return;
            }

            if (!dragging && over && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                dragging = true;

                // The window's top in screen space is the bar anchor less the height it is currently drawn at.
                grabOffset = UI.screenHeight - BarHeight - Height - UI.MousePositionOnUIInverted.y;

                // Taken here so the pane's own contents do not also see this press. The grip is drawn before
                // them, so the event is still available.
                Event.current.Use();

                return;
            }

            if (!dragging)
                return;

            // Ended on the button being up rather than on a MouseUp event alone: a drag released with the cursor
            // outside the window never delivers one here, and the pane would stay glued to the mouse.
            if (Event.current.type == EventType.MouseUp || !Input.GetMouseButton(0))
            {
                dragging = false;

                if (unsaved)
                {
                    unsaved = false;

                    UIGuard.Try("Inspector.SaveHeight", () => UIOverhaulSettingsFile.Current?.Save(), null);
                }

                return;
            }

            Write(UI.screenHeight - BarHeight - (UI.MousePositionOnUIInverted.y + grabOffset));
        }

        /// <summary>Stores a dragged height, clamped, without touching the disk.</summary>
        private static void Write(float height)
        {
            UIGuard.Try("Inspector.WriteHeight", () =>
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                if (settings == null)
                    return;

                float wanted = Mathf.Round(Mathf.Clamp(height, VanillaHeight, Ceiling));

                if (Mathf.Approximately(settings.inspectPaneHeight, wanted))
                    return;

                settings.inspectPaneHeight = wanted;
                unsaved = true;
            }, "The pane cannot be resized this session.");
        }

        /// <summary>
        /// The grip: a short bar centered on the top edge, lit when it can be grabbed.
        ///
        /// Deliberately not the tab resizer's three diagonal strokes. That mark says "drag this corner in two
        /// directions"; this one moves in one, and a horizontal bar is the shape that says so.
        /// </summary>
        private static void PaintGrip(Rect strip, bool highlighted, UIColorPaletteDef palette)
        {
            Rect mark = new Rect(strip.center.x - GripMarkHalfWidth, strip.y + 3f, GripMarkHalfWidth * 2f, 3f);

            UIElementPainter.FillRounded(mark, highlighted ? palette.Accent : palette.TextDisabled);
        }
    }
}
