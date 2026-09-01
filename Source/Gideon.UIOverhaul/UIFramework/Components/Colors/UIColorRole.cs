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
        /// A pawn's inner state: mood, and the other readings that are about how someone feels rather than
        /// about whether something succeeded.
        ///
        /// A role of its own rather than a reuse of <see cref="Info"/> or <see cref="Accent"/>, because it has
        /// to be told apart from both at a glance -- a mood bar sitting beside a health bar must not read as
        /// another health bar, and it must not read as the accent either, since the accent means "selected".
        /// </summary>
        Mood,

        /// <summary>
        /// The Dead tab's own color: its title, its glyph and its rail selection.
        ///
        /// <b>The first of the per-tab identities.</b> Every screen in the mod titled itself in
        /// <see cref="Accent"/> until 2026-08-31, which made them consistent and made them hard to tell apart
        /// at a glance in a screenshot. A tab may now carry a color of its own.
        ///
        /// <b>It is a role rather than a constant in the tab so that themes keep working.</b> A hardcoded
        /// violet would be the one color in the interface that ignored the player's palette, and a theme
        /// built around warm greys would have this one screen sitting outside it forever. Here, a theme
        /// author who never heard of this tab still gets a sensible value from the fallback, and one who has
        /// can set it.
        ///
        /// Kept clear of <see cref="Mood"/>, which is also violet: mood means a feeling being measured, and
        /// this means nothing at all beyond "you are on this screen".
        /// </summary>
        TabTheDead,

        /// <summary>
        /// The Quests tab's own color, the second of the per-tab identities.
        ///
        /// <b>A steel blue: the same family as the accent, deliberately softer.</b> Quests were already the
        /// blue screen and re-hueing them would have cost more than it bought, so this darkens rather than
        /// moves. It sits at the same weight as <see cref="TabTheDead"/>, which is what keeps the two tabs
        /// reading as a pair of identities rather than as one styled screen and one unstyled one.
        /// </summary>
        TabQuests,

        /// <summary>
        /// The Animals tab's own color, the third of the per-tab identities.
        ///
        /// <b>A muted sage, and deliberately not <see cref="Success"/>.</b> Green is the obvious association
        /// for animals and the obvious hazard is that this tab is full of real health and state readings,
        /// which are already green when they are good. Success is saturated and bright; this is neither, so
        /// the two do not compete even sitting on the same row.
        /// </summary>
        TabAnimals,

        /// <summary>
        /// The Power tab's own color, the fourth of the per-tab identities.
        ///
        /// <b>A muted teal, kept well away from amber.</b> Amber is what that tab uses to say a grid is in
        /// trouble, and the mark beside the title used to be drawn in it, so the screen was saying its own
        /// name in the color it warns with. Teal is the far side of the wheel from that and is not near any
        /// other reading the tab makes.
        /// </summary>
        TabPower,

        /// <summary>
        /// Translucent wash laid over a control the cursor is on. Alpha is part of the value: these
        /// three roles are drawn on top of whatever is already there, not instead of it.
        /// </summary>
        HoverOverlay,

        /// <summary>Translucent wash for a control being pressed.</summary>
        PressedOverlay,

        /// <summary>Translucent wash marking the selected row or card.</summary>
        SelectionOverlay,

        /// <summary>
        /// The body of an interactive control holding no value: a toggle switch that is off, an unselected
        /// radio button, the unfilled part of a slider or progress bar.
        ///
        /// <b>Not a surface, which is why it is not one of the <see cref="SurfaceRaised"/> pair.</b> A surface is
        /// chrome the eye is meant to pass over -- a card, a header strip, a row -- so it sits close to the panel
        /// on purpose. A control has to be found before it can be read, and the two jobs pull in opposite
        /// directions. Using one color for both is what made an off switch invisible: this mod's own dark theme
        /// sets <see cref="SurfaceRaised"/> <i>darker</i> than <see cref="PanelBackground"/>, which is right for a
        /// card and leaves a switch with no visible extent at all.
        ///
        /// <b>Set it clearly apart from the panel behind it.</b> That is the whole job. It also has to stay well
        /// clear of <see cref="TextSecondary"/>, which is the knob sitting on top of it -- the knob's position is
        /// the entire signal a switch carries, so if the two converge the control stops saying anything. Roughly
        /// midway between the panel and the dimmest text is a safe place to land in either direction: a dark theme
        /// lifts, a light theme drops.
        /// </summary>
        ControlBackgroundFaded,

        /// <summary>
        /// Fill for chrome drawn over the map rather than inside a window: the corner readouts, docked messages,
        /// the colonist bar.
        ///
        /// <b>The alpha is the point of this role.</b> A window is a thing the player looks at, so it is opaque.
        /// This is chrome the player looks <i>past</i> -- it sits on playable ground, permanently -- and a solid
        /// block there is map the player has lost for as long as the game is running. Vanilla avoids the problem
        /// by drawing no panel at all, and pays for it with bare text that becomes unreadable over pale terrain.
        /// A translucent fill is how a surface can be legible and still be looked through.
        ///
        /// <b>Do not go far below the shipped value.</b> What is underneath is not a static backdrop: it scrolls,
        /// pawns move across it, and RimWorld's day and night lighting swings its brightness a long way. An alpha
        /// chosen because it looked right at dusk is one that fails at midday, and the failure is text that
        /// disappears rather than a panel that looks wrong.
        ///
        /// Pair it with an opaque <see cref="Border"/>, which is what lets the fill stay faint: a defined edge
        /// keeps the panel readable as a panel even where the fill nearly vanishes. That is the same rule as
        /// <see cref="ControlBackgroundFaded"/>, for the same reason.
        /// </summary>
        HudBackground
    }
}
