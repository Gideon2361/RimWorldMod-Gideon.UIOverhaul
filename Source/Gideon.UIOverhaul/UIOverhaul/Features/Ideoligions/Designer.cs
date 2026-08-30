using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Ideoligions
{
    /// <summary>
    /// The one door to the screen you build a faith in.
    ///
    /// <b>A seam rather than a screen, for now.</b> The tab is the half of backlog item 5 that is built; the
    /// designer -- the six-step window with the consequence column beside every choice -- is not, and this hands
    /// the player to <c>Dialog_ReformIdeo</c> in the meantime. Everything that wants to open a designer goes
    /// through here, so when the real one lands there is one method to change rather than a search for callers.
    ///
    /// <b>Deliberately not a silent fallback.</b> The rule this mod keeps is that our screen failing must not
    /// quietly become vanilla's, because that hides the defect. This is the other thing: a screen that was never
    /// written, where vanilla's is the honest answer until ours exists.
    /// </summary>
    internal static class Designer
    {
        /// <summary>
        /// Opens the reform screen for one faith.
        ///
        /// The affordability check is the caller's -- the button is drawn disabled when there are not enough
        /// development points -- but it is asked again here, because a designer opened from anywhere else has no
        /// button to have been disabled.
        /// </summary>
        internal static void OpenReform(Ideo ideo)
        {
            UIGuard.Try("Ideoligions.OpenReform", () =>
            {
                if (ideo == null || !ideo.Fluid || Find.WindowStack == null)
                    return;

                if (ideo.development == null || !ideo.development.CanReformNow)
                    return;

                IdeoDraft draft = IdeoDraft.Of(ideo);

                if (draft == null)
                    return;

                Find.WindowStack.Add(new Dialog_IdeoDesigner(draft));
            }, "The reform screen did not open. RimWorld's own ideoligion tab still reaches it.");
        }
    }
}
