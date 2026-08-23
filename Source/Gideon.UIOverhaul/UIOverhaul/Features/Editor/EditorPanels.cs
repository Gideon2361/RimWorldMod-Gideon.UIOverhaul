using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// Which panel of the editor is open.
    ///
    /// <b>The order is the rail, and the split in it is not decoration.</b> Identity through Genes are the pawn as
    /// a character -- the things a storyteller decided once. Health through Relationships are state: what they are
    /// carrying, what hurts, what they are thinking about this hour. They are edited for completely different
    /// reasons and they carry completely different risks, which is why they are not one list of eleven.
    /// </summary>
    internal enum EditorPanel
    {
        /// <summary>Only on a dead pawn, and first, because it is why the window was opened.</summary>
        Resurrect,

        Identity,

        Appearance,

        Backstory,

        Traits,

        Skills,

        Genes,

        Health,

        Needs,

        Thoughts,

        Equipment,

        Relationships
    }

    /// <summary>Everything a panel needs: who it is editing, where to log what it did, and the palette.</summary>
    internal sealed class EditorContext
    {
        internal Pawn Pawn;

        internal EditorChanges Changes;

        internal UIColorPaletteDef Palette;

        /// <summary>True while editing a corpse's occupant, which closes several panels and opens one.</summary>
        internal bool Dead
        {
            get { return UIGuard.Try("Editor.Dead", () => Pawn != null && Pawn.Dead, false, null); }
        }

        internal bool Humanlike
        {
            get
            {
                return UIGuard.Try("Editor.Humanlike",
                    () => Pawn != null && Pawn.RaceProps != null && Pawn.RaceProps.Humanlike, false, null);
            }
        }
    }

    /// <summary>
    /// The rail: which panels exist for a given pawn, what each is called, and which of them move the render.
    ///
    /// <b>Separate from the window because three things ask.</b> The rail draws it, the window uses it to pick a
    /// panel to fall back to when the current one stops applying, and the panels themselves use the render flag to
    /// decide whether they have the full width or the narrow one.
    /// </summary>
    internal static class EditorPanels
    {
        internal static readonly EditorPanel[] All =
        {
            EditorPanel.Resurrect, EditorPanel.Identity, EditorPanel.Appearance, EditorPanel.Backstory,
            EditorPanel.Traits, EditorPanel.Skills, EditorPanel.Genes, EditorPanel.Health, EditorPanel.Needs,
            EditorPanel.Thoughts, EditorPanel.Equipment, EditorPanel.Relationships
        };

        internal static string LabelOf(EditorPanel panel)
        {
            switch (panel)
            {
                case EditorPanel.Resurrect: return "Resurrect";
                case EditorPanel.Identity: return "Identity";
                case EditorPanel.Appearance: return "Appearance";
                case EditorPanel.Backstory: return "Backstory";
                case EditorPanel.Traits: return "Traits";
                case EditorPanel.Skills: return "Skills";
                case EditorPanel.Genes: return "Genes";
                case EditorPanel.Health: return "Health";
                case EditorPanel.Needs: return "Needs";
                case EditorPanel.Thoughts: return "Thoughts";
                case EditorPanel.Equipment: return "Equipment";
                default: return "Relationships";
            }
        }

        /// <summary>
        /// Which group heading this panel sits under, or null for one that stands alone.
        ///
        /// The wording changes on a dead pawn: "who they were" rather than "who they are", which is the one place
        /// in this window where tense is a fact rather than a flourish.
        /// </summary>
        internal static string GroupOf(EditorPanel panel, bool dead)
        {
            switch (panel)
            {
                case EditorPanel.Resurrect:
                    return "Dead";

                case EditorPanel.Identity:
                    return dead ? "Who they were" : "Who they are";

                case EditorPanel.Health:
                    return "What is true today";

                default:
                    return null;
            }
        }

        /// <summary>Whether this panel changes what the pawn looks like, and so gets the render column.</summary>
        internal static bool NeedsRender(EditorPanel panel)
        {
            // Identity carries it as well as Appearance and Equipment, which is one more than the proposal's rail
            // drew. Gender and age move the body type, so a panel that can change either has to show the result;
            // the alternative is picking a gender and going to another panel to see what it did.
            return panel == EditorPanel.Appearance || panel == EditorPanel.Equipment
                                                   || panel == EditorPanel.Identity
                                                   || panel == EditorPanel.Resurrect;
        }

        /// <summary>
        /// Whether this panel applies to this pawn at all.
        ///
        /// <b>Absent rather than empty.</b> A dead pawn has no needs to set and no mood to think with; an animal
        /// has no backstory, no traits and no skills. A rail entry that opens onto "nothing here" teaches the eye
        /// to distrust the rail.
        /// </summary>
        internal static bool Applies(EditorPanel panel, EditorContext context)
        {
            if (context == null || context.Pawn == null)
                return false;

            bool dead = context.Dead;
            bool humanlike = context.Humanlike;

            switch (panel)
            {
                case EditorPanel.Resurrect:
                    return dead;

                // The three that are a person rather than a creature.
                case EditorPanel.Backstory:
                case EditorPanel.Traits:
                case EditorPanel.Skills:
                    return humanlike;

                case EditorPanel.Genes:
                    return humanlike && ModsConfig.BiotechActive
                                     && UIGuard.Try("Editor.HasGenes", () => context.Pawn.genes != null, false,
                                         null);

                // State a corpse does not have. A dead pawn keeps its needs tracker but every level on it is
                // frozen and meaningless, and mood is gone entirely.
                case EditorPanel.Needs:
                    return !dead && UIGuard.Try("Editor.HasNeeds", () => context.Pawn.needs != null, false, null);

                case EditorPanel.Thoughts:
                    return !dead && UIGuard.Try("Editor.HasMood",
                        () => context.Pawn.needs != null && context.Pawn.needs.mood != null, false, null);

                default:
                    return true;
            }
        }

        /// <summary>The first panel that applies, which is where the window opens.</summary>
        internal static EditorPanel FirstFor(EditorContext context)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (Applies(All[i], context))
                    return All[i];
            }

            return EditorPanel.Identity;
        }

        /// <summary>
        /// The count beside a rail entry, or null when a count would say nothing.
        ///
        /// Only where the number is the reason to go there: how many traits, how many hediffs, how many memories.
        /// Skills has no count worth showing because there are always twelve.
        /// </summary>
        internal static string CountOf(EditorPanel panel, EditorContext context)
        {
            return UIGuard.Try<string>("Editor.RailCount", () =>
            {
                Pawn pawn = context.Pawn;

                switch (panel)
                {
                    case EditorPanel.Traits:
                        return pawn.story != null && pawn.story.traits != null
                            ? Some(pawn.story.traits.allTraits.Count)
                            : null;

                    case EditorPanel.Genes:
                        return pawn.genes != null ? Some(pawn.genes.GenesListForReading.Count) : null;

                    case EditorPanel.Health:
                        return pawn.health != null && pawn.health.hediffSet != null
                            ? Some(pawn.health.hediffSet.hediffs.Count)
                            : null;

                    case EditorPanel.Thoughts:
                        return pawn.needs != null && pawn.needs.mood != null
                            ? Some(pawn.needs.mood.thoughts.memories.Memories.Count)
                            : null;

                    case EditorPanel.Relationships:
                        return pawn.relations != null ? Some(pawn.relations.DirectRelations.Count) : null;

                    default:
                        return null;
                }
            }, null, null);
        }

        private static string Some(int count)
        {
            return count > 0 ? count.ToString() : null;
        }

        /// <summary>Draws the chosen panel and says how tall it came out.</summary>
        internal static float Draw(EditorPanel panel, Rect view, EditorContext context)
        {
            switch (panel)
            {
                case EditorPanel.Resurrect:
                    return EditorResurrect.Draw(view, context);

                case EditorPanel.Identity:
                    return EditorWho.Identity(view, context);

                case EditorPanel.Appearance:
                    return EditorAppearance.Draw(view, context);

                case EditorPanel.Backstory:
                    return EditorWho.Backstory(view, context);

                case EditorPanel.Traits:
                    return EditorWho.Traits(view, context);

                case EditorPanel.Skills:
                    return EditorWho.Skills(view, context);

                case EditorPanel.Genes:
                    return EditorWho.Genes(view, context);

                case EditorPanel.Health:
                    return EditorState.Health(view, context);

                case EditorPanel.Needs:
                    return EditorState.Needs(view, context);

                case EditorPanel.Thoughts:
                    return EditorState.Thoughts(view, context);

                case EditorPanel.Equipment:
                    return EditorState.Equipment(view, context);

                default:
                    return EditorState.Relationships(view, context);
            }
        }
    }
}
