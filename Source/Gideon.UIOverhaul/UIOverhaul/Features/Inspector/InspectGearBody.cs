using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Gideon.UIOverhaul.Shared;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// The Gear body: what is worn, what is carried, and what any of it is actually doing.
    ///
    /// <b>Durability becomes bars and armour becomes three numbers side by side,</b> which are the two readings
    /// vanilla's list makes you do arithmetic for. A duster at 41 percent and a hat at 12 are two rows of text in
    /// the game and two very different lengths of bar here, and the second is the form somebody can act on
    /// without reading either number.
    ///
    /// <b>The comfortable range is drawn as a range with the pawn standing on it.</b> "Minus fourteen to
    /// thirty-eight" is a fact nobody can use until they know where the pawn is; the mark on the bar is the whole
    /// point of the block.
    /// </summary>
    internal static class InspectGearBody
    {
        /// <summary>The span the temperature bar covers, in Celsius, which is where the mark is placed.</summary>
        private const float ColdEnd = -60f;

        private const float HotEnd = 60f;

        /// <summary>The lane kept at the right of every item row for its drop button.</summary>
        private const float DropLane = 22f;

        /// <summary>Side of the drop button itself, inside that lane.</summary>
        private const float DropButton = 18f;

        internal static float Draw(Rect view, Pawn pawn, UIColorPaletteDef palette)
        {
            Rect left;
            Rect right;

            InspectBodies.Columns(view, out left, out right);

            bool split = InspectBodies.Live(right);

            float leftY = Worn(left, view.y, pawn, palette);

            leftY = Carried(left, leftY, pawn, palette);

            Rect second = split ? right : left;
            float secondY = split ? view.y : leftY;

            secondY = Protection(second, secondY, pawn, palette);
            secondY = Temperature(second, secondY, pawn, palette);
            secondY = Wanting(second, secondY, pawn, palette);

            return (split ? Mathf.Max(leftY, secondY) : secondY) - view.y;
        }

        /// <summary>Every worn item as a durability bar, worst first, so the one about to be ruined leads.</summary>
        private static float Worn(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            if (pawn.apparel == null)
                return y;

            List<Apparel> worn = pawn.apparel.WornApparel;

            y = InspectPaneParts.Cap(view, y, "Worn",
                worn == null || worn.Count == 0 ? null : worn.Count + " pieces", palette);

            if (worn == null || worn.Count == 0)
                return InspectPaneParts.Note(view, y, "Wearing nothing.", palette) + InspectPaneParts.BlockGap;

            bool droppable = InspectGearDrop.CanControl(pawn);

            Rect content = droppable
                ? new Rect(view.x, view.y, view.width - DropLane, view.height)
                : view;

            for (int i = 0; i < worn.Count; i++)
            {
                Apparel item = worn[i];

                if (item == null)
                    continue;

                float condition = item.MaxHitPoints > 0 ? item.HitPoints / (float) item.MaxHitPoints : 1f;

                float before = y;

                y = InspectPaneParts.Meter(content, y, item.LabelCap, condition,
                    InspectPaneParts.Level(condition, palette), InspectPaneParts.Percent(condition),
                    InspectPaneParts.Level(condition, palette), palette);

                Rect row = new Rect(view.x, before, content.width, y - before);

                if (Mouse.IsOver(row))
                    TooltipHandler.TipRegion(row, (TipSignal) item.DescriptionDetailed);

                if (droppable)
                    InspectGearDrop.Button(new Rect(view.xMax - DropLane,
                        before + (y - before - DropButton) * 0.5f, DropButton, DropButton), pawn, item, false,
                        palette);
            }

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// The weapon, the inventory, and how close the pawn is to being weighed down.
        ///
        /// The mass bar carries a mark at the capacity rather than being scaled to it, so a pawn who is over the
        /// limit shows a bar past its own tick instead of a full bar that looks like every other full bar.
        /// </summary>
        private static float Carried(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            float mass = UIGuard.Try("Inspector.GearMass", () => MassUtility.GearAndInventoryMass(pawn), 0f, null);
            float capacity = UIGuard.Try("Inspector.GearCapacity", () => MassUtility.Capacity(pawn), 0f, null);

            y = InspectPaneParts.Cap(view, y, "Carried",
                capacity > 0f ? mass.ToString("0.0") + " / " + capacity.ToString("0") + " kg" : null, palette);

            if (capacity > 0f)
            {
                Rect lane = new Rect(view.x, y, view.width, InspectPaneParts.TrackHeight);

                float fraction = mass / capacity;

                InspectPaneParts.Track(lane, Mathf.Min(fraction, 1f),
                    fraction > 1f ? palette.Danger : InspectPaneParts.Level(1f - fraction, palette), palette);

                InspectPaneParts.Tick(lane, 1f, palette.TextPrimary, true);

                y = lane.yMax + 6f;
            }

            bool droppable = InspectGearDrop.CanControl(pawn);

            Rect content = droppable
                ? new Rect(view.x, view.y, view.width - DropLane, view.height)
                : view;

            ThingWithComps weapon = pawn.equipment != null ? pawn.equipment.Primary : null;

            if (weapon != null)
            {
                float before = y;

                y = InspectPaneParts.Entry(content, y, weapon.LabelCap, "equipped", palette.Accent,
                    QualityNote(weapon), palette);

                if (droppable)
                    InspectGearDrop.Button(new Rect(view.xMax - DropLane, before, DropButton, DropButton), pawn,
                        weapon, false, palette);
            }

            if (pawn.inventory != null && pawn.inventory.innerContainer != null)
            {
                ThingOwner container = pawn.inventory.innerContainer;

                for (int i = 0; i < container.Count; i++)
                {
                    Thing thing = container[i];

                    if (thing == null)
                        continue;

                    float before = y;

                    y = InspectPaneParts.Entry(content, y, thing.LabelCap,
                        (thing.GetStatValue(StatDefOf.Mass) * thing.stackCount).ToString("0.0") + " kg",
                        palette.TextDisabled, null, palette);

                    if (droppable)
                        InspectGearDrop.Button(new Rect(view.xMax - DropLane, before, DropButton, DropButton),
                            pawn, thing, true, palette);
                }
            }

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>An item's quality and material as one line, or nothing when it has neither.</summary>
        private static string QualityNote(Thing thing)
        {
            return UIGuard.Try("Inspector.GearQuality", () =>
            {
                QualityCategory quality;

                string material = thing.Stuff != null ? thing.Stuff.LabelAsStuff : null;

                if (!thing.TryGetQuality(out quality))
                    return material.NullOrEmpty() ? null : material.CapitalizeFirst();

                return material.NullOrEmpty()
                    ? quality.GetLabel().CapitalizeFirst() + " quality"
                    : quality.GetLabel().CapitalizeFirst() + " quality, " + material;
            }, null, null);
        }

        /// <summary>The three armour ratings, side by side, because they only mean anything next to each other.</summary>
        private static float Protection(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            y = InspectPaneParts.Cap(view, y, "Protection", null, palette);

            float sharp = Armor(pawn, StatDefOf.ArmorRating_Sharp);
            float blunt = Armor(pawn, StatDefOf.ArmorRating_Blunt);
            float heat = Armor(pawn, StatDefOf.ArmorRating_Heat);

            return InspectPaneParts.Pips(view, y,
                new[] { "Sharp", "Blunt", "Heat" },
                new[]
                {
                    sharp.ToStringPercent(),
                    blunt.ToStringPercent(),
                    heat.ToStringPercent()
                },
                new[]
                {
                    ArmorColor(sharp, palette),
                    ArmorColor(blunt, palette),
                    ArmorColor(heat, palette)
                }, palette) + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// How well protected this pawn actually is, by body part coverage.
        ///
        /// <b>Not <c>pawn.GetStatValue</c>, which was the bug.</b> That stat is the pawn's own natural armour --
        /// their skin, and any gene or hediff that toughens it -- and it does not know what they are wearing. A
        /// colonist in a vacsuit and an archon visor read 0%, 0%, 0%, which is the reading that made this
        /// obviously wrong on screen.
        ///
        /// <b>So vanilla's own calculation is reproduced,</b> from <c>ITab_Pawn_Gear.TryDrawOverallArmor</c>: for
        /// every body part, multiply out what gets through the natural armour and through each piece covering
        /// that part, weight by the part's share of the body, and sum. Halving each rating and doubling the total
        /// is vanilla's, not a simplification of it: an armour rating above 100% deflects rather than reduces, and
        /// this is how the game folds the two into one number. The result therefore runs to 200%, which is why it
        /// is not put through <c>Percent</c>, whose job is to clamp fractions.
        /// </summary>
        private static float Armor(Pawn pawn, StatDef stat)
        {
            return UIGuard.Try("Inspector.Armor", () =>
            {
                float natural = Mathf.Clamp01(pawn.GetStatValue(stat) / 2f);

                List<BodyPartRecord> parts = pawn.RaceProps.body.AllParts;
                List<Apparel> worn = pawn.apparel != null ? pawn.apparel.WornApparel : null;

                float covered = 0f;

                for (int i = 0; i < parts.Count; i++)
                {
                    float through = 1f - natural;

                    if (worn != null)
                    {
                        for (int j = 0; j < worn.Count; j++)
                        {
                            if (worn[j] != null && worn[j].def.apparel.CoversBodyPart(parts[i]))
                                through *= 1f - Mathf.Clamp01(worn[j].GetStatValue(stat) / 2f);
                        }
                    }

                    covered += parts[i].coverageAbs * (1f - through);
                }

                return Mathf.Clamp(covered * 2f, 0f, 2f);
            }, 0f, null);
        }

        /// <summary>
        /// Armour read as protection rather than as a fraction of something.
        ///
        /// The thresholds are not <see cref="InspectPaneParts.Level"/>'s, deliberately: twenty percent sharp
        /// armour is respectable and twenty percent of a need is an emergency, so sharing a scale would paint
        /// every unarmoured colonist red and stop the colour meaning anything.
        /// </summary>
        private static Color ArmorColor(float rating, UIColorPaletteDef palette)
        {
            if (rating >= 0.5f)
                return palette.Success;

            return rating >= 0.15f ? palette.Warning : palette.TextDisabled;
        }

        /// <summary>
        /// What this pawn's clothes can take, with where they are standing marked on it.
        ///
        /// The bar spans a fixed sixty below to sixty above zero rather than the pawn's own range, so two
        /// colonists compared side by side have their bars in the same units.
        /// </summary>
        private static float Temperature(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            FloatRange range = UIGuard.Try("Inspector.ComfortRange",
                () => pawn.ComfortableTemperatureRange(), new FloatRange(0f, 0f), null);

            if (Mathf.Approximately(range.min, range.max))
                return y;

            float ambient = UIGuard.Try("Inspector.Ambient", () => pawn.AmbientTemperature, 0f, null);

            y = InspectPaneParts.Cap(view, y, "Comfortable in",
                "now " + TemperatureText.Of(ambient), palette);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = range.Includes(ambient) ? palette.TextSecondary : palette.Warning;

                Widgets.Label(new Rect(view.x, y, view.width, UIFonts.LineHeightOf(GameFont.Tiny)),
                    TemperatureText.Of(range.min) + " to " + TemperatureText.Of(range.max));
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            y += UIFonts.LineHeightOf(GameFont.Tiny) + 2f;

            Rect lane = new Rect(view.x, y, view.width, InspectPaneParts.TrackHeight);

            UIElementPainter.FillRounded(lane, palette.SurfaceSunken);

            float from = Fraction(range.min);
            float to = Fraction(range.max);

            UIElementPainter.FillRounded(new Rect(lane.x + lane.width * from, lane.y,
                Mathf.Max(2f, lane.width * (to - from)), lane.height), palette.AccentMuted);

            InspectPaneParts.Tick(lane, Fraction(ambient),
                range.Includes(ambient) ? palette.Accent : palette.Danger, true);

            return lane.yMax + InspectPaneParts.BlockGap;
        }

        /// <summary>Where a temperature sits on the fixed span the bar covers.</summary>
        private static float Fraction(float celsius)
        {
            return Mathf.Clamp01((celsius - ColdEnd) / (HotEnd - ColdEnd));
        }

        /// <summary>The outfit policy, and the worn item the policy should be replacing.</summary>
        private static float Wanting(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            string outfit = UIGuard.Try("Inspector.Outfit",
                () => pawn.outfits != null && pawn.outfits.CurrentApparelPolicy != null
                    ? pawn.outfits.CurrentApparelPolicy.label
                    : null, null, null);

            Apparel worst = InspectOverview.WorstApparel(pawn);

            bool ruined = worst != null && worst.MaxHitPoints > 0
                                        && worst.HitPoints / (float) worst.MaxHitPoints < 0.3f;

            if (outfit.NullOrEmpty() && !ruined)
                return y;

            y = InspectPaneParts.Cap(view, y, "Wanting", null, palette);

            if (ruined)
                y = InspectPaneParts.Fact(view, y, worst.def.label.CapitalizeFirst(),
                    "replace, " + InspectPaneParts.Percent(worst.HitPoints / (float) worst.MaxHitPoints),
                    palette.Danger, palette);

            if (!outfit.NullOrEmpty())
                y = InspectPaneParts.Fact(view, y, "Outfit", outfit, palette.TextPrimary, palette);

            return y + InspectPaneParts.BlockGap;
        }
    }
}
