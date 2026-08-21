using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using UnityEngine;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// How many bills a workbench may hold, which the player now chooses.
    ///
    /// <b>One place, because three different things ask.</b> The IL that gates vanilla's Add and Paste buttons,
    /// the bench tab's header, and the picker's footer all have to agree, and the number is a setting so none of
    /// them can cache it. A property read per frame is nothing; three copies of the clamping rule would be a bug
    /// waiting for somebody to edit one of them.
    ///
    /// <b>Read live rather than latched.</b> The transpiler in <see cref="Patch_BillLimit"/> rewrites vanilla's
    /// compiled-in fifteen into a call to this, so moving the slider takes effect on the next frame with no
    /// restart and no repatching. That is the whole reason the transform calls a property instead of writing a
    /// different constant.
    /// </summary>
    internal static class BillCap
    {
        /// <summary>
        /// Vanilla's own limit, which is also the floor.
        ///
        /// <b>The setting cannot go below it,</b> because lowering a limit is a different feature from raising
        /// one: bills already on a bench are never removed by this, so a cap under what a bench already holds
        /// would only produce a disabled Add button and a number that reads as an error. Vanilla's fifteen is the
        /// lowest value that is certainly safe, since it is what the game shipped with.
        /// </summary>
        internal const int Floor = 15;

        /// <summary>
        /// The highest the slider goes.
        ///
        /// Well past anybody's real use. It also stays inside a signed byte, which matters only as a reminder
        /// that the old transform loaded this as a constant and this one does not have to.
        /// </summary>
        internal const int Ceiling = 120;

        /// <summary>
        /// What the game shipped with plus room to work, and the value a settings file that has never heard of
        /// this option is read as.
        ///
        /// Sixty on Aaron's request, 2026-08-19. The previous behaviour was a hard 120 baked into the IL, which
        /// was chosen to be past anybody's use rather than as a considered number; a smelter with forty bills on
        /// it is a real thing and a hundred and twenty is not, so the default now sits between them and the
        /// slider covers the rest.
        /// </summary>
        internal const int Default = 60;

        /// <summary>
        /// The cap in force right now.
        ///
        /// <b>Called from a transpiled method,</b> so it has to be safe to call while RimWorld is drawing and it
        /// must never throw: the settings file may not have loaded yet, and an exception here would come out of
        /// the middle of <c>BillStack.DoListing</c> where nothing is expecting one. Guarded, and clamped on read
        /// rather than on write so a hand-edited file with a silly number gives a sensible cap instead of an
        /// error.
        /// </summary>
        internal static int Current
        {
            get
            {
                return UIGuard.Try("Bills.ReadCap",
                    () => Mathf.Clamp(UIOverhaulSettingsFile.Current?.maxBillsPerBench ?? Default, Floor, Ceiling),
                    Default, null);
            }
        }
    }
}
