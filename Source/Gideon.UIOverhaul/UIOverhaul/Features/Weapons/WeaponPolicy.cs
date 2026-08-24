using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Weapons
{
    /// <summary>
    /// A named set of weapons a colonist is allowed to carry.
    ///
    /// <b>Deliberately the same shape as <c>ApparelPolicy</c>,</b> which is a <c>Policy</c> wrapping a
    /// <c>ThingFilter</c> and nothing else. Matching it exactly is what lets the management window, the picker
    /// and the float menu all be the game's own controls rather than new ones: every part of RimWorld that
    /// handles a policy handles this one, because there is nothing different about it to handle.
    ///
    /// <b>The load key is prefixed.</b> A policy's save id is <c>LoadKey_label_id</c>, and an unprefixed
    /// "WeaponPolicy" is exactly the name another mod would reach for -- two mods with the same key in one save
    /// resolve each other's references. The prefix costs nothing and makes a collision impossible.
    /// </summary>
    public class WeaponPolicy : Policy
    {
        public ThingFilter filter = new ThingFilter();

        protected override string LoadKey
        {
            get { return "Gideon_WeaponPolicy"; }
        }

        public WeaponPolicy()
        {
        }

        public WeaponPolicy(int id, string label)
            : base(id, label)
        {
        }

        public override void CopyFrom(Policy other)
        {
            WeaponPolicy weapons = other as WeaponPolicy;

            if (weapons != null)
                filter.CopyAllowancesFrom(weapons.filter);
        }

        /// <summary>
        /// Never guarded, and never will be.
        ///
        /// <c>Scribe_*</c> calls are the save itself: a guard that swallowed a throw here would write a policy
        /// with no filter and the save would load with every weapon disallowed rather than with an error.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Deep.Look(ref filter, "filter");
        }
    }
}
