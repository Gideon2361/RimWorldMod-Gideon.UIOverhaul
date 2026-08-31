using Gideon.UIFramework.Components.Images;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIFramework.Stages;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// Draws a loading screen: background, stage, step and progress bar.
    ///
    /// Every part is a separate virtual method, so a mod wanting a different layout can override one
    /// of them and keep the rest. Set <see cref="UILoadingScreenConfig.drawerClass"/> to the subclass.
    ///
    /// Instances are created once per def and reused, so a subclass may hold cached state -- but not
    /// per-frame state that assumes the previous frame drew the same thing, because a def switch
    /// swaps the instance out.
    /// </summary>
    public class UILoadingScreenControl
    {
        protected const float Pad = 24f;
        protected const float BarMaxWidth = 720f;
        protected const float RowGap = 6f;

        /// <summary>How much taller the bar is than the step line beneath it.</summary>
        protected const float BarWeight = 4f;

        /// <summary>
        /// The console's prose face: the stage line, which is a sentence a player reads.
        ///
        /// <b>Source Sans 3 rather than the mockup's Segoe UI.</b> The mockup was drawn against a Windows
        /// system font, which is neither licensed for redistribution nor present on Linux or macOS, so every
        /// player off Windows would have been reading whatever their system chose to substitute. Source Sans
        /// 3 is Adobe's, under the OFL, and ships in the mod's own bundle, so the console reads the same
        /// everywhere.
        /// </summary>
        private const UIFace Sans = UIFace.SourceSans3;

        /// <summary>
        /// The console's figure face: the step line, which is a path rather than a sentence.
        ///
        /// The mockup sets paths, timings and footer figures in the mono and everything else in the sans. A
        /// step line is the first of those: "Loading defs: ThingDef_Wall" is a path being walked.
        ///
        /// <b>IBM Plex Mono, though the mockup named Cascadia.</b> Cascadia is a fine face and this was set
        /// in it for a day, but the ideoligion, quest and power tabs all count in Plex, and the mod having
        /// two monospaces means every screen has to be checked against the others to know which one it uses.
        /// One mono is worth more than the better of two.
        /// </summary>
        private const UIFace Mono = UIFace.IBMPlexMono;

        /// <summary>Point size of the stage line.</summary>
        protected const float StagePoints = 15f;

        /// <summary>Point size of the step line, taken from the mockup's own path rows.</summary>
        protected const float StepPoints = 10.5f;

        // Measured from the faces rather than fixed, because RimWorld's UI scale changes line heights
        // at runtime and any hardcoded number would simply be wrong about them. It also keeps the bar
        // proportionate to the two text rows it sits between, whatever those rows end up being.
        //
        // Properties, not constants: line heights are only valid inside OnGUI, and every caller here
        // is a drawing method.

        /// <summary>Row height for the stage line.</summary>
        protected static float StageHeight => Mathf.Ceil(UITextControl.LineHeight(Sans, StagePoints));

        /// <summary>Row height for the step line.</summary>
        protected static float StepHeight => Mathf.Ceil(UITextControl.LineHeight(Mono, StepPoints));

        /// <summary>Bar height.</summary>
        protected static float BarHeight => StepHeight + BarWeight;

        /// <summary>
        /// Line height of <paramref name="font"/>, falling back to Small where Tiny is unavailable.
        /// Some languages have no tiny font, and asking for one there otherwise yields a row too short
        /// for the text the game will actually draw in it.
        /// </summary>
        protected static float LineHeight(GameFont font)
        {
            if (font == GameFont.Tiny && !Text.TinyFontSupported)
                font = GameFont.Small;

            return Text.LineHeightOf(font);
        }

        /// <summary>
        /// Draws the whole screen into <paramref name="screen"/>, which is the full UI rect.
        /// </summary>
        public virtual void Draw(Rect screen, UILoadingScreenConfig config, UILoadingSnapshot progress,
            UIColorPaletteDef palette)
        {
            DrawBackground(screen, config, palette);
            DrawForeground(screen, config, progress, palette);
        }

        /// <summary>
        /// The backdrop. Falls back to a flat palette fill when no texture is configured or the
        /// texture has not loaded yet, so there is never a frame of raw black.
        /// </summary>
        protected virtual void DrawBackground(Rect screen, UILoadingScreenConfig config, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(screen, palette.WindowBackground);

            UIImage image = config.BackgroundImage;
            if (image.IsValid)
                DrawImage(screen, image.Texture, config.backgroundFit, config.BackgroundFlipVertical);

            Color? overlay = config.OverlayColor;
            if (overlay.HasValue)
                Widgets.DrawBoxSolid(screen, overlay.Value);
        }

        /// <summary>Stage, step and bar, laid out along the bottom of the screen over a panel.</summary>
        protected virtual void DrawForeground(Rect screen, UILoadingScreenConfig config, UILoadingSnapshot progress,
            UIColorPaletteDef palette)
        {
            // Read once and reused: each of these measures a font, and the layout below has to agree
            // with the height the panel was sized to.
            float stageHeight = StageHeight;
            float stepHeight = StepHeight;
            float barHeight = BarHeight;

            // Sized from what the screen is configured to show, never from whether those lines happen
            // to have text this frame. The stage and step both go briefly empty during a load, and a
            // panel measured from the text would resize underneath the bar every time they did.
            // Reserving the row keeps the panel still and the bar in one place; an empty line simply
            // leaves its row blank.
            float height = 0f;
            if (config.showStage)
                height += stageHeight;
            if (config.showProgressBar)
                height += (height > 0f ? RowGap : 0f) + barHeight;
            if (config.showStep)
                height += (height > 0f ? RowGap : 0f) + stepHeight;

            if (height <= 0f)
                return;

            float width = Mathf.Min(BarMaxWidth, screen.width - Pad * 4f);
            float x = screen.x + (screen.width - width) * 0.5f;
            Rect content = new Rect(x, screen.yMax - Pad * 2f - height, width, height);

            if (config.showPanel)
                DrawPanel(content.ExpandedBy(config.panelPadding), config, palette);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            // Stage above the bar, step below it. The stage is the stable line a player actually
            // reads; the step changes too fast to follow, so it goes where its own growing and
            // shrinking cannot shove the stage around.
            float y = content.y;

            if (config.showStage)
            {
                if (!progress.Stage.NullOrEmpty())
                {
                    Text.Anchor = TextAnchor.MiddleLeft;
                    GUI.color = palette.TextPrimary;

                    UITextControl.LabelEllipses(new Rect(x, y, width, stageHeight), progress.Stage, Sans,
                        StagePoints);
                }

                // Advanced whether or not anything was drawn, so the bar below does not slide up into
                // the gap on the frames where the stage has no text.
                y += stageHeight + RowGap;
            }

            if (config.showProgressBar)
            {
                UIProgressBarControl.Draw(new Rect(x, y, width, barHeight), progress.Fraction, palette);
                y += barHeight + RowGap;
            }

            if (config.showStep && !progress.Step.NullOrEmpty())
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextSecondary;

                // Ellipsed through the style that draws it rather than pre-truncated. Truncate measures in
                // whatever font Text.Font happens to hold, which is no longer the one this row is set in.
                UITextControl.LabelEllipses(new Rect(x, y, width, stepHeight), progress.Step, Mono,
                    StepPoints);
            }

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        /// <summary>
        /// The slab behind the text and bar.
        ///
        /// A backdrop is a photograph, and text over a photograph is legible only where the image
        /// happens to be dark -- which is a different part of the screen for every backdrop, and no
        /// part of it for some. A translucent panel makes that a property of the screen rather than of
        /// the art.
        /// </summary>
        protected virtual void DrawPanel(Rect rect, UILoadingScreenConfig config, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, config.PanelColor(palette));

            Color previous = GUI.color;
            GUI.color = palette.Border;
            Widgets.DrawBox(rect, 1);
            GUI.color = previous;
        }

        /// <summary>
        /// Draws <paramref name="texture"/> into <paramref name="rect"/> according to
        /// <paramref name="fit"/>. Unity's ScaleMode covers three of the five; the other two are
        /// worked out here.
        /// </summary>
        /// <param name="flipVertical">
        /// Mirrors the image top to bottom. Set for a texture whose rows are stored top-down, which
        /// is every DDS -- see <see cref="UIImage.FlipVertical"/>. Done with the GUI matrix rather
        /// than by rewriting pixels, because mirroring block-compressed data means unpacking and
        /// repacking every block, and this works for all five fits at no cost.
        /// </param>
        protected static void DrawImage(Rect rect, Texture2D texture, UIImageFit fit, bool flipVertical = false)
        {
            if (flipVertical)
            {
                Matrix4x4 saved = GUI.matrix;
                GUIUtility.ScaleAroundPivot(new Vector2(1f, -1f), rect.center);
                DrawImage(rect, texture, fit);
                GUI.matrix = saved;
                return;
            }

            switch (fit)
            {
                case UIImageFit.Cover:
                    GUI.DrawTexture(rect, texture, ScaleMode.ScaleAndCrop);
                    return;

                case UIImageFit.Contain:
                    GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
                    return;

                case UIImageFit.Stretch:
                    GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill);
                    return;

                case UIImageFit.Center:
                {
                    Rect native = new Rect(
                        rect.x + (rect.width - texture.width) * 0.5f,
                        rect.y + (rect.height - texture.height) * 0.5f,
                        texture.width,
                        texture.height);
                    GUI.DrawTexture(native, texture, ScaleMode.StretchToFill);
                    return;
                }

                case UIImageFit.Tile:
                {
                    // Tex coords are in units of the texture, so the repeat count is simply how many
                    // times the texture fits across the rect.
                    Rect coords = new Rect(0f, 0f, rect.width / texture.width, rect.height / texture.height);
                    GUI.DrawTextureWithTexCoords(rect, texture, coords);
                    return;
                }

                default:
                    GUI.DrawTexture(rect, texture, ScaleMode.ScaleAndCrop);
                    return;
            }
        }
    }
}
