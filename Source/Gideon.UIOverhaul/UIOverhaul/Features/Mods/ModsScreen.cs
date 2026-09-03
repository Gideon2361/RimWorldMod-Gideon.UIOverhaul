using System;
using System.Collections.Generic;
using Gideon.UIFramework.Components.Colors;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Mods
{
    /// <summary>
    /// The mods page, drawn over RimWorld's own.
    ///
    /// <b>One list, not two.</b> Vanilla puts active and inactive mods in separate 250 unit columns, so the
    /// answer to "is this one on" is which column it landed in, and a search hits either. Here the active mods
    /// come first carrying their load order numeral, the rest sit under a separator with a dash where the
    /// numeral would be, and turning one on is the checkbox rather than a move between columns.
    ///
    /// <b>Load order is the one place in this mod where a numbered marker earns its keep.</b> It is a real
    /// sequence, the whole reason the screen exists, and vanilla conveys it only by vertical position inside a
    /// scrolling box.
    ///
    /// <b>Three troubles, three colors, and a band that names both sides.</b> See <see cref="ModTrouble"/> for
    /// why they are not one red.
    ///
    /// This is not a tab and does not behave like one: there is no colony behind it, so nothing here can name a
    /// map or a pawn, and leaving is a transaction rather than a close. See <see cref="ModsReflection"/>.
    /// </summary>
    internal static class ModsScreen
    {
        private const float Pad = 8f;

        private const float HeaderHeight = 66f;

        private const float GlyphSize = 34f;

        private const float GlyphGap = 10f;

        private const float RailWidth = 200f;

        private const float DetailWidth = 300f;

        private const float BandHeight = 34f;

        private const float BarHeight = 44f;

        private const float ColumnsHeight = 22f;

        private const float RowHeight = 26f;

        private const float OrderWidth = 34f;

        private const float CheckWidth = 26f;

        private const float SourceWidth = 96f;

        private const float StateWidth = 104f;

        // Rail keys. Strings rather than an enum because the rail control is keyed by string, and a key that is
        // also the thing shown in a tooltip is one fewer mapping to keep in step.
        private const string KeyAll = "all";
        private const string KeyActive = "active";
        private const string KeyAvailable = "available";
        private const string KeyMissing = "missing";
        private const string KeyOrder = "order";
        private const string KeyClash = "clash";
        private const string KeyVersion = "version";
        private const string KeyOfficial = "official";
        private const string KeyWorkshop = "workshop";
        private const string KeyLocal = "local";

        private static readonly Texture2D Glyph;

        private static string scope = KeyAll;

        private static string selected;

        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search mods"
        };

        private static Vector2 railScroll;

        private static bool railDragging;

        private static float railOffset;

        private static Vector2 listScroll;

        private static Vector2 detailScroll;

        private static readonly List<UIRailElement> RailItems = new List<UIRailElement>();

        private static readonly List<ModRow> Shown = new List<ModRow>();

        private static int filledVersion = -1;

        private static string filledScope;

        private static string filledSearch;

        // The detail pane redraws one mod every frame, so everything it derives from that mod is worked out once
        // per selection rather than once per frame: the requirement list allocates and enumerates, and measuring
        // a description wraps a paragraph of text to a width.
        private static readonly List<ModRequirement> Needs = new List<ModRequirement>();

        private static string detailFor;

        private static int detailVersion = -1;

        private static float detailHeight;

        private static float detailWidth;

        private static string sentence;

        private static int sentenceVersion = -1;

        static ModsScreen()
        {
            Texture2D glyph = null;

            UIGuard.Try("Mods.Glyph",
                () => glyph = ContentFinder<Texture2D>.Get("UI/MainButtonIcons/Mods", false));

            Glyph = glyph;
        }

        /// <summary>Called when the page opens, before anything is drawn.</summary>
        internal static void Opened()
        {
            scope = KeyAll;
            selected = null;
            Search.Clear();
            railScroll = Vector2.zero;
            listScroll = Vector2.zero;
            detailScroll = Vector2.zero;

            ModsRoster.Rebuild();
        }

        internal static void Draw(Page_ModsConfig page, Rect rect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            if (palette == null)
                return;

            // The roster is rebuilt by every mutation rather than every frame; this only covers a first draw
            // that somehow arrived without one, which would otherwise be an empty screen.
            if (ModsRoster.Rows.Count == 0 && ModsRoster.InstalledCount == 0)
                ModsRoster.Rebuild();

            Header(new Rect(rect.x, rect.y, rect.width, HeaderHeight), palette);

            float y = rect.y + HeaderHeight + Pad;

            if (ModsRoster.ProblemCount > 0)
            {
                Band(new Rect(rect.x, y, rect.width, BandHeight), page, palette);
                y += BandHeight + Pad;
            }

            float bottom = rect.yMax - BarHeight - Pad;

            Rect body = new Rect(rect.x, y, rect.width, bottom - y);

            Rail(new Rect(body.x, body.y, RailWidth, body.height), palette);

            float listX = body.x + RailWidth + Pad;
            float listWidth = body.width - RailWidth - DetailWidth - Pad - Pad;

            List(new Rect(listX, body.y, listWidth, body.height), page, palette);

            Detail(new Rect(listX + listWidth + Pad, body.y, DetailWidth, body.height), page, palette);

            Bar(new Rect(rect.x, rect.yMax - BarHeight, rect.width, BarHeight), page, palette);
        }

        // -------------------------------------------------------------------------------------------
        // Header
        // -------------------------------------------------------------------------------------------

        private static void Header(Rect rect, UIColorPaletteDef palette)
        {
            // SurfaceSunken, the same fill the rail beside it uses: header and rail are both chrome framing the
            // content, so they share a surface and the blocks between them sit above it.
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(10f);

            Color identity = ModsFaces.AccentOf(palette);

            float text = inner.x;

            if (Glyph != null)
            {
                Rect slot = new Rect(inner.x, inner.y + (inner.height - GlyphSize) * 0.5f, GlyphSize, GlyphSize);

                Color previous = GUI.color;

                GUI.color = identity;
                GUI.DrawTexture(slot, Glyph);
                GUI.color = previous;

                text = slot.xMax + GlyphGap;
            }

            TabParts.RowLabel(new Rect(text, inner.y + 2f, 340f, 26f), "Mods", identity,
                GameFont.Medium, ModsFaces.Display, ModsFaces.Size.Title);

            string subtitle = ModsRoster.ActiveCount + " active of " + ModsRoster.InstalledCount + " installed";

            if (ModsRoster.ProblemCount > 0)
            {
                subtitle += "  -  " + ModsRoster.ProblemCount
                    + (ModsRoster.ProblemCount == 1 ? " problem needs attention" : " problems need attention");
            }

            TabParts.RowLabel(new Rect(text, inner.y + 28f, 460f, 18f), subtitle, palette.TextSecondary,
                GameFont.Tiny, ModsFaces.Condensed, ModsFaces.Size.Subtitle);

            float right = inner.xMax;

            if (ModsRoster.ProblemCount > 0)
            {
                right = TabParts.Readout(inner, right, "problems", ModsRoster.ProblemCount.ToString(),
                    palette, null, palette.Danger);
            }

            if (ModsRoster.WrongVersionCount > 0)
            {
                right = TabParts.Readout(inner, right, "wrong ver", ModsRoster.WrongVersionCount.ToString(),
                    palette, null, palette.Warning);
            }

            right = TabParts.Readout(inner, right, "installed", ModsRoster.InstalledCount.ToString(), palette);

            TabParts.Readout(inner, right, "active", ModsRoster.ActiveCount.ToString(), palette, null, identity);
        }

        // -------------------------------------------------------------------------------------------
        // Problem band
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// What is wrong, in words, across the top.
        ///
        /// <b>Both sides of the conflict are named.</b> The count alone is what vanilla already has; what a
        /// player needs is which mod and what it wants, because that is the sentence they would otherwise have
        /// to assemble by clicking through rows.
        /// </summary>
        private static void Band(Rect rect, Page_ModsConfig page, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Danger, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(8f);

            float buttonWidth = TabParts.ButtonWidth("Sort load order");

            Rect button = new Rect(inner.xMax - buttonWidth, inner.y - 2f, buttonWidth, inner.height + 4f);

            if (TabParts.Button(button, "Sort load order", palette, ModsRoster.OrderCount > 0))
            {
                UIGuard.Try("Mods.Sort", () =>
                {
                    ModsConfig.TrySortMods();
                    ModsReflection.MarkListsDirty(page);
                    ModsRoster.Rebuild();
                });
            }

            string lead = ModsFaces.Caps("blocking");

            float leadWidth = UITextControl.Width(lead, ModsFaces.Mono, ModsFaces.Size.Chip) + 10f;

            TabParts.RowLabel(new Rect(inner.x, inner.y, leadWidth, inner.height), lead, palette.Danger,
                GameFont.Tiny, ModsFaces.Mono, ModsFaces.Size.Chip);

            Rect words = new Rect(inner.x + leadWidth, inner.y, button.x - inner.x - leadWidth - 8f,
                inner.height);

            TabParts.RowLabel(words, Sentence(), palette.TextSecondary, GameFont.Small, ModsFaces.Body,
                ModsFaces.Size.DetailBody);
        }

        /// <summary>
        /// The first two problems in words, and a count for the rest.
        ///
        /// Cached against the roster version, because it walks every row and builds strings, and the answer only
        /// changes when the roster does.
        /// </summary>
        private static string Sentence()
        {
            if (sentenceVersion == ModsRoster.Version)
                return sentence;

            sentenceVersion = ModsRoster.Version;

            List<string> parts = new List<string>();

            for (int i = 0; i < ModsRoster.Rows.Count && parts.Count < 2; i++)
            {
                ModRow row = ModsRoster.Rows[i];

                switch (row.Trouble)
                {
                    case ModTrouble.MissingDependency:
                        parts.Add(row.Name + " is missing something it needs");
                        break;
                    case ModTrouble.Incompatible:
                        parts.Add(row.Name + " clashes with another active mod");
                        break;
                    case ModTrouble.OrderIssue:
                        parts.Add(row.Name + " is in the wrong place in the load order");
                        break;
                }
            }

            sentence = string.Join(".  ", parts.ToArray());

            int rest = ModsRoster.ProblemCount - parts.Count;

            if (rest > 0)
                sentence += ".  " + rest + (rest == 1 ? " other" : " others");

            return sentence;
        }

        // -------------------------------------------------------------------------------------------
        // Rail
        // -------------------------------------------------------------------------------------------

        private static void Rail(Rect rect, UIColorPaletteDef palette)
        {
            RailItems.Clear();

            Color identity = ModsFaces.AccentOf(palette);

            RailItems.Add(Section("Library", palette));
            RailItems.Add(Entry(KeyAll, "All mods", ModsRoster.InstalledCount, palette, identity));
            RailItems.Add(Entry(KeyActive, "Active", ModsRoster.ActiveCount, palette, identity));
            RailItems.Add(Entry(KeyAvailable, "Available",
                ModsRoster.InstalledCount - ModsRoster.ActiveCount, palette, identity));

            if (ModsRoster.ProblemCount > 0 || ModsRoster.WrongVersionCount > 0)
            {
                RailItems.Add(new UIRailDividerControl());
                RailItems.Add(Section("Needs attention", palette));

                if (ModsRoster.MissingCount > 0)
                {
                    RailItems.Add(Trouble(KeyMissing, "Missing dependency", ModsRoster.MissingCount,
                        palette.Danger, palette, identity));
                }

                if (ModsRoster.IncompatibleCount > 0)
                {
                    RailItems.Add(Trouble(KeyClash, "Incompatible", ModsRoster.IncompatibleCount,
                        palette.Danger, palette, identity));
                }

                if (ModsRoster.OrderCount > 0)
                {
                    RailItems.Add(Trouble(KeyOrder, "Load order", ModsRoster.OrderCount,
                        palette.Accent, palette, identity));
                }

                if (ModsRoster.WrongVersionCount > 0)
                {
                    RailItems.Add(Trouble(KeyVersion, "Wrong version", ModsRoster.WrongVersionCount,
                        palette.Warning, palette, identity));
                }
            }

            RailItems.Add(new UIRailDividerControl());
            RailItems.Add(Section("Source", palette));
            RailItems.Add(Entry(KeyOfficial, "Core and DLC", ModsRoster.OfficialCount, palette, identity));
            RailItems.Add(Entry(KeyWorkshop, "Workshop", ModsRoster.WorkshopCount, palette, identity));
            RailItems.Add(Entry(KeyLocal, "Local", ModsRoster.LocalCount, palette, identity));

            string picked = UIRailControl.Draw(rect, RailItems, scope, ref railScroll, ref railDragging,
                ref railOffset, palette);

            if (!picked.NullOrEmpty())
                scope = picked;
        }

        private static UIRailSectionHeaderControl Section(string title, UIColorPaletteDef palette)
        {
            return new UIRailSectionHeaderControl(ModsFaces.Caps(title))
            {
                Face = ModsFaces.Mono,
                Points = ModsFaces.Size.RailHead,
                Color = palette.TextDisabled
            };
        }

        private static UIRailClickableEntry Entry(string key, string label, int count,
            UIColorPaletteDef palette, Color identity)
        {
            UIRailClickableEntry entry = new UIRailClickableEntry
            {
                Label = label,
                Count = count,
                Face = ModsFaces.Condensed,
                Points = ModsFaces.Size.RailName,
                CountFace = ModsFaces.Mono,
                CountPoints = ModsFaces.Size.RailCount,
                TextColor = palette.TextSecondary,
                CountColor = palette.TextDisabled,
                SelectionBar = identity
            };

            entry.SetKey(key);

            return entry;
        }

        /// <summary>A rail entry for one kind of trouble, carrying that trouble's color as its swatch.</summary>
        private static UIRailClickableEntry Trouble(string key, string label, int count, Color mark,
            UIColorPaletteDef palette, Color identity)
        {
            UIRailClickableEntry entry = Entry(key, label, count, palette, identity);

            entry.Swatch = mark;
            entry.CountColor = mark;

            return entry;
        }

        // -------------------------------------------------------------------------------------------
        // The list
        // -------------------------------------------------------------------------------------------

        private static void List(Rect rect, Page_ModsConfig page, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(1f);

            Columns(new Rect(inner.x, inner.y, inner.width, ColumnsHeight), palette);

            Rect view = new Rect(inner.x, inner.y + ColumnsHeight, inner.width,
                inner.height - ColumnsHeight);

            Fill();

            // A separator row costs one row of height, and only when both sides of it are non-empty.
            int firstInactive = -1;

            for (int i = 0; i < Shown.Count; i++)
            {
                if (!Shown[i].Active)
                {
                    firstInactive = i;
                    break;
                }
            }

            bool separator = firstInactive > 0;

            float height = Shown.Count * RowHeight + (separator ? RowHeight : 0f);

            Rect content = new Rect(0f, 0f, view.width - 16f, height);

            Widgets.BeginScrollView(view, ref listScroll, content);

            // <b>Only the rows actually on screen are drawn.</b> This used to run over the whole of Shown,
            // which is fine for a dozen mods and ruinous for two hundred: every row measures and ellipsises a
            // name, measures and paints a pill, and draws a checkbox. Doing that for rows scrolled far out of
            // sight is what made this list lag, the same fault and the same fix as the architect tab.
            //
            // <b>The scroll view is still told the full height above,</b> so the bar, its travel and what
            // scrolling reaches are all unchanged. Only the drawing is skipped, and nothing else needs the
            // skipped rows: hover and clicks can only concern the row under the cursor, which by definition is
            // on screen.
            // Two rows of slack at the top rather than one: a row below the separator sits one row further
            // down than its index suggests, so the index that is first visible can be one lower than the plain
            // division gives. Cheaper to draw two extra rows than to special case the boundary.
            int firstDrawn = Mathf.Max(0, Mathf.FloorToInt(listScroll.y / RowHeight) - 2);
            int lastDrawn = Mathf.Min(Shown.Count - 1,
                Mathf.CeilToInt((listScroll.y + view.height) / RowHeight) + 1);

            for (int i = firstDrawn; i <= lastDrawn; i++)
            {
                // The separator sits above row firstInactive, so every row from there down is pushed one row
                // further into the content. Working the offset out per row rather than accumulating a y keeps
                // the culled loop landing on exactly the same pixels the full loop would have.
                float y = (i + (separator && i >= firstInactive ? 1 : 0)) * RowHeight;

                if (separator && i == firstInactive)
                    Separator(new Rect(0f, y - RowHeight, content.width, RowHeight), palette);

                Row(new Rect(0f, y, content.width, RowHeight), Shown[i], page, palette, i);
            }

            // Drawn outside the loop as well, for the case where the separator is above the first drawn row but
            // its own row is still on screen.
            if (separator && firstDrawn > firstInactive)
            {
                float at = firstInactive * RowHeight;

                if (at + RowHeight >= listScroll.y && at <= listScroll.y + view.height)
                    Separator(new Rect(0f, at, content.width, RowHeight), palette);
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Applies the rail scope and the search box to the roster.
        ///
        /// <b>Only when one of its three inputs moved.</b> Filtering two hundred mods through two case insensitive
        /// substring tests is not expensive once, and it was being done sixty times a second for an answer that
        /// only changes when the player types, picks a rail entry, or turns a mod on.
        /// </summary>
        private static void Fill()
        {
            string needle = Search.Text ?? "";

            if (ModsRoster.Version == filledVersion && scope == filledScope && needle == filledSearch)
                return;

            filledVersion = ModsRoster.Version;
            filledScope = scope;
            filledSearch = needle;

            Shown.Clear();

            for (int i = 0; i < ModsRoster.Rows.Count; i++)
            {
                ModRow row = ModsRoster.Rows[i];

                if (!InScope(row))
                    continue;

                if (!Search.Matches(row.Name) && !Search.Matches(row.PackageId))
                    continue;

                Shown.Add(row);
            }
        }

        private static bool InScope(ModRow row)
        {
            switch (scope)
            {
                case KeyActive: return row.Active;
                case KeyAvailable: return !row.Active;
                case KeyMissing: return row.Trouble == ModTrouble.MissingDependency;
                case KeyClash: return row.Trouble == ModTrouble.Incompatible;
                case KeyOrder: return row.Trouble == ModTrouble.OrderIssue;
                case KeyVersion: return row.Trouble == ModTrouble.WrongVersion;
                case KeyOfficial: return row.Origin == ModOrigin.Game || row.Origin == ModOrigin.Expansion;
                case KeyWorkshop: return row.Origin == ModOrigin.Workshop;
                case KeyLocal: return row.Origin == ModOrigin.Local;
                default: return true;
            }
        }

        private static void Columns(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            GUI.color = palette.Border;
            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);
            GUI.color = Color.white;

            float x = rect.x + 8f;

            Heading(new Rect(x, rect.y, OrderWidth, rect.height), "#", palette, TextAnchor.MiddleRight);

            x += OrderWidth + 8f + CheckWidth;

            float names = rect.width - (x - rect.x) - SourceWidth - StateWidth - 24f;

            Heading(new Rect(x, rect.y, names, rect.height), "Mod", palette, TextAnchor.MiddleLeft);

            x += names + 8f;

            Heading(new Rect(x, rect.y, SourceWidth, rect.height), "Source", palette, TextAnchor.MiddleLeft);

            x += SourceWidth + 8f;

            Heading(new Rect(x, rect.y, StateWidth, rect.height), "State", palette, TextAnchor.MiddleRight);
        }

        private static void Heading(Rect rect, string text, UIColorPaletteDef palette, TextAnchor anchor)
        {
            TextAnchor previous = Text.Anchor;

            Text.Anchor = anchor;

            // LabelEllipses rather than RowLabel, which forces MiddleLeft and would silently left-align the two
            // headings that have to sit over right-aligned columns.
            GUI.color = palette.TextDisabled;

            UITextControl.LabelEllipses(rect, ModsFaces.Caps(text), ModsFaces.Mono, ModsFaces.Size.Chip);

            GUI.color = Color.white;

            Text.Anchor = previous;
        }

        private static void Separator(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            int count = ModsRoster.InstalledCount - ModsRoster.ActiveCount;

            TabParts.RowLabel(new Rect(rect.x + 8f, rect.y, rect.width - 16f, rect.height),
                ModsFaces.Caps("available  -  " + count), palette.TextDisabled, GameFont.Tiny,
                ModsFaces.Mono, ModsFaces.Size.Chip);
        }

        private static void Row(Rect rect, ModRow row, Page_ModsConfig page, UIColorPaletteDef palette,
            int index)
        {
            bool chosen = row.PackageId == selected;

            if (chosen)
                Widgets.DrawBoxSolid(rect, palette.SelectionOverlay);
            else if (index % 2 == 1)
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            if (Mouse.IsOver(rect) && !chosen)
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            Color identity = ModsFaces.AccentOf(palette);

            float x = rect.x + 8f;

            // Order numeral
            Rect order = new Rect(x, rect.y, OrderWidth, rect.height);

            TextAnchor previousAnchor = Text.Anchor;

            Text.Anchor = TextAnchor.MiddleRight;

            GUI.color = row.Active ? (chosen ? identity : palette.TextSecondary) : palette.TextDisabled;

            UITextControl.LabelEllipses(order, row.Active ? row.Order.ToString() : "-", ModsFaces.Mono,
                ModsFaces.Size.RowFigure);

            GUI.color = Color.white;

            Text.Anchor = previousAnchor;

            x += OrderWidth + 8f;

            // The switch. Core has no switch at all rather than a disabled one, because a control that cannot
            // be operated still invites the press that teaches you it cannot.
            Rect check = new Rect(x, rect.y + (rect.height - 16f) * 0.5f, 16f, 16f);

            if (row.Locked)
            {
                UIElementPainter.OutlineRounded(check, palette.Border, palette.ControlBackgroundFaded);
            }
            else
            {
                bool active = row.Active;

                if (UICheckboxControl.Draw(check, ref active, palette) && active != row.Active)
                    Toggle(row, active, page);
            }

            x += CheckWidth;

            float names = rect.width - (x - rect.x) - SourceWidth - StateWidth - 24f;

            TabParts.RowLabel(new Rect(x, rect.y, names, rect.height), row.Name,
                row.Active ? palette.TextPrimary : palette.TextSecondary, GameFont.Small,
                ModsFaces.Condensed, ModsFaces.Size.RowName);

            x += names + 8f;

            TabParts.RowLabel(new Rect(x, rect.y, SourceWidth, rect.height), OriginWord(row.Origin),
                palette.TextDisabled, GameFont.Tiny, ModsFaces.Mono, ModsFaces.Size.RowFigure);

            x += SourceWidth + 8f;

            State(new Rect(x, rect.y, StateWidth, rect.height), row, palette);

            if (Widgets.ButtonInvisible(rect))
                selected = row.PackageId;
        }

        private static string OriginWord(ModOrigin origin)
        {
            switch (origin)
            {
                case ModOrigin.Game: return "Game";
                case ModOrigin.Expansion: return "DLC";
                case ModOrigin.Workshop: return "Workshop";
                default: return "Local";
            }
        }

        /// <summary>The state pill, right aligned so the column reads as a column.</summary>
        private static void State(Rect rect, ModRow row, UIColorPaletteDef palette)
        {
            string text;
            Color color;

            switch (row.Trouble)
            {
                case ModTrouble.MissingDependency:
                    text = "Missing";
                    color = palette.Danger;
                    break;
                case ModTrouble.Incompatible:
                    text = "Clash";
                    color = palette.Danger;
                    break;
                case ModTrouble.OrderIssue:
                    text = "Order";
                    color = palette.Accent;
                    break;
                case ModTrouble.WrongVersion:
                    text = row.BuiltFor ?? "Old";
                    color = palette.Warning;
                    break;
                default:
                    if (row.Locked)
                    {
                        text = "Required";
                        color = ModsFaces.AccentOf(palette);
                    }
                    else
                    {
                        text = row.BuiltFor;
                        color = palette.TextDisabled;
                    }

                    break;
            }

            if (text.NullOrEmpty())
                return;

            float width = TabParts.PillWidth(text, rect.width, ModsFaces.Mono, ModsFaces.Size.Chip);

            TabParts.Pill(rect, rect.xMax - width, rect.y + (rect.height - 18f) * 0.5f, text, color, palette,
                rect.width, null, ModsFaces.Mono, ModsFaces.Size.Chip);
        }

        private static void Toggle(ModRow row, bool active, Page_ModsConfig page)
        {
            UIGuard.Try("Mods.Toggle", () =>
            {
                ModsConfig.SetActive(row.Mod, active);
                ModsReflection.MarkListsDirty(page);
                ModsRoster.Rebuild();
            });
        }

        // -------------------------------------------------------------------------------------------
        // Detail
        // -------------------------------------------------------------------------------------------

        private static void Detail(Rect rect, Page_ModsConfig page, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            ModRow row = Selected();

            Rect inner = rect.ContractedBy(12f);

            if (row == null)
            {
                TabParts.RowLabel(inner, "Pick a mod to read about it", palette.TextDisabled, GameFont.Small,
                    ModsFaces.Body, ModsFaces.Size.DetailBody);

                return;
            }

            Recache(row);

            float y = inner.y;

            TabParts.RowLabel(new Rect(inner.x, y, inner.width, 24f), row.Name, palette.TextPrimary,
                GameFont.Medium, ModsFaces.Display, ModsFaces.Size.DetailName);

            y += 24f;

            string author = row.Mod.AuthorsString;

            if (!author.NullOrEmpty())
            {
                TabParts.RowLabel(new Rect(inner.x, y, inner.width, 18f), author, palette.TextSecondary,
                    GameFont.Tiny, ModsFaces.Body, ModsFaces.Size.DetailBody);

                y += 20f;
            }

            y += 6f;

            y = Fact(inner, y, "package", row.PackageId, palette, palette.TextSecondary);

            if (!row.Mod.ModVersion.NullOrEmpty())
                y = Fact(inner, y, "version", row.Mod.ModVersion, palette, palette.TextSecondary);

            if (!row.BuiltFor.NullOrEmpty())
            {
                y = Fact(inner, y, "built for", row.BuiltFor, palette,
                    row.Trouble == ModTrouble.WrongVersion ? palette.Warning : palette.TextSecondary);
            }

            if (row.Active)
            {
                y = Fact(inner, y, "load order", row.Order + " of " + ModsRoster.ActiveCount, palette,
                    palette.TextSecondary);
            }

            y += 8f;

            if (row.Active && !row.Locked)
                y = Reorder(inner, y, row, page, palette);

            y = Requirements(inner, y, row, palette);

            // The description takes whatever is left, which is the right way round: it is the least urgent
            // thing on the pane and the only one whose length is out of our hands.
            Rect box = new Rect(inner.x, y, inner.width, inner.yMax - y);

            if (box.height > 30f)
            {
                TabParts.RowLabel(new Rect(box.x, box.y, box.width, 16f), ModsFaces.Caps("description"),
                    palette.TextDisabled, GameFont.Tiny, ModsFaces.Mono, ModsFaces.Size.DetailLabel);

                Rect view = new Rect(box.x, box.y + 18f, box.width, box.height - 18f);

                string text = row.Mod.Description ?? "";

                if (detailWidth != view.width)
                {
                    detailWidth = view.width;
                    detailHeight = UITextControl.Height(text, ModsFaces.Body, ModsFaces.Size.DetailBody,
                        view.width - 16f);
                }

                Rect content = new Rect(0f, 0f, view.width - 16f, detailHeight);

                Widgets.BeginScrollView(view, ref detailScroll, content);

                GUI.color = palette.TextSecondary;

                UITextControl.Paragraph(content, text, ModsFaces.Body, ModsFaces.Size.DetailBody);

                GUI.color = Color.white;

                Widgets.EndScrollView();
            }
        }

        private static ModRow Selected()
        {
            if (selected.NullOrEmpty())
                return null;

            for (int i = 0; i < ModsRoster.Rows.Count; i++)
            {
                if (ModsRoster.Rows[i].PackageId == selected)
                    return ModsRoster.Rows[i];
            }

            return null;
        }

        private static float Fact(Rect inner, float y, string caption, string value,
            UIColorPaletteDef palette, Color color)
        {
            const float LabelWidth = 74f;

            TabParts.RowLabel(new Rect(inner.x, y, LabelWidth, 16f), ModsFaces.Caps(caption),
                palette.TextDisabled, GameFont.Tiny, ModsFaces.Mono, ModsFaces.Size.DetailLabel);

            TabParts.RowLabel(new Rect(inner.x + LabelWidth, y, inner.width - LabelWidth, 16f), value, color,
                GameFont.Tiny, ModsFaces.Mono, ModsFaces.Size.DetailLabel);

            return y + 17f;
        }

        /// <summary>
        /// What this mod asks of the list, and whether the list gives it.
        ///
        /// Read straight off the game's own <c>GetRequirements</c>, so a dependency, an incompatibility and
        /// their satisfied states are RimWorld's answer rather than a second opinion of ours.
        /// </summary>
        /// <summary>
        /// Moves one mod through the load order by hand.
        ///
        /// <b>Two buttons rather than a drag.</b> A drag is the better gesture and is what the mockup shows, but
        /// it is also the one that can silently drop a mod somewhere the player did not mean on a list two
        /// hundred long. These move a single place at a time through <c>ModsConfig.TryReorder</c>, which is the
        /// game own reorder and the thing that enforces the forced load rules, so a move it refuses is refused
        /// here too rather than half applied.
        /// </summary>
        private static float Reorder(Rect inner, float y, ModRow row, Page_ModsConfig page,
            UIColorPaletteDef palette)
        {
            TabParts.RowLabel(new Rect(inner.x, y, inner.width, 16f), ModsFaces.Caps("load order"),
                palette.TextDisabled, GameFont.Tiny, ModsFaces.Mono, ModsFaces.Size.DetailLabel);

            y += 18f;

            float half = (inner.width - Pad) * 0.5f;

            Rect up = new Rect(inner.x, y, half, 24f);
            Rect down = new Rect(inner.x + half + Pad, y, half, 24f);

            // One-based on the screen, zero-based in the game list.
            int index = row.Order - 1;

            if (TabParts.Button(up, "Move up", palette, row.Order > 1))
                Move(index, index - 1, page);

            if (TabParts.Button(down, "Move down", palette, row.Order < ModsRoster.ActiveCount))
                Move(index, index + 1, page);

            return y + 32f;
        }

        private static void Move(int from, int to, Page_ModsConfig page)
        {
            UIGuard.Try("Mods.Reorder", () =>
            {
                string error;

                if (!ModsConfig.TryReorder(from, to, out error))
                {
                    // The refusal is the game telling us a forced load rule would break. Saying so is the whole
                    // value of surfacing it, since the alternative is a button that looks broken.
                    if (!error.NullOrEmpty())
                        Messages.Message(error, MessageTypeDefOf.RejectInput, false);

                    return;
                }

                ModsReflection.MarkListsDirty(page);
                ModsRoster.Rebuild();
            });
        }

        /// <summary>Refreshes the per-selection caches when the selection or the roster moved.</summary>
        private static void Recache(ModRow row)
        {
            if (detailFor == row.PackageId && detailVersion == ModsRoster.Version)
                return;

            detailFor = row.PackageId;
            detailVersion = ModsRoster.Version;
            detailWidth = -1f;

            Needs.Clear();

            UIGuard.Try("Mods.Requirements", () =>
            {
                foreach (ModRequirement requirement in row.Mod.GetRequirements())
                {
                    if (requirement != null)
                        Needs.Add(requirement);
                }
            });
        }

        private static float Requirements(Rect inner, float y, ModRow row, UIColorPaletteDef palette)
        {
            List<ModRequirement> all = Needs;

            if (all.Count == 0)
                return y;

            TabParts.RowLabel(new Rect(inner.x, y, inner.width, 16f), ModsFaces.Caps("requirements"),
                palette.TextDisabled, GameFont.Tiny, ModsFaces.Mono, ModsFaces.Size.DetailLabel);

            y += 18f;

            for (int i = 0; i < all.Count && i < 6; i++)
            {
                ModRequirement requirement = all[i];

                bool met = false;

                UIGuard.Try("Mods.Satisfied", () => met = requirement.IsSatisfied);

                string mark = met ? "MET" : (requirement is ModIncompatibility ? "CLASH" : "MISSING");

                Color color = met ? palette.Success : palette.Danger;

                TabParts.RowLabel(new Rect(inner.x, y, 52f, 16f), mark, color, GameFont.Tiny,
                    ModsFaces.Mono, ModsFaces.Size.DetailLabel);

                string name = requirement.displayName.NullOrEmpty()
                    ? requirement.packageId
                    : requirement.displayName;

                TabParts.RowLabel(new Rect(inner.x + 54f, y, inner.width - 54f, 16f), name,
                    palette.TextSecondary, GameFont.Tiny, ModsFaces.Body, ModsFaces.Size.DetailBody);

                y += 17f;
            }

            return y + 8f;
        }

        // -------------------------------------------------------------------------------------------
        // Bottom bar
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The row a page carries and a colony tab does not.
        ///
        /// <b>Save says restart, because that is what it does.</b> Vanilla's own <c>PostClose</c> restarts the
        /// game when the active list changed while the page was open, and a button that said only Save would be
        /// understating what pressing it costs.
        /// </summary>
        private static void Bar(Rect rect, Page_ModsConfig page, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(9f);

            Rect box = new Rect(inner.x, inner.y, 240f, inner.height);

            if (Search.Draw(box, palette))
                listScroll = Vector2.zero;

            float saveWidth = TabParts.ButtonWidth("Save and restart");
            float discardWidth = TabParts.ButtonWidth("Discard changes");

            Rect save = new Rect(inner.xMax - saveWidth, inner.y, saveWidth, inner.height);

            if (TabParts.Button(save, "Save and restart", palette, true, true))
            {
                ModsReflection.MarkSaving(page);
                page.Close();
            }

            Rect discard = new Rect(save.x - discardWidth - Pad, inner.y, discardWidth, inner.height);

            if (TabParts.Button(discard, "Discard changes", palette))
            {
                ModsReflection.MarkDiscarding(page);
                page.Close();
            }
        }
    }
}
