using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// Everything that changes what a pawn looks like, on one panel, with the pawn standing next to it.
    ///
    /// <b>One panel and not four, because body, hair, tattoos, genes and clothing are five systems in RimWorld's
    /// data and one question for a player: what does this person look like.</b> They are judged against each
    /// other rather than on their own -- a hair colour is right or wrong against the hat above it, a skin tone
    /// reads differently under a duster than a t-shirt -- so splitting them across panels means picking each one
    /// against a memory of the other three. And the render is the expensive part; four panels needing it would be
    /// four copies of the same column, three of which would drift.
    ///
    /// <b>Worn apparel is here and the weapon is not.</b> Clothing is how somebody looks and the layer stack
    /// belongs beside the render that shows it. Equipment keeps the weapon and the carried inventory: it is a
    /// panel about what somebody owns rather than what they look like. This is the one part of the consolidation
    /// I am least sure of and it is easy to move.
    ///
    /// <b>The xenotype picker is not here.</b> The proposal put one on this panel labelled "cosmetic only", but
    /// setting a xenotype replaces every gene the pawn has, which is neither cosmetic nor reversible. It lives on
    /// the Genes panel where that consequence is in context; what is here is the genes that only change the look.
    /// </summary>
    internal static class EditorAppearance
    {
        internal static float Draw(Rect view, EditorContext context)
        {
            Pawn pawn = context.Pawn;
            UIColorPaletteDef palette = context.Palette;

            if (!context.Humanlike)
                return Animal(view, context);

            if (pawn.story == null)
                return EditorParts.Note(view, view.y, "This pawn has no appearance to edit.", palette) - view.y;

            float y = Body(view, view.y, context, palette);

            y = Style(view, y, context, palette);

            y = Genes(view, y, context, palette);

            return Wearing(view, y, context, palette) - view.y;
        }

        /// <summary>
        /// An animal's appearance is its species and its life stage, neither of which is a choice.
        ///
        /// The panel still exists for them, because the render does: seeing the animal is the point, and the
        /// controls that would change it are on Identity.
        /// </summary>
        private static float Animal(Rect view, EditorContext context)
        {
            UIColorPaletteDef palette = context.Palette;

            float y = EditorParts.Heading(view, view.y, "Appearance", palette);

            string kind = UIGuard.Try<string>("Editor.AnimalKind",
                () => context.Pawn.def.LabelCap + ", " + context.Pawn.ageTracker.CurLifeStage.LabelCap, null,
                null);

            y = EditorParts.Note(view, y,
                kind + ". An animal's look is its species and its life stage; its age and sex are on Identity.",
                palette);

            return y - view.y;
        }

        // ---------------------------------------------------------------------------------------
        // Body
        // ---------------------------------------------------------------------------------------

        private static float Body(Rect view, float y, EditorContext context, UIColorPaletteDef palette)
        {
            Pawn pawn = context.Pawn;

            y = EditorParts.Heading(view, y, "Body", palette);

            Rect row = new Rect(view.x, y, view.width, EditorParts.FieldHeight);

            BodyType(EditorParts.Column(row, 0, 3), context, palette);
            HeadType(EditorParts.Column(row, 1, 3), context, palette);
            Skin(EditorParts.Column(row, 2, 3), context, palette);

            y = row.yMax + 4f;

            if (UIGuard.Try("Editor.SkinOverridden", () => pawn.story.SkinColorOverriden, false, null))
                y = EditorParts.Note(view, y,
                    "A gene is overriding this pawn's skin colour, so the swatch above will not show on them "
                    + "until that gene is removed.", palette, palette.Warning);

            return y + EditorParts.BlockGap;
        }

        private static void BodyType(Rect cell, EditorContext context, UIColorPaletteDef palette)
        {
            Pawn pawn = context.Pawn;

            if (!EditorParts.Picker(cell, "body type", EditorParts.LabelOf(pawn.story.bodyType), palette))
                return;

            List<EditorOption> options = new List<EditorOption>();

            UIGuard.Try("Editor.BodyTypes", () =>
            {
                List<BodyTypeDef> all = DefDatabase<BodyTypeDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    BodyTypeDef def = all[i];
                    BodyTypeDef captured = def;

                    options.Add(new EditorOption
                    {
                        Label = EditorParts.LabelOf(def),
                        Current = def == pawn.story.bodyType,
                        Chosen = () => context.Changes.Set("body type", () => pawn.story.bodyType,
                            value =>
                            {
                                pawn.story.bodyType = value;

                                EditorParts.Redraw(pawn);
                            }, captured)
                    });
                }
            }, null);

            Dialog_PickFrom.Open("Choose a body type", options, "Search body types");
        }

        private static void HeadType(Rect cell, EditorContext context, UIColorPaletteDef palette)
        {
            Pawn pawn = context.Pawn;

            if (!EditorParts.Picker(cell, "head", EditorParts.LabelOf(pawn.story.headType), palette))
                return;

            List<EditorOption> options = new List<EditorOption>();

            UIGuard.Try("Editor.HeadTypes", () =>
            {
                List<HeadTypeDef> all = DefDatabase<HeadTypeDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    HeadTypeDef def = all[i];
                    HeadTypeDef captured = def;

                    options.Add(new EditorOption
                    {
                        Label = EditorParts.LabelOf(def),
                        Note = def.gender == Gender.None ? null : def.gender.GetLabel(),
                        Current = def == pawn.story.headType,
                        Marked = Fit(def, pawn),
                        Chosen = () => context.Changes.Set("head", () => pawn.story.headType,
                            value =>
                            {
                                pawn.story.headType = value;

                                EditorParts.Redraw(pawn);
                            }, captured)
                    });
                }

                options.Sort((a, b) => string.Compare(a.Label, b.Label, System.StringComparison.Ordinal));
            }, null);

            Dialog_PickFrom.Open("Choose a head", options, "Search heads");
        }

        /// <summary>What is wrong with a head on this pawn, or null. Marked and takeable, never hidden.</summary>
        private static string Fit(HeadTypeDef def, Pawn pawn)
        {
            return UIGuard.Try<string>("Editor.HeadFit", () =>
            {
                if (def.gender != Gender.None && def.gender != pawn.gender)
                    return "for the other sex";

                if (def.requiredGenes.NullOrEmpty())
                    return null;

                if (pawn.genes == null)
                    return "needs a gene they cannot have";

                for (int i = 0; i < def.requiredGenes.Count; i++)
                {
                    if (pawn.genes.HasActiveGene(def.requiredGenes[i]))
                        return null;
                }

                return "needs " + EditorParts.LabelOf(def.requiredGenes[0]);
            }, null, null);
        }

        /// <summary>
        /// Skin tones, from the genes that define them.
        ///
        /// <b>The game's own list rather than a palette of ours.</b> Skin colour moved into genes, so
        /// <c>PawnSkinColors.SkinColorGenesInOrder</c> is the canonical ordered set and it stays right when a mod
        /// adds one. Inventing five hex values would have been five colours no pawn in the game actually has.
        /// </summary>
        private static void Skin(Rect cell, EditorContext context, UIColorPaletteDef palette)
        {
            Pawn pawn = context.Pawn;

            List<Color> tones = new List<Color>();

            UIGuard.Try("Editor.SkinTones", () =>
            {
                List<GeneDef> genes = PawnSkinColors.SkinColorGenesInOrder;

                for (int i = 0; genes != null && i < genes.Count; i++)
                {
                    if (genes[i] != null && genes[i].skinColorBase.HasValue)
                        tones.Add(genes[i].skinColorBase.Value);
                }
            }, null);

            Color current = UIGuard.Try("Editor.SkinNow", () => pawn.story.SkinColorBase, Color.white, null);

            Color? chosen = EditorParts.Swatches(cell, "skin", tones, current, palette);

            if (!chosen.HasValue)
                return;

            context.Changes.Set("skin", () => pawn.story.SkinColorBase,
                value =>
                {
                    pawn.story.SkinColorBase = value;

                    EditorParts.Redraw(pawn);
                }, chosen.Value);
        }

        // ---------------------------------------------------------------------------------------
        // Hair, beard, tattoos
        // ---------------------------------------------------------------------------------------

        private static float Style(Rect view, float y, EditorContext context, UIColorPaletteDef palette)
        {
            Pawn pawn = context.Pawn;

            y = EditorParts.Heading(view, y, "Hair and tattoos", palette);

            Rect row = new Rect(view.x, y, view.width, EditorParts.FieldHeight);

            Hair(EditorParts.Column(row, 0, 3), context, palette);
            HairColor(EditorParts.Column(row, 1, 3), context, palette);
            Beard(EditorParts.Column(row, 2, 3), context, palette);

            y = row.yMax + EditorParts.RowGap;

            row = new Rect(view.x, y, view.width, EditorParts.FieldHeight);

            Tattoo(EditorParts.Column(row, 0, 2), "face tattoo", TattooType.Face, context, palette);
            Tattoo(EditorParts.Column(row, 1, 2), "body tattoo", TattooType.Body, context, palette);

            y = row.yMax;

            if (pawn.style == null)
                y = EditorParts.Note(view, y + 4f,
                    "This pawn has no style tracker, so beards and tattoos cannot be set on them.", palette,
                    palette.Warning);

            return y + EditorParts.BlockGap;
        }

        private static void Hair(Rect cell, EditorContext context, UIColorPaletteDef palette)
        {
            Pawn pawn = context.Pawn;

            if (!EditorParts.Picker(cell, "hair", EditorParts.LabelOf(pawn.story.hairDef), palette))
                return;

            List<EditorOption> options = new List<EditorOption>();

            UIGuard.Try("Editor.Hairs", () =>
            {
                List<HairDef> all = DefDatabase<HairDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    HairDef def = all[i];
                    HairDef captured = def;

                    options.Add(new EditorOption
                    {
                        Label = EditorParts.LabelOf(def),
                        Note = Gendered(def.styleGender),

                        // <b>Drawn in the pawn's own hair colour,</b> not white. A hairstyle is a silhouette, and
                        // two hundred silhouettes all in the same flat white is a page of blobs -- the colour is
                        // what makes one of them look like this pawn's hair rather than like a shape.
                        Icon = def,
                        IconColor = HairColorOf(pawn),

                        Current = def == pawn.story.hairDef,
                        Chosen = () => context.Changes.Set("hair", () => pawn.story.hairDef,
                            value =>
                            {
                                pawn.story.hairDef = value;

                                EditorParts.Redraw(pawn);
                            }, captured)
                    });
                }

                options.Sort((a, b) => string.Compare(a.Label, b.Label, System.StringComparison.Ordinal));
            }, null);

            Dialog_PickFrom.Open("Choose hair", options, "Search hair");
        }

        /// <summary>
        /// The colour to draw a hair or beard preview in.
        ///
        /// <b>Read off the pawn rather than taken from the def,</b> because a hairstyle has no colour of its own:
        /// the graphic is a mask and the colour comes from whoever is wearing it. Falls back to white so a pawn
        /// with no story -- which the editor can be pointed at mid-construction -- still draws something visible
        /// rather than nothing at all.
        /// </summary>
        private static Color HairColorOf(Pawn pawn)
        {
            return UIGuard.Try("Editor.HairColor", () => pawn?.story?.HairColor ?? Color.white, Color.white,
                null);
        }

        private static string Gendered(StyleGender gender)
        {
            switch (gender)
            {
                case StyleGender.Male:
                case StyleGender.MaleUsually:
                    return "usually male";

                case StyleGender.Female:
                case StyleGender.FemaleUsually:
                    return "usually female";

                default:
                    return null;
            }
        }

        /// <summary>
        /// Hair colour, from every colour the styling station offers.
        ///
        /// <b>It used to be six swatches drawn in the cell.</b> They were <c>PawnHairColors</c>' named statics,
        /// which is all that class exposes -- and the game's real palette is the <c>ColorDef</c>s the styling
        /// station reads, which is far more than six. A row showing the first six of forty looks like the whole
        /// choice, so it was worse than showing none. Replaced with a picker on 2026-08-25; see
        /// <see cref="Dialog_PickColor"/>.
        ///
        /// A gene that overrides hair colour still wins over anything set here, which the note under the block
        /// says.
        /// </summary>
        private static void HairColor(Rect cell, EditorContext context, UIColorPaletteDef palette)
        {
            Pawn pawn = context.Pawn;

            Color current = UIGuard.Try("Editor.HairColorNow", () => pawn.story.HairColor, Color.white, null);

            if (!EditorParts.ColorButton(cell, "hair colour", current, palette))
                return;

            Dialog_PickColor.Open("Hair colour", current, picked => Apply(context, picked));
        }

        /// <summary>Records a hair colour through the editor's own undo, as every other change goes.</summary>
        private static void Apply(EditorContext context, Color chosen)
        {
            Pawn pawn = context.Pawn;

            context.Changes.Set("hair colour", () => pawn.story.HairColor,
                value =>
                {
                    pawn.story.HairColor = value;

                    EditorParts.Redraw(pawn);
                }, chosen);
        }

        private static void Beard(Rect cell, EditorContext context, UIColorPaletteDef palette)
        {
            Pawn pawn = context.Pawn;

            if (pawn.style == null)
            {
                EditorParts.Picker(cell, "beard", "none", palette, null, false);

                return;
            }

            if (!EditorParts.Picker(cell, "beard", EditorParts.LabelOf(pawn.style.beardDef), palette))
                return;

            List<EditorOption> options = new List<EditorOption>();

            UIGuard.Try("Editor.Beards", () =>
            {
                List<BeardDef> all = DefDatabase<BeardDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    BeardDef def = all[i];
                    BeardDef captured = def;

                    options.Add(new EditorOption
                    {
                        Label = EditorParts.LabelOf(def),
                        Note = Gendered(def.styleGender),
                        Icon = def,
                        IconColor = HairColorOf(pawn),
                        Current = def == pawn.style.beardDef,
                        Chosen = () => context.Changes.Set("beard", () => pawn.style.beardDef,
                            value =>
                            {
                                pawn.style.beardDef = value;

                                EditorParts.Redraw(pawn);
                            }, captured)
                    });
                }

                options.Sort((a, b) => string.Compare(a.Label, b.Label, System.StringComparison.Ordinal));
            }, null);

            Dialog_PickFrom.Open("Choose a beard", options, "Search beards");
        }

        private static void Tattoo(Rect cell, string caption, TattooType type, EditorContext context,
            UIColorPaletteDef palette)
        {
            Pawn pawn = context.Pawn;

            if (pawn.style == null)
            {
                EditorParts.Picker(cell, caption, "none", palette, null, false);

                return;
            }

            TattooDef current = UIGuard.Try("Editor.TattooNow",
                () => type == TattooType.Face ? pawn.style.FaceTattoo : pawn.style.BodyTattoo, null, null);

            if (!EditorParts.Picker(cell, caption, EditorParts.LabelOf(current), palette))
                return;

            List<EditorOption> options = new List<EditorOption>();

            UIGuard.Try("Editor.Tattoos", () =>
            {
                List<TattooDef> all = DefDatabase<TattooDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    TattooDef def = all[i];

                    if (def.tattooType != type)
                        continue;

                    TattooDef captured = def;

                    options.Add(new EditorOption
                    {
                        Label = EditorParts.LabelOf(def),
                        Current = def == current,
                        Chosen = () => context.Changes.Set(caption,
                            () => type == TattooType.Face ? pawn.style.FaceTattoo : pawn.style.BodyTattoo,
                            value =>
                            {
                                if (type == TattooType.Face)
                                    pawn.style.FaceTattoo = value;
                                else
                                    pawn.style.BodyTattoo = value;

                                EditorParts.Redraw(pawn);
                            }, captured)
                    });
                }

                options.Sort((a, b) => string.Compare(a.Label, b.Label, System.StringComparison.Ordinal));
            }, null);

            Dialog_PickFrom.Open("Choose a " + caption, options, "Search tattoos");
        }

        // ---------------------------------------------------------------------------------------
        // Appearance genes
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The genes that only change how somebody looks.
        ///
        /// <b>The split is by what a gene does, which is the honest cut.</b> A gene that changes skin, hair, a
        /// body type or adds a furry coat is edited here beside the thing it changes. The full list, with
        /// metabolism and complexity and abilities, stays on its own panel -- because Fire immunity filed under a
        /// hair picker would be filed by its side effect.
        ///
        /// <b>The predicate is ours, since the game has none.</b> Costs nothing on all three biostats, and
        /// changes at least one visible thing. A gene that fails either test is on the Genes panel only.
        /// </summary>
        private static float Genes(Rect view, float y, EditorContext context, UIColorPaletteDef palette)
        {
            Pawn pawn = context.Pawn;

            if (!ModsConfig.BiotechActive || pawn.genes == null)
                return y;

            y = EditorParts.Heading(view, y, "Appearance genes", palette, "look only");

            List<Gene> held = new List<Gene>();

            UIGuard.Try("Editor.LookGenes", () =>
            {
                List<Gene> all = pawn.genes.GenesListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] != null && Cosmetic(all[i].def))
                        held.Add(all[i]);
                }
            }, null);

            // The same tiles the Genes panel uses, for the same reason and so the two agree: a gene the player
            // recognises on one panel has to be the same picture on the other.
            y = EditorGeneTiles.Draw(view, y, held, pawn, palette, gene => Drop(context, gene));

            y += EditorParts.RowGap;

            if (EditorParts.Add(view, y, "Add an appearance gene", palette))
                Offer(context);

            return y + EditorParts.ControlHeight + EditorParts.BlockGap;
        }

        private static bool Cosmetic(GeneDef def)
        {
            if (def == null)
                return false;

            if (def.biostatMet != 0 || def.biostatCpx != 0 || def.biostatArc != 0)
                return false;

            return def.hairColorOverride.HasValue || def.skinColorBase.HasValue || def.skinColorOverride.HasValue
                   || def.bodyType.HasValue || !def.forcedHeadTypes.NullOrEmpty() || def.fur != null
                   || def.HasDefinedGraphicProperties;
        }

        private static void Drop(EditorContext context, Gene gene)
        {
            Pawn pawn = context.Pawn;

            UIGuard.Try("Editor.DropLookGene", () =>
            {
                GeneDef def = gene.def;
                bool xeno = pawn.genes.IsXenogene(gene);

                pawn.genes.RemoveGene(gene);

                EditorParts.Redraw(pawn);

                context.Changes.Record("appearance genes", () =>
                {
                    pawn.genes.AddGene(def, xeno);

                    EditorParts.Redraw(pawn);
                });
            }, "That gene could not be removed.");
        }

        private static void Offer(EditorContext context)
        {
            Pawn pawn = context.Pawn;

            List<GeneChoice> options = new List<GeneChoice>();

            UIGuard.Try("Editor.LookGeneOptions", () =>
            {
                List<GeneDef> all = DefDatabase<GeneDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    GeneDef def = all[i];

                    if (!Cosmetic(def) || pawn.genes.GetGene(def) != null)
                        continue;

                    GeneDef captured = def;

                    options.Add(new GeneChoice
                    {
                        Def = def,
                        Chosen = () => UIGuard.Try("Editor.AddLookGene", () =>
                        {
                            Gene added = pawn.genes.AddGene(captured, true);

                            EditorParts.Redraw(pawn);

                            context.Changes.Record("appearance genes", () =>
                            {
                                if (added != null)
                                    pawn.genes.RemoveGene(added);

                                EditorParts.Redraw(pawn);
                            });
                        }, "That gene could not be added.")
                    });
                }

                options.Sort((a, b) => string.Compare(a.Def.LabelCap, b.Def.LabelCap,
                    System.StringComparison.Ordinal));
            }, null);

            Dialog_PickGene.Open("Add an appearance gene", options, "Search appearance genes");
        }

        // ---------------------------------------------------------------------------------------
        // Apparel
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// What they are wearing, layer by layer.
        ///
        /// <b>Nothing is spawned into the world and nothing is destroyed while the window is open.</b> Removing a
        /// coat takes it out of the pawn's container and keeps the object alive in the undo entry, so Revert can
        /// put the same coat back with its own quality and hit points rather than a fresh copy. An item left
        /// removed belongs to nothing when the window closes, which means it is not written to the save and is
        /// gone on the next load -- the same end state as destroying it, reached without making Revert a lie.
        /// </summary>
        private static float Wearing(Rect view, float y, EditorContext context, UIColorPaletteDef palette)
        {
            Pawn pawn = context.Pawn;

            if (pawn.apparel == null)
                return y;

            y = EditorParts.Heading(view, y, "Wearing", palette, Layers(pawn));

            List<Apparel> worn = new List<Apparel>(pawn.apparel.WornApparel);

            for (int i = 0; i < worn.Count; i++)
            {
                Apparel ap = worn[i];

                if (ap == null)
                    continue;

                Rect row;

                if (EditorParts.Row(view, y, Slot(ap) + "  " + ap.LabelCapNoCount, Made(ap),
                        palette.TextSecondary, palette, out row, EditorParts.DescriptionOf(ap.def), true,
                        ap.def, ap.DrawColor))
                    Strip(context, ap);

                y = row.yMax + 4f;
            }

            if (worn.Count == 0)
                y = EditorParts.Note(view, y, "Nothing.", palette);

            y += EditorParts.RowGap;

            if (EditorParts.Add(view, y, "Put something on", palette))
                Dialog_AddApparel.Open(context);

            return y + EditorParts.ControlHeight + EditorParts.BlockGap;
        }

        /// <summary>How many of the game's apparel layers this pawn has something on.</summary>
        private static string Layers(Pawn pawn)
        {
            return UIGuard.Try<string>("Editor.Layers", () =>
            {
                HashSet<ApparelLayerDef> used = new HashSet<ApparelLayerDef>();

                List<Apparel> worn = pawn.apparel.WornApparel;

                for (int i = 0; i < worn.Count; i++)
                {
                    List<ApparelLayerDef> layers = worn[i].def.apparel.layers;

                    for (int l = 0; layers != null && l < layers.Count; l++)
                        used.Add(layers[l]);
                }

                return used.Count + " of " + DefDatabase<ApparelLayerDef>.DefCount + " layers used";
            }, null, null);
        }

        private static string Slot(Apparel ap)
        {
            return UIGuard.Try<string>("Editor.Slot", () =>
            {
                List<ApparelLayerDef> layers = ap.def.apparel.layers;

                if (layers == null || layers.Count == 0)
                    return "-";

                return layers[0].label.NullOrEmpty() ? layers[0].defName : layers[0].label;
            }, "-", null);
        }

        private static string Made(Apparel ap)
        {
            return UIGuard.Try<string>("Editor.Made", () =>
            {
                QualityCategory quality;

                string made = ap.TryGetQuality(out quality) ? quality.GetLabel() : null;

                string stuff = ap.Stuff != null ? ap.Stuff.LabelAsStuff : null;

                if (made.NullOrEmpty())
                    return stuff;

                return stuff.NullOrEmpty() ? made : made + ", " + stuff;
            }, null, null);
        }

        private static void Strip(EditorContext context, Apparel ap)
        {
            Pawn pawn = context.Pawn;

            UIGuard.Try("Editor.Strip", () =>
            {
                pawn.apparel.Remove(ap);

                EditorParts.Redraw(pawn);

                context.Changes.Record("apparel", () =>
                {
                    pawn.apparel.Wear(ap, false);

                    EditorParts.Redraw(pawn);
                });
            }, "That could not be taken off.");
        }
    }
}
