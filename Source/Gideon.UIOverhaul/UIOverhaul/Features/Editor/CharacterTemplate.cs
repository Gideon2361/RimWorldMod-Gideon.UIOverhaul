using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>One trait and the degree of it, as defNames survive a save that the objects do not.</summary>
    internal sealed class TemplateTrait
    {
        internal string DefName;

        internal int Degree;
    }

    internal sealed class TemplateSkill
    {
        internal string DefName;

        internal int Level;

        internal string Passion;
    }

    internal sealed class TemplateGene
    {
        internal string DefName;

        internal bool Xenogene;
    }

    /// <summary>A made thing, as the three fields that decide what it is worth.</summary>
    internal sealed class TemplateThing
    {
        internal string DefName;

        internal string Stuff;

        internal string Quality;
    }

    /// <summary>
    /// One durable thing about a body: an implant, a missing part, a scar, a chronic condition.
    ///
    /// <b>The body part is named by def plus an index, because a <c>BodyPartRecord</c> has no identity of its
    /// own.</b> Both arms are `Arm`, so "the second Arm in this body's list" is the only portable way to say
    /// which. It round-trips exactly on the same race and resolves to nothing on a different one, which is
    /// counted as a miss rather than guessed at.
    /// </summary>
    internal sealed class TemplateHediff
    {
        internal string DefName;

        /// <summary>The <c>BodyPartDef</c> defName, or null for a whole-body condition.</summary>
        internal string PartDef;

        /// <summary>Which part of that def, counted through the body's own list. -1 for whole body.</summary>
        internal int PartIndex = -1;

        internal float Severity;

        /// <summary>An injury that has become a scar rather than one that is still healing.</summary>
        internal bool Permanent;

        /// <summary>
        /// Implants come after missing parts and before everything else, which is the order they have to be
        /// written back in.
        /// </summary>
        internal int Order;
    }

    /// <summary>
    /// A saved character: who somebody is, in defNames, portable between colonies and saves.
    ///
    /// <b>A character sheet rather than a copy of a pawn.</b> Asked for 2026-08-23 so characters can be carried
    /// into other saves. The obvious implementation -- serialise the <c>Pawn</c> -- cannot do that: a pawn holds
    /// references to a map, a faction, an ideo and other pawns by id, and none of those ids mean anything in a
    /// different save. So this holds only what is true about the person: their name, their age, their looks, their
    /// history, what they can do, and what they are carrying. Everything world-bound is deliberately absent.
    ///
    /// <b>Health is here, and the cut inside it matters.</b> The first version left health out entirely on the
    /// grounds that it is the state of one afternoon. Aaron corrected that on 2026-08-23: *"Templates need to save
    /// health because that includes things like implants and bionics."* He is right -- a colonist with an archotech
    /// eye and a bionic arm **is** that character, and exporting one and importing a plain human is the feature
    /// failing at its own purpose.
    ///
    /// So health is split by durability rather than dropped. Four things are the body itself and travel: implants
    /// and prosthetics, missing parts, permanent injuries, and chronic conditions. Everything else is the
    /// afternoon and does not: fresh wounds, diseases, blood loss, addictions, hypothermia, a drug high. Nobody
    /// importing a character means to import the gunshot that was still bleeding when it was saved.
    ///
    /// <b>What is still not here.</b> Needs and thoughts are transient by definition and expire on their own.
    /// Relationships point at pawns that do not exist in the target save. A template carrying either would be
    /// refused on import or silently mangled, and both are worse than leaving them out and saying so.
    ///
    /// <b>defNames, not indices.</b> A template written with one mod list and read with another finds some of its
    /// defs missing; each one it cannot find is skipped and counted, and the rest apply. Failing the whole import
    /// because one hair style is gone would make the feature useless to anybody who ever changes their mods.
    /// </summary>
    internal sealed class CharacterTemplate
    {
        /// <summary>What the player called it. Also the file name, sanitised.</summary>
        internal string Name;

        /// <summary>Who it was taken from and when, so a list of twelve templates is readable.</summary>
        internal string SavedFrom;

        internal string SavedAt;

        // Identity ------------------------------------------------------------------------------

        internal string First;

        internal string Nick;

        internal string Last;

        internal string Gender;

        internal int BiologicalYears = -1;

        internal int ChronologicalYears = -1;

        // Who they are --------------------------------------------------------------------------

        internal string Childhood;

        internal string Adulthood;

        internal readonly List<TemplateTrait> Traits = new List<TemplateTrait>();

        internal readonly List<TemplateSkill> Skills = new List<TemplateSkill>();

        // Looks ---------------------------------------------------------------------------------

        internal string BodyType;

        internal string HeadType;

        internal string Hair;

        internal string Beard;

        internal string FaceTattoo;

        internal string BodyTattoo;

        internal string SkinColor;

        internal string HairColor;

        internal string Xenotype;

        internal readonly List<TemplateGene> Genes = new List<TemplateGene>();

        // Gear ----------------------------------------------------------------------------------

        internal readonly List<TemplateThing> Apparel = new List<TemplateThing>();

        internal TemplateThing Weapon;

        // Body ----------------------------------------------------------------------------------

        /// <summary>Implants, missing parts, scars and chronic conditions. See the note on the class.</summary>
        internal readonly List<TemplateHediff> Health = new List<TemplateHediff>();

        /// <summary>A line for the manager's list: who and when.</summary>
        internal string Subtitle
        {
            get
            {
                if (SavedFrom.NullOrEmpty())
                    return SavedAt;

                return SavedAt.NullOrEmpty() ? SavedFrom : SavedFrom + ", " + SavedAt;
            }
        }

        // ---------------------------------------------------------------------------------------
        // Capture
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Reads a pawn into a template.
        ///
        /// Everything is read through a guard and anything unreadable is simply left out, because a template with
        /// no beard in it is a template that leaves the beard alone -- which is the same rule applying uses.
        /// </summary>
        internal static CharacterTemplate Capture(Pawn pawn, string name)
        {
            CharacterTemplate template = new CharacterTemplate { Name = name };

            if (pawn == null)
                return template;

            UIGuard.Try("Template.Capture", () =>
            {
                template.SavedFrom = pawn.LabelShortCap;
                template.SavedAt = GenDate.DateFullStringWithHourAt(GenTicks.TicksAbs, Vector2.zero);

                NameTriple triple = pawn.Name as NameTriple;

                if (triple != null)
                {
                    template.First = triple.First;
                    template.Nick = triple.Nick;
                    template.Last = triple.Last;
                }
                else if (pawn.Name != null)
                {
                    template.Nick = pawn.Name.ToStringShort;
                }

                template.Gender = pawn.gender.ToString();

                if (pawn.ageTracker != null)
                {
                    template.BiologicalYears = pawn.ageTracker.AgeBiologicalYears;
                    template.ChronologicalYears = pawn.ageTracker.AgeChronologicalYears;
                }

                if (pawn.story != null)
                {
                    template.Childhood = Named(pawn.story.Childhood);
                    template.Adulthood = Named(pawn.story.Adulthood);
                    template.BodyType = Named(pawn.story.bodyType);
                    template.HeadType = Named(pawn.story.headType);
                    template.Hair = Named(pawn.story.hairDef);
                    template.SkinColor = Hex(pawn.story.SkinColorBase);
                    template.HairColor = Hex(pawn.story.HairColor);

                    if (pawn.story.traits != null)
                    {
                        List<Trait> traits = pawn.story.traits.allTraits;

                        for (int i = 0; i < traits.Count; i++)
                        {
                            if (traits[i] != null && traits[i].def != null)
                                template.Traits.Add(new TemplateTrait
                                {
                                    DefName = traits[i].def.defName,
                                    Degree = traits[i].Degree
                                });
                        }
                    }
                }

                if (pawn.style != null)
                {
                    template.Beard = Named(pawn.style.beardDef);
                    template.FaceTattoo = Named(pawn.style.FaceTattoo);
                    template.BodyTattoo = Named(pawn.style.BodyTattoo);
                }

                if (pawn.skills != null && pawn.skills.skills != null)
                {
                    List<SkillRecord> skills = pawn.skills.skills;

                    for (int i = 0; i < skills.Count; i++)
                    {
                        SkillRecord record = skills[i];

                        if (record == null || record.def == null)
                            continue;

                        template.Skills.Add(new TemplateSkill
                        {
                            DefName = record.def.defName,
                            Level = record.Level,
                            Passion = record.passion.ToString()
                        });
                    }
                }

                if (pawn.genes != null)
                {
                    template.Xenotype = Named(pawn.genes.Xenotype);

                    List<Gene> genes = pawn.genes.GenesListForReading;

                    for (int i = 0; i < genes.Count; i++)
                    {
                        if (genes[i] == null || genes[i].def == null)
                            continue;

                        template.Genes.Add(new TemplateGene
                        {
                            DefName = genes[i].def.defName,
                            Xenogene = pawn.genes.IsXenogene(genes[i])
                        });
                    }
                }

                if (pawn.apparel != null)
                {
                    List<Apparel> worn = pawn.apparel.WornApparel;

                    for (int i = 0; i < worn.Count; i++)
                    {
                        TemplateThing made = Read(worn[i]);

                        if (made != null)
                            template.Apparel.Add(made);
                    }
                }

                if (pawn.equipment != null)
                    template.Weapon = Read(pawn.equipment.Primary);

                template.Health.AddRange(Durable(pawn));
            }, "The pawn could not be fully read into a template.");

            return template;
        }

        /// <summary>
        /// The hediffs that are the body rather than the afternoon.
        ///
        /// <b>Four categories, in the order they have to be written back.</b> Missing parts first, because a part
        /// cannot be missing after something has been installed in it. Implants second, since installing one
        /// restores whatever it needs. Scars and chronic conditions last, since they sit on parts the first two
        /// have already settled.
        ///
        /// Also used as the snapshot that makes applying a template's health reversible: the same reader runs over
        /// the target before it is overwritten, and the undo entry writes that back.
        /// </summary>
        internal static List<TemplateHediff> Durable(Pawn pawn)
        {
            List<TemplateHediff> found = new List<TemplateHediff>();

            UIGuard.Try("Template.Durable", () =>
            {
                if (pawn.health == null || pawn.health.hediffSet == null)
                    return;

                List<Hediff> all = pawn.health.hediffSet.hediffs;

                for (int i = 0; i < all.Count; i++)
                {
                    Hediff hediff = all[i];

                    if (hediff == null || hediff.def == null)
                        continue;

                    int order = Category(hediff);

                    if (order < 0)
                        continue;

                    found.Add(new TemplateHediff
                    {
                        DefName = hediff.def.defName,
                        PartDef = hediff.Part != null && hediff.Part.def != null
                            ? hediff.Part.def.defName
                            : null,
                        PartIndex = IndexOf(pawn, hediff.Part),
                        Severity = hediff.Severity,
                        Permanent = hediff is Hediff_Injury && hediff.IsPermanent(),
                        Order = order
                    });
                }

                found.Sort((a, b) => a.Order.CompareTo(b.Order));
            }, null);

            return found;
        }

        /// <summary>
        /// Which of the four durable categories a hediff belongs to, or -1 for the ones that do not travel.
        ///
        /// The return value doubles as the write order, which is why it is a number rather than an enum: there is
        /// exactly one consumer and it needs it sorted.
        /// </summary>
        private static int Category(Hediff hediff)
        {
            if (hediff is Hediff_MissingPart)
                return 0;

            if (hediff.def.countsAsAddedPartOrImplant || hediff is Hediff_AddedPart)
                return 1;

            if (hediff is Hediff_Injury)
                return hediff.IsPermanent() ? 2 : -1;

            return hediff.def.chronic ? 2 : -1;
        }

        /// <summary>
        /// Which part of its own def this is, counted through the body's list.
        ///
        /// The only portable name a body part has. Both arms are `Arm`, so the def alone cannot say which one, and
        /// a <c>BodyPartRecord</c> reference means nothing outside the pawn it came from.
        /// </summary>
        private static int IndexOf(Pawn pawn, BodyPartRecord part)
        {
            if (part == null || part.def == null || pawn.RaceProps == null || pawn.RaceProps.body == null)
                return -1;

            List<BodyPartRecord> parts = pawn.RaceProps.body.GetPartsWithDef(part.def);

            return parts == null ? -1 : parts.IndexOf(part);
        }

        private static BodyPartRecord Resolve(Pawn pawn, TemplateHediff saved, ref int missing)
        {
            if (saved.PartDef.NullOrEmpty() || saved.PartIndex < 0)
                return null;

            BodyPartDef def = DefDatabase<BodyPartDef>.GetNamedSilentFail(saved.PartDef);

            if (def == null || pawn.RaceProps == null || pawn.RaceProps.body == null)
            {
                missing++;

                return null;
            }

            List<BodyPartRecord> parts = pawn.RaceProps.body.GetPartsWithDef(def);

            if (parts == null || saved.PartIndex >= parts.Count)
            {
                missing++;

                return null;
            }

            return parts[saved.PartIndex];
        }

        private static TemplateThing Read(Thing thing)
        {
            if (thing == null || thing.def == null)
                return null;

            QualityCategory quality;

            return new TemplateThing
            {
                DefName = thing.def.defName,
                Stuff = thing.Stuff != null ? thing.Stuff.defName : null,
                Quality = thing.TryGetQuality(out quality) ? quality.ToString() : null
            };
        }

        private static string Named(Def def)
        {
            return def != null ? def.defName : null;
        }

        private static string Hex(Color colour)
        {
            return ColorUtility.ToHtmlStringRGB(colour);
        }

        // ---------------------------------------------------------------------------------------
        // Apply
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Writes the template onto a pawn, logging every field so the whole import can be reverted.
        ///
        /// <b>Field by field through the editor's own change log.</b> Applying a template is dozens of small edits
        /// and Revert all has to reach every one of them; a bulk write that bypassed the log would be the one
        /// operation in the window that could not be taken back, for no reason other than convenience.
        ///
        /// Returns how many named defs this install does not have, which the caller reports rather than hides.
        /// </summary>
        internal int ApplyTo(Pawn pawn, EditorChanges changes)
        {
            int missing = 0;

            if (pawn == null || changes == null)
                return 0;

            UIGuard.Try("Template.Apply", () =>
            {
                Identity(pawn, changes);

                missing += Story(pawn, changes);
                missing += Looks(pawn, changes);
                missing += TraitsOnto(pawn, changes);
                missing += SkillsOnto(pawn, changes);
                missing += GenesOnto(pawn, changes);
                missing += GearOnto(pawn, changes);
                missing += HealthOnto(pawn, changes);

                EditorParts.Redraw(pawn);

                UIGuard.Try("Template.Recache", pawn.Notify_DisabledWorkTypesChanged, null);
            }, "The template could not be fully applied. What was written is in the change list.");

            return missing;
        }

        private void Identity(Pawn pawn, EditorChanges changes)
        {
            if (!Nick.NullOrEmpty() || !First.NullOrEmpty())
                changes.Set("name", () => pawn.Name,
                    name => pawn.Name = name,
                    (Name) new NameTriple(First ?? string.Empty,
                        Nick.NullOrEmpty() ? First : Nick, Last ?? string.Empty));

            Gender wanted;

            if (!Gender.NullOrEmpty() && Enum.TryParse(Gender, out wanted))
                changes.Set("sex", () => pawn.gender, g => pawn.gender = g, wanted);

            if (pawn.ageTracker == null)
                return;

            if (BiologicalYears >= 0)
                changes.Set("biological age", () => pawn.ageTracker.AgeBiologicalTicks,
                    t => pawn.ageTracker.AgeBiologicalTicks = t,
                    BiologicalYears * (long) GenDate.TicksPerYear);

            if (ChronologicalYears >= 0)
                changes.Set("chronological age", () => pawn.ageTracker.AgeChronologicalTicks,
                    t => pawn.ageTracker.AgeChronologicalTicks = t,
                    ChronologicalYears * (long) GenDate.TicksPerYear);
        }

        private int Story(Pawn pawn, EditorChanges changes)
        {
            if (pawn.story == null)
                return 0;

            int missing = 0;

            BackstoryDef childhood = Find<BackstoryDef>(Childhood, ref missing);

            if (childhood != null)
                changes.Set("childhood", () => pawn.story.Childhood, v => pawn.story.Childhood = v, childhood);

            BackstoryDef adulthood = Find<BackstoryDef>(Adulthood, ref missing);

            if (adulthood != null)
                changes.Set("adulthood", () => pawn.story.Adulthood, v => pawn.story.Adulthood = v, adulthood);

            return missing;
        }

        private int Looks(Pawn pawn, EditorChanges changes)
        {
            int missing = 0;

            if (pawn.story != null)
            {
                BodyTypeDef body = Find<BodyTypeDef>(BodyType, ref missing);

                if (body != null)
                    changes.Set("body type", () => pawn.story.bodyType, v => pawn.story.bodyType = v, body);

                HeadTypeDef head = Find<HeadTypeDef>(HeadType, ref missing);

                if (head != null)
                    changes.Set("head", () => pawn.story.headType, v => pawn.story.headType = v, head);

                HairDef hair = Find<HairDef>(Hair, ref missing);

                if (hair != null)
                    changes.Set("hair", () => pawn.story.hairDef, v => pawn.story.hairDef = v, hair);

                Color skin;

                if (Parse(SkinColor, out skin))
                    changes.Set("skin", () => pawn.story.SkinColorBase, v => pawn.story.SkinColorBase = v, skin);

                Color hairColour;

                if (Parse(HairColor, out hairColour))
                    changes.Set("hair colour", () => pawn.story.HairColor, v => pawn.story.HairColor = v,
                        hairColour);
            }

            if (pawn.style == null)
                return missing;

            BeardDef beard = Find<BeardDef>(Beard, ref missing);

            if (beard != null)
                changes.Set("beard", () => pawn.style.beardDef, v => pawn.style.beardDef = v, beard);

            TattooDef face = Find<TattooDef>(FaceTattoo, ref missing);

            if (face != null)
                changes.Set("face tattoo", () => pawn.style.FaceTattoo, v => pawn.style.FaceTattoo = v, face);

            TattooDef bodyTattoo = Find<TattooDef>(BodyTattoo, ref missing);

            if (bodyTattoo != null)
                changes.Set("body tattoo", () => pawn.style.BodyTattoo, v => pawn.style.BodyTattoo = v,
                    bodyTattoo);

            return missing;
        }

        /// <summary>
        /// Replaces the trait set wholesale.
        ///
        /// <b>Every existing trait comes off first,</b> because a template is a description of a person rather
        /// than a set of additions: importing a character and finding they kept the previous occupant's Pyromaniac
        /// would be a bug, not a feature.
        /// </summary>
        private int TraitsOnto(Pawn pawn, EditorChanges changes)
        {
            if (pawn.story == null || pawn.story.traits == null || Traits.Count == 0)
                return 0;

            int missing = 0;

            TraitSet set = pawn.story.traits;

            List<Trait> had = new List<Trait>(set.allTraits);

            for (int i = 0; i < had.Count; i++)
            {
                Trait trait = had[i];
                TraitDef def = trait.def;
                int degree = trait.Degree;

                set.RemoveTrait(trait);

                changes.Record("traits", () => set.GainTrait(new Trait(def, degree), true));
            }

            for (int i = 0; i < Traits.Count; i++)
            {
                TraitDef def = Find<TraitDef>(Traits[i].DefName, ref missing);

                if (def == null)
                    continue;

                int degree = Traits[i].Degree;

                set.GainTrait(new Trait(def, degree), true);

                changes.Record("traits", () =>
                {
                    Trait added = set.GetTrait(def, degree);

                    if (added != null)
                        set.RemoveTrait(added);
                });
            }

            return missing;
        }

        private int SkillsOnto(Pawn pawn, EditorChanges changes)
        {
            if (pawn.skills == null)
                return 0;

            int missing = 0;

            for (int i = 0; i < Skills.Count; i++)
            {
                TemplateSkill saved = Skills[i];

                SkillDef def = Find<SkillDef>(saved.DefName, ref missing);

                if (def == null)
                    continue;

                SkillRecord record = pawn.skills.GetSkill(def);

                if (record == null)
                    continue;

                changes.Set(def.skillLabel, () => record.Level, v => record.Level = v, saved.Level);

                Passion passion;

                if (!saved.Passion.NullOrEmpty() && Enum.TryParse(saved.Passion, out passion))
                    changes.Set(def.skillLabel + " passion", () => record.passion, v => record.passion = v,
                        passion);
            }

            return missing;
        }

        /// <summary>
        /// Sets the xenotype and then the gene list.
        ///
        /// <b>The xenotype first, deliberately.</b> Setting one replaces every gene the pawn has, so doing it
        /// afterwards would throw away the list this template carries. That call is not reversible, so an import
        /// that changes the xenotype is recorded as permanent -- the only part of a template apply that is.
        /// </summary>
        private int GenesOnto(Pawn pawn, EditorChanges changes)
        {
            if (pawn.genes == null || !ModsConfig.BiotechActive)
                return 0;

            int missing = 0;

            XenotypeDef xenotype = Find<XenotypeDef>(Xenotype, ref missing);

            if (xenotype != null && xenotype != pawn.genes.Xenotype)
            {
                pawn.genes.SetXenotype(xenotype);

                changes.RecordPermanent("xenotype");
            }

            if (Genes.Count == 0)
                return missing;

            for (int i = 0; i < Genes.Count; i++)
            {
                GeneDef def = Find<GeneDef>(Genes[i].DefName, ref missing);

                if (def == null || pawn.genes.GetGene(def) != null)
                    continue;

                bool xeno = Genes[i].Xenogene;

                Gene added = pawn.genes.AddGene(def, xeno);

                changes.Record("genes", () =>
                {
                    if (added != null)
                        pawn.genes.RemoveGene(added);
                });
            }

            return missing;
        }

        /// <summary>
        /// Dresses and arms the pawn, replacing whatever they had.
        ///
        /// Removed items are held in their undo entry rather than destroyed, the same as everywhere else in this
        /// window, so a reverted import gives back the original coat and not a fresh copy of it.
        /// </summary>
        private int GearOnto(Pawn pawn, EditorChanges changes)
        {
            int missing = 0;

            if (pawn.apparel != null && Apparel.Count > 0)
            {
                List<Apparel> had = new List<Apparel>(pawn.apparel.WornApparel);

                for (int i = 0; i < had.Count; i++)
                {
                    Apparel ap = had[i];

                    pawn.apparel.Remove(ap);

                    changes.Record("apparel", () => pawn.apparel.Wear(ap, false));
                }

                for (int i = 0; i < Apparel.Count; i++)
                {
                    Apparel made = Make(Apparel[i], ref missing) as Apparel;

                    if (made == null)
                        continue;

                    if (!ApparelUtility.HasPartsToWear(pawn, made.def))
                    {
                        made.Destroy();

                        continue;
                    }

                    pawn.apparel.Wear(made, false);

                    changes.Record("apparel", () =>
                    {
                        pawn.apparel.Remove(made);

                        made.Destroy();
                    });
                }
            }

            if (pawn.equipment == null || Weapon == null)
                return missing;

            ThingWithComps was = pawn.equipment.Primary;

            ThingWithComps weapon = Make(Weapon, ref missing) as ThingWithComps;

            if (weapon == null)
                return missing;

            if (was != null)
                pawn.equipment.Remove(was);

            pawn.equipment.AddEquipment(weapon);

            changes.Record("weapon", () =>
            {
                pawn.equipment.Remove(weapon);

                weapon.Destroy();

                if (was != null)
                    pawn.equipment.AddEquipment(was);
            });

            return missing;
        }

        /// <summary>
        /// Replaces the durable half of the target's health with the template's.
        ///
        /// <b>Replaced rather than added to,</b> for the same reason as traits: a template describes a person, and
        /// importing a character who kept the previous occupant's peg leg is a bug rather than a feature.
        ///
        /// <b>Reversible through a snapshot of the same shape,</b> which is the one place in this class where an
        /// undo entry is not a single field. Recording the inverse of "removed a bionic arm and installed a peg
        /// leg" one hediff at a time would need a closure per surgery; reading the target's durable set first and
        /// writing it back through the same two calls is exact and is the code that is already tested.
        ///
        /// <b>Nothing is touched outside those four categories.</b> A pawn who was bleeding before the import is
        /// still bleeding after it, and after reverting it.
        /// </summary>
        private int HealthOnto(Pawn pawn, EditorChanges changes)
        {
            if (pawn.health == null || Health.Count == 0)
                return 0;

            List<TemplateHediff> was = Durable(pawn);

            Strip(pawn);

            int missing = Write(pawn, Health);

            changes.Record("implants and injuries", () =>
            {
                Strip(pawn);

                int ignored = 0;

                Write(pawn, was, ref ignored);
            });

            return missing;
        }

        /// <summary>
        /// Takes off everything durable, leaving the body as its race describes it.
        ///
        /// <b>Through <c>RestorePart</c> wherever there is a part,</b> because that is the call that undoes a
        /// surgery: it puts the natural part back and clears whatever else was on it. Removing an added-part
        /// hediff on its own leaves a body part that the game no longer has a record of being replaced.
        ///
        /// Bounded rather than a while-true. Each pass takes at least one hediff, and the cap is far past any real
        /// body -- but a modded hediff that re-adds itself on removal would otherwise hang the game inside a
        /// window that had just been asked to do something innocuous.
        /// </summary>
        private static void Strip(Pawn pawn)
        {
            UIGuard.Try("Template.Strip", () =>
            {
                int guard = 0;

                while (guard++ < 400)
                {
                    Hediff found = null;

                    List<Hediff> all = pawn.health.hediffSet.hediffs;

                    for (int i = 0; i < all.Count; i++)
                    {
                        if (all[i] != null && all[i].def != null && Category(all[i]) >= 0)
                        {
                            found = all[i];

                            break;
                        }
                    }

                    if (found == null)
                        return;

                    if (found.Part != null)
                        pawn.health.RestorePart(found.Part);
                    else
                        pawn.health.RemoveHediff(found);
                }
            }, null);
        }

        private static int Write(Pawn pawn, List<TemplateHediff> saved)
        {
            int missing = 0;

            Write(pawn, saved, ref missing);

            return missing;
        }

        /// <summary>
        /// Puts a durable set onto a pawn, in the order the categories were sorted into.
        ///
        /// A scar has to be told it is one: an injury hediff added by hand is a fresh wound until
        /// <c>HediffComp_GetsPermanent.IsPermanent</c> is set, and a fresh wound heals away within the day.
        /// </summary>
        private static void Write(Pawn pawn, List<TemplateHediff> saved, ref int missing)
        {
            for (int i = 0; i < saved.Count; i++)
            {
                TemplateHediff entry = saved[i];

                HediffDef def = Find<HediffDef>(entry.DefName, ref missing);

                if (def == null)
                    continue;

                // A part this body does not have. Counted by Resolve, and skipped rather than applied to the
                // whole body -- a bionic arm on no particular part is not a thing the game can draw.
                BodyPartRecord part = Resolve(pawn, entry, ref missing);

                if (part == null && !entry.PartDef.NullOrEmpty())
                    continue;

                int caught = missing;

                UIGuard.Try("Template.WriteHediff", () =>
                {
                    Hediff made = HediffMaker.MakeHediff(def, pawn, part);

                    if (made == null)
                        return;

                    if (entry.Severity > 0f)
                        made.Severity = entry.Severity;

                    pawn.health.AddHediff(made, part);

                    if (!entry.Permanent)
                        return;

                    HediffComp_GetsPermanent permanent = made.TryGetComp<HediffComp_GetsPermanent>();

                    if (permanent != null)
                        permanent.IsPermanent = true;
                }, null);

                missing = caught;
            }
        }

        private static Thing Make(TemplateThing saved, ref int missing)
        {
            if (saved == null)
                return null;

            ThingDef def = Find<ThingDef>(saved.DefName, ref missing);

            if (def == null)
                return null;

            ThingDef stuff = null;

            if (def.MadeFromStuff)
            {
                int ignored = 0;

                stuff = Find<ThingDef>(saved.Stuff, ref ignored) ?? GenStuff.DefaultStuffFor(def);
            }

            Thing made = ThingMaker.MakeThing(def, stuff);

            QualityCategory quality;

            if (made != null && !saved.Quality.NullOrEmpty() && Enum.TryParse(saved.Quality, out quality))
            {
                CompQuality comp = made.TryGetComp<CompQuality>();

                if (comp != null)
                    comp.SetQuality(quality, ArtGenerationContext.Colony);
            }

            return made;
        }

        /// <summary>
        /// A def by name, counting the ones this install does not have.
        ///
        /// A null or empty name is not a miss: it means the template did not say, which leaves the field alone.
        /// Only a name that was written and cannot be resolved counts.
        /// </summary>
        private static T Find<T>(string defName, ref int missing) where T : Def
        {
            if (defName.NullOrEmpty())
                return null;

            T def = DefDatabase<T>.GetNamedSilentFail(defName);

            if (def == null)
                missing++;

            return def;
        }

        private static bool Parse(string hex, out Color colour)
        {
            colour = Color.white;

            if (hex.NullOrEmpty())
                return false;

            return ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out colour);
        }
    }
}
