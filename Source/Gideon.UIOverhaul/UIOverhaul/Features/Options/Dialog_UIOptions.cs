using System.Collections.Generic;
using Gideon.UIFramework.Components.Colors;
using Gideon.UIFramework.Components.Images;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIFramework.Patches.UIElements;
using Gideon.UIOverhaul.Features.ButtonBar;
using Gideon.UIOverhaul.Features.ButtonBar.BarWidgets;
using Gideon.UIOverhaul.Features.DevTools;
using Gideon.UIOverhaul.Features.Diagnostics;
using Gideon.UIOverhaul.Features.Integrations;
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

        /// <summary>Writes the open mod's settings out, the way closing vanilla's dialog would.</summary>
        public override void PreClose()
        {
            base.PreClose();
            LeaveModSettings();
        }

        protected override float Margin => 0f;

        public Dialog_UIOptions()
        {
            doCloseX = false;
            forcePause = false;
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
        /// the rects the page actually drew. The thing to watch is anything that captures a coordinate here and
        /// uses it after this returns, once the matrix is back -- that is worth a look on a real page rather than
        /// an assurance from me.
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

            // Only Layout and Repaint are watched, and that is what tells a redirect apart from a page doing
            // something perfectly ordinary. A float menu off a dropdown, or a confirmation dialog, is opened in
            // response to a click, which arrives as a mouse event -- never on these two passes. A redirect opens
            // its window unconditionally, so it shows up here on the first pass of the first frame.
            WindowStack stack = Find.WindowStack;

            bool watching = !hosting
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

                        Rect page = new Rect(0f, 0f, authored.x, authored.y);

                        if (hosting)
                            finished = XmlExtensionsIntegration.Draw(page);
                        else
                            mod.DoSettingsWindowContents(page);
                    }
                    finally
                    {
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
                    if (opened is ImmediateWindow || WindowsBeforePage.Contains(opened))
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

                // Centred against the two lines of text rather than pinned to the top, so a square icon sits
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
                MakeCategory("Diagnostics", "Logging", DrawDiagnosticsSection),
                MakeCategory("Developer Tools", "For working on mods", DrawDeveloperToolsSection),
                MakeModSettingsCategory()
            };
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
        /// How this mod's own panels behave, as opposed to what colour they are.
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
                + "left alone, because it resizes itself to fit whatever you have selected.");

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

            WidgetToggle(view, ref y, palette, settings, Indent, "Draw letters as labelled rows",
                settings.restyleLetters, value => settings.restyleLetters = value,
                "The stack of events waiting to be read. RimWorld draws these as icons and shows the label only "
                + "when you point at one; rows show it outright.\n\nClearing this gives back the icons, and with "
                + "them the width setting below stops applying.");

            DockChooser(view, ref y, palette, settings, settings.letterDock,
                value => settings.letterDock = value, !settings.restyleLetters);

            LetterWidthSlider(view, ref y, palette, settings);

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

            DrawGameActions(view, ref y, palette, playing);
            DrawSavingGroup(view, ref y, palette, settings);
            DrawGeneralGroup(view, ref y, palette, playing);
            DrawGraphicsGroup(view, ref y, palette);
            DrawAudioGroup(view, ref y, palette);
            DrawQuitGroup(view, ref y, palette, playing);

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

            // The same eight steps vanilla offers, in days. A free slider would let somebody ask for an autosave
            // every four seconds.
            float[] intervals = { 0.05f, 0.075f, 0.1f, 0.125f, 0.25f, 0.5f, 1f, 2f };

            ChoiceRow(view, ref y, palette, "Autosave interval", AutosaveLabel(Prefs.AutosaveIntervalDays),
                () =>
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();

                    foreach (float days in intervals)
                    {
                        float captured = days;

                        options.Add(new FloatMenuOption(AutosaveLabel(captured),
                            UIGuard.Wrap("Options.SetAutosave",
                                () => Prefs.AutosaveIntervalDays = captured)));
                    }

                    Find.WindowStack.Add(new FloatMenu(options));
                });

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

        private void DrawGraphicsGroup(Rect view, ref float y, UIColorPaletteDef palette)
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

            // Keybindings and the developer options are not reimplemented here, so the window that has them
            // stays one click away. Opened through the bypass, since this mod otherwise replaces it.
            if (SmallButton(new Rect(Indent, y, 230f, RowHeight), "Keybindings and dev options", palette))
            {
                UIGuard.Try("Options.OpenVanillaOptions", Patch_WindowStack_Add_Options.OpenVanilla,
                    "RimWorld's own options window could not be opened.");

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += RowHeight + 6f;

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

        /// <summary>A labelled volume slider, written back by the caller.</summary>
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

        /// <summary>A labelled row whose value is a button opening a menu of choices.</summary>
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
                "Tools for working on mods rather than playing with them. Nothing here changes anything about "
                + "your game; they open a window and show you something.");
            y += 44f;
            GUI.color = palette.TextPrimary;

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

        private static bool SmallButton(Rect r, string label, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(r);
            UIElementPainter.PaintButton(r, palette, over, over && Input.GetMouseButton(0));

            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;

            GUI.color = palette.TextPrimary;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label);

            Text.Anchor = previousAnchor;
            GUI.color = previousColor;

            return Widgets.ButtonInvisible(r);
        }
    }
}
