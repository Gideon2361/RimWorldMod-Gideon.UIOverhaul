using System.Collections.Generic;
using System.Text;

namespace Gideon.UIOverhaul.Features.Pawns.Templates
{
    /// <summary>
    /// What actually happened when a template was applied, so the message shown afterwards can say so.
    ///
    /// <b>Why applying reports rather than just succeeding.</b> Every part of a template can partly not apply for
    /// a reason that is nobody's fault: a work type this pawn is incapable of, a policy renamed since the template
    /// was saved, a work type from a mod that has since been switched off. None of those is an error, and none
    /// should be silent either -- a template that quietly did four fifths of its job looks like it worked, and the
    /// missing fifth gets found much later.
    /// </summary>
    public class PawnTemplateApplyResult
    {
        /// <summary>Which parts of the template were in scope and were written.</summary>
        public PawnTemplateScope applied;

        /// <summary>
        /// Work types the template asked for and the pawn cannot do. Left at zero rather than written, since
        /// SetPriority logs an error for an incapable pawn.
        /// </summary>
        public int incapableWorkTypes;

        /// <summary>
        /// Work types the template named that no longer exist, which is what a removed mod looks like from here.
        /// </summary>
        public int unknownWorkTypes;

        /// <summary>Things named by label that could not be found: policies, and time assignments.</summary>
        public readonly List<string> unresolved = new List<string>();

        public bool AnythingToReport =>
            incapableWorkTypes > 0 || unknownWorkTypes > 0 || unresolved.Count > 0;

        /// <summary>
        /// A sentence for the message shown after applying, or null when everything landed.
        ///
        /// Phrased as what was left alone rather than what failed, because that is what it is: the pawn keeps
        /// whatever they had for those parts.
        /// </summary>
        public string Describe(string pawnLabel)
        {
            if (!AnythingToReport)
                return null;

            StringBuilder text = new StringBuilder();

            if (incapableWorkTypes > 0)
            {
                text.Append(incapableWorkTypes)
                    .Append(incapableWorkTypes == 1 ? " work type was" : " work types were")
                    .Append(" left off; ").Append(pawnLabel).Append(" cannot do ")
                    .Append(incapableWorkTypes == 1 ? "it." : "them.");
            }

            if (unknownWorkTypes > 0)
            {
                if (text.Length > 0)
                    text.Append(' ');

                text.Append(unknownWorkTypes)
                    .Append(unknownWorkTypes == 1 ? " work type in the template no longer exists"
                        : " work types in the template no longer exist")
                    .Append(", so ").Append(unknownWorkTypes == 1 ? "it was" : "they were").Append(" skipped.");
            }

            if (unresolved.Count > 0)
            {
                if (text.Length > 0)
                    text.Append(' ');

                text.Append("Could not find ").Append(string.Join(", ", unresolved.ToArray()))
                    .Append(", so ").Append(unresolved.Count == 1 ? "it was" : "they were").Append(" left as ")
                    .Append(pawnLabel).Append(" had ").Append(unresolved.Count == 1 ? "it." : "them.");
            }

            return text.ToString();
        }
    }
}
