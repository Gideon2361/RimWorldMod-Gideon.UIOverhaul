using System;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// Which body part a hediff has to go on, for the kinds that cannot go on "no particular part".
    ///
    /// <b>Why this exists, found by Aaron on 2026-08-23.</b> Adding One with Death's Control Expansion from the
    /// editor logged <c>ControlExpansion has null Part. It should be set before PostAdd</c>. That message comes
    /// from <c>Hediff_Level.PostAdd</c>, which refuses a null part outright -- and the editor offered "Whole body"
    /// for every condition, because most conditions genuinely accept it.
    ///
    /// <b><c>Hediff_Level</c> is the family that cannot.</b> Its whole purpose is a thing with a level attached to
    /// an organ: vanilla's psylink is one and goes on the brain, and every mod that adds a levelled capability
    /// inherits from it. There is no flag on the def to read, so the class is the test -- and testing the class
    /// covers every mod's subclass without naming any of them.
    ///
    /// <b>The brain, or the body's core part.</b> The brain is where vanilla puts a psylink, and
    /// <c>ConsciousnessSource</c> is how <c>HediffSet.GetBrain</c> finds it, so it is right for anything mind
    /// shaped. A body with no consciousness source at all -- a mech, something a mod invented -- falls back to
    /// <c>corePart</c>, which every body has by definition.
    ///
    /// <b>Two callers on purpose.</b> The editor asks before offering the choice, so "Whole body" is never
    /// offered for a hediff that cannot take it; the template importer asks again, because a template written
    /// before this existed can still carry a null part and would hit the same error on load.
    /// </summary>
    internal static class HediffPlacement
    {
        /// <summary>
        /// Whether this def must be attached to a part.
        ///
        /// Read off <c>hediffClass</c> rather than off an instance, so the question can be answered before one is
        /// made -- which is what the editor needs when it is deciding what to offer.
        /// </summary>
        internal static bool NeedsPart(HediffDef def)
        {
            return UIGuard.Try("Editor.HediffNeedsPart", () =>
            {
                Type hediffClass = def?.hediffClass;

                return hediffClass != null && typeof(Hediff_Level).IsAssignableFrom(hediffClass);
            }, false, null);
        }

        /// <summary>
        /// The part to use when one is required and none was chosen, or null when the body has nothing suitable.
        ///
        /// Null is a real answer and the caller must handle it: a body with neither a consciousness source nor a
        /// core part is not one this can place anything on, and adding the hediff anyway is what produced the
        /// error in the first place.
        /// </summary>
        internal static BodyPartRecord Default(Pawn pawn)
        {
            return UIGuard.Try("Editor.DefaultHediffPart", () =>
            {
                if (pawn?.health?.hediffSet == null)
                    return null;

                BodyPartRecord brain = pawn.health.hediffSet.GetBrain();

                if (brain != null)
                    return brain;

                return pawn.RaceProps?.body?.corePart;
            }, null, null);
        }

        /// <summary>
        /// The part a hediff should actually be added on: what was chosen, or a default when one is required.
        ///
        /// Leaves a chosen part alone always. The player picking the left arm for something is the player's call
        /// even if it is an odd one; this only fills a blank that the game will not accept.
        /// </summary>
        internal static BodyPartRecord Resolve(Pawn pawn, HediffDef def, BodyPartRecord chosen)
        {
            if (chosen != null || !NeedsPart(def))
                return chosen;

            return Default(pawn);
        }
    }
}
