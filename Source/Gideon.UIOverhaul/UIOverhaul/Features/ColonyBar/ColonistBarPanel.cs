using System;
using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using Gideon.UIOverhaul.Features.Options;
using Gideon.UIOverhaul.Features.Pawns;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.ColonyBar
{
    /// <summary>
    /// The grouped colonist bar: named foldable groups of pawns across the top of the screen, each tile a live
    /// view of where that pawn is.
    ///
    /// <b>The roster is vanilla's, the arrangement is ours.</b> Who counts as a colonist comes from
    /// <c>Find.ColonistBar.Entries</c>, which already answers for caravans, other maps, corpses and every way a
    /// pawn can join or leave. Rebuilding that list would mean re-deriving all of it and drifting from it; this
    /// only decides how the entries are grouped and drawn.
    ///
    /// <b>Rebuilt every frame rather than cached.</b> The entry list is already cached by vanilla and the grouping
    /// is a walk over a handful of lists, so there is nothing here worth the invalidation bugs a cache would bring:
    /// a bar that is one recruitment behind is a bar showing a pawn who is not there.
    ///
    /// <b>Folding hides pawns, so folding must not hide emergencies.</b> A folded group keeps a roll-up and raises
    /// a badge when somebody inside is downed, bleeding, breaking or starving. Without that, the feature's main use
    /// -- stop looking at the people who are fine -- is also the way to miss the person who stopped being fine.
    /// </summary>
    internal static class ColonistBarPanel
    {
        private const float MeterHeight = 3f;
        private const float InnerPad = 6f;
        private const float GroupGap = 10f;
        private const float TopMargin = 8f;

        /// <summary>
        /// Tile metrics, which are not constants because the font they are sized for is not guaranteed.
        ///
        /// <b>Asking for Tiny is a request, not a result.</b> <c>Text.Font</c>'s setter substitutes Small whenever
        /// <c>Text.TinyFontSupported</c> is false, and that is false for the disable-tiny-text accessibility
        /// preference, a language whose <c>canBeTiny</c> is false, the Steam Deck, and any draw during a long
        /// event. Small's line box is 22 pixels against the 15 this row was built for, and <c>Widgets.Label</c>
        /// clips rather than shrinks, so every name would lose its ascenders and descenders -- shaved top and
        /// bottom rather than visibly overflowing, which is the hard kind of bug to see in a screenshot.
        ///
        /// <b>Width grows too, and that is the part wrapping could not fix.</b> Small glyphs are about half again
        /// as wide, so the tile widens with the font. The command gizmos solve their overflow by wrapping to two
        /// lines, which works there because a gizmo label is a phrase with a space in it; a pawn's short name is
        /// one word, so a second line buys no characters and only makes the tile taller.
        /// </summary>
        private static float NameHeight => UIFonts.LineHeightOf(GameFont.Tiny);

        private static float TileWidth => Text.TinyFontSupported ? 64f : 88f;

        private static float ViewSize => TileWidth - 2f;

        /// <summary>
        /// Height of a tile's picture, taller than it is wide.
        ///
        /// <b>Matched to the render target's aspect, not chosen separately.</b> The camera takes its aspect from
        /// that target, so a view rect of a different shape would either letterbox or crop the very framing the
        /// zoom was set for. Portrait because a pawn is taller than wide, which a square tile spent a third of its
        /// width failing to use.
        /// </summary>
        private static float ViewHeight => Mathf.Round(ViewSize * 124f / 96f);

        /// <summary>Tall enough for the group name, which is drawn at Small and so is never substituted.</summary>
        private static float HeaderHeight => Mathf.Max(22f, UIFonts.LineHeightOf(GameFont.Small) + 2f);

        /// <summary>
        /// Where the bar starts, which is lower while dev mode is on.
        ///
        /// <b>RimWorld's dev toolbar owns the top centre of the screen.</b> It draws at y=3 with a height of 25,
        /// centred horizontally, which is precisely where a folded group ends up once the bar is narrow enough to
        /// centre under it. The toolbar takes the clicks, so the group could be folded and then never unfolded --
        /// reported from a test on 2026-08-21. Moving down while dev mode is on is cheaper than trying to dodge it
        /// horizontally, which would move the bar every time a group folded.
        /// </summary>
        private static float Top => Prefs.DevMode ? 32f : TopMargin;
        private const float SideMargin = 24f;
        private const float NewGroupWidth = 26f;

        /// <summary>Height of a tile: the view, then the name, then the two meters.</summary>
        private static float TileHeight => ViewHeight + 3f + NameHeight + 2f + MeterHeight * 2f + 2f;

        private static float OpenHeight => HeaderHeight + InnerPad * 2f + TileHeight;

        /// <summary>Two lines of roll-up text, measured rather than assumed for the same reason as the name row.</summary>
        private static float RollupHeight => NameHeight * 2f + 6f;

        /// <summary>One group as it will be drawn: its object, its members, and whether it is folded.</summary>
        private struct Block
        {
            internal PawnGroup Group;
            internal List<Pawn> Members;
            internal bool Collapsed;
            internal string Name;
            internal Color Color;
        }

        private static readonly List<Block> Blocks = new List<Block>();

        /// <summary>
        /// Scratch for the grouping pass, reused between frames.
        ///
        /// <b>The bar rebuilds every frame,</b> so allocating these would be a dozen lists a frame and several
        /// hundred a second for a result that is almost always identical to the last one. The same reason the pawns
        /// tab keeps its own scratch lists rather than allocating per map.
        /// </summary>
        private static readonly List<Pawn> Roster = new List<Pawn>();

        private static readonly List<Pawn> Claimed = new List<Pawn>();

        private static readonly List<Pawn> Rest = new List<Pawn>();

        /// <summary>
        /// One member list per group, kept between frames.
        ///
        /// A block holds a reference to its own list rather than copying out of a shared one, so these cannot be a
        /// single scratch list; a pool indexed by position gives the same effect without allocating.
        /// </summary>
        private static readonly List<List<Pawn>> Buckets = new List<List<Pawn>>();

        private static List<Pawn> Bucket(int slot)
        {
            while (Buckets.Count <= slot)
                Buckets.Add(new List<Pawn>());

            Buckets[slot].Clear();

            return Buckets[slot];
        }

        /// <summary>Where each pawn's tile ended up this frame, for the hit-test patches to answer from.</summary>
        private static readonly List<KeyValuePair<Rect, Pawn>> Hits = new List<KeyValuePair<Rect, Pawn>>();

        private static float scroll;

        /// <summary>The bar's rectangle this frame, or zero when it drew nothing.</summary>
        internal static Rect Bounds { get; private set; }

        /// <summary>The pawn whose tile covers a screen point, or null. Used to replace vanilla's hit-testing.</summary>
        internal static Pawn At(Vector2 point)
        {
            for (int i = 0; i < Hits.Count; i++)
            {
                if (Hits[i].Key.Contains(point))
                    return Hits[i].Value;
            }

            return null;
        }

        /// <summary>
        /// Draws the whole bar.
        ///
        /// Guarded with <c>UIGuard.Try</c> rather than a guarded panel, because a panel needs a rectangle to put
        /// its failure notice in and this bar has no fixed one: its size is the answer to how many groups are
        /// folded, which is not known until it has drawn.
        /// </summary>
        internal static void Draw()
        {
            UIGuard.Try("Bar.Colonist", Contents,
                "The colonist bar failed to draw. Your colonists are unaffected.");
        }

        private static void Contents()
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Hits.Clear();
            Build();

            if (Blocks.Count == 0)
            {
                Bounds = Rect.zero;

                return;
            }

            float width = 0f;
            float height = 0f;

            foreach (Block block in Blocks)
            {
                width += WidthOf(block) + GroupGap;
                height = Mathf.Max(height, HeightOf(block));
            }

            width += NewGroupWidth;

            float room = UI.screenWidth - SideMargin * 2f;

            // Centred while it fits and left-aligned once it does not, which is what keeps a small colony's bar
            // where vanilla's was and stops a large one running off the right of the screen.
            float x = width <= room
                ? (UI.screenWidth - width) * 0.5f
                : SideMargin - scroll;

            Bounds = new Rect(SideMargin, Top, room, height);

            Wheel(width, room);

            foreach (Block block in Blocks)
            {
                float w = WidthOf(block);

                // Wholly off either edge: skipped rather than clipped, so a folded colony of forty costs nothing
                // to the right of the screen.
                if (x + w > SideMargin - GroupGap && x < UI.screenWidth - SideMargin + GroupGap)
                    DrawBlock(new Rect(x, Top, w, HeightOf(block)), block, palette);

                x += w + GroupGap;
            }

            NewGroup(new Rect(x, Top, NewGroupWidth, HeaderHeight), palette);

            // After every tile has said whether it wants a live view, so the renderer knows the whole set before
            // it decides which few to refresh.
            PawnTileView.Refresh();
        }

        /// <summary>Horizontal wheel scrolling, but only once the bar is actually wider than the screen.</summary>
        private static void Wheel(float width, float room)
        {
            if (width <= room)
            {
                scroll = 0f;

                return;
            }

            if (Event.current.type == EventType.ScrollWheel && Bounds.Contains(Event.current.mousePosition))
            {
                scroll = Mathf.Clamp(scroll + Event.current.delta.y * 12f, 0f, width - room);

                Event.current.Use();
            }
            else
            {
                scroll = Mathf.Clamp(scroll, 0f, width - room);
            }
        }

        private static float WidthOf(Block block)
        {
            if (block.Collapsed)
                return Mathf.Max(126f, Text.CalcSize(block.Name).x + 92f);

            return Mathf.Max(HeaderWidth(block), block.Members.Count * TileWidth + InnerPad * 2f);
        }

        private static float HeaderWidth(Block block)
        {
            return Text.CalcSize(block.Name).x + 92f;
        }

        private static float HeightOf(Block block)
        {
            return block.Collapsed ? HeaderHeight + RollupHeight : OpenHeight;
        }

        /// <summary>
        /// Groups the roster.
        ///
        /// <b>Unassigned is the set difference, computed here.</b> Every entry that no group claims lands in it, so
        /// a pawn can never be absent from the bar because a group forgot to take them.
        /// </summary>
        private static void Build()
        {
            Blocks.Clear();

            GameComponent_PawnGroups store = GameComponent_PawnGroups.Current;
            List<RimWorld.ColonistBar.Entry> entries = Find.ColonistBar?.Entries;

            if (entries == null)
                return;

            Roster.Clear();

            foreach (RimWorld.ColonistBar.Entry entry in entries)
            {
                if (entry.pawn != null && !Roster.Contains(entry.pawn))
                    Roster.Add(entry.pawn);
            }

            if (Roster.Count == 0)
                return;

            List<Pawn> roster = Roster;

            Claimed.Clear();

            List<Pawn> claimed = Claimed;

            if (store != null)
            {
                int slot = 0;

                foreach (PawnGroup group in store.Groups)
                {
                    List<Pawn> members = Bucket(slot++);

                    // Ordered by the group's own list rather than by the roster, since the order inside a group is
                    // something the player arranged.
                    foreach (Pawn pawn in group.Pawns)
                    {
                        if (pawn != null && roster.Contains(pawn))
                        {
                            members.Add(pawn);
                            claimed.Add(pawn);
                        }
                    }

                    Blocks.Add(new Block
                    {
                        Group = group,
                        Members = members,
                        Collapsed = group.Collapsed,
                        Name = group.Name,
                        Color = group.Color
                    });
                }
            }

            Rest.Clear();

            List<Pawn> rest = Rest;

            foreach (Pawn pawn in roster)
            {
                if (!claimed.Contains(pawn))
                    rest.Add(pawn);
            }

            // Drawn even when empty only if there are no groups at all, so a colony that has never made one still
            // sees its people. Once groups exist, an empty Unassigned is just a stub taking up room.
            if (rest.Count > 0 || Blocks.Count == 0)
            {
                Blocks.Add(new Block
                {
                    Group = null,
                    Members = rest,
                    Collapsed = store != null && store.UnassignedCollapsed,
                    Name = "Unassigned",
                    Color = new Color(0.55f, 0.57f, 0.60f)
                });
            }
        }

        private static void DrawBlock(Rect rect, Block block, UIColorPaletteDef palette)
        {
            // No panel behind the tiles, asked for on 2026-08-21. Each tile already carries its own frame and
            // ground, so the container only added a second border around a row of bordered things and a slab of
            // opaque grey over the map.
            //
            // The header keeps a backing of its own: it is text over whatever the map happens to be, and a group
            // name on grass is not readable. Translucent rather than the panel colour, so the map still shows
            // through and the strip reads as belonging to the bar rather than as a window.
            Header(new Rect(rect.x, rect.y, rect.width, HeaderHeight), block, palette);

            if (block.Collapsed)
            {
                Rollup(new Rect(rect.x + InnerPad, rect.y + HeaderHeight, rect.width - InnerPad * 2f,
                    RollupHeight), block, palette);

                return;
            }

            float x = rect.x + InnerPad;

            foreach (Pawn pawn in block.Members)
            {
                Tile(new Rect(x, rect.y + HeaderHeight + InnerPad, TileWidth, TileHeight), pawn, block, palette);

                x += TileWidth;
            }
        }

        private static void Header(Rect rect, Block block, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.55f));

            if (Mouse.IsOver(rect))
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            Color previous = GUI.color;
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;

            // Full header height rather than a 12 pixel box: the glyph is Tiny, so it becomes Small wherever tiny
            // text is unavailable, and a clipped caret is a clipped fold control -- the one thing on the header
            // that has to stay readable.
            Rect caret = new Rect(rect.x + 4f, rect.y, 12f, rect.height);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = palette.TextSecondary;

            Widgets.Label(caret, block.Collapsed ? ">" : "v");

            Widgets.DrawBoxSolid(new Rect(rect.x + 19f, rect.y + 4f, 4f, rect.height - 8f), block.Color);

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextPrimary;
            Text.Font = GameFont.Small;

            bool wrap = Text.WordWrap;
            Text.WordWrap = false;

            Widgets.Label(new Rect(rect.x + 28f, rect.y, rect.width - 76f, rect.height), block.Name);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;

            // The count, unless a folded group has something wrong inside it, in which case that takes the slot.
            // Drawn once in whichever colour applies rather than twice, since two labels in one rect overlap into
            // an unreadable smear.
            string alarm = block.Collapsed ? Alarm(block.Members) : null;

            GUI.color = alarm != null ? GzpPalette.Bad : palette.TextDisabled;

            Widgets.Label(new Rect(rect.x, rect.y, rect.width - 26f, rect.height),
                alarm ?? block.Members.Count.ToString());

            Text.WordWrap = wrap;
            Text.Anchor = anchor;
            Text.Font = font;
            GUI.color = previous;

            // Three dots rather than a gear glyph: this mod ships no gear texture, and a borrowed vanilla icon
            // that means something else elsewhere is worse than a label that means only this.
            Rect gear = new Rect(rect.xMax - 24f, rect.y + 2f, 20f, rect.height - 4f);

            if (GzpPalette.GrayButton(gear, "..."))
            {
                GroupActions.Open(block.Group, block.Members, null);

                return;
            }

            // Taken after the gear, since the gear sits inside this rect and whichever is asked first consumes
            // the click.
            if (!Widgets.ButtonInvisible(rect))
                return;

            Fold(block);
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private static void Fold(Block block)
        {
            UIGuard.Try("Bar.Fold", () =>
            {
                GameComponent_PawnGroups store = GameComponent_PawnGroups.Current;

                if (block.Group != null)
                    block.Group.Collapsed = !block.Group.Collapsed;
                else if (store != null)
                    store.UnassignedCollapsed = !store.UnassignedCollapsed;
            }, null);
        }

        /// <summary>
        /// What a folded group says about itself.
        ///
        /// Two lines, because one of them has to be able to be bad news. Counting rather than naming, except for
        /// the emergency, which names the pawn: "someone is bleeding" is not actionable and "Ito is bleeding" is.
        /// </summary>
        private static void Rollup(Rect rect, Block block, UIColorPaletteDef palette)
        {
            // Its own backing, now that there is no panel behind it. Same wash as the header above, so a folded
            // group reads as one strip rather than as a heading with loose text under it.
            Widgets.DrawBoxSolid(new Rect(rect.x - InnerPad, rect.y, rect.width + InnerPad * 2f, rect.height),
                new Color(0f, 0f, 0f, 0.55f));

            Color previous = GUI.color;
            GameFont font = Text.Font;

            Text.Font = GameFont.Tiny;

            string urgent = Urgent(block.Members);

            GUI.color = urgent != null ? GzpPalette.Bad : palette.TextSecondary;

            float line = NameHeight;

            Widgets.Label(new Rect(rect.x, rect.y, rect.width, line),
                urgent ?? block.Members.Count + (block.Members.Count == 1 ? " pawn" : " pawns"));

            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(rect.x, rect.y + line, rect.width, line), Secondary(block.Members));

            Text.Font = font;
            GUI.color = previous;
        }

        private static string Secondary(List<Pawn> members)
        {
            int drafted = 0;
            int away = 0;

            foreach (Pawn pawn in members)
            {
                if (pawn?.drafter != null && pawn.drafter.Drafted)
                    drafted++;

                if (pawn != null && pawn.MapHeld != Find.CurrentMap)
                    away++;
            }

            if (drafted > 0 && away > 0)
                return drafted + " drafted, " + away + " away";

            if (drafted > 0)
                return drafted + " drafted";

            if (away > 0)
                return away + " away";

            return "nothing urgent";
        }

        /// <summary>The badge on a folded header, or null. Short, because it shares the header with the name.</summary>
        private static string Alarm(List<Pawn> members)
        {
            int down = 0;

            foreach (Pawn pawn in members)
            {
                if (pawn != null && pawn.Downed)
                    down++;
            }

            if (down > 0)
                return down + " DOWN";

            return Urgent(members) != null ? "!" : null;
        }

        /// <summary>
        /// The single worst thing happening inside a folded group, worded for a person, or null.
        ///
        /// Ordered worst first and stops at the first hit: a folded group has one line to spend, so it spends it on
        /// the thing that would make somebody unfold.
        /// </summary>
        private static string Urgent(List<Pawn> members)
        {
            return UIGuard.Try("Bar.Urgent", () =>
            {
                foreach (Pawn pawn in members)
                {
                    if (pawn == null)
                        continue;

                    if (pawn.Dead)
                        return pawn.LabelShortCap + " is dead";

                    if (pawn.Downed)
                        return pawn.LabelShortCap + " is down";

                    if (pawn.health?.hediffSet != null && pawn.health.hediffSet.BleedRateTotal > 0.1f)
                        return pawn.LabelShortCap + " is bleeding";

                    if (pawn.InMentalState)
                        return pawn.LabelShortCap + " is breaking";

                    Need_Food food = pawn.needs?.food;

                    if (food != null && food.CurCategory >= HungerCategory.UrgentlyHungry)
                        return pawn.LabelShortCap + " is starving";
                }

                return null;
            }, null, null);
        }

        private static void Tile(Rect rect, Pawn pawn, Block block, UIColorPaletteDef palette)
        {
            Rect view = new Rect(rect.x + (rect.width - ViewSize) * 0.5f, rect.y, ViewSize, ViewHeight);

            Hits.Add(new KeyValuePair<Rect, Pawn>(view, pawn));

            PawnTileView.Want(pawn);

            Texture live = PawnTileView.GetTexture(pawn);

            // The frame goes down FIRST, and that ordering is the whole bug that made every tile a flat grey
            // square. UIElementPainter.OutlineRounded is not a stroke: it fills the entire rect with the border
            // colour and then fills the inset with the second colour. Called after the portrait it painted right
            // over it. Aaron spotted this from the screenshot on 2026-08-21.
            //
            // So it is drawn before the picture, and the picture goes into the inset, which leaves the one pixel
            // ring showing rather than being covered in turn.
            UIElementPainter.OutlineRounded(view, Mouse.IsOver(view) ? block.Color : palette.Border,
                palette.SurfaceSunken);

            Rect inner = view.ContractedBy(1f);

            // White before any texture: GUI.DrawTexture multiplies by GUI.color, and this runs inside vanilla's
            // OnGUI where the colour is whatever the last caller left. A tint left set is the difference between
            // a portrait and an empty square.
            GUI.color = Color.white;

            if (live != null)
            {
                GUI.DrawTexture(inner, live, ScaleMode.ScaleAndCrop);
            }
            else
            {
                // No live picture: the setting is off, the pawn is elsewhere, or their first render has not
                // happened. A portrait is drawn rather than a blank square, which would read as a failure.
                //
                // Asked of PortraitsCache directly rather than through PawnPortraitCell, which is built for the
                // pawns tab and wrong here twice over: it crops to a circle, wasting a square tile's corners, and
                // it takes its own invisible button, which swallowed every click on a tile so nothing selected or
                // jumped. Reported from a test on 2026-08-21.
                RenderTexture face = Portrait(pawn);

                if (face != null)
                    GUI.DrawTexture(inner, face);
            }

            Badges(view, pawn, palette);

            // The label strip gets its own wash for the same reason the header does: with no panel behind the bar,
            // a pawn's name would otherwise be Tiny text sitting on whatever terrain is under it.
            Widgets.DrawBoxSolid(new Rect(view.x, view.yMax, view.width, rect.yMax - view.yMax),
                new Color(0f, 0f, 0f, 0.55f));

            Name(new Rect(view.x + 1f, view.yMax + 2f, view.width - 2f, NameHeight), pawn, palette);

            Meters(new Rect(view.x, view.yMax + 3f + NameHeight, ViewSize, MeterHeight * 2f + 2f), pawn, palette);

            Clicks(view, pawn, block);
        }

        /// <summary>
        /// Side the skull is drawn at, and the gap between it and the name.
        ///
        /// Tied to the name's own line height rather than fixed, so it grows with the text instead of becoming a
        /// speck beside a Small-font name. Capped, because past about fourteen pixels it starts competing with the
        /// name for attention rather than labelling it.
        /// </summary>
        private static float SkullSize => Mathf.Min(14f, NameHeight - 3f);

        private const float SkullGap = 2f;

        /// <summary>
        /// The pawn's name, with a skull standing in for the word "Undead".
        ///
        /// <b>The word is the problem, not the length.</b> One with Death names what it raises "Undead Kleinert",
        /// and seven of the sixteen characters a tile can show were spent saying something every tile in the group
        /// said. Asked for on 2026-08-21 after a test showed five tiles reading "ndead Math", "Undead Gril" and the
        /// like -- clipped at both ends by a centred anchor, so they read as different pawns rather than as
        /// shortened names.
        ///
        /// <b>No ellipses.</b> They were added as a fallback and then fired on almost every name, turning Andrew
        /// into "Andr..." and Basilicus into "Basili..." -- worse than the clipping they replaced, because the
        /// three dots cost characters of their own. Removed on Aaron's instruction, twice given: a name that does
        /// not fit is simply cut, and the tile is wide enough that few are.
        ///
        /// <b>The text is always left-aligned after the glyph, and only the pair is centred.</b> That is what
        /// keeps an overlong name clipping at the right end alone; centring the text itself is what produced
        /// "dead Kleine", cut at both ends and reading as a different pawn.
        /// </summary>
        private static void Name(Rect row, Pawn pawn, UIColorPaletteDef palette)
        {
            string label = pawn.LabelShortCap;
            bool skull = Undead(pawn, ref label) && BarGlyphs.Skull != null;

            Color previous = GUI.color;
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            bool wrap = Text.WordWrap;

            Text.Font = GameFont.Tiny;
            Text.WordWrap = false;

            float glyph = skull ? SkullSize + SkullGap : 0f;
            float wanted = Mathf.Min(row.width, glyph + Text.CalcSize(label).x);
            float x = row.x + (row.width - wanted) * 0.5f;

            if (skull)
            {
                // Dimmer than the name: this marks a pawn, it does not warn about one. Drawn in the palette's
                // secondary rather than the danger red, which would make every undead look like a casualty.
                GUI.color = palette.TextSecondary;

                GUI.DrawTexture(new Rect(x, row.y + (row.height - SkullSize) * 0.5f, SkullSize, SkullSize),
                    BarGlyphs.Skull);

                x += glyph;
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = pawn.Dead ? GzpPalette.Bad : palette.TextPrimary;

            Widgets.Label(new Rect(x, row.y, Mathf.Max(0f, row.xMax - x), row.height), label);

            Text.WordWrap = wrap;
            Text.Anchor = anchor;
            Text.Font = font;
            GUI.color = previous;
        }

        /// <summary>
        /// Strips the "Undead" prefix and says whether this pawn earns a skull.
        ///
        /// <b>Two ways to earn one, because there are two ways to be undead.</b> The prefix covers anything One
        /// with Death raised and named, including a corpse-walker whose necromancer has since died and which no
        /// tracker lists any more. The tracker covers a controlled undead somebody has renamed, who has no prefix
        /// to strip but is still undead.
        ///
        /// <b>Gated on the mod being loaded, not on the name alone.</b> A vanilla colonist somebody nicknamed
        /// "Undead Dave" keeps their whole name in a game that has no undead in it.
        /// </summary>
        private static bool Undead(Pawn pawn, ref string label)
        {
            const string prefix = "Undead ";

            if (!Integrations.OneWithDeathIntegration.Available)
                return false;

            bool named = label != null && label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

            if (named)
                label = label.Substring(prefix.Length);

            return named || Integrations.OneWithDeathIntegration.IsControlledUndead(pawn);
        }

        /// <summary>
        /// The pawn's portrait, framed on the head.
        ///
        /// Lifted and zoomed through the parameters <c>PortraitsCache</c> already takes, because a whole-body
        /// render at 62 pixels is a silhouette. Guarded: this allocates a render target the first time it is asked
        /// about a pawn, and it runs inside vanilla's own OnGUI where an exception would not be ours to catch.
        /// </summary>
        private static RenderTexture Portrait(Pawn pawn)
        {
            bool hats = !(UIOverhaulSettingsFile.Current?.barHideHeadgear ?? false);

            return UIGuard.Try("Bar.Portrait",
                () => PortraitsCache.Get(pawn, new Vector2(ViewSize, ViewHeight), Rot4.South,
                    cameraOffset: new Vector3(0f, 0f, 0.3f), cameraZoom: 1.5f, renderHeadgear: hats),
                null, null);
        }

        /// <summary>Map numeral and drafted flag: the two things the picture alone cannot say.</summary>
        private static void Badges(Rect view, Pawn pawn, UIColorPaletteDef palette)
        {
            Color previous = GUI.color;
            GameFont font = Text.Font;

            Text.Font = GameFont.Tiny;

            // Sized from the line height rather than fixed, so a single character is not shaved top and bottom
            // when Tiny has been substituted for Small.
            float side = NameHeight;

            if (pawn.MapHeld != null && pawn.MapHeld != Find.CurrentMap)
            {
                Rect badge = new Rect(view.x + 2f, view.y + 2f, side + 8f, side);

                Widgets.DrawBoxSolid(badge, new Color(0f, 0f, 0f, 0.66f));

                GUI.color = palette.TextPrimary;

                Widgets.Label(badge, " " + MapNumeral(pawn.MapHeld));
            }

            if (pawn.drafter != null && pawn.drafter.Drafted)
            {
                Rect flag = new Rect(view.xMax - side - 2f, view.yMax - side - 2f, side, side);

                Widgets.DrawBoxSolid(flag, GzpPalette.Warn);

                GUI.color = Color.black;

                Widgets.Label(flag, " D");
            }

            Text.Font = font;
            GUI.color = previous;
        }

        private static string MapNumeral(Map map)
        {
            return UIGuard.Try("Bar.MapNumeral", () =>
            {
                List<Map> maps = Find.Maps;

                return maps == null ? "?" : (maps.IndexOf(map) + 1).ToString();
            }, "?", null);
        }

        private static void Meters(Rect rect, Pawn pawn, UIColorPaletteDef palette)
        {
            float health = PawnAttributes.HealthFractionOf(pawn);

            // The same three bands the pawns tab uses for its health column, so a pawn who reads as hurt there
            // reads as hurt here rather than the two screens disagreeing about what amber means.
            Bar(new Rect(rect.x, rect.y, rect.width, MeterHeight), health,
                health > 0.9f ? palette.Success : health > 0.35f ? palette.Info : palette.Danger, palette);

            // Asked rather than inferred from the value: a pawn with no mood need at all is not a pawn in
            // despair, and a bar drawn at zero would say the second thing.
            if (!PawnAttributes.HasMood(pawn))
                return;

            float mood = PawnAttributes.MoodFractionOf(pawn);

            Bar(new Rect(rect.x, rect.y + MeterHeight + 2f, rect.width, MeterHeight), mood,
                mood < 0.3f ? palette.Danger : palette.Mood, palette);
        }

        private static void Bar(Rect rect, float fill, Color color, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.SurfaceSunken);

            if (fill > 0f)
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill), rect.height),
                    color);
        }

        /// <summary>
        /// Left click selects and jumps, right click moves the pawn between groups.
        ///
        /// <b>Selection is vanilla's, not a copy of it.</b> Clicking hands off to the selector and the camera
        /// driver, so shift-click adds to a selection and a second click centres the view, exactly as vanilla's bar
        /// behaves.
        /// </summary>
        private static void Clicks(Rect view, Pawn pawn, Block block)
        {
            if (Mouse.IsOver(view))
            {
                TooltipHandler.TipRegion(view, (TipSignal) Tip(pawn, block));

                // Taken from the event rather than from the button below, because Widgets.ButtonInvisible answers
                // for the left button only: routing the group move through it meant right-click did nothing.
                if (Event.current.type == EventType.MouseDown && Event.current.button == 1)
                {
                    Event.current.Use();

                    UIGuard.Try("Bar.MoveMenu", () => MoveMenu(pawn), null);

                    return;
                }
            }

            if (!Widgets.ButtonInvisible(view, false))
                return;

            UIGuard.Try("Bar.ClickPawn", () =>
            {
                // Shift adds to whatever is already selected, without moving the camera, which is what makes
                // building a squad out of several groups possible.
                if (Event.current.shift)
                {
                    if (pawn.Spawned)
                        Find.Selector?.Select(pawn);

                    return;
                }

                // One call rather than select-then-jump: TryJumpAndSelect handles a pawn who is in a caravan or on
                // another map, where selecting first and jumping after would select something on a map the camera
                // is about to leave.
                CameraJumper.TryJumpAndSelect(pawn);
            }, null);
        }

        private static string Tip(Pawn pawn, Block block)
        {
            string where = pawn.MapHeld != null && pawn.MapHeld != Find.CurrentMap
                ? "\nOn another map."
                : string.Empty;

            return pawn.LabelCap + "\n" + block.Name + where
                   + "\n\nClick to select and jump. Shift-click to add to the selection."
                   + "\nRight-click to move to another group.";
        }

        private static void MoveMenu(Pawn pawn)
        {
            GameComponent_PawnGroups store = GameComponent_PawnGroups.Current;

            if (store == null)
                return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            PawnGroup now = store.GroupOf(pawn);

            foreach (PawnGroup group in store.Groups)
            {
                PawnGroup captured = group;

                if (captured == now)
                    continue;

                options.Add(new FloatMenuOption("Move to " + captured.Name,
                    UIGuard.Wrap("Bar.MovePawn", () => store.Assign(pawn, captured))));
            }

            if (now != null)
                options.Add(new FloatMenuOption("Move to Unassigned",
                    UIGuard.Wrap("Bar.Unassign", () => store.Assign(pawn, null))));

            options.Add(new FloatMenuOption("New group with this pawn...",
                UIGuard.Wrap("Bar.NewGroupWith", () =>
                    Find.WindowStack.Add(new Dialog_NameGroup("New group", pawn.LabelShortCap, name =>
                        store.Assign(pawn, store.Add(name)))))));

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void NewGroup(Rect rect, UIColorPaletteDef palette)
        {
            if (GameComponent_PawnGroups.Current == null)
                return;

            TooltipHandler.TipRegion(rect, (TipSignal) "New group");

            if (!GzpPalette.GrayButton(rect, "+"))
                return;

            Find.WindowStack.Add(new Dialog_NameGroup("New group", string.Empty,
                name => GameComponent_PawnGroups.Current?.Add(name)));
        }
    }
}
