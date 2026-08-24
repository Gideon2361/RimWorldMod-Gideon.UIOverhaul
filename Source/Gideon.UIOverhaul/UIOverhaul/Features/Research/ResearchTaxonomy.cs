using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// Which band a research project belongs to, and why.
    ///
    /// <b>Classified by what a project unlocks, never by its name and never by its mod.</b> Keyword matching on a
    /// label cannot work: it breaks on every translation, it cannot separate "bioferrite weaponry" from "beam
    /// weapons" without a hand-written list, and it fails on every mod that names things in its own voice. The mod
    /// cannot work either -- that is the whole fault this replaces, and the scan of 2026-08-23 measured it: across
    /// 354 projects only Mechanoids came from a single source, while Medicine drew on four and Flight split five
    /// and five between Core and Odyssey.
    ///
    /// <b><c>ResearchProjectDef.UnlockedDefs</c> is the signal that works.</b> RimWorld builds it by walking every
    /// <c>ThingDef</c>, <c>RecipeDef</c>, <c>TerrainDef</c> and <c>PsychicRitualDef</c> in the game and collecting
    /// the ones naming this project as a prerequisite. A mod author has to declare those links for their content to
    /// be gated at all, so the list is populated whether or not they ever heard of this taxonomy. That is what
    /// makes this work for a mod nobody has written yet.
    ///
    /// <b>One band per project, decided by a fixed order.</b> Duplicating a node would break arrow drawing and
    /// selection, so the tests run in <see cref="ResearchBand"/>'s declaration order and the first match wins.
    /// Some projects are honestly two things -- bionic replacements is Medicine and Production both -- and the
    /// order decides. <see cref="ReasonFor"/> exists so that decision is legible rather than mysterious.
    ///
    /// <b>Categories are asked through <c>IsWithinCategory</c>, which walks the parent chain.</b> A mod's own food
    /// category filed under Foods still answers yes, so the test covers content it has never seen. Testing
    /// <c>thingCategories</c> directly would only ever match vanilla's own leaves.
    ///
    /// <b>Cached per project, because this walks def databases.</b> <c>UnlockedDefs</c> builds four LINQ queries
    /// over every def in the game on its first call per project, and the panel asks for a band once per node per
    /// frame. Cleared by <see cref="Invalidate"/> when the def database could have changed, which in practice is
    /// never during a session -- so the cache is effectively built once and read forever.
    /// </summary>
    internal static class ResearchTaxonomy
    {
        private static readonly Dictionary<ResearchProjectDef, ResearchBand> bands =
            new Dictionary<ResearchProjectDef, ResearchBand>();

        private static readonly Dictionary<ResearchProjectDef, string> reasons =
            new Dictionary<ResearchProjectDef, string>();

        internal static void Invalidate()
        {
            bands.Clear();
            reasons.Clear();
        }

        /// <summary>The band this project belongs to. Never throws, and falls back to Other.</summary>
        internal static ResearchBand BandOf(ResearchProjectDef project)
        {
            if (project == null)
                return ResearchBand.Other;

            ResearchBand cached;

            if (bands.TryGetValue(project, out cached))
                return cached;

            string reason = null;

            ResearchBand band = UIGuard.Try("Research.Classify", () => Classify(project, out reason),
                ResearchBand.Other,
                "One research project could not be sorted into a band and is filed under Other. Nothing about "
                + "your colony's research has changed.");

            bands[project] = band;
            reasons[project] = reason ?? "Nothing this mod recognises, so it sits in Other.";

            return band;
        }

        /// <summary>
        /// One sentence saying why this project landed where it did.
        ///
        /// Shown in the detail panel and in the dev listing. It is written at classification time rather than
        /// reconstructed later, because the reason is which test matched and only the classifier knows that.
        /// </summary>
        internal static string ReasonFor(ResearchProjectDef project)
        {
            if (project == null)
                return string.Empty;

            // Asked through BandOf, so the reason cannot be missing for a project whose band has been read.
            BandOf(project);

            string reason;

            return reasons.TryGetValue(project, out reason) ? reason : string.Empty;
        }

        private static ResearchBand Classify(ResearchProjectDef project, out string reason)
        {
            // --- 1. Named outright -------------------------------------------------------------------------
            ResearchBand? named = ResearchBandOverrides.For(project);

            if (named.HasValue)
            {
                reason = ResearchBandOverrides.ReasonFor(project)
                         ?? "Named by this mod's override table.";

                return named.Value;
            }

            // --- 2. Anomaly's own mechanic ----------------------------------------------------------------
            if (project.knowledgeCategory != null)
            {
                reason = "It is a knowledge project: RimWorld gates it on "
                         + (project.knowledgeCategory.label.NullOrEmpty()
                             ? project.knowledgeCategory.defName
                             : project.knowledgeCategory.label) + " knowledge rather than on research points.";

                return ResearchBand.DarkKnowledge;
            }

            if (project.tab != null && project.tab == ResearchTabDefOf.Anomaly)
            {
                reason = "It sits on Anomaly's research tab.";

                return ResearchBand.DarkKnowledge;
            }

            // --- 3. What it unlocks -----------------------------------------------------------------------
            List<Def> unlocks = project.UnlockedDefs;

            // Mechanitor first among the unlock tests, because it is a flag on the project rather than a guess
            // about a thing, and a mech gestator is also a work bench.
            if (project.requiresMechanitor)
            {
                reason = "It needs a mechanitor.";

                return ResearchBand.Mechanoids;
            }

            ResearchBand band;

            if (unlocks != null && Scan(unlocks, out band, out reason))
                return band;

            // --- 4. Nothing to read ------------------------------------------------------------------------
            if (project.requireGravEngineInspected)
            {
                reason = "It needs a grav engine inspected, and unlocks nothing this mod can read.";

                return ResearchBand.FlightAndSpace;
            }

            reason = unlocks == null || unlocks.Count == 0
                ? "It unlocks no thing, recipe, terrain or ritual, so there is nothing to classify it by."
                : "Nothing it unlocks matches any band's test.";

            return ResearchBand.Other;
        }

        /// <summary>
        /// Walks what a project unlocks and returns the first band any of it matches.
        ///
        /// <b>Band order beats unlock order,</b> which is why this is a loop over bands containing a loop over
        /// unlocks rather than the other way round. A project unlocking a bionic arm recipe and a fabrication
        /// recipe must land in Medicine whichever of the two RimWorld happened to list first, or the same project
        /// would move between sessions as def load order shifted.
        /// </summary>
        private static bool Scan(List<Def> unlocks, out ResearchBand band, out string reason)
        {
            List<ResearchBandInfo> all = ResearchBands.All;

            for (int b = 0; b < all.Count; b++)
            {
                ResearchBand candidate = all[b].Band;

                if (candidate == ResearchBand.Other)
                    break;

                for (int i = 0; i < unlocks.Count; i++)
                {
                    Def unlocked = unlocks[i];

                    if (unlocked == null)
                        continue;

                    string why = Matches(unlocked, candidate);

                    if (why == null)
                        continue;

                    band = candidate;
                    reason = why;

                    return true;
                }
            }

            band = ResearchBand.Other;
            reason = null;

            return false;
        }

        /// <summary>
        /// Whether one unlocked def puts a project in one band, and the sentence saying so.
        ///
        /// Returns null for no match. A string rather than a bool because the reason is the interesting half: the
        /// detail panel and the dev listing both need to say <em>which</em> unlock decided it, and reconstructing
        /// that afterwards would mean running these tests twice.
        /// </summary>
        private static string Matches(Def unlocked, ResearchBand band)
        {
            string name = Name(unlocked);

            ThingDef thing = unlocked as ThingDef;
            RecipeDef recipe = unlocked as RecipeDef;
            BuildingProperties building = thing == null ? null : thing.building;

            switch (band)
            {
                case ResearchBand.Mechanoids:
                    if (thing != null && thing.race != null && thing.race.IsMechanoid)
                        return "It unlocks " + name + ", which is a mechanoid.";

                    if (thing != null && thing.race != null && thing.race.mechWeightClass != null)
                        return "It unlocks " + name + ", which is a player mech.";

                    return null;

                case ResearchBand.FlightAndSpace:
                    if (building != null && building.shipPart)
                        return "It unlocks " + name + ", which is a ship part.";

                    if (thing != null && (thing.HasComp(typeof(CompTransporter))
                                          || thing.HasComp(typeof(CompLaunchable))))
                        return "It unlocks " + name + ", which launches off the map.";

                    if (thing != null && HasCompNamed(thing, "CompProperties_SubstructureFootprint"))
                        return "It unlocks " + name + ", which is gravship structure.";

                    return null;

                case ResearchBand.MedicineAndGenetics:
                    if (recipe != null && recipe.IsSurgery)
                        return "It unlocks the surgery " + name + ".";

                    if (thing != null && Within(thing, ThingCategoryDefOf.BodyParts))
                        return "It unlocks " + name + ", which is a body part.";

                    if (thing != null && Within(thing, ThingCategoryDefOf.Medicine))
                        return "It unlocks " + name + ", which is medicine.";

                    if (thing != null && thing.IsMedicine)
                        return "It unlocks " + name + ", which has medical potency.";

                    return null;

                case ResearchBand.FarmingAndFood:
                    if (thing != null && thing.plant != null)
                        return "It unlocks the plant " + name + ".";

                    if (thing != null && Within(thing, ThingCategoryDefOf.Foods))
                        return "It unlocks " + name + ", which is food.";

                    if (thing != null && Within(thing, ThingCategoryDefOf.Drugs))
                        return "It unlocks " + name + ", which is a drug.";

                    if (thing != null && thing.IsDrug)
                        return "It unlocks " + name + ", which is a drug.";

                    return null;

                case ResearchBand.WeaponsAndDefense:
                    if (thing != null && thing.IsWeapon)
                        return "It unlocks the weapon " + name + ".";

                    if (building != null && building.turretGunDef != null)
                        return "It unlocks " + name + ", which is a turret.";

                    if (building != null && building.isTrap)
                        return "It unlocks " + name + ", which is a trap.";

                    return null;

                case ResearchBand.ApparelAndArmor:
                    if (thing != null && thing.IsApparel)
                        return "It unlocks " + name + ", which is worn.";

                    return null;

                case ResearchBand.PowerAndElectronics:
                    // Makes, stores or carries power -- not merely draws it. Almost every industrial building has
                    // a power comp, so a bare "has CompPower" test files an autodoor under Power.
                    if (thing == null)
                        return null;

                    if (thing.HasComp(typeof(CompPowerBattery)))
                        return "It unlocks " + name + ", which stores power.";

                    CompProperties_Power power = thing.GetCompProperties<CompProperties_Power>();

                    if (power == null)
                        return null;

                    if (power.transmitsPower)
                        return "It unlocks " + name + ", which carries power.";

                    if (power.PowerConsumption < 0f)
                        return "It unlocks " + name + ", which generates power.";

                    return null;

                case ResearchBand.RecreationAndCulture:
                    if (unlocked is PsychicRitualDef)
                        return "It unlocks the ritual " + name + ".";

                    if (building != null && building.joyKind != null)
                        return "It unlocks " + name + ", which colonists use for recreation.";

                    if (thing != null && thing.IsArt)
                        return "It unlocks " + name + ", which is art.";

                    return null;

                case ResearchBand.ProductionAndCrafting:
                    if (thing != null && thing.IsWorkTable)
                        return "It unlocks the work bench " + name + ".";

                    if (recipe != null)
                        return "It unlocks the recipe " + name + ".";

                    if (thing != null && Within(thing, ThingCategoryDefOf.Manufactured))
                        return "It unlocks " + name + ", which is manufactured.";

                    return null;

                case ResearchBand.BuildingAndComfort:
                    if (unlocked is TerrainDef)
                        return "It unlocks the floor " + name + ".";

                    if (thing != null && thing.IsDoor)
                        return "It unlocks the door " + name + ".";

                    if (thing != null && thing.IsBed)
                        return "It unlocks the bed " + name + ".";

                    if (thing != null && HasCompNamed(thing, "CompProperties_TempControl"))
                        return "It unlocks " + name + ", which controls temperature.";

                    // The catch-all for anything buildable, and deliberately last of the last band that has one:
                    // a project unlocking a building nothing above recognised has still unlocked a building, and
                    // filing that under Other would put half of every furniture mod in one undifferentiated block.
                    if (building != null)
                        return "It unlocks " + name + ", which is a building.";

                    return null;

                default:
                    return null;
            }
        }

        private static bool Within(ThingDef thing, ThingCategoryDef category)
        {
            return category != null && thing.IsWithinCategory(category);
        }

        /// <summary>
        /// Whether a thing carries a comp named at runtime rather than referenced.
        ///
        /// For comps that belong to an expansion: naming <c>CompProperties_SubstructureFootprint</c> in code would
        /// be a hard reference to Odyssey's assembly, and the mod has to load without it. Matched on the type's own
        /// name, so it costs nothing when the expansion is absent -- there is simply no comp of that name.
        /// </summary>
        private static bool HasCompNamed(ThingDef thing, string compPropertiesTypeName)
        {
            if (thing.comps == null)
                return false;

            for (int i = 0; i < thing.comps.Count; i++)
            {
                CompProperties props = thing.comps[i];

                if (props != null && props.GetType().Name == compPropertiesTypeName)
                    return true;
            }

            return false;
        }

        private static string Name(Def def)
        {
            if (def == null)
                return "something";

            return def.label.NullOrEmpty() ? def.defName : def.label;
        }
    }
}
