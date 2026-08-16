namespace Gideon.UIOverhaul.Features.Tabs
{
    /// <summary>
    /// Implemented by a tab that opens a side panel, to say how much width that panel needs on top of whatever
    /// size the player has dragged the tab to.
    ///
    /// <b>The problem this solves.</b> A resized tab is restored to its stored width every time it opens, and
    /// that width was chosen while looking at the tab's main content. A panel that appears later -- the pawns
    /// tab's work priorities, opened by unfolding a row -- has to come from somewhere, and without this it
    /// comes out of the content: the columns get squeezed, a horizontal scrollbar appears under a table that
    /// had been fitting perfectly, and the row you clicked slides sideways to make room for the panel
    /// describing it.
    ///
    /// <b>So the reservation is added to the stored width rather than taken out of it.</b> The player's size
    /// keeps meaning what it meant when they chose it, which is how much room the content gets.
    ///
    /// <b>And it is subtracted again before storing.</b> A resize made while the panel is open would otherwise
    /// bake the panel's width into the stored size, which would be added again the next time the panel opened,
    /// and again after that. That is not a hypothetical: a tab that grew a little every time it was opened was
    /// a real fault in this feature once already, and this is the same shape of mistake.
    /// </summary>
    internal interface IUITabWidthReservation
    {
        /// <summary>
        /// Width the tab's side panel needs right now, or zero when it is closed.
        ///
        /// Read every frame, so it must be cheap and must not allocate.
        /// </summary>
        float ReservedWidth { get; }
    }
}
