using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Study
{
    /// <summary>
    /// The button: assign one colonist to study this entity, or let anybody.
    ///
    /// <b>It sits beside vanilla's study toggle,</b> because that is the switch it refines: the toggle answers
    /// whether this entity is studied at all and this one answers by whom. Appended to
    /// <c>CompStudiable.CompGetGizmosExtra</c> so it appears wherever that toggle does, on the entity and on the
    /// platform holding it, with no def edits and nothing hidden.
    ///
    /// <b>Wrapped rather than transpiled.</b> The patched method is an iterator, so a postfix receives the
    /// enumerable rather than the yielded items: the original is enumerated first and ours follows. Wrapping keeps
    /// vanilla's laziness intact, which matters because that method reads <c>EverStudiable</c> and the dev gizmos
    /// as it goes.
    ///
    /// <b>Only where an assignment could mean something.</b> No button on something nobody can ever study, and
    /// none on an entity with no colonists to choose from, since a picker with nothing in it is a dead end rather
    /// than a control.
    /// </summary>
    [HarmonyPatch(typeof(CompStudiable), nameof(CompStudiable.CompGetGizmosExtra))]
    internal static class Patch_StudyAssignGizmo
    {
        public static void Postfix(CompStudiable __instance, ref IEnumerable<Gizmo> __result)
        {
            IEnumerable<Gizmo> original = __result;

            __result = Append(original, __instance);
        }

        /// <summary>
        /// Vanilla's gizmos, then ours.
        ///
        /// The guard is inside the iterator rather than around it: this runs while the inspect pane is being
        /// drawn, and a throw from a gizmo enumerator loses every command on the selected thing, vanilla's
        /// included.
        /// </summary>
        private static IEnumerable<Gizmo> Append(IEnumerable<Gizmo> original, CompStudiable comp)
        {
            if (original != null)
            {
                foreach (Gizmo gizmo in original)
                    yield return gizmo;
            }

            Gizmo mine = UIGuard.Try("Study.AssignGizmo", () => Build(comp), null,
                "The study assignment button is missing from this entity. Everything else on it still works.");

            if (mine != null)
                yield return mine;
        }

        private static Gizmo Build(CompStudiable comp)
        {
            Thing entity = comp?.parent;

            if (entity == null || !entity.Spawned || entity.Map == null)
                return null;

            // The same question vanilla's toggle asks itself: something that can never be studied has nothing to
            // assign. Cached because it is asked every frame the thing is selected.
            if (!comp.EverStudiableCached())
                return null;

            Pawn studier = StudyAssignments.AssignedTo(entity);

            Command_Action command = new Command_Action
            {
                defaultLabel = studier == null ? "Studier: anyone" : "Studier: " + studier.LabelShortCap,
                defaultDesc = Description(entity, studier),
                icon = StudyGlyphs.Assign,
                action = () => Dialog_PickStudier.For(entity, studier)
            };

            return command;
        }

        private static string Description(Thing entity, Pawn studier)
        {
            string text = studier == null
                ? "Anybody may study and suppress this."
                : studier.LabelShortCap + " is the only colonist who will study or suppress this.";

            return text + "\n\nAssigning one colonist keeps the rest away from both jobs: an entity that does "
                        + "something to whoever stands next to it is not work to be shared around, and "
                        + "suppression is what puts somebody there most often.\n\nOrdering another colonist to "
                        + "study or suppress it by right clicking still works. This governs what they choose to "
                        + "do on their own.";
        }
    }
}
