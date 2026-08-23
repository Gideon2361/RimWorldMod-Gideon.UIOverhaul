using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>Which drawer of the condition picker a hediff belongs in.</summary>
    internal enum HospitalConditionGroup
    {
        /// <summary>Not a named condition at all: pain, bleeding, downed, in a bed.</summary>
        State,

        Injuries,

        Diseases,

        Chronic,

        Addictions
    }

    /// <summary>
    /// When a standing order is allowed to fire.
    ///
    /// <b>Two settings rather than a menu of canned ones.</b> Always, or any of these. An order gated on pain and
    /// a gunshot wound stops on its own when the wound heals, instead of dosing a well person twice a day until
    /// somebody notices; an order set to always is the one you want for penoxycyline and for luciferium.
    ///
    /// <b>Ticked conditions are OR, not AND,</b> which is Aaron's own refinement and is the only reading that
    /// works: a painkiller wanted for a gunshot is wanted for a burn too, and requiring both at once would
    /// describe almost nobody.
    ///
    /// <b>The state group is above the hediffs because the reason you reach for a painkiller is not a hediff.</b>
    /// Pain is a degree rather than a presence, so it carries its own threshold; bleeding, downed and lying in a
    /// medical bed are facts about the patient's situation rather than about anything named on their health tab.
    /// </summary>
    internal class HospitalConditionGate : IExposable
    {
        /// <summary>Nothing is checked: the clock alone decides.</summary>
        internal bool always = true;

        /// <summary>Pain at or above this fraction, or negative when pain is not one of the conditions.</summary>
        internal float painAbove = -1f;

        internal bool bleeding;

        internal bool downed;

        internal bool inMedicalBed;

        internal List<HediffDef> hediffs = new List<HediffDef>();

        /// <summary>How many conditions are ticked, which is what the summary chip says.</summary>
        internal int Count
        {
            get
            {
                int count = hediffs != null ? hediffs.Count : 0;

                if (painAbove >= 0f)
                    count++;

                if (bleeding)
                    count++;

                if (downed)
                    count++;

                if (inMedicalBed)
                    count++;

                return count;
            }
        }

        /// <summary>The chip on the row: "always" or "any of 3".</summary>
        internal string Summary
        {
            get
            {
                if (always)
                    return "always";

                int count = Count;

                return count == 0 ? "never" : "any of " + count;
            }
        }

        /// <summary>
        /// Whether this patient is in a state the order wants to act on right now.
        ///
        /// A gate with nothing ticked and Always switched off refuses everything, deliberately: that is a
        /// half-finished order, and firing it on the clock alone would be doing something the player did not ask
        /// for. The editor says as much beside the list.
        /// </summary>
        internal bool Allows(Pawn pawn)
        {
            if (pawn == null)
                return false;

            if (always)
                return true;

            return UIGuard.Try("Hospital.Gate", () =>
            {
                if (painAbove >= 0f && pawn.health != null && pawn.health.hediffSet != null
                    && pawn.health.hediffSet.PainTotal >= painAbove)
                    return true;

                if (bleeding && pawn.health != null && pawn.health.hediffSet != null
                    && pawn.health.hediffSet.BleedRateTotal > 0f)
                    return true;

                if (downed && pawn.Downed)
                    return true;

                if (inMedicalBed)
                {
                    Building_Bed bed = pawn.CurrentBed();

                    if (bed != null && bed.Medical)
                        return true;
                }

                if (hediffs == null || pawn.health == null || pawn.health.hediffSet == null)
                    return false;

                for (int i = 0; i < hediffs.Count; i++)
                {
                    if (hediffs[i] != null && pawn.health.hediffSet.HasHediff(hediffs[i]))
                        return true;
                }

                return false;
            }, false, null);
        }

        internal HospitalConditionGate Clone()
        {
            HospitalConditionGate copy = new HospitalConditionGate
            {
                always = always,
                painAbove = painAbove,
                bleeding = bleeding,
                downed = downed,
                inMedicalBed = inMedicalBed,
                hediffs = new List<HediffDef>()
            };

            if (hediffs != null)
                copy.hediffs.AddRange(hediffs);

            return copy;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref always, "always", true);
            Scribe_Values.Look(ref painAbove, "painAbove", -1f);
            Scribe_Values.Look(ref bleeding, "bleeding");
            Scribe_Values.Look(ref downed, "downed");
            Scribe_Values.Look(ref inMedicalBed, "inMedicalBed");
            Scribe_Collections.Look(ref hediffs, "hediffs", LookMode.Def);

            if (Scribe.mode != LoadSaveMode.PostLoadInit)
                return;

            // A list saved while empty comes back null, and a null list here would throw rather than read as no
            // conditions ticked. Nulls inside it are hediffs from a mod that has since been removed.
            if (hediffs == null)
                hediffs = new List<HediffDef>();
            else
                hediffs.RemoveAll(def => def == null);
        }
    }

    /// <summary>
    /// The catalogue the condition picker lists, and the live counts beside it.
    ///
    /// <b>Grouped the way a player thinks about them rather than the way the defs are declared.</b> Injuries,
    /// diseases, chronic conditions and addictions are four different reasons to give somebody a drug, and the
    /// hediff database has no field that says which of the four anything is. So each group is a test against what
    /// the def actually does, and anything that answers to none of them is left out: a list containing every
    /// hediff in the game would contain four hundred rows nobody would ever tick.
    /// </summary>
    internal static class HospitalConditions
    {
        private static readonly List<HediffDef> Cached = new List<HediffDef>();

        private static bool built;

        /// <summary>Every condition worth offering, in group order then alphabetically.</summary>
        internal static List<HediffDef> Catalogue
        {
            get
            {
                if (built)
                    return Cached;

                built = true;

                UIGuard.Try("Hospital.Catalogue", Build,
                    "The list of conditions could not be built, so a standing order can only be set to always.");

                return Cached;
            }
        }

        private static void Build()
        {
            List<HediffDef> all = DefDatabase<HediffDef>.AllDefsListForReading;

            for (int i = 0; i < all.Count; i++)
            {
                HediffDef def = all[i];

                if (def == null || def.label.NullOrEmpty())
                    continue;

                if (GroupOf(def) == HospitalConditionGroup.State)
                    continue;

                Cached.Add(def);
            }

            Cached.SortBy(def => (int) GroupOf(def), def => def.label);
        }

        /// <summary>
        /// Which drawer a hediff belongs in, or <see cref="HospitalConditionGroup.State"/> for one that belongs in
        /// none and should not be listed at all.
        ///
        /// The order of the tests matters: an addiction is chronic and a disease is often curable by an item, so
        /// the most specific reading has to win or everything ends up in one drawer.
        /// </summary>
        internal static HospitalConditionGroup GroupOf(HediffDef def)
        {
            return UIGuard.Try("Hospital.ConditionGroup", () =>
            {
                if (def.IsAddiction)
                    return HospitalConditionGroup.Addictions;

                if (typeof(Hediff_Injury).IsAssignableFrom(def.hediffClass))
                    return HospitalConditionGroup.Injuries;

                if (def.CompProps<HediffCompProperties_Immunizable>() != null)
                    return HospitalConditionGroup.Diseases;

                if (def.chronic)
                    return HospitalConditionGroup.Chronic;

                return HospitalConditionGroup.State;
            }, HospitalConditionGroup.State, null);
        }

        internal static string LabelOf(HospitalConditionGroup group)
        {
            switch (group)
            {
                case HospitalConditionGroup.State:
                    return "State";

                case HospitalConditionGroup.Injuries:
                    return "Injuries";

                case HospitalConditionGroup.Diseases:
                    return "Diseases";

                case HospitalConditionGroup.Chronic:
                    return "Chronic";

                default:
                    return "Addictions";
            }
        }

        /// <summary>
        /// How many people on this map have this condition right now.
        ///
        /// <b>Shown beside every row because it turns a guess into a decision.</b> Ticking "gunshot" reads very
        /// differently when two colonists currently have one than when nobody has had one in a year, and the
        /// hunting bill's species list already established that a live count belongs next to a checkbox.
        /// </summary>
        internal static int CountOnMap(Map map, HediffDef def)
        {
            if (map == null || def == null)
                return 0;

            return UIGuard.Try("Hospital.ConditionCount", () =>
            {
                List<Pawn> pawns = map.mapPawns.FreeColonistsAndPrisonersSpawned;

                if (pawns == null)
                    return 0;

                int count = 0;

                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];

                    if (pawn != null && pawn.health != null && pawn.health.hediffSet != null
                        && pawn.health.hediffSet.HasHediff(def))
                        count++;
                }

                return count;
            }, 0, null);
        }

        /// <summary>How many people are in one of the four states, for the same reason as above.</summary>
        internal static int CountInState(Map map, string state, float painAbove)
        {
            if (map == null)
                return 0;

            return UIGuard.Try("Hospital.StateCount", () =>
            {
                List<Pawn> pawns = map.mapPawns.FreeColonistsAndPrisonersSpawned;

                if (pawns == null)
                    return 0;

                int count = 0;

                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];

                    if (pawn == null || pawn.health == null || pawn.health.hediffSet == null)
                        continue;

                    bool hit;

                    switch (state)
                    {
                        case "pain":
                            hit = pawn.health.hediffSet.PainTotal >= Mathf.Max(0f, painAbove);

                            break;

                        case "bleeding":
                            hit = pawn.health.hediffSet.BleedRateTotal > 0f;

                            break;

                        case "downed":
                            hit = pawn.Downed;

                            break;

                        default:
                            Building_Bed bed = pawn.CurrentBed();
                            hit = bed != null && bed.Medical;

                            break;
                    }

                    if (hit)
                        count++;
                }

                return count;
            }, 0, null);
        }
    }
}
