using System;

namespace Gideon.UIOverhaul.Features.Pawns.Templates
{
    /// <summary>
    /// Which parts of a pawn a template speaks for.
    ///
    /// <b>Flags, and the reason matters to how templates behave.</b> The edit tools appear in three places and
    /// each has a different reach: the tools on a pawn's card template the whole pawn, the ones above the work
    /// priorities pane template priorities alone, and the ones on the schedule strip template the schedule alone.
    /// Those are not three separate features, they are one template type with three scopes, which is why this is
    /// a set of flags rather than three classes.
    ///
    /// <b>Scope is recorded, not inferred.</b> A template could be read as covering whatever it happens to carry
    /// data for, and that would be wrong in a way worth avoiding: a whole-pawn template taken from a colonist with
    /// an empty schedule carries no schedule entries, and inferring scope would quietly turn it into a
    /// priorities-only template. Saying what a template is for is separate from saying what it contains.
    ///
    /// It also decides what a template offers to apply. A schedule template applied to a pawn must not touch
    /// their work priorities even if it happens to have some recorded from an earlier version of itself.
    /// </summary>
    [Flags]
    public enum PawnTemplateScope
    {
        None = 0,

        /// <summary>Work priorities, one per WorkTypeDef.</summary>
        Priorities = 1,

        /// <summary>The 24 hour timetable.</summary>
        Schedule = 2,

        /// <summary>
        /// The assign tab's policies: apparel, drug, food and reading, plus medical care, hostility response and
        /// self-tend.
        /// </summary>
        Policies = 4,

        /// <summary>What the tools on a pawn's card capture.</summary>
        Everything = Priorities | Schedule | Policies
    }
}
