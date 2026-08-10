using Gideon.UIFramework.Defs;

namespace Gideon.UIFramework.Components.Colors
{
    /// <summary>
    /// The color slots a <see cref="UIColorPaletteDef"/> fills.
    ///
    /// Roles are named for the job the color does, not for the color it happens to be, so a light
    /// template can supply a dark <see cref="TextPrimary"/> without the name becoming a lie. A
    /// control asks for a role and gets whatever the active palette decided that role should look
    /// like; nothing in the framework should hardcode a color value.
    ///
    /// Adding a role is a compile-time change to the framework, and every palette gets the new role's
    /// built-in default until its XML names it. Adding a color that only one mod cares about does not
    /// need a role at all -- use the palette's named custom colors instead. See the Help folder.
    /// </summary>
    public enum UIColorRole
    {
        /// <summary>The base fill behind a whole window or tab.</summary>
        WindowBackground,

        /// <summary>Fill for a panel, card or row sitting on <see cref="WindowBackground"/>.</summary>
        PanelBackground,

        /// <summary>
        /// A surface standing above the panel: button fills, header strips, divider lines. Lighter
        /// than the panel in a dark template, darker in a light one.
        /// </summary>
        SurfaceRaised,

        /// <summary>
        /// A surface cut into the panel: text field interiors, progress bar troughs, window chrome.
        /// The counterpart to <see cref="SurfaceRaised"/>.
        /// </summary>
        SurfaceSunken,

        /// <summary>Ordinary one-pixel border around a control at rest.</summary>
        Border,

        /// <summary>Border for a control that has focus or is otherwise active.</summary>
        BorderFocused,

        /// <summary>Body and label text.</summary>
        TextPrimary,

        /// <summary>Supporting text: subtitles, units, field labels.</summary>
        TextSecondary,

        /// <summary>Text for something that cannot currently be used.</summary>
        TextDisabled,

        /// <summary>
        /// The palette's identity color. Selection, focus, links, and the fill of a primary button.
        /// </summary>
        Accent,

        /// <summary>
        /// A dimmed <see cref="Accent"/>, for primary button fills and field borders that would
        /// overpower at full strength.
        /// </summary>
        AccentMuted,

        /// <summary>Something worked, is complete, or is within its healthy range.</summary>
        Success,

        /// <summary>Something needs attention but is not broken.</summary>
        Warning,

        /// <summary>Something failed, is forbidden, or is out of range.</summary>
        Danger,

        /// <summary>Neutral information, and the cold end of any hot/cold scale.</summary>
        Info,

        /// <summary>
        /// Translucent wash laid over a control the cursor is on. Alpha is part of the value: these
        /// three roles are drawn on top of whatever is already there, not instead of it.
        /// </summary>
        HoverOverlay,

        /// <summary>Translucent wash for a control being pressed.</summary>
        PressedOverlay,

        /// <summary>Translucent wash marking the selected row or card.</summary>
        SelectionOverlay
    }
}
