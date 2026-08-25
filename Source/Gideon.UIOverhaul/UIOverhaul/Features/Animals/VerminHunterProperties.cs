using Verse;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// Marks a race as one that patrols for small game rather than only eating when it is hungry.
    ///
    /// <b>A def extension rather than a check on one defName.</b> The barn owl is the reason this exists, but
    /// nothing here is about owls: any race whose fantasy is "keeps the vermin down" wants the same behavior,
    /// including one from another mod, and a race gets it by adding four lines of XML rather than by being named
    /// in this assembly.
    ///
    /// <b>Why it is needed at all.</b> Vanilla predation lives inside <c>FoodUtility</c> and is reached only
    /// through the hunger path, so a fed predator never hunts. It also runs
    /// <c>FoodUtility.IsAcceptablePreyFor</c>, which happily returns true for the colony's own animals: that is
    /// the behavior behind <c>Alert_PredatorInPen</c> and behind every story about a tamed lynx eating the
    /// chickens. Both of those are wrong for a working animal, so this is a separate job giver rather than a
    /// patch onto vanilla's.
    /// </summary>
    public class VerminHunterProperties : DefModExtension
    {
        /// <summary>
        /// The largest thing it will go after, in body size.
        ///
        /// Separate from <c>RaceProps.maxPreyBodySize</c> on purpose, and expected to be smaller. That one is
        /// what a starving animal will risk; this one is what it will pick a fight with on a full stomach, and
        /// there is no reason those should be the same number.
        /// </summary>
        public float maxPreyBodySize = 0.15f;

        /// <summary>How far it will travel from where it is standing to reach prey.</summary>
        public float radius = 30f;

        /// <summary>
        /// It patrols only below this much food. Not a hunger check: at the default it covers nearly the whole
        /// range, so a hunter that has just eaten rests and everything else counts as on duty.
        /// </summary>
        public float huntBelowFood = 0.9f;

        /// <summary>
        /// How long it is left alone after a look that found nothing. One in-game hour by default.
        ///
        /// The scan below walks the map's pawns and does a reachability test per candidate, which is the same
        /// order of cost as vanilla's own predator scan and wants the same treatment.
        /// </summary>
        public int restTicks = 2500;
    }
}
