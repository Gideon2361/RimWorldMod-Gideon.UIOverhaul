using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// Sedation the editor can apply and, if asked, hold on.
    ///
    /// <b>Why holding it on needs machinery at all.</b> Anesthetic is not a switch. Its def carries
    /// <c>HediffCompProperties_SeverityPerDay</c> at <c>-0.8</c> and a <c>Disappears</c> comp set to
    /// 45,000-120,000 ticks, so one dose fades and then deletes itself somewhere between three quarters of a day
    /// and two days later. Anything that wants a creature to stay under has to keep saying so.
    ///
    /// <b>250 ticks, which is the interval Aaron asked for and a generous one.</b> Severity falls 0.8 per day,
    /// so across 250 ticks it drops about 0.003 and the top-up is almost always a no-op -- which is what a
    /// keep-alive should be. The interval only really matters for the other case: when the Disappears comp
    /// finally takes the hediff away, this is how long the creature is awake before a fresh dose lands. Four
    /// seconds of game time, once a day or so.
    ///
    /// <b>Stored on the game rather than on the pawn,</b> for the reasons written up on
    /// <see cref="Anomaly.StudyAssignments"/>: a GameComponent needs no def, is made for every game, and carries
    /// its own save data. Pawns are saved by reference, so the list comes back as the same creatures.
    ///
    /// <b>The tick is the only part of this that touches the game while the editor is shut,</b> and it does one
    /// thing: puts the severity back. It never doses somebody who was not checked, and it drops a pawn who has
    /// died rather than going on treating a corpse.
    /// </summary>
    public class EditorSedation : GameComponent
    {
        /// <summary>How often the dose is put back, in ticks.</summary>
        private const int Interval = 250;

        private List<Pawn> kept = new List<Pawn>();

        /// <summary>Required by RimWorld: every GameComponent is constructed with the game it belongs to.</summary>
        public EditorSedation(Game game)
        {
        }

        private static EditorSedation Component =>
            UIGuard.Try("Editor.Sedation.Component",
                () => Current.Game?.GetComponent<EditorSedation>(), null, null);

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref kept, "gideonKeepSedated", LookMode.Reference);

            if (kept == null)
                kept = new List<Pawn>();

            // References only resolve by PostLoadInit, so pruning earlier would find every entry null and empty
            // the list. A null that survives to here is a pawn the save no longer contains.
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                kept.RemoveAll(pawn => !Usable(pawn));
        }

        public override void GameComponentTick()
        {
            if (kept.Count == 0)
                return;

            if (Find.TickManager.TicksGame % Interval != 0)
                return;

            UIGuard.Try("Editor.Sedation.Tick", () =>
            {
                // Backwards, so removing a pawn who has died does not skip the one after them.
                for (int i = kept.Count - 1; i >= 0; i--)
                {
                    Pawn pawn = kept[i];

                    if (!Usable(pawn))
                    {
                        kept.RemoveAt(i);

                        continue;
                    }

                    Sedate(pawn);
                }
            }, null);
        }

        /// <summary>Whether this pawn is being held under.</summary>
        internal static bool Kept(Pawn pawn)
        {
            EditorSedation component = Component;

            return pawn != null && component != null && component.kept.Contains(pawn);
        }

        /// <summary>
        /// Starts or stops holding this pawn under.
        ///
        /// Starting doses them at once rather than at the next multiple of the interval, so ticking the box does
        /// what it says while somebody is looking at it.
        /// </summary>
        internal static void SetKept(Pawn pawn, bool keep)
        {
            EditorSedation component = Component;

            if (pawn == null || component == null)
                return;

            if (!keep)
            {
                component.kept.Remove(pawn);

                return;
            }

            if (!component.kept.Contains(pawn))
                component.kept.Add(pawn);

            Sedate(pawn);
        }

        /// <summary>
        /// Anesthetic at full strength, whether or not there is already some.
        ///
        /// <b>Topped up rather than added again.</b> Nothing stops a second Anesthetic hediff existing beside the
        /// first: the def is not unique and <c>AddHediff</c> would happily build a pile of them, which reads as a
        /// column of identical rows in the editor and is not what sedating somebody means.
        ///
        /// Returns whether anything actually changed, so a caller can decide if it is worth recording an undo.
        /// </summary>
        internal static bool Sedate(Pawn pawn)
        {
            return UIGuard.Try("Editor.Sedate", () =>
            {
                if (pawn == null || pawn.Dead || pawn.health == null || pawn.health.hediffSet == null)
                    return false;

                HediffDef def = HediffDefOf.Anesthetic;

                if (def == null)
                    return false;

                // <b>maxSeverity cannot be trusted blind:</b> HediffDef declares it as float.MaxValue and relies
                // on each def to say otherwise. Anesthetic says 1. A def that never said is treated as 1 rather
                // than as infinity, since every severity in the game is a fraction of one.
                float max = def.maxSeverity >= float.MaxValue ? 1f : def.maxSeverity;

                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);

                if (existing != null)
                {
                    if (existing.Severity >= max)
                        return false;

                    existing.Severity = max;

                    return true;
                }

                Hediff made = HediffMaker.MakeHediff(def, pawn);

                if (made == null)
                    return false;

                made.Severity = max;

                pawn.health.AddHediff(made);

                return true;
            }, false, null);
        }

        private static bool Usable(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Destroyed;
        }
    }
}
