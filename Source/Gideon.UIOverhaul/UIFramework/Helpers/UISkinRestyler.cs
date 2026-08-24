using System;
using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// Restyles the shared GUIStyles that Unity and RimWorld draw text fields and scrollbars from.
    ///
    /// These two cannot be reached by patching a Widgets method, which is how everything else here is
    /// done. A text field's border is painted by its GUIStyle's background texture inside
    /// GUI.TextField, and a scrollbar is drawn by Unity inside GUI.BeginScrollView from
    /// GUI.skin.verticalScrollbar. Neither goes through any RimWorld code we could replace, so the styles
    /// themselves are what has to change.
    ///
    /// Reapplied whenever the active palette changes, and the textures are rebuilt with it. Doing it once
    /// at startup would leave both stuck on whichever theme was loaded first.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class UISkinRestyler
    {
        /// <summary>Slim scrollbars, matching the flat bar the growing-zone windows draw.</summary>
        private const float ScrollBarWidth = 8f;

        /// <summary>
        /// How strong the text selection highlight is.
        ///
        /// Enough to see which words are selected, faint enough to read them through. The palette's own selection
        /// wash is far fainter than this because it covers whole rows rather than sitting behind letters.
        /// </summary>
        private const float SelectionStrength = 0.45f;

        private static UIColorPaletteDef appliedFor;
        private static readonly List<Texture2D> Owned = new List<Texture2D>();

        /// <summary>
        /// Applies the theme to the shared styles if it has changed since the last call.
        ///
        /// Must run inside OnGUI: GUI.skin is only valid there. Cheap enough for every frame -- it is a
        /// reference comparison in the common case.
        /// </summary>
        public static void EnsureApplied()
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;
            if (palette == null || ReferenceEquals(palette, appliedFor))
                return;

            appliedFor = palette;

            try
            {
                Rebuild(palette);
            }
            catch (Exception ex)
            {
                // Not retried for this palette: appliedFor was already set above, so the next frame sees the
                // theme as applied. Changing the theme asks again, which is the right moment to retry.
                UIGuard.Report("Framework.RebuildSkin", ex,
                    "Text fields and scrollbars keep their vanilla look until the theme is changed.");
            }
        }

        private static void Rebuild(UIColorPaletteDef palette)
        {
            // The previous theme's textures are ours and nothing else references them once the styles are
            // repointed, so they are destroyed rather than left to accumulate on a theme switch.
            foreach (Texture2D texture in Owned)
            {
                if (texture != null)
                    UnityEngine.Object.Destroy(texture);
            }

            Owned.Clear();

            Texture2D track = Solid(palette.SurfaceSunken);
            Texture2D thumb = Solid(Fade(palette.TextSecondary, 0.45f));
            Texture2D thumbHover = Solid(Fade(palette.TextSecondary, 0.7f));

            RestyleTextFields(palette);
            RestyleScrollbars(track, thumb, thumbHover);
        }

        /// <summary>
        /// Every RimWorld text field style, so the chrome can be put on and taken off them together.
        ///
        /// The styles are private statics on Verse.Text, built once in its static constructor as clones of
        /// GUI.skin.textField. Being clones is what makes this safe: changing their backgrounds cannot affect
        /// the skin every other Unity control draws from.
        /// </summary>
        private static readonly List<GUIStyle> FieldStyles = new List<GUIStyle>();

        private static Texture2D fieldNormal;
        private static Texture2D fieldFocused;

        /// <summary>
        /// Gives RimWorld's text fields a themed fill and a one pixel border.
        ///
        /// <b>This used to strip the backgrounds instead, and that was a mistake with a long reach.</b> The
        /// reasoning was that a field's border should come from whoever draws the field -- which is true of this
        /// mod's own boxes, and false of every text field in the rest of the game. Vanilla's fields have no
        /// caller drawing chrome for them; their border <i>is</i> the style background. Clearing it left every
        /// search box, name field and number entry in RimWorld as text floating on the panel behind it.
        ///
        /// The background is a three by one texture with a one pixel border inset, so Unity's nine-slice keeps
        /// the edge exactly one pixel at any field size instead of scaling it.
        /// </summary>
        private static void RestyleTextFields(UIColorPaletteDef palette)
        {
            fieldNormal = Framed(palette.SurfaceSunken, palette.Border);
            fieldFocused = Framed(palette.SurfaceSunken, palette.BorderFocused);

            FieldStyles.Clear();

            foreach (string fieldName in new[] { "textFieldStyles", "textAreaStyles", "textAreaReadOnlyStyles" })
            {
                GUIStyle[] styles = AccessTools.StaticFieldRefAccess<GUIStyle[]>(typeof(Text), fieldName);
                if (styles == null)
                    continue;

                foreach (GUIStyle style in styles)
                {
                    if (style == null)
                        continue;

                    // One pixel from each edge is the border; the middle stretches. Without this the whole
                    // texture is scaled and the border thickens with the field.
                    style.border = new RectOffset(1, 1, 1, 1);

                    Level(style);

                    FieldStyles.Add(style);
                }
            }

            Selection(palette);

            SetFieldChrome(true);
        }

        /// <summary>
        /// Gives every state of a field style the text colour its normal state has.
        ///
        /// <b>This is a fault of ours, and the cause is one line up.</b> Unity only uses a style state when that
        /// state has a background: with no hover background a hovered field is drawn in the normal state, which
        /// is why vanilla never showed this. <see cref="SetFieldChrome"/> gives every state a background so the
        /// field keeps its border in all of them -- and that switched on a hover state whose <c>textColor</c> came
        /// from Unity's own skin and is near black. Reported 2026-08-23: the colony name field went unreadable
        /// under the pointer.
        ///
        /// <b>Copied from normal rather than set to the palette's text colour.</b> This mod's own text box draws
        /// its field with <c>GUI.color</c> already set to <c>TextPrimary</c>, and Unity multiplies the two, so
        /// naming a colour here would square it and give that box darker text than the rest of the game. Whatever
        /// normal is, every other state should match it.
        /// </summary>
        private static void Level(GUIStyle style)
        {
            Color color = style.normal.textColor;

            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.onNormal.textColor = color;
            style.onHover.textColor = color;
            style.onActive.textColor = color;
            style.onFocused.textColor = color;
        }

        /// <summary>
        /// The highlight behind selected text, in the palette's own selection blue.
        ///
        /// <b>Unity's default is an orange that belongs to no theme in this mod.</b> It is a skin setting rather
        /// than a style, so it cannot be reached by restyling the field: <c>GUI.skin.settings</c> is where the
        /// selection colour, the caret and double-click behaviour live, and RimWorld writes to that same object in
        /// <c>Verse.Text</c>'s static constructor. Safe to touch from here because this whole class runs inside
        /// OnGUI, which is the only place <c>GUI.skin</c> means anything.
        ///
        /// <b>The role's hue, at an alpha of this method's choosing.</b> <c>SelectionOverlay</c> is authored to
        /// wash over a whole row at fourteen per cent and is invisible behind three words of text, so what is
        /// borrowed is the colour and not the strength. Faded rather than solid, asked for on 2026-08-23, so the
        /// letters stay the thing being read.
        /// </summary>
        private static void Selection(UIColorPaletteDef palette)
        {
            GUI.skin.settings.selectionColor = Fade(palette.SelectionOverlay, SelectionStrength);
        }

        /// <summary>
        /// Turns the field chrome on or off for every style at once.
        ///
        /// <b>Off is for this mod's own text box,</b> which draws its own frame around a rect wider than the
        /// field itself -- the frame has to enclose the search icon and the clear button, which are outside the
        /// editable area. Letting the style draw as well would put a second border inside the first.
        /// </summary>
        internal static void SetFieldChrome(bool shown)
        {
            foreach (GUIStyle style in FieldStyles)
            {
                style.normal.background = shown ? fieldNormal : null;
                style.hover.background = shown ? fieldNormal : null;
                style.active.background = shown ? fieldNormal : null;
                style.onNormal.background = shown ? fieldNormal : null;
                style.onHover.background = shown ? fieldNormal : null;
                style.onActive.background = shown ? fieldNormal : null;

                // The focused pair takes the brighter edge the palette names for exactly this.
                style.focused.background = shown ? fieldFocused : null;
                style.onFocused.background = shown ? fieldFocused : null;
            }
        }

        /// <summary>
        /// A three by three texture: a one pixel ring of <paramref name="border"/> around a single
        /// <paramref name="fill"/> pixel, for nine-slicing.
        /// </summary>
        private static Texture2D Framed(Color fill, Color border)
        {
            Texture2D texture = new Texture2D(3, 3, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };

            for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
                texture.SetPixel(x, y, x == 1 && y == 1 ? fill : border);

            texture.Apply(false, true);
            Owned.Add(texture);

            return texture;
        }

        /// <summary>
        /// Repoints Unity's scrollbar styles at flat textures.
        ///
        /// Every Widgets.BeginScrollView ends in GUI.BeginScrollView, which draws its bars from
        /// GUI.skin.verticalScrollbar and friends, so this is what reaches all of them at once. The border
        /// and margin are zeroed as well: a 9-sliced scrollbar keeps its rounded caps otherwise, and a
        /// flat bar with rounded ends reads as a mistake.
        /// </summary>
        private static void RestyleScrollbars(Texture2D track, Texture2D thumb, Texture2D thumbHover)
        {
            Apply(GUI.skin.verticalScrollbar, track, track, track, ScrollBarWidth, true);
            Apply(GUI.skin.verticalScrollbarThumb, thumb, thumbHover, thumbHover, ScrollBarWidth, true);
            Apply(GUI.skin.horizontalScrollbar, track, track, track, ScrollBarWidth, false);
            Apply(GUI.skin.horizontalScrollbarThumb, thumb, thumbHover, thumbHover, ScrollBarWidth, false);

            // The little arrow buttons at each end. Blanked rather than restyled: they are a scrollbar
            // idiom RimWorld's own flat panels do not use, and leaving them textured would put vanilla
            // artwork at both ends of every themed bar.
            Blank(GUI.skin.verticalScrollbarUpButton);
            Blank(GUI.skin.verticalScrollbarDownButton);
            Blank(GUI.skin.horizontalScrollbarLeftButton);
            Blank(GUI.skin.horizontalScrollbarRightButton);
        }

        private static void Apply(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D active,
            float thickness, bool vertical)
        {
            if (style == null)
                return;

            style.normal.background = normal;
            style.hover.background = hover;
            style.active.background = active;
            style.focused.background = normal;
            style.onNormal.background = normal;
            style.onHover.background = hover;
            style.onActive.background = active;
            style.onFocused.background = normal;

            style.border = new RectOffset(0, 0, 0, 0);
            style.margin = new RectOffset(0, 0, 0, 0);
            style.padding = new RectOffset(0, 0, 0, 0);
            style.overflow = new RectOffset(0, 0, 0, 0);

            if (vertical)
                style.fixedWidth = thickness;
            else
                style.fixedHeight = thickness;
        }

        private static void Blank(GUIStyle style)
        {
            if (style == null)
                return;

            style.normal.background = null;
            style.hover.background = null;
            style.active.background = null;
            style.fixedWidth = 0f;
            style.fixedHeight = 0f;
            style.border = new RectOffset(0, 0, 0, 0);
            style.margin = new RectOffset(0, 0, 0, 0);
        }

        private static Texture2D Solid(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "Gideon_UIOverhaul_Solid",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };

            texture.SetPixel(0, 0, color);
            texture.Apply(false, false);

            Owned.Add(texture);
            return texture;
        }

        private static Color Fade(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }
    }

    /// <summary>
    /// Drives <see cref="UISkinRestyler"/> from the start of each OnGUI.
    ///
    /// StartOfOnGUI is RimWorld's own "a GUI pass is beginning" hook, which is exactly the context
    /// GUI.skin requires and where doing this cannot land midway through drawing a control.
    /// </summary>
    [HarmonyPatch(typeof(Text), nameof(Text.StartOfOnGUI))]
    public static class Patch_Text_StartOfOnGUI
    {
        /// <summary>
        /// Guarded even though EnsureApplied catches its own rebuild failures, because reading the active palette
        /// to decide whether a rebuild is needed happens before that catch. This is the earliest thing in a GUI
        /// pass and it runs several times a frame, so an escape from here would arrive before anything had been
        /// drawn -- with nothing on screen to suggest where it came from.
        /// </summary>
        public static void Postfix()
        {
            UIGuard.Try("Framework.RestyleSkin", UISkinRestyler.EnsureApplied);
        }
    }
}
