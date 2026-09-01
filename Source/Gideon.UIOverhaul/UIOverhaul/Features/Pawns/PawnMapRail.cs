using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// The maps the tab can list, and which one is being looked at.
    ///
    /// <b>The table used to group twice: by map, then by kind of person inside it.</b> Only one of those is
    /// something you read down. Colonists, prisoners and patients sit in one population and are compared row
    /// against row -- who is hurt, who is idle, who is in the wrong area -- which is exactly what a grouped
    /// table is for. Maps are not like that. Nobody compares a colonist at home with one at a mining outpost;
    /// they are separate colonies with separate problems, and a map is something you switch to rather than
    /// read past. Switching is what a rail is for.
    ///
    /// <b>No rail with one map,</b> which is most colonies. A rail holding a single entry spends
    /// <see cref="Width"/> pixels saying nothing, and the map heading it would replace was already saying
    /// nothing for the same reason. Below two maps the tab is drawn exactly as it was, minus that heading.
    ///
    /// <b>All maps stays reachable</b> as the last entry, and restores the old view exactly: every map, its
    /// name back as a top-level heading, kinds nested underneath. The grouping was not removed, it stopped
    /// being the only way to look.
    /// </summary>
    internal static class PawnMapRail
    {
        /// <summary>What <see cref="Selected"/> holds when every map is being listed at once.</summary>
        internal const int AllMaps = -1;

        /// <summary>The rail's width, plus the gap to the table. Only spent when the rail is drawn.</summary>
        internal const float Width = 190f;

        internal const float Gap = 8f;

        /// <summary>
        /// One map's readings, gathered in the pass <see cref="PawnsPanel"/> already makes over its pawns.
        ///
        /// Filled from the candidates rather than from the rows, so a map keeps its count and its dot while
        /// its people are filtered out of view. A filter is a view preference; it does not move anybody.
        /// </summary>
        internal sealed class MapTally
        {
            internal int Id;
            internal string Label;

            /// <summary>Everyone this tab could list here, before the category filters and the search.</summary>
            internal int People;

            /// <summary>Downed, bleeding, or with something tendable. What the dot is for.</summary>
            internal int NeedsCare;

            /// <summary>Whether any of <see cref="NeedsCare"/> is the urgent kind rather than a wound.</summary>
            internal bool Urgent;

            /// <summary>Counts per <see cref="PawnCategories.All"/>, positionally.</summary>
            internal readonly int[] ByCategory = new int[PawnCategories.All.Length];

            internal int Idle;
            internal float MoodTotal;
            internal int MoodCount;
        }

        /// <summary>Rebuilt every frame by <see cref="PawnsPanel.Collect"/>, in map order.</summary>
        internal static readonly List<MapTally> Tallies = new List<MapTally>();

        /// <summary>
        /// The chosen map's id, or <see cref="AllMaps"/>.
        ///
        /// Remembered between openings for the same reason the category filters are: which colony you are
        /// managing is not something to re-pick every time the tab opens.
        /// </summary>
        internal static int Selected = AllMaps;

        private static Vector2 scroll;
        private static bool dragging;
        private static float dragOffset;

        /// <summary>Whether there is more than one map to choose between.</summary>
        internal static bool Wanted
        {
            get
            {
                List<Map> maps = Find.Maps;

                return maps != null && maps.Count > 1;
            }
        }

        /// <summary>The width the window has to find for the rail, which is none when it is not drawn.</summary>
        internal static float WidthNow
        {
            get { return Wanted ? Width + Gap : 0f; }
        }

        /// <summary>Whether this map's rows should be written at all.</summary>
        internal static bool Shows(Map map)
        {
            return Selected == AllMaps || !Wanted || map.uniqueID == Selected;
        }

        /// <summary>
        /// Whether a map heading belongs above the rows.
        ///
        /// Only when every map is being shown at once. With one map picked, or with only one map to pick, the
        /// heading would be the same words above every row on screen.
        /// </summary>
        internal static bool WantsHeadings
        {
            get { return Wanted && Selected == AllMaps; }
        }

        /// <summary>
        /// Drops a selection whose map has gone, which happens the moment a pocket map closes or an outpost
        /// is abandoned. Falling back to every map rather than to another one: picking a colony on the
        /// player's behalf is worse than showing them all of them.
        /// </summary>
        /// <remarks>
        /// Against the live map list rather than against <see cref="Tallies"/>, which are cleared at the top
        /// of the same pass this is called from: a selection checked against a list that is still being built
        /// would drop every map on the frame a pocket map closed.
        /// </remarks>
        internal static void Validate(List<Map> maps)
        {
            if (Selected == AllMaps || maps == null)
                return;

            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i].uniqueID == Selected)
                    return;
            }

            Selected = AllMaps;
        }

        /// <summary>
        /// The readings for what is on screen: one map's, or every map's added up.
        ///
        /// <b>Not narrowed by the category filters.</b> The header says what is here; the filters say what you
        /// are looking at. Unticking Patients should not make a hurt one stop counting.
        /// </summary>
        internal static MapTally Showing()
        {
            MapTally total = new MapTally { Label = "All maps" };

            for (int i = 0; i < Tallies.Count; i++)
            {
                MapTally tally = Tallies[i];

                if (Selected != AllMaps && Wanted && tally.Id != Selected)
                    continue;

                total.Label = tally.Label;
                total.Id = tally.Id;
                total.People += tally.People;
                total.NeedsCare += tally.NeedsCare;
                total.Urgent |= tally.Urgent;
                total.Idle += tally.Idle;
                total.MoodTotal += tally.MoodTotal;
                total.MoodCount += tally.MoodCount;

                for (int c = 0; c < total.ByCategory.Length; c++)
                    total.ByCategory[c] += tally.ByCategory[c];
            }

            // Only one map contributed, so its own name is the honest label. More than one and the name of the
            // last one round the loop would be a lie.
            if (Selected == AllMaps && Tallies.Count != 1)
            {
                total.Label = "All maps";
                total.Id = AllMaps;
            }

            return total;
        }

        /// <summary>
        /// The rail. Returns true when the pick changed, which is the panel's cue to drop its scroll.
        /// </summary>
        internal static bool Draw(Rect rect, UIColorPaletteDef palette, Color hue)
        {
            List<UIRailElement> elements = new List<UIRailElement>();

            elements.Add(new UIRailSectionHeaderControl("Maps")
            {
                Uppercase = true,
                Face = PawnFaces.Mono,
                Points = PawnFaces.Size.Caption,
                Color = palette.TextDisabled
            });

            for (int i = 0; i < Tallies.Count; i++)
                elements.Add(EntryFor(Tallies[i], palette, hue));

            // Worth its own line only when there is more than one map to add up. The divider says the entry
            // below is a different kind of thing from the maps above it.
            if (Tallies.Count > 1)
            {
                elements.Add(new UIRailDividerControl());

                int everyone = 0;

                for (int i = 0; i < Tallies.Count; i++)
                    everyone += Tallies[i].People;

                elements.Add(new UIRailClickableEntry(AllMaps.ToString(), "All maps")
                {
                    Rise = 28f,
                    Face = PawnFaces.Condensed,
                    Points = PawnFaces.Size.RailName,
                    TextColor = Selected == AllMaps ? hue : (Color?) null,
                    Count = everyone,
                    CountFace = PawnFaces.Mono,
                    CountPoints = PawnFaces.Size.RailCount,
                    Tooltip = "Every map at once, grouped by map."
                });
            }

            string picked = UIRailControl.Draw(rect, elements, Selected.ToString(), ref scroll, ref dragging,
                ref dragOffset, palette);

            int chosen;

            if (picked == null || !int.TryParse(picked, out chosen) || chosen == Selected)
                return false;

            Selected = chosen;

            return true;
        }

        private static UIRailClickableEntry EntryFor(MapTally tally, UIColorPaletteDef palette, Color hue)
        {
            bool chosen = tally.Id == Selected;

            return new UIRailClickableEntry(tally.Id.ToString(), tally.Label)
            {
                Rise = 28f,
                Face = PawnFaces.Condensed,
                Points = PawnFaces.Size.RailName,
                TextColor = chosen ? hue : (Color?) null,
                Count = tally.People,
                CountFace = PawnFaces.Mono,
                CountPoints = PawnFaces.Size.RailCount,

                // The whole reason the rail earns its width. Somebody bleeding on your second map is
                // currently below a fold you may have collapsed, with nothing on screen to say so.
                TrailingGlyph = tally.NeedsCare > 0
                    ? Dot(tally.Urgent ? palette.Danger : palette.Warning)
                    : null,
                TrailingGlyphSize = 8f,

                Tooltip = TooltipFor(tally)
            };
        }

        /// <summary>
        /// A filled circle at the trailing edge, in a color of its own rather than the row's.
        ///
        /// The rail entry hands a glyph the label's color, which is right for a decoration and wrong for a
        /// state: this dot means somebody is hurt whether or not the row it sits on is the chosen one.
        /// </summary>
        private static System.Action<Rect, Color> Dot(Color color)
        {
            return (slot, ignored) =>
            {
                float size = Mathf.Min(slot.width, slot.height);
                Rect box = new Rect(slot.center.x - size / 2f, slot.center.y - size / 2f, size, size);

                if (UIShapes.Disc == null)
                {
                    Widgets.DrawBoxSolid(box, color);

                    return;
                }

                Color previous = GUI.color;

                GUI.color = color;

                GUI.DrawTexture(box, UIShapes.Disc);

                GUI.color = previous;
            };
        }

        private static string TooltipFor(MapTally tally)
        {
            string text = tally.Label + "\n" + Plural(tally.People, "person", "people");

            for (int i = 0; i < PawnCategories.All.Length; i++)
            {
                if (tally.ByCategory[i] > 0)
                    text += "\n" + tally.ByCategory[i] + " " + PawnCategories.Label(PawnCategories.All[i]);
            }

            if (tally.NeedsCare > 0)
                text += "\n\n" + Plural(tally.NeedsCare, "person needs", "people need") + " care.";

            return text;
        }

        private static string Plural(int count, string one, string many)
        {
            return count + " " + (count == 1 ? one : many);
        }
    }
}
