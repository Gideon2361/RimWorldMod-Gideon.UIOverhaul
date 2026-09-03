using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Mechs
{
    /// <summary>How the deck is arranged. A one-of choice, so it is a segment rather than a chip.</summary>
    internal enum MechView
    {
        Group,
        Work,
        Kind,
        Flat
    }

    /// <summary>
    /// The mech tab: the mechanitor tree, a deck of control groups, and one mech's detail.
    ///
    /// <b>The shape is the argument.</b> RimWorld draws this system as a ten column <c>PawnTable</c>, two of
    /// whose columns draw nothing at all: <c>GapTiny</c> is a spacer and
    /// <c>PawnColumnWorker_RemainingSpace</c> has an empty <c>DoCell</c> with a <c>GetOptimalWidth</c> of a
    /// million, there to swallow leftover width. Three more repeat one fact per row, because overseer,
    /// control group and work mode all belong to the group. What is underneath is a tree, and this draws it
    /// as one.
    ///
    /// <b>Bandwidth is on the screen.</b> It is the only scarce resource in the whole system and vanilla
    /// keeps it on a gizmo that is visible only while the mechanitor is selected, so the tab that exists to
    /// manage mechs could not tell you whether you could afford another one.
    ///
    /// <b>Presentation only, with one exception that is stated as one.</b> Selecting goes through
    /// <c>Find.Selector</c>, work modes go through <c>MechanitorControlGroupGizmo.GetWorkModeOptions</c>,
    /// recharge opens vanilla's <c>Dialog_RechargeSettings</c>, priorities go through
    /// <c>Pawn_WorkSettings.SetPriority</c>. The exception is mech hibernation, which is a behaviour change,
    /// is off by default and lives behind the Settings button. See <see cref="Dialog_MechSettings"/>.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class MechsPanel
    {
        private const float Pad = 6f;

        private const float Gap = 6f;

        private const float HeaderHeight = 62f;

        private const float ControlHeight = 26f;

        private const float GlyphSize = 30f;

        private const float GlyphGap = 10f;

        private const float ChipGap = 4f;

        private const float DetailWidth = 288f;

        private const float MinRailWidth = 168f;

        private const float MaxRailWidth = 248f;

        /// <summary>Below this the deck is not worth having, so the rail goes rather than the cards.</summary>
        private const float MinDeckWidth = 400f;

        private const float RowHeight = 40f;

        private const float PortraitSize = 28f;

        private const float CardHeadHeight = 30f;

        private const float ColumnHeadHeight = 16f;

        /// <summary>The name column, which carries the mech's name over its work priority chips.</summary>
        private const float NameWidth = 210f;

        private const float CostWidth = 46f;

        private const float PercentWidth = 36f;

        private const float IntegrityWidth = 54f;

        private const float ToggleWidth = 20f;

        private const float PriorityBox = 24f;

        internal static float WindowWidth
        {
            get { return Mathf.Min(1240f, UI.screenWidth - 80f); }
        }

        internal static float WindowHeight
        {
            get { return Mathf.Min(760f, UI.screenHeight - 120f); }
        }

        // -------------------------------------------------------------------------------------------
        // State
        // -------------------------------------------------------------------------------------------

        private static Pawn selected;

        /// <summary>The rail row the player is standing on: a mechanitor, a group, or the unlinked section.</summary>
        private static string railKey;

        private static MechView view = MechView.Group;

        private static Vector2 railScroll;
        private static bool railDragging;
        private static float railDragOffset;

        private static Vector2 deckScroll;
        private static Vector2 detailScroll;

        /// <summary>Work modes whose mechs are hidden. Empty means everything shows.</summary>
        private static readonly HashSet<string> hiddenModes = new HashSet<string>();

        /// <summary>
        /// The three narrowing filters. Off means no narrowing at all, which is why they are not the same
        /// kind of control as the work mode chips beside them and why they carry a color bar and those do not.
        /// </summary>
        private static bool onlyLowCharge;
        private static bool onlyDamaged;
        private static bool onlyDrafted;

        /// <summary>Whether control groups with no mechs are listed. Off: most of them are empty.</summary>
        private static bool showEmptyGroups;

        private static readonly List<string> damagedParts = new List<string>();

        private static readonly List<Pawn> shown = new List<Pawn>();

        // -------------------------------------------------------------------------------------------
        // Frame
        // -------------------------------------------------------------------------------------------

        internal static void Draw(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            MechRoster.Build();

            // Dropped rather than held. A mech that died, was shredded or left the map is no longer anything
            // the detail pane can describe, and a held reference would keep describing it.
            if (selected != null && (selected.Dead || !selected.Spawned))
                selected = null;

            Rect content = inRect.ContractedBy(Pad);

            Rect header = new Rect(content.x, content.y, content.width, HeaderHeight);

            Header(header, palette);

            Rect strip = new Rect(content.x, header.yMax + Gap, content.width, ControlHeight);

            Toolbar(strip, palette);

            float top = strip.yMax + Gap;
            float height = content.yMax - top;

            float right = content.xMax;
            bool showDetail = selected != null;

            Rect detail = new Rect(right - DetailWidth, top, DetailWidth, height);

            if (showDetail)
                right -= DetailWidth + Gap;

            float left = content.x;
            float rail = RailWidth();
            bool showRail = rail < (right - content.x) - MinDeckWidth;

            if (showRail)
                left += rail + Gap;

            Deck(new Rect(left, top, right - left, height), palette);

            if (showRail)
                Rail(new Rect(content.x, top, rail, height), palette);

            if (showDetail)
                Detail(detail, palette);

            // A click on a portrait asks for a jump and the request outlives the frame; resolving it here is
            // what closes the tab and moves the camera.
            PawnCameraJump.Resolve();
        }

        // -------------------------------------------------------------------------------------------
        // Header
        // -------------------------------------------------------------------------------------------

        private static readonly Texture2D Glyph;

        static MechsPanel()
        {
            Texture2D glyph = null;

            UIGuard.Try("Mechs.Glyph",
                () => glyph = ContentFinder<Texture2D>.Get("UI/MainButtonIcons/Mechanoids", false),
                "The header has no glyph this session. Everything on the tab still reads.");

            Glyph = glyph;
        }

        /// <summary>
        /// The block that names the screen, with the colony's mech figures seated in it.
        ///
        /// <b>Bandwidth is the first figure and the only one with a bar under it.</b> Everything else on this
        /// screen is downstream of it: how many mechs there are, how many groups, whether the gestator can
        /// start. It is the sum across every mechanitor, which is the sum vanilla never shows because its
        /// gizmo is per person.
        /// </summary>
        private static void Header(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(10f);

            float text = inner.x;

            if (Glyph != null)
            {
                Rect mark = new Rect(inner.x, inner.y + (inner.height - GlyphSize) * 0.5f, GlyphSize, GlyphSize);

                Color previous = GUI.color;

                GUI.color = MechsFaces.AccentOf(palette);
                GUI.DrawTexture(mark, Glyph);
                GUI.color = previous;

                text = mark.xMax + GlyphGap;
            }

            float wall = Settings(inner, palette) - 12f;

            wall = Readouts(new Rect(inner.x, inner.y, wall - inner.x, inner.height), palette) - 12f;

            float titleWidth = Mathf.Max(0f, Mathf.Min(300f, wall - text));
            float subtitleWidth = Mathf.Max(0f, Mathf.Min(520f, wall - text));

            TabParts.RowLabel(new Rect(text, inner.y, titleWidth, 24f), "Mechs",
                MechsFaces.AccentOf(palette), MechsFaces.Display, MechsFaces.Size.Title);

            TabParts.RowLabel(new Rect(text, inner.y + 23f, subtitleWidth, 18f), Subtitle(),
                palette.TextSecondary, MechsFaces.Condensed, MechsFaces.Size.Subtitle);
        }

        private static string Subtitle()
        {
            return UIGuard.Try("Mechs.Subtitle", () =>
            {
                string line = MechRoster.MechCount + (MechRoster.MechCount == 1 ? " mech" : " mechs")
                              + "  -  " + MechRoster.Mechanitors.Count
                              + (MechRoster.Mechanitors.Count == 1 ? " mechanitor" : " mechanitors")
                              + "  -  " + MechRoster.GroupCount
                              + (MechRoster.GroupCount == 1 ? " group" : " groups");

                if (MechRoster.Gestating.Count > 0)
                    line += "  -  " + MechRoster.Gestating.Count + " gestating";

                if (MechRoster.HibernatingCount > 0)
                    line += "  -  " + MechRoster.HibernatingCount + " hibernating";

                return line;
            }, "The colony's mechs", null);
        }

        /// <summary>
        /// The Settings button, at the right hand end. Returns its left edge.
        ///
        /// <b>A labeled word rather than a bare icon.</b> An icon earns its place when what it stands for is
        /// already familiar, and a settings dialog nobody has opened is not that. The first draft of this
        /// header carried an unlabeled gear here and it was findable in the source and not on the screen.
        /// </summary>
        private static float Settings(Rect inner, UIColorPaletteDef palette)
        {
            float width = TabParts.ButtonWidth("Settings", 22f);
            Rect rect = new Rect(inner.xMax - width, inner.y + (inner.height - ControlHeight) * 0.5f,
                width, ControlHeight);

            if (TabParts.Button(rect, "Settings", palette))
            {
                Find.WindowStack.Add(new Dialog_MechSettings());

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            TooltipHandler.TipRegion(rect,
                (TipSignal) "This colony's mech color, and whether idle mechs hibernate.");

            return rect.x;
        }

        /// <summary>The figures, right to left. Returns the left edge of the leftmost one.</summary>
        private static float Readouts(Rect area, UIColorPaletteDef palette)
        {
            float x = area.xMax;

            x = Readout(area, x, "charging", MechRoster.ChargingCount.ToString(), palette,
                "Mechs gaining charge right now, at a recharger or self charging.",
                MechRoster.ChargingCount > 0 ? palette.Info : (Color?) null);

            x = Readout(area, x, "damaged", MechRoster.DamagedCount.ToString(), palette,
                "Mechs carrying damage. Repairs are done by the overseer, and cost them time and a little "
                + "of the mech's own energy.",
                MechRoster.DamagedCount > 0 ? palette.Warning : (Color?) null);

            x = Readout(area, x, "charge", MechRoster.MeanCharge + "%", palette,
                "Mean charge across every mech. It says whether the rechargers are keeping up; it does not "
                + "say which mech is about to drop, which is what the low charge filter is for.");

            x = Readout(area, x, "bandwidth", MechRoster.UsedBandwidth + " / " + MechRoster.TotalBandwidth,
                palette, "Bandwidth spent, out of what every mechanitor between them provides. Gestating "
                         + "mechs are counted: they spend it before they exist.",
                MechRoster.UsedBandwidth > MechRoster.TotalBandwidth ? palette.Danger : (Color?) null);

            // The one bar in the header, under the one figure everything else follows from.
            if (MechRoster.TotalBandwidth > 0)
            {
                float fraction = Mathf.Clamp01((float) MechRoster.UsedBandwidth / MechRoster.TotalBandwidth);
                Rect trough = new Rect(x + 10f, area.yMax - 5f, area.xMax - x - 20f, 3f);

                if (trough.width > 8f)
                {
                    Widgets.DrawBoxSolid(trough, palette.ControlBackgroundFaded);
                    Widgets.DrawBoxSolid(new Rect(trough.x, trough.y, trough.width * fraction, trough.height),
                        MechsFaces.AccentOf(palette));
                }
            }

            return x;
        }

        /// <summary>One right-aligned caption over a figure, in the mono, returning the x it ends at.</summary>
        private static float Readout(Rect bar, float right, string caption, string value,
            UIColorPaletteDef palette, string tip = null, Color? valueColor = null)
        {
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;
            bool wrap = Text.WordWrap;

            try
            {
                Text.WordWrap = false;

                float width = Mathf.Max(
                        UITextControl.Width(caption ?? string.Empty, MechsFaces.Mono, MechsFaces.Size.Caption),
                        UITextControl.Width(value ?? string.Empty, MechsFaces.Mono, MechsFaces.Size.Readout))
                    + 20f;

                Rect cell = new Rect(right - width, bar.y, width, bar.height);
                float valueHeight = UITextControl.LineHeight(MechsFaces.Mono, MechsFaces.Size.Readout);

                Text.Anchor = TextAnchor.LowerRight;
                GUI.color = valueColor ?? palette.TextPrimary;

                UITextControl.Label(new Rect(cell.x, cell.y, cell.width - 6f, valueHeight + 2f), value,
                    MechsFaces.Mono, MechsFaces.Size.Readout);

                Text.Anchor = TextAnchor.UpperRight;
                GUI.color = palette.TextDisabled;

                UITextControl.Label(new Rect(cell.x, cell.y + valueHeight + 3f, cell.width - 6f, 14f),
                    caption.ToUpperInvariant(), MechsFaces.Mono, MechsFaces.Size.Caption);

                if (!tip.NullOrEmpty())
                    TooltipHandler.TipRegion(cell, (TipSignal) tip);

                return cell.x;
            }
            finally
            {
                Text.WordWrap = wrap;
                GUI.color = color;
                Text.Anchor = anchor;
            }
        }

        // -------------------------------------------------------------------------------------------
        // Filter strip
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Two runs of chips and the view segments.
        ///
        /// <b>The work mode chips carry no color bar and the state chips do.</b> That is the whole visual
        /// difference between the two kinds of control here: a work mode is a glyph on this tab and never a
        /// hue, so a colored bar would be inventing a code the rest of the screen does not use, while danger,
        /// warning and accent already mean those three states everywhere else in the mod.
        /// <c>TabParts.FilterChip</c> takes a nullable color for exactly this.
        /// </summary>
        private static void Toolbar(Rect bar, UIColorPaletteDef palette)
        {
            float x = bar.x;

            List<MechWorkModeDef> all = MechModes.All();

            for (int i = 0; i < all.Count; i++)
            {
                MechWorkModeDef mode = all[i];
                MechWorkModeDef chosen = mode;
                string label = mode.LabelCap;

                x = Chip(x, bar, label, MechRoster.CountFor(mode), !hiddenModes.Contains(mode.defName), null,
                    palette, () =>
                    {
                        if (!hiddenModes.Remove(chosen.defName))
                            hiddenModes.Add(chosen.defName);
                    }, mode.description);
            }

            x += 8f;

            x = Chip(x, bar, "Low charge", MechRoster.LowChargeCount, onlyLowCharge, palette.Danger, palette,
                () => onlyLowCharge = !onlyLowCharge,
                "Show only mechs at or under the " + Mathf.RoundToInt(MechFacts.ShutdownAt)
                + " percent they shut down at.");

            x = Chip(x, bar, "Damaged", MechRoster.DamagedCount, onlyDamaged, palette.Warning, palette,
                () => onlyDamaged = !onlyDamaged, "Show only mechs carrying damage.");

            Chip(x, bar, "Drafted", MechRoster.DraftedCount, onlyDrafted, palette.Accent, palette,
                () => onlyDrafted = !onlyDrafted, "Show only mechs you have taken manual control of.");

            x += 8f;

            // From the right, so the segments grow leftwards into whatever room is left rather than
            // colliding with the chips.
            float right = bar.xMax;

            right = Segment(right, bar, "Flat", view == MechView.Flat, palette, () => view = MechView.Flat,
                "Every mech in one list, the way RimWorld's own table shows them.");

            right = Segment(right, bar, "By kind", view == MechView.Kind, palette, () => view = MechView.Kind,
                "Grouped by what each mech is, which is how you answer whether you have enough lifters.");

            right = Segment(right, bar, "By work", view == MechView.Work, palette, () => view = MechView.Work,
                "Grouped by the work each mech is assigned to. A mech with two work types appears twice.");

            right = Segment(right, bar, "By group", view == MechView.Group, palette,
                () => view = MechView.Group, "The mechanitor tree: one card per control group.");

            // Last, and only where there is room left between the chips and the segments.
            //
            // <b>Empty groups are the default state of this screen, which is why they are hidden.</b> The
            // base mechlink grants two control groups and RimWorld creates both the moment a mechanitor
            // exists, so three mechanitors with one mech between them show six rows of which five say
            // nothing. Hiding them is what makes the rail a list of things rather than a list of slots.
            //
            // <b>The chip stays visible when it is off,</b> because a control that vanishes when it is not
            // in use is a control nobody finds. It carries the count, so the rail is honest about what it
            // is not showing.
            if (MechRoster.EmptyGroupCount > 0)
            {
                string figure = MechRoster.EmptyGroupCount.ToString();
                float width = TabParts.FilterChipWidth("Empty groups", figure, MechsFaces.Condensed,
                    MechsFaces.Size.Chip, MechsFaces.Mono, MechsFaces.Size.RailCount);

                // Stops rather than clipping. A chip drawn under a segment is a control that silently does
                // the wrong thing when clicked.
                if (right - width - ChipGap > x)
                {
                    if (TabParts.FilterChip(new Rect(right - width - ChipGap * 3f, bar.y, width, bar.height),
                            "Empty groups", figure, showEmptyGroups, null, palette, MechsFaces.Condensed,
                            MechsFaces.Size.Chip, MechsFaces.Mono, MechsFaces.Size.RailCount,
                            "Show control groups with no mechs in them.\n\nEvery mechanitor is given two "
                            + "groups the moment they get a mechlink, so most colonies have several that "
                            + "have never held anything. They are still the targets for Move to group."))
                    {
                        showEmptyGroups = !showEmptyGroups;

                        SoundDefOf.Click.PlayOneShotOnCamera();
                    }
                }
            }
        }

        /// <summary>
        /// Whether a group with nothing in it is drawn, in the rail and in the deck.
        ///
        /// The group the rail is standing on is always drawn whatever this says: a player who selected an
        /// empty group meant to look at it, and having it disappear underneath them would be the control
        /// fighting them.
        /// </summary>
        internal static bool ShowsEmpty(MechGroupEntry group)
        {
            return showEmptyGroups || group.Mechs.Count > 0 || railKey == group.Key;
        }

        private static float Chip(float x, Rect bar, string label, int count, bool on, Color? color,
            UIColorPaletteDef palette, System.Action toggled, string tip)
        {
            string figure = count.ToString();
            float width = TabParts.FilterChipWidth(label, figure, MechsFaces.Condensed, MechsFaces.Size.Chip,
                MechsFaces.Mono, MechsFaces.Size.RailCount);

            if (TabParts.FilterChip(new Rect(x, bar.y, width, bar.height), label, figure, on, color, palette,
                    MechsFaces.Condensed, MechsFaces.Size.Chip, MechsFaces.Mono, MechsFaces.Size.RailCount,
                    tip))
            {
                toggled();

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            return x + width + ChipGap;
        }

        /// <summary>One segment of a one-of choice, underlined in the tab's color, laid out right to left.</summary>
        private static float Segment(float right, Rect bar, string label, bool on, UIColorPaletteDef palette,
            System.Action chosen, string tip)
        {
            float width = UITextControl.Width(label, MechsFaces.Condensed, MechsFaces.Size.Chip) + 18f;
            Rect rect = new Rect(right - width, bar.y, width, bar.height);

            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;
            bool wrap = Text.WordWrap;

            try
            {
                bool over = Mouse.IsOver(rect);

                Text.WordWrap = false;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = on ? palette.TextPrimary : over ? palette.TextSecondary : palette.TextDisabled;

                UITextControl.Label(new Rect(rect.x, rect.y - 2f, rect.width, rect.height), label,
                    MechsFaces.Condensed, MechsFaces.Size.Chip);

                if (on)
                {
                    Widgets.DrawBoxSolid(new Rect(rect.x + 3f, rect.yMax - 2f, rect.width - 6f, 2f),
                        MechsFaces.AccentOf(palette));
                }
            }
            finally
            {
                Text.WordWrap = wrap;
                GUI.color = color;
                Text.Anchor = anchor;
            }

            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, (TipSignal) tip);

            if (Widgets.ButtonInvisible(rect) && !on)
            {
                chosen();

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            return rect.x;
        }

        // -------------------------------------------------------------------------------------------
        // The rail
        // -------------------------------------------------------------------------------------------

        private static float RailWidth()
        {
            float widest = 0f;

            for (int i = 0; i < MechRoster.Mechanitors.Count; i++)
            {
                MechanitorEntry entry = MechRoster.Mechanitors[i];

                float name = UITextControl.Width(entry.Pawn == null ? string.Empty : entry.Pawn.LabelShortCap,
                    MechsFaces.Condensed, MechsFaces.Size.RailName);

                float band = UITextControl.Width(entry.Used + "/" + entry.Total, MechsFaces.Mono,
                    MechsFaces.Size.RailCount);

                widest = Mathf.Max(widest, name + band);

                for (int g = 0; g < entry.Groups.Count; g++)
                {
                    MechGroupEntry group = entry.Groups[g];

                    float label = UITextControl.Width(GroupLabel(group), MechsFaces.Condensed,
                        MechsFaces.Size.RailName);

                    widest = Mathf.Max(widest, label + 22f);
                }
            }

            // 6 for the selection bar's reserved lane, 12 for the row's indent, 6 between label and tally,
            // 4 for the tally's right margin, 18 for the scrollbar, 2 for the rail's border.
            return Mathf.Clamp(widest + 48f, MinRailWidth, MaxRailWidth);
        }

        /// <summary>
        /// A group's name in the rail.
        ///
        /// <b>The word "Group" is carried on every row rather than left implied.</b> Without it the label is
        /// a bare index and a mode, and a mechanitor with two empty groups reads as "1 work" over "2 work",
        /// which looks like the same row printed twice rather than like two groups that both happen to be
        /// set to work. Every mechanitor starts with two, because the base mechlink grants
        /// <c>MechControlGroups 2</c>, so that is the first thing anybody sees on this tab.
        /// </summary>
        private static string GroupLabel(MechGroupEntry group)
        {
            string mode = group.Mode == null ? "no mode" : group.Mode.LabelCap.ToString().ToLowerInvariant();

            return "Group " + group.Index + "  -  " + mode;
        }

        /// <summary>
        /// Mechanitors as section headers, their groups as entries, and the unlinked at the bottom.
        ///
        /// <b>Empty groups stay in the list.</b> A mechanitor with four groups and two mechs has two empty
        /// ones, and they are targets: moving something into one is the whole reason to look at this rail.
        /// Vanilla's table has no way to show a group that contains nothing.
        /// </summary>
        private static void Rail(Rect rail, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rail, palette.Border, palette.SurfaceSunken);

            Rect body = rail.ContractedBy(1f);

            List<UIRailElement> elements = new List<UIRailElement>();

            elements.Add(new UIRailSectionHeaderControl("Mechanitors")
            {
                Uppercase = true,
                Face = MechsFaces.Mono,
                Points = MechsFaces.Size.Caption,
                Trailing = MechRoster.Mechanitors.Count.ToString()
            });

            for (int i = 0; i < MechRoster.Mechanitors.Count; i++)
            {
                MechanitorEntry entry = MechRoster.Mechanitors[i];
                MechanitorEntry captured = entry;

                elements.Add(new UIRailClickableEntry(entry.Key, entry.Pawn.LabelShortCap)
                {
                    Face = MechsFaces.Condensed,
                    Points = MechsFaces.Size.RailName,
                    CountFace = MechsFaces.Mono,
                    CountPoints = MechsFaces.Size.RailCount,
                    Trailing = entry.Used + "/" + entry.Total,
                    CountColor = entry.Used > entry.Total ? palette.Danger : palette.TextSecondary,
                    SelectionBar = MechsFaces.AccentOf(palette),
                    Rise = 30f,
                    Tooltip = entry.Pawn.LabelCap + "\n\nBandwidth " + entry.Used + " of " + entry.Total
                              + (entry.FromGestation > 0
                                  ? ", of which " + entry.FromGestation + " is reserved by a gestation."
                                  : "."),

                    // The split meter, drawn under the name: solid for bandwidth spent on mechs that exist,
                    // info for bandwidth a gestation has reserved. The tracker keeps those apart already and
                    // the distinction matters, because one frees when you shred something and the other frees
                    // when the gestator finishes.
                    Decorate = rect => Meter(rect, captured, palette)
                });

                for (int g = 0; g < entry.Groups.Count; g++)
                {
                    MechGroupEntry group = entry.Groups[g];

                    if (!ShowsEmpty(group))
                        continue;

                    elements.Add(new UIRailClickableEntry(group.Key, GroupLabel(group))
                    {
                        Face = MechsFaces.Condensed,
                        Points = MechsFaces.Size.RailName,
                        CountFace = MechsFaces.Mono,
                        CountPoints = MechsFaces.Size.RailCount,
                        Count = group.Mechs.Count,
                        LeadPad = 16f,
                        SelectionBar = MechsFaces.AccentOf(palette),
                        TextColor = group.Mechs.Count == 0 ? palette.TextDisabled : (Color?) null,
                        Tooltip = group.Mode == null
                            ? null
                            : group.Mode.LabelCap + "\n\n" + group.Mode.description
                    });
                }
            }

            if (MechRoster.Unlinked.Count > 0)
            {
                elements.Add(new UIRailSectionHeaderControl("Unlinked")
                {
                    Uppercase = true,
                    Face = MechsFaces.Mono,
                    Points = MechsFaces.Size.Caption,
                    Color = palette.Danger,
                    Trailing = MechRoster.Unlinked.Count.ToString()
                });

                elements.Add(new UIRailClickableEntry("unlinked", "No overseer")
                {
                    Face = MechsFaces.Condensed,
                    Points = MechsFaces.Size.RailName,
                    CountFace = MechsFaces.Mono,
                    CountPoints = MechsFaces.Size.RailCount,
                    Count = MechRoster.Unlinked.Count,
                    SelectionBar = MechsFaces.AccentOf(palette),
                    TextColor = palette.Danger,
                    Tooltip = "Mechs of yours that answer to nobody, because their mechanitor died or lost "
                              + "their mechlink. RimWorld's own tab filters these out, which is why you have "
                              + "not seen them before."
                });
            }

            string picked = UIRailControl.Draw(body, elements, railKey, ref railScroll, ref railDragging,
                ref railDragOffset, palette, false);

            if (!picked.NullOrEmpty())
            {
                railKey = picked == railKey ? null : picked;

                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        /// <summary>The two part bandwidth meter under a mechanitor's name.</summary>
        private static void Meter(Rect rect, MechanitorEntry entry, UIColorPaletteDef palette)
        {
            if (entry.Total <= 0)
                return;

            Rect trough = new Rect(rect.x + 12f, rect.yMax - 7f, rect.width - 24f, 3f);

            if (trough.width <= 4f)
                return;

            Widgets.DrawBoxSolid(trough, palette.ControlBackgroundFaded);

            float spent = Mathf.Clamp01((float) (entry.Used - entry.FromGestation) / entry.Total);
            float gestating = Mathf.Clamp01((float) entry.FromGestation / entry.Total);

            Widgets.DrawBoxSolid(new Rect(trough.x, trough.y, trough.width * spent, trough.height),
                MechsFaces.AccentOf(palette));

            if (gestating > 0f)
            {
                Widgets.DrawBoxSolid(new Rect(trough.x + trough.width * spent, trough.y,
                    trough.width * gestating, trough.height), palette.Info);
            }
        }

        // -------------------------------------------------------------------------------------------
        // The deck
        // -------------------------------------------------------------------------------------------


        private static void Deck(Rect rect, UIColorPaletteDef palette)
        {
            MechDeck.Draw(rect, ref deckScroll, palette);
        }

        private static void Detail(Rect rect, UIColorPaletteDef palette)
        {
            MechDetailPane.Draw(rect, selected, ref detailScroll, palette);
        }

        // -------------------------------------------------------------------------------------------
        // What the deck and the detail pane need from here
        // -------------------------------------------------------------------------------------------

        internal static MechView View
        {
            get { return view; }
        }

        internal static Pawn Selected
        {
            get { return selected; }
        }

        internal static void Select(Pawn mech)
        {
            selected = selected == mech ? null : mech;

            detailScroll = Vector2.zero;
        }

        /// <summary>Whether the rail is standing on the unlinked section rather than on a mechanitor.</summary>
        internal static bool OnUnlinked
        {
            get { return railKey == UnlinkedKey; }
        }

        internal const string UnlinkedKey = "unlinked";

        internal static float Rows
        {
            get { return RowHeight; }
        }

        internal static float Portrait
        {
            get { return PortraitSize; }
        }

        internal static float CardHead
        {
            get { return CardHeadHeight; }
        }

        internal static float ColumnHead
        {
            get { return ColumnHeadHeight; }
        }

        internal static float Name
        {
            get { return NameWidth; }
        }

        internal static float Cost
        {
            get { return CostWidth; }
        }

        internal static float Percent
        {
            get { return PercentWidth; }
        }

        internal static float Integrity
        {
            get { return IntegrityWidth; }
        }

        internal static float Toggle
        {
            get { return ToggleWidth; }
        }

        internal static float PriorityBoxSize
        {
            get { return PriorityBox; }
        }

        internal static float Spacing
        {
            get { return Gap; }
        }

        internal static List<string> DamagedScratch
        {
            get { return damagedParts; }
        }

        internal static List<Pawn> Scratch
        {
            get { return shown; }
        }

        /// <summary>Whether a mech survives the strip's filters.</summary>
        internal static bool Passes(Pawn mech)
        {
            if (mech == null)
                return false;

            MechWorkModeDef mode = mech.GetMechWorkMode();

            if (mode != null && hiddenModes.Contains(mode.defName))
                return false;

            // The three state chips narrow rather than reveal, and they are an "any of" set: with low charge
            // and damaged both held down, a mech that is either shows. Nothing held down narrows nothing.
            if (!onlyLowCharge && !onlyDamaged && !onlyDrafted)
                return true;

            if (onlyLowCharge)
            {
                float charge = MechFacts.Charge(mech);

                if (charge >= 0f && charge * 100f <= MechFacts.ShutdownAt)
                    return true;
            }

            if (onlyDamaged && MechFacts.Integrity(mech) < 0.999f)
                return true;

            if (onlyDrafted && mech.Drafted)
                return true;

            return false;
        }

        /// <summary>Whether the rail's current selection lets this group through.</summary>
        internal static bool InRail(MechanitorEntry owner, MechGroupEntry group)
        {
            if (railKey.NullOrEmpty())
                return true;

            if (railKey == UnlinkedKey)
                return false;

            return railKey == owner.Key || (group != null && railKey == group.Key);
        }
    }
}
