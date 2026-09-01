using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// The one-line answer to "how is this colonist doing", in severity order.
    ///
    /// Ordered rather than combined, and the order is the whole design. A bleeding, downed, freezing pawn is
    /// three problems, but a column that says all three says nothing at a glance -- and only one of them is
    /// what you would act on first. So the most urgent wins the line, and the tooltip carries the rest.
    ///
    /// The order is by how soon it costs you something: bleeding out has a clock on it, vacuum burns through
    /// a pawn in seconds, being downed is dangerous but stable, the temperature hediffs take body parts, a
    /// mental break is doing damage right now, a life-threatening condition is the game's own red alert,
    /// needing treatment is a job to queue, being merely too cold is a room to fix, and healthy is everything
    /// else.
    /// </summary>
    internal enum PawnHealthState
    {
        Healthy,

        /// <summary>A corpse. Grey, no badge: nothing about a body is a job to queue.</summary>
        Dead,

        /// <summary>
        /// Down, but already in a bed and with nothing else the matter. Blue, no badge.
        ///
        /// <b>Separated from <see cref="Downed"/> because the two ask for opposite things.</b> A pawn on the
        /// floor needs somebody to drop what they are doing and carry them; a pawn in a bed has already had that
        /// done and is waiting to get better. Badging the second one 911 puts a red emergency marker on every
        /// hospital bed in a colony that is recovering from a raid, which is precisely when the marker most needs
        /// to mean something.
        ///
        /// Nothing is hidden by this: it is the last state tested, so an untended wound, an infection, a heart
        /// attack or anything else the game calls life threatening still wins the line and keeps its own badge.
        /// </summary>
        Recovering,

        /// <summary>
        /// Standing somewhere outside what this pawn can comfortably take, with nothing yet wrong with them.
        /// Amber, no badge.
        /// </summary>
        Temperature,

        /// <summary>Something is tendable, and nothing about it is racing a clock. Amber, badged TEND.</summary>
        NeedsTending,

        /// <summary>
        /// An infection is present. Amber like plain tending, because it is the same kind of problem and the
        /// pawn is not dying of it today; badged HELP, because unlike a wound it gets worse on its own.
        /// </summary>
        UrgentTending,

        /// <summary>
        /// The game itself considers something this pawn has to be currently life threatening. Red, badged
        /// 911. See <see cref="HasLifeThreateningHediff"/>: this is the same test that raises vanilla's own
        /// critical alert, so the column and the alert cannot disagree.
        /// </summary>
        LifeThreatening,

        /// <summary>
        /// The pawn is in a mental state. Purple, badged BREAK: the only state here that is not a medical
        /// problem at all, which is exactly why it gets a colour of its own rather than sharing red.
        /// </summary>
        MentalBreak,

        Downed,

        /// <summary>
        /// The game has given this pawn a temperature hediff: frostbite forming, heatstroke, or hypothermia.
        /// Red, badged 911. All three progress on their own and all three end in something lost, which is what
        /// separates them from the amber state of merely standing somewhere too cold.
        /// </summary>
        SevereTemperature,

        Vacuum,

        /// <summary>The game says this pawn dies of blood loss soon. Red, and stated as an emergency.</summary>
        BleedingOut
    }

    /// <summary>
    /// Reads a pawn's condition into something a cell can draw: a state, a short label, and a color.
    ///
    /// Labels are title case throughout, including the single-word ones, so the column does not mix two
    /// conventions down its length.
    ///
    /// Every reading here is a live property, so nothing needs invalidating. All of it is cheap except
    /// <c>TicksUntilDeathDueToBloodLoss</c>, which is only asked for once bleeding is already established.
    /// </summary>
    internal readonly struct PawnHealthSummary
    {
        /// <summary>
        /// What the badges say. See <see cref="Tag"/> for which state gets which.
        ///
        /// Untranslated, and that is deliberate rather than an omission. All three are three or four
        /// characters wide in a column with none to spare, and they are read as symbols rather than as
        /// English -- the color and the position carry the meaning, and the text only distinguishes them from
        /// each other. Translating would mean promising the replacement stays this short in every language
        /// the mod is played in, and a badge that wraps or clips is worse than one nobody has to read.
        /// </summary>
        private const string EmergencyTag = "911";

        private const string UrgentTag = "HELP";

        private const string TendTag = "TEND";

        private const string BreakTag = "BREAK";

        public readonly PawnHealthState State;

        /// <summary>What the cell shows. Carries the countdown when there is one.</summary>
        public readonly string Label;

        /// <summary>Everything true about the pawn, not only the winning line.</summary>
        public readonly string Detail;

        private PawnHealthSummary(PawnHealthState state, string label, string detail)
        {
            State = state;
            Label = label;
            Detail = detail;
        }

        /// <summary>
        /// A badge drawn before the label, or null for the states that do not warrant one.
        ///
        /// <b>Derived from the state rather than passed in,</b> so a state cannot end up badged in one branch
        /// of <see cref="For"/> and bare in another. Three of them are a triage scale, sorted by how long you
        /// have rather than by how bad it looks, and the fourth is off that scale entirely:
        ///
        /// <b>911, red. This pawn is losing something you cannot give back.</b> Bleeding out has a clock the
        /// game has already started, vacuum burns through an unprotected pawn in seconds, a downed pawn has to
        /// be carried out now, and frostbite, heatstroke and hypothermia are the point at which cold and heat
        /// stop being discomfort and start taking body parts and lives. Every one is a
        /// drop-what-you-are-doing.
        ///
        /// <b>HELP, amber. This gets worse while you decide.</b> An infection races the pawn's immunity, so it
        /// is not something to leave for the next tending pass, but nobody is dying this hour.
        ///
        /// <b>TEND, blue. This is work to queue.</b> A wound that needs tending waits patiently; it belongs on
        /// the list rather than at the top of it, and blue is the palette's colour for "here is something",
        /// which is exactly the weight it should carry. It is also the one badge that does not match its own
        /// text colour, and see <see cref="TagColor"/> for why that is the point rather than an oversight.
        ///
        /// <b>BREAK, purple. This one is not medicine at all.</b> A pawn in a mental state needs talking down,
        /// arresting or leaving alone, and a doctor is no use to any of it. It sits off the red-amber-blue
        /// scale for that reason: sharing a colour with the medical rows would file it as a treatment queue
        /// item, which is the one thing it is not.
        ///
        /// <b>Below that, nothing,</b> and the restraint is what keeps the rest working. Being too cold for
        /// comfort is a room to fix rather than a job to assign, and healthy is the answer for most of the
        /// colony most of the time; badging either would put a marker on every row, and a marker on every row
        /// is one the eye stops finding.
        /// </summary>
        public string Tag
        {
            get
            {
                switch (State)
                {
                    case PawnHealthState.BleedingOut:
                    case PawnHealthState.Vacuum:
                    case PawnHealthState.Downed:
                    case PawnHealthState.SevereTemperature:
                    case PawnHealthState.LifeThreatening:
                        return EmergencyTag;

                    case PawnHealthState.MentalBreak:
                        return BreakTag;

                    case PawnHealthState.UrgentTending:
                        return UrgentTag;

                    case PawnHealthState.NeedsTending:
                        return TendTag;

                    default:
                        return null;
                }
            }
        }

        /// <summary>
        /// The badge's fill, which is not always the label's color.
        ///
        /// <b>The badge and the text answer different questions,</b> which is why the two are allowed to
        /// disagree. The text is severity, in three tiers: red for dying, amber for hurt, green for fine. The
        /// badge is urgency, which is not the same axis -- an infection and a plain wound are both amber
        /// problems, and only one of them is getting worse while you read the row.
        ///
        /// <b>So they part in exactly one place.</b> A tendable wound keeps amber text and takes a blue badge,
        /// which is what separates it from the infection sitting directly above it in the same colour.
        /// Everywhere else the badge and the text agree, and a badge that agreed everywhere would be telling
        /// you nothing the colour had not already said.
        ///
        /// Palette roles rather than literals throughout, so a theme restating what danger or attention looks
        /// like carries into the badges without touching this.
        /// </summary>
        public Color TagColor(UIColorPaletteDef palette)
        {
            return State == PawnHealthState.NeedsTending ? palette.Accent : Color(palette);
        }

        /// <summary>
        /// Reads a pawn's condition. Not cheap -- it walks the hediff list for infections, has
        /// <c>HasTendableHediff</c> walk it again, and reads the vacuum at the pawn's cell.
        ///
        /// Deliberately uncached here. The panel caches a whole row's worth of display values on one clock
        /// rather than every reading owning a cache of its own; see <c>PawnsPanel.RowData</c>.
        /// </summary>
        public static PawnHealthSummary For(Pawn pawn)
        {
            // <b>First, because none of the triage below means anything about a corpse.</b> A dead pawn still has
            // hediffs, still has a bleed rate frozen at the moment of death, and still stands somewhere cold, so
            // every test in this method has an opinion about them and all of those opinions are wrong. The one
            // fact worth the line is how far gone the body is.
            if (pawn.Dead)
                return new PawnHealthSummary(PawnHealthState.Dead, DeadLabel(pawn), DeadDetail(pawn));

            bool downed = pawn.Downed;
            bool needsTending = pawn.health?.hediffSet?.HasTendableHediff(false) ?? false;
            float bleedRate = pawn.health?.hediffSet?.BleedRateTotal ?? 0f;
            bool infected = HasInfection(pawn);

            bool vacuum = InVacuum(pawn);

            TemperatureTrouble temperature = ReadTemperature(pawn);
            Hediff dying = LifeThreateningHediff(pawn);
            MentalStateDef breaking = pawn.InMentalState ? pawn.MentalStateDef : null;

            string detail = BuildDetail(pawn, downed, needsTending, bleedRate, infected, vacuum, temperature,
                dying, breaking);

            // Tier three: the game itself says this pawn dies of blood loss soon. Asked only once bleeding is
            // established, because the call walks hediffs; and gated on a day, because a scratch bleeds too and
            // a column that cries emergency over a scratch is one players learn to ignore.
            if (bleedRate > 0.0001f)
            {
                int ticks = HealthUtility.TicksUntilDeathDueToBloodLoss(pawn);

                if (ticks < GenDate.TicksPerDay)
                {
                    // The urgency is a badge rather than a word, so the row reads as a state and a countdown
                    // instead of a sentence. "Emergency:" spent the first third of the column saying something
                    // the color had already said, and pushed the number that actually matters off to the right.
                    // Which badge is decided by Tag, from the state, so it is not restated here.
                    return new PawnHealthSummary(PawnHealthState.BleedingOut,
                        "Bleeding Out, " + ticks.ToStringTicksToPeriod(true, false, true, true),
                        detail);
                }
            }

            if (vacuum)
                return new PawnHealthSummary(PawnHealthState.Vacuum, "In Vacuum, Unprotected", detail);

            // <b>Only a pawn who still needs carrying.</b> Downed is an emergency because somebody has to stop
            // what they are doing and rescue them, which is vanilla's own reading -- Alert_ColonistNeedsRescuing
            // ignores a pawn already in a bed for exactly this reason. One who has been carried there is waiting
            // to heal, and a red 911 on every hospital bed after a raid is a marker that has stopped meaning
            // anything.
            //
            // Falling through rather than returning a quieter state here is the other half of it: everything
            // below this line -- frostbite, a mental break, a life threatening condition, an infection, an
            // untended wound -- is a real reason to raise the alarm about somebody in a bed, and each of them
            // now gets to. Only when none of them applies does the pawn read as recovering, at the very bottom
            // of this method.
            if (downed && !InBed(pawn))
                return new PawnHealthSummary(PawnHealthState.Downed, "Downed", detail);

            // Above tending, because all three progress while a doctor walks over and the cure is a warmer or
            // cooler room rather than a bandage. Sitting behind a row that says "Needs Tending" would point at
            // the wrong action.
            switch (temperature)
            {
                case TemperatureTrouble.Freezing:
                    return new PawnHealthSummary(PawnHealthState.SevereTemperature, "Frostbite", detail);

                case TemperatureTrouble.Hot:
                    return new PawnHealthSummary(PawnHealthState.SevereTemperature, "Heatstroke", detail);

                case TemperatureTrouble.Cold:
                    return new PawnHealthSummary(PawnHealthState.SevereTemperature, "Hypothermic", detail);
            }

            // Above the medical rows below it, because a pawn in a break is doing damage right now and the
            // response is nothing a doctor does. Below the rows above it, because those are all somebody dying
            // in the next few minutes.
            if (breaking != null)
                return new PawnHealthSummary(PawnHealthState.MentalBreak, breaking.LabelCap, detail);

            // Named by the condition rather than by a generic phrase. "Plague" beside a red 911 says both what
            // is wrong and how bad it is, where "Life-Threatening Illness" says only the half the badge had
            // already said. Below the temperature rows on purpose: hypothermia and heatstroke qualify here too
            // once they progress, and their own labels point at the fix while this one does not.
            if (dying != null)
                return new PawnHealthSummary(PawnHealthState.LifeThreatening, dying.LabelCap, detail);

            // Tier two: an infection. Above plain tending because this is the one that gets worse while you
            // decide -- an untended cut waits, an infection races the pawn's immunity.
            if (infected)
                return new PawnHealthSummary(PawnHealthState.UrgentTending, "Urgent Tending Needed", detail);

            // Tier one: something is tendable and nothing about it is on a clock. Bleeding that is not fatal
            // within a day lands here too -- it is a wound to tend, not an emergency, and saying so twice in
            // two different colors would be worse than saying it once.
            if (needsTending || bleedRate > 0.0001f)
                return new PawnHealthSummary(PawnHealthState.NeedsTending, "Needs Tending", detail);

            // The quiet tier: a pawn standing somewhere they cannot comfortably take, with nothing wrong with
            // them yet. Amber and unbadged, because it is not a job to assign -- it is a room to fix, and
            // fixing it now is what stops one of the red rows above appearing later.
            switch (temperature)
            {
                case TemperatureTrouble.Chilly:
                    return new PawnHealthSummary(PawnHealthState.Temperature, "Too Cold", detail);

                case TemperatureTrouble.Sweltering:
                    return new PawnHealthSummary(PawnHealthState.Temperature, "Too Hot", detail);
            }

            // Everything that could have been wrong has now been asked. A pawn still down at this point is in a
            // bed with nothing else the matter, which is a state worth naming and not worth alarming about.
            if (downed)
                return new PawnHealthSummary(PawnHealthState.Recovering, "In Bed", detail);

            return new PawnHealthSummary(PawnHealthState.Healthy, "Healthy", detail);
        }

        /// <summary>
        /// What a corpse's line says: how far the body has gone, which is the only reading that changes.
        ///
        /// The stage comes from the corpse's own rot comp rather than from a time calculation of ours, so a body
        /// in a freezer reads fresh for as long as the game says it is.
        /// </summary>
        private static string DeadLabel(Pawn pawn)
        {
            return UIGuard.Try("Pawns.DeadLabel", () =>
            {
                Corpse corpse = pawn.Corpse;

                CompRottable rot = corpse != null ? corpse.TryGetComp<CompRottable>() : null;

                if (rot == null)
                    return "Dead";

                switch (rot.Stage)
                {
                    case RotStage.Rotting:
                        return "Rotting";

                    case RotStage.Dessicated:
                        return "Dessicated";

                    default:
                        return "Dead";
                }
            }, "Dead", null);
        }

        /// <summary>How long ago, for the tooltip, since the line itself has room for one word.</summary>
        private static string DeadDetail(Pawn pawn)
        {
            return UIGuard.Try("Pawns.DeadDetail", () =>
            {
                Corpse corpse = pawn.Corpse;

                if (corpse == null || corpse.Age <= 0)
                    return "Dead.";

                return "Dead for " + corpse.Age.ToStringTicksToPeriod(false, false, false) + ".";
            }, "Dead.", null);
        }

        /// <summary>
        /// Whether this pawn is in a bed, by RimWorld's own test.
        ///
        /// <c>RestUtility.InBed</c> rather than a check for a bed at the pawn's cell: it also covers being
        /// carried into one, sleeping spots, and the caravan and space cases, and it is the same method vanilla's
        /// rescue alert uses to decide the same thing.
        /// </summary>
        private static bool InBed(Pawn pawn)
        {
            return UIGuard.Try("Pawns.InBed", () => pawn.InBed(), false, null);
        }

        /// <summary>
        /// Whether somebody has to do something about this pawn.
        ///
        /// The question the map rail's dot asks, and it is deliberately wider than "is this an emergency":
        /// a tendable wound nobody has got to is the case a player most wants told about on a map they are
        /// not currently looking at. <see cref="PawnHealthState.Recovering"/> is excluded because it is the
        /// state that means somebody already did -- the pawn is in a bed, getting better.
        /// </summary>
        public bool NeedsCare
        {
            get
            {
                switch (State)
                {
                    case PawnHealthState.NeedsTending:
                    case PawnHealthState.UrgentTending:
                    case PawnHealthState.LifeThreatening:
                    case PawnHealthState.BleedingOut:
                    case PawnHealthState.Downed:
                    case PawnHealthState.Vacuum:
                    case PawnHealthState.SevereTemperature:
                        return true;

                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// Whether the care is on a clock, which is what turns the rail's dot from amber to red.
        ///
        /// The same split the column's own colors make: an untended wound waits, and everything here does
        /// not. A mental break is left out on purpose -- it is urgent, but it is not care, and colouring
        /// it as an injury on a rail with no room to explain would send somebody looking for a doctor.
        /// </summary>
        public bool Urgent
        {
            get
            {
                switch (State)
                {
                    case PawnHealthState.LifeThreatening:
                    case PawnHealthState.BleedingOut:
                    case PawnHealthState.Downed:
                    case PawnHealthState.Vacuum:
                    case PawnHealthState.SevereTemperature:
                        return true;

                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// The color for the winning state, from the palette's meaning roles rather than from literals, so a
        /// theme can restate what "danger" looks like and this follows.
        /// </summary>
        public Color Color(UIColorPaletteDef palette)
        {
            switch (State)
            {
                case PawnHealthState.BleedingOut:
                case PawnHealthState.Vacuum:
                case PawnHealthState.Downed:
                case PawnHealthState.SevereTemperature:
                case PawnHealthState.LifeThreatening:
                    return palette.Danger;

                // The palette's existing mood colour rather than a new purple, and the fit is exact rather
                // than convenient: a mental break is a mood failure, and this is the colour the mood bar two
                // columns over is already drawn in. A theme that restates what mood looks like gets a matching
                // break row for free.
                case PawnHealthState.MentalBreak:
                    return palette.Mood;

                case PawnHealthState.UrgentTending:
                case PawnHealthState.NeedsTending:
                case PawnHealthState.Temperature:
                    return palette.Warning;

                // Blue rather than green or amber: nothing is wrong, but nothing is finished either, and this is
                // the palette's colour for "here is something" -- the same weight the TEND badge carries.
                case PawnHealthState.Recovering:
                    return palette.Accent;

                // Grey, which is the one thing on this scale that is not a call to action at all.
                case PawnHealthState.Dead:
                    return palette.TextDisabled;

                default:
                    return palette.Success;
            }
        }

        /// <summary>
        /// Whether the pawn is standing in vacuum that can hurt them.
        ///
        /// Two halves, and getting this wrong the first time is instructive: <c>Pawn.HarmedByVacuum</c> reads
        /// like "exposed and unprotected" and is not. It is a <b>capability</b> --
        /// <c>OdysseyActive &amp;&amp; !IsMechanoid &amp;&amp; breathesAir &amp;&amp; VacuumResistance &lt; 1</c>
        /// -- which is true of every human colonist who is not in fully vacuum-proof gear, wherever they happen
        /// to be. Using it alone reported the whole colony as exposed while they stood indoors.
        ///
        /// The location half is what was missing. <c>VacuumUtility.GetVacuum</c> at the pawn's own cell answers
        /// where they actually are, and the 0.5 threshold is vanilla's: it is what
        /// <c>VacuumUtility.VacuumConcernTo</c> uses to decide whether a pawn should care about a place.
        ///
        /// <c>PositionHeld</c> and <c>MapHeld</c> rather than Position and Map, so a pawn inside a container or
        /// a caravan resolves to wherever the container is instead of throwing on an unspawned pawn.
        ///
        /// The <c>VacuumExposure</c> hediff was the other candidate, and is deliberately not used: it lingers
        /// while it heals, so a pawn who reached safety would still be reported as being in vacuum. Temperature
        /// reads its hediffs for the opposite reason -- "freezing" while recovering from hypothermia is fair,
        /// where "in vacuum" while standing in a corridor is not.
        /// </summary>
        private static bool InVacuum(Pawn pawn)
        {
            // The capability half. Also covers Odyssey being absent, so nothing below needs to.
            if (!pawn.HarmedByVacuum)
                return false;

            Map map = pawn.MapHeld;

            if (map == null)
                return false;

            return VacuumUtility.GetVacuum(pawn.PositionHeld, map) > 0.5f;
        }

        /// <summary>
        /// The first hediff the game currently considers life threatening, or null.
        ///
        /// <b>Vanilla's own test, copied deliberately rather than approximated.</b> This is the condition
        /// behind <c>Alert_LifeThreateningHediff</c>, the red critical alert at the top of the screen. Judging
        /// it ourselves would mean the column and the alert eventually disagreeing about whether a colonist is
        /// dying, and when those two disagree the player has no way to tell which is wrong.
        ///
        /// <c>IsCurrentlyLifeThreatening</c> is the game's own reading and accounts for the hediff's current
        /// stage, so a plague that has not progressed far does not qualify and the same plague later does.
        /// <c>FullyImmune</c> excludes the ones the pawn has already beaten and is merely recovering from,
        /// which would otherwise sit at maximum severity looking like an emergency for days.
        ///
        /// <b>The Deathless exception is vanilla's too.</b> A pawn with that gene does not die of ordinary
        /// conditions, so the alert only counts hediffs whose stage destroys the brain, and so does this. It
        /// is guarded on Biotech being active before the gene is named, so the check costs nothing and reaches
        /// nothing when the DLC is absent.
        /// </summary>
        private static Hediff LifeThreateningHediff(Pawn pawn)
        {
            List<Hediff> hediffs = pawn.health?.hediffSet?.hediffs;

            if (hediffs == null)
                return null;

            bool deathless = ModsConfig.BiotechActive && pawn.genes != null
                                                      && pawn.genes.HasActiveGene(GeneDefOf.Deathless);

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];

                if (hediff == null || !hediff.IsCurrentlyLifeThreatening || hediff.FullyImmune())
                    continue;

                if (deathless)
                {
                    HediffStage stage = hediff.CurStage;

                    if (stage == null || !stage.mtbDeathDestroysBrain)
                        continue;
                }

                return hediff;
            }

            return null;
        }

        /// <summary>
        /// Whether the pawn has an infection.
        ///
        /// Keyed on <c>HediffDef.isInfection</c>, which the game sets on the defs that are infections. Two
        /// things recommend it over naming WoundInfection: that def is not in <c>HediffDefOf</c>, so reaching
        /// it would mean a database lookup by string; and a mod's own infection sets the same flag, so this
        /// covers those without knowing about them.
        ///
        /// The list is walked directly rather than through a helper because HediffSet has none for this. It is
        /// short -- a pawn's hediffs number in the tens at worst -- and this is read once per row per frame.
        /// </summary>
        private static bool HasInfection(Pawn pawn)
        {
            List<Hediff> hediffs = pawn.health?.hediffSet?.hediffs;

            if (hediffs == null)
                return false;

            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i]?.def != null && hediffs[i].def.isInfection)
                    return true;
            }

            return false;
        }

        private enum TemperatureTrouble
        {
            None,

            /// <summary>Outside the comfortable range, with nothing wrong yet.</summary>
            Chilly,

            Sweltering,

            /// <summary>Hypothermia has set in, and it progresses to death if the pawn stays where they are.</summary>
            Cold,

            /// <summary>Frostbite is forming. Body parts are being taken.</summary>
            Freezing,

            /// <summary>Heatstroke. The one heat state the game gives out, and it kills.</summary>
            Hot
        }

        /// <summary>
        /// The Frostbite hediff, looked up once.
        ///
        /// <b>By name, because it is not in <c>HediffDefOf</c>,</b> and unlike infection there is no flag on
        /// the def to key off instead. Resolved lazily rather than in a static initializer, since this type is
        /// reachable before the definition database is populated and a null there would be cached forever.
        /// </summary>
        private static HediffDef frostbite;

        private static bool frostbiteResolved;

        private static HediffDef Frostbite
        {
            get
            {
                if (frostbiteResolved)
                    return frostbite;

                frostbite = DefDatabase<HediffDef>.GetNamedSilentFail("Frostbite");
                frostbiteResolved = true;

                return frostbite;
            }
        }

        /// <summary>
        /// Whether the pawn is in temperature trouble, and how badly.
        ///
        /// <b>Two tiers, and the line between them is whether the game has given the pawn a hediff for it.</b>
        /// Frostbite, heatstroke and hypothermia all progress on their own and all three end in something
        /// being lost -- a body part or the pawn. Being merely too cold for comfort is a different kind of
        /// fact: nothing is wrong with the pawn, they are standing somewhere wrong. That line is worth drawing
        /// exactly where the game already draws it, rather than inventing a severity threshold of our own that
        /// would disagree with the health tab.
        ///
        /// <b>Only frostbite that is currently forming counts.</b> The def carries
        /// <c>HediffCompProperties_GetsPermanent</c>, so a colonist who lost a toe three winters ago still has
        /// a frostbite hediff for the rest of their life. Testing for the def alone would have flagged that
        /// pawn as an emergency permanently, which is the fastest way to teach somebody to ignore the badge.
        ///
        /// <b>Ambient temperature is the bottom tier only,</b> and it is deliberately outranked by every
        /// hediff above it. Read alone it answers "is this uncomfortable", which is true of a pawn walking
        /// briskly across a cold map in no danger at all -- so it is not fit to be a warning on its own. As the
        /// quietest thing the column can say, when nothing has actually gone wrong yet, it is exactly right:
        /// it is the reading that lets somebody fix the room before the hediff appears.
        /// </summary>
        private static TemperatureTrouble ReadTemperature(Pawn pawn)
        {
            HediffSet set = pawn.health?.hediffSet;

            if (set == null)
                return TemperatureTrouble.None;

            if (HasActiveFrostbite(set))
                return TemperatureTrouble.Freezing;

            if (HasVisible(set, HediffDefOf.Heatstroke))
                return TemperatureTrouble.Hot;

            if (HasVisible(set, HediffDefOf.Hypothermia))
                return TemperatureTrouble.Cold;

            return Ambient(pawn);
        }

        /// <summary>
        /// Whether the pawn has this condition to a degree the game itself admits to.
        ///
        /// <b>Not <c>HasHediff</c>, and that is the fix for what Aaron reported on 2026-08-22:</b> a visiting
        /// trader with a red 911 reading Hypothermic whose health tab listed nothing but an ambrosia tolerance.
        /// Both temperature hediffs open with a stage marked <c>becomeVisible false</c> -- hypothermia's runs to
        /// severity 0.04 -- and RimWorld hides those from the health card entirely. The hediff is genuinely there,
        /// so <c>HasHediff</c> is genuinely true, and the pawn is merely standing somewhere chilly.
        ///
        /// <c>Hediff.Visible</c> is the same test <c>HealthCardUtility</c> filters its own list with, so this row
        /// now says what the health tab says. Below the threshold the reading falls through to the ambient tier
        /// and comes out as an amber "Too Cold", which is what a shivering guest actually is.
        /// </summary>
        private static bool HasVisible(HediffSet set, HediffDef def)
        {
            if (def == null)
                return false;

            List<Hediff> hediffs = set.hediffs;

            if (hediffs == null)
                return false;

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];

                if (hediff != null && hediff.def == def && hediff.Visible)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether any frostbite on the pawn is a live injury rather than an old scar.
        ///
        /// The hediff list is walked directly because HediffSet has no query for this: <c>HasHediff</c> would
        /// answer for the scars too, and the permanence lives on the individual hediff's comp rather than on
        /// the def.
        /// </summary>
        private static bool HasActiveFrostbite(HediffSet set)
        {
            HediffDef def = Frostbite;

            if (def == null)
                return false;

            List<Hediff> hediffs = set.hediffs;

            if (hediffs == null)
                return false;

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];

                // Visible for the same reason the two temperature hediffs are tested for it: this column should
                // never name something the health tab does not show.
                if (hediff != null && hediff.def == def && hediff.Visible && !hediff.IsPermanent())
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether the pawn is simply standing somewhere outside what they can comfortably take.
        ///
        /// <c>ComfortableTemperatureRange</c> is the game's own answer, so it already accounts for the pawn's
        /// clothing, their race and any trait or gene that moves it. <c>AmbientTemperature</c> is where they
        /// actually are rather than the map's outdoor reading, so a colonist indoors in winter reads as fine.
        /// </summary>
        private static TemperatureTrouble Ambient(Pawn pawn)
        {
            if (!pawn.Spawned)
                return TemperatureTrouble.None;

            FloatRange comfortable = pawn.ComfortableTemperatureRange();
            float here = pawn.AmbientTemperature;

            if (here < comfortable.min)
                return TemperatureTrouble.Chilly;

            return here > comfortable.max ? TemperatureTrouble.Sweltering : TemperatureTrouble.None;
        }

        /// <summary>
        /// Everything true at once, for the tooltip: the line in the cell is only the most urgent of these.
        /// </summary>
        private static string BuildDetail(Pawn pawn, bool downed, bool needsTending, float bleedRate,
            bool infected, bool vacuum, TemperatureTrouble temperature, Hediff dying, MentalStateDef breaking)
        {
            string detail = "Health: "
                            + (pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f).ToStringPercent();

            if (breaking != null)
                detail += "\nMental break: " + breaking.LabelCap;

            // Spelled out here even though the row already says the condition's name, because the row cannot
            // say why that name is in red. This is the line that connects it to the alert on screen.
            if (dying != null)
                detail += "\n" + dying.LabelCap + " is currently life threatening";

            if (bleedRate > 0.0001f)
                detail += "\nBleeding";

            if (infected)
                detail += "\nInfected";

            if (vacuum)
                detail += "\nExposed to vacuum with no protection";

            if (downed)
                detail += "\nDowned";

            if (needsTending)
                detail += "\nHas untended injuries or conditions";

            switch (temperature)
            {
                case TemperatureTrouble.Freezing:
                    detail += "\nFrostbite is forming";
                    break;

                case TemperatureTrouble.Hot:
                    detail += "\nHeatstroke";
                    break;

                case TemperatureTrouble.Cold:
                    detail += "\nHypothermia";
                    break;

                case TemperatureTrouble.Chilly:
                    detail += "\nColder than this pawn can comfortably take";
                    break;

                case TemperatureTrouble.Sweltering:
                    detail += "\nHotter than this pawn can comfortably take";
                    break;
            }

            return detail;
        }
    }
}
