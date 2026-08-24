using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// The five panels that are the pawn as a character: identity, backstory, traits, skills, genes.
    ///
    /// <b>Together because they are read together and never mixed with state.</b> These are the things a
    /// storyteller decided once; the panels in <see cref="EditorState"/> are what happens to be true this hour.
    /// Appearance is the sixth of this half and lives on its own, because it is the only one with a render
    /// beside it and it is longer than the other five put together.
    /// </summary>
    internal static class EditorWho
    {
        // ---------------------------------------------------------------------------------------
        // Identity
        // ---------------------------------------------------------------------------------------

        private static readonly UITextBoxControl FirstBox = new UITextBoxControl { MaxLength = 32 };

        private static readonly UITextBoxControl NickBox = new UITextBoxControl { MaxLength = 32 };

        private static readonly UITextBoxControl LastBox = new UITextBoxControl { MaxLength = 32 };

        /// <summary>Whose name is in the boxes, so switching pawn reseeds them rather than renaming the new one.</summary>
        private static Pawn seededFor;

        private static readonly string[] Genders = { "Male", "Female" };

        /// <summary>
        /// Name, sex and both ages.
        ///
        /// <b>Chronological above biological means cryptosleep,</b> and this panel says so rather than leaving two
        /// number boxes to be puzzled over. The gap between them is the only thing either number means on its
        /// own.
        ///
        /// <b>Setting the biological age does not re-roll age injuries,</b> which is a departure from the
        /// proposal. The call that would -- <c>Pawn_AgeTracker.BirthdayBiological</c> -- is private and fires
        /// birthday letters and growth moments as it goes, so driving it from a text box would spam the message
        /// log with one letter per keystroke. Age injuries are their own button instead, which is also the honest
        /// shape: aging somebody up and giving them a bad back are two decisions.
        /// </summary>
        internal static float Identity(Rect view, EditorContext context)
        {
            Pawn pawn = context.Pawn;
            UIColorPaletteDef palette = context.Palette;

            Seed(pawn);

            float y = view.y;

            y = EditorParts.Heading(view, y, "Name", palette);

            NameTriple triple = UIGuard.Try("Editor.NameTriple", () => pawn.Name as NameTriple, null, null);

            Rect row = new Rect(view.x, y, view.width, EditorParts.FieldHeight);

            if (triple != null)
            {
                Box(EditorParts.Column(row, 0, 3), "first name", FirstBox, palette, context,
                    () => Rename(pawn, context, FirstBox.Text, NickBox.Text, LastBox.Text));

                Box(EditorParts.Column(row, 1, 3), "nickname", NickBox, palette, context,
                    () => Rename(pawn, context, FirstBox.Text, NickBox.Text, LastBox.Text));

                Box(EditorParts.Column(row, 2, 3), "surname", LastBox, palette, context,
                    () => Rename(pawn, context, FirstBox.Text, NickBox.Text, LastBox.Text));
            }
            else
            {
                // Animals and anything else carrying a NameSingle. One box, and writing to it makes a
                // NameSingle back rather than promoting them to a three part name they never had.
                Box(EditorParts.Column(row, 0, 3), "name", NickBox, palette, context,
                    () => context.Changes.Set("name", () => NameOf(pawn),
                        text => SetSingle(pawn, text), NickBox.Text));
            }

            y = row.yMax + EditorParts.BlockGap;

            y = EditorParts.Heading(view, y, "Age and sex", palette);

            row = new Rect(view.x, y, view.width, EditorParts.FieldHeight);

            int chosen = EditorParts.Segments(EditorParts.Column(row, 0, 3), "sex", Genders,
                UIGuard.Try("Editor.Gender", () => pawn.gender == Gender.Female ? 1 : 0, 0, null), palette);

            if (chosen >= 0)
            {
                Gender wanted = chosen == 1 ? Gender.Female : Gender.Male;

                context.Changes.Set("sex", () => pawn.gender, g =>
                {
                    pawn.gender = g;

                    EditorParts.Redraw(pawn);
                }, wanted);

                if (!context.Humanlike)
                    EditorParts.Warn("Changing an animal's sex does not change what it can do: eggs, milk and "
                                     + "mating are driven by its own comps, which follow this and may take a "
                                     + "day to notice.");
            }

            Years(EditorParts.Column(row, 1, 3), "biological age", palette, context,
                UIGuard.Try("Editor.BioAge", () => pawn.ageTracker.AgeBiologicalYears, 0, null),
                years => context.Changes.Set("biological age",
                    () => pawn.ageTracker.AgeBiologicalTicks,
                    ticks =>
                    {
                        pawn.ageTracker.AgeBiologicalTicks = ticks;

                        EditorParts.Redraw(pawn);
                    },
                    years * (long) GenDate.TicksPerYear));

            Years(EditorParts.Column(row, 2, 3), "chronological age", palette, context,
                UIGuard.Try("Editor.ChronoAge", () => pawn.ageTracker.AgeChronologicalYears, 0, null),
                years => context.Changes.Set("chronological age",
                    () => pawn.ageTracker.AgeChronologicalTicks,
                    ticks => pawn.ageTracker.AgeChronologicalTicks = ticks,
                    years * (long) GenDate.TicksPerYear));

            y = row.yMax + 4f;

            int gap = UIGuard.Try("Editor.AgeGap",
                () => pawn.ageTracker.AgeChronologicalYears - pawn.ageTracker.AgeBiologicalYears, 0, null);

            if (gap > 0)
                y = EditorParts.Note(view, y,
                    gap + " years of the difference is time they spent not aging: cryptosleep, a growth vat, or "
                        + "age reversal.", palette);

            y += EditorParts.RowGap;

            if (context.Humanlike && EditorParts.Add(view, y, "Re-roll age injuries", palette, true,
                    "Clears nothing. Rolls the permanent injuries and chronic conditions the game would have "
                    + "given somebody who reached this age, on top of what they already have."))
            {
                UIGuard.Try("Editor.AgeInjuries", () =>
                {
                    AgeInjuryUtility.GenerateRandomOldAgeInjuries(pawn, true);

                    context.Changes.RecordPermanent("age injuries");
                }, "The age injuries could not be rolled.");
            }

            return y + EditorParts.ControlHeight + EditorParts.BlockGap - view.y;
        }

        private static void Seed(Pawn pawn)
        {
            if (seededFor == pawn)
                return;

            seededFor = pawn;

            UIGuard.Try("Editor.Seed", () =>
            {
                NameTriple triple = pawn.Name as NameTriple;

                if (triple != null)
                {
                    FirstBox.Text = triple.First ?? string.Empty;
                    NickBox.Text = triple.Nick ?? string.Empty;
                    LastBox.Text = triple.Last ?? string.Empty;

                    return;
                }

                FirstBox.Text = string.Empty;
                LastBox.Text = string.Empty;
                NickBox.Text = NameOf(pawn);
            }, null);
        }

        private static string NameOf(Pawn pawn)
        {
            return UIGuard.Try<string>("Editor.NameOf",
                () => pawn.Name != null ? pawn.Name.ToStringShort : string.Empty, string.Empty, null);
        }

        private static void SetSingle(Pawn pawn, string text)
        {
            if (text.NullOrEmpty())
                return;

            pawn.Name = new NameSingle(text);
        }

        /// <summary>
        /// Writes a three part name back.
        ///
        /// <b>An empty first or last name is allowed and an empty nickname is not.</b> The nickname is what every
        /// list in the game shows, so a pawn with none is a pawn with no label; RimWorld itself falls back to the
        /// first name, and reproducing that fallback here is less surprising than refusing the keystroke.
        /// </summary>
        private static void Rename(Pawn pawn, EditorContext context, string first, string nick, string last)
        {
            string shown = nick.NullOrEmpty() ? first : nick;

            if (shown.NullOrEmpty())
                return;

            context.Changes.Set("name", () => pawn.Name,
                name =>
                {
                    pawn.Name = name;

                    PortraitsCache.SetDirty(pawn);
                },
                new NameTriple(first ?? string.Empty, shown, last ?? string.Empty));
        }

        private static void Box(Rect cell, string caption, UITextBoxControl box, UIColorPaletteDef palette,
            EditorContext context, System.Action changed)
        {
            Rect control = EditorParts.Field(cell, caption, palette);

            if (box.Draw(control, palette))
                changed();
        }

        /// <summary>
        /// A whole number of years as a slider.
        ///
        /// <b>A slider rather than a text box,</b> because an age is chosen by feel between two bounds and a text
        /// box for it means every intermediate keystroke is a valid age: typing 40 over 8 passes through 4, and a
        /// four year old colonist has a different body type and a different life stage.
        /// </summary>
        private static void Years(Rect cell, string caption, UIColorPaletteDef palette, EditorContext context,
            int current, System.Action<int> apply)
        {
            float value = EditorParts.Slider(cell, caption, current, 0f, 120f, palette, current + " years");

            int years = Mathf.RoundToInt(value);

            if (years != current)
                apply(years);
        }

        // ---------------------------------------------------------------------------------------
        // Backstory
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Childhood and adulthood, each with what it grants shown before it is taken.
        ///
        /// <b>Nothing is hidden for not fitting.</b> A childhood meant for a child, an adulthood the pawn's
        /// faction never spawns: both are marked and both are takeable. Choosing a backstory blind is what makes
        /// vanilla's own character creation a wiki tab, and refusing the odd one would put this tool in the same
        /// position.
        /// </summary>
        internal static float Backstory(Rect view, EditorContext context)
        {
            Pawn pawn = context.Pawn;
            UIColorPaletteDef palette = context.Palette;

            float y = EditorParts.Heading(view, view.y, "Backstory", palette);

            Rect row = new Rect(view.x, y, view.width, EditorParts.FieldHeight);

            Slot(EditorParts.Column(row, 0, 2), "childhood", BackstorySlot.Childhood, context, palette);
            Slot(EditorParts.Column(row, 1, 2), "adulthood", BackstorySlot.Adulthood, context, palette);

            y = row.yMax + EditorParts.RowGap;

            y = Describe(view, y, pawn, pawn.story != null ? pawn.story.Childhood : null, "Childhood", palette);
            y = Describe(view, y, pawn, pawn.story != null ? pawn.story.Adulthood : null, "Adulthood", palette);

            y = EditorParts.Heading(view, y, "What the backstories disable", palette);

            List<WorkTypeDef> disabled = new List<WorkTypeDef>();

            UIGuard.Try("Editor.Disabled", () =>
            {
                // Every disabled type, not only the permanent ones: a trait that disables violence counts here
                // just as much as a backstory that does, and the panel's heading does not claim otherwise.
                List<WorkTypeDef> found = pawn.GetDisabledWorkTypes();

                for (int i = 0; found != null && i < found.Count; i++)
                    disabled.Add(found[i]);
            }, null);

            if (disabled.Count == 0)
                return EditorParts.Note(view, y, "Nothing. They can be put on any work type.", palette)
                       + EditorParts.BlockGap - view.y;

            List<string> names = new List<string>();

            for (int i = 0; i < disabled.Count; i++)
                names.Add(disabled[i].gerundLabel.NullOrEmpty()
                    ? disabled[i].labelShort
                    : disabled[i].gerundLabel);

            y = EditorParts.Note(view, y, names.ToCommaList(true).CapitalizeFirst() + ".", palette,
                palette.Warning);

            return y + EditorParts.BlockGap - view.y;
        }

        private static void Slot(Rect cell, string caption, BackstorySlot slot, EditorContext context,
            UIColorPaletteDef palette)
        {
            Pawn pawn = context.Pawn;

            BackstoryDef current = UIGuard.Try("Editor.Slot",
                () => pawn.story != null ? pawn.story.GetBackstory(slot) : null, null, null);

            if (!EditorParts.Picker(cell, caption, Title(current, pawn), palette,
                    EditorParts.DescriptionOf(current)))
                return;

            List<EditorOption> options = new List<EditorOption>();

            UIGuard.Try("Editor.Backstories", () =>
            {
                List<BackstoryDef> all = DefDatabase<BackstoryDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    BackstoryDef def = all[i];

                    if (def.slot != slot)
                        continue;

                    BackstoryDef captured = def;

                    options.Add(new EditorOption
                    {
                        Label = Title(def, pawn),
                        Note = Gains(def),
                        Tooltip = EditorParts.DescriptionOf(def),
                        Current = def == current,
                        Marked = Mismatch(def, pawn),
                        Chosen = () => context.Changes.Set(caption,
                            () => pawn.story.GetBackstory(slot),
                            value =>
                            {
                                if (slot == BackstorySlot.Childhood)
                                    pawn.story.Childhood = value;
                                else
                                    pawn.story.Adulthood = value;

                                Recache(pawn);
                            },
                            captured)
                    });
                }

                options.Sort((a, b) => string.Compare(a.Label, b.Label, System.StringComparison.Ordinal));
            }, null);

            Dialog_PickFrom.Open("Choose a " + caption, options, "Search backstories");
        }

        private static string Title(BackstoryDef def, Pawn pawn)
        {
            if (def == null)
                return "none";

            return UIGuard.Try<string>("Editor.BackstoryTitle", () => def.TitleCapFor(pawn.gender),
                def.defName, null);
        }

        /// <summary>The skills a backstory grants, which is the reason the picker is a window.</summary>
        private static string Gains(BackstoryDef def)
        {
            return UIGuard.Try<string>("Editor.BackstoryGains", () =>
            {
                if (def.skillGains == null || def.skillGains.Count == 0)
                    return def.workDisables == WorkTags.None ? null : "disables work";

                List<string> parts = new List<string>();

                for (int i = 0; i < def.skillGains.Count && i < 4; i++)
                {
                    SkillGain gain = def.skillGains[i];

                    if (gain == null || gain.skill == null)
                        continue;

                    parts.Add(gain.skill.skillLabel.CapitalizeFirst() + " "
                              + gain.amount.ToStringWithSign());
                }

                if (def.workDisables != WorkTags.None)
                    parts.Add("disables work");

                return parts.Count == 0 ? null : string.Join(", ", parts.ToArray());
            }, null, null);
        }

        /// <summary>What is wrong with taking this backstory, or null when nothing is.</summary>
        private static string Mismatch(BackstoryDef def, Pawn pawn)
        {
            return UIGuard.Try<string>("Editor.BackstoryFit", () =>
            {
                bool child = def.spawnCategories != null && def.spawnCategories.Contains("Child");

                if (child && pawn.DevelopmentalStage.Adult())
                    return "meant for a child";

                if (!child && !pawn.DevelopmentalStage.Adult() && def.slot == BackstorySlot.Adulthood)
                    return "meant for an adult";

                return null;
            }, null, null);
        }

        private static float Describe(Rect view, float y, Pawn pawn, BackstoryDef def, string when,
            UIColorPaletteDef palette)
        {
            if (def == null)
                return y;

            string text = UIGuard.Try<string>("Editor.BackstoryDesc",
                () => def.FullDescriptionFor(pawn).Resolve(), null, null);

            if (text.NullOrEmpty())
                return y;

            y = EditorParts.Heading(view, y, when + ": " + Title(def, pawn), palette);

            return EditorParts.Note(view, y, text, palette) + EditorParts.RowGap;
        }

        /// <summary>
        /// Tells everything that caches a backstory to look again.
        ///
        /// A backstory decides disabled work types and the title on every list in the game, and both are cached
        /// off it. The skill cache is the one that bites: a skill disabled by the old backstory stays disabled
        /// until something dirties it, so the pawn ends up unable to do a job nothing is stopping them from doing.
        /// </summary>
        private static void Recache(Pawn pawn)
        {
            // One call, because it is already the funnel: it clears all three of the pawn's own caches and then
            // notifies the work settings and the skills itself. Calling those two as well, which an earlier
            // version did, is two redundant invalidations and a second place to keep in step with vanilla.
            UIGuard.Try("Editor.Recache", pawn.Notify_DisabledWorkTypesChanged, null);
        }

        // ---------------------------------------------------------------------------------------
        // Traits
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Traits as rows, with degree where a trait has one.
        ///
        /// <b>Conflicts are flagged rather than blocked,</b> and so is the fifth trait. Vanilla only enforces its
        /// cap while generating a pawn, so there is nothing to override -- but a colonist with nine traits is a
        /// colonist whose mood is doing something nobody can follow, and that is worth saying once.
        /// </summary>
        internal static float Traits(Rect view, EditorContext context)
        {
            Pawn pawn = context.Pawn;
            UIColorPaletteDef palette = context.Palette;

            TraitSet traits = UIGuard.Try("Editor.TraitSet",
                () => pawn.story != null ? pawn.story.traits : null, null, null);

            if (traits == null)
                return EditorParts.Note(view, view.y, "This pawn has no traits to edit.", palette) - view.y;

            float y = EditorParts.Heading(view, view.y, "Traits", palette,
                traits.allTraits.Count + " of the usual 4");

            List<Trait> held = new List<Trait>(traits.allTraits);

            for (int i = 0; i < held.Count; i++)
            {
                Trait trait = held[i];

                if (trait == null)
                    continue;

                Rect row;

                string degree = Degrees(trait) > 1 ? "degree " + trait.Degree : null;

                if (EditorParts.Row(view, y, trait.LabelCap, degree, palette.TextSecondary, palette, out row,
                        Description(trait)))
                    Remove(context, trait);

                // The degree is a second control on the same row, so the row's own hit target has to stop short
                // of it. Measured from the row rather than from the label, which is the trap the pawns tab hit.
                if (degree != null)
                {
                    Rect cycle = new Rect(row.xMax - 46f, row.y + 2f, 22f, row.height - 4f);

                    if (Widgets.ButtonInvisible(cycle))
                        Cycle(context, trait);

                    TooltipHandler.TipRegion(cycle, (TipSignal) "Click to step through this trait's degrees.");
                }

                y = row.yMax + 4f;
            }

            if (held.Count == 0)
                y = EditorParts.Note(view, y, "None.", palette);

            y += EditorParts.RowGap;

            if (EditorParts.Add(view, y, "Add a trait", palette))
                Offer(context, traits);

            return y + EditorParts.ControlHeight + EditorParts.BlockGap - view.y;
        }

        private static int Degrees(Trait trait)
        {
            return UIGuard.Try("Editor.Degrees",
                () => trait.def.degreeDatas != null ? trait.def.degreeDatas.Count : 1, 1, null);
        }

        private static string Description(Trait trait)
        {
            return UIGuard.Try<string>("Editor.TraitDesc",
                () => trait.CurrentData != null ? trait.CurrentData.description : null, null, null);
        }

        private static void Remove(EditorContext context, Trait trait)
        {
            TraitSet traits = context.Pawn.story.traits;

            UIGuard.Try("Editor.RemoveTrait", () =>
            {
                TraitDef def = trait.def;
                int degree = trait.Degree;

                traits.RemoveTrait(trait);

                Recache(context.Pawn);

                context.Changes.Record("traits", () =>
                {
                    traits.GainTrait(new Trait(def, degree), true);

                    Recache(context.Pawn);
                });
            }, "That trait could not be removed.");
        }

        /// <summary>Steps a trait to its next degree, which is a remove and a gain because a degree is immutable.</summary>
        private static void Cycle(EditorContext context, Trait trait)
        {
            UIGuard.Try("Editor.CycleTrait", () =>
            {
                List<TraitDegreeData> degrees = trait.def.degreeDatas;

                if (degrees == null || degrees.Count < 2)
                    return;

                int at = 0;

                for (int i = 0; i < degrees.Count; i++)
                {
                    if (degrees[i].degree == trait.Degree)
                        at = i;
                }

                int wanted = degrees[(at + 1) % degrees.Count].degree;

                TraitSet traits = context.Pawn.story.traits;
                TraitDef def = trait.def;
                int was = trait.Degree;

                traits.RemoveTrait(trait);
                traits.GainTrait(new Trait(def, wanted), true);

                Recache(context.Pawn);

                context.Changes.Record("traits", () =>
                {
                    Trait now = traits.GetTrait(def, wanted);

                    if (now != null)
                        traits.RemoveTrait(now);

                    traits.GainTrait(new Trait(def, was), true);

                    Recache(context.Pawn);
                });
            }, "That trait's degree could not be changed.");
        }

        private static void Offer(EditorContext context, TraitSet traits)
        {
            Pawn pawn = context.Pawn;

            List<EditorOption> options = new List<EditorOption>();

            UIGuard.Try("Editor.TraitOptions", () =>
            {
                List<TraitDef> all = DefDatabase<TraitDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    TraitDef def = all[i];

                    List<TraitDegreeData> degrees = def.degreeDatas;

                    if (degrees == null)
                        continue;

                    for (int d = 0; d < degrees.Count; d++)
                    {
                        TraitDegreeData data = degrees[d];

                        if (traits.HasTrait(def, data.degree))
                            continue;

                        TraitDef captured = def;
                        int degree = data.degree;

                        options.Add(new EditorOption
                        {
                            Label = data.GetLabelCapFor(pawn),
                            Note = Cost(def, data),
                            Tooltip = data.description,
                            Marked = Conflict(traits, def),
                            Chosen = () => Gain(context, traits, captured, degree)
                        });
                    }
                }

                options.Sort((a, b) => string.Compare(a.Label, b.Label, System.StringComparison.Ordinal));
            }, null);

            Dialog_PickFrom.Open("Add a trait", options, "Search traits");
        }

        private static string Cost(TraitDef def, TraitDegreeData data)
        {
            return UIGuard.Try<string>("Editor.TraitCost", () =>
            {
                if (data.skillGains == null || data.skillGains.Count == 0)
                    return def.disabledWorkTags == WorkTags.None ? null : "disables work";

                List<string> parts = new List<string>();

                for (int i = 0; i < data.skillGains.Count; i++)
                {
                    SkillGain gain = data.skillGains[i];

                    if (gain != null && gain.skill != null)
                        parts.Add(gain.skill.skillLabel.CapitalizeFirst() + " " + gain.amount.ToStringWithSign());
                }

                if (def.disabledWorkTags != WorkTags.None)
                    parts.Add("disables work");

                return parts.Count == 0 ? null : string.Join(", ", parts.ToArray());
            }, null, null);
        }

        private static string Conflict(TraitSet traits, TraitDef def)
        {
            return UIGuard.Try<string>("Editor.TraitConflict", () =>
            {
                if (def.conflictingTraits == null)
                    return null;

                for (int i = 0; i < def.conflictingTraits.Count; i++)
                {
                    if (traits.HasTrait(def.conflictingTraits[i]))
                        return "conflicts with " + def.conflictingTraits[i].degreeDatas[0].label;
                }

                return null;
            }, null, null);
        }

        /// <summary>
        /// Gains a trait, suppressing vanilla's conflict handling.
        ///
        /// <b><c>suppressConflicts: true</c> on purpose.</b> Left false, <c>GainTrait</c> quietly suppresses the
        /// trait it conflicts with, so taking Kind on a Psychopath appears to work and leaves one of them inert.
        /// The editor said in the picker what the conflict was; having said it, it should do exactly what was
        /// asked and leave both traits real.
        /// </summary>
        private static void Gain(EditorContext context, TraitSet traits, TraitDef def, int degree)
        {
            UIGuard.Try("Editor.GainTrait", () =>
            {
                traits.GainTrait(new Trait(def, degree), true);

                Recache(context.Pawn);

                if (traits.allTraits.Count > 4)
                    EditorParts.Warn(context.Pawn.LabelShortCap + " now has " + traits.allTraits.Count
                                     + " traits. Nothing in the game refuses that, but their mood will be doing "
                                     + "several things at once.");

                context.Changes.Record("traits", () =>
                {
                    Trait added = traits.GetTrait(def, degree);

                    if (added != null)
                        traits.RemoveTrait(added);

                    Recache(context.Pawn);
                });
            }, "That trait could not be added.");
        }

        // ---------------------------------------------------------------------------------------
        // Skills
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Twelve sliders and twelve passions.
        ///
        /// <b>A disabled skill is shown and can still be set,</b> since undoing exactly that is a thing people
        /// open an editor for. It says which backstory or trait disabled it rather than only greying out.
        ///
        /// <b>Experience within the level is kept.</b> <c>SkillRecord.Level</c> writes only the level integer and
        /// leaves <c>xpSinceLastLevel</c> alone, so nudging a 6 to a 7 does not silently cost a day of learning.
        /// That is vanilla's behaviour rather than ours; it is written down because it is the sort of thing that
        /// would be easy to break by "tidying up" this setter.
        /// </summary>
        internal static float Skills(Rect view, EditorContext context)
        {
            Pawn pawn = context.Pawn;
            UIColorPaletteDef palette = context.Palette;

            Pawn_SkillTracker skills = UIGuard.Try("Editor.Skills", () => pawn.skills, null, null);

            if (skills == null)
                return EditorParts.Note(view, view.y, "This pawn has no skills to edit.", palette) - view.y;

            float y = EditorParts.Heading(view, view.y, "Skills", palette, "0 to 20");

            List<SkillDef> all = DefDatabase<SkillDef>.AllDefsListForReading;

            for (int i = 0; i < all.Count; i++)
            {
                SkillRecord record = UIGuard.Try("Editor.Skill", () => skills.GetSkill(all[i]), null, null);

                if (record == null)
                    continue;

                y = Skill(view, y, record, context, palette);
            }

            return y + EditorParts.BlockGap - view.y;
        }

        private static float Skill(Rect view, float y, SkillRecord record, EditorContext context,
            UIColorPaletteDef palette)
        {
            float height = EditorParts.CaptionHeight + 20f;

            Rect row = new Rect(view.x, y, view.width, height);

            Rect passion = new Rect(row.xMax - 28f, row.y + 2f, 24f, 20f);
            Rect lane = new Rect(row.x, row.y, Mathf.Max(60f, row.width - 34f), height);

            bool disabled = UIGuard.Try("Editor.SkillDisabled", () => record.TotallyDisabled, false, null);

            string readout = record.Level.ToString();

            if (disabled)
                readout += "  disabled";

            float value = EditorParts.Slider(lane, record.def.skillLabel.CapitalizeFirst(), record.Level, 0f,
                20f, palette, readout);

            int level = Mathf.RoundToInt(value);

            if (level != record.Level)
                context.Changes.Set(record.def.skillLabel, () => record.Level, v => record.Level = v, level);

            PassionMark(passion, record, context, palette);

            if (disabled)
                TooltipHandler.TipRegion(lane, (TipSignal) ("A backstory, trait or gene disables this skill. The "
                                                            + "level can still be set, and it will apply the "
                                                            + "moment whatever disabled it is removed."));

            return row.yMax + 2f;
        }

        /// <summary>
        /// The passion, as the same two flames the rest of the game uses, cycling on click.
        ///
        /// Its own textures rather than ours: a player reads a flame as a passion everywhere else, and drawing a
        /// different mark here would be a second vocabulary for a fact they already know.
        /// </summary>
        private static void PassionMark(Rect rect, SkillRecord record, EditorContext context,
            UIColorPaletteDef palette)
        {
            Passion current = record.passion;

            Texture2D icon = current == Passion.Major
                ? SkillUI.PassionMajorIcon
                : current == Passion.Minor
                    ? SkillUI.PassionMinorIcon
                    : null;

            bool over = Mouse.IsOver(rect);

            if (over)
                UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceRaised);

            if (icon != null)
            {
                Color previous = GUI.color;

                GUI.color = current == Passion.Major ? palette.Warning : palette.AccentMuted;

                GUI.DrawTexture(new Rect(rect.center.x - 8f, rect.center.y - 8f, 16f, 16f), icon);

                GUI.color = previous;
            }
            else
            {
                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;

                try
                {
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = palette.TextDisabled;

                    Widgets.Label(rect, "-");
                }
                finally
                {
                    GUI.color = previousColor;
                    Text.Anchor = previousAnchor;
                    Text.Font = previousFont;
                }
            }

            TooltipHandler.TipRegion(rect, (TipSignal) "Click to step through no passion, minor and burning.");

            if (!Widgets.ButtonInvisible(rect))
                return;

            Passion wanted = current == Passion.None
                ? Passion.Minor
                : current == Passion.Minor
                    ? Passion.Major
                    : Passion.None;

            context.Changes.Set(record.def.skillLabel + " passion", () => record.passion,
                p => record.passion = p, wanted);
        }

        // ---------------------------------------------------------------------------------------
        // Genes
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The xenotype and every gene, with the three biostats a gene is judged by.
        ///
        /// <b>The full list is here rather than on Appearance, and that is the honest cut.</b> A gene that only
        /// changes the look is editable beside the thing it changes; metabolism, complexity and abilities belong
        /// on their own panel, because Fire immunity filed under a hair picker would be filed by its side effect.
        /// </summary>
        internal static float Genes(Rect view, EditorContext context)
        {
            Pawn pawn = context.Pawn;
            UIColorPaletteDef palette = context.Palette;

            Pawn_GeneTracker genes = UIGuard.Try("Editor.Genes", () => pawn.genes, null, null);

            if (genes == null)
                return EditorParts.Note(view, view.y, "This pawn has no genes.", palette) - view.y;

            float y = EditorParts.Heading(view, view.y, "Xenotype", palette);

            Rect row = new Rect(view.x, y, view.width, EditorParts.FieldHeight);

            Xenotype(EditorParts.Column(row, 0, 2), context, genes, palette);

            y = row.yMax + EditorParts.BlockGap;

            y = EditorParts.Heading(view, y, "Genes", palette, Biostats(genes));

            List<Gene> held = new List<Gene>(genes.GenesListForReading);

            // Tiles rather than rows. The endogene and xenogene backgrounds and the dimming of an overridden
            // gene are the game's own, so the note each row used to carry -- "endogene, inactive" -- is now the
            // tile's own appearance rather than a fourteenth repetition of the same two words.
            y = EditorGeneTiles.Draw(view, y, held, pawn, palette,
                gene => RemoveGene(context, genes, gene,
                    UIGuard.Try("Editor.IsXeno", () => genes.IsXenogene(gene), false, null)));

            y += EditorParts.RowGap;

            if (EditorParts.Add(view, y, "Add a gene", palette))
                OfferGene(context, genes);

            return y + EditorParts.ControlHeight + EditorParts.BlockGap - view.y;
        }

        private static string Biostats(Pawn_GeneTracker genes)
        {
            return UIGuard.Try<string>("Editor.Biostats", () =>
            {
                int metabolism = 0;
                int complexity = 0;
                int archites = 0;

                List<Gene> all = genes.GenesListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] == null || all[i].def == null)
                        continue;

                    metabolism += all[i].def.biostatMet;
                    complexity += all[i].def.biostatCpx;
                    archites += all[i].def.biostatArc;
                }

                string text = "metabolism " + metabolism.ToStringWithSign() + "  complexity " + complexity;

                return archites > 0 ? text + "  archites " + archites : text;
            }, null, null);
        }

        private static void Xenotype(Rect cell, EditorContext context, Pawn_GeneTracker genes,
            UIColorPaletteDef palette)
        {
            if (!EditorParts.Picker(cell, "xenotype", EditorParts.LabelOf(genes.Xenotype), palette,
                    "Setting a xenotype replaces every gene the pawn has with that xenotype's, which is what "
                    + "the game itself does. Individual genes can be added back below."))
                return;

            List<EditorOption> options = new List<EditorOption>();

            UIGuard.Try("Editor.Xenotypes", () =>
            {
                List<XenotypeDef> all = DefDatabase<XenotypeDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    XenotypeDef def = all[i];
                    XenotypeDef captured = def;

                    options.Add(new EditorOption
                    {
                        Label = EditorParts.LabelOf(def),
                        Note = def.genes != null ? def.genes.Count + " genes" : null,
                        Tooltip = EditorParts.DescriptionOf(def),
                        Current = def == genes.Xenotype,
                        Chosen = () => UIGuard.Try("Editor.SetXenotype", () =>
                        {
                            genes.SetXenotype(captured);

                            EditorParts.Redraw(context.Pawn);

                            // Replacing a whole gene set is not something one closure can put back: the genes
                            // that were there were themselves a mixture of xenogenes and endogenes with their
                            // own state. Recorded as permanent rather than pretending otherwise.
                            context.Changes.RecordPermanent("xenotype");
                        }, "The xenotype could not be set.")
                    });
                }

                options.Sort((a, b) => string.Compare(a.Label, b.Label, System.StringComparison.Ordinal));
            }, null);

            Dialog_PickFrom.Open("Choose a xenotype", options, "Search xenotypes");
        }

        private static void RemoveGene(EditorContext context, Pawn_GeneTracker genes, Gene gene, bool xeno)
        {
            UIGuard.Try("Editor.RemoveGene", () =>
            {
                GeneDef def = gene.def;

                genes.RemoveGene(gene);

                EditorParts.Redraw(context.Pawn);

                context.Changes.Record("genes", () =>
                {
                    genes.AddGene(def, xeno);

                    EditorParts.Redraw(context.Pawn);
                });
            }, "That gene could not be removed.");
        }

        private static void OfferGene(EditorContext context, Pawn_GeneTracker genes)
        {
            List<GeneChoice> options = new List<GeneChoice>();

            UIGuard.Try("Editor.GeneOptions", () =>
            {
                List<GeneDef> all = DefDatabase<GeneDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    GeneDef def = all[i];

                    if (genes.GetGene(def) != null)
                        continue;

                    GeneDef captured = def;

                    // No note about metabolism or complexity: the tile draws both along its bottom edge, which
                    // is where a player already reads them in the gene assembler.
                    options.Add(new GeneChoice
                    {
                        Def = def,
                        Chosen = () => UIGuard.Try("Editor.AddGene", () =>
                        {
                            Gene added = genes.AddGene(captured, true);

                            EditorParts.Redraw(context.Pawn);

                            context.Changes.Record("genes", () =>
                            {
                                if (added != null)
                                    genes.RemoveGene(added);

                                EditorParts.Redraw(context.Pawn);
                            });
                        }, "That gene could not be added.")
                    });
                }

                options.Sort((a, b) => string.Compare(a.Def.LabelCap, b.Def.LabelCap,
                    System.StringComparison.Ordinal));
            }, null);

            Dialog_PickGene.Open("Add a gene", options, "Search genes");
        }
    }
}
