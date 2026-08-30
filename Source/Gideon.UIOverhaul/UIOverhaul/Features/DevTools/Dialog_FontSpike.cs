using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Helpers;
using LudeonTK;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.DevTools
{
    /// <summary>
    /// Draws the same text three ways at once, to settle whether a runtime built font works in RimWorld.
    ///
    /// <b>The question this exists to answer.</b> <see cref="UITextControl"/> costs one draw call per character
    /// because a texture draw is one quad. <see cref="UIRuntimeFont"/> claims a whole label can be one call by
    /// pouring the same baked sheet into a <c>UnityEngine.Font</c> and letting IMGUI build the mesh. Every API
    /// that needs is confirmed to exist and to have a setter. Whether Unity's text mesh generator honours glyph
    /// metrics handed to a font with no native typeface behind it is not something the assembly can be read for.
    /// It has to be looked at.
    ///
    /// <b>Three columns, one rect each, deliberately the same size.</b> RimWorld's own text, then ours drawn
    /// glyph by glyph, then ours drawn as a font. Anything the third column gets wrong shows up as a difference
    /// from the second, which is known to be correct because it is already on screen in the trade window. A
    /// blank third column means the font built and drew nothing; text sitting too high or too low means
    /// <c>lineHeight</c> came back zero and vertical placement has to be ours rather than Unity's.
    ///
    /// <b>The stress rows are the actual point.</b> Whether the font route is worth having is a question about
    /// draw calls, and the honest way to ask it is to draw several hundred labels each way and watch the frame
    /// time. The readout is smoothed and unscaled, so game speed does not enter into it.
    ///
    /// Developer tool. It ships in the assembly because it costs nothing until opened, and because the next
    /// face added is the next time somebody wants exactly this window.
    /// </summary>
    public class Dialog_FontSpike : Window
    {
        /// <summary>
        /// Every face the registry offers, generated rather than listed.
        ///
        /// So a face added to <see cref="UIFace"/> appears here without this window being touched, which is the
        /// point of the enum being the registry. <see cref="UIFace.Game"/> is skipped: it is already the first
        /// of the three columns on every row.
        /// </summary>
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
        /// Loads to measure at, reaching well past anything a real screen draws.
        ///
        /// The top of the range exists because vsync hides the answer: at sixty frames a second there are 16.67
        /// milliseconds to spend, and a load that fits inside them reports 16.67 whatever it cost. The reading
        /// only means something once something is missing the frame.
        /// </summary>
        private static readonly int[] Loads = { 0, 500, 2000, 5000 };

        /// <summary>Ascenders, descenders, a cap, digits and a comma: everything the metrics have to place.</summary>
        private const string Sample = "Bulk goods trader, 1,240 silver";

        private GameFont size = GameFont.Small;
        private int load;
        private int path = 2;

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
            get { return new Vector2(1120f, 900f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (Event.current.type == EventType.Repaint)
            {
                // Lerped rather than averaged over a window of samples, because the interesting reading is the
                // one taken while a load is held rather than the one across the moment it changed.
                smoothed = Mathf.Lerp(smoothed, Time.unscaledDeltaTime * 1000f, 0.05f);
            }

            float y = inRect.y;

            y = Controls(inRect, y);
            y = Diagnostics(inRect, y);
            y = Comparison(inRect, y);

            Stress(new Rect(inRect.x, y, inRect.width, inRect.yMax - y));
        }

        /// <summary>The size, the load and which path the load is drawn with.</summary>
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

            x = Toggle(row, x, "vanilla", path == 0, () => path = 0);
            x = Toggle(row, x, "glyphs", path == 1, () => path = 1);

            x = Toggle(row, x, "font", path == 2, () => path = 2);

            Toggle(row, x, "ttf", path == 3, () => path = 3);

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

        /// <summary>What each font actually came out as. Drawn in RimWorld's font, so it reads whatever happens.</summary>
        private float Diagnostics(Rect inRect, float y)
        {
            Text.Font = GameFont.Tiny;

            float height = UIFonts.LineHeightOf(GameFont.Tiny);

            for (int i = 0; i < Faces.Length; i++)
            {
                Widgets.Label(new Rect(inRect.x, y, inRect.width, height),
                    UIRuntimeFont.Diagnose(Faces[i], size));

                y += height + 1f;
            }

            Text.Font = GameFont.Small;

            return y + 8f;
        }

        /// <summary>
        /// The same string three ways, in three rects of the same size.
        ///
        /// The boxes are drawn whether or not anything lands inside them, which is the whole point: an empty
        /// bordered cell is a legible answer and an empty region of window is not.
        /// </summary>
        private float Comparison(Rect inRect, float y)
        {
            float lineHeight = UIFonts.LineHeightOf(size);
            float cellWidth = Mathf.Min(300f, (inRect.width - 20f) / 3f);
            float cellHeight = lineHeight + 8f;

            y = Heading(inRect, y, "RimWorld / glyph by glyph / runtime font");

            for (int i = 0; i < Faces.Length; i++)
            {
                UIFace face = Faces[i];

                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                for (int column = 0; column < 3; column++)
                {
                    Rect cell = new Rect(inRect.x + column * (cellWidth + 8f), y, cellWidth, cellHeight);

                    Widgets.DrawBoxSolid(cell, new Color(1f, 1f, 1f, 0.04f));
                    Widgets.DrawBox(cell);

                    Rect text = cell.ContractedBy(4f);

                    UIGuard.Try("FontSpike.Cell", () => Cell(text, column, face));
                }

                Text.Font = GameFont.Tiny;
                GUI.color = new Color(1f, 1f, 1f, 0.6f);

                Widgets.Label(new Rect(inRect.x + 3f * (cellWidth + 8f) + 4f, y, 220f, cellHeight),
                    UIFaces.Named(face));

                GUI.color = Color.white;
                Text.Font = GameFont.Small;

                y += cellHeight + 4f;
            }

            return Ttf(inRect, Widths(inRect, y)) + 10f;
        }

        /// <summary>
        /// The same sample through a TTF loaded straight off disk, which is the vanilla mechanism itself.
        ///
        /// <b>This section exists to settle one question:</b> <c>Font(string)</c> routes a path to
        /// <c>Internal_CreateFontFromPath</c> in RimWorld's own player build, and if the native side honours it,
        /// a shipped TTF becomes a live dynamic font -- FreeType rasterizing hinted glyphs at any size, exactly
        /// as the game's own text does. That would supersede the entire baked-atlas pipeline: no sheets, no
        /// baker, no per-size bakes, no rounding arithmetic. The sample carries bold and italic tags because a
        /// dynamic font is supposed to honour them on its own.
        ///
        /// <b>What each failure looks like.</b> "Did not load" means the native call is a stub in the player and
        /// the atlas pipeline stays. Text at the wrong size means the fontSize calibration is ours to do. Blurry
        /// or wavy text here would mean FreeType is not being asked at native size -- unlikely, since hinted
        /// per-size rasterization is the whole point of a dynamic font.
        /// </summary>
        private float Ttf(Rect inRect, float y)
        {
            Font font = UIDynamicFont.FromFile("BarlowCondensed-Regular", "Barlow Condensed");

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);

            string diag;

            if (font == null)
            {
                diag = "TTF via OS registration: did not load. AddFontResourceEx or the OS font lookup failed.";
            }
            else
            {
                // <b>Substitution is the silent failure this line exists to catch.</b> Asking the OS route for
                // a family the engine's font list never heard of does not fail -- it hands back a default face,
                // and HasCharacter('A') cannot tell the two apart because every face has an A. Two things can:
                // Barlow Condensed has no Cyrillic, so a true answer for that letter means this is not Barlow;
                // and the sample's measured width against the baked sheets' known width -- equal means the real
                // face, half again wider means the OS substituted something.
                float ttfWidth = TtfStyle(font, GameFont.Small).CalcSize(new GUIContent(Sample)).x;
                float bakedWidth = UITextControl.Width(Sample, UIFace.BarlowCondensed, GameFont.Small);

                diag = string.Format(
                    "TTF via OS registration: dynamic={0} lineHeight={1} ascent={2} hasA={3} "
                    + "cyrillic={4} (must be False for real Barlow) osList={5}   sample at Small: ttf {6:0}px "
                    + "vs baked Barlow {7:0}px (equal = real face, wider = substituted)",
                    font.dynamic, font.lineHeight, font.ascent, font.HasCharacter('A'),
                    font.HasCharacter((char) 0x042F), UIDynamicFont.OsListContains("Barlow Condensed"),
                    ttfWidth, bakedWidth);
            }

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 18f), diag);

            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            y += 20f;

            if (font == null)
                return y;

            float x = inRect.x;
            float tallest = 0f;

            foreach (GameFont each in new[] { GameFont.Tiny, GameFont.Small, GameFont.Medium })
            {
                float line = UIFonts.LineHeightOf(each);
                Rect cell = new Rect(x, y, 300f, line + 8f);

                Widgets.DrawBox(cell);

                GUI.Label(cell.ContractedBy(4f), Sample + " <b>bold</b> <i>italic</i>", TtfStyle(font, each));

                tallest = Mathf.Max(tallest, cell.height);
                x += 308f;
            }

            return y + tallest + 4f;
        }

        private static readonly Dictionary<int, GUIStyle> TtfStyles = new Dictionary<int, GUIStyle>();

        /// <summary>
        /// A style asking the dynamic font for one size.
        ///
        /// <b><c>fontSize</c> is the entire mechanism.</b> A dynamic font rasterizes at whatever size the style
        /// requests, so this is where the atlas pipeline's whole per-size bake collapses into one integer. The
        /// 1.2 divisor is Barlow's line ratio, hard coded because this is a spike; the real version would read
        /// it from the face.
        /// </summary>
        private static GUIStyle TtfStyle(Font font, GameFont size)
        {
            GUIStyle style;

            if (TtfStyles.TryGetValue((int) size, out style))
                return style;

            style = new GUIStyle
            {
                font = font,
                fontSize = Mathf.RoundToInt(UIFonts.LineHeightOf(size) / 1.2f),
                alignment = TextAnchor.MiddleLeft,
                richText = true,
                wordWrap = false,
                clipping = TextClipping.Clip
            };

            style.normal.textColor = Color.white;

            TtfStyles[(int) size] = style;

            return style;
        }

        /// <summary>
        /// The same string measured three ways.
        ///
        /// <b>Because "does it look the same size" is not a question two people can answer from a screenshot.</b>
        /// The glyph path and the font path are given the same metrics and should report the same width to
        /// within rounding; RimWorld's own is there as the size the layout was built against. A font column that
        /// draws at a different size from the glyph column shows up here as a number rather than as a doubt.
        /// </summary>
        private float Widths(Rect inRect, float y)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);

            float glyphs = UITextControl.Width(Sample, UIFace.BarlowCondensed, size);

            GUIStyle style = UIRuntimeFont.StyleFor(UIFace.BarlowCondensed, size, TextAnchor.UpperLeft, false);
            float font = style == null ? 0f : style.CalcSize(new GUIContent(Sample)).x;

            GameFont previous = Text.Font;

            Text.Font = size;

            float vanilla = Text.CalcSize(Sample).x;

            Text.Font = previous;

            // <b>The number the whole sharpness question turns on.</b> A sheet baked at 32 and drawn at some
            // fraction of it is resampled, and bilinear at a fraction is what makes stems land at different
            // weights. Whether that can be avoided depends on RimWorld's line height, which is computed at run
            // time from the loaded font and the UI scale -- so it cannot be read off any file and has to be
            // asked for here. Bake at the em this reports and the scale becomes 1, and nothing is resampled.
            UITypefaceAtlas atlas = UIFaces.AtlasFor(UIFace.BarlowCondensed);

            float line = UIFonts.LineHeightOf(size);
            float wantedEm = atlas != null && atlas.LineRatio > 0f ? line / atlas.LineRatio : 0f;
            float sheetEm = atlas == null ? 0f : atlas.Em;

            // <b>All three sizes at once, whichever is selected.</b> The bake size has to be decided per
            // GameFont -- a sheet is only 1:1 at one of them -- so reading them one at a time is three trips for
            // one decision.
            string ems = string.Empty;

            foreach (GameFont each in new[] { GameFont.Tiny, GameFont.Small, GameFont.Medium })
            {
                float height = UIFonts.LineHeightOf(each);
                float em = atlas != null && atlas.LineRatio > 0f ? height / atlas.LineRatio : 0f;

                ems += string.Format("   {0}: line {1:0.0} -> bake at {2:0.00}", each, height, em);
            }

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 20f),
                string.Format(
                    "width: RimWorld {0:0.0}  glyphs {1:0.0}  font {2:0.0}     sheet {3:0}, scale now {4:0.000}, "
                    + "UI scale {5}  |{6}",
                    vanilla, glyphs, font, sheetEm, sheetEm > 0f ? wantedEm / sheetEm : 0f, Prefs.UIScale, ems));

            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            return y + 22f;
        }

        private void Cell(Rect rect, int column, UIFace face)
        {
            if (column == 0)
            {
                GameFont previous = Text.Font;

                Text.Font = size;

                Widgets.Label(rect, Sample);

                Text.Font = previous;

                return;
            }

            if (column == 1)
            {
                UITextControl.Label(rect, Sample, face, size);

                return;
            }

            GUIStyle style = UIRuntimeFont.StyleFor(face, size, TextAnchor.UpperLeft, false);

            if (style != null)
                GUI.Label(rect, Sample, style);
        }

        /// <summary>
        /// Several hundred labels down one path, with the frame time beside them.
        ///
        /// Each label gets its own text, because a run of identical strings is the one case a future cache would
        /// make free and so is the one case that would not measure anything.
        /// </summary>
        private void Stress(Rect rect)
        {
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f),
                string.Format("{0:0.00} ms/frame   {1} labels   {2}", smoothed, load, PathName()));

            Rect field = new Rect(rect.x, rect.y + 28f, rect.width, Mathf.Max(0f, rect.height - 28f));

            if (load == 0)
                return;

            float lineHeight = UIFonts.LineHeightOf(GameFont.Tiny);
            float columnWidth = 190f;
            int rows = Mathf.Max(1, (int) (field.height / (lineHeight + 1f)));

            // <b>Every label asked for is drawn, wrapping back over the grid once it is full.</b> This used to
            // break out of the loop when it ran out of columns, so a reading taken at "500 labels" was really
            // taken at about two hundred -- the number on screen was the number requested and not the number
            // drawn, which is the one way a benchmark can lie without looking wrong. Overlapping draws cost
            // exactly what separate ones do, and cost is the whole question here.
            int columns = Mathf.Max(1, (int) (field.width / columnWidth));
            int slots = Mathf.Max(1, rows * columns);

            GUI.BeginGroup(field);

            Text.Anchor = TextAnchor.UpperLeft;

            UIGuard.Try("FontSpike.Stress", () =>
            {
                for (int i = 0; i < load; i++)
                {
                    int slot = i % slots;
                    int column = slot / rows;
                    int row = slot % rows;

                    Rect cell = new Rect(column * columnWidth, row * (lineHeight + 1f), columnWidth - 6f,
                        lineHeight);

                    string text = "Muffalo wool " + i;

                    if (path == 0)
                    {
                        GameFont previous = Text.Font;

                        Text.Font = GameFont.Tiny;

                        Widgets.Label(cell, text);

                        Text.Font = previous;
                    }
                    else if (path == 1)
                    {
                        UITextControl.Label(cell, text, UIFace.BarlowCondensed, GameFont.Tiny);
                    }
                    else if (path == 2)
                    {
                        GUIStyle style = UIRuntimeFont.StyleFor(UIFace.BarlowCondensed, GameFont.Tiny,
                            TextAnchor.UpperLeft, false);

                        if (style != null)
                            GUI.Label(cell, text, style);
                    }
                    else
                    {
                        Font ttf = UIDynamicFont.FromFile("BarlowCondensed-Regular", "Barlow Condensed");

                        if (ttf != null)
                            GUI.Label(cell, text, TtfStyle(ttf, GameFont.Tiny));
                    }
                }
            });

            GUI.EndGroup();

            Text.Font = GameFont.Small;
        }

        private string PathName()
        {
            return path == 0 ? "RimWorld" : path == 1 ? "glyph by glyph" : path == 2 ? "runtime font" : "ttf from disk";
        }

        private static float Heading(Rect inRect, float y, string text)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 20f), text);

            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            return y + 22f;
        }

        public override void PostClose()
        {
            base.PostClose();

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        /// <summary>
        /// Two entries rather than one, because the two game states cannot be asked for together.
        ///
        /// <c>IsAllowedInCurrentGameState</c> ands its flags rather than oring them, so Entry and Playing set at
        /// once means the action requires the program to be in both at the same time and it never appears.
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
