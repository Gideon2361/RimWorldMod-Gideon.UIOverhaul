using System.Collections.Generic;
using System.Text;
using Gideon.UIOverhaul.Features.Work;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Pawns.Templates
{
    /// <summary>
    /// A named, saved set of a pawn's assignments: their work priorities, their daily schedule, their assign-tab
    /// policies, or any combination, according to the template's <see cref="scope"/>.
    ///
    /// <b>One type for all three, because the player is doing one thing.</b> The edit tools appear in three places
    /// and differ only in reach: on a pawn's card they capture everything, above the work priorities pane they
    /// capture priorities alone, on the schedule strip the schedule alone. Three classes would mean three stores,
    /// three manager windows and three sets of the same apply-and-report logic.
    ///
    /// <b>Keyed by defName throughout, never by def instance.</b> A template outlives the session it was made in:
    /// the file is read before any def is resolved, and a work type or time assignment belonging to a mod that is
    /// currently switched off has to survive being read and written again rather than being silently dropped.
    /// Policies are the exception and cannot follow this rule -- see <see cref="PawnPolicySet"/> for why they are
    /// matched by label instead.
    ///
    /// <b>Applying leaves alone anything it cannot set.</b> Work the pawn is incapable of, a work type from a mod
    /// since removed, a policy renamed since saving: each is skipped and counted rather than forced or zeroed. A
    /// template that cleared everything it did not understand would look like it was corrupting the pawn.
    /// </summary>
    public class PawnTemplate
    {
        /// <summary>Hours in a RimWorld day, and therefore the length of a schedule.</summary>
        public const int ScheduleHours = GenDate.HoursPerDay;

        public string name;

        /// <summary>
        /// What this template speaks for. Recorded rather than inferred from what it contains: a whole-pawn
        /// template taken from a colonist with an empty schedule carries no schedule entries, and inferring would
        /// quietly demote it to priorities-only.
        /// </summary>
        public PawnTemplateScope scope = PawnTemplateScope.Priorities;

        /// <summary>
        /// WorkTypeDef defName to priority. Zeros are stored as well as non-zeros: a template says what the whole
        /// assignment should be, and "not assigned" is part of that. Dropping the zeros would leave a template
        /// unable to switch anything off.
        /// </summary>
        public Dictionary<string, int> priorities = new Dictionary<string, int>();

        /// <summary>
        /// TimeAssignmentDef defNames, one per hour, or null when this template has no schedule. Always
        /// <see cref="ScheduleHours"/> long when present.
        /// </summary>
        public List<string> schedule;

        public PawnPolicySet policies;

        public bool Covers(PawnTemplateScope part) => (scope & part) == part && part != PawnTemplateScope.None;

        /// <summary>
        /// This work type's priority as the template holds it, or 0 when it says nothing about it.
        ///
        /// For the manager window, which edits a template directly rather than through a pawn.
        /// </summary>
        public int PriorityFor(WorkTypeDef work)
        {
            return work != null && priorities.TryGetValue(work.defName, out int priority) ? priority : 0;
        }

        public void Set(WorkTypeDef work, int priority)
        {
            if (work != null)
                priorities[work.defName] = Mathf.Clamp(priority, 0, WorkPriorityRange.Lowest);
        }

        /// <summary>The assignment this template wants for an hour, or null when it says nothing about it.</summary>
        public TimeAssignmentDef AssignmentAt(int hour)
        {
            if (schedule == null || hour < 0 || hour >= schedule.Count || schedule[hour].NullOrEmpty())
                return null;

            return DefDatabase<TimeAssignmentDef>.GetNamedSilentFail(schedule[hour]);
        }

        // ---------------------------------------------------------------------------------------
        // Capture
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Reads the parts of <paramref name="pawn"/> that <paramref name="scope"/> asks for.
        /// </summary>
        public static PawnTemplate From(Pawn pawn, string name, PawnTemplateScope scope)
        {
            PawnTemplate template = new PawnTemplate { name = name, scope = scope };

            if (pawn == null)
                return template;

            if (template.Covers(PawnTemplateScope.Priorities))
                template.CapturePriorities(pawn);

            if (template.Covers(PawnTemplateScope.Schedule))
                template.CaptureSchedule(pawn);

            if (template.Covers(PawnTemplateScope.Policies))
                template.policies = PawnPolicySet.From(pawn);

            return template;
        }

        private void CapturePriorities(Pawn pawn)
        {
            if (pawn.workSettings == null)
                return;

            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (!work.visible)
                    continue;

                // A work type the pawn cannot do reads as 0 whatever the player intended, so recording it would
                // bake this pawn's incapabilities into a template meant for anyone.
                if (pawn.WorkTypeIsDisabled(work))
                    continue;

                priorities[work.defName] = pawn.workSettings.GetPriority(work);
            }
        }

        private void CaptureSchedule(Pawn pawn)
        {
            List<TimeAssignmentDef> times = pawn.timetable?.times;

            if (times == null)
                return;

            schedule = new List<string>(ScheduleHours);

            for (int hour = 0; hour < ScheduleHours; hour++)
            {
                // A shorter list than expected is not something to trust silently; an empty string records "this
                // hour said nothing" and applying skips it.
                TimeAssignmentDef assignment = hour < times.Count ? times[hour] : null;
                schedule.Add(assignment?.defName ?? string.Empty);
            }
        }

        // ---------------------------------------------------------------------------------------
        // Apply
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Writes every in-scope part of this template onto a pawn, reporting what could not be set.
        /// </summary>
        public PawnTemplateApplyResult ApplyTo(Pawn pawn)
        {
            return ApplyTo(pawn, PawnTemplateScope.Everything);
        }

        /// <summary>
        /// Writes the parts of this template that are both in its own scope and within <paramref name="limit"/>.
        ///
        /// <b>The limit is what lets one template serve three sets of tools.</b> A whole-pawn template holds a
        /// schedule, so the schedule strip offers it -- but pressing apply there must change the schedule and
        /// nothing else. Refusing to offer it instead would mean the player's most complete template is the one
        /// they cannot use where they want part of it, and applying all of it would rewrite work priorities from a
        /// button that says schedule.
        /// </summary>
        public PawnTemplateApplyResult ApplyTo(Pawn pawn, PawnTemplateScope limit)
        {
            PawnTemplateApplyResult result = new PawnTemplateApplyResult();

            if (pawn == null)
                return result;

            if (Applies(PawnTemplateScope.Priorities, limit))
            {
                ApplyPriorities(pawn, result);
                result.applied |= PawnTemplateScope.Priorities;
            }

            if (Applies(PawnTemplateScope.Schedule, limit))
            {
                ApplySchedule(pawn, result);
                result.applied |= PawnTemplateScope.Schedule;
            }

            if (Applies(PawnTemplateScope.Policies, limit) && policies != null)
            {
                policies.ApplyTo(pawn, result.unresolved);
                result.applied |= PawnTemplateScope.Policies;
            }

            return result;
        }

        /// <summary>Whether a part is both something this template speaks for and something the caller asked for.</summary>
        private bool Applies(PawnTemplateScope part, PawnTemplateScope limit)
        {
            return Covers(part) && (limit & part) == part;
        }

        private void ApplyPriorities(Pawn pawn, PawnTemplateApplyResult result)
        {
            if (pawn.workSettings == null)
                return;

            foreach (KeyValuePair<string, int> entry in priorities)
            {
                WorkTypeDef work = DefDatabase<WorkTypeDef>.GetNamedSilentFail(entry.Key);

                if (work == null)
                {
                    // Only counted when the template actually wanted the work done. A template carrying a zero for
                    // a work type that no longer exists has lost nothing worth telling the player about.
                    if (entry.Value > 0)
                        result.unknownWorkTypes++;

                    continue;
                }

                if (pawn.WorkTypeIsDisabled(work))
                {
                    if (entry.Value > 0)
                        result.incapableWorkTypes++;

                    continue;
                }

                pawn.workSettings.SetPriority(work, Mathf.Clamp(entry.Value, 0, WorkPriorityRange.Lowest));
            }
        }

        private void ApplySchedule(Pawn pawn, PawnTemplateApplyResult result)
        {
            if (schedule == null || pawn.timetable == null)
                return;

            HashSet<string> reported = null;

            for (int hour = 0; hour < ScheduleHours && hour < schedule.Count; hour++)
            {
                string defName = schedule[hour];

                // An hour the template had no opinion about keeps whatever the pawn had.
                if (defName.NullOrEmpty())
                    continue;

                TimeAssignmentDef assignment = DefDatabase<TimeAssignmentDef>.GetNamedSilentFail(defName);

                if (assignment == null)
                {
                    // Reported once per missing assignment rather than once per hour: a template built around a
                    // modded assignment that is now absent would otherwise name it up to 24 times in one message.
                    if (reported == null)
                        reported = new HashSet<string>();

                    if (reported.Add(defName))
                        result.unresolved.Add("schedule assignment \"" + defName + "\"");

                    continue;
                }

                // SetAssignment rather than writing times[hour] directly, so whatever bookkeeping vanilla does
                // around a schedule change keeps happening.
                pawn.timetable.SetAssignment(hour, assignment);
            }
        }

        // ---------------------------------------------------------------------------------------
        // Description
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// What this template covers, for the manager window's subtitle: "12 work types, schedule, 4 policies".
        ///
        /// Counts rather than a bare scope list, because the count is what a player checks a template against.
        /// </summary>
        public string Describe()
        {
            List<string> parts = new List<string>();

            if (Covers(PawnTemplateScope.Priorities))
            {
                int assigned = AssignedWorkCount;
                parts.Add(assigned + (assigned == 1 ? " work type" : " work types"));
            }

            if (Covers(PawnTemplateScope.Schedule))
                parts.Add("schedule");

            if (Covers(PawnTemplateScope.Policies))
            {
                int count = PolicyCount;
                parts.Add(count + (count == 1 ? " policy" : " policies"));
            }

            return parts.Count == 0 ? "empty" : string.Join(", ", parts.ToArray());
        }

        /// <summary>How many work types this template actually asks for, which is what a player counts.</summary>
        public int AssignedWorkCount
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

        private int PolicyCount
        {
            get
            {
                if (policies == null)
                    return 0;

                int count = 0;

                if (!policies.apparel.NullOrEmpty()) count++;
                if (!policies.drug.NullOrEmpty()) count++;
                if (!policies.food.NullOrEmpty()) count++;
                if (!policies.reading.NullOrEmpty()) count++;
                if (policies.medicalCare.HasValue) count++;
                if (policies.hostilityResponse.HasValue) count++;
                if (policies.selfTend.HasValue) count++;

                return count;
            }
        }

        public PawnTemplate Clone()
        {
            return new PawnTemplate
            {
                name = name,
                scope = scope,
                priorities = new Dictionary<string, int>(priorities),
                schedule = schedule == null ? null : new List<string>(schedule),
                policies = policies?.Clone()
            };
        }
    }
}
