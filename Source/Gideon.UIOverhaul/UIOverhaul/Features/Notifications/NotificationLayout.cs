using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Notifications
{
    /// <summary>
    /// Decides where each notification surface starts drawing, so three of them can share a corner without
    /// knowing about each other.
    ///
    /// <b>This exists to break one dependency.</b> Vanilla stacks alerts by reading
    /// <c>Find.LetterStack.LastTopY</c> -- the letter stack publishes where it stopped, and the alerts readout
    /// begins there. That is a direct coupling between two surfaces, and it is exactly what makes moving either of
    /// them impossible: letters docked at the top left would leave the alerts column anchored to a number that no
    /// longer describes anything on screen. Both surfaces ask this instead, and neither has to know the other
    /// exists.
    ///
    /// <b>Reservations are recorded, not accumulated.</b> The obvious implementation is a cursor per dock that
    /// each surface advances as it draws, and it is wrong twice over. Unity calls OnGUI several times per frame --
    /// a layout pass, a repaint, one per input event -- all with the same frame count, so a cursor would keep
    /// climbing within a single frame and the second pass would draw everything somewhere else. And the surfaces
    /// do not draw in a fixed order: messages are drawn by <c>UIRoot</c> before the map interface, letters from
    /// inside the corner panel, alerts after both. So each surface records the height it used, and its start is
    /// computed from the recorded heights of the surfaces that stack below it. Passes are then idempotent and the
    /// call order stops mattering.
    ///
    /// <b>A surface reads last frame's heights for anything drawn after it.</b> Messages ask before letters have
    /// drawn this frame, so what they get is the letters' height from the previous one. A frame of lag is
    /// invisible on a stack that changes when a letter arrives; the alternative is ordering constraints between
    /// three patches in three different parts of the frame.
    ///
    /// <b>Nothing here draws.</b> It answers questions about rectangles. Every surface keeps its own drawing, its
    /// own behavior and its own guarding.
    /// </summary>
    internal static class NotificationLayout
    {
        /// <summary>How far a docked column sits from the top of the screen. Vanilla's own message inset.</summary>
        private const float TopInset = 16f;

        /// <summary>
        /// How far the top left column sits from the left edge.
        ///
        /// Vanilla's number, and it is not decoration: the resource readout occupies the top left corner, and 140
        /// is what clears it. Taken from <c>Messages.MessagesTopLeftStandard</c> rather than written out, so it
        /// follows if the game ever moves it.
        /// </summary>
        private static float LeftInset => Messages.MessagesTopLeftStandard.x;

        /// <summary>Gap between two surfaces sharing a dock, so their cards do not read as one column.</summary>
        private const float SurfaceGap = 6f;

        /// <summary>
        /// What one surface used, and when.
        ///
        /// The frame stamp is what lets a surface that stopped drawing -- the last message expired, the last alert
        /// cleared -- give its space back instead of holding it until something else happened.
        /// </summary>
        private struct Reservation
        {
            public NotificationDock Dock;
            public float Height;
            public int Frame;
        }

        private static readonly Reservation[] reservations = new Reservation[3];

        /// <summary>
        /// Where the bottom right column has to stop, which is the top of this mod's corner panel.
        ///
        /// Reported by the panel each frame rather than computed here, because its height depends on which
        /// readouts are switched on, how many game conditions are running, and how many rows of play setting
        /// toggles other mods have added to it. Only the panel knows, and only after it has drawn.
        /// </summary>
        private static float cornerTop;

        private static int cornerFrame = -1;

        /// <summary>Tells the layout where the corner panel ended this frame.</summary>
        internal static void Notify_CornerTop(float y)
        {
            cornerTop = y;
            cornerFrame = Time.frameCount;
        }

        /// <summary>
        /// The top of this mod's corner panel, for anything that wants to sit above the widgets.
        ///
        /// <b>Exposed rather than left private because the minimap needs the same answer the letters do.</b>
        /// The panel's height depends on which readouts are on, how many conditions are running and what other
        /// mods have added to it, so it can only be reported after it draws. Anything guessing at a number here
        /// would sit on top of the widgets the first time somebody switched one on.
        ///
        /// Falls back the same way <see cref="BaseOf"/> does, so a frame where the panel has not reported --
        /// it retires itself on its first failure, after which vanilla draws the corner and nothing reports at
        /// all -- gives a usable anchor rather than zero.
        /// </summary>
        internal static float BottomRightTop => BaseOf(NotificationDock.BottomRight);

        /// <summary>
        /// The screen edge a surface docked here grows away from, before anything else is stacked against it.
        ///
        /// The bottom right falls back to a computed anchor when the corner panel has not reported one for two
        /// frames. That is not hypothetical: the panel retires itself on its first failure, after which vanilla
        /// draws the corner and nothing reports anything. Letters would otherwise pile up at whatever height the
        /// panel happened to end at when it broke.
        /// </summary>
        private static float BaseOf(NotificationDock dock)
        {
            if (dock != NotificationDock.BottomRight)
                return TopInset;

            if (cornerFrame >= Time.frameCount - 1)
                return cornerTop;

            return UI.screenHeight - MainButtonDef.ButtonHeight - 4f - 200f;
        }

        /// <summary>Whether a dock stacks upward from the bottom of the screen rather than down from the top.</summary>
        internal static bool GrowsUp(NotificationDock dock) => dock == NotificationDock.BottomRight;

        /// <summary>The left edge of a column of the given width at this dock.</summary>
        internal static float ColumnX(NotificationDock dock, float width)
        {
            return dock == NotificationDock.TopLeft ? LeftInset : UI.screenWidth - width;
        }

        /// <summary>
        /// Where <paramref name="surface"/> should begin: the edge of its own slot, past whatever stacks below it.
        ///
        /// For an upward dock this is the bottom of the surface's slot and heights are subtracted from it; for a
        /// downward dock it is the top and heights are added.
        /// </summary>
        internal static float Anchor(NotificationSurface surface, NotificationDock dock)
        {
            float offset = OffsetOf(surface, dock);

            return GrowsUp(dock) ? BaseOf(dock) - offset : BaseOf(dock) + offset;
        }

        /// <summary>
        /// How much room is left for <paramref name="surface"/> at this dock before it runs off the screen.
        ///
        /// Used by the letter stack to decide how many letters it can show before the rest have to be bundled.
        /// Never negative, so a caller dividing by a row height cannot get a nonsensical count.
        /// </summary>
        internal static float Room(NotificationSurface surface, NotificationDock dock)
        {
            float anchor = Anchor(surface, dock);

            return Mathf.Max(0f, GrowsUp(dock) ? anchor : UI.screenHeight - anchor);
        }

        /// <summary>
        /// Records what a surface used, so the ones stacking above it know where to start.
        ///
        /// Called every frame a surface draws, including with a height of zero: a surface that has nothing to show
        /// still holds its dock, and reporting zero is what collapses its slot rather than leaving a gap where the
        /// last alert used to be.
        /// </summary>
        internal static void Report(NotificationSurface surface, NotificationDock dock, float height)
        {
            reservations[(int) surface] = new Reservation
            {
                Dock = dock,
                Height = Mathf.Max(0f, height),
                Frame = Time.frameCount
            };
        }

        /// <summary>
        /// What one surface used at this dock last time it drew, or zero if it is elsewhere or has stopped.
        ///
        /// For the one caller that has to know about a specific neighbor rather than about everything below it:
        /// the letter stack subtracts the alerts' height when deciding how many letters fit, the way vanilla
        /// subtracts <c>AlertsHeight</c>, but only while the two are actually sharing a corner.
        /// </summary>
        internal static float HeightOf(NotificationSurface surface, NotificationDock dock)
        {
            Reservation reservation = reservations[(int) surface];

            if (reservation.Dock != dock || reservation.Frame < Time.frameCount - 1)
                return 0f;

            return reservation.Height;
        }

        /// <summary>
        /// The combined height of every surface stacking between this one and the screen edge.
        ///
        /// A reservation older than one frame is ignored rather than trusted. A surface whose patch stood down, or
        /// which is drawn by vanilla because the player asked for that, stops reporting -- and its slot should
        /// close rather than be held open forever by a number nothing is maintaining.
        /// </summary>
        private static float OffsetOf(NotificationSurface surface, NotificationDock dock)
        {
            float offset = 0f;

            for (int i = 0; i < reservations.Length; i++)
            {
                if (i >= (int) surface)
                    continue;

                Reservation other = reservations[i];

                if (other.Dock != dock || other.Frame < Time.frameCount - 1 || other.Height <= 0f)
                    continue;

                offset += other.Height + SurfaceGap;
            }

            return offset;
        }

        /// <summary>
        /// The dock a surface is set to, or its default if the settings cannot be read.
        ///
        /// The defaults reproduce where RimWorld already puts these three, which is the only sensible starting
        /// point: installing this mod should move nothing until the player asks for it to move.
        /// </summary>
        internal static NotificationDock DockOf(NotificationSurface surface)
        {
            return UIGuard.Try("Notifications.ReadDock", () => Read(surface), Default(surface),
                "One notification surface is drawn in its usual place.");
        }

        private static NotificationDock Read(NotificationSurface surface)
        {
            Options.UIOverhaulSettingsFile settings = Options.UIOverhaulSettingsFile.Current;

            if (settings == null)
                return Default(surface);

            switch (surface)
            {
                case NotificationSurface.Letters:
                    return settings.letterDock;

                case NotificationSurface.Alerts:
                    return settings.alertDock;

                default:
                    return settings.messageDock;
            }
        }

        private static NotificationDock Default(NotificationSurface surface)
        {
            return surface == NotificationSurface.Messages
                ? NotificationDock.TopLeft
                : NotificationDock.BottomRight;
        }
    }
}
