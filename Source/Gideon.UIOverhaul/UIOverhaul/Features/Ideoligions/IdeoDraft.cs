using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Ideoligions
{
    /// <summary>
    /// The ideoligion being edited, before anything is committed to the colony.
    ///
    /// <b>The scratch copy is vanilla's own arrangement, not an invention.</b>
    /// <c>Dialog_ReformIdeo</c> makes an empty ideoligion of the same foundation and copies the real one into it,
    /// edits that, and hands both to <c>IdeoDevelopmentUtility</c> at the end. Everything here does the same,
    /// which is what keeps the designer out of the business of writing a colony's faith: the two calls that
    /// actually change anything are RimWorld's, and they are made from one place.
    ///
    /// <b>What this class exists for is the sequence, not the writing.</b> Changing a meme is not adding a def to
    /// a list -- the precepts have to be reconciled against the new meme set afterwards, and getting that order
    /// wrong is the one way this feature can leave a faith in a state the game did not intend. So the sequence
    /// lives here, once, rather than at each button that changes a meme.
    ///
    /// <b>Preserve doctrine costs nothing to honor here,</b> because the designer owns its own orchestration:
    /// the switch simply means "do not call <c>EnsurePreceptsCompatibleWithMemes</c>", with nothing patched.
    /// World generation is the other half of the same feature and cannot be reached this way, since the meme
    /// picker there is vanilla's own window; that half lives in <see cref="PreserveDoctrine"/> and carries the
    /// condition this path does not need -- that there is already a doctrine to keep.
    /// </summary>
    internal class IdeoDraft
    {
        /// <summary>The faith as it stands. Never edited: it is the left-hand side of every comparison.</summary>
        internal readonly Ideo original;

        /// <summary>The faith as it would be. Everything the designer edits is on this one.</summary>
        internal readonly Ideo draft;

        /// <summary>The memes the draft started with, for the diff and for the reconcile.</summary>
        internal readonly List<MemeDef> startingMemes = new List<MemeDef>();

        private IdeoDraft(Ideo original, Ideo draft)
        {
            this.original = original;
            this.draft = draft;

            startingMemes.AddRange(original.memes);
        }

        /// <summary>
        /// Opens a draft of one faith, or null when one cannot be made.
        ///
        /// Null rather than a half-built draft, because every caller of this opens a window with it and a window
        /// holding a broken draft is worse than a window that did not open.
        /// </summary>
        internal static IdeoDraft Of(Ideo ideo)
        {
            return UIGuard.Try("Ideoligions.Draft", () =>
            {
                if (ideo?.foundation?.def == null)
                    return null;

                Ideo copy = IdeoGenerator.MakeIdeo(ideo.foundation.def);

                if (copy == null)
                    return null;

                ideo.CopyTo(copy);

                return new IdeoDraft(ideo, copy);
            }, null, "The ideoligion designer did not open. Nothing about the faith has been changed.");
        }

        /// <summary>Whether the doctrine is being kept across meme changes.</summary>
        internal static bool Preserve
        {
            get { return UIOverhaulSettingsFile.Current != null && UIOverhaulSettingsFile.Current.preservePrecepts; }
        }

        // -------------------------------------------------------------------------------------------
        // Memes
        // -------------------------------------------------------------------------------------------

        /// <summary>The draft's normal memes, which are the ones a reform may add and remove.</summary>
        internal List<MemeDef> NormalMemes()
        {
            List<MemeDef> memes = new List<MemeDef>();

            for (int i = 0; i < draft.memes.Count; i++)
            {
                if (draft.memes[i] != null && draft.memes[i].category == MemeCategory.Normal)
                    memes.Add(draft.memes[i]);
            }

            return memes;
        }

        /// <summary>
        /// Why this meme cannot be taken, or null when it can.
        ///
        /// <b>Shown greyed with its reason rather than hidden,</b> which is the mockup's rule and a real one: a
        /// wall you can see before you walk into it is a different experience from a refusal after the fact,
        /// which is what vanilla gives you.
        ///
        /// The exclusion test is the game's own tag matching. Nothing here names a meme.
        /// </summary>
        internal string Blocked(MemeDef meme)
        {
            if (meme == null)
                return "unknown";

            if (draft.memes.Contains(meme))
                return null;

            // RimWorld allows a fluid reform exactly one of: a new structure, a changed meme set, or changed
            // styles. Vanilla enforces it by greying the other two boxes once you have used your change, and it
            // is enforced here for the same reason -- our designer owning the drawing does not make it ours to
            // decide how much a reform may do.
            if (meme.category == MemeCategory.Normal && StructureChanged)
                return "structure already changed";

            if (meme.category == MemeCategory.Structure && NormalMemesChanged)
                return "memes already changed";

            MemeDef clash = Clash(meme);

            if (clash != null)
                return "excluded by " + clash.LabelCap;

            if (meme.category == MemeCategory.Normal
                && NormalMemes().Count >= IdeoFoundation.MemeCountRangeAbsolute.max - 1)
                return "no room left";

            return null;
        }

        /// <summary>Whether the structure meme has been swapped for another.</summary>
        internal bool StructureChanged
        {
            get { return draft.StructureMeme != original.StructureMeme; }
        }

        /// <summary>Whether the set of normal memes differs from the one the draft opened with.</summary>
        internal bool NormalMemesChanged
        {
            get
            {
                List<MemeDef> was = new List<MemeDef>();

                for (int i = 0; i < startingMemes.Count; i++)
                {
                    if (startingMemes[i] != null && startingMemes[i].category == MemeCategory.Normal)
                        was.Add(startingMemes[i]);
                }

                return !NormalMemes().SetsEqual(was);
            }
        }

        /// <summary>The first meme already taken that this one cannot sit beside.</summary>
        private MemeDef Clash(MemeDef meme)
        {
            if (meme.exclusionTags.NullOrEmpty())
                return null;

            for (int i = 0; i < draft.memes.Count; i++)
            {
                MemeDef held = draft.memes[i];

                if (held == null || held == meme || held.exclusionTags.NullOrEmpty())
                    continue;

                for (int t = 0; t < meme.exclusionTags.Count; t++)
                {
                    if (held.exclusionTags.Contains(meme.exclusionTags[t]))
                        return held;
                }
            }

            return null;
        }

        /// <summary>Takes or drops a normal meme, then reconciles the doctrine.</summary>
        internal void ToggleMeme(MemeDef meme)
        {
            UIGuard.Try("Ideoligions.ToggleMeme", () =>
            {
                if (meme == null || meme.category != MemeCategory.Normal)
                    return;

                Change(() =>
                {
                    if (draft.memes.Contains(meme))
                        draft.memes.Remove(meme);
                    else if (Blocked(meme) == null)
                        draft.memes.Add(meme);
                });
            }, "That meme was not changed.");
        }

        /// <summary>Swaps the structure meme, which every faith has exactly one of.</summary>
        internal void SetStructure(MemeDef meme)
        {
            UIGuard.Try("Ideoligions.SetStructure", () =>
            {
                if (meme == null || meme.category != MemeCategory.Structure || draft.StructureMeme == meme)
                    return;

                if (Blocked(meme) != null)
                    return;

                Change(() =>
                {
                    for (int i = draft.memes.Count - 1; i >= 0; i--)
                    {
                        if (draft.memes[i] != null && draft.memes[i].category == MemeCategory.Structure)
                            draft.memes.RemoveAt(i);
                    }

                    draft.memes.Add(meme);
                });
            }, "The structure meme was not changed.");
        }

        /// <summary>
        /// Runs one meme edit and puts the faith back in a consistent state afterwards.
        ///
        /// <b>This is the sequence <c>Dialog_ChooseMemes</c> runs for a fluid ideoligion being reformed,</b> and
        /// it is deliberately the short one: the reform path never calls <c>RandomizePrecepts</c>. That method
        /// belongs to founding, where there is no doctrine to keep, and calling it here would throw away a
        /// carefully built faith every time a meme was toggled.
        ///
        /// <b>With the doctrine preserved, one call is skipped and nothing replaces it.</b>
        /// <c>EnsurePreceptsCompatibleWithMemes</c> is what drops the precepts the new meme set forbids and adds
        /// the ones it demands; not calling it means the doctrine is exactly what the player built, which is the
        /// whole point of the switch. <c>RecachePrecepts</c> still runs either way, because that only rebuilds
        /// the lookups the game reads the precepts through and skipping it would leave stale caches rather than
        /// preserved doctrine.
        /// </summary>
        private void Change(System.Action edit)
        {
            List<MemeDef> before = new List<MemeDef>(draft.memes);

            edit();

            draft.SortMemesInDisplayOrder();

            if (!Preserve)
            {
                FactionDef faction = IdeoUIUtility.FactionForRandomization(draft);

                draft.foundation.EnsurePreceptsCompatibleWithMemes(before, draft.memes,
                    new IdeoGenerationParms(faction));
            }

            draft.RecachePrecepts();
        }

        // -------------------------------------------------------------------------------------------
        // What the change costs
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The precepts a commit would drop, which is empty while the doctrine is being preserved.
        ///
        /// <b>Asked for the review screen rather than for a confirmation box.</b> Vanilla computes exactly this
        /// list and spends it on a yes/no dialog that appears after you have already made the change; here it is
        /// on the screen beside the choice, before anything is committed.
        /// </summary>
        internal List<Precept> Losing()
        {
            List<Precept> losing = new List<Precept>();

            if (Preserve)
                return losing;

            UIGuard.Try("Ideoligions.Losing", () =>
            {
                foreach (Precept precept in draft.foundation.GetPreceptsToRemoveFromMemeChanges(startingMemes,
                             draft.memes))
                {
                    if (precept?.def != null && precept.def.visible)
                        losing.Add(precept);
                }
            }, null);

            return losing;
        }

        /// <summary>
        /// Two precepts the draft holds that contradict each other, if there are any.
        ///
        /// <b>This is the check preserving the doctrine makes worth showing.</b> Vanilla reconciles the precepts
        /// against the memes and so rarely produces a contradiction; keeping them means the player can, and the
        /// honest thing is to say so on the review screen rather than let them find out afterwards. RimWorld
        /// itself only warns and still allows the commit, and this does the same -- it is the player's faith.
        /// </summary>
        internal Pair<Precept, Precept> Contradiction()
        {
            return UIGuard.Try("Ideoligions.Contradiction", () => draft.FirstIncompatiblePreceptPair(),
                default(Pair<Precept, Precept>), null);
        }

        /// <summary>Whether anything at all has been changed.</summary>
        internal bool Changed()
        {
            return UIGuard.Try("Ideoligions.Changed", () =>
            {
                if (!draft.memes.SetsEqual(startingMemes))
                    return true;

                if (draft.name != original.name || draft.adjective != original.adjective
                    || draft.memberName != original.memberName || draft.description != original.description)
                    return true;

                return draft.PreceptsListForReading.Count != original.PreceptsListForReading.Count;
            }, false, null);
        }

        // -------------------------------------------------------------------------------------------
        // Commit
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Hands the draft to RimWorld to be made real.
        ///
        /// <b>Both calls are the game's own and neither is reimplemented.</b> <c>ConfirmChangesToIdeo</c> puts up
        /// the price -- the development points, and the certainty every believer loses for the reform -- and
        /// calls back only if the player agrees; <c>ApplyChangesToIdeo</c> does the rest, including the parts
        /// that would be genuinely dangerous to write again from the outside.
        /// </summary>
        internal void Commit(System.Action done)
        {
            UIGuard.Try("Ideoligions.Commit", () =>
            {
                IdeoDevelopmentUtility.ConfirmChangesToIdeo(original, draft, () =>
                {
                    UIGuard.Try("Ideoligions.Apply", () =>
                    {
                        IdeoDevelopmentUtility.ApplyChangesToIdeo(original, draft);

                        if (done != null)
                            done();
                    }, "The reform was not applied. The faith is unchanged.");
                });
            }, "The reform was not applied. The faith is unchanged.");
        }
    }
}
