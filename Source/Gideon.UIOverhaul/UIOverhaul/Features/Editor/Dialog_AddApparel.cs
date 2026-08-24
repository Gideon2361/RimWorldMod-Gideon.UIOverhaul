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
    /// Browsing every piece of apparel in the game, and specifying the one being made.
    ///
    /// <b>Not <see cref="Dialog_PickFrom"/>, and that is the whole reason this exists.</b> That picker answers
    /// "which of these several hundred things", which is one click and done. Apparel is four more questions after
    /// the name -- material, quality, health, colour -- and a picker that closed on the first of them would mean
    /// choosing a duster and then having no way to say plasteel.
    ///
    /// <b>Two panes: the list on the left, what it will be on the right.</b> The right pane only appears once
    /// something is chosen, because every control on it is about a specific item and a pane full of dead controls
    /// is a worse first impression than an empty one.
    ///
    /// <b>The preview is the item's own icon in the colour it will be.</b> Cheap -- one tinted texture -- and it
    /// is the only way to tell what a dye actually looks like before committing to it.
    /// </summary>
    internal sealed class Dialog_AddApparel : Window
    {
        private const float HeaderHeight = 30f;

        /// <summary>A card is two lines of text and an icon, whichever of those is taller.</summary>
        private static float RowHeight
        {
            get
            {
                return Mathf.Max(EditorParts.IconSize + 10f,
                    UIFonts.LineHeightOf(GameFont.Small) + UIFonts.LineHeightOf(GameFont.Tiny) + 10f);
            }
        }

        private const float FooterHeight = 34f;

        private const float ListWidth = 286f;

        private const float Pad = 8f;

        private const float PreviewSize = 56f;

        /// <summary>Width of the button onto the full colour wheel, beside the dye swatches.</summary>
        private const float CustomWidth = 62f;

        private readonly EditorContext context;

        private readonly List<ThingDef> matching = new List<ThingDef>();

        private readonly UITextBoxControl search = new UITextBoxControl
        {
            Icon = TexButton.Search,
            Placeholder = "Search apparel",
            MaxLength = 40
        };

        private readonly ApparelChoice choice = new ApparelChoice();

        private string query = string.Empty;

        private Vector2 scroll;

        private Dialog_AddApparel(EditorContext context)
        {
            this.context = context;

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            draggable = true;
            drawShadow = true;
        }

        internal static void Open(EditorContext context)
        {
            if (context?.Pawn == null)
                return;

            if (EditorApparel.All().Count == 0)
            {
                EditorParts.Warn("There is no apparel in this install to choose from.");

                return;
            }

            Find.WindowStack.Add(new Dialog_AddApparel(context));
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(760f, Mathf.Min(600f, UI.screenHeight - 80f)); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Editor.AddApparel", inRect, () => Contents(inRect),
                "This window failed to draw. Nothing has been put on.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Medium;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, HeaderHeight), "Add apparel");

                Text.Font = GameFont.Small;

                float top = inRect.y + HeaderHeight + 4f;
                float bottom = inRect.yMax - FooterHeight - Pad;

                List(new Rect(inRect.x, top, ListWidth, bottom - top), palette);

                Rect right = new Rect(inRect.x + ListWidth + Pad * 2f, top,
                    inRect.width - ListWidth - Pad * 2f, bottom - top);

                if (choice.Def == null)
                    EditorParts.Note(right, right.y, "Pick something on the left, then set what it is made "
                                                     + "from, how well it was made, how worn it is and what "
                                                     + "colour it has been dyed.", palette);
                else
                    Options(right, palette);

                Footer(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight), palette);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        // ---------------------------------------------------------------------------------------
        // The list
        // ---------------------------------------------------------------------------------------

        private void List(Rect rect, UIColorPaletteDef palette)
        {
            search.Draw(new Rect(rect.x, rect.y, rect.width, 26f), palette);

            if (search.Text != query)
            {
                query = search.Text ?? string.Empty;
                Rebuild();
            }
            else if (matching.Count == 0 && query.NullOrEmpty())
            {
                Rebuild();
            }

            Rect list = new Rect(rect.x, rect.y + 30f, rect.width, rect.height - 30f);

            Widgets.DrawBoxSolid(list, palette.SurfaceSunken);

            Rect view = new Rect(0f, 0f, list.width - 18f, matching.Count * RowHeight + 4f);

            Widgets.BeginScrollView(list, ref scroll, view);

            try
            {
                for (int i = 0; i < matching.Count; i++)
                    Card(new Rect(2f, 2f + i * RowHeight, view.width - 4f, RowHeight - 2f), matching[i],
                        palette);
            }
            finally
            {
                Widgets.EndScrollView();
            }

            if (matching.Count == 0)
                EditorParts.Note(new Rect(list.x + 6f, list.y + 6f, list.width - 12f, 0f), list.y + 6f,
                    "Nothing matches.", palette);
        }

        private void Rebuild()
        {
            matching.Clear();

            List<ThingDef> all = EditorApparel.All();
            string lower = query.NullOrEmpty() ? null : query.ToLower();

            for (int i = 0; i < all.Count; i++)
            {
                if (lower == null || EditorParts.LabelOf(all[i]).ToLower().Contains(lower))
                    matching.Add(all[i]);
            }
        }

        /// <summary>
        /// One item as a card: its picture, its name, and the layer it goes on.
        ///
        /// <b>Two lines and an icon rather than a row with the layer squeezed onto the end,</b> asked for on
        /// 2026-08-23. The old row put the layer in a hundred right-aligned pixels, where "shell" and "on top"
        /// fitted and "middle, outer" did not, and it showed nothing of the thing itself -- which for apparel is
        /// the one attribute you would recognise it by.
        ///
        /// <b>The icon is drawn in the colour the item would actually be.</b> Undyed for the whole list rather
        /// than in the dye currently chosen on the right: the list is what is available, not a preview of the
        /// one being configured, and tinting forty items with the dye picked for one of them would be a list
        /// that lies about thirty-nine.
        /// </summary>
        private void Card(Rect rect, ThingDef def, UIColorPaletteDef palette)
        {
            bool chosen = def == choice.Def;
            bool over = Mouse.IsOver(rect);

            if (chosen)
                UIElementPainter.OutlineRounded(rect, palette.Accent,
                    UIElementPainter.Composite(palette.PanelBackground, palette.SelectionOverlay));
            else if (over)
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);

            string refusal = EditorApparel.Refusal(context.Pawn, def);

            EditorParts.Icon(new Rect(rect.x + 5f, rect.y + (rect.height - EditorParts.IconSize) * 0.5f,
                EditorParts.IconSize, EditorParts.IconSize), def, EditorApparel.Undyed(def, null));

            float x = rect.x + 5f + EditorParts.IconSize + 7f;
            float width = Mathf.Max(20f, rect.xMax - x - 6f);
            float line = Mathf.Max(16f, UIFonts.LineHeightOf(GameFont.Small));

            TabParts.RowLabel(new Rect(x, rect.y + 3f, width, line), EditorParts.LabelOf(def),
                refusal != null ? palette.TextDisabled : palette.TextPrimary);

            // The refusal displaces the layer rather than joining it. A body with no part for this cannot wear
            // it at all, so which layer it would have gone on is no longer the useful half.
            TabParts.RowLabel(new Rect(x, rect.y + 3f + line, width, rect.height - line - 6f),
                refusal ?? EditorApparel.Note(def) ?? string.Empty,
                refusal != null ? palette.Warning : palette.TextDisabled, GameFont.Tiny);

            string description = EditorParts.DescriptionOf(def);

            if (over && !description.NullOrEmpty())
                TooltipHandler.TipRegion(rect, (TipSignal) description);

            if (!Widgets.ButtonInvisible(rect) || chosen)
                return;

            Choose(def);
        }

        /// <summary>
        /// Takes a def and resets everything that was about the previous one.
        ///
        /// <b>Health is kept as a fraction and the colour is dropped.</b> Somebody who set ninety percent and
        /// then changed their mind about the garment meant ninety percent; somebody who dyed the last item green
        /// did not necessarily mean this one to be green, and the undyed swatch is a different colour now anyway.
        /// </summary>
        private void Choose(ThingDef def)
        {
            List<ThingDef> stuffs = EditorApparel.StuffsFor(def);

            choice.Def = def;
            choice.Stuff = stuffs.Count > 0 ? GenStuff.DefaultStuffFor(def) : null;
            choice.Colour = null;

            if (!EditorApparel.HasQuality(def))
                choice.Quality = QualityCategory.Normal;

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        // ---------------------------------------------------------------------------------------
        // What it will be
        // ---------------------------------------------------------------------------------------

        /// <summary>Quality, in two rows, because seven of them across one pane are unreadably narrow.</summary>
        private static readonly string[] LowerQualities = { "Awful", "Poor", "Normal", "Good" };

        private static readonly string[] UpperQualities = { "Excellent", "Masterwork", "Legendary" };

        private void Options(Rect rect, UIColorPaletteDef palette)
        {
            ThingDef def = choice.Def;

            Preview(new Rect(rect.x, rect.y, rect.width, PreviewSize), palette);

            float y = rect.y + PreviewSize + EditorParts.RowGap;
            float block = EditorParts.CaptionHeight + 2f + EditorParts.ControlHeight + EditorParts.FieldGap;

            List<ThingDef> stuffs = EditorApparel.StuffsFor(def);

            if (EditorParts.Picker(new Rect(rect.x, y, rect.width, EditorParts.ControlHeight), "Material",
                    choice.Stuff != null ? EditorParts.LabelOf(choice.Stuff) : "not made from stuff", palette,
                    stuffs.Count == 0 ? "This is not made from a material." : null, stuffs.Count > 0))
                OfferStuff(stuffs);

            y += block;

            if (EditorApparel.HasQuality(def))
            {
                int lower = System.Array.IndexOf(LowerQualities, choice.Quality.ToString());
                int upper = System.Array.IndexOf(UpperQualities, choice.Quality.ToString());

                int pickedLower = EditorParts.Segments(new Rect(rect.x, y, rect.width,
                    EditorParts.ControlHeight), "Quality", LowerQualities, lower, palette);

                if (pickedLower >= 0)
                    choice.Quality = (QualityCategory) pickedLower;

                y += block;

                int pickedUpper = EditorParts.Segments(new Rect(rect.x, y, rect.width,
                    EditorParts.ControlHeight), null, UpperQualities, upper, palette);

                if (pickedUpper >= 0)
                    choice.Quality = (QualityCategory) (pickedUpper + LowerQualities.Length);

                y += block;
            }

            int max = EditorApparel.MaxHealth(def, choice.Stuff);
            int health = Mathf.Clamp(Mathf.RoundToInt(max * choice.Health), 1, max);

            float moved = EditorParts.Slider(new Rect(rect.x, y, rect.width, 40f), "Item health", health, 1f,
                max, palette, health + " / " + max + "   " + (health * 100 / max) + "%");

            if (Mathf.Abs(moved - health) >= 1f)
                choice.Health = Mathf.Clamp01(moved / max);

            y += 40f + EditorParts.FieldGap;

            if (!EditorApparel.HasColour(def))
                return;

            List<Color> palette2 = EditorApparel.Palette(def, choice.Stuff);
            Color current = choice.Colour ?? EditorApparel.Undyed(def, choice.Stuff);

            // <b>The swatches give up their last slot to a button onto the full picker,</b> asked for on
            // 2026-08-23. The dye list is what the game's own styling system offers and is the right default,
            // but it is a list of somebody else's choices; the wheel is how you get the one that is not on it.
            Rect row = new Rect(rect.x, y, rect.width - CustomWidth - 6f, EditorParts.ControlHeight);

            Color? picked = EditorParts.Swatches(row, "Colour", palette2, current, palette);

            Rect custom = new Rect(row.xMax + 6f, row.y + EditorParts.CaptionHeight + 2f, CustomWidth,
                EditorParts.ControlHeight - EditorParts.CaptionHeight - 2f);

            if (TabParts.Button(custom, "Custom", palette, true, false,
                    "Any colour at all, on the wheel the styling station uses."))
            {
                Dialog_PickColour.Open(current, palette2, EditorApparel.Undyed(def, choice.Stuff),
                    chosen => choice.Colour = chosen);
            }

            if (!picked.HasValue)
                return;

            // The first swatch is the undyed colour, and choosing it means "no dye" rather than "dye it exactly
            // the colour it already is" -- so the item keeps following its material if that ever changes.
            choice.Colour = palette2.Count > 0 && EditorParts.Near(picked.Value, palette2[0])
                ? (Color?) null
                : picked;
        }

        /// <summary>The item as it will look: its own icon, tinted, beside its name and what it covers.</summary>
        private void Preview(Rect rect, UIColorPaletteDef palette)
        {
            ThingDef def = choice.Def;

            Rect icon = new Rect(rect.x, rect.y, PreviewSize, PreviewSize);

            UIElementPainter.OutlineRounded(icon, palette.Border, palette.SurfaceSunken);

            UIGuard.Try("Editor.ApparelPreview", () =>
            {
                Texture texture = def.uiIcon;

                if (texture == null)
                    return;

                Color previous = GUI.color;

                GUI.color = choice.Colour ?? EditorApparel.Undyed(def, choice.Stuff);
                GUI.DrawTexture(icon.ContractedBy(4f), texture, ScaleMode.ScaleToFit);
                GUI.color = previous;
            }, null);

            float x = icon.xMax + Pad;
            float width = Mathf.Max(20f, rect.xMax - x);

            TabParts.RowLabel(new Rect(x, rect.y, width, 24f), EditorParts.LabelOf(def), palette.TextPrimary);

            string refusal = EditorApparel.Refusal(context.Pawn, def);

            EditorParts.Note(new Rect(x, rect.y + 24f, width, 0f), rect.y + 24f,
                refusal != null
                    ? "This body has no part that could wear it, so it cannot be put on."
                    : EditorApparel.Note(def) ?? string.Empty, palette,
                refusal != null ? palette.Warning : (Color?) null);
        }

        private void OfferStuff(List<ThingDef> stuffs)
        {
            List<EditorOption> options = new List<EditorOption>();

            for (int i = 0; i < stuffs.Count; i++)
            {
                ThingDef captured = stuffs[i];

                options.Add(new EditorOption
                {
                    Label = EditorParts.LabelOf(captured),
                    Note = captured.stuffProps != null
                        ? "x" + captured.stuffProps.statFactors.GetStatFactorFromList(StatDefOf.MaxHitPoints)
                            .ToString("0.##")
                        : null,
                    Tooltip = EditorParts.DescriptionOf(captured),
                    Current = captured == choice.Stuff,
                    Chosen = () => choice.Stuff = captured
                });
            }

            Dialog_PickFrom.Open("Material", options, "Search materials");
        }

        // ---------------------------------------------------------------------------------------
        // Footer
        // ---------------------------------------------------------------------------------------

        private void Footer(Rect rect, UIColorPaletteDef palette)
        {
            bool ready = choice.Def != null;
            bool refused = ready && EditorApparel.Refusal(context.Pawn, choice.Def) != null;

            float wearWidth = Mathf.Max(TabParts.ButtonWidth("Wear it"), 110f);
            float closeWidth = Mathf.Max(TabParts.ButtonWidth("Close"), 90f);

            Rect wear = new Rect(rect.xMax - wearWidth, rect.y, wearWidth, 30f);
            Rect close = new Rect(wear.x - closeWidth - Pad, rect.y, closeWidth, 30f);

            if (TabParts.Button(wear, "Wear it", palette, ready && !refused, true,
                    !ready
                        ? "Pick a piece of apparel first."
                        : refused
                            ? "This body cannot wear it."
                            : null))
            {
                EditorApparel.Wear(context, choice);

                Close();
            }

            if (TabParts.Button(close, "Close", palette))
                Close();
        }
    }
}
