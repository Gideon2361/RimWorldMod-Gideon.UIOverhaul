using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Helpers;
using LudeonTK;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.DevTools
{
    /// <summary>
    /// Every bundled typeface beside RimWorld's own text, at every interface size.
    ///
    /// <b>This window is the survivor of the one that settled the font architecture.</b> Its ancestor drew the
    /// same sample through four renderers at once -- baked glyph loop, runtime-assembled font, OS-registered
    /// TTF and the AssetBundle -- and the bundle won: real Barlow at 181 pixels where the OS route drew
    /// substituted Arial at 243, with working bold and italic tags. The losing paths are deleted; what remains
    /// checks the winner and any face added later.
    ///
    /// The sample carries bold and italic tags on purpose, and the weight column draws the named weights,
    /// which come from their own files where the bundle has them rather than from synthesis.
    /// </summary>
    public class Dialog_FontSpike : Window
    {
        private static readonly UIFace[] Faces = BuildFaces();

        private static UIFace[] BuildFaces()
        {
            List<UIFace> faces = new List<UIFace>();

            foreach (UIFace face in (UIFace[]) System.Enum.GetValues(typeof(UIFace)))
            {
                if (face != UIFace.Game)
                    faces.Add(face);
            }

            return faces.ToArray();
        }

        /// <summary>
        /// Loads reaching well past anything a real screen draws, because vsync hides any reading that fits
        /// inside a frame.
        /// </summary>
        private static readonly int[] Loads = { 0, 500, 2000, 5000 };

        /// <summary>Ascenders, descenders, a cap, digits, a comma and both tags.</summary>
        private const string Sample = "Bulk goods trader, 1,240 silver <b>bold</b> <i>italic</i>";

        private GameFont size = GameFont.Small;
        private int load;
        private bool stressBundle = true;

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
            get { return new Vector2(1080f, 760f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (Event.current.type == EventType.Repaint)
                smoothed = Mathf.Lerp(smoothed, Time.unscaledDeltaTime * 1000f, 0.05f);

            float y = inRect.y;

            y = Controls(inRect, y);
            y = Comparison(inRect, y);

            Stress(new Rect(inRect.x, y, inRect.width, inRect.yMax - y));
        }

        private float Controls(Rect inRect, float y)
        {
            Text.Font = GameFont.Small;

            Rect row = new Rect(inRect.x, y, inRect.width, 30f);
            float x = row.x;

            x = Toggle(row, x, "Tiny", size == GameFont.Tiny, () => size = GameFont.Tiny);
            x = Toggle(row, x, "Small", size == GameFont.Small, () => size = GameFont.Small);
            x = Toggle(row, x, "Medium", size == GameFont.Medium, () => size = GameFont.Medium);

            x += 20f;

            for (int i = 0; i < Loads.Length; i++)
            {
                int count = Loads[i];

                x = Toggle(row, x, count + " labels", load == count, () => load = count);
            }

            x += 20f;

            x = Toggle(row, x, "vanilla", !stressBundle, () => stressBundle = false);

            Toggle(row, x, "bundle", stressBundle, () => stressBundle = true);

            return y + 36f;
        }

        private static float Toggle(Rect row, float x, string label, bool on, System.Action pick)
        {
            float width = Text.CalcSize(label).x + 20f;
            Rect button = new Rect(x, row.y, width, row.height);

            if (Widgets.ButtonText(button, label))
                pick();

            if (on)
                Widgets.DrawBox(button, 2);

            return x + width + 6f;
        }

        /// <summary>
        /// Per face: RimWorld's text, the face itself, and the face at its named bold -- which is a real file
        /// for Barlow and IBM Plex and synthesis for the rest.
        /// </summary>
        private float Comparison(Rect inRect, float y)
        {
            float lineHeight = UIFonts.LineHeightOf(size);
            float cellWidth = Mathf.Min(330f, (inRect.width - 160f) / 3f);
            float cellHeight = lineHeight + 8f;

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 20f),
                "RimWorld / bundled face / bundled face asked for bold");

            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            y += 22f;

            for (int i = 0; i < Faces.Length; i++)
            {
                UIFace face = Faces[i];

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;

                for (int column = 0; column < 3; column++)
                {
                    Rect cell = new Rect(inRect.x + column * (cellWidth + 8f), y, cellWidth, cellHeight);

                    Widgets.DrawBoxSolid(cell, new Color(1f, 1f, 1f, 0.04f));
                    Widgets.DrawBox(cell);

                    Rect text = cell.ContractedBy(4f);

                    if (column == 0)
                        Vanilla(text);
                    else
                        UITextControl.LabelEllipses(text, Sample, face, size,
                            column == 2 ? FontStyle.Bold : FontStyle.Normal);
                }

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = new Color(1f, 1f, 1f, 0.6f);

                Widgets.Label(new Rect(inRect.x + 3f * (cellWidth + 8f) + 4f, y, 150f, cellHeight),
                    UIFaces.Named(face) + (UIFaces.Available(face) ? "" : "  (missing)"));

                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;

                y += cellHeight + 4f;
            }

            return y + 8f;
        }

        private void Vanilla(Rect rect)
        {
            GameFont previous = Text.Font;

            Text.Font = size;

            Widgets.LabelEllipses(rect, Sample);

            Text.Font = previous;
            Text.Anchor = TextAnchor.MiddleLeft;
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
                    stressBundle ? "bundle" : "RimWorld"));

            Rect field = new Rect(rect.x, rect.y + 28f, rect.width, Mathf.Max(0f, rect.height - 28f));

            if (load == 0)
                return;

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

                    if (stressBundle)
                    {
                        UITextControl.Label(cell, text, UIFace.BarlowCondensed, GameFont.Tiny);
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
