using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Work
{
    /// <summary>
    /// A named set of work priorities, saved so it can be put on another pawn or reused in another colony.
    ///
    /// Keyed by defName rather than by WorkTypeDef, because a template outlives the game session it was made
    /// in: the file is read before any def is resolved, and a work type from a mod that is currently switched
    /// off has to survive being read and written again rather than being silently dropped.
    /// </summary>
    public class WorkPriorityTemplate
    {
        public string name;

        /// <summary>
        /// WorkTypeDef defName to priority. Zeros are stored as well as non-zeros: a template says what the
        /// whole assignment should be, and "not assigned" is part of that. Dropping the zeros would make
        /// applying a template unable to switch anything off.
        /// </summary>
        public Dictionary<string, int> priorities = new Dictionary<string, int>();

        public int PriorityFor(WorkTypeDef work)
        {
            return work != null && priorities.TryGetValue(work.defName, out int priority) ? priority : 0;
        }

        public void Set(WorkTypeDef work, int priority)
        {
            if (work != null)
                priorities[work.defName] = Mathf.Clamp(priority, 0, WorkPriorityRange.Lowest);
        }

        /// <summary>How many work types this template actually asks for, which is what a player counts.</summary>
        public int AssignedCount
        {
            get
            {
                int count = 0;
                foreach (KeyValuePair<string, int> entry in priorities)
                {
                    if (entry.Value > 0)
                        count++;
                }

                return count;
            }
        }

        /// <summary>Every visible work type's current priority on this pawn, zeros included.</summary>
        public static WorkPriorityTemplate From(Pawn pawn, string name)
        {
            WorkPriorityTemplate template = new WorkPriorityTemplate { name = name };

            if (pawn?.workSettings == null)
                return template;

            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (!work.visible)
                    continue;

                // A work type the pawn cannot do reads as 0 whatever the template meant, so recording it
                // would bake this pawn's incapabilities into a template meant for anyone.
                if (pawn.WorkTypeIsDisabled(work))
                    continue;

                template.priorities[work.defName] = pawn.workSettings.GetPriority(work);
            }

            return template;
        }

        /// <summary>
        /// Writes this template onto a pawn.
        ///
        /// A work type the pawn is incapable of is left alone rather than written -- SetPriority logs an error
        /// for those, and a template made from a capable pawn should not fail on an incapable one. It stays at
        /// 0, which is the only value an incapable work type can hold.
        ///
        /// A work type the template has never heard of is also left alone rather than zeroed. That is the case
        /// when a mod is added after the template was saved, and clearing work the template never had an
        /// opinion about would look like the template was corrupting the pawn.
        /// </summary>
        /// <returns>How many work types were skipped because the pawn cannot do them.</returns>
        public int ApplyTo(Pawn pawn)
        {
            if (pawn?.workSettings == null)
                return 0;

            int skipped = 0;

            foreach (KeyValuePair<string, int> entry in priorities)
            {
                WorkTypeDef work = DefDatabase<WorkTypeDef>.GetNamedSilentFail(entry.Key);
                if (work == null)
                    continue;

                if (pawn.WorkTypeIsDisabled(work))
                {
                    if (entry.Value > 0)
                        skipped++;

                    continue;
                }

                pawn.workSettings.SetPriority(work, Mathf.Clamp(entry.Value, 0, WorkPriorityRange.Lowest));
            }

            return skipped;
        }

        public WorkPriorityTemplate Clone()
        {
            return new WorkPriorityTemplate
            {
                name = name,
                priorities = new Dictionary<string, int>(priorities)
            };
        }
    }
}
