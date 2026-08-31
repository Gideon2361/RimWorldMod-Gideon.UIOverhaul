using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// One gene a picker is offering, and what to do when it is taken.
    ///
    /// No equivalent of <see cref="EditorOption.Marked"/>, because neither caller has anything to mark: both
    /// already exclude the genes the pawn has, and nothing else about a gene makes it a bad choice that the
    /// tile's own description does not say. <c>GeneUIUtility.DrawGeneDef</c> takes an extra tooltip for exactly
    /// that purpose if one is ever needed -- see the call in <see cref="Dialog_PickGene"/>.
    /// </summary>
    internal sealed class GeneChoice
    {
        internal GeneDef Def;

        internal Action Chosen;
    }

    /// <summary>
    /// The gene picker: a grid of the game's own gene tiles.
    ///
    /// <b>Genes are recognised by sight, and a list throws that away.</b> Aaron made this point on 2026-08-23
    /// about the Genes panel, against a screenshot of fourteen rows each reading "endogene", and again the same
    /// day about this picker -- a column of identical grey rows where every entry has an icon the player already
    /// knows from the gene assembler. <see cref="Dialog_PickFrom"/> is right for backstories and traits, which
    /// are words; it is wrong for the two lists whose entries are pictures.
    ///
    /// <b>Through <c>GeneUIUtility.DrawGeneDef</c>, the same call the gene assembler makes.</b> So a gene looks
    /// here exactly as it does in the two screens a player already knows it from, including the backgrounds that
    /// separate an endogene from a xenogene and the biostats along the bottom. The tooltip -- label, full
    /// description, and the warning line if there is one -- comes with it.
    ///
    /// <b>Drawn unclickable and clicked by us,</b> for the reason <see cref="EditorGeneTiles"/> records: vanilla's
    /// tile takes the whole rect for its own info card, and here the whole rect has to mean "take this one".
    /// </summary>
    internal sealed class Dialog_PickGene : Window
    {
        private const float HeaderHeight = 28f;

        private const float FooterHeight = 34f;

        private const float Pad = 8f;

        private const float Gap = 6f;

        /// <summary>
        /// Tiles across the grid at the opening width.
        ///
        /// <b>Ten, where this used to be four.</b> Asked for as "about three times as wide" on 2026-08-24, and
        /// the reason it was four is gone: four was chosen when the window was a search box over a flat grid of
        /// several hundred tiles, where a narrow window at least kept the scroll bar's travel honest. With the
        /// categories down the side there is a real page to fill, and a gene is recognised by sight -- so the
        /// thing worth spending width on is how many of them are in front of you at once.
        /// </summary>
        private const int Columns = 10;

        /// <summary>How wide the category rail is. Enough for "resistance and sensitivity" at Small.</summary>
        private const float RailWidth = 196f;

        private const float RailRowHeight = 24f;

        private const float ScrollBar = 18f;

        /// <summary>Vanilla's own tile, so the grid lines up with the gene assembler's.</summary>
        private static readonly Vector2 TileSize = GeneCreationDialogBase.GeneSize;

        private readonly string heading;

        private readonly List<GeneChoice> choices;

        private readonly List<GeneChoice> matching = new List<GeneChoice>();

        /// <summary>Every category present in this picker's own choices, in the game's display order.</summary>
        private readonly List<GeneCategoryDef> categories = new List<GeneCategoryDef>();

        /// <summary>How many of the choices fall in each category, before the search narrows anything.</summary>
        private readonly Dictionary<GeneCategoryDef, int> tallies = new Dictionary<GeneCategoryDef, int>();

        /// <summary>The category the rail has selected, or null for all of them.</summary>
        private GeneCategoryDef only;

        private bool sorted;

        private readonly UITextBoxControl search = new UITextBoxControl
        {
            Icon = TexButton.Search,
            MaxLength = 40
        };

        private Vector2 scroll;

        private Vector2 railScroll;
        private bool railDragging;
        private float railDragOffset;

        private Dialog_PickGene(string heading, List<GeneChoice> choices, string placeholder)
        {
            this.heading = heading;
            this.choices = choices;

            search.Placeholder = placeholder ?? "Search";

            doCloseX = false;
            forcePause = false;
            absorbInputAroundWindow = true;

            // Both changed together, and neither is optional once the other moves. A window you can drag has to
            // stop closing when the click lands outside it, or the first drag that overshoots the edge dismisses
            // it -- and a window this wide is dragged for a reason: to see the pawn underneath while choosing.
            closeOnClickedOutside = false;
            draggable = true;

            drawShadow = true;
        }

        internal static void Open(string heading, List<GeneChoice> choices, string placeholder = null)
        {
            if (choices == null || choices.Count == 0)
            {
                EditorParts.Warn("There is nothing to choose from here.");

                return;
            }

            Find.WindowStack.Add(new Dialog_PickGene(heading, choices, placeholder));
        }

        /// <summary>
        /// The rail, ten tiles, and whatever the screen will actually allow.
        ///
        /// The clamp is not decoration: ten tiles plus the rail is over eleven hundred pixels, which is most of a
        /// 1280-wide screen and more than all of a smaller one. <see cref="GridColumns"/> divides whatever
        /// survives back into tiles, so a narrow screen gets fewer columns rather than a window off the edge.
        /// </summary>
        public override Vector2 InitialSize
        {
            get
            {
                float wanted = RailWidth + Pad + Columns * (TileSize.x + Gap) - Gap + ScrollBar + Pad * 2f;

                return new Vector2(Mathf.Min(wanted, UI.screenWidth - 40f),
                    Mathf.Min(620f, UI.screenHeight - 40f));
            }
        }

        /// <summary>At the cursor and clamped on screen, exactly as the row picker is and for the same reason.</summary>
        protected override void SetInitialSizeAndPosition()
        {
            Vector2 size = InitialSize;
            Vector2 mouse = UI.MousePositionOnUIInverted;

            windowRect = new Rect(
                Mathf.Clamp(mouse.x, 0f, UI.screenWidth - size.x),
                Mathf.Clamp(mouse.y, 0f, UI.screenHeight - size.y),
                size.x, size.y);
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Editor.GenePicker", inRect, () => Contents(inRect),
                "The gene picker failed to draw. Nothing has been changed.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight), heading);

                Sort();

                float bodyTop = inRect.y + HeaderHeight;
                float bodyHeight = Mathf.Max(0f, inRect.height - HeaderHeight - FooterHeight - Pad);

                Rail(new Rect(inRect.x, bodyTop, RailWidth, bodyHeight), palette);

                float right = inRect.x + RailWidth + Pad;
                float width = Mathf.Max(TileSize.x + ScrollBar, inRect.xMax - right);

                Rect box = new Rect(right, bodyTop, width, 26f);

                search.Draw(box, palette);

                Gather();

                Rect grid = new Rect(right, box.yMax + Pad, width,
                    Mathf.Max(0f, bodyTop + bodyHeight - box.yMax - Pad));

                Grid(grid, palette);

                if (matching.Count == 0)
                {
                    GUI.color = palette.TextDisabled;
                    Text.Font = GameFont.Tiny;

                    Widgets.Label(new Rect(grid.x + 4f, grid.y + 4f, grid.width - 8f, 40f),
                        "Nothing here matches that.");

                    Text.Font = GameFont.Small;
                    GUI.color = palette.TextPrimary;
                }

                if (TabParts.Button(new Rect(inRect.xMax - 90f, inRect.yMax - FooterHeight, 90f, 28f), "Cancel",
                        palette))
                    Close();
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// The categories down the left, each with how many genes it holds.
        ///
        /// <b>The game's own grouping, not one invented here.</b> Every <c>GeneDef</c> carries a
        /// <c>displayCategory</c>, and <c>ResolveReferences</c> fills in Miscellaneous for any that did not name
        /// one -- so the list is complete by construction and a modded gene lands wherever its author put it
        /// rather than in a bucket this window guessed at. The order is
        /// <c>displayPriorityInXenotype</c>, which is the order the gene assembler shows the same categories in,
        /// so a player who has used that screen already knows where to look.
        ///
        /// <b>Only categories this picker actually has genes in.</b> The caller has already removed the genes the
        /// pawn has, so an empty category is one there is nothing left to add from, and a rail row that can only
        /// ever show "nothing matches" is a row worth not drawing.
        ///
        /// A filter rather than a jump. The research tab's rail scrolls to a heading because that canvas is one
        /// picture whose parts relate to each other; this is a catalogue, and "show me the mood genes" is the
        /// whole question somebody opens it with.
        /// </summary>
        /// <summary>
        /// All genes, then one row per category with how many are in it.
        ///
        /// <b>Clicking the row you are already on clears the filter,</b> so the rail is its own way back and
        /// "All genes" is a shortcut rather than the only route.
        /// </summary>
        private void Rail(Rect rail, UIColorPaletteDef palette)
        {
            List<UIRailElement> elements = new List<UIRailElement>();

            elements.Add(new UIRailClickableEntry(string.Empty, "All genes")
            {
                Count = choices.Count,
                Rise = RailRowHeight
            });

            for (int i = 0; i < categories.Count; i++)
            {
                GeneCategoryDef category = categories[i];

                int tally;
                tallies.TryGetValue(category, out tally);

                elements.Add(new UIRailClickableEntry(category.defName, category.LabelCap)
                {
                    Count = tally,
                    Rise = RailRowHeight
                });
            }

            string picked = UIRailControl.Draw(rail, elements, only == null ? string.Empty : only.defName,
                ref railScroll, ref railDragging, ref railDragOffset, palette, false);

            if (picked == null)
                return;

            GeneCategoryDef wanted = null;

            for (int i = 0; i < categories.Count; i++)
            {
                if (categories[i].defName == picked)
                {
                    wanted = categories[i];

                    break;
                }
            }

            only = wanted == null || only == wanted ? null : wanted;
            scroll = Vector2.zero;
        }

        /// <summary>
        /// How many tiles fit across the grid as it is now.
        ///
        /// Measured rather than assumed, so the clamp in <see cref="InitialSize"/> and a window dragged nowhere
        /// in particular both end up with a grid that fills its own width.
        /// </summary>
        private static int GridColumns(Rect grid)
        {
            return Mathf.Max(1, Mathf.FloorToInt((grid.width - ScrollBar + Gap) / (TileSize.x + Gap)));
        }

        private void Grid(Rect grid, UIColorPaletteDef palette)
        {
            int perRow = GridColumns(grid);
            int rows = Mathf.CeilToInt(matching.Count / (float) perRow);

            Rect view = new Rect(0f, 0f, grid.width - ScrollBar, rows * (TileSize.y + Gap));

            Widgets.BeginScrollView(grid, ref scroll, view);

            for (int i = 0; i < matching.Count; i++)
            {
                Rect tile = new Rect(
                    i % perRow * (TileSize.x + Gap),
                    i / perRow * (TileSize.y + Gap),
                    TileSize.x, TileSize.y);

                // Only what is on screen. A full gene list with every mod loaded runs to several hundred, and
                // each tile is an icon draw plus a tooltip region.
                if (tile.yMax >= scroll.y && tile.y <= scroll.y + grid.height)
                    Tile(tile, matching[i], palette);
            }

            Widgets.EndScrollView();
        }

        private void Tile(Rect rect, GeneChoice choice, UIColorPaletteDef palette)
        {
            // Xenogene, because that is what both callers add. The background a tile carries is the game's way of
            // saying which kind a gene is, so showing one kind and adding another would be a picture that lies.
            //
            // The null is the extra tooltip, which is where a per-gene warning would go if one were ever wanted.
            GeneUIUtility.DrawGeneDef(choice.Def, rect, GeneType.Xenogene, null, true, false);

            if (Mouse.IsOver(rect))
                Widgets.DrawHighlight(rect);

            MouseoverSounds.DoRegion(rect);

            if (!Widgets.ButtonInvisible(rect))
                return;

            SoundDefOf.Click.PlayOneShotOnCamera();

            // Closed before the gene is added, so the window is gone by the time anything it caused draws --
            // a warning about the change lands in front of the editor rather than behind this.
            Close();

            if (choice.Chosen != null)
                choice.Chosen();
        }

        private void Gather()
        {
            matching.Clear();

            for (int i = 0; i < choices.Count; i++)
            {
                GeneChoice choice = choices[i];

                if (choice == null || choice.Def == null)
                    continue;

                if (only != null && CategoryOf(choice.Def) != only)
                    continue;

                if (!search.IsEmpty && !search.Matches(choice.Def.LabelCap))
                    continue;

                matching.Add(choice);
            }
        }

        /// <summary>
        /// Puts the choices in category order once, and works out what the rail has to show.
        ///
        /// <b>Once, not per frame.</b> The list a caller hands over does not change while the window is open --
        /// it is built from the pawn's genes at the moment the window opens and the window closes the instant one
        /// is taken -- so sorting it and counting it is startup work that happens to be done on the first draw,
        /// which is the first moment the def database is certainly ready.
        ///
        /// Sorted in place. The caller built the list for this window and nothing else holds it.
        /// </summary>
        private void Sort()
        {
            if (sorted)
                return;

            sorted = true;

            choices.Sort((left, right) =>
            {
                GeneCategoryDef a = CategoryOf(left != null ? left.Def : null);
                GeneCategoryDef b = CategoryOf(right != null ? right.Def : null);

                if (a != b)
                {
                    float first = a != null ? a.displayPriorityInXenotype : float.MaxValue;
                    float second = b != null ? b.displayPriorityInXenotype : float.MaxValue;

                    // Descending, which is the direction the gene assembler reads its own priorities in.
                    return second.CompareTo(first);
                }

                float leftOrder = left != null && left.Def != null ? left.Def.displayOrderInCategory : 0f;
                float rightOrder = right != null && right.Def != null ? right.Def.displayOrderInCategory : 0f;

                if (Mathf.Abs(leftOrder - rightOrder) > 0.0001f)
                    return leftOrder.CompareTo(rightOrder);

                return string.Compare(Label(left), Label(right), StringComparison.OrdinalIgnoreCase);
            });

            categories.Clear();
            tallies.Clear();

            for (int i = 0; i < choices.Count; i++)
            {
                GeneChoice choice = choices[i];

                if (choice == null || choice.Def == null)
                    continue;

                GeneCategoryDef category = CategoryOf(choice.Def);

                if (category == null)
                    continue;

                int tally;

                if (tallies.TryGetValue(category, out tally))
                {
                    tallies[category] = tally + 1;
                }
                else
                {
                    tallies[category] = 1;

                    // Added as they are met, which after the sort above is already display order -- so the rail
                    // and the grid agree without the rail sorting anything of its own.
                    categories.Add(category);
                }
            }
        }

        private static GeneCategoryDef CategoryOf(GeneDef def)
        {
            return def != null ? def.displayCategory : null;
        }

        private static string Label(GeneChoice choice)
        {
            return choice != null && choice.Def != null ? choice.Def.LabelCap.ToString() : string.Empty;
        }
    }
}
