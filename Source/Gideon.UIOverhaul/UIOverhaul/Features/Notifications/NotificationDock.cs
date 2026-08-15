namespace Gideon.UIOverhaul.Features.Notifications
{
    /// <summary>
    /// Where a notification surface sits on screen.
    ///
    /// <b>Three corners, and the missing one is missing on purpose.</b> The bottom left is where RimWorld puts the
    /// inspect pane and the architect menu -- the two panels a player has open most of the time -- so a dock there
    /// would be a choice that looks fine on an empty map and buries itself the moment anything is selected.
    /// Offering it would be offering a trap.
    ///
    /// The top left keeps vanilla's own inset from the edge, which is there to clear the resource readout rather
    /// than for looks.
    /// </summary>
    public enum NotificationDock
    {
        TopLeft,
        TopRight,
        BottomRight
    }

    /// <summary>
    /// Which surface is asking for space.
    ///
    /// The order matters and is the stacking order within a dock: the lower value sits nearer the screen edge it
    /// docks against. Letters first and alerts above them reproduces where vanilla puts the two, which is the one
    /// arrangement a player already has muscle memory for.
    /// </summary>
    public enum NotificationSurface
    {
        Letters = 0,
        Alerts = 1,
        Messages = 2
    }
}
