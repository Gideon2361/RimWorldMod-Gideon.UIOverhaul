using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// The panel beside the list, describing one species: what it is, what is ordered on it, what the pasture
    /// will carry, and where its auto slaughter limits are set.
    ///
    /// <b>The auto slaughter limits live here, and vanilla's own window is why.</b>
    /// <c>Dialog_AutoSlaughter</c> lists every species in the game over again so five numbers can be typed into a
    /// row, in a window that has no idea how many animals you have or what the pen grows. Those five numbers are
    /// properties of the species already selected on screen, and they only make sense next to the head count and
    /// the winter forecast, because those are what decide the answer.
    ///
    /// <b>The pasture bars are the same figures the pen marker hides.</b> Four quadrums of growth against what
    /// this pen eats, from <see cref="AnimalPasture"/>. A colony reads this once and knows whether the herd is
    /// too big, which is a question no vanilla screen answers.
    ///
    /// <b>Nothing here is a second way to do something.</b> Every write goes through the same helpers the row's
    /// menu uses, so a limit set here and one set from the menu are the same operation. The pane is a view with
    /// two editable things in it, not a second interface.
    /// </summary>
    internal static class AnimalSpeciesPane
    {
        internal const float PaneWidth = 300f;

        private const float Pad = 10f;

        /// <summary>The five auto slaughter fields, in the order vanilla's own window lists them.</summary>
        private static readonly UITextBoxControl[] Limits =
        {
            Numeric(), Numeric(), Numeric(), Numeric(), Numeric()
        };

        private static readonly string[] LimitCaptions =
        {
            "Total", "Females", "Males", "Female young", "Male young"
        };

        private static UITextBoxControl Numeric()
        {
            return new UITextBoxControl
            {
                Placeholder = "none",
                MaxLength = 4,
                ShowClearButton = false
            };
        }

        /// <summary>Which species the limit boxes currently hold, so they are reloaded when it changes.</summary>
        private static ThingDef loadedFor;

        private static int loadedMap = -1;

        private static Vector2 scroll;

        /// <summary>Where the last draw ended, which is what the next one scrolls. See the note in Contents.</summary>
        private static float contentHeight = 600f;

        /// <summary>
        /// Draws the pane. Returns false when there is nothing left to describe, which closes it.
        ///
        /// The caller passes the group it found this frame rather than one the pane remembers, for the same reason
        /// the panel keys its open row by identity: the roster recycles its groups, and a pane holding one would
        /// eventually describe a different species.
        /// </summary>
        internal static bool Draw(Rect rect, AnimalGroup group, UIColorPaletteDef palette, Action changed)
        {
            if (group == null || group.Count == 0)
                return false;

            return UIGuard.Try("Animals.Pane", () =>
            {
                Widgets.DrawBoxSolid(rect, palette.PanelBackground);

                GUI.color = palette.Border;

                Widgets.DrawBox(rect, 1);

                GUI.color = Color.white;

                Rect inner = rect.ContractedBy(Pad);

                Load(group);
                Contents(inner, group, palette, changed);

                return true;
            }, false, "The species panel could not be drawn. The list beside it is unaffected.");
        }

        private static void Contents(Rect inner, AnimalGroup group, UIColorPaletteDef palette, Action changed)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                float y = inner.y;

                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextPrimary;

                Widgets.LabelEllipses(new Rect(inner.x, y, inner.width, 30f),
                    group.Def.LabelCap + "  " + group.Count);

                y += 30f;

                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                Widgets.LabelEllipses(new Rect(inner.x, y, inner.width, UIFonts.LineHeightOf(GameFont.Tiny)),
                    Kind(group));

                y += UIFonts.LineHeightOf(GameFont.Tiny) + 6f;

                Rect body = new Rect(inner.x, y, inner.width, Mathf.Max(0f, inner.yMax - y));

                // Scrolled, because this pane is long: the facts, a row per training kind, the pasture bars and
                // five limit fields do not fit beside a short window.
                Rect view = new Rect(0f, 0f, body.width - 18f, contentHeight);

                Widgets.BeginScrollView(body, ref scroll, view);

                float at = 0f;

                at = Facts(view, at, group, palette);
                at = Training(view, at, group, palette, changed);
                at = Orders(view, at, group, palette);
                at = Pasture(view, at, group, palette);
                at = LimitFields(view, at, group, palette, changed);

                // Last, and it is the longest section now that the menu's contents live in it. Above it are the
                // readings somebody opened the pane to look at; a settings block that pushed those off the top
                // would make the pane worse at the thing it was for first.
                at = Settings(view, at, group, palette, changed);

                Widgets.EndScrollView();

                // Measured rather than estimated, and that is a fix rather than a refinement. This used to guess
                // the height from a formula, and the guess did not know how many training rows a species would
                // have: Aaron's goat had fifteen, which is 480 pixels the estimate had never heard of, so the
                // scroll view believed everything fitted and quietly clipped the auto slaughter limits off the
                // bottom with no way to reach them.
                //
                // Remembering where the last draw ended cannot be wrong in that way: whatever was drawn is
                // exactly what the next frame scrolls. The cost is one frame of lag after the content changes
                // size, which is invisible next to a panel that cannot scroll to its own contents.
                contentHeight = at;
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private static string Kind(AnimalGroup group)
        {
            if (group.Kind == AnimalKind.Wild)
                return group.Predator ? "wildlife, predator" : "wildlife";

            if (group.Caravan != null)
                return "colony animals, travelling";

            return group.Pen != null ? "colony livestock, penned" : "colony animals";
        }


        // ---------------------------------------------------------------------------------------
        // Blocks
        // ---------------------------------------------------------------------------------------

        // Both of these moved to AnimalPaneParts when the individual animal's card arrived and needed the same
        // two shapes. They stay here as names this pane already used at twenty call sites, forwarding to the one
        // implementation, so the two cards cannot drift apart.

        private static float Heading(Rect view, float y, string text, UIColorPaletteDef palette)
        {
            return AnimalPaneParts.Heading(view, y, text, palette);
        }

        /// <summary>One fact: a name on the left and its value on the right, which is most of this pane.</summary>
        private static float Pair(Rect view, float y, string name, string value, Color color,
            UIColorPaletteDef palette)
        {
            return AnimalPaneParts.Pair(view, y, name, value, color, palette);
        }

        private static float Facts(Rect view, float y, AnimalGroup group, UIColorPaletteDef palette)
        {
            y = Heading(view, y, group.Kind == AnimalKind.Wild ? "WORTH KNOWING" : "THE SPECIES", palette);

            if (group.Kind == AnimalKind.Wild)
            {
                y = Pair(view, y, "Meat, whole group", Mathf.RoundToInt(group.Meat).ToString(),
                    palette.TextPrimary, palette);

                if (!group.LeatherLabel.NullOrEmpty())
                    y = Pair(view, y, "Leather", Mathf.RoundToInt(group.Leather) + " " + group.LeatherLabel,
                        palette.TextPrimary, palette);

                y = Pair(view, y, "Wildness", group.Wildness.ToStringPercent(), palette.TextPrimary, palette);

                y = Pair(view, y, "Trainability", group.Trainability ?? "none", palette.TextPrimary, palette);

                if (group.ManhunterOnDamage > 0f)
                    y = Pair(view, y, "Manhunter if shot", group.ManhunterOnDamage.ToStringPercent(),
                        group.ManhunterOnDamage >= 0.5f ? palette.Danger : palette.Warning, palette);

                if (group.ManhunterOnTameFail > 0f)
                    y = Pair(view, y, "Manhunter on tame fail", group.ManhunterOnTameFail.ToStringPercent(),
                        group.ManhunterOnTameFail >= 0.3f ? palette.Danger : palette.Warning, palette);

                if (group.TameOdds.Known)
                {
                    y = Pair(view, y, "Best handler",
                        group.TameOdds.Handler == null ? "nobody" : group.TameOdds.Handler.LabelShortCap.ToString(),
                        palette.TextPrimary, palette);

                    y = Pair(view, y, "Chance to tame", group.TameOdds.Chance.ToStringPercent(),
                        group.TameOdds.AnyoneSkilledEnough ? palette.TextPrimary : palette.TextDisabled, palette);

                    if (!group.TameOdds.AnyoneSkilledEnough)
                        y = Pair(view, y, "Handling needed", group.TameOdds.MinSkill.ToString(), palette.Warning,
                            palette);
                }

                if (group.NearestDistance >= 0)
                    y = Pair(view, y, "Nearest", group.NearestDistance + " tiles", palette.TextPrimary, palette);

                return y + 4f;
            }

            y = Pair(view, y, "Nutrition eaten", group.NutritionPerDay.ToString("0.##") + " a day",
                palette.TextPrimary, palette);

            if (group.Produce.Any)
            {
                y = Pair(view, y, group.Produce.ResourceLabel.CapitalizeFirst(),
                    group.ProducePerDay.ToString("0.#") + " a day", palette.TextPrimary, palette);
            }

            y = Pair(view, y, "Meat each",
                Mathf.RoundToInt(group.Meat / Mathf.Max(1, group.Count)).ToString(), palette.TextPrimary,
                palette);

            if (group.Def.race != null && group.Def.race.gestationPeriodDays > 0f)
                y = Pair(view, y, "Gestation", group.Def.race.gestationPeriodDays.ToString("0.#") + " days",
                    palette.TextPrimary, palette);

            if (group.Pregnant > 0)
                y = Pair(view, y, "Pregnant", group.Pregnant.ToString(), palette.TextPrimary, palette);

            if (group.TrainingAtRisk > 0)
                y = Pair(view, y, "Training at risk", group.TrainingAtRisk.ToString(), palette.Danger, palette);

            return y + 4f;
        }

        /// <summary>
        /// The training requests for this species, one labelled row per skill.
        ///
        /// <b>The card is where the full set lives.</b> Asked for on 2026-08-22. The species row carries the same
        /// boxes but only as many as its column can hold and with the names in tooltips; here every skill the
        /// species can take gets a row, its own name, and how many have learned it. Clicking either applies to the
        /// whole species, because they are the same control on the same subject.
        ///
        /// Nothing is drawn for a species that can learn nothing, rather than a heading over an empty space that
        /// invites the reader to look for controls that are not there.
        /// </summary>
        private static float Training(Rect view, float y, AnimalGroup group, UIColorPaletteDef palette,
            Action changed)
        {
            if (group.Kind != AnimalKind.Colony)
                return y;

            List<TrainableDef> kinds = AnimalTrainingBoxes.KindsFor(group);

            if (kinds.Count == 0)
                return y;

            // Copied, because the helper's scratch list is reused by the per animal reads inside the draw below.
            TrainableDef[] set = kinds.ToArray();

            y = Heading(view, y, "TRAINING", palette);

            for (int i = 0; i < set.Length; i++)
            {
                TrainableDef kind = set[i];
                Rect row = new Rect(view.x, y, view.width, AnimalTrainingBoxes.PillHeight + 4f);

                AnimalTrainingBoxes.DrawForKind(row, group, kind, palette, changed);

                y = row.yMax + 2f;
            }

            return y + 4f;
        }

        /// <summary>
        /// What is ordered on this species, and for a hunt, which animals were chosen.
        ///
        /// <b>Naming the chosen animals is the point of this block.</b> A stepper that says "4 of 6" without
        /// saying which four is asking to be trusted; naming them makes the picking rule visible, and lets
        /// somebody who disagrees uncheck one inside the opened row.
        /// </summary>
        private static float Orders(Rect view, float y, AnimalGroup group, UIColorPaletteDef palette)
        {
            // The colony's own used to get a count of slaughter and release orders here. Those are switches in
            // Species settings now, and each says how many of the group carries the order, so a block above
            // repeating it would be the same number in two places: the first of them to go stale would look like
            // a bug in whichever one the player happened to read.
            if (group.Kind != AnimalKind.Wild)
                return y;

            y = Heading(view, y, "ORDERED", palette);

            y = Pair(view, y, "Hunt", group.HuntOrdered + " of " + group.Count,
                group.HuntOrdered > 0 ? palette.Warning : palette.TextSecondary, palette);

            if (group.HuntOrdered > 0)
            {
                float each = group.Meat / Mathf.Max(1, group.Count);

                y = Pair(view, y, "Yield from those",
                    Mathf.RoundToInt(each * group.HuntOrdered) + " meat", palette.Success, palette);
            }

            y = Pair(view, y, "Tame", group.TameOrdered + " of " + group.Count,
                group.TameOrdered > 0 ? palette.Success : palette.TextSecondary, palette);

            if (group.HuntOrdered <= 0)
                return y + 4f;

            List<Pawn> chosen = AnimalDesignations.ChooseForHunt(group, group.HuntOrdered);

            string names = string.Empty;

            for (int i = 0; i < chosen.Count; i++)
            {
                if (i > 0)
                    names += ", ";

                names += chosen[i].LabelShortCap;
            }

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;

            Rect block = new Rect(view.x, y, view.width, 34f);

            Widgets.Label(block, "Chosen, wounded and nearest first: " + names);

            return block.yMax + 4f;
        }

        private static float Pasture(Rect view, float y, AnimalGroup group, UIColorPaletteDef palette)
        {
            PastureReading reading = AnimalPasture.ForGroup(group);

            if (!reading.Available || reading.PerQuadrum == null)
                return y;

            y = Heading(view, y, "PASTURE, NUTRITION PER DAY", palette);

            if (reading.Unenclosed)
            {
                GUI.color = palette.Warning;

                Text.Font = GameFont.Tiny;

                Widgets.Label(new Rect(view.x, y, view.width, 30f),
                    "This pen is not closed, so nothing here is dependable.");

                return y + 32f;
            }

            float widest = Mathf.Max(reading.ConsumptionPerDay, 0.01f);

            for (int i = 0; i < reading.PerQuadrum.Length; i++)
                widest = Mathf.Max(widest, reading.PerQuadrum[i]);

            float barsHeight = 70f;
            float slot = view.width / reading.PerQuadrum.Length;

            for (int i = 0; i < reading.PerQuadrum.Length; i++)
            {
                float grown = reading.PerQuadrum[i];
                float height = Mathf.Max(2f, grown / widest * (barsHeight - 14f));

                Rect bar = new Rect(view.x + i * slot + 4f, y + barsHeight - 14f - height, slot - 8f, height);

                Color color = grown >= reading.ConsumptionPerDay
                    ? palette.Success
                    : grown <= 0f ? palette.Danger : palette.Warning;

                Widgets.DrawBoxSolid(bar, color);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperCenter;
                GUI.color = palette.TextSecondary;

                Widgets.Label(new Rect(view.x + i * slot, y + barsHeight - 12f, slot, 16f),
                    QuadrumUtility.QuadrumsInChronologicalOrder[i].Label().Substring(0, 3));

                Text.Anchor = TextAnchor.UpperLeft;
            }

            // The line the bars are read against: cross it and the herd is eating more than the pen grows.
            float eatenY = y + barsHeight - 14f - Mathf.Min(barsHeight - 14f,
                reading.ConsumptionPerDay / widest * (barsHeight - 14f));

            GUI.color = palette.Danger;

            Widgets.DrawLineHorizontal(view.x, eatenY, view.width);

            GUI.color = Color.white;

            y += barsHeight + 6f;

            y = Pair(view, y, "This pen eats", reading.ConsumptionPerDay.ToString("0.##") + " a day",
                palette.TextPrimary, palette);

            if (reading.Short)
            {
                int over = AnimalPasture.ShortBy(reading, group);

                y = Pair(view, y, reading.WorstQuadrum.Label().CapitalizeFirst() + " shortfall",
                    over > 0 ? over + " too many" : reading.WorstMargin.ToString("0.##"), palette.Warning,
                    palette);

                if (reading.StockpiledNutrition > 0f)
                    y = Pair(view, y, "Hay in store covers", Mathf.FloorToInt(reading.DaysOfStockpile) + " days",
                        palette.TextPrimary, palette);
            }
            else
            {
                int carries = AnimalPasture.Carries(reading, group);

                if (carries >= 0)
                    y = Pair(view, y, "Pasture carries", carries + " of these", palette.Success, palette);
            }

            return y + 4f;
        }

        // ---------------------------------------------------------------------------------------
        // Auto slaughter limits
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Reloads the limit boxes when the species being shown changes.
        ///
        /// The boxes are shared static controls, so without this they would keep the previous species' numbers and
        /// the first keystroke would write them onto the new one.
        /// </summary>
        private static void Load(AnimalGroup group)
        {
            int map = group.Map?.uniqueID ?? -1;

            if (loadedFor == group.Def && loadedMap == map)
                return;

            loadedFor = group.Def;
            loadedMap = map;

            AutoSlaughterConfig config = group.Limits;

            Limits[0].Text = Number(config?.maxTotal);
            Limits[1].Text = Number(config?.maxFemales);
            Limits[2].Text = Number(config?.maxMales);
            Limits[3].Text = Number(config?.maxFemalesYoung);
            Limits[4].Text = Number(config?.maxMalesYoung);
        }

        /// <summary>A limit as text, where vanilla's own "no limit" is minus one and reads as blank.</summary>
        private static string Number(int? value)
        {
            if (value == null || value.Value < 0)
                return string.Empty;

            return value.Value.ToString();
        }

        private static float LimitFields(Rect view, float y, AnimalGroup group, UIColorPaletteDef palette,
            Action changed)
        {
            if (group.Kind != AnimalKind.Colony || group.Map == null)
                return y;

            AutoSlaughterConfig config = group.Limits;

            if (config == null)
                return y;

            y = Heading(view, y, "AUTO SLAUGHTER LIMITS", palette);

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(view.x, y, view.width, 30f),
                "Blank means no limit. Surplus animals are slaughtered oldest first.");

            y += 30f;

            for (int i = 0; i < Limits.Length; i++)
            {
                Rect row = new Rect(view.x, y, view.width, 24f);

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextSecondary;

                Widgets.Label(new Rect(row.x, row.y, row.width - 70f, row.height), LimitCaptions[i]);

                Text.Anchor = TextAnchor.UpperLeft;

                // The whole row's height, rather than the 22 this drew until 2026-08-22: a number field wants the
                // same air around its text as the caption beside it has.
                if (Limits[i].Draw(new Rect(row.xMax - 64f, row.y, 64f, row.height), palette))
                    Write(config, i, Limits[i].Text, group.Map, changed);

                y += 26f;
            }

            bool pregnant = config.allowSlaughterPregnant;

            if (UICheckboxControl.Draw(new Rect(view.x, y, view.width, 24f), ref pregnant, palette,
                    "Slaughter pregnant"))
            {
                config.allowSlaughterPregnant = pregnant;

                Notify(group.Map, changed);
            }

            y += 26f;

            bool bonded = config.allowSlaughterBonded;

            if (UICheckboxControl.Draw(new Rect(view.x, y, view.width, 24f), ref bonded, palette,
                    "Slaughter bonded"))
            {
                config.allowSlaughterBonded = bonded;

                Notify(group.Map, changed);
            }

            return y + 30f;
        }

        /// <summary>
        /// Writes one limit field.
        ///
        /// <b>Anything that is not a number is a blank, which is no limit.</b> The alternative is refusing the
        /// keystroke, which fights somebody who is halfway through typing, or keeping the old value, which means
        /// the field on screen and the limit in force disagree.
        /// </summary>
        private static void Write(AutoSlaughterConfig config, int field, string text, Map map, Action changed)
        {
            int value;

            if (!int.TryParse(text, out value) || value < 0)
                value = AutoSlaughterConfig.NoLimit;

            switch (field)
            {
                case 0:
                    config.maxTotal = value;
                    break;

                case 1:
                    config.maxFemales = value;
                    break;

                case 2:
                    config.maxMales = value;
                    break;

                case 3:
                    config.maxFemalesYoung = value;
                    break;

                default:
                    config.maxMalesYoung = value;
                    break;
            }

            Notify(map, changed);
        }

        /// <summary>
        /// Tells vanilla its cached slaughter list is stale.
        ///
        /// Without this the limits change and nothing happens until something else happens to dirty the cache,
        /// which reads as the field not working.
        /// </summary>
        private static void Notify(Map map, Action changed)
        {
            map?.autoSlaughterManager?.Notify_ConfigChanged();

            changed?.Invoke();
        }

        // ---------------------------------------------------------------------------------------
        // Actions
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Everything that can be set on the whole species, drawn rather than hidden behind a menu.
        ///
        /// <b>This replaced the Species menu button on 2026-08-22 at Aaron's request,</b> and the reasoning is
        /// worth keeping: nine settings behind one button showed none of their values, and finding out what the
        /// area was set to cost two clicks and a menu that vanished if the mouse strayed. Drawn here, every one of
        /// them reads back without being touched, which is the only real difference between a menu and a settings
        /// panel.
        ///
        /// <b>Three chips still open a list,</b> and <see cref="AnimalGroupActions"/> says why for each: the area
        /// because RimWorld's own menu draws the highlight on the map, the pen because it is however many
        /// buildings the player has built, and medical care because it is a five level enum. The switches are all
        /// tri-state, because a herd can disagree with itself and a group control that hides that is worse than no
        /// control.
        ///
        /// <b>The count is in the heading rather than in every label.</b> "Species settings" over a group of
        /// twelve is understood to mean all twelve; repeating "for all 12" on nine rows would be nine reminders of
        /// something the heading already said.
        /// </summary>
        private static float Settings(Rect view, float y, AnimalGroup group, UIColorPaletteDef palette,
            Action changed)
        {
            y = Heading(view, y, group.Count > 1 ? "SPECIES SETTINGS: ALL " + group.Count : "SPECIES SETTINGS",
                palette);

            if (group.Kind != AnimalKind.Colony)
                return Wildlife(view, y, group, palette, changed);

            // A travelling group has no map, so it has no areas to be offered and the write would go nowhere. Said
            // on the chip rather than left live and inert.
            string areaReason = group.Map == null
                ? "not while travelling"
                : AnimalGroupActions.AreaAssignable(group)
                    ? null
                    : AnimalGroupActions.AreaReason(group);

            y = AnimalPaneParts.Chip(view, y, "Area", AnimalGroupActions.AreaLabel(group), palette,
                () => AnimalGroupActions.ChooseArea(group, changed), areaReason);

            y = AnimalPaneParts.Chip(view, y, "Master", AnimalGroupActions.MasterLabel(group), palette,
                () => AnimalGroupActions.ChooseMaster(group, changed));

            y = AnimalPaneParts.Chip(view, y, "Medical care", AnimalGroupActions.CareLabel(group), palette,
                () => AnimalGroupActions.ChooseCare(group, changed));

            if (group.Map != null)
            {
                y = AnimalPaneParts.Chip(view, y, "Pen", AnimalGroupActions.PenLabel(group), palette,
                    () => AnimalGroupActions.ChoosePen(group, changed));
            }

            y = AnimalPaneParts.TriToggle(view, y, "Follows when drafted",
                AnimalGroupActions.FollowState(group, true), true, palette,
                value => AnimalGroupActions.SetFollow(group, true, value, changed));

            y = AnimalPaneParts.TriToggle(view, y, "Follows to field work",
                AnimalGroupActions.FollowState(group, false), true, palette,
                value => AnimalGroupActions.SetFollow(group, false, value, changed));

            y = Chore(view, y, group, TrainableDefOf.Forage, "Allowed to forage", palette, changed,
                p => p.playerSettings.animalForage, (p, on) => p.playerSettings.animalForage = on);

            y = Chore(view, y, group, TrainableDefOf.Dig, "Allowed to dig", palette, changed,
                p => p.playerSettings.animalDig, (p, on) => p.playerSettings.animalDig = on);

            y = Designation(view, y, group, DesignationDefOf.Slaughter, "Slaughter", palette, changed);

            y = Designation(view, y, group, DesignationDefOf.ReleaseAnimalToWild, "Release to the wild", palette,
                changed);

            return Select(view, y + 4f, group, palette);
        }

        /// <summary>
        /// One of Odyssey's trained chores, shown only once something in the group has learned it.
        ///
        /// Vanilla's own gate, and without it every chicken would carry two switches that do nothing.
        /// </summary>
        private static float Chore(Rect view, float y, AnimalGroup group, TrainableDef skill, string label,
            UIColorPaletteDef palette, Action changed, Func<Pawn, bool> read, Action<Pawn, bool> write)
        {
            if (!AnimalGroupActions.AnyTrained(group, skill))
                return y;

            return AnimalPaneParts.TriToggle(view, y, label,
                AnimalGroupActions.ChoreState(group, skill, read), true, palette,
                value => AnimalGroupActions.SetChore(group, skill, value, write, changed));
        }

        /// <summary>
        /// Slaughter or release, as a switch that says how many of the group already carry the order.
        ///
        /// The count is on the label because partial is the interesting state and "some of them" is not enough to
        /// act on: three of twelve and eleven of twelve are different situations.
        /// </summary>
        private static float Designation(Rect view, float y, AnimalGroup group, DesignationDef what, string label,
            UIColorPaletteDef palette, Action changed)
        {
            int ordered = AnimalGroupActions.OrderedCount(group, what);

            string caption = ordered > 0 && ordered < group.Count
                ? label + " (" + ordered + " of " + group.Count + ")"
                : label;

            return AnimalPaneParts.TriToggle(view, y, caption,
                AnimalGroupActions.DesignationState(group, what), true, palette,
                value => AnimalGroupActions.SetDesignated(group, what, value, changed));
        }

        /// <summary>
        /// The wildlife half: the whole group versions of the two orders the row carries as steppers.
        ///
        /// Buttons rather than switches, because these are not settings. "Hunt all" is an act with a count, and
        /// the row's stepper is how a number other than all is chosen.
        /// </summary>
        private static float Wildlife(Rect view, float y, AnimalGroup group, UIColorPaletteDef palette,
            Action changed)
        {
            y = Button(view, y, "Hunt all " + group.Count, palette,
                () => AnimalGroupActions.HuntAll(group, changed));

            y = Button(view, y, "Tame all " + group.Count, palette,
                () => AnimalGroupActions.TameAll(group, changed));

            if (group.HuntOrdered > 0 || group.TameOrdered > 0)
            {
                y = Button(view, y, "Cancel all orders", palette,
                    () => AnimalGroupActions.CancelOrders(group, changed));
            }

            return Select(view, y + 4f, group, palette);
        }

        private static float Select(Rect view, float y, AnimalGroup group, UIColorPaletteDef palette)
        {
            return Button(view, y, group.Count == 1 ? "Select it on the map" : "Select all " + group.Count
                + " on the map", palette, () => AnimalGroupActions.SelectAll(group));
        }

        private static float Button(Rect view, float y, string label, UIColorPaletteDef palette, Action clicked)
        {
            Rect button = new Rect(view.x, y, view.width, 28f);
            bool over = Mouse.IsOver(button);

            UIElementPainter.PaintButton(button, palette, over, over && Input.GetMouseButton(0));

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = palette.TextPrimary;

                Widgets.Label(button, label);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (Widgets.ButtonInvisible(button))
                clicked();

            return button.yMax + AnimalPaneParts.RowGap;
        }
    }
}
