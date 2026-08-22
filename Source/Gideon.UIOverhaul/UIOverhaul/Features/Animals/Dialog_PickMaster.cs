using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// Who is master to an animal, or to a whole species at once.
    ///
    /// <b>A window rather than the float menu this replaced.</b> Aaron's standing preference, and a master list is
    /// a good example of why: a float menu shows one candidate per line with nothing beside it, so the question a
    /// player actually has, which of my colonists is any good with animals, has to be answered somewhere else and
    /// carried back. Here the handler's Animals skill is on the row, the colonists who cannot take the job at all
    /// are left out rather than listed and refused, and the current master is marked.
    ///
    /// <b>Opened over the control that asked, like the menu it replaces.</b> A picker that jumps to the middle of
    /// the screen loses the row it belongs to, and this is opened from a chip on one animal's row among a dozen.
    ///
    /// <b>One window for one animal and for a species.</b> The member row passes one animal, the species menu
    /// passes the lot. Nothing about the list changes between those two cases except how many animals the answer
    /// is written to, so they share the window rather than each having their own.
    /// </summary>
    internal class Dialog_PickMaster : Window
    {
        private const float HeaderHeight = 30f;
        private const float SearchHeight = 26f;
        private const float RowHeight = 30f;
        private const float FooterHeight = 34f;
        private const float Pad = 8f;

        /// <summary>Where the skill readout sits on a row.</summary>
        private const float SkillWidth = 72f;

        private readonly List<Pawn> animals;
        private readonly Action changed;
        private readonly string heading;

        private readonly UITextBoxControl search = new UITextBoxControl
        {
            Placeholder = "Search colonists",
            Icon = TexButton.Search,
            MaxLength = 40
        };

        private Vector2 scroll;

        /// <summary>Rebuilt every draw. The list is short and the test behind it is not cacheable per frame.</summary>
        private readonly List<Pawn> candidates = new List<Pawn>();

        private Dialog_PickMaster(List<Pawn> targets, string title, Action onChanged)
        {
            animals = targets;
            heading = title;
            changed = onChanged;

            doCloseX = false;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = false;
            drawShadow = true;
        }

        /// <summary>Opens the picker for one animal.</summary>
        internal static void For(Pawn animal, Action changed)
        {
            if (animal == null)
                return;

            Find.WindowStack.Add(new Dialog_PickMaster(new List<Pawn> { animal },
                "Master for " + animal.LabelShortCap, changed));
        }

        /// <summary>
        /// Opens the picker for every animal handed over, which is how the species menu asks.
        ///
        /// The list is copied, because the caller's is the roster's own scratch and is rebuilt on a timer while
        /// this window is open. Without the copy the window would be writing to whatever the roster happened to
        /// be holding by the time somebody clicked.
        /// </summary>
        internal static void For(List<Pawn> members, Action changed)
        {
            if (members == null || members.Count == 0)
                return;

            string title = members.Count == 1 && members[0] != null
                ? "Master for " + members[0].LabelShortCap
                : "Master for " + members.Count + " animals";

            Find.WindowStack.Add(new Dialog_PickMaster(new List<Pawn>(members), title, changed));
        }

        public override Vector2 InitialSize => new Vector2(300f, 420f);

        /// <summary>
        /// Opened at the cursor, clamped so it cannot hang off an edge.
        ///
        /// The same placement rule a float menu follows, for the same reason: the window is an answer to a control
        /// that was just clicked, and it has to appear next to it.
        /// </summary>
        protected override void SetInitialSizeAndPosition()
        {
            Vector2 size = InitialSize;
            Vector2 mouse = UI.MousePositionOnUIInverted;

            float x = Mathf.Clamp(mouse.x - size.x * 0.5f, 0f, UI.screenWidth - size.x);
            float y = Mathf.Clamp(mouse.y - 12f, 0f, UI.screenHeight - size.y);

            windowRect = new Rect(x, y, size.x, size.y);
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Animals.PickMaster", inRect, () => Contents(inRect),
                "This master list failed to draw. Nothing has been assigned.");
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
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextPrimary;

                Widgets.LabelEllipses(new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight), heading);

                float y = inRect.y + HeaderHeight;

                search.Draw(new Rect(inRect.x, y, inRect.width, SearchHeight), palette);

                y += SearchHeight + Pad;

                Rect list = new Rect(inRect.x, y, inRect.width,
                    Mathf.Max(RowHeight, inRect.yMax - y - FooterHeight));

                Rows(list, palette);

                Clear(new Rect(inRect.x, inRect.yMax - FooterHeight + Pad, inRect.width, FooterHeight - Pad),
                    palette);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// The candidates, scrolled.
        ///
        /// <b>The whole row is the click,</b> which is what a list of one choice each should be: there is no
        /// second control on the row for a click to belong to.
        /// </summary>
        private void Rows(Rect rect, UIColorPaletteDef palette)
        {
            Gather();

            Rect view = new Rect(0f, 0f, rect.width - 18f, candidates.Count * RowHeight + 4f);

            Widgets.BeginScrollView(rect, ref scroll, view);

            if (candidates.Count == 0)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(4f, 4f, view.width - 8f, 40f), search.IsEmpty
                    ? "Nobody here can master this. A master needs obedience training on the animal and a handler "
                      + "who can manage it."
                    : "Nobody by that name.");
            }

            Pawn current = Current();

            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn colonist = candidates[i];
                Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight - 2f);

                Row(row, colonist, colonist == current, palette);
            }

            Widgets.EndScrollView();
        }

        private void Row(Rect row, Pawn colonist, bool selected, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(row);

            if (selected)
                UIElementPainter.OutlineRounded(row, palette.Accent, palette.SurfaceRaised);
            else if (over)
                UIElementPainter.FillRounded(row, palette.SurfaceRaised);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = selected ? palette.TextPrimary : over ? palette.TextPrimary : palette.TextSecondary;

            Widgets.LabelEllipses(new Rect(row.x + 8f, row.y, Mathf.Max(0f, row.width - SkillWidth - 12f),
                row.height), colonist.LabelShortCap);

            // The number somebody is actually choosing on, next to the name rather than a tooltip away. Vanilla's
            // menu made this the player's job to remember.
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = palette.TextDisabled;

            bool previousWrap = Text.WordWrap;
            Text.WordWrap = false;

            Widgets.Label(new Rect(row.xMax - SkillWidth - 6f, row.y, SkillWidth, row.height), SkillOf(colonist));

            Text.WordWrap = previousWrap;

            if (!Widgets.ButtonInvisible(row))
                return;

            Assign(colonist);
        }

        /// <summary>
        /// The row that takes the master away.
        ///
        /// At the bottom rather than the top of the list, which is where the destructive answer belongs: at the
        /// top it is what the cursor lands on when the window opens.
        /// </summary>
        private void Clear(Rect rect, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect);

            UIElementPainter.OutlineRounded(rect, over ? palette.Danger : palette.Border,
                over ? palette.SurfaceRaised : palette.PanelBackground);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = over ? palette.TextPrimary : palette.TextSecondary;

            Widgets.Label(rect, "No master");

            if (Widgets.ButtonInvisible(rect))
                Assign(null);
        }

        /// <summary>
        /// Writes the answer and closes.
        ///
        /// <b>Applied only where vanilla's own test allows it,</b> which can differ inside one species: a juvenile
        /// with no obedience training takes no master, and the handler has to be able to manage the animal. The
        /// window offers a colonist who can master at least one of the animals and then writes to the ones they
        /// can, which is the same rule the species menu used before this window existed.
        /// </summary>
        private void Assign(Pawn colonist)
        {
            for (int i = 0; i < animals.Count; i++)
            {
                Pawn animal = animals[i];

                if (animal?.playerSettings == null)
                    continue;

                if (colonist == null)
                {
                    animal.playerSettings.Master = null;

                    continue;
                }

                if (TrainableUtility.CanBeMaster(colonist, animal))
                    animal.playerSettings.Master = colonist;
            }

            changed?.Invoke();

            SoundDefOf.Click.PlayOneShotOnCamera();

            Close();
        }

        /// <summary>
        /// Everybody who could master at least one of these animals, best handler first.
        ///
        /// <b>Sorted by the Animals skill rather than by name,</b> because the list is being read to answer "who
        /// should do this" far more often than "where is Jeff", and the search box covers the second question.
        /// Incapable colonists are ordered last by the same rule without a special case, since the skill reads as
        /// zero for them.
        /// </summary>
        private void Gather()
        {
            candidates.Clear();

            for (int i = 0; i < animals.Count; i++)
            {
                Pawn animal = animals[i];
                Map map = animal?.MapHeld;

                if (map?.mapPawns == null)
                    continue;

                List<Pawn> colonists = map.mapPawns.FreeColonists;

                for (int c = 0; c < colonists.Count; c++)
                {
                    Pawn colonist = colonists[c];

                    if (colonist == null || candidates.Contains(colonist))
                        continue;

                    if (!search.Matches(colonist.LabelShortCap))
                        continue;

                    if (TrainableUtility.CanBeMaster(colonist, animal))
                        candidates.Add(colonist);
                }
            }

            candidates.SortByDescending(SkillLevel);
        }

        /// <summary>
        /// The master these animals share, or null when they disagree.
        ///
        /// A group with two masters between it has no answer to mark, and marking one of them would say the other
        /// half of the herd is set when it is not.
        /// </summary>
        private Pawn Current()
        {
            Pawn found = null;

            for (int i = 0; i < animals.Count; i++)
            {
                Pawn master = animals[i]?.playerSettings?.Master;

                if (master == null)
                    return null;

                if (found == null)
                    found = master;
                else if (found != master)
                    return null;
            }

            return found;
        }

        private static int SkillLevel(Pawn colonist)
        {
            SkillRecord record = colonist?.skills?.GetSkill(SkillDefOf.Animals);

            return record == null || record.TotallyDisabled ? 0 : record.Level;
        }

        private static string SkillOf(Pawn colonist)
        {
            SkillRecord record = colonist?.skills?.GetSkill(SkillDefOf.Animals);

            if (record == null || record.TotallyDisabled)
                return "incapable";

            return "animals " + record.Level;
        }
    }
}
