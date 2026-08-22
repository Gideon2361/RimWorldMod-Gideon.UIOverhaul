using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Study
{
    /// <summary>
    /// Who studies this entity.
    ///
    /// <b>A window rather than a float menu,</b> the same reasoning as the animal master picker: the question a
    /// player has here is "who is any good at this", and a menu of bare names cannot answer it. Every row carries
    /// the colonist's Intellectual skill, which is what the study speed is read from, and says outright when
    /// somebody is not going to do the work because research is switched off for them.
    ///
    /// <b>Anyone is a row rather than a Cancel button.</b> Clearing the assignment is a choice with the same
    /// standing as picking a person, and it is the one most likely to be wanted second.
    ///
    /// <b>Everybody is offered, capable or not.</b> A colonist with research disabled is shown greyed with the
    /// reason rather than left out: their absence from a list of eight colonists reads as a bug, and the fix for
    /// it is on the Work tab rather than here.
    /// </summary>
    internal class Dialog_PickStudier : Window
    {
        private const float HeaderHeight = 30f;
        private const float SearchHeight = 26f;
        private const float RowHeight = 30f;
        private const float FooterHeight = 34f;
        private const float Pad = 8f;
        private const float SkillWidth = 96f;

        private readonly Thing entity;
        private readonly Pawn assigned;

        private readonly UITextBoxControl search = new UITextBoxControl
        {
            Placeholder = "Search colonists",
            Icon = TexButton.Search,
            MaxLength = 40
        };

        private Vector2 scroll;

        private readonly List<Pawn> candidates = new List<Pawn>();

        private Dialog_PickStudier(Thing target, Pawn current)
        {
            entity = target;
            assigned = current;

            doCloseX = false;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = false;
        }

        internal static void For(Thing target, Pawn current)
        {
            if (target == null)
                return;

            Find.WindowStack.Add(new Dialog_PickStudier(target, current));
        }

        public override Vector2 InitialSize => new Vector2(320f, 420f);

        /// <summary>Opened at the cursor, clamped to the screen, like the menu it stands in for.</summary>
        protected override void SetInitialSizeAndPosition()
        {
            Vector2 size = InitialSize;
            Vector2 mouse = UI.MousePositionOnUIInverted;

            windowRect = new Rect(Mathf.Clamp(mouse.x - size.x * 0.5f, 0f, UI.screenWidth - size.x),
                Mathf.Clamp(mouse.y - 12f, 0f, UI.screenHeight - size.y), size.x, size.y);
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Study.PickStudier", inRect, () => Contents(inRect),
                "This list failed to draw. Nothing has been assigned.");
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

                // Studies and suppresses, both: one assignment covers the two jobs an entity gives out, and the
                // header says so rather than leaving the suppression half to be discovered.
                Widgets.LabelEllipses(new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight),
                    "Who handles " + entity.LabelShortCap);

                float y = inRect.y + HeaderHeight;

                search.Draw(new Rect(inRect.x, y, inRect.width, SearchHeight), palette);

                y += SearchHeight + Pad;

                Rows(new Rect(inRect.x, y, inRect.width, Mathf.Max(RowHeight, inRect.yMax - y - FooterHeight)),
                    palette);

                Anyone(new Rect(inRect.x, inRect.yMax - FooterHeight + Pad, inRect.width, FooterHeight - Pad),
                    palette);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private void Rows(Rect rect, UIColorPaletteDef palette)
        {
            Gather();

            Rect view = new Rect(0f, 0f, rect.width - 18f, candidates.Count * RowHeight + 4f);

            Widgets.BeginScrollView(rect, ref scroll, view);

            if (candidates.Count == 0)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(4f, 4f, view.width - 8f, 40f),
                    search.IsEmpty ? "Nobody here to assign." : "Nobody by that name.");

                Text.Font = GameFont.Small;
            }

            for (int i = 0; i < candidates.Count; i++)
                Row(new Rect(0f, i * RowHeight, view.width, RowHeight - 2f), candidates[i], palette);

            Widgets.EndScrollView();
        }

        private void Row(Rect row, Pawn colonist, UIColorPaletteDef palette)
        {
            bool selected = colonist == assigned;
            bool over = Mouse.IsOver(row);
            bool capable = Capable(colonist);

            if (selected)
                UIElementPainter.OutlineRounded(row, palette.Accent, palette.SurfaceRaised);
            else if (over)
                UIElementPainter.FillRounded(row, palette.SurfaceRaised);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = !capable ? palette.TextDisabled : over || selected
                ? palette.TextPrimary
                : palette.TextSecondary;

            Widgets.LabelEllipses(new Rect(row.x + 8f, row.y, Mathf.Max(0f, row.width - SkillWidth - 12f),
                row.height), colonist.LabelShortCap);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = capable ? palette.TextDisabled : palette.Warning;

            bool previousWrap = Text.WordWrap;
            Text.WordWrap = false;

            Widgets.Label(new Rect(row.xMax - SkillWidth - 6f, row.y, SkillWidth, row.height), Standing(colonist));

            Text.WordWrap = previousWrap;

            // Offered either way. Assigning somebody whose research is switched off is a legitimate thing to do
            // ahead of switching it back on, and refusing the click would leave the reason unexplained.
            if (!Widgets.ButtonInvisible(row))
                return;

            StudyAssignments.Assign(entity, colonist);

            SoundDefOf.Click.PlayOneShotOnCamera();

            Close();
        }

        /// <summary>
        /// The row that lets anybody study it again.
        ///
        /// At the bottom, where the animal master picker puts No master, so the two windows read alike.
        /// </summary>
        private void Anyone(Rect rect, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(rect);
            bool none = assigned == null;

            UIElementPainter.OutlineRounded(rect, none ? palette.Accent : over ? palette.TextSecondary
                : palette.Border, over ? palette.SurfaceRaised : palette.PanelBackground);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = over || none ? palette.TextPrimary : palette.TextSecondary;

            Widgets.Label(rect, "Anyone");

            if (!Widgets.ButtonInvisible(rect))
                return;

            StudyAssignments.Assign(entity, null);

            SoundDefOf.Click.PlayOneShotOnCamera();

            Close();
        }

        /// <summary>
        /// The colony's own, best researcher first.
        ///
        /// <b>Sorted by Intellectual rather than by name,</b> because that is the stat the study speed is read
        /// from and therefore the thing being chosen on. The search box covers looking for one person.
        /// </summary>
        private void Gather()
        {
            candidates.Clear();

            List<Pawn> colonists = entity?.Map?.mapPawns?.FreeColonists;

            if (colonists == null)
                return;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn colonist = colonists[i];

                if (colonist == null || !search.Matches(colonist.LabelShortCap))
                    continue;

                candidates.Add(colonist);
            }

            candidates.SortByDescending(Skill);
        }

        /// <summary>
        /// Whether this colonist will actually go and do it.
        ///
        /// Research being switched off for them is the case that comes up, and it is a Work tab setting rather
        /// than anything about the pawn, so it is worth naming on the row instead of hiding them.
        /// </summary>
        private static bool Capable(Pawn colonist)
        {
            if (colonist?.workSettings == null || colonist.WorkTypeIsDisabled(WorkTypeDefOf.Research))
                return false;

            return colonist.workSettings.WorkIsActive(WorkTypeDefOf.Research);
        }

        private static int Skill(Pawn colonist)
        {
            SkillRecord record = colonist?.skills?.GetSkill(SkillDefOf.Intellectual);

            return record == null || record.TotallyDisabled ? -1 : record.Level;
        }

        private static string Standing(Pawn colonist)
        {
            if (colonist.WorkTypeIsDisabled(WorkTypeDefOf.Research))
                return "cannot research";

            if (!Capable(colonist))
                return "research off";

            int level = Skill(colonist);

            return level < 0 ? "no skill" : "intellectual " + level;
        }
    }
}
