using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Mechs
{
    [DefOf]
    public static class MechDefOf
    {
        /// <summary>
        /// A work mech waiting, at length, instead of asking for work again immediately.
        ///
        /// <b>Deliberately not <c>JobDefOf.SelfShutdown</c>,</b> which is the obvious candidate and the wrong
        /// one: <c>Need_MechEnergy.IsSelfShutdown</c> tests <c>CurJobDef == JobDefOf.SelfShutdown</c> and a
        /// mech in that state gains energy. Hibernation is a performance setting and must not hand out free
        /// charge. See Defs/Jobs_MechHibernate.xml.
        /// </summary>
        public static JobDef Gideon_MechHibernate;

        static MechDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MechDefOf));
        }
    }
}
