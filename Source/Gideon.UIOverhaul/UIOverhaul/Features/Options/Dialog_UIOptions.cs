using System.Collections.Generic;
using Gideon.UIFramework.Components.Colors;
using Gideon.UIFramework.Components.Images;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIFramework.Patches.UIElements;
using Gideon.UIOverhaul.Features.ButtonBar;
using Gideon.UIOverhaul.Features.ButtonBar.BarWidgets;
using Gideon.UIOverhaul.Features.ColonyBar;
using Gideon.UIOverhaul.Features.DevTools;
using Gideon.UIOverhaul.Features.Diagnostics;
using Gideon.UIOverhaul.Features.FloorLabels;
using Gideon.UIOverhaul.Features.Integrations;
using Gideon.UIOverhaul.Features.Minimap;
using Gideon.UIOverhaul.Features.Notifications;
using Gideon.UIOverhaul.Features.Panel;
using Gideon.UIOverhaul.Features.Saves;
using Gideon.UIOverhaul.Features.Tabs;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Options
{
    /// <summary>
    /// This mod's settings, as a window of our own.
    ///
    /// It began as a category in the vanilla Options window, which does not work: Dialog_Options builds
    /// its category list from DefDatabase&lt;OptionCategoryDef&gt;.AllDefs but skips any def whose
    /// ModContentPack is not official, logging "Unofficial OptionCategoryDef ... ignoring". Short of
    /// patching that filter, a mod cannot add a category at all -- so the settings live here instead,
    /// reached from the bar button, and get the theme applied to them rather than inheriting vanilla's.
    /// </summary>
    public class Dialog_UIOptions : Window
    {
        private const float HeaderHeight = 52f;
        private const float FooterHeight = 46f;
        private const float Pad = 16f;
        private const float RowHeight = 32f;

        /// <summary>
        /// Width of the category column.
        ///
        /// Sized for mod names rather than for ours. Our own six are short and fitted comfortably in 196; the
        /// children are named by whoever wrote them, and "Vanilla Expanded Framework" or longer is ordinary.
        /// Widened until the common ones fit, with the labels set to ellipsize so the ones that still do not are
        /// cut deliberately rather than clipped mid-letter, and the card's tooltip carries the full name.
        /// </summary>
        private const float ColumnWidth = 260f;

        private const float ColumnGap = 10f;
        /// <summary>
        /// Two lines of text and the padding around them.
        ///
        /// Sized for <c>Small</c> on both lines rather than for the <c>Tiny</c> the blurb asks for, because
        /// <c>Text.Font = GameFont.Tiny</c> is a request rather than a result: the setter substitutes
        /// <c>Small</c> whenever <c>TinyFontSupported</c> is false, which covers several languages, the
        /// "disable tiny text" preference and the Steam Deck. A rect shorter than the line it holds does not
        /// shrink the text, it shaves the top and bottom off it.
        /// </summary>
        private const float CardHeight = 56f;

        private const float CardPadding = 6f;
        private const float CardLine = 22f;
        private const float CardGap = 6f;

        /// <summary>How far a child card sits in from its parent.</summary>
        private const float ChildIndent = 14f;

        /// <summary>
        /// The mod icon on a card. Square, and the height of the two text lines it sits beside.
        /// </summary>
        private const float IconSize = 32f;

        private const float IconGap = 8f;

        private Vector2 scroll;
        private Vector2 categoryScroll;

        private List<Category> categories;

        /// <summary>
        /// Which category is showing, and which of its children if it has any.
        ///
        /// Static, so closing the window and opening it again comes back to where the player was rather than to
        /// the top of the list. Someone adjusting a setting and checking the result is going to do that several
        /// times in a row, and sending them back to Theme each time would make the window feel like it forgot.
        ///
        /// Indices rather than a reference to the category itself, precisely because they are static: the
        /// category objects are rebuilt per window, so a held reference would point at the previous window's
        /// list. The lists are built the same way every time, so the indices survive where a reference would not.
        /// </summary>
        private static int selectedCategory;

        /// <summary>Which child of the selected category, or -1 for the category itself.</summary>
        private static int selectedChild = -1;

        /// <summary>
        /// The mod whose settings were last drawn, so its <c>WriteSettings</c> can be called when we leave it.
        ///
        /// Vanilla's <c>Dialog_ModSettings</c> writes on close, because closing is the only way to leave it.
        /// Here a player can click straight from one mod to the next without the window going anywhere, and that
        /// is a leave too -- so this is the record of what to write when it happens.
        /// </summary>
        private Mod lastSettingsMod;

        /// <summary>
        /// The windows that were open just before a mod's settings page drew.
        ///
        /// Kept so the page can be checked afterwards for having opened one of its own. Reused rather than
        /// allocated per call, because this runs on every pass of every frame a mod page is showing and the
        /// stack is short enough that clearing and refilling it costs nothing worth measuring.
        /// </summary>
        private static readonly List<Window> WindowsBeforePage = new List<Window>();

        /// <summary>
        /// Wide enough that the settings pane is no narrower than the single column it replaced.
        ///
        /// The column, its gap and the window padding take a little over 200, so the old 620 would have left the
        /// settings themselves with 50 less than before. Every explanatory paragraph in this window is drawn into
        /// a rect of a height fixed for the number of lines it takes at the width it was written for, and a
        /// narrower pane is what turns a two line paragraph into a three line one that runs out of its rect.
        /// </summary>
        /// <summary>How far the settings pane is inset from its panel.</summary>
        private const float PaneInset = 10f;

        /// <summary>
        /// Sized so a mod settings page fits at its authored size, rather than sized for our own sections.
        ///
        /// <b>Derived from the parts rather than written as a number.</b> Everything between the window edge and
        /// the pane is accounted for here -- the outer padding, the category column, the gap after it, and the
        /// pane's own inset -- so changing any of those keeps the window the right size instead of silently
        /// putting a mod page back under the scale factor.
        ///
        /// One size whatever is selected. A window that grew when a mod was picked would jump under the cursor,
        /// which is worse than a Theme page with room around it.
        ///
        /// Clamped to the screen, because a window wider than the display cannot be reached. The clamp is not a
        /// failure case: <see cref="DrawModSettings"/> still scales to whatever it is given, so a small display
        /// gets the page shrunk to fit rather than cropped.
        /// </summary>
        private static float RequiredWidth =>
            LargestPane.x + PaneInset * 2f + ColumnWidth + ColumnGap + Pad * 2f;

        private static float RequiredHeight =>
            LargestPane.y + PaneInset * 2f + HeaderHeight + FooterHeight;

        public override Vector2 InitialSize => new Vector2(
            Mathf.Min(RequiredWidth, UI.screenWidth - 20f),
            Mathf.Min(RequiredHeight, UI.screenHeight - 20f));

        /// <summary>
        /// Writes the open mod's settings out, and RimWorld's own prefs with them, the way closing vanilla's
        /// dialog would.
        ///
        /// <b><c>Prefs.Save()</c> is the half that was missing, and it cost every preference this window sets.</b>
        /// Reported on 2026-08-25 as an autosave interval that would not survive a restart, and the interval was
        /// only the one somebody noticed. Every <c>Prefs</c> property has a setter that calls <c>Apply()</c> and
        /// nothing else: the value takes effect immediately and lives in memory, and the file on disk is written
        /// by exactly one thing, which is whoever closes the window. Vanilla's <c>Dialog_Options.PreClose</c> does
        /// this on the line after <c>base.PreClose()</c>; we replaced that window and did not replace the line.
        ///
        /// Nothing else in the game would have covered for it. There is no save on quit and no periodic flush --
        /// <c>Prefs.Save</c> has four callers in the whole assembly, and the other three are the dev palette, the
        /// resolution helper and the main menu's language button, each saving after its own change. So a player
        /// who set an interval, played for six hours and quit lost it, with nothing to see but the old number.
        ///
        /// Ordered after the mod settings, so a mod page that writes prefs of its own in <c>Write()</c> is
        /// included rather than missing this pass by one line.
        /// </summary>
        public override void PreClose()
        {
            base.PreClose();
            LeaveModSettings();

            UIGuard.Try("Options.SavePrefs", Prefs.Save,
                "RimWorld's own preferences were not written to disk, so anything changed on the Game page will "
                + "be back to its previous value next time the game starts.");
        }

        protected override float Margin => 0f;

        /// <summary>
        /// Opens the settings window.
        /// </summary>
        /// <param name="pauseGame">
        /// Whether the colony stops while the window is up.
        ///
        /// <b>A parameter rather than a fixed value, because the two ways in want different answers.</b> Escape
        /// pauses, because that is what Escape did when it opened RimWorld's menu and because the alternative is
        /// a raid continuing behind a settings window the player opened to get away from it. The button on the
        /// bar does not, because somebody adjusting a colour while the colony runs has not asked for time to
        /// stop.
        /// </param>
        public Dialog_UIOptions(bool pauseGame = false)
        {
            // Every open starts at the top of the list.
            //
            // The selection is static so that rebuilding the category objects cannot lose it, which is a
            // different thing from carrying it across a close and a reopen. It was doing both, so the window
            // came back wherever it was last left. That is the wrong first impression for a window that is now
            // what Escape opens: Game Settings is pinned first and holds saving, loading and quitting, which is
            // what somebody reaching for Escape came for.
            selectedCategory = 0;
            selectedChild = -1;

            doCloseX = false;
            forcePause = pauseGame;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIWindowDrag.TitleBarOnly(this, inRect.y + HeaderHeight);

            UIGuardedPanel.Draw("Options.Window", inRect, () => DrawContents(inRect),
                "The settings window shows a failure notice; settings already saved are unaffected.");
        }

        private void DrawContents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

            // No fill here. Margin is zero, so inRect is the whole window, and RimWorld has already painted
            // exactly this color across it through the patched Widgets.DrawWindowBackground -- along with the
            // border, which a second fill over the top was quietly erasing.

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            // Header
            Text.Font = GameFont.Medium;
            GUI.color = palette.TextPrimary;
            Widgets.Label(new Rect(inRect.x + Pad, inRect.y + 12f, inRect.width - Pad * 3f - 24f, 32f),
                "Options");

            Rect closeRect = new Rect(inRect.xMax - Pad - 24f, inRect.y + 14f, 24f, 24f);
            if (SmallButton(closeRect, "X", palette))
                Close();

            Rect body = new Rect(inRect.x + Pad, inRect.y + HeaderHeight,
                inRect.width - Pad * 2f, inRect.height - HeaderHeight - FooterHeight);

            EnsureCategories();

            Rect column = new Rect(body.x, body.y, ColumnWidth, body.height);
            Rect pane = new Rect(column.xMax + ColumnGap, body.y,
                body.width - ColumnWidth - ColumnGap, body.height);

            Text.Font = GameFont.Small;

            DrawCategoryColumn(column, palette);

            // Only the settings pane gets the panel fill. The category cards carry their own, so leaving the
            // column on the window background is what makes them read as cards sitting on it rather than as
            // rows ruled inside a second panel.
            Widgets.DrawBoxSolid(pane, palette.PanelBackground);

            Category current = Resolve();

            Rect inner = pane.ContractedBy(PaneInset);

            // Writing out happens on the way past rather than on the way in, so a mod that was being edited a
            // moment ago has its settings saved whether the player moved to another mod, to one of our own
            // sections, or closed the window.
            if (current.Mod != lastSettingsMod)
                LeaveModSettings();

            if (current.RawPane)
            {
                lastSettingsMod = current.Mod;

                if (DrawModSettings(inner, current.Mod))
                {
                    // The page is done with this window: it opened one of its own, or it asked to close. Step
                    // back to the category page first, because the selection is static and outlives this window
                    // -- left pointing at a redirect it would walk straight back into it the next time Options
                    // was opened, with no way through to anything else.
                    selectedChild = -1;

                    // Silently, because the window the page just opened is the feedback. A close sound here would
                    // read as something having gone wrong at the moment their window appears.
                    Close(false);
                }
            }
            else
            {
                // Height comes from what the section actually drew last frame rather than from a constant per
                // section. The old single figure had to be raised by hand whenever a section grew, and a figure
                // short of the content does not scroll to the rest -- it clips it away, somewhere nobody looks.
                // Measuring is exact from the second frame on and needs nothing maintained; the seed only has to
                // be too big rather than right, and too big costs one frame of empty scroll space.
                Rect view = new Rect(0f, 0f, inner.width - 18f, current.MeasuredHeight);
                Widgets.BeginScrollView(inner, ref scroll, view);

                float y = 0f;
                current.Draw(view, ref y, palette, settings);

                Widgets.EndScrollView();

                current.MeasuredHeight = y;
            }

            Text.Anchor = previousAnchor;
            GUI.color = previousColor;
            Text.Font = previousFont;
        }

        /// <summary>Whether a mod's own settings page is what the pane is showing.</summary>
        private bool ShowingModSettings
        {
            get
            {
                if (categories == null || selectedCategory < 0 || selectedCategory >= categories.Count)
                    return false;

                List<Category> children = categories[selectedCategory].Children;

                return children != null && selectedChild >= 0 && selectedChild < children.Count;
            }
        }

        /// <summary>
        /// The category the pane should draw, clamped so a stale index cannot throw.
        ///
        /// The indices are static and outlive any one window, so they can point past the end of a list that has
        /// since been rebuilt -- a mod removed between sessions is enough.
        /// </summary>
        private Category Resolve()
        {
            selectedCategory = Mathf.Clamp(selectedCategory, 0, categories.Count - 1);
            Category category = categories[selectedCategory];

            if (category.Children == null || selectedChild < 0)
            {
                selectedChild = -1;
                return category;
            }

            if (selectedChild >= category.Children.Count)
            {
                selectedChild = -1;
                return category;
            }

            return category.Children[selectedChild];
        }

        /// <summary>
        /// The rect vanilla's <c>Dialog_ModSettings</c> hands a mod.
        ///
        /// Its window is 900 by 700; it spends 40 on the heading and <c>CloseButSize.y</c> on the close button,
        /// and gives the mod the rest. Every mod settings page in the game was laid out looking at this rect, so
        /// it is the one measurement worth treating as the authored size.
        /// </summary>
        private static Vector2 VanillaModPane =>
            new Vector2(900f, 700f - 40f - Window.CloseButSize.y);

        /// <summary>
        /// The rect a particular mod's page is drawn into, before it is scaled to fit.
        ///
        /// Nearly always the vanilla one, because that is the rect every settings page in the game was written
        /// against. <see cref="XmlExtensionsIntegration"/> is the exception: its menu is hosted rather than
        /// asked to draw itself, and its layout needs more width than a vanilla settings page is ever given.
        /// </summary>
        private static Vector2 PaneFor(Mod mod)
        {
            return XmlExtensionsIntegration.Hosts(mod)
                ? XmlExtensionsIntegration.AuthoredPane
                : VanillaModPane;
        }

        /// <summary>
        /// The largest pane anything could ask for, which is what the window is sized around.
        ///
        /// <b>One size for every page, chosen up front rather than per selection.</b> Sizing to the current
        /// page would make the window jump under the cursor the moment a mod was picked, which is worse than a
        /// Theme page with room to spare around it. So if a hosted menu that wants more is installed, every page
        /// gets the larger window and the smaller ones sit in it comfortably.
        /// </summary>
        private static Vector2 LargestPane
        {
            get
            {
                Vector2 largest = VanillaModPane;

                if (!XmlExtensionsIntegration.Available)
                    return largest;

                Vector2 hosted = XmlExtensionsIntegration.AuthoredPane;

                return new Vector2(Mathf.Max(largest.x, hosted.x), Mathf.Max(largest.y, hosted.y));
            }
        }

        /// <summary>
        /// Hands the pane to another mod to draw into, scaled so its layout arrives whole.
        ///
        /// <b>Scaled rather than resized, because their layout is not ours to reflow.</b> A settings page is
        /// arbitrary IMGUI code: some of it is a <c>Listing_Standard</c> that would adapt to a narrower rect,
        /// and plenty of it is hard-coded rects, fixed label widths and columns positioned by arithmetic. Handing
        /// that a smaller rect is what cuts labels off and wraps them badly, and nothing can be inspected ahead of
        /// time to tell which kind a given page is.
        ///
        /// So the page is given exactly <see cref="VanillaModPane"/> -- the rect it was authored against -- and
        /// the whole coordinate space is scaled to fit our pane through <c>GUI.matrix</c>. The layout that
        /// results is identical to the one in vanilla's dialog, because it <i>is</i> that layout; only the
        /// magnification differs. Nothing reflows, so nothing reflows badly.
        ///
        /// Uniform on both axes, never above 1. Scaling the axes separately would stretch text and turn every
        /// icon into an ellipse, and magnifying a page that already fits would be inventing a problem.
        ///
        /// <b>Input follows the matrix.</b> Unity transforms <c>Event.current.mousePosition</c> by it, so clicks,
        /// drags and <c>Mouse.IsOver</c> all land where they look like they should, and tooltips register against
        /// the rects the page actually drew. RimWorld's own UI scale is this mechanism and nothing else, which is
        /// where the confidence comes from: <c>UI.ApplyUIScale</c> is a single <c>GUI.matrix</c> assignment.
        ///
        /// <b>What does not follow it is anything converting a coordinate back out.</b> That was the open question
        /// here and it had a real answer. <c>Mouse.IsOver</c> ends in <c>WindowStack.MouseObscuredNow</c>, which
        /// converts the cursor to screen space on the assumption that RimWorld's is the only transform in play, so
        /// under ours it asks about the wrong point. Every tooltip in a hosted page was destroying itself on that.
        /// See <see cref="Patch_WindowStack_MouseObscured"/>, switched on below for exactly this call.
        ///
        /// <b>Guarded, and this is the one place in this window where that is not a formality.</b> Everything
        /// else drawn here is ours; this is arbitrary code from another author running inside our window every
        /// frame. Left unguarded, a mod whose settings page throws would take this whole window down with it and
        /// look for all the world like our bug. The matrix is restored in a finally, because leaving it set would
        /// scale everything drawn after it for the rest of the frame.
        ///
        /// <b>Some pages do not draw into the rect at all, and this reports them.</b> A settings page is free to
        /// ignore its rect and open a window of its own instead, and several do -- XML Extensions' whole page is
        /// <c>Find.WindowStack.Add(new XmlExtensionsMenuModSettings(...))</c> and nothing else. That works in
        /// vanilla because the window it opens closes <c>Dialog_ModSettings</c> from its constructor, so the call
        /// happens exactly once. It looks for <c>Dialog_ModSettings</c> by type, does not find this window, and so
        /// nothing stops us calling the page again on the very next pass.
        ///
        /// The result is a loop, and a nasty one: <c>WindowStack.Add</c> begins by removing any window of the same
        /// type, which fires that window's <c>soundClose</c> -- left at its default of <c>SoundDefOf.Click</c> --
        /// and runs its <c>PreClose</c>, which writes the settings file to disk. So every pass of every frame
        /// played a click, wrote a file and rebuilt the mod list. That is the constant clicking and the
        /// unresponsive window, and none of it reaches the log because nothing throws.
        ///
        /// So the window stack is compared across the call, and a page that opened one is reported to the caller,
        /// which gets this window out of the way -- the same handoff vanilla's dialog performs, arrived at from
        /// the other side.
        /// </summary>
        /// <returns>True if this page is finished with the window: it opened one of its own, or asked to close.</returns>
        private static bool DrawModSettings(Rect rect, Mod mod)
        {
            if (mod == null)
                return false;

            // XML Extensions is drawn by us rather than asked to draw itself, so its redirect never runs and
            // there is nothing to watch for. See XmlExtensionsIntegration for why that one mod is worth it.
            bool hosting = XmlExtensionsIntegration.Hosts(mod);

            // Only Layout and Repaint are watched: a redirect opens its window unconditionally, so it shows up
            // on the first pass of the first frame, while an ordinary child window is opened in response to
            // input.
            //
            // <b>"In response to input" is not the same as "on a mouse event pass",</b> and that mistake shipped
            // here. This condition used to be Layout-or-Repaint alone, on the reasoning that a float menu off a
            // dropdown arrives as a mouse event and so could never be seen. It can.
            // <c>Widgets.Dropdown</c> ends in <c>Widgets.ButtonInvisibleDraggable</c>, which tests
            // <c>Input.GetMouseButtonUp(0)</c> -- Unity's polling API, not <c>Event.current</c> -- and that is
            // true on *every* pass of the frame the button came up on, Layout and Repaint included. So the menu
            // was added during a watched pass, read as a redirect, and this window closed itself. OgreStack found
            // it on 2026-08-23; every dropdown in every hosted settings page did it.
            //
            // The frame a mouse button changed state on is therefore not judged at all. That is the exact inverse
            // of the fault and needs no list of window types to keep current: a redirect is unconditional, so it
            // will still be caught on the next quiet frame, and a menu opened by a click is already in the
            // snapshot by then rather than new.
            WindowStack stack = Find.WindowStack;

            bool clicking = Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0)
                                                        || Input.GetMouseButtonDown(1) || Input.GetMouseButtonUp(1)
                                                        || Input.GetMouseButtonDown(2) || Input.GetMouseButtonUp(2);

            bool watching = !hosting
                            && !clicking
                            && stack != null
                            && (Event.current == null
                                || Event.current.type == EventType.Layout
                                || Event.current.type == EventType.Repaint);

            if (watching)
            {
                WindowsBeforePage.Clear();

                for (int index = 0; index < stack.Count; index++)
                    WindowsBeforePage.Add(stack[index]);
            }

            bool finished = false;

            UIGuardedPanel.Draw("Options.ModSettings." + mod.GetType().Name, rect,
                () =>
                {
                    Vector2 authored = PaneFor(mod);
                    float scale = Mathf.Min(rect.width / authored.x, rect.height / authored.y, 1f);

                    Matrix4x4 previous = GUI.matrix;

                    try
                    {
                        // Multiplied into whatever is already there rather than assigned, so this composes with
                        // the window's own transform instead of replacing it.
                        GUI.matrix = previous * Matrix4x4.TRS(new Vector3(rect.x, rect.y, 0f),
                            Quaternion.identity, new Vector3(scale, scale, 1f));

                        // Hover tests inside this call have to read the real cursor rather than convert one back
                        // through the matrix just installed, or a tooltip's own window reads as covering the
                        // control that raised it and the tip destroys itself. See Patch_WindowStack_MouseObscured.
                        Patch_WindowStack_MouseObscured.Transformed = true;

                        Rect page = new Rect(0f, 0f, authored.x, authored.y);

                        if (hosting)
                            finished = XmlExtensionsIntegration.Draw(page);
                        else
                            mod.DoSettingsWindowContents(page);
                    }
                    finally
                    {
                        Patch_WindowStack_MouseObscured.Transformed = false;
                        GUI.matrix = previous;
                    }
                },
                "This mod's settings page could not be drawn. The fault is in that mod rather than in this one; "
                + "its own settings window from the mod list may still work.");

            bool redirected = false;

            if (watching)
            {
                for (int index = 0; index < stack.Count; index++)
                {
                    Window opened = stack[index];

                    // ImmediateWindow is the one thing vanilla adds during Repaint by design: it is how
                    // Find.WindowStack.ImmediateWindow draws an overlay, and the first call for a given ID creates
                    // one. Counting it would call a page a redirect for drawing a tooltip over itself.
                    //
                    // A FloatMenu is never a redirect either, whatever frame it lands on. A page that replaces
                    // itself opens a dialog to be read instead of this window; a float menu is a transient list
                    // of choices belonging to a control inside the page, and it closes itself the moment
                    // something is picked. Named as well as covered by the frame test above, because the two
                    // catch it for different reasons and this one holds even if a menu is ever opened from a key.
                    if (opened is ImmediateWindow || opened is FloatMenu || WindowsBeforePage.Contains(opened))
                        continue;

                    redirected = true;

                    break;
                }

                WindowsBeforePage.Clear();
            }

            return redirected || finished;
        }

        /// <summary>Saves the settings of whichever mod was last shown, if any.</summary>
        private void LeaveModSettings()
        {
            // First, and outside the null check: a hosted menu holds a live window object of another mod's, and
            // its PreClose is what writes the settings the player just changed. Dropping it without that would
            // lose them silently.
            XmlExtensionsIntegration.Leave();

            if (lastSettingsMod == null)
                return;

            Mod leaving = lastSettingsMod;
            lastSettingsMod = null;

            UIGuard.Try("Options.WriteModSettings." + leaving.GetType().Name,
                () => leaving.WriteSettings(),
                "That mod's settings may not have been saved. Its own settings window from the mod list writes "
                + "them the same way.");
        }

        /// <summary>
        /// The category column: one card per section, the chosen one lit.
        /// </summary>
        private void DrawCategoryColumn(Rect column, UIColorPaletteDef palette)
        {
            float contentHeight = VisibleRowCount() * (CardHeight + CardGap);

            // Scrolls only when it has to. The six categories fit today, but a column that silently ran off
            // the bottom would put a whole section out of reach with nothing on screen saying so.
            bool scrolls = contentHeight > column.height;
            Rect view = new Rect(0f, 0f, column.width - (scrolls ? 18f : 0f), contentHeight);

            Widgets.BeginScrollView(column, ref categoryScroll, view);

            float y = 0f;

            for (int index = 0; index < categories.Count; index++)
            {
                Category category = categories[index];
                bool expanded = index == selectedCategory && category.Children != null;

                // Copied per iteration before the lambda closes over it. A for loop's counter is one variable
                // shared by every pass, not a fresh one each time -- so all of these closures would otherwise
                // read the same slot, and read it after the loop had finished, when it holds Count. That is
                // exactly what happened: every card selected an index one past the end, Resolve clamped it to
                // the last category, and clicking anything at all landed on Mod Settings with its children
                // still showing. The inner loop below already did this; this one did not.
                int captured = index;

                DrawCategoryCard(new Rect(0f, y, view.width, CardHeight), category, palette,
                    chosen: index == selectedCategory && selectedChild < 0, indent: 0f,
                    onClick: () =>
                    {
                        // Choosing a branch reveals its children rather than picking one of them: the category
                        // page explains what the list is, and guessing which mod they wanted would be wrong more
                        // often than not. Choosing anything else collapses whatever was open, because expansion
                        // is read from this one field rather than stored per category.
                        selectedCategory = captured;
                        selectedChild = -1;
                    });

                y += CardHeight + CardGap;

                if (!expanded)
                    continue;

                for (int childIndex = 0; childIndex < category.Children.Count; childIndex++)
                {
                    Category child = category.Children[childIndex];
                    int capturedChild = childIndex;

                    // Indented, and narrower for it, so the list reads as belonging to the entry above rather
                    // than as more categories that happen to be lower down.
                    DrawCategoryCard(new Rect(ChildIndent, y, view.width - ChildIndent, CardHeight), child,
                        palette, chosen: selectedChild == childIndex, indent: ChildIndent,
                        onClick: () => selectedChild = capturedChild);

                    y += CardHeight + CardGap;
                }
            }

            Widgets.EndScrollView();
        }

        /// <summary>How many cards the column is showing, counting the children of an expanded category.</summary>
        private int VisibleRowCount()
        {
            int count = categories.Count;

            if (selectedCategory >= 0 && selectedCategory < categories.Count)
                count += categories[selectedCategory].Children?.Count ?? 0;

            return count;
        }

        /// <summary>
        /// One card in the column, at whatever rect and depth the caller decided.
        /// </summary>
        private void DrawCategoryCard(Rect rect, Category category, UIColorPaletteDef palette, bool chosen,
            float indent, System.Action onClick)
        {
            // Resolved every frame rather than held on the card, because the palette can change while this
            // window is open -- the Theme section is one click away -- and a color baked in at construction
            // would leave the categories drawn in the old theme until the window was reopened.
            Color highlight = palette.Get(category.Highlight);
            bool marked = category.Highlight != UIColorRole.Accent;

            UICardControl card = category.Card;
            card.Selected = chosen;
            card.AccentColor = chosen ? highlight : Faded(highlight, marked, palette);

            // A wash over the card's own fill, at the same weight the critical alert card uses, so a marked
            // category is tinted rather than filled and its text stays as readable as any other.
            card.BackgroundTexture = marked ? BaseContent.WhiteTex : null;
            card.BackgroundTint = new Color(highlight.r, highlight.g, highlight.b, 0.13f);

            category.Title.Color = chosen ? palette.TextPrimary : palette.TextSecondary;
            category.Blurb.Color = palette.TextSecondary;

            // Sized here rather than at construction because the column narrows by the width of a scrollbar when
            // one appears, and an indented card is narrower again. Nothing clips a card's contents to the card,
            // so a label left at its old width would write across the gap and into the settings pane.
            float textLeft = category.Icon != null ? IconSize + IconGap : 0f;
            float labelWidth = rect.width - card.Padding * 2f - card.AccentWidth - textLeft;

            category.Title.Bounds.x = textLeft;
            category.Title.Bounds.width = labelWidth;
            category.Blurb.Bounds.x = textLeft;
            category.Blurb.Bounds.width = labelWidth;

            if (category.IconElement != null)
            {
                category.IconElement.Texture = category.Icon;
                category.IconElement.Visible = category.Icon != null;

                // Centered against the two lines of text rather than pinned to the top, so a square icon sits
                // level with the block it labels whichever of the two lines is longer.
                category.IconElement.Bounds =
                    new Rect(0f, (CardLine * 2f - IconSize) * 0.5f, IconSize, IconSize);
            }

            if (!card.Draw(rect, palette) || chosen)
                return;

            onClick();

            // The pane it is about to show has its own length, and keeping the old offset would open a short
            // section already scrolled past its end.
            scroll = Vector2.zero;

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// The unlit form of a category's highlight.
        ///
        /// A palette author names <c>accentMuted</c> deliberately as the quiet companion to their accent, so the
        /// ordinary case uses it as written instead of a color this mod derived. The marked case has no authored
        /// companion -- no palette names a muted warning -- so that one is mixed toward the panel behind it, which
        /// is the same thing the authored pair does to the eye.
        /// </summary>
        private static Color Faded(Color highlight, bool marked, UIColorPaletteDef palette)
        {
            return marked ? Color.Lerp(highlight, palette.PanelBackground, 0.55f) : palette.AccentMuted;
        }

        /// <summary>
        /// Draws one category's settings into the pane, advancing <paramref name="y"/> past what it drew.
        ///
        /// A named delegate rather than an <c>Action</c> because of the <c>ref</c>: every section already writes
        /// its height back through one, and changing that would mean rewriting all six to hand a cursor object
        /// around for no gain.
        /// </summary>
        private delegate void SectionDrawer(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings);

        /// <summary>
        /// One entry in the category column, and the section it shows.
        /// </summary>
        private sealed class Category
        {
            public UICardControl Card;
            public UICardLabel Title;
            public UICardLabel Blurb;
            public SectionDrawer Draw;

            /// <summary>
            /// The palette role the card's accent stripe and wash take.
            ///
            /// <see cref="UIColorRole.Accent"/> is an ordinary category. <see cref="UIColorRole.Warning"/> marks
            /// one as holding experimental settings, which is the whole of what marking a category involves --
            /// the card already knows how to draw a stripe and a wash, so nothing else has to be told about it.
            /// Nothing is marked today; the settings here are all finished ones.
            ///
            /// A role rather than a color because the player can change the palette from inside this window, so
            /// the color has to be looked up when it is drawn rather than when the category is built.
            /// </summary>
            public UIColorRole Highlight = UIColorRole.Accent;

            /// <summary>
            /// Sub-entries revealed when this one is chosen, or null for an ordinary category.
            ///
            /// Only one level. A settings list that nests further is a settings list nobody can find anything in,
            /// and there is nothing below a mod to show anyway.
            /// </summary>
            public List<Category> Children;

            /// <summary>The mod this entry shows the settings for, on a child of the mod settings category.</summary>
            public Mod Mod;

            /// <summary>
            /// The mod's own icon, shown at the left of its card. Null on our own categories, which have none.
            ///
            /// Held on the category rather than added to the card at construction because the labels have to
            /// move over to make room for it, and that is decided where the card is laid out.
            /// </summary>
            public Texture Icon;

            /// <summary>The card element the icon is drawn through, hidden on categories without one.</summary>
            public UICardImage IconElement;

            /// <summary>
            /// Whether the pane hands this category the rect directly instead of putting it in a scroll view.
            ///
            /// True for another mod's settings, and it has to be. <c>DoSettingsWindowContents</c> is given a
            /// plain rect by vanilla and a good many mods open their own scroll view inside it; nesting one
            /// scroll view in another gives two scrollbars that fight, and the inner one usually wins by
            /// swallowing the wheel.
            /// </summary>
            public bool RawPane;


            /// <summary>
            /// How tall this category's content was the last time it drew.
            ///
            /// Seeded far above any real section so the first frame over-reaches rather than clipping. Over-
            /// reaching shows one frame of scroll space below the content and then corrects itself; falling
            /// short hides the bottom of a section until someone happens to notice it missing.
            /// </summary>
            public float MeasuredHeight = 1400f;
        }

        private void EnsureCategories()
        {
            if (categories != null)
                return;

            categories = new List<Category>
            {
                MakeCategory("Game Settings", "Saving, options, quitting", DrawGameSettingsSection),
                MakeCategory("Theme", "Colors and palettes", DrawThemeSection),
                MakeCategory("UI Preferences", "How this mod's panels behave", DrawUIPreferencesSection),
                MakeCategory("Manage Tabs", "The button bar", DrawBarSection),
                MakeCategory("Clock", "How the time reads", DrawClockSection),
                MakeCategory("Desktop Widgets", "Readouts in the corners", DrawWidgetSection),
                MakeCategory("Notifications", "Messages, letters and alerts", DrawNotificationSection),
                MakeCategory("Mod Integrations", "Extras for other mods you have", DrawIntegrationSection),
                MakeCategory("Display", "Fullscreen and resolution", DrawDisplaySection),
                MakeCategory("Additional Features", "Extras beyond the restyling",
                    DrawAdditionalFeaturesSection),
                MakeCategory("Raids and Incidents", "Turn off the ones you dislike", DrawThreatsSection),
                MakeCategory("Quality of Life", "Interruptions and busywork", DrawQualityOfLifeSection),
                MakeCategory("Diagnostics", "Logging", DrawDiagnosticsSection),
                MakeCategory("Developer Tools", "For working on mods", DrawDeveloperToolsSection),
                MakeModSettingsCategory()
            };

            // Absent without Odyssey rather than present and empty. There is no grav engine to configure, and a
            // category that can only say "you do not have this expansion" is a page advertising a purchase.
            if (Gravships.GravshipTuning.Available)
                categories.Add(MakeCategory("Gravships", "How big a ship may be", DrawGravshipSection));

            Order(categories);
        }

        /// <summary>
        /// Puts the category list in reading order: Game Settings, Mod Settings, then everything else by name.
        ///
        /// <b>Two are pinned because they are not peers of the rest.</b> Game Settings is the one that changes
        /// RimWorld rather than this mod, and Mod Settings is a doorway to other people's mods rather than a
        /// page of its own. Everything between them is one of our own feature areas, and for a list that long
        /// alphabetical beats an order that only made sense to whoever added them.
        ///
        /// Sorted here rather than by writing the list in order, so adding a category cannot put it in the wrong
        /// place -- the next person to add one does not have to know about this rule for it to hold.
        /// </summary>
        private static void Order(List<Category> list)
        {
            Category game = Named(list, "Game Settings");
            Category mods = Named(list, "Mod Settings");

            List<Category> rest = new List<Category>();

            foreach (Category category in list)
            {
                if (category != game && category != mods)
                    rest.Add(category);
            }

            rest.Sort((a, b) => string.Compare(LabelOf(a), LabelOf(b), System.StringComparison.OrdinalIgnoreCase));

            list.Clear();

            // Either could be absent if it is ever renamed, and a missing pin should reorder the rest rather
            // than throw or leave a hole.
            if (game != null)
                list.Add(game);

            if (mods != null)
                list.Add(mods);

            list.AddRange(rest);
        }

        private static Category Named(List<Category> list, string label)
        {
            foreach (Category category in list)
            {
                if (string.Equals(LabelOf(category), label, System.StringComparison.OrdinalIgnoreCase))
                    return category;
            }

            return null;
        }

        private static string LabelOf(Category category)
        {
            return category == null || category.Title == null ? string.Empty : category.Title.Text ?? string.Empty;
        }

        /// <summary>
        /// The mod settings category: every other mod that offers settings, as children of one entry.
        ///
        /// <b>The list is exactly vanilla's.</b> A mod appears here if <c>SettingsCategory()</c> returns
        /// something, which is the same test <c>Dialog_ModsConfig</c> uses to decide whether to offer the button
        /// at all. Anything else would be this mod inventing an opinion about whose settings are worth showing.
        ///
        /// Built once, because <c>LoadedModManager.ModHandles</c> is fixed for the session -- mods cannot be
        /// loaded or unloaded without a restart, so a list rebuilt every frame would be the same list.
        ///
        /// Sorted by the name shown rather than by load order, because the player is looking for a name.
        /// </summary>
        private Category MakeModSettingsCategory()
        {
            Category parent = MakeCategory("Mod Settings", "Other mods' options", DrawModPickerSection);
            parent.Children = new List<Category>();

            IEnumerable<Mod> handles = UIGuard.Try("Options.ListModsWithSettings",
                () => LoadedModManager.ModHandles, null,
                "The mod settings list is empty.");

            if (handles == null)
                return parent;

            List<Mod> withSettings = new List<Mod>();

            foreach (Mod mod in handles)
            {
                // Guarded per mod: SettingsCategory is another mod's code, and one that throws should cost its
                // own row rather than the whole list.
                string category = UIGuard.Try("Options.ReadModSettingsCategory",
                    () => mod?.SettingsCategory(), null,
                    "One mod is left out of the mod settings list.");

                if (!category.NullOrEmpty())
                    withSettings.Add(mod);
            }

            withSettings.SortBy(m => m.SettingsCategory());

            foreach (Mod mod in withSettings)
            {
                Category child = MakeCategory(mod.SettingsCategory(), mod.Content?.Name ?? "", null);
                child.Mod = mod;
                child.RawPane = true;
                child.Icon = IconFor(mod);
                parent.Children.Add(child);
            }

            return parent;
        }

        /// <summary>
        /// A mod's own icon, the one the mod list shows.
        ///
        /// <c>ModMetaData.Icon</c> never returns null -- it falls back to RimWorld's generic mod icon when the
        /// author shipped no ModIcon.png -- so a card either shows the mod's mark or the same placeholder the
        /// mod list would show it with. It does file I/O the first time it is asked, which is why this is called
        /// while the list is built rather than every frame.
        ///
        /// Guarded because it touches the filesystem and another mod's About folder, neither of which is owed
        /// to us in any particular state.
        /// </summary>
        private static Texture IconFor(Mod mod)
        {
            return UIGuard.Try("Options.ReadModIcon",
                () => (Texture) mod?.Content?.ModMetaData?.Icon, null,
                "That mod's card shows no icon.");
        }

        /// <summary>
        /// What the mod settings category itself shows: what to do, or why there is nothing to do.
        /// </summary>
        private void DrawModPickerSection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "Mod Settings", palette);

            int count = categories[selectedCategory].Children?.Count ?? 0;

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(0f, y, view.width, 56f),
                count == 0
                    ? "No other mod you have loaded offers settings. Mods that do will appear here on their own."
                    : "Choose a mod from the list to the left. Its own settings page opens here, drawn by the "
                      + "mod itself, and is saved when you move away from it or close this window.");

            y += 60f;
            GUI.color = palette.TextPrimary;
        }

        /// <summary>
        /// Builds a category and its card once.
        ///
        /// The card is kept and reassigned between frames rather than rebuilt, which is what it is designed for:
        /// the two labels are held by reference so a frame only writes the colors that changed.
        /// </summary>
        private static Category MakeCategory(string label, string blurb, SectionDrawer draw)
        {
            UICardControl card = new UICardControl
            {
                Height = CardHeight,
                Padding = CardPadding,
                AccentWidth = 3f
            };

            Category category = new Category
            {
                Card = card,
                Draw = draw,
                // Added before the labels so it draws under them if they ever overlap, and so the element order
                // matches the reading order.
                IconElement = card.Add(new UICardImage
                {
                    Fit = UIImageFit.Contain,
                    Visible = false
                }),
                Title = card.Add(new UICardLabel
                {
                    Text = label,
                    Bounds = new Rect(0f, 0f, 0f, CardLine),
                    Anchor = TextAnchor.MiddleLeft,
                    Ellipses = true
                }),
                Blurb = card.Add(new UICardLabel
                {
                    Text = blurb,
                    Bounds = new Rect(0f, CardLine, 0f, CardLine),
                    Font = GameFont.Tiny,
                    Anchor = TextAnchor.MiddleLeft,
                    Ellipses = true
                })
            };

            // The full text on hover, since ellipsizing is a promise that nothing is lost, only hidden. Both
            // lines, because a mod's name and the pack it came from are different things and either can be the
            // one that was cut.
            card.Tooltip = blurb.NullOrEmpty() ? label : label + "\n" + blurb;

            return category;
        }

        /// <summary>
        /// How this mod's own panels behave, as opposed to what color they are.
        ///
        /// <b>Separate from Theme on purpose.</b> Theme answers "what does it look like"; this answers "what is
        /// on screen at all". They get confused with each other whenever they share a page, because a player
        /// hunting for a panel they want gone starts looking under appearance and gives up there.
        ///
        /// <b>Deliberately not a dumping ground.</b> A setting belongs here when it governs a panel this mod
        /// draws and has nowhere more specific to live. Anything that belongs to one feature stays with that
        /// feature -- saving is under Game Settings, tab sizing is under Manage Tabs -- because a page that
        /// collects every leftover switch is the page nobody can find anything on.
        /// </summary>
        private void DrawUIPreferencesSection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "UI Preferences", palette);

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(0f, y, view.width, 40f),
                "What this mod puts on screen. Everything here can be switched off without losing the "
                + "information behind it.");
            y += 44f;
            GUI.color = palette.TextPrimary;

            GroupLabel(view, ref y, palette, "Architect");

            WidgetToggle(view, ref y, palette, settings, Indent, "Show architect tab info panel",
                settings.showArchitectInfoPanel, value => settings.showArchitectInfoPanel = value,
                "The strip under the build grid showing the selected building's description, what it costs "
                + "and its stats.\n\nRimWorld puts this in a floating box that sits over the menu you are "
                + "reading it from. This mod moved it inside the window, below the grid.\n\nSwitching it off "
                + "gives its height back to the build tiles, so more of them fit before the grid scrolls. The "
                + "same text then appears as a hover tip on each tile instead, so nothing becomes unreadable "
                + "-- the tip is hidden while the panel is on precisely because the two would be saying the "
                + "same thing twice.");

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight),
                "Whether the material list shows each material's stats is a toggle in the architect itself.");
            GUI.color = palette.TextPrimary;

            y += RowHeight + 12f;

            GroupLabel(view, ref y, palette, "Inspect pane");

            WidgetToggle(view, ref y, palette, settings, Indent, "Use the rebuilt inspect pane",
                settings.richInspectPane, value => settings.richInspectPane = value,
                "The panel at the bottom left, showing whatever you have selected.\n\nRimWorld gives it a name, "
                + "a job sentence and a row of tab buttons, at one fixed height. This mod fills it: a portrait, "
                + "the same condition reading the colonists tab uses, needs with each pawn's own break "
                + "thresholds marked, what is impaired, the skill grid, and the assignment chips.\n\nHealth, "
                + "Gear, Social, Needs, Bio and Log are drawn inside the pane instead of opening a window over "
                + "it. Every other tab, including modded ones and a building's bills and storage, still opens "
                + "its own window exactly as it does now.\n\nDrag the grip on the pane's top edge to resize it. "
                + "Drag it all the way down and you get RimWorld's own pane back at RimWorld's own size, so this "
                + "switch is only needed to put the tab buttons and the vanilla layout back as well.");

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight),
                "The pane's height is set by dragging its top edge, and remembered here.");
            GUI.color = palette.TextPrimary;

            y += RowHeight + 12f;

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight),
                "The minimap and its corner, size and position are with the other widgets, under Desktop "
                + "Widgets.");
            GUI.color = palette.TextPrimary;

            y += RowHeight + 12f;
        }

        /// <summary>
        /// The minimap: whether it is drawn, where, and how big.
        ///
        /// Corner and size are pickers rather than a slider and four radio buttons, because both are short
        /// closed lists and a picker states the current answer in the row rather than making it be read off a
        /// control.
        /// </summary>
        /// <summary>
        /// The minimap's own settings, under its switch in the widget list.
        ///
        /// <b>Here rather than on the UI Preferences page, and only in one place.</b> Every other "is this
        /// widget drawn" switch lives in Desktop Widgets, which is where somebody goes to turn one off; a
        /// second copy of the same control on another page would be two ways to set one value, which is the
        /// arrangement that leaves people unsure which one won.
        ///
        /// Drawn only while the widget is on, so the page does not offer a corner for a panel that is not
        /// being drawn.
        /// </summary>
        private void DrawMinimapOptions(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings, float indent)
        {
            WidgetToggle(view, ref y, palette, settings, indent, "Show enemy pings",
                settings.showMinimapEnemies, value => settings.showMinimapEnemies = value,
                "Marks hostiles on the minimap in red.\n\nIt only ever shows what your colony can already "
                + "see: anything standing in unexplored ground is not drawn, so this is not information the "
                + "base game keeps from you.\n\nIt is still a far easier read than scanning the map yourself, "
                + "and if that feels too generous you can switch it off and keep the rest of the minimap. "
                + "Colonists, animals and downed pawns are unaffected.");

            ChoiceRow(view, ref y, palette, "Corner", CornerLabel(settings.minimapCorner), () =>
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();

                foreach (MinimapCorner corner in (MinimapCorner[]) System.Enum.GetValues(typeof(MinimapCorner)))
                {
                    MinimapCorner captured = corner;

                    options.Add(new FloatMenuOption(CornerLabel(captured),
                        UIGuard.Wrap("Options.MinimapCorner", () =>
                        {
                            settings.minimapCorner = captured;
                            settings.Save();
                        })));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            });

            ChoiceRow(view, ref y, palette, "Size", SizeLabel(settings.minimapSize), () =>
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();

                foreach (MinimapSize size in (MinimapSize[]) System.Enum.GetValues(typeof(MinimapSize)))
                {
                    MinimapSize captured = size;

                    options.Add(new FloatMenuOption(SizeLabel(captured),
                        UIGuard.Wrap("Options.MinimapSize", () =>
                        {
                            settings.minimapSize = captured;
                            settings.Save();
                        })));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            });

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(indent, y, view.width - indent, RowHeight),
                settings.minimapX >= 0f && settings.minimapY >= 0f
                    ? "Moved by hand. Drag its title bar to move it again."
                    : "Drag its title bar to move it anywhere on screen.");
            GUI.color = palette.TextPrimary;

            y += RowHeight + 2f;

            // Only offered once there is something to undo. A reset that does nothing is a control that
            // teaches somebody the button is broken.
            if (settings.minimapX >= 0f && settings.minimapY >= 0f
                && SmallButton(new Rect(indent, y, 200f, RowHeight), "Reset position", palette))
            {
                settings.minimapX = -1f;
                settings.minimapY = -1f;
                settings.Save();

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += RowHeight + 12f;
        }

        /// <summary>
        /// The colonist bar's own settings, under its switch in the widget list.
        ///
        /// <b>Two rows, and they are a mode and its parameter rather than two answers to one question.</b> The
        /// switch decides between live views and portraits; the frequency tunes the live case only, and is not
        /// drawn at all when the switch is off, so it can never look like a control that does nothing.
        /// </summary>
        private void DrawColonistBarOptions(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings, float indent)
        {
            // Ahead of the live view block, because that block returns early when the live view is off and
            // anything after it would only ever be reachable with the live view on. The weapon row has nothing to
            // do with how the tile is rendered.
            ChoiceRow(view, ref y, palette, "Show carried weapon", WeaponDisplayLabel(settings.barWeaponDisplay),
                () =>
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();

                    foreach (BarWeaponDisplay mode in
                        (BarWeaponDisplay[]) System.Enum.GetValues(typeof(BarWeaponDisplay)))
                    {
                        BarWeaponDisplay captured = mode;

                        options.Add(new FloatMenuOption(WeaponDisplayLabel(captured),
                            UIGuard.Wrap("Options.BarWeaponDisplay", () =>
                            {
                                settings.barWeaponDisplay = captured;
                                settings.Save();
                            })));
                    }

                    Find.WindowStack.Add(new FloatMenu(options));
                });

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(indent, y, view.width - indent, RowHeight * 2f),
                "Anything but Never makes every tile taller, whether or not that pawn is carrying something. A "
                + "row that appeared and vanished as pawns drafted would make the whole bar jump.");
            GUI.color = palette.TextPrimary;

            y += RowHeight * 2f;

            WidgetToggle(view, ref y, palette, settings, indent, "Live pawn view",
                settings.livePawnView, value => settings.livePawnView = value,
                "Draws each tile as a live camera view of the pawn and the ground around them, instead of their "
                + "portrait.\n\nOff by default because it is not free: every live tile costs the game an extra "
                + "camera pass. Folding a group stops its tiles rendering, so folding is also how the cost is "
                + "kept down.\n\nPawns on another map keep their portrait either way. RimWorld only ever draws "
                + "the map you are looking at, so there is nothing elsewhere to render.");

            if (!settings.livePawnView)
                return;

            ChoiceRow(view, ref y, palette, "Render frequency", RefreshLabel(settings.pawnViewRefresh), () =>
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();

                foreach (PawnViewRefresh rate in (PawnViewRefresh[]) System.Enum.GetValues(typeof(PawnViewRefresh)))
                {
                    PawnViewRefresh captured = rate;

                    options.Add(new FloatMenuOption(RefreshLabel(captured),
                        UIGuard.Wrap("Options.PawnViewRefresh", () =>
                        {
                            settings.pawnViewRefresh = captured;
                            settings.Save();
                        })));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            });

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(indent, y, view.width - indent, RowHeight * 2f),
                "This is the rate at normal speed. Running the game faster stretches it, and pausing stretches "
                + "it further, so the tiles never compete with the simulation for a frame.");
            GUI.color = palette.TextPrimary;

            y += RowHeight * 2f;
        }

        /// <summary>
        /// The weapon row choices, worded as when rather than as what.
        ///
        /// "While drafted" rather than "Drafted" because the bare adjective reads as a filter on which pawns are
        /// listed, which is a thing this bar also does elsewhere.
        /// </summary>
        private static string WeaponDisplayLabel(BarWeaponDisplay display)
        {
            switch (display)
            {
                case BarWeaponDisplay.Drafted:
                    return "While drafted";

                case BarWeaponDisplay.Always:
                    return "Always";

                default:
                    return "Never";
            }
        }

        /// <summary>
        /// The frequency choices, worded as the interval with its rate.
        ///
        /// The interval alone is the number that bounds the cost and the rate alone is the number somebody can
        /// picture, so both are shown rather than choosing between them.
        /// </summary>
        private static string RefreshLabel(PawnViewRefresh rate)
        {
            switch (rate)
            {
                case PawnViewRefresh.Ms500: return "Every 500 ms  (2/sec)";
                case PawnViewRefresh.Ms125: return "Every 125 ms  (8/sec)";
                case PawnViewRefresh.Ms50: return "Every 50 ms  (20/sec)";
                case PawnViewRefresh.EveryFrame: return "Every frame";
                default: return "Every 250 ms  (4/sec)";
            }
        }

        private static string CornerLabel(MinimapCorner corner)
        {
            switch (corner)
            {
                case MinimapCorner.BottomRight: return "Bottom right";
                case MinimapCorner.TopLeft: return "Top left";
                case MinimapCorner.TopRight: return "Top right";
                default: return "Bottom left";
            }
        }

        private static string SizeLabel(MinimapSize size)
        {
            switch (size)
            {
                case MinimapSize.Small: return "Small";
                case MinimapSize.Large: return "Large";
                default: return "Medium";
            }
        }

        private void DrawThemeSection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "Theme", palette);

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(0f, y, view.width, 38f),
                "Applies to this mod's windows and to the RimWorld controls it restyles. Takes effect "
                + "immediately.");
            y += 42f;
            GUI.color = palette.TextPrimary;

            List<UIColorPaletteDef> palettes = UIColorPaletteDef.All;
            if (palettes == null || palettes.Count == 0)
            {
                Widgets.Label(new Rect(0f, y, view.width, RowHeight), "No palettes are loaded.");
                y += RowHeight;
                return;
            }

            string activeName = UIColorPaletteDef.ActiveDefName.NullOrEmpty()
                ? UIColorPaletteDef.Default?.defName
                : UIColorPaletteDef.ActiveDefName;

            foreach (UIColorPaletteDef option in palettes)
            {
                Rect row = new Rect(0f, y, view.width, RowHeight);
                bool chosen = option.defName == activeName;

                if (chosen)
                    Widgets.DrawBoxSolid(row, palette.SelectionOverlay);

                string label = option.label.NullOrEmpty()
                    ? option.defName
                    : option.label.CapitalizeFirst();

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = chosen ? palette.TextPrimary : palette.TextSecondary;
                Widgets.Label(new Rect(24f, row.y, row.width - 28f, row.height), label);
                Text.Anchor = TextAnchor.UpperLeft;

                // Radio marker drawn as a filled square rather than vanilla's textured dot, so the row
                // matches everything else in this window.
                Rect marker = new Rect(4f, row.y + 10f, 12f, 12f);
                Widgets.DrawBoxSolid(marker, chosen ? palette.Accent : palette.SurfaceSunken);

                if (!option.description.NullOrEmpty())
                    TooltipHandler.TipRegion(row, (TipSignal) option.description);

                if (Widgets.ButtonInvisible(row) && !chosen)
                {
                    UIColorPaletteDef.ActiveDefName = option.defName;
                    settings.activePalette = option.defName;
                    settings.Save();
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                y += RowHeight + 2f;
            }

            GUI.color = palette.TextPrimary;
        }

        private void DrawBarSection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "Manage Tabs", palette);

            if (SmallButton(new Rect(0f, y, 200f, RowHeight), "Open Manager", palette))
            {
                Find.WindowStack.Add(new Dialog_ButtonBarEditor());
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += RowHeight + 4f;

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(0f, y, view.width, 40f),
                "Reorder tabs, rename them, take them off the bar, group them into menus, and choose "
                + "icons.");
            y += 44f;
            GUI.color = palette.TextPrimary;

            GroupLabel(view, ref y, palette, "Tab size");

            WidgetToggle(view, ref y, palette, settings, Indent, "Let open tabs be resized",
                settings.resizableTabs, value => settings.resizableTabs = value,
                "Adds a grip to the free corner of an open tab, opposite the corner of the screen it is anchored "
                + "to. Drag it to make the tab bigger or smaller, and the size is remembered for that tab.\n\n"
                + "How well a tab uses the extra room is up to the tab: most lists and grids fill it, and a few "
                + "are laid out at a fixed size and will simply have space around them.\n\nThe inspect pane is "
                + "left alone here, because it sizes itself to fit whatever you have selected. It has a grip of "
                + "its own on its top edge, under Panels.");

            int stored = UIGuard.Try("Options.CountTabSizes", () => TabSizes.Count, 0, null);

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight),
                stored == 0
                    ? "No tab has been resized yet."
                    : stored == 1
                        ? "1 tab has a remembered size."
                        : stored + " tabs have remembered sizes.");
            GUI.color = palette.TextPrimary;

            y += RowHeight + 2f;

            if (stored > 0
                && SmallButton(new Rect(Indent, y, 200f, RowHeight), "Forget Tab Sizes", palette))
            {
                UIGuard.Try("Options.ResetTabSizes", TabSizes.ResetAll,
                    "The remembered tab sizes were not cleared.");

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += RowHeight + 4f;

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(Indent, y, view.width - Indent, 40f),
                "Forgetting a size puts that tab back to whatever size it asks for. It takes effect the next "
                + "time the tab is opened.");
            y += 44f;
            GUI.color = palette.TextPrimary;
        }

        /// <summary>
        /// The clock section.
        ///
        /// Only the date widget reads this today, and the section says so: a player who has not put that
        /// widget on the bar would otherwise change the setting and see nothing happen anywhere.
        /// </summary>
        private void DrawClockSection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "Clock", palette);

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(0f, y, view.width, 38f),
                "How the date and time widget writes the time of day. RimWorld's own readouts are not "
                + "affected.");
            y += 42f;
            GUI.color = palette.TextPrimary;

            foreach (UITimeFormat option in (UITimeFormat[]) System.Enum.GetValues(typeof(UITimeFormat)))
            {
                bool chosen = settings.timeFormat == option;
                Rect row = new Rect(0f, y, view.width, RowHeight);

                if (UIRadioButtonControl.Draw(row, chosen, palette,
                        UIClock.Label(option) + "   " + UIClock.Example(option))
                    && !chosen)
                {
                    settings.timeFormat = option;
                    settings.Save();

                    // The widget's slot is sized by the widest reading it has ever given, which never
                    // shrinks on its own. Without this, going from "14:30" back to "14h" would leave the
                    // slot holding the wider form's width until the next launch.
                    foreach (UIBarWidgetDef def in DefDatabase<UIBarWidgetDef>.AllDefsListForReading)
                        def.WorkerIfCreated?.ResetWidth();

                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                y += RowHeight + 2f;
            }

            GUI.color = palette.TextPrimary;
        }

        /// <summary>
        /// The display section.
        ///
        /// Applied the moment the box is ticked rather than on the next launch, so the player finds out whether
        /// it did what they wanted while they are still looking at the setting.
        /// </summary>
        private void DrawDisplaySection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "Display", palette);

            bool fullscreen = settings.fullscreenOnStartup;

            if (UICheckboxControl.Draw(new Rect(0f, y, view.width, RowHeight), ref fullscreen, palette,
                    "Fullscreen at native resolution on startup"))
            {
                settings.fullscreenOnStartup = fullscreen;
                settings.Save();

                if (fullscreen)
                    Features.Display.StartupFullscreen.Apply();
            }

            y += RowHeight + 4f;

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(0f, y, view.width, 56f),
                "Alt+Enter leaves fullscreen and RimWorld remembers it, so the next launch comes up windowed. "
                + "This puts it back. Leaving fullscreen during a session still works; it just no longer sticks.");
            y += 60f;
            GUI.color = palette.TextPrimary;
        }

        /// <summary>
        /// The desktop widgets section: which readouts are drawn in the bottom right corner.
        ///
        /// <b>Two groups, and the split is by whose widget it is rather than by what it does.</b> The lower group
        /// is RimWorld's own corner -- the readouts the base game puts there, each with a switch this mod adds.
        /// The upper group is for widgets this mod contributes itself, and it is deliberately empty until there
        /// are some.
        ///
        /// <b>What does not belong in the upper group:</b> anything that only restyles a vanilla readout. The
        /// speed control glyphs were listed there once and were wrong, because drawn icons on RimWorld's buttons
        /// do not make the speed controls ours -- they are still vanilla's widget, wearing our artwork. That
        /// toggle now sits under the readout it restyles, where its scope is obvious.
        ///
        /// <b>Everything defaults to on.</b> A readout nobody can see is a readout nobody learns to want.
        /// </summary>
        private void DrawWidgetSection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "Desktop Widgets", palette);

            const float indent = 18f;

            GroupLabel(view, ref y, palette, "This mod's widgets");

            WidgetToggle(view, ref y, palette, settings, indent, "Calendar",
                settings.showCalendarWidget, value => settings.showCalendarWidget = value,
                "A bar of the whole year showing the growing season for this tile, where today falls in it, and "
                + "the current quadrum.\n\nThe icon on it opens a fifteen day calendar: what happened either "
                + "side of today, and what is already scheduled.");

            WidgetToggle(view, ref y, palette, settings, indent * 2f, "Show explicit story events",
                settings.showExplicitStoryEvents, value => settings.showExplicitStoryEvents = value,
                "The storyteller settles when an incident happens well before it settles which incident it is, "
                + "so the calendar can say a major threat is coming without being able to say what it will "
                + "be.\n\nSwitching this on adds whatever more is actually known: the exact incident where the "
                + "storyteller only ever fires one, and the category otherwise. Some players will consider that "
                + "a spoiler, which is why it is off.");

            WidgetToggle(view, ref y, palette, settings, indent * 2f, "Hide birthdays",
                settings.hideCalendarBirthdays, value => settings.hideCalendarBirthdays = value,
                "Leaves colonist birthdays out of the calendar.\n\nBirthdays are shown by default and are worth "
                + "keeping in a colony of any ordinary size. Past a few dozen colonists every day carries one, "
                + "and the fifteen day view fills with them until a quest deadline is hard to find.\n\nEverything "
                + "else on the calendar is unaffected.");

            WidgetToggle(view, ref y, palette, settings, indent, "Quick orders",
                settings.showQuickOrders, value => settings.showQuickOrders = value,
                "A strip of six common orders in the bottom left corner: claim, deconstruct, mine, mine vein, "
                + "allow and forbid.\n\nEach of them is otherwise four clicks deep in the Architect menu, and "
                + "each is something you give dozens of times an hour.\n\nOnly shown when nothing is selected, "
                + "since that corner belongs to the inspect pane the moment you select anything. It sits to the "
                + "right of the terrain readout rather than over it.");

            WidgetToggle(view, ref y, palette, settings, indent, "Minimap",
                settings.showMinimapWidget, value =>
                {
                    settings.showMinimapWidget = value;

                    // Switched off means gone, not hidden. The baked pictures are a texture per loaded map, and
                    // somebody turning this off to use a different minimap mod should not still be paying for
                    // ours in video memory.
                    if (!value)
                        MinimapWidget.Clear();
                },
                "A picture of the whole map in a corner of the screen, with your colonists, animals and any "
                + "hostiles the colony can see.\n\nClick or drag on it to move the view, and drag its title "
                + "bar to move the panel itself. The rectangle shows where you are looking now.\n\nUnexplored "
                + "ground is drawn as nothing and anybody standing in it is not shown, so it never tells you "
                + "something your colony has not seen.\n\nSwitch it off if you would rather use another mod's "
                + "minimap; nothing else in this mod depends on it.");

            if (settings.showMinimapWidget)
                DrawMinimapOptions(view, ref y, palette, settings, indent * 2f);

            WidgetToggle(view, ref y, palette, settings, indent, "Now playing",
                settings.showMusicWidget, value => settings.showMusicWidget = value,
                "A strip in the corner saying what is playing, with skip, pause and a way to change "
                + "playlist.\n\nIt also names the gap: RimWorld leaves eighty five to a hundred and five seconds "
                + "of silence between songs in peacetime and nothing on screen says that is deliberate, so the "
                + "strip counts it down and offers to end it.\n\nSwitching this off hides the strip only. The "
                + "music window and playback are unaffected, and the window is still reachable from the speaker "
                + "in the play settings row.",
                !settings.musicPlayer || Music.MusicRivals.Any);

            WidgetToggle(view, ref y, palette, settings, indent, "Colonist bar",
                settings.showGroupedColonistBar, value => settings.showGroupedColonistBar = value,
                "Replaces RimWorld's colonist bar with named groups you can fold away.\n\nFold a group to stop "
                + "looking at people who are fine; a folded group still raises a badge when somebody inside is "
                + "down, bleeding, breaking or starving, so folding hides pawns without hiding emergencies."
                + "\n\nThe gear on a group applies an area, schedule, policy or medical setting to everybody in "
                + "it at once, and right-clicking a pawn moves them between groups.\n\nSwitch it off to go back "
                + "to RimWorld's own bar. Your groups are kept in the save either way.");

            if (settings.showGroupedColonistBar)
                DrawColonistBarOptions(view, ref y, palette, settings, indent * 2f);

            GroupLabel(view, ref y, palette, "RimWorld's corner");

            WidgetToggle(view, ref y, palette, settings, indent, "Play setting toggles",
                settings.showGlobalControlsWidget, value => settings.showGlobalControlsWidget = value,
                "The row of small buttons: zones, roof overlay, colonist bar and the rest.\n\nThe same toggles "
                + "are in the Controls tab, so this can be switched off to reclaim the corner. The beauty, room "
                + "stats and map search keys keep working either way.");

            WidgetToggle(view, ref y, palette, settings, indent, "Speed controls",
                settings.showSpeedControlsWidget, value => settings.showSpeedControlsWidget = value,
                "The pause and speed buttons in the corner. Hiding them leaves space and the speed number keys "
                + "working.");

            // There was a switch here for the drawn speed glyphs. It is gone rather than moved: the two options
            // were this mod's icons and the ones they exist to replace, which is not a choice anybody needs to
            // be offered. The glyphs are simply part of the theme now.

            WidgetToggle(view, ref y, palette, settings, indent, "Date, season and hour",
                settings.showDateWidget, value => settings.showDateWidget = value,
                "RimWorld draws these three lines as one readout and gives no way to separate them, so they "
                + "share a switch.");

            WidgetToggle(view, ref y, palette, settings, indent, "Real-time clock",
                settings.showTimeWidget, value => settings.showTimeWidget = value,
                "The wall clock time. RimWorld keeps this behind a preference in its own options; this switch "
                + "overrides it in both directions, and starts out matching whatever that preference already "
                + "said.");

            WidgetToggle(view, ref y, palette, settings, indent, "Performance meter (FPS and TPS)",
                settings.showPerformanceWidget, value => settings.showPerformanceWidget = value,
                "RimWorld keeps these counters behind its developer view settings. This shows them without "
                + "turning developer mode on.\n\nSwitching it off gives the developer settings the last word "
                + "again rather than hiding a counter you turned on there.");

            // These three were greyed out for a long time, and the tooltip said why: RimWorld drew them inside the
            // method that lays the whole corner out rather than through a call of their own, so there was no seam
            // to hide them at. GlobalControlsPanel replaced that method, which is what made them switches.
            WidgetToggle(view, ref y, palette, settings, indent, "Temperature",
                settings.showTemperatureWidget, value => settings.showTemperatureWidget = value,
                "The reading for whatever the cursor is over: the room it is in, or outdoors.");

            WidgetToggle(view, ref y, palette, settings, indent, "Weather",
                settings.showWeatherWidget, value => settings.showWeatherWidget = value,
                "The current weather, shown beside the temperature. Pocket maps have no weather and never show "
                + "this one.");

            WidgetToggle(view, ref y, palette, settings, indent, "Game conditions",
                settings.showConditionsWidget, value => settings.showConditionsWidget = value,
                "Toxic fallout, eclipses, solar flares and the rest, with how long each has left to run.");

            y += 12f;
        }

        /// <summary>
        /// The notification section: which corner each surface lives in, and whether this mod draws it at all.
        ///
        /// <b>The escape is offered first for each surface, not last.</b> A player who came here because one of
        /// these is misbehaving should find the switch that turns it off before they find the settings that only
        /// matter while it is on -- and clearing it greys the rest, so the two read as a group rather than as
        /// settings that stopped working.
        ///
        /// <b>Three corners rather than four.</b> The bottom left is where the inspect pane and the architect menu
        /// live, so a dock there would look fine on an empty map and bury itself the moment anything is selected.
        /// See <see cref="NotificationDock"/>.
        /// </summary>
        private void DrawNotificationSection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "Notifications", palette);

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(0f, y, view.width, 56f),
                "The three ways RimWorld tells you something happened. Each can be moved to a different corner, "
                + "or handed back to the base game entirely. Two surfaces in the same corner stack rather than "
                + "overlap.");
            y += 60f;
            GUI.color = palette.TextPrimary;

            GroupLabel(view, ref y, palette, "Messages");

            WidgetToggle(view, ref y, palette, settings, Indent, "Draw messages as cards",
                settings.restyleMessages, value => settings.restyleMessages = value,
                "The short notices that appear for a few seconds and fade. Clearing this gives back RimWorld's "
                + "own floating text.");

            DockChooser(view, ref y, palette, settings, settings.messageDock,
                value => settings.messageDock = value, !settings.restyleMessages);

            GroupLabel(view, ref y, palette, "Letters");

            WidgetToggle(view, ref y, palette, settings, Indent, "Draw letters as labeled rows",
                settings.restyleLetters, value => settings.restyleLetters = value,
                "The stack of events waiting to be read. RimWorld draws these as icons and shows the label only "
                + "when you point at one; rows show it outright.\n\nClearing this gives back the icons, and with "
                + "them the width setting below stops applying.");

            DockChooser(view, ref y, palette, settings, settings.letterDock,
                value => settings.letterDock = value, !settings.restyleLetters);

            LetterWidthSlider(view, ref y, palette, settings);

            GroupLabel(view, ref y, palette, "Mental breaks");

            WidgetToggle(view, ref y, palette, settings, Indent, "Announce every break, and say how long",
                settings.mentalBreakLetters, value => settings.mentalBreakLetters = value,
                "RimWorld only writes a letter for a mental break whose state has begin-letter text of its own, "
                + "so a colonist can wander off in a daze with nothing on the stack to say so. And none of the "
                + "letters it does send says how long the break runs.\n\nThis sends one for the breaks that "
                + "stayed silent, and adds the expected duration to all of them.\n\nThe duration is a range "
                + "because the game decides it as one: a break ends at its maximum age, or earlier on a random "
                + "roll once it is past its minimum. Where there is no maximum the letter says so and gives the "
                + "average instead of inventing a number.\n\nOff restores RimWorld exactly.");

            y += 8f;

            GroupLabel(view, ref y, palette, "Alerts");

            WidgetToggle(view, ref y, palette, settings, Indent, "Draw alerts as cards",
                settings.restyleAlerts, value => settings.restyleAlerts = value,
                "The standing warnings about the colony. Cards are sized to their label rather than clipped at a "
                + "fixed width, and add snoozing and hiding.\n\nClearing this gives back RimWorld's own readout "
                + "and with it anything you have snoozed or hidden, since those are this mod's additions.");

            DockChooser(view, ref y, palette, settings, settings.alertDock,
                value => settings.alertDock = value, !settings.restyleAlerts);

            y += 12f;
        }

        /// <summary>How far the notification controls sit in from the group heading above them.</summary>
        private const float Indent = 18f;

        /// <summary>Width of the label column in the game settings rows, so the controls line up.</summary>
        private const float LabelColumn = 190f;

        /// <summary>Whether a colony is loaded, which several of the game controls depend on.</summary>
        private static bool InGame =>
            UIGuard.Try("Options.ReadProgramState", () => Current.ProgramState == ProgramState.Playing, false,
                null);

        /// <summary>
        /// RimWorld's own pause menu and options, reimplemented in this mod's controls.
        ///
        /// <b>Reimplemented rather than hosted, and the trade is worth stating.</b> Hosting vanilla's own drawing
        /// would be complete by construction and could never drift; this cannot make either promise. What it buys
        /// is that the settings a player reaches most often look like the rest of this mod instead of like a
        /// window embedded in it. That was the call, and the cost is that a RimWorld update adding an option adds
        /// it here too, by hand.
        ///
        /// <b>Every control calls the same API vanilla's own does.</b> The values are <c>Prefs</c>, the resolution
        /// and scale changes go through <c>ResolutionUtility</c>'s safe setters, and quitting goes through the
        /// same confirmation. Nothing here reimplements behavior -- only layout. That is the line: getting the
        /// arrangement wrong is cosmetic, getting <c>SafeSetUIScale</c> wrong makes the game unusable.
        ///
        /// <b>What is deliberately not here:</b> keybindings and the developer options. Both are large, both are
        /// tables rather than rows, and both are reached from RimWorld's own Options window, which this mod has
        /// not taken away.
        /// </summary>
        private void DrawGameSettingsSection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "Game Settings", palette);

            bool playing = InGame;

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(0f, y, view.width, 40f),
                "RimWorld's own pause menu and options, which take effect exactly as they do in RimWorld's own "
                + "windows, alongside how this mod writes your saves.");
            y += 44f;
            GUI.color = palette.TextPrimary;

            // Quit sits directly under This game, because the two are the same subject: what you are doing with
            // the colony currently loaded. Saving, then RimWorld's own preferences, follow. It was last, which
            // put the whole of General, Graphics and Audio between a colony's actions and the way out of it.
            DrawGameActions(view, ref y, palette, playing);
            DrawQuitGroup(view, ref y, palette, playing);
            DrawSavingGroup(view, ref y, palette, settings);
            DrawGeneralGroup(view, ref y, palette, playing);
            DrawGraphicsGroup(view, ref y, palette, settings);
            DrawAudioGroup(view, ref y, palette);

            y += 12f;
        }

        /// <summary>
        /// How saves are written.
        ///
        /// <b>The only setting in this section that belongs to this mod rather than to RimWorld,</b> and it is
        /// here because this is where saving is: the section's own card already reads "Saving, options,
        /// quitting", and somebody looking for what happens to their saves will look here before they look
        /// under a mod heading.
        ///
        /// <b>Manual saves are not switched here on purpose.</b> That choice is a tick box in the save window
        /// itself, where it sits in front of the person about to write one, and this page would be a second
        /// control governing the same thing.
        /// </summary>
        private void DrawSavingGroup(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            GroupLabel(view, ref y, palette, "Saving");

            WidgetToggle(view, ref y, palette, settings, Indent, "Compress autosaves",
                settings.compressAutosaves, value => settings.compressAutosaves = value,
                "Rewrites each autosave with LZMA, which on a large colony is typically around fifteen times "
                + "smaller. Autosaves are usually most of a Saves folder, so this is where the space is.\n\n"
                + "It costs time at every autosave, and unlike a save you asked for, an autosave fires while "
                + "you are playing. On a big colony that is a pause of a few seconds rather than a brief "
                + "hitch.\n\nCompressed saves can only be opened while this mod is installed. Whether saves "
                + "you make by hand are compressed is chosen in the save window.");

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight),
                "Saves already written are left as they are, whichever format they are in.");
            GUI.color = palette.TextPrimary;

            y += RowHeight + 8f;
        }

        /// <summary>
        /// The pause menu's actions.
        ///
        /// <b>Save and Review scenario are unavailable outside a colony, and so is quitting to the menu.</b>
        /// Vanilla does not offer them at all in that state rather than offering them dead, which is the better
        /// behavior in a menu that is rebuilt every time it opens. Here the rows are fixed, so they are shown
        /// disabled with a reason -- a row that appeared and vanished between the menu and a colony would be
        /// harder to find than one that is always in the same place.
        ///
        /// Vanilla's own conditions are reproduced rather than simplified to "is a game loaded": saving is also
        /// off during a temporary block and in permadeath, where the only save is the automatic one.
        /// </summary>
        private void DrawGameActions(Rect view, ref float y, UIColorPaletteDef palette, bool playing)
        {
            GroupLabel(view, ref y, palette, "This game");

            bool permadeath = playing && UIGuard.Try("Options.ReadPermadeath",
                () => Current.Game.Info.permadeathMode, false, null);

            // Only asked while a colony is loaded, and that is not belt-and-braces. Despite reading like a
            // static flag, SavingIsTemporarilyDisabled goes through Find.TilePicker, which does not exist at the
            // main menu -- so reading it there throws every frame this section is drawn.
            bool savingBlocked = playing && UIGuard.Try("Options.ReadSavingBlocked",
                () => GameDataSaveLoader.SavingIsTemporarilyDisabled, false, null);

            // <b>Asked of the save feature rather than worked out again here.</b> These buttons, the mode
            // toggle inside the save windows and the windows themselves all have to agree about when saving
            // is possible, and three copies of the rule would eventually disagree. SavesModeBar owns it and
            // supplies the sentence explaining a refusal as well as the answer.
            string saveWhy;
            string loadWhy;

            bool canSave = SavesModeBar.CanSave(out saveWhy) && !savingBlocked;
            bool canLoad = SavesModeBar.CanLoad(out loadWhy);

            float x = Indent;
            float row = y;

            ActionButton(ref x, row, 130f, "Save", palette, canSave,
                saveWhy ?? "Saving is temporarily unavailable.",
                () => Find.WindowStack.Add(new Dialog_SaveGame()));

            ActionButton(ref x, row, 130f, "Load", palette, canLoad,
                loadWhy ?? "Another save cannot be loaded right now.",
                () => Find.WindowStack.Add(new Dialog_LoadGame()));

            ActionButton(ref x, row, 150f, "Review scenario", palette, playing,
                "Only available while a colony is loaded.",
                () => Find.WindowStack.Add(new Dialog_MessageBox(Find.Scenario.GetFullInformationText(),
                    null, null, null, null, Find.Scenario.name) { layer = WindowLayer.Super }));

            ActionButton(ref x, row, 130f, "Mods", palette, !playing,
                "The mod list can only be changed from the main menu.",
                () => Find.WindowStack.Add(new Page_ModsConfig()));

            y += RowHeight + 8f;
        }

        private void DrawGeneralGroup(Rect view, ref float y, UIColorPaletteDef palette, bool playing)
        {
            GroupLabel(view, ref y, palette, "General");

            // Vanilla refuses a language change mid-colony and says so rather than doing it, because the switch
            // reloads the def database. Reproduced exactly, message and all.
            ChoiceRow(view, ref y, palette, "Language",
                UIGuard.Try("Options.ReadLanguage", () => LanguageDatabase.activeLanguage.DisplayName, "?", null),
                () =>
                {
                    if (playing)
                    {
                        Messages.Message("ChangeLanguageFromMainMenu".Translate(), MessageTypeDefOf.RejectInput,
                            false);

                        return;
                    }

                    List<FloatMenuOption> options = new List<FloatMenuOption>();

                    foreach (LoadedLanguage language in LanguageDatabase.AllLoadedLanguages)
                    {
                        LoadedLanguage captured = language;

                        options.Add(new FloatMenuOption(captured.DisplayName,
                            UIGuard.Wrap("Options.SelectLanguage",
                                () => LanguageDatabase.SelectLanguage(captured))));
                    }

                    Find.WindowStack.Add(new FloatMenu(options));
                });

            // <b>Vanilla's own ten steps, in days, and the same split between them.</b> This was a list of eight
            // that stopped at two days and offered the sub-half-day steps to everybody. Both halves of that were
            // wrong: RimWorld goes on to three, seven and fourteen, so somebody who wanted a weekly autosave could
            // not ask for one in the window that replaced the one where they could -- and it keeps everything
            // under half a day behind dev mode, because those are debug intervals and an autosave every 72 in-game
            // minutes hitches a large colony over and over for nothing.
            //
            // A fixed list rather than a slider, still: a free number would let somebody ask for an autosave every
            // four seconds.
            float[] intervals = Prefs.DevMode
                ? new[] { 0.05f, 0.075f, 0.1f, 0.125f, 0.25f, 0.5f, 1f, 3f, 7f, 14f }
                : new[] { 0.5f, 1f, 3f, 7f, 14f };

            ChoiceRow(view, ref y, palette, "Autosave interval", AutosaveLabel(Prefs.AutosaveIntervalDays),
                () =>
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();

                    foreach (float days in intervals)
                    {
                        float captured = days;

                        options.Add(new FloatMenuOption(
                            AutosaveLabel(captured) + (captured < 0.5f ? " (debug)" : string.Empty),
                            UIGuard.Wrap("Options.SetAutosave",
                                () => Prefs.AutosaveIntervalDays = captured)));
                    }

                    Find.WindowStack.Add(new FloatMenu(options));
                });

            // <b>How many autosaves are kept, which vanilla offers and this window did not.</b> The two settings
            // are one decision -- half a day with five kept is two and a half days of history, and neither number
            // means anything without the other -- so leaving this out left the row above impossible to reason
            // about. Vanilla's own range, 1 to 25.
            Prefs.AutosavesCount = CountRow(view, ref y, palette, "Autosaves kept", Prefs.AutosavesCount, 1, 25);

            // <b>Permadeath with a long interval is the one combination worth saying something about.</b> Vanilla
            // prints this in red under the same row and it is not a formality: in permadeath the autosave is the
            // save, so a fortnight between them is a fortnight of play riding on nothing going wrong. Said rather
            // than enforced, matching vanilla -- somebody who chose permadeath is allowed to choose this too.
            if (playing && Current.Game != null && Current.Game.Info != null && Current.Game.Info.permadeathMode
                && Prefs.AutosaveIntervalDays > 1f)
            {
                Color previousWarning = GUI.color;
                GameFont previousWarningFont = Text.Font;

                Text.Font = GameFont.Tiny;
                GUI.color = palette.Danger;

                string warning = "MaxPermadeathAutosaveIntervalInfo".Translate(1f);
                float warningHeight = Text.CalcHeight(warning, view.width - Indent);

                Widgets.Label(new Rect(Indent, y, view.width - Indent, warningHeight), warning);

                Text.Font = previousWarningFont;
                GUI.color = previousWarning;

                y += warningHeight + 4f;
            }

            bool background = Prefs.RunInBackground;

            if (UICheckboxControl.Draw(new Rect(Indent, y, view.width - Indent, RowHeight), ref background,
                    palette, "Run in background",
                    "Keep simulating while the window is not focused."))
                UIGuard.Try("Options.SetRunInBackground", () => Prefs.RunInBackground = background, null);

            y += RowHeight + 8f;
        }

        private static string AutosaveLabel(float days)
        {
            if (days >= 1f)
                return days + (days == 1f ? " day" : " days");

            return Mathf.RoundToInt(days * 24f * 60f) + " minutes";
        }

        private void DrawGraphicsGroup(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            GroupLabel(view, ref y, palette, "Graphics");

            ChoiceRow(view, ref y, palette, "Resolution",
                Screen.width + " x " + Screen.height,
                () =>
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();

                    foreach (Resolution resolution in Screen.resolutions)
                    {
                        Resolution captured = resolution;

                        options.Add(new FloatMenuOption(captured.width + " x " + captured.height, () =>
                        {
                            // Vanilla's check, and it is not optional: a resolution too small for the current UI
                            // scale produces a game whose interface does not fit on its own screen.
                            if (!ResolutionUtility.UIScaleSafeWithResolution(Prefs.UIScale, captured.width,
                                    captured.height))
                            {
                                Messages.Message("MessageScreenResTooSmallForUIScale".Translate(),
                                    MessageTypeDefOf.RejectInput, false);

                                return;
                            }

                            ResolutionUtility.SafeSetResolution(captured);
                        }));
                    }

                    Find.WindowStack.Add(new FloatMenu(options));
                });

            ChoiceRow(view, ref y, palette, "UI scale", Prefs.UIScale + "x", () =>
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();

                // Vanilla's own list, read from its public field rather than copied, so a future release adding
                // a scale adds it here too.
                foreach (float scale in Dialog_Options.UIScales)
                {
                    float captured = scale;

                    options.Add(new FloatMenuOption(captured + "x", () =>
                    {
                        if (captured != 1f && !ResolutionUtility.UIScaleSafeWithResolution(captured,
                                Screen.width, Screen.height))
                        {
                            Messages.Message("MessageScreenResTooSmallForUIScale".Translate(),
                                MessageTypeDefOf.RejectInput, false);

                            return;
                        }

                        ResolutionUtility.SafeSetUIScale(captured);
                    }));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            });

            if (!ResolutionUtility.BorderlessFullscreen)
            {
                bool fullscreen = Screen.fullScreen;

                if (UICheckboxControl.Draw(new Rect(Indent, y, view.width - Indent, RowHeight), ref fullscreen,
                        palette, "Fullscreen"))
                    UIGuard.Try("Options.SetFullscreen",
                        () => ResolutionUtility.SafeSetFullscreen(fullscreen), null);

                y += RowHeight + 2f;
            }

            WidgetToggle(view, ref y, palette, settings, Indent, "Vertical sync",
                settings.vsync, value =>
                {
                    settings.vsync = value;

                    // Applied now rather than at the next launch, because a graphics switch you cannot see take
                    // effect is one you cannot judge.
                    GraphicsPreferences.Apply();
                },
                "Waits for the monitor before showing each frame, which is what stops the tearing you see when "
                + "the picture updates halfway down the screen.\n\nRimWorld has no setting of its own for this: "
                + "the engine picks it at startup and forgets any change, so this mod remembers it for "
                + "you.\n\nSwitching it off does not make the game faster. Nothing else here limits the frame "
                + "rate, so your card will draw as many frames as it can and run hot doing it. It is on by "
                + "default because that is what the game already does.");

            // Here rather than with the colonist bar's options, where it started and where it no longer belongs.
            // It began as a portrait setting for the bar; it now takes hats off the map, the portraits and the
            // bar's live tiles alike, and a world-rendering switch filed under one widget's settings is a switch
            // nobody will find. Moved on Aaron's suggestion, 2026-08-23.
            WidgetToggle(view, ref y, palette, settings, Indent, "Hide headgear",
                settings.barHideHeadgear, value =>
                {
                    settings.barHideHeadgear = value;

                    // Portraits are cached per pawn and per render setting, so the ones already built were made
                    // with the old answer. The map needs no equivalent: its skip flags are worked out on every
                    // draw, so it changes on the next frame.
                    UIGuard.Try("Options.ClearPortraits", PortraitsCache.Clear, null);
                },
                "Takes your colonists' hats and helmets off, for a colony where every face is behind a visor and "
                + "you have stopped being able to tell who is who.\n\nEverywhere they are drawn: on the map, in "
                + "portraits, and in the colonist bar's live tiles, which are a camera pointed at the map and so "
                + "could not have been done any other way. Hair, beards and eyes come back with the hat "
                + "off.\n\nThe apparel is still worn and still working. This changes the picture and nothing "
                + "else.\n\nYour colonists only. Mechanoids, shamblers, ghouls and anybody else's pawns keep "
                + "what they are wearing -- a raider in a helmet is a raider in a helmet, and a mechanoid's head "
                + "is not a hat.\n\nTwo further exceptions: a colonist in orbit keeps their helmet, because up "
                + "there it is the difference between somebody who can go outside and somebody who cannot, and "
                + "the character editor's own preview obeys its own switch, since looking under the hat is what "
                + "that window is for.");

            ChoiceRow(view, ref y, palette, "Temperature",
                UIGuard.Try("Options.ReadTemperatureMode", () => Prefs.TemperatureMode.ToStringHuman(), "?",
                    null),
                () =>
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();

                    foreach (TemperatureDisplayMode mode in
                             (TemperatureDisplayMode[]) System.Enum.GetValues(typeof(TemperatureDisplayMode)))
                    {
                        TemperatureDisplayMode captured = mode;

                        options.Add(new FloatMenuOption(captured.ToStringHuman(),
                            UIGuard.Wrap("Options.SetTemperatureMode",
                                () => Prefs.TemperatureMode = captured)));
                    }

                    Find.WindowStack.Add(new FloatMenu(options));
                });

            y += 6f;
        }

        private void DrawAudioGroup(Rect view, ref float y, UIColorPaletteDef palette)
        {
            GroupLabel(view, ref y, palette, "Audio");

            Prefs.VolumeMaster = VolumeRow(view, ref y, palette, "Master volume", Prefs.VolumeMaster);
            Prefs.VolumeGame = VolumeRow(view, ref y, palette, "Game", Prefs.VolumeGame);
            Prefs.VolumeMusic = VolumeRow(view, ref y, palette, "Music", Prefs.VolumeMusic);
            Prefs.VolumeAmbient = VolumeRow(view, ref y, palette, "Ambient", Prefs.VolumeAmbient);
            Prefs.VolumeUI = VolumeRow(view, ref y, palette, "Interface", Prefs.VolumeUI);

            y += 6f;
        }

        private void DrawQuitGroup(Rect view, ref float y, UIColorPaletteDef palette, bool playing)
        {
            GroupLabel(view, ref y, palette, "Quit");

            bool permadeath = playing && UIGuard.Try("Options.ReadPermadeathQuit",
                () => Current.Game.Info.permadeathMode, false, null);

            float x = Indent;
            float row = y;

            ActionButton(ref x, row, 210f, permadeath ? "Save and quit to main menu" : "Quit to main menu",
                palette, playing, "Only available while a colony is loaded.",
                () => Quit(permadeath, GenScene.GoToMainMenu));

            ActionButton(ref x, row, 190f, permadeath ? "Save and quit to OS" : "Quit to OS", palette, true,
                null, () => Quit(permadeath, Root.Shutdown));

            y += RowHeight + 8f;
        }

        /// <summary>
        /// Leaving the game, by whichever of vanilla's three routes applies.
        ///
        /// Permadeath saves first and does not ask, because there is nothing to decide -- the colony is being
        /// kept either way. Otherwise the confirmation is shown only when there is something to lose, which is
        /// what <c>CurrentGameStateIsValuable</c> answers.
        /// </summary>
        private static void Quit(bool permadeath, System.Action leave)
        {
            UIGuard.Try("Options.Quit", () =>
            {
                if (permadeath)
                {
                    LongEventHandler.QueueLongEvent(() =>
                        {
                            GameDataSaveLoader.SaveGame(Current.Game.Info.permadeathModeUniqueName);
                            LongEventHandler.ExecuteWhenFinished(leave);
                        },
                        "SavingLongEvent", false, null, false);

                    return;
                }

                // Nothing to lose outside a colony, and nothing to ask about either. The check itself has to be
                // skipped rather than merely ignored: CurrentGameStateIsValuable reads
                // Find.TickManager.TicksGame, which is not there at the main menu -- so quitting to the OS from
                // the menu would have thrown instead of quitting.
                if (!InGame)
                {
                    leave();

                    return;
                }

                if (GameDataSaveLoader.CurrentGameStateIsValuable)
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmQuit".Translate(),
                        () => leave(), true, null, WindowLayer.Super));

                    return;
                }

                leave();
            }, "The game did not quit. Use RimWorld's own menu.");
        }

        /// <summary>A labeled volume slider, written back by the caller.</summary>
        private float VolumeRow(Rect view, ref float y, UIColorPaletteDef palette, string label, float value)
        {
            Rect row = new Rect(Indent, y, view.width - Indent, RowHeight);

            Color previous = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(row.x, row.y, LabelColumn, row.height), label);

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = palette.TextPrimary;
            Widgets.Label(new Rect(row.xMax - 60f, row.y, 56f, row.height), Mathf.RoundToInt(value * 100f) + "%");

            Text.Anchor = previousAnchor;
            GUI.color = previous;

            float result = Widgets.HorizontalSlider(
                new Rect(row.x + LabelColumn, row.y + (row.height - 22f) * 0.5f,
                    Mathf.Max(60f, row.width - LabelColumn - 70f), 22f),
                value, 0f, 1f, false, null, null, null, 0.01f);

            y += RowHeight + 2f;

            return result;
        }

        /// <summary>
        /// A labeled row carrying a whole number on a slider, built to match <see cref="VolumeRow"/>.
        ///
        /// Same label column, same slider lane and same right-aligned readout, because a settings page only reads
        /// as one page if two rows doing the same kind of thing are laid out the same way. What differs is that
        /// the value is an integer and the readout is not a percentage.
        /// </summary>
        private int CountRow(Rect view, ref float y, UIColorPaletteDef palette, string label, int value,
            int minimum, int maximum)
        {
            Rect row = new Rect(Indent, y, view.width - Indent, RowHeight);

            Color previous = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(row.x, row.y, LabelColumn, row.height), label);

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = palette.TextPrimary;
            Widgets.Label(new Rect(row.xMax - 60f, row.y, 56f, row.height), value.ToString());

            Text.Anchor = previousAnchor;
            GUI.color = previous;

            float result = Widgets.HorizontalSlider(
                new Rect(row.x + LabelColumn, row.y + (row.height - 22f) * 0.5f,
                    Mathf.Max(60f, row.width - LabelColumn - 70f), 22f),
                value, minimum, maximum, false, null, null, null, 1f);

            y += RowHeight + 2f;

            return Mathf.RoundToInt(result);
        }

        /// <summary>A labeled row whose value is a button opening a menu of choices.</summary>
        private void ChoiceRow(Rect view, ref float y, UIColorPaletteDef palette, string label, string value,
            System.Action onClick)
        {
            Rect row = new Rect(Indent, y, view.width - Indent, RowHeight);

            Color previous = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(row.x, row.y, LabelColumn, row.height), label);

            Text.Anchor = previousAnchor;
            GUI.color = previous;

            Rect button = new Rect(row.x + LabelColumn, row.y + 2f,
                Mathf.Min(280f, Mathf.Max(80f, row.width - LabelColumn - 10f)), row.height - 4f);

            if (SmallButton(button, value, palette))
            {
                UIGuard.Try("Options.ChoiceRow", onClick, "That option could not be opened.");
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += RowHeight + 2f;
        }

        /// <summary>
        /// One action button on a row of them, advancing the cursor.
        ///
        /// A disabled one keeps its place and says why on hover, rather than disappearing. These rows are fixed,
        /// unlike vanilla's rebuilt list, so a control that vanished between the menu and a colony would be
        /// harder to find again than one that is always where it was.
        /// </summary>
        private void ActionButton(ref float x, float y, float width, string label, UIColorPaletteDef palette,
            bool enabled, string disabledReason, System.Action action)
        {
            Rect rect = new Rect(x, y, width, RowHeight);

            x += width + 6f;

            if (!enabled)
            {
                // Rounded like the enabled one. A disabled control that is also a different shape reads as a
                // different kind of control rather than as the same one switched off.
                UIElementPainter.OutlineRounded(rect, palette.Border, palette.ControlBackgroundFaded);

                Color previous = GUI.color;
                TextAnchor previousAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = palette.TextDisabled;
                Widgets.Label(rect, label);

                Text.Anchor = previousAnchor;
                GUI.color = previous;

                if (Mouse.IsOver(rect) && !disabledReason.NullOrEmpty())
                    TooltipHandler.TipRegion(rect, (TipSignal) disabledReason);

                return;
            }

            if (!SmallButton(rect, label, palette))
                return;

            SoundDefOf.Click.PlayOneShotOnCamera();

            UIGuard.Try("Options.GameAction", action, "That action could not be started.");
        }

        /// <summary>
        /// The mod integrations section: things this mod adds alongside another mod, shown only when that mod is
        /// actually installed.
        ///
        /// <b>The category is always listed, and its contents are not.</b> A section that appeared and vanished
        /// with the mod list would be one nobody knows exists until they happen to have the right mod, and a
        /// player wondering whether this mod does anything with theirs would have nowhere to look for the answer.
        /// So the heading is permanent and says plainly when there is nothing to show.
        ///
        /// <b>Nothing here changes what the other mod does.</b> Each of these adds something that mod chose not
        /// to do, through whatever it made public, and is silent when it is absent. See <see cref="ModIntegrations"/>.
        /// </summary>
        private void DrawIntegrationSection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "Mod Integrations", palette);

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(0f, y, view.width, 40f),
                "Extras this mod adds when another mod is installed. Each one appears here only while that mod "
                + "is loaded, and none of them changes how that mod behaves on its own.");
            y += 44f;
            GUI.color = palette.TextPrimary;

            bool anything = false;

            if (ModIntegrations.Loaded(ModIntegrations.PhinixPackageId))
            {
                anything = true;

                GroupLabel(view, ref y, palette, "Phinix");

                WidgetToggle(view, ref y, palette, settings, Indent, "Notify incoming chat messages",
                    settings.notifyPhinixChat, value => settings.notifyPhinixChat = value,
                    "Shows a message card when somebody sends a chat message.\n\nPhinix plays a small sound for "
                    + "an incoming message and shows nothing, so one that arrives while you are looking at the "
                    + "map is easy to miss entirely. This is the visible half, and it stays silent because the "
                    + "sound is already theirs.\n\nMessages from you, from anyone you have blocked, and any that "
                    + "arrive while the chat tab is open are left alone.");

                WidgetToggle(view, ref y, palette, settings, Indent, "Suppress information logging",
                    settings.suppressPhinixInfoLog, value => settings.suppressPhinixInfoLog = value,
                    "Throws away the running commentary Phinix writes to the log: logins, logouts, name "
                    + "changes, created trades and every chat message received.\n\nOn a busy server that is a "
                    + "constant stream, and the cost is not the lines themselves but everything else they push "
                    + "out of a log you opened to look into something else.\n\nPhinix's warnings and errors are "
                    + "never suppressed, so nothing that reports a real problem is hidden. Clearing this shows "
                    + "their information lines again immediately, with no restart.");
            }

            if (anything)
                return;

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(Indent, y, view.width - Indent, 40f),
                "None of the mods this one integrates with are loaded, so there is nothing to configure here.");
            y += 44f;
            GUI.color = palette.TextPrimary;
        }

        /// <summary>
        /// Three corners on one row, as radio buttons.
        ///
        /// A row rather than a column because the choice is spatial: the three sit in roughly the arrangement
        /// they describe, which is quicker to read than three stacked lines of prose.
        /// </summary>
        private static void DockChooser(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings, NotificationDock current, System.Action<NotificationDock> apply,
            bool disabled)
        {
            NotificationDock[] options =
            {
                NotificationDock.TopLeft, NotificationDock.TopRight, NotificationDock.BottomRight
            };

            float available = view.width - Indent;
            float column = available / options.Length;

            // Greyed rather than hidden when the surface is handed back to vanilla. A control that vanishes takes
            // the explanation with it; a greyed one still says a choice exists and is not currently in use. The
            // control reports no clicks while disabled, so the guard is in one place rather than at every caller.
            for (int i = 0; i < options.Length; i++)
            {
                Rect cell = new Rect(Indent + i * column, y, column - 6f, RowHeight);
                bool chosen = current == options[i];

                if (!UIRadioButtonControl.Draw(cell, chosen, palette, DockLabel(options[i]),
                        disabled: disabled) || chosen)
                    continue;

                apply(options[i]);
                settings.Save();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += RowHeight + 6f;
        }

        private static string DockLabel(NotificationDock dock)
        {
            switch (dock)
            {
                case NotificationDock.TopLeft:
                    return "Top left";

                case NotificationDock.TopRight:
                    return "Top right";

                default:
                    return "Bottom right";
            }
        }

        /// <summary>
        /// How wide a letter row is drawn.
        ///
        /// <b>A slider rather than a number box, because there is no right answer to type.</b> Width here is
        /// bought with map: these rows sit over the colony, so wider is more readable and costs more of what is
        /// underneath. The default lines the stack up with the corner panel below it, which is the one value with
        /// a reason behind it rather than a preference.
        ///
        /// Saved on release rather than per pixel of drag, since each save rewrites the settings file.
        /// </summary>
        private void LetterWidthSlider(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            Color previous = GUI.color;

            if (!settings.restyleLetters)
                GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight),
                "Row width: " + Mathf.RoundToInt(settings.letterRowWidth) + " px");

            y += RowHeight;

            float width = Widgets.HorizontalSlider(new Rect(Indent, y, view.width - Indent - 10f, 22f),
                settings.letterRowWidth, 150f, 520f, false, null, null, null, 10f);

            GUI.color = previous;

            y += 30f;

            if (settings.restyleLetters && !Mathf.Approximately(width, settings.letterRowWidth))
            {
                // Applied immediately, so the stack behind this window resizes while the bar is being dragged.
                settings.letterRowWidth = width;
                letterWidthUnsaved = true;
            }

            // Written when the drag ends rather than on every frame of it. Each save rewrites the settings file,
            // and a slider raises a change on every pixel of movement -- so the flag is what separates "the value
            // changed" from "the player has finished choosing it".
            if (!letterWidthUnsaved || Input.GetMouseButton(0))
                return;

            letterWidthUnsaved = false;
            settings.Save();
        }

        /// <summary>Whether the row width has been dragged to somewhere that is not on disk yet.</summary>
        private static bool letterWidthUnsaved;

        /// <summary>
        /// Things this mod adds rather than restyles, grouped by where they appear.
        ///
        /// <b>Its own category, because none of the existing ones is a home for it.</b> An overlay drawn onto
        /// the world is not a corner readout and not a panel preference, and wedging it into Desktop Widgets
        /// would blur a category that currently means exactly one thing. The heading names a kind rather than a
        /// feature so the next optional addition has somewhere to go.
        /// </summary>
        /// <summary>
        /// Things the game does to you that this mod can stop doing.
        ///
        /// <b>Its own category rather than a group inside Additional Features,</b> added 2026-08-23. That
        /// category is for things this mod <em>adds</em> -- an overlay, a radius, a designation it issues for you
        /// -- and every one of its toggles turns a new behaviour on. These take something away instead: an
        /// interruption the game insists on, or a refusal it makes on the player's behalf. A player hunting for
        /// the setting that stops the research popup is not looking under a heading called Additional Features.
        ///
        /// <b>The livestock area setting moved here on Aaron's call,</b> and it is the case that shows the line is
        /// about direction rather than about size. It reads both ways -- it grants livestock an ability they did
        /// not have -- but what it actually does is lift RimWorld's refusal to give a roaming animal an area, and
        /// a refusal lifted belongs with the other refusals. It is still the one setting in this mod that changes
        /// what pawns are allowed to do, which is why it alone starts switched off.
        ///
        /// The heading names a kind rather than a feature, so the next interruption worth silencing has an
        /// obvious home and nobody has to widen a category to fit it.
        /// </summary>
        private void DrawQualityOfLifeSection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "Quality of Life", palette);

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(0f, y, view.width, 40f),
                "Interruptions RimWorld hands you and refusals it makes on your behalf, that this mod can take "
                + "back. Each is switched separately, and switching one off never affects the rest.");
            y += 44f;
            GUI.color = palette.TextPrimary;

            GroupLabel(view, ref y, palette, "Research");

            WidgetToggle(view, ref y, palette, settings, Indent, "Disable alert on research completion",
                settings.quietResearchCompletion, value => settings.quietResearchCompletion = value,
                "Finishing a research project sends a letter instead of stopping the game with a popup.\n\n"
                + "RimWorld's popup is modal: it takes the keyboard and nothing else responds until you dismiss "
                + "it, and it arrives on the colony's clock rather than yours -- which with three benches running "
                + "is several times a day, often mid-raid. Nothing in it needs an answer. It names the project "
                + "and then repeats the description already written on that project's page.\n\n"
                + "The letter says the same thing and waits in the corner until you want it. A project that "
                + "carries its own discovery letter still sends that one and no letter of ours, since that text "
                + "was written for the discovery.\n\n"
                + "Switch this off and the popup comes back exactly as the game shipped it.");

            y += 8f;

            GroupLabel(view, ref y, palette, "Animals");

            WidgetToggle(view, ref y, palette, settings, Indent, "Let an allowed area keep livestock",
                settings.penAnimalsUseAreas, value => settings.penAnimalsUseAreas = value,
                "RimWorld refuses an allowed area to any animal that roams, which is every animal the pen system "
                + "exists for: cows, sheep, chickens, muffalo. Turn this on and they can be given one like any "
                + "other animal, from this mod's animals tab or RimWorld's.\n\nThe whole AI honors it, so an area "
                + "keeps a cow out of your crops or off a bridge, and an animal left outside its area walks back "
                + "to it the way a penned one walks back to its pen.\n\nIt also stops them wandering off the map. "
                + "Livestock with an area, or standing in a pen meant for them, no longer starts roaming, and one "
                + "already on its way turns back the moment you give it either. RimWorld's own rule only counts "
                + "ropes and a fully enclosed pen, so a fence with a gap in it loses you the herd.\n\nAn animal an "
                + "area is keeping stops being pen business: nobody tries to rope it, and it can use ordinary "
                + "doors, which a roaming animal normally cannot. The other side of that is that a fence no "
                + "longer holds it either -- the area is what keeps it in now, so an area drawn wider than your "
                + "fence line will let it out.\n\nThis is the one setting in this mod that changes what pawns are "
                + "allowed to do rather than how something is drawn, which is why it starts switched off.");

            y += 8f;

            GroupLabel(view, ref y, palette, "Beds");

            WidgetToggle(view, ref y, palette, settings, Indent, "Allow communal bed assignment",
                settings.allowCommunalBeds, value => settings.allowCommunalBeds = value,
                "Adds a Communal switch to every colonist bed. A bed marked communal will take anyone who needs "
                + "a bed, whether or not it already has an owner.\n\nRimWorld's rule is that once a bed has an "
                + "owner, nobody else may sleep in it except a love partner. That is right for a private bedroom "
                + "and wrong for a spare bunk, the bed beside the workshop somebody naps in, or a bunk worked in "
                + "shifts -- and there is no way to say \"this one is mine, but help yourself when I am not in "
                + "it\".\n\nOwnership itself is untouched. A communal bed can still be assigned, its owner still "
                + "gets the bedroom they are owed, and the room still counts as theirs for mood. The only rule "
                + "relaxed is the refusal to let anyone else lie down in it.\n\nA bed with somebody already in it "
                + "is unavailable exactly as it is now: the mark lets a pawn consider the bed, it does not let "
                + "two pawns share a slot.\n\nOn by default, because on its own it changes nothing: it adds a "
                + "switch, and no bed behaves differently until you use it. Switching this off again leaves any "
                + "marks in the save and simply stops honoring them.");

            y += 8f;

            GroupLabel(view, ref y, palette, "Mood Fixes");

            WidgetToggle(view, ref y, palette, settings, Indent, "Barracks are neutral",
                settings.barracksAreNeutral, value =>
                {
                    settings.barracksAreNeutral = value;
                    Mood.MoodFixes.Apply();
                },
                "Sleeping in a shared room stops costing mood.\n\nRimWorld charges between -1 and -7 for it, "
                + "scaled by how good the room is, on top of whatever the room's own quality is already worth. A "
                + "barracks is what an early colony can afford and what a large one often still wants, so the "
                + "penalty falls hardest on a decision the player made deliberately.\n\nThe four best stages are "
                + "a bonus rather than a penalty -- a barracks impressive enough that nobody minds sharing it -- "
                + "and those are left as they are. This sets a floor, not a flat zero.\n\nIt applies to memories "
                + "colonists are already carrying, so the mood tab agrees the moment you switch it.",
                !Mood.MoodFixes.BarracksAvailable);

            y += 8f;

            GroupLabel(view, ref y, palette, "Salvage");

            WidgetToggle(view, ref y, palette, settings, Indent, "Ancient wreckage can be deconstructed",
                settings.salvageAncientWrecks, value =>
                {
                    settings.salvageAncientWrecks = value;
                    Salvage.AncientSalvage.Apply();
                },
                "Ruined tanks, APCs, warwalker limbs, dropships and the rest of the ancient junk a map generates "
                + "become deconstructible, and yield steel and components when they are.\n\nRimWorld marks them "
                + "non-deconstructible with no cost list, so the only way to clear one is to shoot it apart, and "
                + "that leaves nothing behind. What you get is priced off the wreck's own footprint: "
                + "five steel a cell, and a component for every eight, up to four. A ruined tank is fifteen "
                + "cells.\n\nOnly wreckage, named piece by piece. Cryptosleep pods, mech gestators and anything "
                + "Anomaly's quests are counting on stay exactly as they are.\n\nSwitch it off and they go back "
                + "to scenery. Any deconstruct order already standing on one is cleared at the same time.",
                !Salvage.AncientSalvage.Available);

            y += 8f;

            GroupLabel(view, ref y, palette, "Alerts");

            WidgetToggle(view, ref y, palette, settings, Indent, "Only warn about idle colonists you can help",
                settings.quietIdleAlert, value => settings.quietIdleAlert = value,
                "Drops two kinds of pawn from the Colonists idle alert: someone else's pawn standing in your "
                + "colony, and anyone with no work type open to them at all.\n\nThe alert is there to catch a "
                + "colonist you could go and give a job to. Neither of these is that. A visiting trader's guard "
                + "or a lodger you cannot command will idle for as long as they stay, and a pawn incapable of "
                + "every kind of work will idle forever, so the alert lights up and nothing you do puts it "
                + "out.\n\nRimWorld already does this for quest lodgers and for nobles whose title excuses them. "
                + "This carries the same rule to everyone else it applies to.\n\nSlaves still count. A slave "
                + "with nothing to do is the same problem as a colonist with nothing to do.");
            y += 8f;
        }

        private void DrawAdditionalFeaturesSection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "Additional Features", palette);

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(0f, y, view.width, 40f),
                "Things this mod adds on top of restyling what RimWorld already has. Each can be switched off "
                + "on its own, and switching one off never affects the rest.");
            y += 44f;
            GUI.color = palette.TextPrimary;

            GroupLabel(view, ref y, palette, "Overlays");

            WidgetToggle(view, ref y, palette, settings, Indent, "Enable customizable room name labels",
                settings.roomNameLabels, value => settings.roomNameLabels = value,
                "Draws each room's name onto its floor, out on the map, so you can read a base at a glance "
                + "without clicking into it.\n\nThe name starts as the one RimWorld already gives the room -- "
                + "Bedroom, Barracks, Machining workshop -- and follows it if the room's use changes. Rooms too "
                + "small to read are left blank. Growing zones and stockpiles get their own names the same "
                + "way.\n\nRename or recolor any of them from the Floor labels window.\n\nSwitching this off "
                + "stops the drawing and closes that window. Names you have already set are kept in the save, "
                + "so turning it back on restores them.");

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight),
                "Names you have set are kept either way, and old saves stay safe to load.");
            GUI.color = palette.TextPrimary;

            y += RowHeight + 6f;

            DrawRoomLabelFace(view, ref y, palette, settings);
            DrawRoomLabelMinimum(view, ref y, palette, settings);

            if (SmallButton(new Rect(Indent, y, 200f, RowHeight), "Open Floor labels", palette))
            {
                Find.WindowStack.Add(new FloorLabels.Dialog_FloorLabels());
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += RowHeight + 4f;

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(Indent, y, view.width - Indent, 40f),
                "Rename or recolor any room or zone, including ones too small to draw a label. Needs a colony "
                + "loaded.");
            GUI.color = palette.TextPrimary;

            y += 44f;

            GroupLabel(view, ref y, palette, "Bills");

            DrawBillCap(view, ref y, palette, settings);
            DrawIngredientRadius(view, ref y, palette, settings);

            WidgetToggle(view, ref y, palette, settings, Indent, "Point out bills nobody can work",
                settings.warnStalledBills, value => settings.warnStalledBills = value,
                "Marks a bill red in the bills window when no colonist is allowed to start it, whether because "
                + "of its skill range, its worker restriction, or the work type being switched off for "
                + "everybody.\n\nDisplay only. Nothing is ever suspended, altered or reassigned because of it.");

            WidgetToggle(view, ref y, palette, settings, Indent, "Highlight ore while the mine tool is out",
                settings.showMineableOverlay, value => settings.showMineableOverlay = value,
                "Shades every ore vein on the map in the colour of what it yields, but only while the Mine or "
                + "Mine vein tool is selected.\n\nRimWorld draws ore as rock with a slightly different texture, "
                + "which at anything but full zoom is no difference at all, so finding the compacted steel in a "
                + "mountain means hovering cell by cell.\n\nPlain stone is not shaded: on a mountain map that "
                + "would be the same as shading nothing.");

            WidgetToggle(view, ref y, palette, settings, Indent, "Ring the blast radius of explosives",
                settings.showBlastRadius, value => settings.showBlastRadius = value,
                "Draws the blast radius on the ground whenever you select something that can explode: an IED, a "
                + "shell rack, a chemfuel pile, a fuelled generator, a boomalope.\n\nThe number is on the info "
                + "card and nowhere on the map, which is where you decide how far from the wall to stack the "
                + "chemfuel.\n\nThe ring is the real radius, not the one printed on the item: a stack of shells "
                + "and a tank with fuel in it both blow up bigger than one of them would, and the ring grows to "
                + "match.");

            y += 8f;

            GroupLabel(view, ref y, palette, "Trade");

            // <b>One switch per replaced window, not one for the set.</b> The compatibility risk is per window: a
            // mod adding a column to the trade dialog has nothing to do with the caravan packer, and somebody who
            // has to switch one off should not lose the other three with it.
            //
            // These are escape hatches rather than fallbacks. This mod's rule is that a feature failing mid-draw
            // must not quietly hand off to vanilla, because that hides the defect; a choice made here, with the
            // consequence written down, is a different thing.
            WidgetToggle(view, ref y, palette, settings, Indent, "Use our trade window",
                settings.customTradeWindow, value => settings.customTradeWindow = value,
                "Replaces RimWorld's trade dialog with ours: buying and selling as separate views instead of one "
                + "flat interleaved list, a category rail with live counts, prices that say their level in a "
                + "word and their favour in a colour, a count you can type into, what you will still be holding "
                + "afterwards, and the deal itself standing beside the table where you can read all of it before "
                + "accepting.\n\nEvery price, limit, refusal and the trade itself stay RimWorld's. Nothing here "
                + "reimplements a trade rule.\n\nSwitch it off if you run mods that add to the vanilla trade "
                + "dialog. They patch that window, so they will never see ours, and a column or button they add "
                + "will simply stop appearing.");

            WidgetToggle(view, ref y, palette, settings, Indent, "Use our caravan packing window",
                settings.customCaravanWindow, value => settings.customCaravanWindow = value,
                "Replaces the form-caravan and split-caravan dialogs with one window. Same shape as the trade "
                + "screen, with mass and days in place of silver: the manifest stands beside the table with the "
                + "travel projection built in, each row is judged against this particular route rather than "
                + "reporting a raw stat, and the line that put you over capacity says so on itself.\n\nMass, "
                + "speed, food and visibility all come from RimWorld's own calculators.\n\nSwitch it off if you "
                + "run mods that add to either vanilla caravan dialog.");

            WidgetToggle(view, ref y, palette, settings, Indent, "Use our comms directory",
                settings.customCommsWindow, value => settings.customCommsWindow = value,
                "Replaces the comms console's float menu with a window of cards. Vanilla answers \"who can I "
                + "call\" with a list of bare text lines; every target already has to supply a name, a detail "
                + "line and a faction, so a card can show who they are, how they feel about you, what an orbital "
                + "trader is carrying and how long they are staying.\n\nTargets you cannot call stay visible and "
                + "dimmed with the reason, instead of the whole menu being replaced by one disabled line during "
                + "a solar flare.\n\nEvery call is the target's own, unchanged. Targets added by other mods draw "
                + "the same card as vanilla's.");

            WidgetToggle(view, ref y, palette, settings, Indent, "Show what a trade beacon reaches",
                settings.beaconReadout, value => settings.beaconReadout = value,
                "Adds a button to a selected orbital trade beacon that opens a readout of its reach: the cells "
                + "it covers, the stacks it can actually sell and what they are worth, what is inside the ring "
                + "but behind a wall, and how close the beacon is to the region limit the cell walk stops "
                + "at.\n\nThat limit is the one worth watching. Past it the ring is drawn at the size you asked "
                + "for and sells nothing extra, so a beacon with a wide radius can quietly be lying to "
                + "you.\n\nNothing is replaced by this one. RimWorld draws nothing at all for a built beacon, so "
                + "switching it off costs the readout and nothing else.");

            y += 4f;

            DrawBeaconRadius(view, ref y, palette, settings);

            y += 8f;

            GroupLabel(view, ref y, palette, "Plants");

            WidgetToggle(view, ref y, palette, settings, Indent, "Mark blighted crops for cutting",
                settings.autoCutBlightedPlants, value => settings.autoCutBlightedPlants = value,
                "The moment a crop catches blight it is designated to be cut, wherever it is: in the field, in a "
                + "hydroponics basin, or one plant in the middle of a healthy row.\n\nA blighted plant yields "
                + "nothing at all, and it spreads to its neighbours while it stands, so cutting it is the only "
                + "answer the game has. This saves you finding them among the healthy plants and dragging over "
                + "each one.\n\nA pending harvest order on the plant is replaced, since it cannot yield. A plant "
                + "you have set to never be cut is left alone. Blight already on the map when you switch this on "
                + "is left alone too; from then on, new blight is marked.");

            y += 8f;

            GroupLabel(view, ref y, palette, "Pawns");

            WidgetToggle(view, ref y, palette, settings, Indent, "Enable the character editor",
                settings.characterEditor, value => settings.characterEditor = value,
                "Adds an Edit button to a colonist's bio panel that opens a window for changing anything about "
                + "them: name, age, gender, looks, backstory, traits, skills, genes, health, needs, thoughts, "
                + "gear and relationships. On a dead pawn it can also bring them back.\n\nThis one changes the "
                + "game rather than the interface. Everything else in this mod reads your colony and hands it "
                + "back better arranged; this writes to it, and there is no version of a character editor that "
                + "is not a way to give somebody Shooting 20.\n\nWith it off the button does not exist -- not a "
                + "greyed one, an absent one. Nothing is patched and nothing is watching. Changes you made while "
                + "it was on are already part of your colony and stay that way.");

            WidgetToggle(view, ref y, palette, settings, Indent, "Describe pawns you are offered",
                settings.pawnDetailsOnOffers, value => settings.pawnDetailsOnOffers = value,
                "Puts a panel of skills, traits and refused work beside the letters that offer you a person: a "
                + "wanderer asking to join, a refugee at the door, a creepjoiner, a ransom demand, and the quest "
                + "reward that asks you to pick one of three.\n\nVanilla gives you the prose of the letter and a "
                + "row of names. Choosing between three strangers by name alone means accepting one, opening "
                + "their Bio tab, and finding out.\n\nDisplay only. The buttons, the offer and what happens next "
                + "are RimWorld's.");

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight),
                "Every change applies at once and can be reverted while the window is open. Resurrection "
                + "cannot.");
            GUI.color = palette.TextPrimary;

            y += RowHeight + 6f;

            y += 8f;

            GroupLabel(view, ref y, palette, "Music");

            string rival = Music.MusicRivals.Detected;

            WidgetToggle(view, ref y, palette, settings, Indent, "Enable the music player",
                settings.musicPlayer && rival == null, value => settings.musicPlayer = value,
                "Replaces RimWorld's hidden music system with one you can see: your own playlists, music from "
                + "your drive in ogg, wav, mp3, mp4 or m4a, and every song your mods added -- including the ones "
                + "the game will never choose on its own.\n\nOpen it from the speaker in the play settings row, "
                + "the strip in the corner, or the main menu.\n\nWith this off nothing is patched and nothing is "
                + "watching: the game picks songs the way it always did, and there is no window and no strip. "
                + "Playlists you made are kept and come back if you switch it on again.",
                rival != null);

            // The reason a locked toggle is locked, which is the one thing a player cannot work out for
            // themselves. Not an explanation of the control: without this line the feature reads as broken.
            if (rival != null)
            {
                GUI.color = palette.Warning;
                Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight),
                    "Switched off: " + rival + " is loaded and manages music itself.");
                GUI.color = palette.TextPrimary;

                y += RowHeight + 6f;
            }

            y += 8f;

            GroupLabel(view, ref y, palette, "Research");

            WidgetToggle(view, ref y, palette, settings, Indent, "Rebuild the research tab",
                settings.researchTab, value => settings.researchTab = value,
                "One flow chart in place of the category tabs, laid out from the prerequisites themselves, with "
                + "every DLC and every mod on the same canvas. Projects say which of the eight things they are "
                + "waiting on rather than all going grey together, and there is a queue: set four and the colony "
                + "works through them.\n\nThe arrangement is computed, so it will not be the one you have "
                + "learned. RimWorld's own coordinates are authored per tab and cannot be merged -- Main and "
                + "Anomaly are placed in the same space as each other -- so there is no version of one chart "
                + "that keeps them.\n\nWith this off the vanilla screen is untouched. A queue you have already "
                + "set is kept in the save and starts working again if you switch it back on.");

            if (settings.researchTab && ModsConfig.AnomalyActive)
                DrawAnomalyScript(view, ref y, palette, settings);

            y += 8f;

            GroupLabel(view, ref y, palette, "World map");

            DrawSiteFade(view, ref y, palette);
        }

        /// <summary>
        /// How long the markers a colony leaves behind stay on the planet.
        ///
        /// <b>Segments rather than a slider or a menu,</b> because the question is which of five answers rather
        /// than a number: a slider would offer 37 days, which is not a thing anybody means, and a menu would hide
        /// the current answer until it was opened. Four rows read as a table of what happens to what.
        ///
        /// <b>The count underneath is not decoration.</b> A lifespan is measured from the day a marker appeared,
        /// so choosing one shorter than what is already out there removes those markers within the hour. Saying
        /// how many, before the window is closed, is the difference between a setting and a surprise.
        ///
        /// <b>A kind whose def is not in this install is not drawn.</b> Camps at landmarks are Odyssey's, and a
        /// row that cannot ever apply is worse than an absent one.
        /// </summary>
        private void DrawSiteFade(Rect view, ref float y, UIColorPaletteDef palette)
        {
            y = Shared.TabParts.Note(new Rect(0f, y, view.width, 0f), y,
                "Abandoning a colony, launching a gravship and packing up a camp each leave a marker on the "
                + "planet. RimWorld clears up the plain camp after thirty days and keeps the rest for the whole "
                + "game, so all four are set to thirty days here. A marker is removed that long after it "
                + "appeared, however long ago that was. Keep means keep it.", palette,
                GameFont.Small, palette.TextSecondary) + 6f;

            foreach (WorldSites.SiteFadeKind kind in WorldSites.SiteFadeKinds.All)
            {
                if (!WorldSites.SiteFadeKinds.Available(kind))
                    continue;

                DrawSiteFadeRow(view, ref y, palette, kind);
            }

            int immediate;
            int counted = WorldSites.SiteFade.Counting(out immediate);

            if (!InGame)
            {
                y = Shared.TabParts.Note(new Rect(Indent, y, view.width - Indent, 0f), y,
                    "With no colony loaded there is nothing to count. These apply to the save you open next as "
                    + "well as to this one.", palette, GameFont.Tiny, palette.TextDisabled) + 4f;

                return;
            }

            string readout = counted == 0
                ? "No markers are on a clock."
                : counted + (counted == 1 ? " marker is" : " markers are") + " on a clock.";

            if (immediate > 0)
                readout += " " + immediate + " of them " + (immediate == 1 ? "has" : "have")
                    + " already outlived the lifespan set here and will be gone within the hour.";

            y = Shared.TabParts.Note(new Rect(Indent, y, view.width - Indent, 0f), y, readout, palette,
                GameFont.Tiny, immediate > 0 ? palette.Warning : palette.TextDisabled) + 4f;
        }

        /// <summary>One kind's row: its name, then the five lifespans with the current one filled.</summary>
        private void DrawSiteFadeRow(Rect view, ref float y, UIColorPaletteDef palette,
            WorldSites.SiteFadeKind kind)
        {
            int[] choices = WorldSites.SiteFadeKinds.Choices;
            int current = WorldSites.SiteFadeKinds.Days(kind);

            const float height = 26f;

            float available = view.width - Indent - 10f;
            float width = Mathf.Floor((available - (choices.Length - 1) * Shared.TabParts.SegmentGap)
                                      / choices.Length);

            Rect label = new Rect(Indent, y, available, RowHeight - 6f);

            Widgets.Label(label, kind.Label);

            // The whole row, label and segments together, so the explanation is reachable from wherever the
            // pointer happens to be rather than from the four words on the left only.
            Rect row = new Rect(Indent, y, available, RowHeight - 6f + height);

            if (!kind.Tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(row, kind.Tooltip);

            y += RowHeight - 6f;

            float x = Indent;

            for (int i = 0; i < choices.Length; i++)
            {
                int days = choices[i];

                Shared.TabParts.Segment(new Rect(x, y, width, height),
                    WorldSites.SiteFadeKinds.LabelOf(days), days == current, palette,
                    () => WorldSites.SiteFadeKinds.Set(kind, days));

                x += width + Shared.TabParts.SegmentGap;
            }

            y += height + 8f;
        }

        /// <summary>
        /// Which characters an undiscovered Anomaly project is written in.
        ///
        /// <b>Every option is labelled in its own characters,</b> asked for on 2026-08-23: an option you cannot
        /// preview is one you have to pick twice. The readable name is in the tooltip, which is where this mod
        /// puts that sort of thing anyway, and Off is the one option that stays in words because there is nothing
        /// to preview.
        ///
        /// <b>A script whose atlas did not load is not offered.</b> Three of the five are baked sheets under the
        /// mod's Fonts folder; a missing one is a broken install rather than a choice, and it is already reported
        /// in the log. Offering a swatch that would draw the generated marks instead would be a picker that lies.
        /// </summary>
        private void DrawAnomalyScript(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight), "Unknown anomaly script");

            y += RowHeight;

            float x = Indent;
            const float height = 30f;
            const float width = 92f;

            foreach (Research.ResearchScript script in Research.ResearchScripts.All)
            {
                if (!Research.ResearchMask.Usable(script))
                    continue;

                Rect cell = new Rect(x, y, width, height);
                bool chosen = settings.anomalyScript == script;
                Research.ResearchScript captured = script;

                Shared.TabParts.IconToggle(cell, null, chosen, palette, () =>
                {
                    settings.anomalyScript = captured;
                    settings.Save();
                }, Research.ResearchScripts.Named(script));

                if (script == Research.ResearchScript.Off)
                {
                    TextAnchor anchor = Text.Anchor;
                    GameFont font = Text.Font;

                    Text.Anchor = TextAnchor.MiddleCenter;
                    Text.Font = GameFont.Tiny;
                    GUI.color = chosen ? palette.WindowBackground : palette.TextSecondary;

                    Widgets.Label(cell, "Off");

                    GUI.color = palette.TextPrimary;
                    Text.Font = font;
                    Text.Anchor = anchor;
                }
                else
                {
                    // A sample rather than a fixed string: the same run-fitting the nodes use, so what is
                    // previewed is drawn the way the real thing will be.
                    Research.ResearchMask.Sample(cell.ContractedBy(8f), script,
                        chosen ? palette.WindowBackground : palette.Mood);
                }

                x += width + Shared.TabParts.SegmentGap;
            }

            y += height + 8f;
        }

        /// <summary>
        /// How many bills one workbench may hold.
        ///
        /// <b>The floor is vanilla's own fifteen and that is deliberate.</b> Raising a cap is safe; lowering one
        /// below what a bench already holds only produces a disabled Add button and a number that reads as an
        /// error, since nothing here ever deletes a bill.
        ///
        /// Saved when the drag ends rather than on every frame of it, like the room label minimum: each save
        /// rewrites the whole settings file and a slider raises a change per pixel of movement.
        /// </summary>
        private void DrawBillCap(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            int shown = Mathf.Clamp(settings.maxBillsPerBench, Bills.BillCap.Floor, Bills.BillCap.Ceiling);

            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight),
                "Most bills on one workbench: " + shown);

            y += RowHeight;

            float chosen = Widgets.HorizontalSlider(new Rect(Indent, y, view.width - Indent - 10f, 22f), shown,
                Bills.BillCap.Floor, Bills.BillCap.Ceiling, false, null, null, null, 1f);

            y += 26f;

            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight),
                "RimWorld's own limit is fifteen. Bills already on a bench are never removed by lowering this.");

            GUI.color = palette.TextPrimary;

            y += RowHeight + 6f;

            int rounded = Mathf.RoundToInt(chosen);

            if (rounded != settings.maxBillsPerBench)
            {
                settings.maxBillsPerBench = rounded;
                billCapUnsaved = true;
            }

            if (!billCapUnsaved || Input.GetMouseButton(0))
                return;

            billCapUnsaved = false;
            settings.Save();
        }

        /// <summary>Whether the bill cap has been dragged somewhere that is not on disk yet.</summary>
        private bool billCapUnsaved;

        /// <summary>
        /// The search radius a newly made bill starts with.
        ///
        /// <b>999 is vanilla's own value and reads as Whole map,</b> because that is what it means: a crafter will
        /// walk to the far corner for one item. Shipping anything smaller as the default would quietly stall bills
        /// for everybody who never opened this, in colonies whose stockpiles are simply far from the bench.
        ///
        /// Existing bills are never touched. Each bill's own radius is on its settings window.
        /// </summary>
        private void DrawIngredientRadius(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            int shown = Mathf.Clamp(Mathf.RoundToInt(settings.defaultIngredientRadius), 3, 999);

            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight),
                "New bills search for ingredients within: " + (shown >= 999 ? "the whole map" : shown + " tiles"));

            y += RowHeight;

            float chosen = Widgets.HorizontalSlider(new Rect(Indent, y, view.width - Indent - 10f, 22f), shown, 3f,
                999f, false, null, null, null, 1f);

            y += 26f;

            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight),
                "Applies to bills you make from now on. Bills you already have keep the radius you gave them.");

            GUI.color = palette.TextPrimary;

            y += RowHeight + 6f;

            int rounded = Mathf.RoundToInt(chosen);

            if (rounded != Mathf.RoundToInt(settings.defaultIngredientRadius))
            {
                settings.defaultIngredientRadius = rounded;
                radiusUnsaved = true;
            }

            if (!radiusUnsaved || Input.GetMouseButton(0))
                return;

            radiusUnsaved = false;
            settings.Save();
        }

        /// <summary>Whether the default ingredient radius has been dragged somewhere that is not on disk yet.</summary>
        private bool radiusUnsaved;

        /// <summary>
        /// How far an orbital trade beacon reaches.
        ///
        /// <b>Shown to one decimal, because vanilla's own number has one.</b> 7.9 is not a tidy figure and
        /// rounding it to 8 in the readout would leave the default looking like something this mod had changed.
        /// The slider steps in tenths for the same reason: the player can put it back exactly where it started.
        ///
        /// <b>Saved on mouse up rather than on every frame of the drag,</b> which is what the ingredient radius
        /// above does and for the same reason: a slider writes a file per pixel otherwise.
        /// </summary>
        private void DrawBeaconRadius(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            float current = Mathf.Clamp(settings.tradeBeaconRadius, Trade.TradeBeaconRadius.Minimum,
                Trade.TradeBeaconRadius.Maximum);

            string suffix = Mathf.Approximately(current, Trade.TradeBeaconRadius.Default)
                ? " (RimWorld's own)"
                : string.Empty;

            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight),
                "Orbital trade beacons reach: " + current.ToString("0.#") + " tiles" + suffix);

            y += RowHeight;

            float chosen = Widgets.HorizontalSlider(new Rect(Indent, y, view.width - Indent - 10f, 22f), current,
                Trade.TradeBeaconRadius.Minimum, Trade.TradeBeaconRadius.Maximum, false, null, null, null, 0.1f);

            y += 26f;

            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight * 2f),
                "What a beacon covers, what it can sell, and the outline drawn while you place one all follow "
                + "this. Walls and doors still stop it: a beacon reaches through open floor only, as it does in "
                + "the base game.");

            GUI.color = palette.TextPrimary;

            y += RowHeight * 2f + 6f;

            if (!Mathf.Approximately(chosen, settings.tradeBeaconRadius))
            {
                settings.tradeBeaconRadius = chosen;
                beaconUnsaved = true;
            }

            if (!beaconUnsaved || Input.GetMouseButton(0))
                return;

            beaconUnsaved = false;
            settings.Save();
        }

        /// <summary>Whether the beacon radius has been dragged somewhere that is not on disk yet.</summary>
        private bool beaconUnsaved;

        /// <summary>
        /// The typeface, with each choice drawn in itself.
        ///
        /// <b>Every option is previewed rather than named,</b> because the only reason to offer a choice here is
        /// that the faces look different -- and a list of names in the interface font would show none of that. The
        /// preview walks the same glyph metrics the map does, so what is shown is what will be drawn.
        ///
        /// Rows rather than a dropdown for the same reason: a float menu would hide two of the three behind a
        /// click, which is exactly the comparison somebody is here to make.
        /// </summary>
        private void DrawRoomLabelFace(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight), "Typeface");

            y += RowHeight + 2f;

            foreach (FloorLabelFace face in (FloorLabelFace[]) System.Enum.GetValues(typeof(FloorLabelFace)))
            {
                Rect row = new Rect(Indent, y, Mathf.Min(420f, view.width - Indent - 10f), 38f);
                bool chosen = settings.roomLabelFace == face;

                // Composited rather than handed over translucent: an outline is painted as two fills, so an
                // overlay given as the inside lands on the border colour instead of on the panel and comes out
                // very nearly solid. The chosen row was reading as a block of accent rather than as a tinted one.
                if (chosen)
                    UIElementPainter.OutlineRounded(row, palette.Accent,
                        UIElementPainter.Composite(palette.PanelBackground, palette.SelectionOverlay));
                else if (Mouse.IsOver(row))
                    UIElementPainter.OutlineRounded(row, palette.Border,
                        UIElementPainter.Composite(palette.PanelBackground, palette.HoverOverlay));
                else
                    UIElementPainter.OutlineRounded(row, palette.Border, palette.PanelBackground);

                // The sample reads as a room name because that is what it will be, rather than the usual
                // pangram: what matters here is how a short colony word looks at a glance.
                FloorLabelPreview.Draw(new Rect(row.x + 10f, row.y + 5f, row.width - 130f, row.height - 10f),
                    "Dining room", face, palette);

                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = chosen ? palette.Accent : palette.TextDisabled;

                Widgets.Label(new Rect(row.x, row.y, row.width - 10f, row.height), Named(face));

                GUI.color = palette.TextPrimary;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;

                if (Widgets.ButtonInvisible(row) && !chosen)
                {
                    settings.roomLabelFace = face;
                    settings.Save();

                    // Every cached mesh addresses the old atlas, so they go now rather than being noticed as
                    // garbled letters on the next frame.
                    FloorLabelMeshes.Clear();
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                y += 40f;
            }

            y += 4f;
        }

        private static string Named(FloorLabelFace face)
        {
            switch (face)
            {
                case FloorLabelFace.HammersmithOne: return "Hammersmith One";
                default: return "Oswald Bold";
            }
        }

        /// <summary>
        /// The smallest room that gets a label.
        ///
        /// <b>Written when the drag ends, not on every frame of it,</b> the same way the letter row width is
        /// handled: each save rewrites the whole settings file, and a slider raises a change per pixel of
        /// movement. The flag is what separates "the value moved" from "the player has finished choosing".
        ///
        /// Bounds come from the settings class rather than being repeated here, so the slider cannot offer a
        /// value the reader would clamp away -- which would look like a setting that refuses to stick.
        /// </summary>
        private void DrawRoomLabelMinimum(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            Color previous = GUI.color;

            if (!settings.roomNameLabels)
                GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight),
                "Smallest room to label: " + settings.roomLabelMinimumCells + " tiles");

            y += RowHeight;

            float chosen = Widgets.HorizontalSlider(new Rect(Indent, y, view.width - Indent - 10f, 22f),
                settings.roomLabelMinimumCells, UIOverhaulSettingsFile.MinimumRoomCellsFloor,
                UIOverhaulSettingsFile.MinimumRoomCellsCeiling, false, null, null, null, 1f);

            GUI.color = previous;

            y += 28f;

            int rounded = Mathf.RoundToInt(chosen);

            if (settings.roomNameLabels && rounded != settings.roomLabelMinimumCells)
            {
                settings.roomLabelMinimumCells = rounded;
                roomMinimumUnsaved = true;
            }

            if (!roomMinimumUnsaved || Input.GetMouseButton(0))
                return;

            roomMinimumUnsaved = false;
            settings.Save();
        }

        /// <summary>Whether the room label minimum has been dragged somewhere that is not on disk yet.</summary>
        private bool roomMinimumUnsaved;

        /// <summary>
        /// A heading inside a section, for the two halves of the widget list.
        ///
        /// Lighter than <see cref="SectionHeader"/> on purpose: these divide a section rather than start one, and
        /// giving them the same weight would read as two sections that had lost their place in the category list.
        /// </summary>
        private static void GroupLabel(Rect view, ref float y, UIColorPaletteDef palette, string title)
        {
            Color previous = GUI.color;
            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(0f, y, view.width, 24f), title);
            GUI.color = previous;

            y += 24f;
            Widgets.DrawBoxSolid(new Rect(0f, y, view.width, 1f), palette.Border);
            y += 6f;
        }

        /// <summary>
        /// One widget's checkbox.
        ///
        /// Takes a setter rather than a <c>ref</c> to a field, because the six of these differ only in which field
        /// they write and a ref parameter cannot be handed a field of an object in a list-like call sequence without
        /// each line becoming its own block. Six near-identical blocks is exactly how one of them ends up saving the
        /// wrong setting.
        /// </summary>
        private static void WidgetToggle(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings, float indent, string label, bool current,
            System.Action<bool> apply, string tooltip = null, bool disabled = false)
        {
            bool value = current;

            if (UICheckboxControl.Draw(new Rect(indent, y, view.width - indent, RowHeight), ref value, palette,
                    label, tooltip, disabled: disabled))
            {
                apply(value);
                settings.Save();
            }

            y += RowHeight + 2f;
        }

        /// <summary>
        /// Raids and incidents the player would rather the game did not send.
        ///
        /// <b>This is Raid and Event Manager, brought in with No Way Jose's permission and reimplemented.</b>
        /// That mod was twenty XML patch operations that zeroed a def's selection weight at load time, which is
        /// why it needed XML Extensions and why it asked for a restart after every change. Ours are Harmony
        /// filters over the same twenty things, so there is no dependency and a switch takes effect on the next
        /// raid the storyteller rolls.
        ///
        /// <b>Every switch starts off, and off means nothing is patched.</b> A player who never opens this
        /// section is running the game exactly as Ludeon shipped it.
        ///
        /// <b>A switch with nothing to act on is not drawn.</b> Without Anomaly there is no shambler assault def,
        /// so that row is absent rather than present and inert -- the same rule the rest of this window follows
        /// for expansion content.
        /// </summary>
        private void DrawThreatsSection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "Raids and Incidents", palette);

            // Measured rather than allowed two rows. At the widths this column takes on a narrow window it is
            // three lines, and a two-row allowance loses the last one -- which is the fault that had the saved
            // characters window reading "and the durable half of" and stopping.
            y = Shared.TabParts.Note(new Rect(0f, y, view.width, 0f), y,
                "Anything ticked here is never chosen again. The raid still happens -- it arrives some other "
                + "way, or the storyteller sends something else.", palette, GameFont.Small,
                palette.TextSecondary) + 6f;

            string group = null;

            foreach (Threats.ThreatToggle toggle in Threats.ThreatToggles.All)
            {
                if (!Threats.ThreatToggles.Available(toggle))
                    continue;

                if (toggle.Group != group)
                {
                    group = toggle.Group;

                    y += 6f;

                    GroupLabel(view, ref y, palette, group);
                }

                Threats.ThreatToggle captured = toggle;

                WidgetToggle(view, ref y, palette, settings, Indent, toggle.Label,
                    Threats.ThreatToggles.IsOff(toggle),
                    value => Threats.ThreatToggles.Set(captured, value), toggle.Tooltip);
            }

            y += 10f;

            y = Shared.TabParts.Note(new Rect(0f, y, view.width, 0f), y,
                "Raid and Event Manager by No Way Jose, rebuilt here with his permission.", palette,
                GameFont.Small, palette.TextDisabled) + 4f;
        }

        /// <summary>
        /// How large a gravship the game will allow.
        ///
        /// <b>One switch in front of three settings, and off means the game's own numbers.</b> Asked for in those
        /// terms on 2026-08-23. These are the only settings in this window that change what can be built rather
        /// than how something is drawn, so the three below are drawn greyed and inert until the switch is on --
        /// visible, because they are what the switch is for, and unreachable, because they do nothing until it
        /// moves.
        ///
        /// <b>Both sliders read out in the game's own units and name vanilla's value.</b> A radius means nothing
        /// as a bare number; it means something next to "the game's own is 18.9".
        /// </summary>
        private void DrawGravshipSection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "Gravships", palette);

            y = Shared.TabParts.Note(new Rect(0f, y, view.width, 0f), y,
                "How far substructure may be built from the grav engine, how many tiles a ship may cover, and "
                + "how many field extenders it may have. With the switch off, every one of these is the number "
                + "Odyssey ships with.", palette, GameFont.Small, palette.TextSecondary) + 6f;

            WidgetToggle(view, ref y, palette, settings, Indent, "Enable gravship overrides",
                settings.gravshipOverrides, value =>
                {
                    settings.gravshipOverrides = value;
                    Gravships.GravshipTuning.Apply();
                },
                "Hands the three settings below to the game.\n\nWith this off they are ignored and the engine, "
                + "the tile limit and the extender limit are written back to the values Odyssey shipped -- so "
                + "turning it off is a return to vanilla rather than a promise to stop interfering. Nothing here "
                + "is stored in your save: a colony built with a larger ship keeps whatever it has already built "
                + "if you switch it off, and cannot extend it further.");

            y += 8f;

            GroupLabel(view, ref y, palette, "Size");

            DrawGravRadius(view, ref y, palette, settings);

            WidgetToggle(view, ref y, palette, settings, Indent,
                "Remove maximum tiles and govern ship size by grav engine and extender radius only",
                settings.gravshipUnlimitedTiles, value =>
                {
                    settings.gravshipUnlimitedTiles = value;
                    Gravships.GravshipTuning.Apply();
                },
                "A gravship is normally limited twice: by where substructure may be built, and by a count of how "
                + "many tiles the engine can support. This lifts the count, so only the radii decide.\n\nThe "
                + "engine's support becomes 99999 and extenders stop adding any of their own, which is what makes "
                + "the extender limit below a question about reach rather than about tiles.",
                !settings.gravshipOverrides);

            DrawGravExtenders(view, ref y, palette, settings);
        }

        /// <summary>The engine's footprint radius, from one cell up to four times whatever this install's is.</summary>
        private void DrawGravRadius(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            bool on = settings.gravshipOverrides;

            float vanilla = Gravships.GravshipTuning.VanillaRadius;
            float current = Gravships.GravshipTuning.Radius(settings);

            Color previous = GUI.color;

            if (!on)
                GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight),
                "Grav engine radius: " + current.ToString("0.0") + " tiles"
                + (Mathf.Abs(current - vanilla) < 0.05f ? "   (the game's own)" : "   (the game's own is "
                    + vanilla.ToString("0.0") + ")"));

            y += RowHeight;

            float chosen = Widgets.HorizontalSlider(new Rect(Indent, y, view.width - Indent - 10f, 22f),
                current, Gravships.GravshipTuning.RadiusFloor, Gravships.GravshipTuning.RadiusCeiling,
                false, null, null, null, 0.1f);

            GUI.color = previous;

            y += 28f;

            if (on && Mathf.Abs(chosen - current) >= 0.001f)
            {
                settings.gravEngineRadius = chosen;
                gravSliderUnsaved = true;
            }

            // Tested outside the change above, and that is the whole trick: the frame the mouse is released on
            // is a frame where the value did not change, so a commit inside the branch would never run.
            if (!gravSliderUnsaved || Input.GetMouseButton(0))
                return;

            Commit(settings);
        }

        /// <summary>How many extenders may link to one engine, from none to twenty.</summary>
        private void DrawGravExtenders(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            bool on = settings.gravshipOverrides;

            int vanilla = Gravships.GravshipTuning.VanillaExtenders;
            int current = Gravships.GravshipTuning.ExtenderLimit(settings, vanilla);

            Color previous = GUI.color;

            if (!on)
                GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(Indent, y, view.width - Indent, RowHeight),
                "Maximum grav extenders: " + current
                + (current == vanilla ? "   (the game's own)" : "   (the game's own is " + vanilla + ")"));

            y += RowHeight;

            float chosen = Widgets.HorizontalSlider(new Rect(Indent, y, view.width - Indent - 10f, 22f),
                current, 0f, Gravships.GravshipTuning.ExtenderCeiling, false, null, null, null, 1f);

            GUI.color = previous;

            y += 28f;

            int rounded = Mathf.RoundToInt(chosen);

            if (on && rounded != current)
            {
                settings.gravExtenderMax = rounded;
                gravSliderUnsaved = true;
            }

            if (!gravSliderUnsaved || Input.GetMouseButton(0))
                return;

            Commit(settings);
        }

        /// <summary>
        /// Saves and applies once the slider has been let go.
        ///
        /// Shared by both gravship sliders. Writing on every dragged pixel would rewrite the defs and relink
        /// every extender on every map sixty times a second, which is a settings file written sixty times a
        /// second as well.
        /// </summary>
        private void Commit(UIOverhaulSettingsFile settings)
        {
            gravSliderUnsaved = false;

            settings.Save();

            Gravships.GravshipTuning.Apply();
        }

        /// <summary>Whether a gravship slider has been dragged somewhere that is not on disk yet.</summary>
        private bool gravSliderUnsaved;

        /// <summary>
        /// The diagnostics section.
        ///
        /// Last, and described plainly as something to turn on when asked to, because that is the only time it is
        /// useful. It is not tied to RimWorld's dev mode: dev mode stays on for whole sessions for unrelated
        /// reasons, and this is noisy enough to be worth choosing deliberately.
        /// </summary>
        private void DrawDiagnosticsSection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "Diagnostics", palette);

            bool debug = settings.debugLogging;

            if (UICheckboxControl.Draw(new Rect(0f, y, view.width, RowHeight), ref debug, palette,
                    "Write debug detail to the log"))
            {
                settings.debugLogging = debug;

                // Pushed straight through rather than waiting for a reload, so turning it on starts logging
                // now. Probes that allocate control ids are latched at launch and wait for a restart; see
                // UIDebug.InstrumentControlIds.
                UIDebug.Enabled = debug;
                settings.Save();
            }

            y += RowHeight + 4f;

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(0f, y, view.width, 56f),
                "Off unless you have been asked to turn it on. Adds detail to the log for diagnosing a "
                + "problem; some of it only starts collecting after a restart.");
            y += 60f;
            GUI.color = palette.TextPrimary;

            // There is deliberately no switch here for the modernized debug log. The two options would be this
            // mod's log and the one it exists to replace, which is not a choice worth offering -- the same
            // reasoning that retired the speed glyph toggle. Whether it applies at all is decided by whether
            // Modern Dev Tools is loaded, and that is not a preference either.

            bool console = settings.showLoadingConsole;

            if (UICheckboxControl.Draw(new Rect(0f, y, view.width, RowHeight), ref console, palette,
                    "Show loading console on main menu",
                    "Everything the loading screen said, with a timestamp on each line and the slow phases "
                    + "marked, as a scrollable panel down the left of the main menu.\n\nThe log is kept whether "
                    + "or not this is ticked, so switching it on now shows the load that already happened. There "
                    + "is nothing to reproduce and no restart needed."))
            {
                settings.showLoadingConsole = console;
                settings.Save();
            }

            y += RowHeight + 4f;

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(0f, y, view.width, 56f),
                "A loading screen normally throws away everything it shows. This keeps it, which is how you "
                + "find out which phase of a long load is the slow one. The panel has a button to copy the "
                + "whole thing for pasting into a bug report.");
            y += 60f;
            GUI.color = palette.TextPrimary;

        }

        /// <summary>
        /// The developer tools section: things that are opened rather than set.
        ///
        /// <b>Separate from Diagnostics on purpose.</b> Diagnostics holds switches that change what the mod
        /// records and shows; these are windows a person opens to go and look at something. Mixing the two would
        /// mean a category where half the rows do nothing until you tick them and the other half do something
        /// the moment you click.
        /// </summary>
        private void DrawDeveloperToolsSection(Rect view, ref float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            SectionHeader(view, ref y, "Developer Tools", palette);

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(0f, y, view.width, 40f),
                "Tools for working on mods rather than playing with them. The switches here change what the "
                + "game exposes to you; the buttons open a window and show you something.");
            y += 44f;
            GUI.color = palette.TextPrimary;

            GroupLabel(view, ref y, palette, "Developer mode");

            // <b>Vanilla hides this row outright once dev mode has been permanently disabled,</b> and hiding it
            // is the entire point of that file: it exists so the switch cannot be found again. Drawing the row
            // anyway would hand back precisely what that file was created to take away, so the same test guards
            // ours. Still drawn while dev mode is on, which is vanilla's own way back out once the file exists.
            if (!DevModePermanentlyDisabledUtility.Disabled || Prefs.DevMode)
            {
                bool devMode = Prefs.DevMode;

                // DevelopmentMode is RimWorld's own key, so this row is already translated everywhere the game
                // is. Assigned without saving Prefs by hand, which is what the rest of this window does and what
                // the setter expects: it clears god mode and verbose logging on its way down, then calls Apply.
                if (UICheckboxControl.Draw(new Rect(Indent, y, view.width - Indent, RowHeight), ref devMode,
                        palette, "DevelopmentMode".Translate(),
                        "The game's own developer tools: the debug toolbar across the top, the inspector, and "
                        + "the actions the developer palette below collects.\n\nTurning it off also clears god "
                        + "mode and verbose logging. That is RimWorld's behavior rather than ours, and it is why "
                        + "this belongs beside the palette instead of under Graphics."))
                    UIGuard.Try("Options.SetDevMode", () => Prefs.DevMode = devMode, null);

                y += RowHeight + 2f;
            }

            // Keybindings and the developer options are not reimplemented here, so the window that has them
            // stays one click away. Opened through the bypass, since this mod otherwise replaces it.
            //
            // Here rather than under Graphics, where it started. Nobody hunting for dev mode reads a heading
            // about resolution and UI scale, and Hoki had to go looking. Moved on Aaron's suggestion 2026-08-25.
            if (SmallButton(new Rect(Indent, y, 230f, RowHeight), "Keybindings and dev options", palette))
            {
                UIGuard.Try("Options.OpenVanillaOptions", Patch_WindowStack_Add_Options.OpenVanilla,
                    "RimWorld's own options window could not be opened.");

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += RowHeight + 6f;

            GroupLabel(view, ref y, palette, "Developer palette");

            if (SmallButton(new Rect(Indent, y, 200f, RowHeight), "Open developer palette", palette))
            {
                UIGuard.Try("Options.OpenDevPalette",
                    () => Find.WindowStack.Add(new Dialog_DevPalette()),
                    "The developer palette could not be opened.");

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += RowHeight + 4f;

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(Indent, y, view.width - Indent, 72f),
                "Every developer action in the game, searchable in one place. The game's own menu filters only "
                + "the tab you are standing in, so finding an action means knowing which of nine tabs owns it "
                + "first.\n\nThis also replaces that menu when dev mode's own button is used.");
            y += 76f;
            GUI.color = palette.TextPrimary;

            WidgetToggle(view, ref y, palette, settings, Indent,
                "Skip confirmation on irreversible actions", settings.skipDevActionConfirm,
                value => settings.skipDevActionConfirm = value,
                "The palette marks actions that read as irreversible, such as destroying everything on the map, "
                + "and asks before running one.\n\nTurn this on to run them immediately, the way the game's own "
                + "menu does. The confirmation also offers an Always allow button, which turns this on.");

            y += 6f;

            GroupLabel(view, ref y, palette, "Research bands");

            if (SmallButton(new Rect(Indent, y, 200f, RowHeight), "Open research bands", palette))
            {
                Research.Dialog_ResearchBands.Open();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += RowHeight + 4f;

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(Indent, y, view.width - Indent, 72f),
                "Every research project in the game, the band the research tab sorted it into, and the sentence "
                + "saying why.\n\nThe bands are worked out from what a project unlocks, so a mod nobody has "
                + "written yet still lands somewhere. Filter to Other to see the projects that rule could not "
                + "read: if a whole mod is sitting there, it wants naming outright.");
            y += 76f;
            GUI.color = palette.TextPrimary;

            GroupLabel(view, ref y, palette, "XML Workbench");

            if (SmallButton(new Rect(Indent, y, 200f, RowHeight), "Open XML Workbench", palette))
            {
                UIGuard.Try("Options.OpenWorkbench",
                    () => Find.WindowStack.Add(new Dialog_XmlWorkbench()),
                    "The XML workbench could not be opened.");

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += RowHeight + 4f;

            GUI.color = palette.TextSecondary;
            Widgets.Label(new Rect(Indent, y, view.width - Indent, 72f),
                "Run an XPath expression against the game's definition XML and see exactly which nodes it "
                + "matches, and which file each one came from.\n\nThis is the answer to \"why did my patch "
                + "operation fail\": a patch whose xpath matches nothing fails silently hours later, during a "
                + "load, with nothing pointing at the expression.");
            y += 76f;
            GUI.color = palette.TextPrimary;
        }

        private static void SectionHeader(Rect view, ref float y, string title, UIColorPaletteDef palette)
        {
            GameFont previous = Text.Font;
            Text.Font = GameFont.Medium;
            GUI.color = palette.TextPrimary;
            Widgets.Label(new Rect(0f, y, view.width, 30f), title);
            Text.Font = previous;

            y += 30f;
            Widgets.DrawBoxSolid(new Rect(0f, y, view.width, 1f), palette.Border);
            y += 8f;
        }

        /// <summary>
        /// A button, and the mod's own one since 2026-08-25.
        ///
        /// <b>It used to set no font at all,</b> which meant it took whatever the last thing drawn had left in
        /// <c>Text.Font</c> -- so its size depended on what happened to be above it rather than on what it is.
        /// Naming it Small and then not saying so is the same defect the window titles have to guard against by
        /// resetting the font on the line after they use Medium. It is Small now because it says it is.
        /// </summary>
        private static bool SmallButton(Rect r, string label, UIColorPaletteDef palette)
        {
            return UIActionButtonControl.Draw(r, label, palette, false, true, GameFont.Small);
        }
    }
}
