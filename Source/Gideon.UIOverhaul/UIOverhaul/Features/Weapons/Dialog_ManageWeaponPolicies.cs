using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Weapons
{
    /// <summary>
    /// The manage-policies window for weapons.
    ///
    /// <b>RimWorld's own window, with four answers supplied.</b> <c>Dialog_ManagePolicies&lt;T&gt;</c> is public
    /// and abstract, and it already carries the whole management surface: the list down the left, rename, copy
    /// and paste, delete with its refusal message, set-default, and the search box. Subclassing it means this
    /// window is the apparel and food windows -- same layout, same shortcuts, same behaviour -- rather than a
    /// third thing shaped roughly like them.
    ///
    /// <b>The filter tree is the game's too.</b> <c>ThingFilterUI.DoThingFilterConfigWindow</c> against a parent
    /// filter of the Weapons category is exactly what the apparel window does against Apparel, so a modded
    /// weapon appears in the tree without this knowing it exists.
    /// </summary>
    public class Dialog_ManageWeaponPolicies : Dialog_ManagePolicies<WeaponPolicy>
    {
        private readonly ThingFilterUI.UIState filterState = new ThingFilterUI.UIState();

        /// <summary>
        /// Everything a weapon policy may contain: the Weapons category and whatever is under it.
        ///
        /// Built once and kept, as the apparel window keeps its own. It is the tree's outer bound rather than a
        /// setting, so nothing writes to it after this.
        /// </summary>
        private static ThingFilter weaponGlobalFilter;

        private static ThingFilter WeaponGlobalFilter
        {
            get
            {
                if (weaponGlobalFilter == null)
                {
                    weaponGlobalFilter = new ThingFilter();
                    weaponGlobalFilter.SetAllow(ThingCategoryDefOf.Weapons, true);
                }

                return weaponGlobalFilter;
            }
        }

        protected override string TitleKey
        {
            get { return "Gideon_WeaponPolicyTitle"; }
        }

        protected override string TipKey
        {
            get { return "Gideon_WeaponPolicyTip"; }
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(700f, 700f); }
        }

        public Dialog_ManageWeaponPolicies(WeaponPolicy policy)
            : base(policy)
        {
        }

        public override void PreOpen()
        {
            base.PreOpen();

            filterState.quickSearch.Reset();
        }

        protected override WeaponPolicy CreateNewPolicy()
        {
            WeaponPolicies set = WeaponPolicies.Current;

            return set != null ? set.MakeNew() : null;
        }

        protected override WeaponPolicy GetDefaultPolicy()
        {
            WeaponPolicies set = WeaponPolicies.Current;

            return set != null ? set.Default() : null;
        }

        protected override void SetDefaultPolicy(WeaponPolicy policy)
        {
            WeaponPolicies set = WeaponPolicies.Current;

            if (set != null)
                set.SetDefault(policy);
        }

        protected override AcceptanceReport TryDeletePolicy(WeaponPolicy policy)
        {
            WeaponPolicies set = WeaponPolicies.Current;

            return set != null ? set.TryDelete(policy) : AcceptanceReport.WasRejected;
        }

        protected override List<WeaponPolicy> GetPolicies()
        {
            WeaponPolicies set = WeaponPolicies.Current;

            return set != null ? set.All : new List<WeaponPolicy>();
        }

        protected override void DoContentsRect(Rect rect)
        {
            if (SelectedPolicy == null)
                return;

            ThingFilterUI.DoThingFilterConfigWindow(rect, filterState, SelectedPolicy.filter, WeaponGlobalFilter,
                16, null, null);
        }
    }
}
