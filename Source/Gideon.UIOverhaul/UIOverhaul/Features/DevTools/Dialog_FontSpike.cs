using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using LudeonTK;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.DevTools
{
    /// <summary>
    /// One bundled typeface at a time, chosen from a rail that previews each face in itself.
    ///
    /// <b>This replaced a grid that drew every face at once.</b> That worked at four typefaces and had become
    /// unreadable at ten, with each new family stealing rows from the one before it. Picking a face and giving
    /// it the whole pane scales to as many as the bundle ever carries, and the rail stays useful because every
    /// entry is drawn in the face it names -- browsing the list is itself the comparison.
    ///
    /// <b>The rail is the preview; the pane is the test.</b> The rail answers "which face do I want", so it
    /// only needs each name in its own letters. The pane answers "does this face actually work", which takes
    /// RimWorld's text beside it, all four styles, and every interface size at once.
    /// </summary>
    public class Dialog_FontSpike : Window
    {
        private static readonly UIFace[] Faces = BuildFaces();

        private static UIFace[] BuildFaces()
        {
            List<UIFace> faces = new List<UIFace>();

            foreach (UIFace face in (UIFace[]) Enum.GetValues(typeof(UIFace)))
            {
                if (face != UIFace.Game)
                    faces.Add(face);
            }

            return faces.ToArray();
        }

        /// <summary>Ascenders, descenders, a cap, digits, a comma and both tags.</summary>
        private const string Sample = "Bulk goods trader, 1,240 silver <b>bold</b> <i>italic</i>";

        /// <summary>
        /// Loads reaching well past anything a real screen draws, because vsync hides any reading that fits
        /// inside a frame.
        /// </summary>
        private static readonly int[] Loads = { 0, 500, 2000, 5000 };

        private static readonly GameFont[] Sizes = { GameFont.Tiny, GameFont.Small, GameFont.Medium };

        private const float RailWidth = 210f;

        private UIFace selected = Faces.Length > 0 ? Faces[0] : UIFace.Game;
        private GameFont size = GameFont.Small;
        private int load;
        private bool stressFace = true;

        private Vector2 railScroll;
        private float smoothed;

        public Dialog_FontSpike()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            preventCameraMotion = false;
            absorbInputAroundWindow = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(1080f, 720f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (Event.current.type == EventType.Repaint)
                smoothed = Mathf.Lerp(smoothed, Time.unscaledDeltaTime * 1000f, 0.05f);

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            float y = Controls(inRect, palette);

            // The stress field is measured from the bottom so the rail and the detail pane share whatever is
            // left, rather than the field moving when a face changes the pane's height.
            float stressHeight = load == 0 ? 28f : Mathf.Max(120f, (inRect.height - y) * 0.42f);
            float bodyHeight = inRect.yMax - y - stressHeight - 8f;

            Rect rail = new Rect(inRect.x, y, RailWidth, bodyHeight);
            Rect detail = new Rect(rail.xMax + 10f, y, inRect.width - RailWidth - 10f, bodyHeight);

            Rail(rail, palette);
            Detail(detail, palette);
            Stress(new Rect(inRect.x, detail.yMax + 8f, inRect.width, stressHeight));

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private float Controls(Rect inRect, UIColorPaletteDef palette)
        {
            Text.Font = GameFont.Small;

            Rect row = new Rect(inRect.x, inRect.y, inRect.width, 30f);
            float x = row.x;

            for (int i = 0; i < Sizes.Length; i++)
            {
                GameFont candidate = Sizes[i];

                x = Toggle(row, x, candidate.ToString(), size == candidate, palette,
                    () => size = candidate);
            }

            x += 18f;

            for (int i = 0; i < Loads.Length; i++)
            {
                int count = Loads[i];

                x = Toggle(row, x, count == 0 ? "no load" : count + " labels", load == count, palette,
                    () => load = count);
            }

            x += 18f;

            x = Toggle(row, x, "stress: RimWorld", !stressFace, palette, () => stressFace = false);

            Toggle(row, x, "stress: face", stressFace, palette, () => stressFace = true);

            return row.yMax + 8f;
        }

        private static float Toggle(Rect row, float x, string label, bool on, UIColorPaletteDef palette,
            Action pick)
        {
            float width = Text.CalcSize(label).x + 22f;
            Rect button = new Rect(x, row.y, width, row.height);

            if (UIActionButtonControl.Draw(button, label, palette, toggled: on))
                pick();

            return x + width + 6f;
        }

        /// <summary>
        /// Every face, each drawn in itself. A face the bundle does not carry is named in RimWorld's font and
        /// marked, because a row that silently fell back to the game font would look like a face that merely
        /// resembles it.
        /// </summary>
        private void Rail(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.SurfaceSunken);
            Widgets.DrawBox(rect);

            float rowHeight = UIFonts.LineHeightOf(GameFont.Medium) + 10f;
            Rect view = new Rect(0f, 0f, rect.width - 18f, Faces.Length * rowHeight);

            Widgets.BeginScrollView(rect.ContractedBy(1f), ref railScroll, view);

            float y = 0f;

            for (int i = 0; i < Faces.Length; i++)
            {
                UIFace face = Faces[i];
                bool available = UIFaces.Available(face);
                Rect row = new Rect(0f, y, view.width, rowHeight);

                if (face == selected)
                    Widgets.DrawBoxSolid(row, palette.Accent * new Color(1f, 1f, 1f, 0.22f));
                else if (Mouse.IsOver(row))
                    Widgets.DrawBoxSolid(row, palette.HoverOverlay);

                Rect circle = new Rect(row.x + 6f, row.y + (rowHeight - UIRadioButtonControl.ButtonSize) / 2f,
                    UIRadioButtonControl.ButtonSize, UIRadioButtonControl.ButtonSize);

                UIRadioButtonControl.DrawButton(circle, face == selected, palette);

                Rect label = new Rect(circle.xMax + 6f, row.y, row.width - circle.width - 18f, rowHeight);

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = available ? palette.TextPrimary : palette.TextDisabled;

                if (available)
                {
                    UITextControl.LabelEllipses(label, UIFaces.Named(face), face, GameFont.Medium);
                }
                else
                {
                    Text.Font = GameFont.Small;

                    Widgets.LabelEllipses(label, UIFaces.Named(face) + "  (missing)");
                }

                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;

                if (Widgets.ButtonInvisible(row))
                    selected = face;

                y += rowHeight;
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// The chosen face put through everything worth checking: RimWorld's text for reference, the four
        /// styles, and all three interface sizes.
        ///
        /// <b>Bold and italic are two questions, not one.</b> The tags inside the sample ask whether rich text
        /// still works; the style rows ask whether the face has a real file for that weight or is being
        /// synthesized. A family with drawn italics and one with slanted uprights look quite different here,
        /// which is the point.
        /// </summary>
        private void Detail(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.SurfaceRaised);
            Widgets.DrawBox(rect);

            Rect inner = rect.ContractedBy(8f);
            float y = inner.y;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = palette.TextPrimary;

            Widgets.Label(new Rect(inner.x, y, inner.width, 26f), UIFaces.Named(selected));

            y += 28f;

            if (!UIFaces.Available(selected))
            {
                GUI.color = palette.TextSecondary;

                Widgets.Label(new Rect(inner.x, y, inner.width, 44f),
                    "The bundle carries no file for this face, so nothing below would be it. Check the bake.");

                GUI.color = Color.white;

                return;
            }

            y = Row(inner, y, "RimWorld", palette, Vanilla);
            y = Row(inner, y, "regular", palette, r => Faced(r, FontStyle.Normal));
            y = Row(inner, y, "bold", palette, r => Faced(r, FontStyle.Bold));
            y = Row(inner, y, "italic", palette, r => Faced(r, FontStyle.Italic));
            y = Row(inner, y, "bold italic", palette, r => Faced(r, FontStyle.BoldAndItalic));

            y += 6f;

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(inner.x, y, inner.width, 18f), "every interface size");

            GUI.color = Color.white;

            y += 20f;

            for (int i = 0; i < Sizes.Length; i++)
            {
                float height = UIFonts.LineHeightOf(Sizes[i]) + 6f;
                Rect line = new Rect(inner.x, y, inner.width, height);

                Text.Anchor = TextAnchor.MiddleLeft;

                UITextControl.LabelEllipses(line, Sizes[i] + " -- " + Sample, selected, Sizes[i]);

                Text.Anchor = TextAnchor.UpperLeft;

                y += height + 2f;
            }
        }


        /// <summary>One labeled line: a caption in RimWorld's font, then whatever the caller draws beside it.</summary>
        private float Row(Rect inner, float y, string caption, UIColorPaletteDef palette, Action<Rect> draw)
        {
            float height = UIFonts.LineHeightOf(size) + 8f;


            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(inner.x, y, 92f, height), caption);

            GUI.color = palette.TextPrimary;

            draw(new Rect(inner.x + 96f, y, inner.width - 96f, height));

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            return y + height + 2f;
        }

        private void Faced(Rect rect, FontStyle weight)
        {
            Text.Anchor = TextAnchor.MiddleLeft;

            UITextControl.LabelEllipses(rect, Sample, selected, size, weight);
        }

        private void Vanilla(Rect rect)
        {
            GameFont previous = Text.Font;

            Text.Font = size;
            Text.Anchor = TextAnchor.MiddleLeft;

            Widgets.LabelEllipses(rect, Sample);

            Text.Font = previous;
        }

        /// <summary>
        /// Hundreds of labels down one path with the frame time beside them, each with its own text so no
        /// future cache can make the measurement free.
        /// </summary>
        private void Stress(Rect rect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f),
                string.Format("{0:0.00} ms/frame   {1} labels   {2}", smoothed, load,
                    stressFace ? UIFaces.Named(selected) : "RimWorld"));

            if (load == 0)
                return;

            Rect field = new Rect(rect.x, rect.y + 26f, rect.width, Mathf.Max(0f, rect.height - 26f));

            float lineHeight = UIFonts.LineHeightOf(GameFont.Tiny);
            float columnWidth = 190f;
            int rows = Mathf.Max(1, (int) (field.height / (lineHeight + 1f)));
            int columns = Mathf.Max(1, (int) (field.width / columnWidth));
            int slots = Mathf.Max(1, rows * columns);

            GUI.BeginGroup(field);

            UIGuard.Try("FontSpike.Stress", () =>
            {
                for (int i = 0; i < load; i++)
                {
                    int slot = i % slots;
                    Rect cell = new Rect(slot / rows * columnWidth, slot % rows * (lineHeight + 1f),
                        columnWidth - 6f, lineHeight);

                    string text = "Muffalo wool " + i;

                    if (stressFace)
                    {
                        UITextControl.Label(cell, text, selected, GameFont.Tiny);
                    }
                    else
                    {
                        GameFont previous = Text.Font;

                        Text.Font = GameFont.Tiny;

                        Widgets.Label(cell, text);

                        Text.Font = previous;
                    }
                }
            });

            GUI.EndGroup();
        }

        public override void PostClose()
        {
            base.PostClose();

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        /// <summary>
        /// Two entries because <c>IsAllowedInCurrentGameState</c> ands its flags: Entry and Playing set
        /// together would require both states at once and the action would never appear.
        /// </summary>
        [DebugAction("Gideon UI", "Font spike", allowedGameStates = AllowedGameStates.Entry)]
        private static void OpenFromMenu()
        {
            Open();
        }

        [DebugAction("Gideon UI", "Font spike", allowedGameStates = AllowedGameStates.Playing)]
        private static void OpenInGame()
        {
            Open();
        }

        private static void Open()
        {
            Find.WindowStack.Add(new Dialog_FontSpike());
        }
    }
}
