using System.Collections.Generic;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Orders
{
    /// <summary>
    /// Orders a colonist to use a recreation source: play the chess table, watch the television, shoot at the
    /// horseshoes pin.
    ///
    /// <b>Vanilla offers none of this,</b> because recreation is not work. The right click menu is built from work
    /// givers and a list of special cases, and joy is neither: a pawn picks their own recreation from
    /// <c>JoyGiver</c>s during their recreation hours, weighted by chance and their own preferences. Asked for on
    /// 2026-08-22, and it is the one order players reach for that vanilla has never had.
    ///
    /// <b>Through the game's own joy giver rather than by making the job here.</b> Each giver builds a different
    /// job shape: a chess table needs a chair chosen and stored as target B, a telescope needs its interaction
    /// cell, a billiards table needs a cue position. Handing <c>JobMaker</c> the job def and the building would
    /// produce a job the driver cannot run, and it would break differently for each kind of building. So the
    /// giver's own <c>TryGivePlayJob</c> is called, which is what the think tree calls when the pawn chooses it.
    ///
    /// <b>Which means reflection, because that method is protected.</b> The two members needed are resolved once
    /// and cached; if either is ever renamed the feature reports itself and goes quiet rather than throwing at
    /// every right click. <see cref="JoyGiver_InteractBuilding"/> is the only base offered here for the same
    /// reason: it is the one whose whole job is "a pawn interacts with this building", so a building it lists is a
    /// building a pawn can be sent to. Drug and food joy givers are deliberately not offered, because ingesting
    /// already has its own right click option.
    ///
    /// <b>The giver's own validity test is honored.</b> <c>CanInteractWith</c> covers reservation, forbidding,
    /// social and political properness, power, and vacuum, which is a longer list than would be worth
    /// reimplementing and would drift the moment a DLC added to it.
    /// </summary>
    public class FloatMenuOptionProvider_Recreation : FloatMenuOptionProvider
    {
        protected override bool Drafted => false;

        protected override bool Undrafted => true;

        /// <summary>
        /// One pawn at a time.
        ///
        /// Most recreation buildings seat one or two, and the giver reserves as it goes, so a squad order would
        /// send the first pawn and refuse the rest one by one. That is a menu full of failures rather than an
        /// order.
        /// </summary>
        protected override bool Multiselect => false;

        /// <summary>
        /// Left to the giver.
        ///
        /// Every joy giver def carries its own <c>requiredCapacities</c>, checked through
        /// <c>JoyGiver.CanBeGivenTo</c>: sight for a television, manipulation for chess, both for billiards.
        /// Asserting manipulation here would refuse a blind pawn the horseshoes they can actually play.
        /// </summary>
        protected override bool RequiresManipulation => false;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            return UIGuard.Try("Orders.RecreationApplies", () =>
            {
                if (!base.AppliesInt(context))
                    return false;

                Pawn pawn = context.FirstSelectedPawn;

                // The joy need is the test: no need, nothing to gain, and no joy giver would accept them anyway.
                return pawn?.needs?.joy != null && pawn.IsColonistPlayerControlled;
            }, false, "The recreation order is not offered.");
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            List<FloatMenuOption> options = UIGuard.Try("Orders.RecreationOptions",
                () => Options(clickedThing, context), null,
                "The recreation order is not offered for that thing.");

            if (options == null)
                yield break;

            for (int i = 0; i < options.Count; i++)
                yield return options[i];
        }

        /// <summary>
        /// Every recreation this thing offers this pawn.
        ///
        /// <b>A list rather than one option,</b> because a building can appear in more than one joy giver: a
        /// television is watched by three of them in Core alone, one per kind of programme, and they are genuinely
        /// different choices with different joy kinds. Where two givers produce the same label, the second is
        /// dropped rather than drawn twice.
        /// </summary>
        private static List<FloatMenuOption> Options(Thing clicked, FloatMenuContext context)
        {
            Pawn pawn = context?.FirstSelectedPawn;

            if (clicked?.def == null || pawn == null || !Ready)
                return null;

            List<FloatMenuOption> options = null;
            List<JoyGiverDef> givers = DefDatabase<JoyGiverDef>.AllDefsListForReading;

            for (int i = 0; i < givers.Count; i++)
            {
                JoyGiverDef def = givers[i];

                if (def?.thingDefs == null || !def.thingDefs.Contains(clicked.def))
                    continue;

                FloatMenuOption option = Option(def, pawn, clicked);

                if (option == null)
                    continue;

                if (options == null)
                    options = new List<FloatMenuOption>();

                if (!Named(options, option.Label))
                    options.Add(option);
            }

            return options;
        }

        private static bool Named(List<FloatMenuOption> options, string label)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Label == label)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// One giver's option, or null when this giver has nothing to offer here.
        ///
        /// <b>Silence rather than a greyed line for most refusals.</b> A pawn who cannot see has no business being
        /// told they cannot watch television, and a building that is out of power or reserved is usually obvious
        /// from the building itself. The exception is the pawn's own recreation tolerance, which is invisible and
        /// worth a word: see the tooltip.
        /// </summary>
        private static FloatMenuOption Option(JoyGiverDef def, Pawn pawn, Thing thing)
        {
            JoyGiver giver = def.Worker;

            if (!(giver is JoyGiver_InteractBuilding) || def.jobDef == null)
                return null;

            if (!giver.CanBeGivenTo(pawn))
                return null;

            if (!Interactable(giver, pawn, thing))
                return null;

            Job job = PlayJob(giver, pawn, thing);

            if (job == null)
                return null;

            string label = Label(def, thing);

            FloatMenuOption option = new FloatMenuOption(label, UIGuard.Wrap("Orders.TakeJoyJob", () =>
            {
                // Ordered rather than forced into the joy need's own path, so it interrupts what the pawn is doing
                // and is dropped when the joy is spent, exactly as their own choice would have been.
                pawn.jobs.TryTakeOrderedJob(job, JobTag.SatisfyingNeeds);
            }));

            option.tooltip = (TipSignal) Tip(def, pawn);

            return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, thing);
        }

        /// <summary>
        /// What the option says.
        ///
        /// <b>Built from the job's report string,</b> which is where RimWorld already keeps the human wording for
        /// every one of these: "playing chess.", "watching television.". Trimmed of its full stop and capitalized,
        /// it reads as the order it is, in whatever language the game is running in, and a modded joy giver gets
        /// the same treatment for free. The thing's own name is appended when the report does not already carry a
        /// target, so two televisions in two rooms can be told apart.
        /// </summary>
        private static string Label(JoyGiverDef def, Thing thing)
        {
            string report = def.jobDef.reportString;

            if (report.NullOrEmpty() || report.Contains("Target"))
                return "Recreation at " + thing.LabelShort;

            return report.TrimEnd('.').CapitalizeFirst() + " at " + thing.LabelShort;
        }

        /// <summary>
        /// The one thing about this order a player cannot see for themselves.
        ///
        /// Recreation the pawn has had too much of lately gives almost nothing, which looks like the order not
        /// working. <c>JoyKindDef</c> tolerance is per pawn and per kind, so it is read here rather than guessed.
        /// </summary>
        private static string Tip(JoyGiverDef def, Pawn pawn)
        {
            if (def.joyKind == null)
                return "Recreation.";

            string text = def.joyKind.LabelCap + " recreation.";

            JoyToleranceSet tolerances = pawn.needs?.joy?.tolerances;

            if (tolerances == null)
                return text;

            float factor = tolerances.JoyFactorFromTolerance(def.joyKind);

            if (factor < 0.5f)
            {
                text += "\n\n" + pawn.LabelShortCap + " has had a lot of this lately and will get "
                        + factor.ToStringPercent() + " of the usual recreation from it.";
            }

            return text;
        }

        // ---------------------------------------------------------------------------------------
        // Reaching the giver's protected members
        // ---------------------------------------------------------------------------------------

        /// <summary><c>JoyGiver_InteractBuilding.TryGivePlayJob(Pawn, Thing)</c>, which is protected abstract.</summary>
        private static MethodInfo play;

        /// <summary><c>JoyGiver_InteractBuilding.CanInteractWith(Pawn, Thing, bool)</c>, protected virtual.</summary>
        private static MethodInfo interactable;

        private static bool resolved;

        /// <summary>
        /// Whether both members were found.
        ///
        /// <b>Resolved on first use rather than in a static initializer,</b> and that is not a style choice. This
        /// class is instantiated by <c>FloatMenuMakerMap.Init</c> through <c>Activator.CreateInstance</c>, with no
        /// guarding of its own: anything thrown while the type initializes would come out as a
        /// <c>TypeInitializationException</c> from that loop and cost the game every float menu, ours and its own.
        /// Resolving inside a guard, later, cannot do that. A miss means an update renamed something, and the
        /// feature reports once and stays quiet.
        /// </summary>
        private static bool Ready
        {
            get
            {
                if (resolved)
                    return play != null && interactable != null;

                resolved = true;

                UIGuard.Try("Orders.ResolveJoyGiver", () =>
                {
                    play = AccessTools.Method(typeof(JoyGiver_InteractBuilding), "TryGivePlayJob",
                        new[] { typeof(Pawn), typeof(Thing) });

                    interactable = AccessTools.Method(typeof(JoyGiver_InteractBuilding), "CanInteractWith",
                        new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
                }, "Recreation cannot be ordered from the right click menu.");

                if (play == null || interactable == null)
                {
                    Log.Warning(UILogTag.Prefix + "JoyGiver_InteractBuilding did not have the expected members, "
                                + "so recreation cannot be ordered from the right click menu. Everything else "
                                + "works.");
                }

                return play != null && interactable != null;
            }
        }

        private static bool Interactable(JoyGiver giver, Pawn pawn, Thing thing)
        {
            object answer = interactable.Invoke(giver, new object[] { pawn, thing, false });

            return answer is bool && (bool) answer;
        }

        /// <summary>
        /// The giver's own job for this building, or null if it declines.
        ///
        /// <b>Virtual dispatch through reflection is the point:</b> the override that runs is the one belonging to
        /// this giver's class, so a chess table gets the seated version and a telescope gets the interaction cell
        /// version without this code knowing either exists.
        /// </summary>
        private static Job PlayJob(JoyGiver giver, Pawn pawn, Thing thing)
        {
            return play.Invoke(giver, new object[] { pawn, thing }) as Job;
        }
    }
}
