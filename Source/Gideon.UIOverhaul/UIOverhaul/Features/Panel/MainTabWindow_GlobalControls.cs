using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Controls;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Panel
{
    /// <summary>
    /// The global controls, as a tab: every play settings toggle the game and its mods offer, in one small panel.
    ///
    /// <b>Why a tab for something that already exists in the corner.</b> The toggle row grows with every mod that
    /// adds to it -- vanilla's own dozen, plus a button from each of Dubs Mint Minimap, Better Pawn Control,
    /// TailorMade and anything else -- and it grows sideways across the bottom of the screen, over the map, in a
    /// single line of unlabelled icons. Moving them somewhere with room means they can be given space and, later,
    /// labels, and it lets the corner be reclaimed by anyone who wants their map back.
    ///
    /// <b>The toggles are vanilla's own, drawn by vanilla.</b> This hands a <c>WidgetRow</c> to
    /// <c>PlaySettings.DoPlaySettingsGlobalControls</c>, which is the same call the corner makes. So every toggle
    /// appears, in the same order, with the same behavior and the same tooltips -- including the ones added by mods
    /// this has never heard of. That method is the only way anything gets into this row: RimWorld has no
    /// PlaySettingDef or registry, so a mod adds a toggle by postfixing it and drawing into the row it is handed.
    /// Calling it is what makes those toggles ours to show; enumerating the list ourselves would mean a panel that
    /// silently omits other people's buttons, which is the failure this design exists to avoid.
    ///
    /// Our own future toggles should go in the same way, by postfixing that method rather than being added here, so
    /// they appear in the corner and in this tab both.
    /// </summary>
    public class MainTabWindow_GlobalControls : MainTabWindow
    {
        /// <summary>
        /// Wide enough for eight toggles on a row before wrapping, which keeps the common case to two rows.
        ///
        /// Vanilla's toggles are 24 wide with a 4 gap, so this is deliberately a multiple of that rather than a
        /// round number: a width that ends mid-icon wastes the remainder on every row.
        /// </summary>
        private const float PanelWidth = 236f;

        private const float Pad = 10f;

        /// <summary>Height of one row of toggles: vanilla's 24 pixel icon plus the row's default 4 gap.</summary>
        private const float RowHeight = 28f;

        /// <summary>Vanilla's icon size, which is the height of the last row on top of everything above it.</summary>
        private const float IconSize = 24f;

        private const float ScrollBarWidth = 16f;

        /// <summary>
        /// How tall the toggles were the last time they drew.
        ///
        /// <b>Measured rather than declared, because the count is not ours to know.</b>
        /// <c>DoPlaySettingsGlobalControls</c> draws rather than reports, and every mod that postfixes it adds to
        /// the row without telling anyone. This used to reserve four rows as a constant, which was fine for vanilla
        /// and clipped silently once enough mods had piled in -- the worst way for it to fail, since a toggle that
        /// is merely missing looks like a toggle that does not exist.
        ///
        /// <c>WidgetRow.FinalY</c> is the seam: with <c>RightThenDown</c> it is the top of the last row, so the
        /// height drawn is that minus where the row started, plus one icon.
        ///
        /// Static because there is only ever one of this tab, and seeded at four rows so the first frame errs
        /// large. Erring large costs one frame of empty panel; erring small hides buttons.
        /// </summary>
        private static float measuredHeight = RowHeight * 4f;

        private Vector2 scroll;

        /// <summary>
        /// Capped, so a mod list with a great many toggles gets a panel that scrolls rather than one that covers
        /// the screen. The cap and the scroll view go together: without the view, capping would clip, which is the
        /// fault this measurement exists to remove.
        /// </summary>
        private float ContentHeight => Mathf.Min(measuredHeight, UI.screenHeight * 0.5f);

        public override Vector2 RequestedTabSize =>
            new Vector2(PanelWidth + Pad * 2f, ContentHeight + Pad * 2f);

        protected override float Margin => 0f;

        /// <summary>
        /// Resizes the window when the measurement changes.
        ///
        /// <c>RequestedTabSize</c> is only read when the tab opens, so a panel that measured differently while it
        /// was on screen would keep its old size until it was closed and opened again. The pawns tab does the same
        /// thing for the same reason.
        /// </summary>
        public override void WindowUpdate()
        {
            base.WindowUpdate();

            if (Mathf.Abs(windowRect.height - (ContentHeight + Pad * 2f)) > 0.5f)
                SetInitialSizeAndPosition();
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Panel.GlobalControlsTab", inRect, () => DrawContents(inRect),
                "This tab shows a failure notice. The same toggles are still in the bottom right corner of the "
                + "screen unless they have been switched off in this mod's settings.");
        }

        private void DrawContents(Rect inRect)
        {
            Widgets.DrawBoxSolid(inRect, UIColorPaletteDef.Active.WindowBackground);

            Rect content = inRect.ContractedBy(Pad);

            bool scrolls = measuredHeight > content.height + 0.5f;
            float width = content.width - (scrolls ? ScrollBarWidth : 0f);

            Rect view = new Rect(0f, 0f, width, measuredHeight);
            Widgets.BeginScrollView(content, ref scroll, view);

            // RightThenDown so a long list wraps within the panel instead of running off the edge, which is what
            // the corner row does when enough mods add to it. maxWidth is the view's width rather than the
            // panel's, so the wrap point moves in when a scrollbar appears rather than running under it.
            WidgetRow row = new WidgetRow(0f, 0f, UIDirection.RightThenDown, width);

            // <b>Not on key presses, and this is not an optimization.</b> DoPlaySettingsGlobalControls handles
            // three keyboard shortcuts as it draws -- beauty display, room stats and map search -- and vanilla's
            // CheckKeyBindingToggle does not call Event.current.Use(). So while this tab is open, the corner and
            // this panel would both see the same key press: beauty would toggle on and straight back off, and
            // the shortcut would appear dead with two checkbox sounds to show for it.
            //
            // The corner is where those shortcuts are handled, including when it is hidden -- the patch that
            // hides it runs the row off screen precisely so they keep working. This panel only ever draws, and
            // nothing it draws needs a KeyDown event to do it.
            bool drew = Event.current.type != EventType.KeyDown;

            if (drew)
            {
                // worldView false: this is the map's set of toggles. The world view has its own, shorter list,
                // and a tab that showed map toggles while looking at the planet would offer buttons that do
                // nothing.
                Find.PlaySettings.DoPlaySettingsGlobalControls(row, false);
            }

            Widgets.EndScrollView();

            // Read after the draw, since it is the drawing that moves the cursor. A row that wrapped three times
            // leaves FinalY three pitches below where it started. Skipped on the frames the row did not run, or
            // the panel would measure itself as empty every time a key was pressed.
            if (drew)
                measuredHeight = Mathf.Max(RowHeight, row.FinalY + IconSize);
        }
    }
}
