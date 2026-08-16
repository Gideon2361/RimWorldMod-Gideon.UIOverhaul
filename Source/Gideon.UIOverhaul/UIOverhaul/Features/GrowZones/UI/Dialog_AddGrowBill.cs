using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.GrowZones.UI
{
    /// <summary>
    /// Plant picker for new grow bills, laid out like Modern Ideology Menu's window: full-bleed
    /// dark chrome, a fixed-width card list on the left, a hero + collapsible detail pane on the
    /// right, and an action bar along the bottom.
    /// </summary>
    public class Dialog_AddGrowBill : Window
    {
        // Matches Window_ModernIdeo's layout constants so the suite stays visually consistent.
        // At a 900-wide window the body is 884 after the edge inset, so the detail pane was 576.
        // A quarter of that (144) moves to the list, leaving 432 for the detail pane.
        private const float ListWidth = 444f;

        // Card rows: LineHeight is the text box, RowStep the pitch between rows. The gap between
        // them is what stops descenders being clipped.
        private const float CardHeight = 130f;
        private const float NoticeRowHeight = 28f;
        private const float StatIcon = 24f;
        private const float LineHeight = 26f;
        private const float RowStep = 28f;

        /// <summary>Short numeric stats per row. The wider list column fits all of them on one line.</summary>
        private const int StatColumns = 5;
        private const float CardGap = 6f;

        /// <summary>Side of the favorite star. Large enough to hit without hunting, small enough to ignore.</summary>
        private const float StarSize = 18f;
        private const float HeaderHeight = 46f;
        private const float HeroHeight = 68f;
        private const float FooterHeight = 52f;
        private const float Pad = 12f;

        // The window is filled with the chrome color and the two panels are inset into it, so the
        // chrome shows through as a border around the outside and as a divider between the panels.
        private const float EdgeInset = 8f;
        private const float Gutter = 8f;

        private static readonly HashSet<string> CollapsedSections = new HashSet<string>();

        /// <summary>Wash color for a notice: green for a benefit, orange when unconfirmed, red otherwise.</summary>
        private static Color WashFor(PlantNoticeInfo notice)
        {
            if (notice.IsBenefit) return GzpPalette.NoticeGreen;
            if (notice.IsPossibleHazard) return GzpPalette.NoticeOrange;
            return GzpPalette.NoticeRed;
        }

        private readonly Zone_GrowingPlus zone;
        private readonly string tutorTag;
        private readonly List<ThingDef> allPlants;

        private ThingDef selected;
        private string search = "";
        private Vector2 listScroll;
        private Vector2 detailScroll;
        private bool listDragging;
        private float listDragOffset;
        /// <summary>
        /// Height the detail sections came to when they were last drawn. Zero until the first frame
        /// has laid them out, which <see cref="DrawDetail"/> reads as "no measurement yet".
        /// </summary>
        private float measuredDetailHeight;

        private bool detailDragging;
        private float detailDragOffset;

        /// <summary>
        /// The card chrome, shared by every plant row. Reconfigured per plant as the list is drawn: cards
        /// here are transient, so one instance is enough and allocating one per plant per frame would be
        /// waste.
        /// </summary>
        private readonly UICardControl plantCard = new UICardControl();

        /// <summary>Card chrome for the detail pane's heading panel.</summary>
        private readonly UICardControl heroCard = new UICardControl();

        public override Vector2 InitialSize => new Vector2(900f, 640f);

        protected override float Margin => 0f;

        public Dialog_AddGrowBill(Zone_GrowingPlus zone, string tutorTag)
        {
            this.zone = zone;
            this.tutorTag = tutorTag;
            allPlants = GrowBillUtility.AvailablePlants(zone);
            selected = allPlants.Count > 0 ? allPlants[0] : null;

            doCloseX = false;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
        }

        /// <summary>
        /// The plants to list: everything the zone offers, filtered by the search, favorites first.
        ///
        /// The ordering is applied after filtering rather than once up front, so a search that matches a
        /// favorite still floats it to the top of whatever is left.
        /// </summary>
        private List<ThingDef> VisiblePlants()
        {
            if (search.NullOrEmpty())
                return PlantFavorites.Ordered(allPlants);

            List<ThingDef> filtered = new List<ThingDef>();
            foreach (ThingDef plant in allPlants)
            {
                if (plant.label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    filtered.Add(plant);
            }

            return PlantFavorites.Ordered(filtered);
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("GrowZones.AddBillDialog", inRect, () => DrawContents(inRect),
                "The add-bill dialog shows a failure notice; bills already on the zone are unaffected.");
        }

        private void DrawContents(Rect inRect)
        {
            // Chrome fill for the whole window; the header, the outer border and the gutter between
            // the panels are all just this showing through.
            Widgets.DrawBoxSolid(inRect, GzpPalette.BGD);
            Text.Font = GameFont.Small;

            Rect header = new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight);
            DrawHeader(header);

            Rect bodyOuter = new Rect(inRect.x, header.yMax, inRect.width,
                inRect.height - HeaderHeight - FooterHeight);
            Rect body = bodyOuter.ContractedBy(EdgeInset);

            Rect listRect = new Rect(body.x, body.y, ListWidth, body.height);
            Rect detailRect = new Rect(listRect.xMax + Gutter, body.y,
                body.width - ListWidth - Gutter, body.height);

            DrawList(listRect);
            DrawDetail(detailRect);
            DrawFooter(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight));

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawHeader(Rect r)
        {
            // Chrome color, matching the footer and the window border -- no distinct title bar.
            Widgets.DrawBoxSolid(r, GzpPalette.BGD);

            Color previous = GUI.color;
            Text.Font = GameFont.Medium;
            GUI.color = GzpPalette.Stat;
            Widgets.Label(new Rect(r.x + Pad, r.y + 8f, 260f, 30f), "Add Growing Bill");
            Text.Font = GameFont.Small;

            // Search field, right-aligned before the close button.
            Rect closeRect = new Rect(r.xMax - Pad - 24f, r.y + 11f, 24f, 24f);
            Rect searchRect = new Rect(closeRect.x - 10f - 240f, r.y + 10f, 240f, 26f);

            GUI.color = GzpPalette.Stat;
            search = GzpPalette.FlatTextField(searchRect, search);
            if (search.NullOrEmpty() && !Mouse.IsOver(searchRect))
            {
                GUI.color = GzpPalette.TextDim;
                Widgets.Label(searchRect.ContractedBy(7f, 2f), "Search plants...");
            }
            GUI.color = previous;

            if (GzpPalette.IconButton(closeRect, GzpTex.Close, "Close"))
                Close();
        }

        private void DrawList(Rect r)
        {
            Widgets.DrawBoxSolid(r, GzpPalette.BG);
            Rect inner = r.ContractedBy(Pad, Pad);

            List<ThingDef> visible = VisiblePlants();

            // Cards vary in height: only hazardous plants carry the extra row, and most are not.
            float viewHeight = 0f;
            foreach (ThingDef plant in visible)
                viewHeight += CardHeightFor(plant) + CardGap;

            Rect view = new Rect(0f, 0f, GzpPalette.ContentWidth(inner), viewHeight);

            Widgets.BeginScrollView(inner, ref listScroll, view, false);
            float y = 0f;
            foreach (ThingDef plant in visible)
            {
                float height = CardHeightFor(plant);
                DrawPlantCard(new Rect(0f, y, view.width, height), plant);
                y += height + CardGap;
            }
            Widgets.EndScrollView();
            GzpPalette.FlatScrollbar(inner, viewHeight, ref listScroll, ref listDragging, ref listDragOffset);

            if (visible.Count != 0)
                return;

            Color previous = GUI.color;
            GUI.color = GzpPalette.TextDim;
            Widgets.Label(new Rect(inner.x, inner.y + 8f, inner.width, 24f), "No matching plants.");
            GUI.color = previous;
        }

        private static float CardHeightFor(ThingDef plant)
        {
            return PlantNotices.For(plant).HasValue ? CardHeight + NoticeRowHeight : CardHeight;
        }

        /// <summary>Biohazard for a confirmed hazard, question mark when unconfirmed, caduceus for a benefit.</summary>
        private static Texture NoticeIcon(PlantNoticeInfo notice)
        {
            if (notice.IsBenefit) return GzpTex.Healthy;
            if (notice.IsPossibleHazard) return GzpTex.PossibleHazard;
            return GzpTex.Hazard;
        }

        private void DrawPlantCard(Rect card, ThingDef plant)
        {
            bool isSelected = plant == selected;
            PlantNoticeInfo? notice = PlantNotices.For(plant);

            // Chrome from the shared card control: fill, accent stripe, notice wash, selection and hover
            // washes, and the click. One instance reconfigured per plant rather than one per plant --
            // these are drawn and forgotten inside a loop, so there is no state to keep between them.
            //
            // The interior below is still laid out by hand. A plant card's stat grid has deliberately
            // uneven columns -- the ideal-temperature range needs roughly twice the width of a single
            // figure -- and expressing that as a flat list of elements with absolute bounds would hide the
            // arithmetic that makes it line up. UICardControl supports exactly this: elements when the
            // layout is regular, ContentRect when it is not.
            plantCard.Padding = 0f;
            plantCard.AccentColor = StripeFor(plant);
            plantCard.BackgroundColor = GzpPalette.PanelBG;
            plantCard.Selected = isSelected;

            // Flagged plants get the striped banner washed across the card: deep red for a hazard,
            // green for a benefit. Passed as the card's background image so the control composites it in
            // the right order -- over the fill, under the stripe and the state washes.
            plantCard.BackgroundTexture = notice.HasValue ? GzpTex.NoticeBackground : null;
            plantCard.BackgroundTint = notice.HasValue ? WashFor(notice.Value) : (Color?) null;

            // Chrome only. The card's own click is taken at the very end of this method instead, because
            // anything clickable drawn on top of it has to be asked first. See the note down there.
            plantCard.DrawChrome(card);

            Rect iconRect = new Rect(card.x + 10f, card.y + 8f, 44f, 44f);
            Widgets.ThingIcon(iconRect, plant);

            float textX = iconRect.xMax + 10f;
            float textWidth = card.xMax - 10f - textX;
            // The product icon sits top-right, so the title and source lines stop short of it.
            float headWidth = textWidth - 36f;

            Color previous = GUI.color;

            Rect plantCardTitle = new Rect(textX, card.y + 6f, headWidth, LineHeight);
            Rect plantCardSourceModName = new Rect(textX, card.y + 6f + RowStep, headWidth, LineHeight);

            GzpPalette.CardLabel(plantCardTitle, plant.LabelCap,
                isSelected ? GzpPalette.Accent : GzpPalette.Stat);

            // Source mod only. Purpose moved to the Yield section, where it has room and sits next
            // to the product it describes.
            GzpPalette.CardLabel(plantCardSourceModName, SourceMod(plant), GzpPalette.TextDim);

            // Short stats now fit on a single row at this width; the grid still wraps if a future
            // stat pushes past StatColumns.
            float statRowY = plantCardSourceModName.yMax + 8f;
            float colWidth = textWidth / StatColumns;
            int slot = 0;
            foreach (Stat stat in ShortStats(plant))
            {
                StatPair(textX + slot % StatColumns * colWidth,
                    statRowY + slot / StatColumns * RowStep,
                    colWidth, stat.Icon, stat.Value, stat.Tip);
                slot++;
            }

            // The three temperatures and light share the next row. Widths are uneven on purpose:
            // the ideal figure is a range and needs roughly twice the room of a single value.
            float wideRowY = statRowY + RowStep;
            float wMin = textWidth * 0.20f;
            float wIdeal = textWidth * 0.35f;
            float wMax = textWidth * 0.20f;
            float wLight = textWidth - wMin - wIdeal - wMax;

            PlantProperties props = plant.plant;
            string coldest = props.minGrowthTemperature.ToStringTemperature("F0");
            string hottest = props.maxGrowthTemperature.ToStringTemperature("F0");
            string idealLow = props.minOptimalGrowthTemperature.ToStringTemperature("F0");
            string idealHigh = props.maxOptimalGrowthTemperature.ToStringTemperature("F0");

            float x = textX;
            StatPair(x, wideRowY, wMin, GzpTex.MinTemp, coldest,
                $"Coldest temperature it will grow in.\nBelow this it stops growing and may die.");
            x += wMin;

            StatPair(x, wideRowY, wIdeal, GzpTex.IdealTemp, $"{idealLow}–{idealHigh}",
                $"Grows fastest between {idealLow} and {idealHigh}.\n"
                + $"Full range: {coldest} to {hottest}.");
            x += wIdeal;

            StatPair(x, wideRowY, wMax, GzpTex.MaxTemp, hottest,
                "Hottest temperature it will grow in.\nAbove this it stops growing and may die.");
            x += wMax;

            LightInfo(plant, out string lightLabel, out Color lightColor, out string lightTip);
            StatPair(x, wideRowY, wLight, GzpTex.Light, lightLabel, lightTip, lightColor);

            if (notice.HasValue)
            {
                StatPair(textX, wideRowY + RowStep, textWidth,
                    NoticeIcon(notice.Value), notice.Value.Label, notice.Value.Detail,
                    notice.Value.IsBenefit ? GzpPalette.Good : GzpPalette.Warn);
            }

            // <b>The small targets are drawn before the card's own, and the order is the whole of it.</b>
            // Widgets.ButtonInvisible is GUI.Button, which takes the event on mouse down: the first control
            // drawn that contains the point calls Use() and every control after it sees an event that has
            // already been spent. So whichever button is drawn first wins, regardless of which is on top
            // visually or which is smaller.
            //
            // This method used to take the card's click first, at the top, and then try to undo it by asking
            // whether the cursor happened to be over the product icon. That silently did nothing on the
            // product -- its own button could never fire, because the card had already consumed the event --
            // and the star added later inherited the same fault, which is how it was found.
            //
            // Drawing them in this order makes the guessing unnecessary: the star gets first refusal, then the
            // product, then the card takes whatever neither claimed.
            DrawFavorite(card, plant);
            DrawProduct(card, plant);

            if (!Widgets.ButtonInvisible(card))
                return;

            selected = plant;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// The favorite star: hollow until flagged, solid after.
        ///
        /// <b>Under the plant icon, in the left gutter.</b> The product icon is top right and opens an info
        /// card, and the card itself selects the plant, so a third target has to land where neither reaches.
        /// The bottom right corner looked free and is not: a hazardous plant carries an extra notice row
        /// spanning the full width down there, so the star would have sat on top of the warning on exactly
        /// the cards that most need reading. The column beneath the icon is empty on every card, whatever its
        /// height, because the stat grid starts to the right of it.
        ///
        /// <b>Filled means flagged, and the shape carries that rather than the colour.</b> An outline against
        /// a solid is legible at sixteen pixels and on any card wash, where two shades of the same fill are
        /// not -- these sit on hazard red and benefit green as often as on the plain panel.
        /// </summary>
        private static void DrawFavorite(Rect card, ThingDef plant)
        {
            bool favorite = PlantFavorites.IsFavorite(plant);

            // Centred on the plant icon's column, just below it. Kept in step with the icon rect in
            // DrawPlantCard rather than written as its own literals, so moving the icon moves this with it.
            Rect star = new Rect(card.x + 10f + (44f - StarSize) * 0.5f, card.y + 8f + 44f + 8f,
                StarSize, StarSize);

            bool over = Mouse.IsOver(star);

            Texture2D shape = favorite ? UIShapes.StarFilled : UIShapes.StarHollow;

            if (shape != null)
            {
                Color previous = GUI.color;

                // Dim until it is either flagged or under the cursor. A row of bright stars down an unsorted
                // list would compete with the plant names, which are what somebody is actually reading.
                GUI.color = favorite
                    ? GzpPalette.Accent
                    : over
                        ? GzpPalette.Stat
                        : GzpPalette.TextDim;

                GUI.DrawTexture(star, shape);

                GUI.color = previous;
            }

            TooltipHandler.TipRegion(star, (TipSignal) (favorite
                ? "Favorited. Click to remove.\n\nFavorites are pinned to the top of this list and kept "
                  + "between colonies."
                : "Click to favorite.\n\nFavorites are pinned to the top of this list and kept between "
                  + "colonies."));

            if (Widgets.ButtonInvisible(star))
            {
                PlantFavorites.Toggle(plant);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        /// <summary>Draws the harvested product's own artwork as a button onto the vanilla info card.</summary>
        private static void DrawProduct(Rect card, ThingDef plant)
        {
            ThingDef harvested = plant.plant.harvestedThingDef;
            if (harvested == null)
                return;

            // Top-right, clear of the stat rows below.
            Rect productRect = new Rect(card.xMax - 42f, card.y + 8f, 32f, 32f);
            bool over = Mouse.IsOver(productRect);

            if (over)
                Widgets.DrawBoxSolid(productRect, UIColorPaletteDef.Active.HoverOverlay);

            Widgets.ThingIcon(productRect, harvested);
            TooltipHandler.TipRegion(productRect,
                (TipSignal) $"Produces {harvested.LabelCap}\nClick for details.");

            if (Widgets.ButtonInvisible(productRect))
            {
                Find.WindowStack.Add(new Dialog_InfoCard(harvested, (Precept_ThingStyle) null));
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        private struct Stat
        {
            public readonly Texture Icon;
            public readonly string Value;
            public readonly string Tip;

            public Stat(Texture icon, string value, string tip)
            {
                Icon = icon;
                Value = value;
                Tip = tip;
            }
        }

        /// <summary>The compact numeric stats, in display order, skipping any that do not apply.</summary>
        private static IEnumerable<Stat> ShortStats(ThingDef plant)
        {
            PlantProperties props = plant.plant;

            yield return new Stat(GzpTex.GrowTime, $"{props.growDays:0.#}d", "Days to fully grow");

            yield return props.LimitedLifespan
                ? new Stat(GzpTex.Lifespan, $"{props.LifespanDays:0.#}d",
                    "Lifespan -- the plant dies of old age after this long")
                : new Stat(GzpTex.Lifespan, "never", "Does not die of old age");

            yield return new Stat(GzpTex.Beauty, Beauty(plant).ToString(), "Beauty");

            if (props.sowMinSkill > 0)
            {
                yield return new Stat(GzpTex.Skill, props.sowMinSkill.ToString(),
                    "Minimum Growing skill needed to sow");
            }

            ThingDef harvested = props.harvestedThingDef;
            if (harvested != null && harvested.IsNutritionGivingIngestible)
            {
                yield return new Stat(GzpTex.Nutrition,
                    $"{harvested.GetStatValueAbstract(StatDefOf.Nutrition):0.##}",
                    $"Nutrition per {harvested.label}");
            }
        }

        /// <summary>
        /// "Produces" row with the product's own artwork beside its name. Both the icon and the
        /// text are one click target onto the vanilla info card, so whichever the player aims at
        /// works.
        /// </summary>
        private static void ProducesLine(ref float y, float width, ThingDef harvested)
        {
            const float iconSize = 20f;
            Rect row = new Rect(0f, y, width, 22f);
            Color previous = GUI.color;

            GUI.color = GzpPalette.TextDim;
            Widgets.Label(new Rect(row.x, row.y, row.width * 0.4f, row.height), "Produces");

            string label = harvested.LabelCap;
            float labelWidth = Text.CalcSize(label).x;
            Rect target = new Rect(row.xMax - labelWidth - iconSize - 6f, row.y,
                labelWidth + iconSize + 6f, row.height);
            bool over = Mouse.IsOver(target);

            GUI.color = Color.white;
            Widgets.ThingIcon(new Rect(target.x, target.y + 1f, iconSize, iconSize), harvested);

            GUI.color = over ? GzpPalette.Accent : GzpPalette.Stat;
            TextAnchor anchor = Text.Anchor;
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(target.x + iconSize + 6f, row.y, labelWidth, row.height), label);
            Text.Anchor = anchor;
            GUI.color = previous;

            TooltipHandler.TipRegion(target, (TipSignal) $"{label}\nClick for details.");
            if (Widgets.ButtonInvisible(target))
            {
                Find.WindowStack.Add(new Dialog_InfoCard(harvested, (Precept_ThingStyle) null));
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += row.height;
        }

        /// <summary>Icon plus value. Icons draw untinted so their own art reads correctly.</summary>
        private static void StatPair(float x, float y, float width, Texture icon, string value,
            string tooltip, Color? valueColor = null)
        {
            Color previous = GUI.color;

            // Always untinted: these are full-color illustrations, not silhouettes.
            Rect iconRect = new Rect(x, y + 1f, StatIcon, StatIcon);
            GUI.color = Color.white;
            GUI.DrawTexture(iconRect, icon);
            GUI.color = previous;

            // Centerd on the icon's own center line rather than on the row. LineHeight is a little
            // taller than the icon -- deliberately, since a box the height of the icon clips
            // descenders -- so a top-aligned label sits a couple of pixels high next to the artwork.
            Rect valueRect = new Rect(x + StatIcon + 4f, iconRect.center.y - LineHeight * 0.5f,
                width - StatIcon - 4f, LineHeight);

            GzpPalette.CardLabel(valueRect, value, valueColor ?? GzpPalette.TextDim,
                TextAnchor.MiddleLeft);

            TooltipHandler.TipRegion(new Rect(x, y, width, LineHeight), (TipSignal) tooltip);
        }

        private static string SourceMod(ThingDef plant)
        {
            ModContentPack pack = plant.modContentPack;
            return pack == null ? "Unknown source" : pack.Name;
        }

        private static int Beauty(ThingDef plant)
        {
            return Mathf.RoundToInt(plant.GetStatValueAbstract(StatDefOf.Beauty));
        }

        /// <summary>
        /// Light behavior, in the three cases that matter when picking a crop: harmed by light,
        /// indifferent to it, or needing a minimum glow. The finer detail -- optimal glow, or a
        /// sowing requirement of permanent darkness -- goes in the tooltip rather than the label,
        /// which has to stay short enough for the card.
        /// </summary>
        private static void LightInfo(ThingDef plant, out string label, out Color color, out string tip)
        {
            PlantProperties props = plant.plant;

            // A mod that implements light damage in its own thingClass leaves diesToLight false and
            // growMinGlow at zero, which reads as "Any" when light in fact kills the plant. A table
            // row can say so; it is checked first because it exists to contradict these fields.
            PlantLightBehaviour over = PlantNotices.LightFor(plant, out string overrideTip);

            if (props.diesToLight || over == PlantLightBehaviour.Deadly)
            {
                label = "Deadly";
                color = GzpPalette.Bad;
                tip = overrideTip.NullOrEmpty()
                    ? "Light damages this plant. Grow it in darkness."
                    : overrideTip;
                return;
            }

            if (over == PlantLightBehaviour.Normal)
            {
                label = "Normal";
                color = GzpPalette.TextDim;
                tip = overrideTip.NullOrEmpty() ? "Needs ordinary crop light to grow." : overrideTip;
                return;
            }

            if (props.growMinGlow <= 0.001f || over == PlantLightBehaviour.Any)
            {
                label = "Any";
                color = GzpPalette.Accent;
                if (!overrideTip.NullOrEmpty())
                    tip = overrideTip;
                else
                    tip = props.mustBePermanentDarknessToSow
                        ? "Grows at any light level, but can only be sown in permanent darkness."
                        : "Grows at any light level.";
                return;
            }

            // TextDim matches StatPair's default, so this row sits level with the stat grid above.
            color = GzpPalette.TextDim;
            tip = $"Needs at least {props.growMinGlow.ToStringPercent()} light to grow.\n"
                  + $"Optimal: {props.growOptimalGlow.ToStringPercent()}.";

            // 51% is what almost every ordinary crop wants, so it earns a word instead of a number.
            label = Mathf.Abs(props.growMinGlow - 0.51f) < 0.005f
                ? "Normal"
                : props.growMinGlow.ToStringPercent();
        }

        private static Color StripeFor(ThingDef plant)
        {
            if (plant.plant.IsTree)
                return GzpPalette.Warn;
            switch (plant.plant.purpose)
            {
                case PlantPurpose.Food:
                    return GzpPalette.Good;
                case PlantPurpose.Health:
                    return GzpPalette.Accent;
                default:
                    return GzpPalette.BGL;
            }
        }

        private void DrawDetail(Rect r)
        {
            // BG, not PanelBG: cards drawn inside this pane are PanelBG, so a PanelBG pane would
            // render the hero card invisible. This also matches the list pane, where PanelBG cards
            // sit on a BG background.
            Widgets.DrawBoxSolid(r, GzpPalette.BG);

            if (selected == null)
            {
                Color dim = GUI.color;
                GUI.color = GzpPalette.TextDim;
                Widgets.Label(r.ContractedBy(Pad), "No plants available for this zone.");
                GUI.color = dim;
                return;
            }

            Rect inner = r.ContractedBy(Pad, Pad);
            DrawHero(new Rect(inner.x, inner.y, inner.width, HeroHeight), selected);

            Rect body = new Rect(inner.x, inner.y + HeroHeight + 8f, inner.width, inner.height - HeroHeight - 8f);
            float contentWidth = GzpPalette.ContentWidth(body);

            // Height taken from what the previous frame actually laid out, rather than estimated.
            // The sections vary with the plant, with its description, and with whichever ones the
            // player has collapsed, and an estimate that runs over lets the pane scroll into blank
            // space below the content. DrawSections already returns an exact figure as a side effect
            // of drawing, so the honest measure is free -- one frame late, which nothing can see,
            // and self-correcting because the next frame measures again.
            float contentHeight = measuredDetailHeight > 0f ? measuredDetailHeight : body.height;
            Rect view = new Rect(0f, 0f, contentWidth, contentHeight);

            Widgets.BeginScrollView(body, ref detailScroll, view, false);
            float y = 0f;
            DrawSections(ref y, view.width);
            Widgets.EndScrollView();

            measuredDetailHeight = y;
            GzpPalette.FlatScrollbar(body, contentHeight, ref detailScroll, ref detailDragging, ref detailDragOffset);
        }

        private void DrawHero(Rect hero, ThingDef plant)
        {
            // Same wash the plant's card carries, so the notice color follows the selection into
            // the detail pane instead of only appearing further down the page.
            PlantNoticeInfo? notice = PlantNotices.For(plant);

            // No accent stripe and no hover: this is a heading, not a row in a list, so there is nothing
            // to categorize and nothing to click. AccentColor left null is what suppresses the stripe.
            //
            // Nothing to click is also why this is DrawChrome. Draw ends with a ButtonInvisible covering the
            // whole card, which would consume the event before the info card button below ever saw it.
            heroCard.Padding = 0f;
            heroCard.AccentColor = null;
            heroCard.HoverHighlight = false;
            heroCard.BackgroundColor = GzpPalette.PanelBG;
            heroCard.BackgroundTexture = notice.HasValue ? GzpTex.NoticeBackground : null;
            heroCard.BackgroundTint = notice.HasValue ? WashFor(notice.Value) : (Color?) null;
            heroCard.DrawChrome(hero);

            // Name only. The description used to live here and was routinely clipped; it now has its
            // own section below, where it can wrap to whatever height it needs.
            Rect iconRect = new Rect(hero.x + 10f, hero.y + 10f, 48f, 48f);
            Widgets.ThingIcon(iconRect, plant);

            Color previous = GUI.color;
            Text.Font = GameFont.Medium;
            GUI.color = GzpPalette.Stat;
            Widgets.Label(new Rect(iconRect.xMax + 12f, hero.y + 18f, hero.width - 110f, 32f), plant.LabelCap);
            Text.Font = GameFont.Small;
            GUI.color = previous;

            Widgets.InfoCardButton(hero.xMax - 30f, hero.y + 12f, plant);
        }

        private void DrawSections(ref float y, float width)
        {
            PlantProperties plant = selected.plant;

            if (!selected.description.NullOrEmpty()
                && GzpPalette.SectionHeader(ref y, 0f, width, "Description", CollapsedSections))
            {
                Color descColour = GUI.color;
                float descHeight = Text.CalcHeight(selected.description, width);
                GUI.color = GzpPalette.TextDim;
                Widgets.Label(new Rect(0f, y, width, descHeight), selected.description);
                GUI.color = descColour;
                y += descHeight + 6f;
            }

            // Notices lead: a hazard is the reason to reject a plant outright, and a benefit is
            // often the reason to pick it.
            PlantNoticeInfo? notice = PlantNotices.For(selected);
            bool isBenefit = notice.HasValue && notice.Value.IsBenefit;
            string sectionTitle = isBenefit ? "Benefits" : "Hazards";

            if (notice.HasValue && GzpPalette.SectionHeader(ref y, 0f, width, sectionTitle, CollapsedSections))
            {
                Color previous = GUI.color;

                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(0f, y + 1f, 22f, 22f), NoticeIcon(notice.Value));
                GUI.color = isBenefit ? GzpPalette.Good : GzpPalette.Warn;
                Widgets.Label(new Rect(26f, y, width - 26f, 22f), notice.Value.Label);
                y += 24f;

                string detail = notice.Value.Detail;
                if (!detail.NullOrEmpty())
                {
                    float detailHeight = Text.CalcHeight(detail, width);
                    GUI.color = GzpPalette.Stat;
                    Widgets.Label(new Rect(0f, y, width, detailHeight), detail);
                    y += detailHeight + 2f;
                }

                GUI.color = previous;
                y += 6f;
            }

            if (GzpPalette.SectionHeader(ref y, 0f, width, "Growth", CollapsedSections))
            {
                GzpPalette.InfoLine(ref y, 0f, width, "Days to mature", $"{plant.growDays:0.#}");
                GzpPalette.InfoLine(ref y, 0f, width, "Minimum fertility", plant.fertilityMin.ToStringPercent());
                GzpPalette.InfoLine(ref y, 0f, width, "Fertility sensitivity", plant.fertilitySensitivity.ToStringPercent());
                y += 6f;
            }

            if (GzpPalette.SectionHeader(ref y, 0f, width, "Yield", CollapsedSections))
            {
                GzpPalette.InfoLine(ref y, 0f, width, "Grown for", GrowBillUtility.PurposeLabel(selected));

                ThingDef harvested = plant.harvestedThingDef;
                if (harvested == null)
                {
                    GzpPalette.InfoLine(ref y, 0f, width, "Harvest", "None", GzpPalette.TextDim);
                }
                else
                {
                    ProducesLine(ref y, width, harvested);
                    GzpPalette.InfoLine(ref y, 0f, width, "Yield per plant", $"{plant.harvestYield:0.#}");
                    if (harvested.IsNutritionGivingIngestible)
                    {
                        float nutrition = harvested.GetStatValueAbstract(StatDefOf.Nutrition);
                        GzpPalette.InfoLine(ref y, 0f, width, "Nutrition each", $"{nutrition:0.##}");
                    }
                }
                y += 6f;
            }

            if (!GzpPalette.SectionHeader(ref y, 0f, width, "Requirements", CollapsedSections))
                return;

            if (plant.sowMinSkill > 0)
            {
                bool canSow = GrowBillUtility.AnyGrowerCanSow(zone, selected);
                GzpPalette.InfoLine(ref y, 0f, width, "Growing skill", plant.sowMinSkill.ToString(),
                    canSow ? GzpPalette.Stat : GzpPalette.Bad);
                if (!canSow)
                    GzpPalette.InfoLine(ref y, 0f, width, "", "No colonist can sow this", GzpPalette.Bad);
            }
            else
            {
                GzpPalette.InfoLine(ref y, 0f, width, "Growing skill", "None");
            }

            if (plant.cavePlant)
                GzpPalette.InfoLine(ref y, 0f, width, "Light", "Must be in darkness", GzpPalette.Warn);
            if (plant.interferesWithRoof)
                GzpPalette.InfoLine(ref y, 0f, width, "Roof", "Cannot grow under a roof", GzpPalette.Warn);
            if (plant.IsTree)
                GzpPalette.InfoLine(ref y, 0f, width, "Type", "Tree", GzpPalette.Warn);
        }

        private void DrawFooter(Rect r)
        {
            Widgets.DrawBoxSolid(r, GzpPalette.BGD);

            bool full = zone.BillStack.Count >= BillStack.MaxCount;

            Color previous = GUI.color;
            GUI.color = full ? GzpPalette.Bad : GzpPalette.TextDim;
            Widgets.Label(new Rect(r.x + Pad, r.y + 16f, 320f, 24f),
                full
                    ? $"Bill limit reached ({BillStack.MaxCount})."
                    : $"{zone.BillStack.Count} / {BillStack.MaxCount} bills on this zone");
            GUI.color = previous;

            Rect addRect = new Rect(r.xMax - Pad - 150f, r.y + 10f, 150f, 32f);
            bool canAdd = selected != null && !full;
            if (!GzpPalette.GrayButton(addRect, "Add Bill", canAdd, true))
                return;

            GrowBillUtility.AddBill(zone, selected, tutorTag);
            Close();
        }
    }
}

