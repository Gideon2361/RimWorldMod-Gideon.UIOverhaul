using System;
using System.Collections.Generic;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>What a template carries.</summary>
    internal enum BillTemplateKind
    {
        /// <summary>A whole bill configuration, applicable to any bench that knows the recipe.</summary>
        Bill,

        /// <summary>Only an ingredient filter, reusable across unrelated bills.</summary>
        Filter,

        /// <summary>
        /// Every bill on one bench, so an identical bench elsewhere can be set up in one action.
        ///
        /// Holds no settings of its own: everything is in <see cref="BillTemplate.Bills"/>, one entry per bill,
        /// in the order they sat on the bench. That order is their priority, so it is preserved rather than
        /// sorted.
        /// </summary>
        Bench
    }

    /// <summary>
    /// A saved bill configuration, stored globally and outside any save.
    ///
    /// <b>Nothing here is a <c>Def</c> or a <c>Thing</c>, and that is the point.</b> A template outlives the colony
    /// that made it and is read in a game running a different mod list, so everything is held as a name to be
    /// resolved at the moment it is applied. Holding a resolved reference would either fail to load or, worse,
    /// quietly bind to whatever now answers to that name.
    ///
    /// <b>Three tiers, and which tier a value falls in decides what applying it has to check.</b>
    ///
    /// <list type="number">
    /// <item><b>Portable.</b> Plain numbers and flags that mean the same thing in any colony: the counts, the pause
    /// thresholds, the radius, the hit point and quality ranges, the skill range, and whether the work is limited to
    /// slaves or mechs. These always apply.</item>
    /// <item><b>Resolved by name.</b> The recipe and every def in the ingredient filter. Each is looked up when the
    /// template is applied. A missing recipe makes the whole template unusable; a missing filter def is skipped on
    /// its own and named, while everything else still applies.</item>
    /// <item><b>Colony bound.</b> A worker restriction naming a particular pawn, and a store zone naming a
    /// particular stockpile. Neither exists in another colony. The pawn is never carried across at all, and the
    /// stockpile is matched by name only if one here happens to have it.</item>
    /// </list>
    ///
    /// <b>The worker restriction splits across two tiers,</b> which is easy to miss: restricting to a named pawn is
    /// colony bound, but restricting to slaves or to mechs is a plain flag and travels perfectly well. Only the
    /// pawn is dropped.
    /// </summary>
    internal sealed class BillTemplate
    {
        /// <summary>What the player calls it. Unique within the store, which enforces that on add and rename.</summary>
        internal string Name;

        internal BillTemplateKind Kind;

        /// <summary>Where it was captured from, shown as a subtitle. Free text, never resolved.</summary>
        internal string Origin;

        /// <summary>When it was captured, as a plain sortable stamp. Free text, never parsed for meaning.</summary>
        internal string Saved;

        // ---- tier two: resolved by name when applied ----

        /// <summary>The recipe's defName, or null for a filter or bench template.</summary>
        internal string Recipe;

        /// <summary>
        /// The bench this was captured from, as a <c>ThingDef</c> defName. Only set for a bench template.
        ///
        /// <b>Recorded so applying one somewhere else can say whether the bench matches,</b> which is what Aaron
        /// asked the feature for: importing a bench's bills onto an <i>identical</i> bench. A different bench is
        /// not refused, because a stonecutter's table and an electric one share most of their recipes and
        /// refusing would be more annoying than useful; it is warned about, and each bill that the target cannot
        /// make is skipped and named, which is the same rule the rest of this class already follows.
        /// </summary>
        internal string BenchDef;

        /// <summary>
        /// The bills on the bench, for a bench template. Empty for every other kind.
        ///
        /// <b>Nested templates rather than a second class,</b> so every rule about what travels and what does not
        /// is written once and applies to a bill whether it was saved on its own or as part of a bench.
        /// </summary>
        internal List<BillTemplate> Bills = new List<BillTemplate>();

        /// <summary>The <c>ThingDef</c> names the ingredient filter allows.</summary>
        internal List<string> Allowed = new List<string>();

        /// <summary>
        /// The repeat mode's defName.
        ///
        /// Resolved rather than assumed even though the three vanilla modes always exist, since a mod can add one.
        /// Unlike a filter def this cannot simply be skipped, because a bill must have some repeat mode, so a name
        /// that does not resolve falls back rather than dropping the template.
        /// </summary>
        internal string RepeatMode;

        /// <summary>The store mode's defName, with the same fallback reasoning as <see cref="RepeatMode"/>.</summary>
        internal string StoreMode;

        // ---- tier three: colony bound, kept for the report but never applied blindly ----

        /// <summary>
        /// The stockpile the original bill stored into, by name.
        ///
        /// Applied only if a stockpile of that name exists in the colony being applied to. Otherwise the store mode
        /// falls back and the substitution is reported rather than made silently.
        /// </summary>
        internal string StoreZone;

        /// <summary>
        /// The pawn the original bill was restricted to, by name, for the report only.
        ///
        /// <b>Never applied.</b> A pawn cannot exist in another colony, so this is here so the window can say what
        /// it is dropping instead of dropping it silently.
        /// </summary>
        internal string WorkerName;

        // ---- tier one: portable ----

        internal int RepeatCount = 1;
        internal int TargetCount = 10;
        internal bool PauseWhenSatisfied;
        internal int UnpauseWhenYouHave = 5;
        internal float SearchRadius = 999f;
        internal bool IncludeEquipped;
        internal bool IncludeTainted;
        internal bool LimitToAllowedStuff;
        internal bool SlavesOnly;
        internal bool MechsOnly;

        /// <summary>
        /// The fourth restriction mode, and the one easiest to forget.
        ///
        /// Vanilla offers any pawn, one named pawn, slaves only, mechs only and non&#8209;mechs only. Every one of
        /// those except the named pawn is a plain flag that means the same thing in any colony.
        /// </summary>
        internal bool NonMechsOnly;
        internal float HpMin;
        internal float HpMax = 1f;

        /// <summary>Quality bounds as <c>QualityCategory</c> names. An enum, so portable, unlike a def.</summary>
        internal string QualityMin = "Awful";

        internal string QualityMax = "Legendary";

        internal int SkillMin;
        internal int SkillMax = 20;

        /// <summary>A copy, so editing or renaming one never disturbs the stored original.</summary>
        internal BillTemplate Copy()
        {
            BillTemplate copy = (BillTemplate) MemberwiseClone();

            copy.Allowed = new List<string>(Allowed);

            // Deep, not shared. A bench template's children are edited and renamed through the copy the window
            // holds, and a shallow list would write those edits straight back into the stored original.
            copy.Bills = new List<BillTemplate>(Bills.Count);

            foreach (BillTemplate child in Bills)
                copy.Bills.Add(child?.Copy());

            return copy;
        }

        /// <summary>
        /// A one line summary for the list, built from whichever fields the kind actually uses.
        /// </summary>
        internal string Summary()
        {
            if (Kind == BillTemplateKind.Filter)
                return "Ingredient filter, " + Allowed.Count + (Allowed.Count == 1 ? " def" : " defs");

            List<string> parts = new List<string>();

            if (RepeatMode != null && RepeatMode.EndsWith("TargetCount", StringComparison.Ordinal))
                parts.Add("Until you have " + TargetCount);
            else if (RepeatMode != null && RepeatMode.EndsWith("Forever", StringComparison.Ordinal))
                parts.Add("Forever");
            else
                parts.Add("Do " + RepeatCount + "x");

            if (Allowed.Count > 0)
                parts.Add(Allowed.Count + (Allowed.Count == 1 ? " ingredient" : " ingredients"));

            if (SkillMin > 0)
                parts.Add("skill " + SkillMin + "+");

            return string.Join(" - ", parts.ToArray());
        }
    }
}
