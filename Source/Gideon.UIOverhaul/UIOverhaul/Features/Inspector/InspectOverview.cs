using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Animals;
using Gideon.UIOverhaul.Features.Pawns;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// The Overview body: the tab the pane opens on, and the only one of the seven that RimWorld does not have.
    ///
    /// <b>It exists because the pane can be dragged tall.</b> Everything on it is already somewhere in the game,
    /// one tab away and one click each; the point is to answer the four questions a player actually has on
    /// selecting a colonist -- is she hungry, is she about to break, is she hurt, what is she good at -- without
    /// any of the clicks. Nothing here is invented and nothing here is authoritative: each block is a filtered
    /// view of the tab that owns it, and that tab is one chip away for the rest of it.
    ///
    /// <b>Two columns, or one when the pane is narrow.</b> A second column of ellipses is worse than a longer
    /// first one, so below <see cref="InspectBodies"/>'s threshold the right column's blocks simply continue
    /// under the left column's.
    /// </summary>
    internal static class InspectOverview
    {
        /// <summary>How many needs the overview shows before it stops. The Needs body shows all of them.</summary>
        private const int NeedsShown = 5;

        /// <summary>How many rows the body block shows before it stops counting out loud.</summary>
        private const int BodyRowsShown = 5;

        /// <summary>Reused so a frame does not allocate a list per draw.</summary>
        private static readonly List<PawnCapacityDef> Capacities = new List<PawnCapacityDef>();

        /// <summary>
        /// A pawn's overview. Returns how tall it came out, which is what the scroll view is sized from.
        /// </summary>
        internal static float DrawPawn(Rect view, Pawn pawn, UIColorPaletteDef palette)
        {
            Rect left;
            Rect right;

            InspectBodies.Columns(view, out left, out right);

            bool split = InspectBodies.Live(right);
            bool animal = pawn.RaceProps != null && pawn.RaceProps.Animal;
            bool dead = UIGuard.Try("Inspector.IsDead", () => pawn.Dead, false, null);

            // <b>A corpse gets the same panel with the blocks that still mean something.</b> Needs are frozen at
            // the moment of death and an allowed area is nobody's business now, so those two go; what is left --
            // skills, gear, wounds, traits -- is exactly what somebody looking at a body wants, and it is the
            // reason a corpse should not drop to a hit points bar the way it used to.
            float leftY = dead
                ? Remains(left, view.y, pawn, palette)
                : Vitals(left, view.y, pawn, palette);

            if (!dead)
                leftY = Needs(left, leftY, pawn, palette, NeedsShown);

            leftY = animal && !dead
                ? Training(left, leftY, pawn, palette)
                : Body(left, leftY, pawn, palette);

            Rect second = split ? right : left;
            float secondY = split ? view.y : leftY + InspectPaneParts.BlockGap;

            if (animal && !dead)
            {
                secondY = AnimalAssignment(second, secondY, pawn, palette);
                secondY = Yield(second, secondY, pawn, palette);
            }
            else
            {
                secondY = Skills(second, secondY, pawn, palette);
                secondY = Carrying(second, secondY, pawn, palette);

                if (dead)
                    secondY = Character(second, secondY, pawn, palette);
            }

            float y = split ? Mathf.Max(leftY, secondY) : secondY;

            // A corpse has no standing orders to show, and drawing the chips dead would only invite clicking
            // them.
            if (!animal && !dead)
                y = Assignment(view, y, pawn, palette);

            return y - view.y;
        }

        /// <summary>
        /// The two bars everything else is read against: how hurt they are, and how close they are to breaking.
        ///
        /// <b>Mood needed drawing explicitly, and that is why it was missing.</b> RimWorld's own
        /// <c>Mood</c> need declares <c>showOnNeedList false</c>, because vanilla draws it as the headline of its
        /// Needs tab rather than as one row among the others. Every need list in this mod filters on
        /// <c>ShowOnNeedList</c> and so correctly left it out, which meant the single most important bar about a
        /// colonist appeared nowhere at all.
        ///
        /// <b>Health gets the four step scale</b> from <see cref="InspectPaneParts.Vital"/> rather than the three
        /// step one the needs use: a need at 60 percent is fine and a colonist at 60 percent is not.
        /// </summary>
        private static float Vitals(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            bool anything = false;

            float health = UIGuard.Try("Inspector.Vitals", () =>
                pawn.health != null ? pawn.health.summaryHealth.SummaryHealthPercent : -1f, -1f, null);

            Need_Mood mood = UIGuard.Try("Inspector.VitalsMood",
                () => pawn.needs != null ? pawn.needs.mood : null, null, null);

            if (health < 0f && mood == null)
                return y;

            y = InspectPaneParts.Cap(view, y, "Condition", null, palette);

            if (health >= 0f)
            {
                y = InspectPaneParts.Need(view, y, "Health", InspectPaneParts.Percent(health),
                    InspectPaneParts.Vital(health, palette), health, InspectPaneParts.Vital(health, palette),
                    null, null, palette);

                anything = true;
            }

            if (mood != null)
            {
                float level = UIGuard.Try("Inspector.MoodLevel", () => mood.CurLevelPercentage, 0f, null);

                // The break thresholds ride on this bar for the same reason they ride on the Needs body's:
                // 34 percent is comfortable for one colonist and a tantrum for another.
                y = InspectPaneParts.Need(view, y, "Mood", InspectPaneParts.Percent(level) + Arrow(mood),
                    MoodColor(pawn, level, palette), level, palette.Mood, MoodTicks(pawn), null, palette);

                anything = true;
            }

            return anything ? y + InspectPaneParts.BlockGap : y;
        }

        /// <summary>
        /// What is left of somebody: how long they have been dead, how far the body has gone, and who they were
        /// with.
        ///
        /// <b>The corpse's own rot comp rather than a sum of ours.</b> A body in a freezer and one in the rain
        /// have been dead the same length of time and are in completely different states, and only the comp
        /// knows which.
        /// </summary>
        private static float Remains(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            Corpse corpse = UIGuard.Try("Inspector.Corpse", () => pawn.Corpse, null, null);

            y = InspectPaneParts.Cap(view, y, "Remains",
                UIGuard.Try<string>("Inspector.DeadFor", () => corpse != null && corpse.Age > 0
                    ? corpse.Age.ToStringTicksToPeriod(false, false, false)
                    : null, null, null), palette);

            CompRottable rot = corpse != null
                ? UIGuard.Try("Inspector.CorpseRot", corpse.TryGetComp<CompRottable>, null, null)
                : null;

            if (rot != null)
            {
                float progress = UIGuard.Try("Inspector.CorpseRotPct", () => rot.RotProgressPct, 0f, null);
                RotStage stage = UIGuard.Try("Inspector.CorpseStage", () => rot.Stage, RotStage.Fresh, null);

                y = InspectPaneParts.Need(view, y, stage.ToString(), InspectPaneParts.Percent(progress),
                    stage == RotStage.Fresh ? palette.TextSecondary : palette.Warning, progress,
                    InspectPaneParts.Level(1f - progress, palette), null, null, palette);
            }

            Faction faction = UIGuard.Try("Inspector.CorpseFaction", () => pawn.Faction, null, null);

            if (faction != null)
                y = InspectPaneParts.Fact(view, y, "Faction", faction.Name,
                    faction.IsPlayer ? palette.Accent : palette.TextSecondary, palette);

            y = InspectPaneParts.Fact(view, y, "Race",
                UIGuard.Try<string>("Inspector.CorpseRace",
                    () => pawn.def != null ? pawn.def.LabelCap.ToString() : null, null, null),
                palette.TextSecondary, palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// Traits and what they could not do, for a body.
        ///
        /// <b>On the overview only for the dead,</b> because for a living pawn it is one chip away on the Bio
        /// body and the space is better spent on their needs. For a corpse there are no needs, and who this
        /// person was is most of what is left to say about them.
        /// </summary>
        private static float Character(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            if (pawn.story == null || pawn.story.traits == null || pawn.story.traits.allTraits == null)
                return y;

            List<Trait> traits = pawn.story.traits.allTraits;

            if (traits.Count == 0)
                return y;

            y = InspectPaneParts.Cap(view, y, "Traits", traits.Count.ToString(), palette);

            float x = view.x;
            float rowHeight = 0f;

            for (int i = 0; i < traits.Count; i++)
            {
                Trait trait = traits[i];

                if (trait == null)
                    continue;

                InspectPaneParts.Chip(view, ref x, ref y, ref rowHeight,
                    UIGuard.Try("Inspector.CorpseTrait", () => trait.LabelCap, "?", null),
                    palette.Accent, false, palette);
            }

            return y + rowHeight + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// The needs, in RimWorld's own order, with the mood bar carrying this pawn's own break thresholds.
        ///
        /// <b>The ticks are the reason the block is worth the space.</b> A mood of 34 percent is comfortable for
        /// one colonist and a tantrum for another, because the threshold is a stat and traits move it. A bar
        /// without them is a number nobody can act on; a bar with them says at a glance how much room is left.
        /// </summary>
        private static float Needs(Rect view, float y, Pawn pawn, UIColorPaletteDef palette, int max)
        {
            if (pawn.needs == null)
                return y;

            List<Need> all = pawn.needs.AllNeeds;

            if (all == null || all.Count == 0)
                return y;

            // No mood in the caption any more: it has its own bar in the Condition block above, and a percentage
            // repeated two inches apart is one the eye stops reading in both places.
            y = InspectPaneParts.Cap(view, y, "Needs", null, palette);

            int drawn = 0;

            for (int i = 0; i < all.Count && drawn < max; i++)
            {
                Need need = all[i];

                if (need == null || !need.ShowOnNeedList)
                    continue;

                drawn++;

                y = DrawNeed(view, y, need, pawn, palette, null);
            }

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// One need row, with its thresholds when it has any and RimWorld's own tip string behind it.
        ///
        /// Shared with the Needs body, which draws the same rows and adds the notes.
        /// </summary>
        internal static float DrawNeed(Rect view, float y, Need need, Pawn pawn, UIColorPaletteDef palette,
            string note)
        {
            float level = need.CurLevelPercentage;
            bool isMood = need is Need_Mood;

            float[] ticks = isMood ? MoodTicks(pawn) : null;

            Color fill = isMood ? palette.Mood : InspectPaneParts.Level(level, palette);
            Color value = isMood ? MoodColor(pawn, level, palette) : InspectPaneParts.Level(level, palette);

            float before = y;

            // The arrow is RimWorld's own reading of which way the need is going, so a food bar that is about to
            // matter is told apart from one that has just been filled.
            string arrow = Arrow(need);

            y = InspectPaneParts.Need(view, y, need.LabelCap,
                InspectPaneParts.Percent(level) + arrow, value, level, fill, ticks, note, palette);

            Rect row = new Rect(view.x, before, view.width, y - before);

            if (Mouse.IsOver(row))
            {
                string tip = UIGuard.Try("Inspector.NeedTip", need.GetTipString, null, null);

                if (!tip.NullOrEmpty())
                    TooltipHandler.TipRegion(row, (TipSignal) tip);
            }

            return y;
        }

        /// <summary>Which way a need is moving, in the same three states vanilla's own arrows show.</summary>
        private static string Arrow(Need need)
        {
            int direction = UIGuard.Try("Inspector.NeedArrow", () => need.GUIChangeArrow, 0, null);

            if (direction > 0)
                return " +";

            return direction < 0 ? " -" : string.Empty;
        }

        /// <summary>
        /// This pawn's three break points as fractions, heaviest first.
        ///
        /// Extreme leads because <see cref="InspectPaneParts.Need"/> draws the first tick heavier than the rest,
        /// and extreme is the one there is no coming back from.
        /// </summary>
        internal static float[] MoodTicks(Pawn pawn)
        {
            return UIGuard.Try("Inspector.MoodTicks", () =>
            {
                MentalBreaker breaker = pawn.mindState != null ? pawn.mindState.mentalBreaker : null;

                if (breaker == null)
                    return null;

                return new[]
                {
                    breaker.BreakThresholdExtreme,
                    breaker.BreakThresholdMajor,
                    breaker.BreakThresholdMinor
                };
            }, null, null);
        }

        /// <summary>Mood read against this pawn's own thresholds rather than against a flat percentage.</summary>
        internal static Color MoodColor(Pawn pawn, float level, UIColorPaletteDef palette)
        {
            return UIGuard.Try("Inspector.MoodColor", () =>
            {
                MentalBreaker breaker = pawn.mindState != null ? pawn.mindState.mentalBreaker : null;

                if (breaker == null)
                    return palette.TextPrimary;

                if (level <= breaker.BreakThresholdMajor)
                    return palette.Danger;

                return level <= breaker.BreakThresholdMinor ? palette.Warning : palette.TextPrimary;
            }, palette.TextPrimary, null);
        }

        /// <summary>
        /// What is wrong with this pawn's body, and nothing about what is right with it.
        ///
        /// <b>Healthy capacities are left out, which Aaron chose deliberately when he approved the mockup.</b> A
        /// column of twelve rows all reading 100 percent is furniture: it takes the space the two that matter
        /// need and trains the eye to skip the block. What is left is the impaired ones, any immunity race, and
        /// bleeding, which is the one fact with a clock on it.
        /// </summary>
        private static float Body(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            if (pawn.health == null)
                return y;

            // The overall percentage is the Health bar's job now, up in the Condition block. This caption stays
            // bare rather than repeating it.
            y = InspectPaneParts.Cap(view, y, "Body", null, palette);

            int rows = 0;

            float bleed = UIGuard.Try("Inspector.BleedRate",
                () => pawn.health.hediffSet.BleedRateTotal, 0f, null);

            if (bleed > 0.01f)
            {
                int ticks = UIGuard.Try("Inspector.BleedOut",
                    () => pawn.health.hediffSet.BleedRateTotal > 0f
                        ? HealthUtility.TicksUntilDeathDueToBloodLoss(pawn)
                        : int.MaxValue, int.MaxValue, null);

                y = InspectPaneParts.Fact(view, y, "Bleeding",
                    ticks < int.MaxValue
                        ? "dead in " + ticks.ToStringTicksToPeriod(false, false, true, true)
                        : InspectPaneParts.Percent(bleed) + " a day",
                    palette.Danger, palette);

                rows++;
            }

            rows += Impairments(view, ref y, pawn, palette, BodyRowsShown - rows);
            rows += Immunities(view, ref y, pawn, palette, BodyRowsShown - rows);

            if (rows == 0)
                y = InspectPaneParts.Note(view, y, "Nothing hurt and nothing impaired.", palette) + 2f;

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// The capacities that are not at full, worst first.
        ///
        /// <see cref="PawnCapacityDef.CanShowOnPawn"/> is what decides which apply, rather than a list of ours:
        /// a mechanoid, an animal and a person answer differently and RimWorld already knows how.
        /// </summary>
        private static int Impairments(Rect view, ref float y, Pawn pawn, UIColorPaletteDef palette, int max)
        {
            if (max <= 0)
                return 0;

            Capacities.Clear();

            UIGuard.Try("Inspector.Capacities", () =>
            {
                List<PawnCapacityDef> defs = DefDatabase<PawnCapacityDef>.AllDefsListForReading;

                for (int i = 0; i < defs.Count; i++)
                {
                    PawnCapacityDef def = defs[i];

                    if (def == null || !def.CanShowOnPawn(pawn))
                        continue;

                    if (pawn.health.capacities.GetLevel(def) < 0.995f)
                        Capacities.Add(def);
                }

                Capacities.SortBy(def => pawn.health.capacities.GetLevel(def));
            }, "The inspect pane cannot list impaired capacities for this pawn.");

            int drawn = 0;

            for (int i = 0; i < Capacities.Count && drawn < max; i++)
            {
                PawnCapacityDef def = Capacities[i];
                float level = pawn.health.capacities.GetLevel(def);

                y = InspectPaneParts.Fact(view, y, def.GetLabelFor(pawn).CapitalizeFirst(),
                    InspectPaneParts.Percent(level), InspectPaneParts.Level(level, palette), palette);

                drawn++;
            }

            Capacities.Clear();

            return drawn;
        }

        /// <summary>
        /// Every disease the pawn is racing, with both numbers.
        ///
        /// <b>Both numbers, because this is the one health fact with a deadline.</b> An immunity on its own says
        /// nothing: 61 percent is winning against a severity of 44 and losing against one of 70, and which of
        /// those it is decides whether a bed and a doctor are needed this hour or this quadrum.
        /// </summary>
        private static int Immunities(Rect view, ref float y, Pawn pawn, UIColorPaletteDef palette, int max)
        {
            if (max <= 0)
                return 0;

            int drawn = 0;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;

            for (int i = 0; i < hediffs.Count && drawn < max; i++)
            {
                Hediff hediff = hediffs[i];

                if (hediff == null || !hediff.Visible)
                    continue;

                HediffComp_Immunizable comp =
                    UIGuard.Try("Inspector.Immunity", hediff.TryGetComp<HediffComp_Immunizable>, null, null);

                if (comp == null)
                    continue;

                float immunity = comp.Immunity;
                bool winning = immunity > hediff.Severity;

                y = InspectPaneParts.Fact(view, y, "Immunity vs " + hediff.LabelBase,
                    InspectPaneParts.Percent(immunity) + " / " + InspectPaneParts.Percent(hediff.Severity),
                    winning ? palette.Success : palette.Danger, palette);

                drawn++;
            }

            return drawn;
        }

        /// <summary>
        /// Every skill, dimmed where it does not apply, in two sub-columns.
        ///
        /// <b>The whole grid rather than the good ones.</b> A filtered list answers "what is she good at" and
        /// leaves "can she do this at all" unanswered, which is the question being asked when somebody is about
        /// to hand out a job. Incapable reads as a dash rather than a zero, because those are different facts.
        /// </summary>
        private static float Skills(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            if (pawn.skills == null || pawn.skills.skills == null || pawn.skills.skills.Count == 0)
                return y;

            y = InspectPaneParts.Cap(view, y, "Skills", "passions", palette);

            List<SkillRecord> skills = pawn.skills.skills;

            bool two = view.width >= 220f;
            int columns = two ? 2 : 1;
            float columnWidth = (view.width - (columns - 1) * 8f) / columns;
            int rows = Mathf.CeilToInt(skills.Count / (float) columns);
            float line = UIFonts.LineHeightOf(GameFont.Tiny);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;

                for (int i = 0; i < skills.Count; i++)
                {
                    SkillRecord skill = skills[i];

                    if (skill == null || skill.def == null)
                        continue;

                    int column = i / rows;
                    int row = i % rows;

                    Rect cell = new Rect(view.x + column * (columnWidth + 8f), y + row * line, columnWidth, line);

                    Text.Anchor = TextAnchor.MiddleLeft;
                    GUI.color = skill.TotallyDisabled ? palette.TextDisabled : palette.TextSecondary;

                    UIRichText.Label(new Rect(cell.x, cell.y, cell.width - 40f, cell.height),
                        skill.def.skillLabel.CapitalizeFirst());

                    Text.Anchor = TextAnchor.MiddleRight;
                    GUI.color = skill.TotallyDisabled
                        ? palette.TextDisabled
                        : skill.Level >= 10
                            ? palette.TextPrimary
                            : palette.TextSecondary;

                    Widgets.Label(new Rect(cell.xMax - 38f, cell.y, 18f, cell.height),
                        skill.TotallyDisabled ? "-" : skill.Level.ToStringCached());

                    PassionMark(new Rect(cell.xMax - 18f, cell.y, 18f, cell.height), skill, palette);

                    if (Mouse.IsOver(cell))
                        TooltipHandler.TipRegion(cell, (TipSignal) SkillTip(skill));
                }
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            return y + rows * line + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// The passion mark, in RimWorld's own two icons.
        ///
        /// Its own textures rather than ours: a player reads a flame as a passion everywhere else in the game,
        /// and drawing a different mark here would be a second vocabulary for a fact they already know.
        /// </summary>
        private static void PassionMark(Rect rect, SkillRecord skill, UIColorPaletteDef palette)
        {
            if (skill.passion == Passion.None)
                return;

            Texture2D icon = skill.passion == Passion.Major
                ? SkillUI.PassionMajorIcon
                : SkillUI.PassionMinorIcon;

            if (icon == null)
                return;

            Color previous = GUI.color;

            GUI.color = skill.passion == Passion.Major ? palette.Warning : palette.AccentMuted;

            GUI.DrawTexture(new Rect(rect.x + 2f, rect.center.y - 7f, 14f, 14f), icon);

            GUI.color = previous;
        }

        /// <summary>What a skill row says on hover: the level, the passion and how fast it is still learning.</summary>
        private static string SkillTip(SkillRecord skill)
        {
            return UIGuard.Try("Inspector.SkillTip", () =>
            {
                if (skill.TotallyDisabled)
                    return skill.def.LabelCap + ": " + "IncapableOf".Translate();

                return skill.def.LabelCap + ": " + skill.Level + "\n" + skill.passion.GetLabel();
            }, skill.def.LabelCap, null);
        }

        /// <summary>What the pawn is holding, wearing and carrying, in three lines rather than three lists.</summary>
        private static float Carrying(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            bool any = pawn.equipment != null || pawn.apparel != null || pawn.inventory != null;

            if (!any)
                return y;

            y = InspectPaneParts.Cap(view, y, "Carrying", null, palette);

            ThingWithComps weapon = pawn.equipment != null ? pawn.equipment.Primary : null;

            y = InspectPaneParts.Fact(view, y, "Weapon",
                weapon != null ? weapon.LabelCap.ToString() : "unarmed",
                weapon != null ? palette.TextPrimary : palette.TextDisabled, palette);

            Apparel worst = WorstApparel(pawn);

            if (worst != null)
            {
                float condition = worst.MaxHitPoints > 0 ? worst.HitPoints / (float) worst.MaxHitPoints : 1f;

                y = InspectPaneParts.Fact(view, y, worst.def.label.CapitalizeFirst(),
                    InspectPaneParts.Percent(condition), InspectPaneParts.Level(condition, palette), palette);
            }

            if (pawn.inventory != null && pawn.inventory.innerContainer != null)
            {
                int count = pawn.inventory.innerContainer.Count;

                float mass = UIGuard.Try("Inspector.InventoryMass",
                    () => MassUtility.GearAndInventoryMass(pawn), 0f, null);

                y = InspectPaneParts.Fact(view, y, "Inventory",
                    count == 0 ? "empty" : count + " items, " + mass.ToString("0.0") + " kg",
                    count == 0 ? palette.TextDisabled : palette.TextPrimary, palette);
            }

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// The worn item closest to falling apart, which is the only one worth a row on a summary.
        ///
        /// The Gear body lists all of them; this answers "is anything about to be ruined" without the list.
        /// </summary>
        internal static Apparel WorstApparel(Pawn pawn)
        {
            if (pawn.apparel == null)
                return null;

            return UIGuard.Try("Inspector.WorstApparel", () =>
            {
                List<Apparel> worn = pawn.apparel.WornApparel;
                Apparel worst = null;
                float lowest = 2f;

                for (int i = 0; i < worn.Count; i++)
                {
                    Apparel item = worn[i];

                    if (item == null || item.MaxHitPoints <= 0 || !item.def.useHitPoints)
                        continue;

                    float condition = item.HitPoints / (float) item.MaxHitPoints;

                    if (condition >= lowest)
                        continue;

                    lowest = condition;
                    worst = item;
                }

                return worst;
            }, null, null);
        }

        /// <summary>
        /// The standing orders, as the same chips the pawns tab uses.
        ///
        /// <b>A band under both columns rather than a block inside one.</b> A chip is as wide as its own text and
        /// <see cref="PolicyStrip"/> drops the ones that do not fit, so half the width would silently lose the
        /// last two or three of them. Across the whole pane all five fit, which is the arrangement that makes
        /// them worth having here at all.
        /// </summary>
        private static float Assignment(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            float height = UIGuard.Try("Inspector.PolicyHeight", () => PolicyStrip.HeightFor(pawn), 0f, null);

            if (height <= 0f)
                return y;

            y = InspectPaneParts.Cap(view, y + InspectPaneParts.BlockGap, "Assignment", null, palette);

            PolicyStrip.Draw(new Rect(view.x, y, view.width, height), pawn, palette);

            return y + height;
        }

        /// <summary>
        /// An animal's training, drawn with the animals tab's own pills.
        ///
        /// Nothing is reimplemented here: these are the same controls at the same size, so a request set from the
        /// inspect pane and one set from the animals tab are visibly the same act.
        /// </summary>
        private static float Training(Rect view, float y, Pawn animal, UIColorPaletteDef palette)
        {
            List<TrainableDef> kinds = UIGuard.Try("Inspector.TrainingKinds",
                () => AnimalTrainingBoxes.KindsFor(animal), null, null);

            if (kinds == null || kinds.Count == 0)
                return y;

            y = InspectPaneParts.Cap(view, y, "Training", kinds.Count + " requests", palette);

            float width = Mathf.Min(view.width, AnimalTrainingBoxes.WidthFor(kinds.Count));

            AnimalTrainingBoxes.DrawForAnimal(new Rect(view.x, y, width, AnimalTrainingBoxes.PillHeight), animal,
                palette, null);

            return y + AnimalTrainingBoxes.PillHeight + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// Where an animal is allowed, who it answers to and how it is treated, using the same readers the
        /// animals tab uses so the two cannot word the same fact differently.
        ///
        /// <b>Area and pen are set from here as well as read, asked for on 2026-08-24.</b> Both go through the
        /// controls the animals tab already uses -- <c>PawnAreas.Choose</c> and
        /// <c>AnimalGroupActions.ChoosePenFor</c> -- so an area set from the pane and one set from the tab are
        /// the same act, and neither can drift from the other. Selecting the animal is already the fastest way to
        /// find it, and having to open a tab afterwards to act on what the pane just told you was the gap.
        ///
        /// <b>The area row is a plain fact when the animal cannot take one,</b> with <c>PawnAreas.Reason</c> in
        /// the tooltip. A menu that opens onto a setting the game will refuse is worse than a row that says why
        /// it is not offering one -- and for livestock that reason is usually the pen row directly below.
        ///
        /// <b>Master is left alone.</b> It was not asked for, and it is the one of the three that changes what a
        /// colonist does rather than where an animal goes: a master is a bonded handler, and reassigning one from
        /// a passing glance at the pane is a heavier act than it looks.
        /// </summary>
        private static float AnimalAssignment(Rect view, float y, Pawn animal, UIColorPaletteDef palette)
        {
            if (animal.playerSettings == null || animal.Faction != Faction.OfPlayer)
                return y;

            y = InspectPaneParts.Cap(view, y, "Assignment", null, palette);

            bool areas = UIGuard.Try("Inspector.AnimalAreaAssignable", () => PawnAreas.Assignable(animal), false,
                null);

            y = InspectPaneParts.Choice(view, y, "Area",
                UIGuard.Try("Inspector.AnimalArea", () => PawnAreas.Label(animal), null, null),
                areas ? palette.TextPrimary : palette.TextDisabled, palette,
                areas ? (System.Action) (() => PawnAreas.Choose(animal)) : null,
                areas ? null : UIGuard.Try("Inspector.AnimalAreaReason", () => PawnAreas.Reason(animal), null,
                    null));

            Pawn master = animal.playerSettings.Master;

            y = InspectPaneParts.Fact(view, y, "Master",
                master == null ? "none" : master.LabelShortCap.ToString(),
                master == null ? palette.TextDisabled : palette.TextPrimary, palette);

            CompAnimalPenMarker pen = UIGuard.Try("Inspector.AnimalPen", () => AnimalFacts.Pen(animal), null, null);

            if (animal.Roamer)
                y = InspectPaneParts.Choice(view, y, "Pen",
                    pen != null && pen.parent != null ? pen.parent.LabelShortCap.ToString() : "none",
                    pen != null ? palette.TextPrimary : palette.Warning, palette,
                    () => AnimalGroupActions.ChoosePenFor(animal),
                    "Choose which pen takes this species.\n\nA pen states which species it accepts rather "
                    + "than which animals, so this allows every " + animal.def.label + " in the pen you pick "
                    + "and disallows them in the rest. The whole species moves, not just this one.");

            y = InspectPaneParts.Fact(view, y, "Medical care",
                UIGuard.Try("Inspector.AnimalCare", () => animal.playerSettings.medCare.GetLabel(), null, null),
                palette.TextPrimary, palette);

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>What an animal is worth alive and what it is worth dead, which is the whole of livestock.</summary>
        private static float Yield(Rect view, float y, Pawn animal, UIColorPaletteDef palette)
        {
            AnimalProduce produce = UIGuard.Try("Inspector.AnimalProduce",
                () => AnimalFacts.Produce(animal), default(AnimalProduce), null);

            float meat = UIGuard.Try("Inspector.AnimalMeat", () => AnimalFacts.Meat(animal), 0f, null);

            if (!produce.Any && meat <= 0f)
                return y;

            y = InspectPaneParts.Cap(view, y, "Yield", null, palette);

            if (produce.Any)
                y = InspectPaneParts.Fact(view, y, produce.ResourceLabel.CapitalizeFirst(),
                    produce.Ready
                        ? "ready"
                        : produce.DaysLeft > 0f
                            ? "in " + produce.DaysLeft.ToString("0.0") + " days"
                            : produce.PerDay.ToString("0.0") + " a day",
                    produce.Ready ? palette.Success : palette.TextSecondary, palette);

            if (meat > 0f)
                y = InspectPaneParts.Fact(view, y, "Meat", Mathf.RoundToInt(meat).ToString(),
                    palette.TextSecondary, palette);

            return y + InspectPaneParts.BlockGap;
        }
    }
}
