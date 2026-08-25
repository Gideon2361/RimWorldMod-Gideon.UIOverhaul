using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>One species and how many of each sex a taming template asks for.</summary>
    internal sealed class AnimalBillTarget
    {
        internal string Species;

        internal int Males;

        internal int Females;
    }

    /// <summary>
    /// A saved hunting or taming bill, ready to be poured into a new one.
    ///
    /// <b>One type for both kinds rather than two.</b> They are saved together, listed together and imported
    /// together, and the alternative is two of every file below for a difference that amounts to which half of
    /// the fields are filled in. <see cref="Taming"/> says which half that is, and the picker only ever offers a
    /// template of the kind the window asking for it can use.
    ///
    /// <b>Defs are held as names, not as defs.</b> A template outlives the game it was made in and is meant to be
    /// carried between saves and shared, so a mod that is not loaded next time has to come back as a missing name
    /// that gets skipped rather than as a null that gets applied. See <see cref="AnimalBillTemplates.Apply"/>.
    ///
    /// <b>What is deliberately not saved:</b> the label, and everything about what a bill has done. A template is
    /// a shape, not a bill: copying the name would give every bill made from one the same name, and copying
    /// lastActedTick would have a fresh bill claiming it had already worked.
    /// </summary>
    internal sealed class AnimalBillTemplate
    {
        internal string Name;

        /// <summary>Which kind this is. A taming template can only be applied to a taming bill and vice versa.</summary>
        internal bool Taming;

        // ---- hunting ----

        internal string Mode = "UntilStocked";

        internal int TargetCount = 300;

        internal int ResumeAt = -1;

        internal int KeepAlive = 2;

        internal int MaxPopulation = 6;

        internal bool AllowPredators;

        internal float MaxManhunterChance = 0.1f;

        /// <summary>The meats the bill keeps stocked, as thing defNames.</summary>
        internal List<string> Items = new List<string>();

        /// <summary>The species it may take, as thing defNames. Empty means anything huntable.</summary>
        internal List<string> Species = new List<string>();

        // ---- taming ----

        internal float MinTameChance = 0.05f;

        internal List<AnimalBillTarget> Targets = new List<AnimalBillTarget>();

        // ---- both ----

        internal int MaxOutstanding = 6;

        internal string Summary
        {
            get
            {
                if (Taming)
                {
                    int species = Targets == null ? 0 : Targets.Count;

                    return species == 0
                        ? "taming, nothing chosen"
                        : species == 1 ? "taming, 1 species" : "taming, " + species + " species";
                }

                string what = Species == null || Species.Count == 0
                    ? "any wildlife"
                    : Species.Count == 1 ? "1 species" : Species.Count + " species";

                switch (Mode)
                {
                    case "Forever": return "culling forever, " + what;
                    case "MaxPopulation": return "culling over " + MaxPopulation + ", " + what;
                    default: return "stocking " + TargetCount + ", " + what;
                }
            }
        }
    }

    /// <summary>
    /// Turning a bill into a template and back again.
    ///
    /// <b>Kept apart from the bills themselves</b> so neither bill type has to know that templates exist. The
    /// bills are saved into the game and these are saved into a config file; folding one into the other would put
    /// a file format inside a save format.
    /// </summary>
    internal static class AnimalBillTemplates
    {
        internal static AnimalBillTemplate Capture(HuntingBill bill, string name)
        {
            return UIGuard.Try("Animals.CaptureHuntTemplate", () =>
            {
                AnimalBillTemplate made = new AnimalBillTemplate
                {
                    Name = name,
                    Taming = false,
                    Mode = bill.mode.ToString(),
                    TargetCount = bill.targetCount,
                    ResumeAt = bill.resumeAt,
                    KeepAlive = bill.keepAlive,
                    MaxPopulation = bill.maxPopulation,
                    AllowPredators = bill.allowPredators,
                    MaxManhunterChance = bill.maxManhunterChance,
                    MaxOutstanding = bill.maxOutstanding
                };

                if (bill.filter != null)
                {
                    foreach (ThingDef def in bill.filter.AllowedThingDefs)
                    {
                        if (def != null)
                            made.Items.Add(def.defName);
                    }
                }

                for (int i = 0; bill.species != null && i < bill.species.Count; i++)
                {
                    if (bill.species[i] != null)
                        made.Species.Add(bill.species[i].defName);
                }

                return made;
            }, null, null);
        }

        internal static AnimalBillTemplate Capture(TamingBill bill, string name)
        {
            return UIGuard.Try("Animals.CaptureTameTemplate", () =>
            {
                AnimalBillTemplate made = new AnimalBillTemplate
                {
                    Name = name,
                    Taming = true,
                    MinTameChance = bill.minTameChance,
                    MaxOutstanding = bill.maxOutstanding
                };

                for (int i = 0; bill.targets != null && i < bill.targets.Count; i++)
                {
                    TamingTarget target = bill.targets[i];

                    if (target == null || target.species == null || target.Empty)
                        continue;

                    made.Targets.Add(new AnimalBillTarget
                    {
                        Species = target.species.defName,
                        Males = target.males,
                        Females = target.females
                    });
                }

                return made;
            }, null, null);
        }

        /// <summary>
        /// Pours a template into a hunting bill, replacing what was there.
        ///
        /// <b>Everything or nothing per field, and missing defs are skipped rather than nulled.</b> A template
        /// naming a mod's animal that is not loaded this time should give a bill without that animal, not a bill
        /// with a hole in its species list that every reader then has to guard against.
        ///
        /// The label is left alone. A bill somebody named keeps its name when a template is poured into it,
        /// which is what makes "apply this shape to that order" a usable thing to do twice.
        /// </summary>
        internal static void Apply(AnimalBillTemplate template, HuntingBill bill)
        {
            UIGuard.Try("Animals.ApplyHuntTemplate", () =>
            {
                if (template == null || bill == null || template.Taming)
                    return;

                HuntingBillMode mode;

                bill.mode = System.Enum.TryParse(template.Mode, out mode)
                    ? mode
                    : HuntingBillMode.UntilStocked;

                bill.targetCount = Mathf.Max(0, template.TargetCount);
                bill.resumeAt = template.ResumeAt;
                bill.keepAlive = Mathf.Max(0, template.KeepAlive);
                bill.maxPopulation = Mathf.Max(0, template.MaxPopulation);
                bill.allowPredators = template.AllowPredators;
                bill.maxManhunterChance = Mathf.Clamp01(template.MaxManhunterChance);
                bill.maxOutstanding = Mathf.Clamp(template.MaxOutstanding, 1, 20);

                if (bill.filter == null)
                    bill.filter = new ThingFilter();

                bill.filter.SetDisallowAll();

                for (int i = 0; template.Items != null && i < template.Items.Count; i++)
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(template.Items[i]);

                    if (def != null)
                        bill.filter.SetAllow(def, true);
                }

                // Kept inside the meat universe the window offers, or a template made before that restriction
                // could allow rows the dialog will not show and so cannot be turned off again.
                bill.ConfineToMeat();

                bill.species = new List<ThingDef>();

                for (int i = 0; template.Species != null && i < template.Species.Count; i++)
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(template.Species[i]);

                    if (def != null && !bill.species.Contains(def))
                        bill.species.Add(def);
                }
            }, "The template was not applied. The bill is unchanged.");
        }

        internal static void Apply(AnimalBillTemplate template, TamingBill bill)
        {
            UIGuard.Try("Animals.ApplyTameTemplate", () =>
            {
                if (template == null || bill == null || !template.Taming)
                    return;

                bill.minTameChance = Mathf.Clamp01(template.MinTameChance);
                bill.maxOutstanding = Mathf.Clamp(template.MaxOutstanding, 1, 20);

                bill.targets = new List<TamingTarget>();

                for (int i = 0; template.Targets != null && i < template.Targets.Count; i++)
                {
                    AnimalBillTarget target = template.Targets[i];

                    if (target == null)
                        continue;

                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(target.Species);

                    if (def == null || bill.TargetFor(def) != null)
                        continue;

                    bill.targets.Add(new TamingTarget(def,
                        Mathf.Clamp(target.Males, 0, TamingTarget.Ceiling),
                        Mathf.Clamp(target.Females, 0, TamingTarget.Ceiling)));
                }

                // The tamer is deliberately not part of a template. It is a reference to one colonist in one
                // save, and a template is meant to survive being carried to another.
            }, "The template was not applied. The bill is unchanged.");
        }
    }
}
