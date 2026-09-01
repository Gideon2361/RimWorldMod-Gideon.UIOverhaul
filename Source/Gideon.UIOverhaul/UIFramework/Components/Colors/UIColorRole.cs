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
        /// The Growing Zones tab's own color, the fifth of the per-tab identities. A muted wheat,
        /// defaulting to <c>#C0AE6A</c>.
        ///
        /// <b>Not the green a grower would reach for first.</b> That hue is taken twice over: <c>success</c>
        /// is what a healthy temperature reads as inside this very tab, and the sage of
        /// <see cref="TabAnimals"/> sits next door in the tab strip. Wheat is what the zone is for rather
        /// than what it is made of, and it is far enough from both to be told apart at a glance.
        ///
        /// It shares a hue with <see cref="Warning"/> and is separated by saturation, the same way every tab
        /// color here is: a state shouts and an identity does not.
        /// </summary>
        TabGrowing,
        /// The Bills tab's own color, the sixth of the per-tab identities.
        ///
        /// <b>A warm clay, and the first identity that is not a cool hue.</b> The others are violet, steel
        /// blue, sage and teal, with the growing tab on a yellow green, so a sixth cold color would have had
        /// nowhere to stand. It is kept clear of <see cref="Warning"/>, which matters more on this screen
        /// than most: amber is a state bills show often.
        /// </summary>
        TabBills,

        /// <summary>
        /// The Pawns tab's own color, the seventh of the per-tab identities. A dusty rose, defaulting to
        /// <c>#C98BA4</c>.
        ///
        /// <b>The magenta side, because nothing else in the mod is there.</b> This one tab spends six colors
        /// on its own categories before an identity is even asked for: <see cref="Accent"/> on colonists,
        /// <see cref="Warning"/> on prisoners, <see cref="Mood"/> on slaves, <see cref="Danger"/> on patients,
        /// <see cref="Success"/> on guests and <see cref="Info"/> on the undead. Add the schedule strip's five
        /// and the six identities above and the wheel is full everywhere but here.
        ///
        /// Its one near neighbor is <see cref="Danger"/>, and it is separated the way every identity here is
        /// separated from a state: by saturation, around forty percent against the red's eighty.
        ///
        /// <b>It never touches a row.</b> The category colors say who someone is; this says which tab you are
        /// on. It is the mark, the title, the chosen map, the open row's wash and the work grid's edges, and
        /// nothing that belongs to a person.
        /// </summary>
        TabPawns,

        /// <summary>
        /// The Hospital tab's own color, the eighth of the per-tab identities. A muted orchid, defaulting to
        /// <c>#CC8BC7</c>.
        ///
        /// <b>Not the rose a hospital would have taken.</b> That was the intent, and <see cref="TabPawns"/>
        /// reached the magenta side first at <c>#C98BA4</c>. Two identities thirty degrees apart, on the two
        /// tabs that both list the same people, is a distinction nobody could make.
        ///
        /// <b>Not red either, which is the other obvious answer.</b> Red is <see cref="Danger"/>, and this is
        /// the one screen where danger genuinely appears in the rows: a title in the alarm color over a
        /// condition in the alarm color is the mistake the bills tab made with amber.
        ///
        /// <b>What is left is the arc between the violet and the rose,</b> and it is the only arc no state
        /// occupies. Its neighbours are <see cref="TabTheDead"/>, <see cref="TabPawns"/> and
        /// <see cref="Mood"/>, each about thirty-five degrees away, which is as much room as an eighth
        /// identity can be given.
        /// </summary>
        TabHospital,

        /// <summary>
        /// The Research tab's own color, the ninth of the per-tab identities. A muted iris, defaulting to
        /// <c>#8B90CC</c>.
        ///
        /// <b>This tab picks last and from the fullest ring.</b> The eight above it sit at 18, 44, 85, 172,
        /// 209, 268, 305 and 336 degrees, and the canvas underneath spends twelve more on its theme bands. A
        /// ninth identity has to land in the same 26 to 45 percent saturation band as the rest or it reads as
        /// a different kind of thing rather than as one of a set.
        ///
        /// <b>Not the green the geometry points at.</b> The widest hole left is 85 to 172, and its center is
        /// a jade around 130. <see cref="Success"/> is at 120, and this is the tab that spends success green
        /// on the word "Done", on every finished node and on a wash across the whole card. A jade title over a
        /// green Done column is the mistake <see cref="TabPower"/> avoided with amber, at half the separation.
        ///
        /// <b>So the second gap takes it: 235 degrees, between <see cref="TabQuests"/> at 209 and
        /// <see cref="TabTheDead"/> at 268.</b> Twenty-six and thirty-three degrees of clearance, which is no
        /// tighter than <see cref="TabBills"/> and <see cref="TabGrowing"/> already live at.
        ///
        /// <b>The one collision is a band, not a tab.</b> Flight and Space sits at 224, eleven degrees off,
        /// and the two are separated the way <see cref="TabGrowing"/> is separated from
        /// <see cref="Warning"/>: by saturation, 32 percent against 54, and by territory. A band color only
        /// ever draws on the canvas and this one only ever draws off it, in the header, the segment underline
        /// and the two rail selections, so the two are never in the same hundred pixels.
        /// </summary>
        TabResearch,

        /// <summary>
        /// The Factions tab's own color, the tenth of the per-tab identities. A sea green, defaulting to
        /// <c>#65B486</c>.
        ///
        /// <b>This one takes the hole <see cref="TabResearch"/> turned down, and it can because it moved the
        /// band out of the way first.</b> The nine identities above sit at 18, 44, 85, 172, 209, 235, 268,
        /// 305 and 336 degrees. The only stretch left wider than 37 is 85 to 172, and the reason two tabs in
        /// a row refused it is <see cref="Success"/> at 120: research spends success green on the word
        /// "Done", and this tab spends it on the word "Ally".
        ///
        /// <b>The refusal was about adjacency, not about hue.</b> An identity is separated from a state by
        /// saturation in this palette -- <see cref="TabGrowing"/> is two degrees from <see cref="Warning"/>
        /// and reads as a different thing entirely -- so green was never disqualified on its own. What
        /// disqualified it was that the factions scale drew its resting band in the identity, which would
        /// have put a green band under a green pin inside a green zone on an allied faction's row. The band
        /// is grey now, the identity never touches a row, and the objection goes with it.
        ///
        /// <b>So 145 degrees, at the far side of success from the teal.</b> Twenty five degrees from
        /// <see cref="Success"/> with fifteen points less saturation, and twenty seven from
        /// <see cref="TabPower"/>, which is wider clearance than <see cref="TabBills"/> and
        /// <see cref="TabGrowing"/> have from each other. Every other opening left is under twenty degrees
        /// from two identities at once, and the purple arc already holds three.
        /// </summary>
        TabFactions,

        /// <summary>
        /// The History tab's own color, the eleventh of the per-tab identities. A muted cyan, defaulting to
        /// <c>#72B3C0</c>.
        ///
        /// <b>This was drawn at 145 and moved, because <see cref="TabFactions"/> got there first.</b> Two
        /// tabs reached that arc in the same week by the same argument, independently, which is the clearest
        /// evidence yet both that the argument was right and that the ring has run out of good answers.
        /// Factions had shipped; this moved.
        ///
        /// <b>Which leaves 190, and it is a tighter fit than anything above it.</b> Eighteen degrees from
        /// <see cref="TabPower"/> at 172 and nineteen from <see cref="TabQuests"/> at 209, against the twenty
        /// five and twenty seven Factions kept. That is the honest cost, and it is paid rather than dodged
        /// because every alternative is worse: the purple arc at 286 is nineteen degrees from
        /// <see cref="Mood"/>, which this tab plots as an entire graph, and the citron at 64 is eighteen from
        /// <see cref="Warning"/> with <see cref="TabGrowing"/> already sitting on that hue.
        ///
        /// <b>What makes 190 survivable is saturation and the plot.</b> It sits at 38 percent against the
        /// power teal's 27, and no two identities are ever on screen together. More to the point, what this
        /// tab actually shows is a gold ramp: the four chart series run 40 to 45 degrees, so a cool identity is
        /// as far from the data as the wheel allows -- which is the separation that matters most on a screen
        /// whose entire content is colored quantities.
        ///
        /// <b>It never draws inside the axes.</b> The series own the plot; this draws only on the chrome
        /// around it -- the header mark and title, the range segments, the rail selection and the open row.
        /// The same rule <see cref="TabResearch"/> keeps from its band colors.
        ///
        /// <b>The ring is full at eleven.</b> A twelfth identity cannot be placed more than eighteen degrees
        /// from its neighbours anywhere, so the next tab either shares an arc on purpose or this system needs
        /// a second channel to tell tabs apart by.
        /// </summary>
        TabHistory,

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
