using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.ButtonBar.BarWidgets;
using Gideon.UIOverhaul.Features.ColonyBar;
using Gideon.UIOverhaul.Features.FloorLabels;
using Gideon.UIOverhaul.Features.Minimap;
using Gideon.UIOverhaul.Features.Notifications;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Options
{
    /// <summary>
    /// This mod's player-facing settings, stored as XML in RimWorld's config folder beside the game's
    /// own settings and the button bar layout.
    ///
    /// Not ModSettings. These are preferences that have to be readable before defs finish loading -- the
    /// chosen theme in particular -- and keeping them in a plain file next to the bar layout means one
    /// place to look, one format, and something a player can inspect or share.
    /// </summary>
    public class UIOverhaulSettingsFile
    {
        public const string FileName = "UIOverhaul_Settings.xml";

        /// <summary>
        /// Range the room label minimum is held to.
        ///
        /// Shared with the slider that sets it, so the control and the reader cannot drift apart -- a slider
        /// offering a value the reader clamps away is a setting that silently will not stick.
        /// </summary>
        public const int MinimumRoomCellsFloor = 4;

        public const int MinimumRoomCellsCeiling = 80;

        /// <summary>
        /// defName of the palette the player chose. Empty means the shipped default.
        /// </summary>
        public string activePalette = "";

        /// <summary>
        /// Whether this mod writes diagnostic detail to the log.
        ///
        /// Off by default, and deliberately not tied to RimWorld's own dev mode: dev mode is on for whole
        /// sessions for unrelated reasons, and this is noisy enough that it should be something asked for
        /// rather than something inherited.
        ///
        /// Pushed into <see cref="UIDebug"/>, which is what the framework's instrumentation actually reads --
        /// the framework cannot see this file, so the value has to be handed to it.
        /// </summary>
        public bool debugLogging;

        /// <summary>
        /// Whether the main menu shows what the loading screen said, as a scrollable panel down the left.
        ///
        /// Off by default: it is a diagnostic, and a wall of profiler labels across the title screen is not
        /// something to give anybody who did not ask for it.
        ///
        /// <b>This governs the panel, not the recording.</b> The log is kept whether or not this is set, so
        /// switching it on shows the load that already happened rather than requiring a restart and a
        /// reproduction. See <c>UIFramework.Stages.UILoadingLog</c>, which also explains why the framework could
        /// not read this setting even if that were wanted.
        /// </summary>
        public bool showLoadingConsole;

        /// <summary>
        /// Whether the developer palette runs irreversible actions without asking first.
        ///
        /// <b>Off by default, which means the confirmation is on.</b> Vanilla asks nothing at all: "Destroy all
        /// things" is one click away from "Set weather" and looks identical. That is a reasonable default for a
        /// menu only developers see and a poor one for the moment somebody is tired and reading quickly, so the
        /// confirmation is the default here and this is the way out of it.
        ///
        /// Reachable from the confirmation itself, through its "Always allow" button, because the person who
        /// wants this switched off is by definition looking at a dialog they did not want.
        /// </summary>
        public bool skipDevActionConfirm;


        /// <summary>
        /// Whether to force fullscreen at the display's native resolution on every launch.
        ///
        /// Off by default, and it has to be: this overrides a display preference the player set, and someone who
        /// plays windowed on purpose would find the game fighting them every launch with no obvious culprit.
        /// See <c>Features.Display.StartupFullscreen</c>.
        /// </summary>
        public bool fullscreenOnStartup;

        /// <summary>
        /// How the date widget writes the time of day.
        ///
        /// 24-hour with minutes by default. RimWorld's own readout shows the bare hour, which is a clock
        /// that cannot tell you how long is left of it; a colonist's shift, a caravan's arrival and a
        /// growing season are all read off this, and "14h" rounds away most of what makes that useful.
        /// The vanilla form is still on offer for anyone who prefers it.
        /// </summary>
        public UITimeFormat timeFormat = UITimeFormat.TwentyFourHour;

        /// <summary>
        /// Whether an incoming Phinix chat message raises a notification.
        ///
        /// On by default, and only ever read when Phinix is loaded. Phinix plays a small tick when a message
        /// arrives and shows nothing, so a message that lands while you are looking at the map is a sound you may
        /// not have caught; this is the visible half. Silent, since the tick is already theirs.
        ///
        /// See <c>Features.Integrations.PhinixIntegration</c>.
        /// </summary>
        public bool notifyPhinixChat = true;

        /// <summary>
        /// Whether Phinix's routine information logging is thrown away.
        ///
        /// <b>On by default, which is unusual for something that hides information, and is right here.</b>
        /// Phinix logs every login, logout, name change, trade and received chat message as it happens. That is
        /// a steady stream on a populated server, and its cost is not the lines themselves but everything else
        /// they push out of a log somebody opened to investigate something unrelated.
        ///
        /// <b>Only their information lines.</b> Warnings and errors are never touched, so nothing that reports a
        /// real fault is hidden by this. See <c>Features.Integrations.PhinixLogSilencer</c>.
        /// </summary>
        public bool suppressPhinixInfoLog = true;

        /// <summary>
        /// Whether an open main tab can be dragged to a different size.
        ///
        /// On by default. RimWorld gives every tab one fixed size chosen by whoever wrote it, and the one that
        /// suits a three colonist camp is not the one that suits a colony of twenty -- the work grid and the
        /// pawn tables are the obvious cases, but every list tab has the same problem at some colony size.
        ///
        /// Sizes themselves live in their own file rather than here; see <c>Features.Tabs.TabSizes</c> for why.
        /// </summary>
        public bool resizableTabs = true;

        /// <summary>
        /// Whether the architect's material list shows each material's stats, or only its icon and name.
        ///
        /// <b>On by default, because the stats are the reason that pane exists.</b> Vanilla offers materials
        /// through a float menu, which can only list names, so choosing granite over sandstone meant already
        /// knowing the difference or leaving the menu to look it up.
        ///
        /// Off is for the player who does know: once the numbers are in your head, four lines of them per
        /// material is four times the scrolling to reach the one you were always going to pick.
        /// </summary>
        public bool showStuffDetails = true;

        /// <summary>
        /// Plants flagged as favorites in the growing bill picker, as defNames separated by commas.
        ///
        /// <b>In the config file rather than in the save,</b> because a favorite is a statement about how
        /// somebody plays rather than about one colony. Anybody who always plants rice and healroot wants that
        /// list on the next colony too, and storing it per save would make them rebuild it every time.
        ///
        /// <b>One string rather than a list of elements,</b> to match how everything else in this file is
        /// written: the reader is a switch over element names taking each one's text, and a nested list would
        /// be the only shape in here that needed its own parsing. A defName cannot contain a comma, so the
        /// separator is unambiguous.
        /// </summary>
        public string favoritePlants = string.Empty;

        /// <summary>
        /// Recipes flagged as favorites in the workbench bill picker, in the same shape as
        /// <see cref="favoritePlants"/> and for the same reasons.
        ///
        /// <b>A recipe's defName is not always enough to name the entry,</b> because an ideoligion's styles put
        /// the same recipe in the list more than once. An entry is therefore the recipe's defName, and for a
        /// styled variant the precept's name after a colon. A defName cannot contain a colon or a comma, so both
        /// separators stay unambiguous.
        /// </summary>
        public string favoriteRecipes = string.Empty;

        /// <summary>
        /// Whether the minimap is drawn in a corner of the map view.
        ///
        /// <b>On by default, like the other corner widgets.</b> It is the largest thing this mod puts on screen
        /// unasked, which is an argument for defaulting it off -- but a minimap nobody can see is a minimap
        /// nobody knows to switch on, and it collapses to a title bar in one click.
        /// </summary>
        public bool showMinimapWidget = true;

        /// <summary>
        /// Which corner the minimap docks against.
        ///
        /// Bottom left by default, because it is the emptiest corner of RimWorld's screen: the resource readout
        /// owns the top left, and the letters, alerts and this mod's own widgets share the right.
        /// </summary>
        public MinimapCorner minimapCorner = MinimapCorner.BottomRight;

        /// <summary>How large the minimap is drawn. Medium is 220 pixels square.</summary>
        public MinimapSize minimapSize = MinimapSize.Medium;

        /// <summary>
        /// Whether the grouped colonist bar replaces RimWorld's.
        ///
        /// On by default, since it is the feature. Off leaves vanilla's bar drawing untouched, which is an opt out
        /// rather than a failure path: nothing in this mod hands off to vanilla's own window when something goes
        /// wrong, and this is somebody deciding they prefer the original.
        /// </summary>
        public bool showGroupedColonistBar = true;

        /// <summary>
        /// Whether each tile renders a live view of the pawn instead of their portrait.
        ///
        /// <b>Off by default, deliberately.</b> Every live tile costs a camera pass, so leaving this on by default
        /// would spend frames on behalf of players who never asked for it. Off falls back to
        /// <c>PortraitsCache</c>, which is what vanilla draws and is already cached.
        /// </summary>
        public bool livePawnView;

        /// <summary>How often live tiles are refreshed. Ignored entirely when <see cref="livePawnView"/> is off.</summary>
        public PawnViewRefresh pawnViewRefresh = PawnViewRefresh.Ms250;

        /// <summary>
        /// When a colonist tile shows the weapon its pawn is carrying, under the meters.
        ///
        /// Never by default, because anything else makes every tile taller and the bar is the one panel that sits
        /// over the map rather than beside it. Somebody who wants the information can spend the pixels.
        /// </summary>
        public BarWeaponDisplay barWeaponDisplay = BarWeaponDisplay.Never;

        /// <summary>
        /// Whether worn headgear is drawn at all.
        ///
        /// Off by default, since a hat is a thing the player chose to put on somebody. On is for the colony where
        /// every face is behind a helmet and nothing tells you who is who.
        ///
        /// <b>Everywhere, as of 2026-08-23.</b> It began as a portrait setting for the colonist bar, because the
        /// portrait cache takes a <c>renderHeadgear</c> argument and the map takes nothing of the kind. The live
        /// tiles then showed helmets whatever the bar asked, which was reported as a bug and is really the
        /// limitation: a pawn's sprite is submitted to the frame once and every camera sees the same submission.
        /// So it is now a patch on <c>PawnRenderNodeWorker_Apparel_Head.HeadgearVisible</c> and applies to the
        /// map, the portraits and the tiles alike -- for the colony's own people only, so mechanoids and
        /// shamblers are untouched. See <c>Patch_HeadgearVisible</c> for the exceptions and for why patching that
        /// one gate also brings the hair back.
        ///
        /// <b>The name is kept for the config file's sake.</b> It reads <c>barHideHeadgear</c> in everybody's
        /// settings already, and renaming it would silently reset the setting for every existing player.
        /// </summary>
        public bool barHideHeadgear;

        /// <summary>
        /// Whether hostiles are marked on the minimap.
        ///
        /// <b>On by default, and switchable because it is a fair thing to disagree about.</b> The minimap only
        /// ever shows what the colony can already see -- anything under unexplored fog is not drawn and not
        /// listed -- so this is not information the base game withholds. It is still a much easier read than
        /// scanning the map yourself, and a player who finds that too generous should be able to turn it off
        /// rather than give up the minimap.
        ///
        /// Colonists, animals and downed pawns are unaffected: the question is about reading the enemy, not
        /// about reading your own colony.
        /// </summary>
        public bool showMinimapEnemies = true;

        /// <summary>
        /// Where the player dragged the minimap to, in screen pixels, or negative for "wherever the corner
        /// puts it".
        ///
        /// <b>Negative rather than a nullable pair,</b> to match how everything else in this file is written:
        /// the reader is a switch over element names taking each one's text, and a nullable would be the only
        /// shape in here needing its own parsing. A negative screen coordinate is never a real position, so it
        /// is an unambiguous way to say "not set".
        ///
        /// <b>Both are checked together</b> wherever they are read, so a hand-edited file with only one of
        /// them falls back to the corner rather than parking the panel against an edge.
        /// </summary>
        public float minimapX = -1f;

        public float minimapY = -1f;

        /// <summary>
        /// Whether the architect draws the detail strip under the build grid.
        ///
        /// <b>On by default, because it is where the build information lives.</b> Vanilla puts a building's
        /// description, cost and stats in a floating info box; this mod moved that inside the window, under the
        /// grid, where it does not cover the thing being read about. Switching it off is for somebody who knows
        /// their build menu and would rather have the hundred and twelve pixels back as build tiles.
        ///
        /// <b>It also governs whether the build tiles carry a hover tip.</b> The strip and the tip say the same
        /// words, so both at once is duplication -- but with the strip gone the tip is the only way left to read
        /// a description, so it returns. See <c>ArchitectPanel.DrawDesignatorCard</c>.
        /// </summary>
        public bool showArchitectInfoPanel = true;

        /// <summary>
        /// Whether the inspect pane is this mod's rebuilt one.
        ///
        /// <b>On by default, and it has a floor rather than needing to be switched off.</b> The pane can be
        /// dragged down to RimWorld's own 165 pixels, at which point it shows a name, a condition and the inspect
        /// string, which is what the game shows. This switch exists for somebody who wants the vanilla pane
        /// itself back, chips and portrait included, usually because another mod is drawing into the same space.
        ///
        /// Off also hands the tab row back, so an ITab that this replaces opens its own window again.
        /// </summary>
        public bool richInspectPane = true;

        /// <summary>
        /// How tall the inspect pane is, in pixels.
        ///
        /// Written by the grip on the pane's top edge rather than by any control in the options window, and
        /// clamped where it is read rather than here: a hand-edited number larger than the screen would put the
        /// grip needed to drag it back off the top of it. See <c>InspectPaneMetrics.Height</c>.
        ///
        /// <b>The default is 300 rather than vanilla's 165,</b> which is the smallest height that fits a header,
        /// a body and the inspect string at once. Shipping at the floor would mean an install that never finds
        /// the grip never sees the feature, and a feature nobody discovers is one that was not built; shipping
        /// tall means the pane covers more map than somebody may want, and that is one drag to fix.
        /// </summary>
        public float inspectPaneHeight = 300f;

        /// <summary>
        /// Whether room and zone names are drawn onto the floor, and renameable.
        ///
        /// <b>On by default.</b> It is the whole point of the feature that it works before anybody configures
        /// anything: RimWorld already decides what each room is for, and this shows that answer where you are
        /// already looking. A feature that has to be switched on is one most people never see.
        ///
        /// <b>Off disables the drawing and the labels window, and nothing else.</b> Names already set are kept
        /// in the save, so switching off and on again does not lose them. More importantly it does not disable
        /// <c>Compat_LabelsOnFloor</c>, which exists to stop a save made with that mod losing components on
        /// load -- that is save integrity rather than a feature, and a preference must not be able to turn it
        /// into a broken colony.
        /// </summary>
        public bool roomNameLabels = true;

        /// <summary>
        /// Smallest room, in cells, that gets a label drawn on its floor.
        ///
        /// <b>A setting because how small is too small is not a fact about the room.</b> The same closet is
        /// unreadable clutter to somebody who plays zoomed out and perfectly legible to somebody who does not.
        /// Twelve is roughly a three by four room, which is where a name stops crowding the walls.
        ///
        /// Clamped where it is read rather than where it is written, so a hand-edited file with a silly number
        /// gives odd labels instead of an exception.
        /// </summary>
        public int roomLabelMinimumCells = 12;

        /// <summary>
        /// The ingredient search radius given to a newly created bill.
        ///
        /// <b>Vanilla's own default, so nothing changes until the player says so.</b> Backlog 20 asks for this to be
        /// settable once rather than per bill, and the temptation is to ship a smaller number because 999 sends a
        /// pawn across the map for one piece of steel. Doing that would quietly stall bills for everybody who never
        /// opened the setting, in colonies whose stockpiles are simply far from the bench. The setting exists so
        /// the player can choose; choosing for them is a different thing.
        ///
        /// Existing bills are never touched by this. The bills window offers that as an explicit action.
        /// </summary>
        public float defaultIngredientRadius = 999f;

        /// <summary>
        /// Whether the bills window points out a bill nothing can work.
        ///
        /// Display only. Nothing is ever suspended or altered because of it.
        /// </summary>
        public bool warnStalledBills = true;

        /// <summary>
        /// Whether ore veins are shaded while the mine designator is up.
        ///
        /// On by default: it costs nothing while the designator is not selected, and the thing it fixes is that
        /// ore is drawn as rock with a slightly different texture, which at anything but full zoom is no
        /// difference at all.
        /// </summary>
        public bool showMineableOverlay = true;

        /// <summary>
        /// Whether a selected thing that can explode is ringed at its blast radius.
        ///
        /// On by default: it draws nothing until something explosive is selected, and the thing it fixes is that
        /// the blast radius of an IED, a shell rack or a chemfuel pile is stated on the info card as a number and
        /// nowhere at all on the map, which is where the decision about where to put it is made.
        /// </summary>
        public bool showBlastRadius = true;

        /// <summary>
        /// Whether a pawn somebody is offering is described beside the letter that offers them.
        ///
        /// On by default. The letters this reaches ask the player to accept, refuse, ransom or pick between
        /// people, and vanilla gives them the prose of the letter and a row of bare names to decide on. The
        /// panel is display only: nothing about the offer, its buttons or its outcome changes.
        /// </summary>
        public bool pawnDetailsOnOffers = true;

        /// <summary>
        /// How far an orbital trade beacon reaches, in tiles.
        ///
        /// <b>RimWorld's own 7.9 by default,</b> which is a beacon covering 15 by 15 with the corners clipped.
        /// Asked for on 2026-08-22 as a slider from 3 to three times vanilla, and the default is vanilla's for the
        /// same reason the ingredient radius keeps its: an install that never opens this setting plays the game
        /// the game shipped.
        ///
        /// Clamped where it is read rather than here, so a hand edited file with a silly number gives a sensible
        /// beacon rather than one that covers the map. See <c>TradeBeaconRadius</c>.
        /// </summary>
        public float tradeBeaconRadius = 7.9f;

        /// <summary>
        /// Whether trading uses our window instead of <c>Dialog_Trade</c>.
        ///
        /// <b>On by default, and the reason it exists is compatibility rather than taste.</b> A mod that patches
        /// the vanilla trade dialog -- adding a column, a button, a filter -- will never see ours, so it silently
        /// stops working, and some of those failures are quiet rather than loud. Somebody running heavy trade
        /// mods switches this off and keeps theirs. It shipped with the window rather than after the first bug
        /// report, because building an escape hatch is cheaper than retrofitting one.
        ///
        /// <b>Not a runtime fallback.</b> This mod's rule is that a feature failing mid-draw must not quietly
        /// hand off to vanilla, because that hides the defect. This is a choice made in the settings window with
        /// the consequences written down, which is a different thing.
        /// </summary>
        public bool customTradeWindow = true;

        /// <summary>
        /// Whether forming and splitting a caravan uses our window instead of vanilla's two.
        ///
        /// Separate from the trade window on purpose: the compatibility risk is per window, and a mod that adds a
        /// column to the trade dialog has nothing to do with the caravan packer. See
        /// <c>customTradeWindow</c> for the reasoning behind having the setting at all.
        /// </summary>
        public bool customCaravanWindow = true;

        /// <summary>
        /// Whether the comms console opens our directory instead of RimWorld's float menu of bare text lines.
        ///
        /// <b>The one of the four with a fallback that changes behaviour rather than only appearance.</b> Vanilla
        /// answers "who can I call" with one <c>FloatMenuOption</c> per target; ours draws a card per target from
        /// the same <c>ICommunicable</c> interface. A mod that adds an option to that float menu by patching
        /// <c>Building_CommsConsole.GetFloatMenuOptions</c> still appears in ours, because ours reads the same
        /// options -- but one that patches the menu after the fact would not.
        /// </summary>
        public bool customCommsWindow = true;

        /// <summary>
        /// Whether a selected trade beacon offers a readout of what its reach is actually worth.
        ///
        /// <b>Nothing is replaced by this one,</b> which is why it is the safest of the four: vanilla draws
        /// nothing at all for a built beacon, so this is a gizmo and a window that did not exist rather than a
        /// substitute for one that did. Off costs the readout and nothing else.
        /// </summary>
        public bool beaconReadout = true;

        /// <summary>
        /// Whether a crop that catches blight is marked for cutting automatically.
        ///
        /// <b>On by default, which is the opposite of the livestock setting below and for a reason.</b> That one
        /// changes what pawns are allowed to do; this one issues a designation the player was going to issue
        /// anyway. A blighted plant yields nothing at all, <c>Plant.CanYieldNow</c> says so outright, and it
        /// spreads to its neighbours while it stands: there is no reading of the game where leaving it is the
        /// better play, so doing it by hand is busywork rather than a decision.
        ///
        /// Still a setting, because it writes designations into a live colony and anything that does that should
        /// be switchable off in one place.
        /// </summary>
        public bool autoCutBlightedPlants = true;

        /// <summary>
        /// Whether finishing a research project reports itself as a letter instead of a popup.
        ///
        /// <b>On by default, on Aaron's instruction of 2026-08-23.</b> The completion popup is a modal dialog: it
        /// takes the keyboard, it stops the game responding to anything else until somebody dismisses it, and it
        /// arrives at a moment the player did not pick -- which for a colony running three benches is several
        /// times a day, often in the middle of a raid. Nothing in it is urgent. It names the project that
        /// finished and then repeats the description already written on that project's own page.
        ///
        /// <b>The letter is ours, not the one RimWorld already sends.</b> <c>ResearchManager.FinishProject</c>
        /// sends a letter only for a project carrying a <c>discoveredLetterTitle</c>, and eight of the game's
        /// hundred and sixty-four projects carry one. Suppressing the popup and leaning on that would mean the
        /// other hundred and fifty-six finished in silence, which is a worse outcome than the popup.
        /// </summary>
        public bool quietResearchCompletion = true;

        /// <summary>
        /// Whether the Colonists idle alert drops pawns whose idleness nobody can act on.
        ///
        /// <b>On by default.</b> The alert exists to catch a colonist you could go and give a job to, and both
        /// groups it drops here are idle in a way no order can change: somebody else's pawn standing in your
        /// colony, and somebody with no work type open to them at all. Lit permanently by a case the player
        /// cannot answer, the alert stops being read at all, and that costs them the times it was real.
        ///
        /// <b>RimWorld already makes this distinction and stops halfway,</b> which is why this is a lifted
        /// refusal rather than a new rule: the alert skips quest lodgers, and skips a royal whose title carries
        /// <c>suppressIdleAlert</c>. Everything here is another way of arriving at the same position.
        /// </summary>
        public bool quietIdleAlert = true;

        /// <summary>
        /// What the research canvas is cut into blocks along: "theme", "source" or "tech".
        ///
        /// <b>Theme by default,</b> which is the point of the rework: a block per mod answers what a mod added,
        /// and that is almost never the question somebody opens the research tab with. "source" reproduces the
        /// layout that shipped in 14162 exactly, for anybody who wants it back.
        ///
        /// A word rather than a number so a hand-edited file reads, and an unknown value falls back to theme.
        /// See <see cref="Research.ResearchGroupings"/>.
        /// </summary>
        public string researchGrouping = "theme";

        /// <summary>
        /// Whether ancient wreckage can be deconstructed for steel and components.
        ///
        /// <b>Off by default, because it changes what a generated map is worth.</b> A ruined tank is currently
        /// scenery you build around: not deconstructible, and shooting it apart leaves nothing. On, it becomes a
        /// pile of salvage priced off its own footprint. That is a real change to the early game's material
        /// budget, and a player who wants the wrecks left as obstacles should not have to find the switch to keep
        /// them.
        ///
        /// Named wreckage only, never quest machinery. See <c>AncientSalvage</c> for why that is a list rather
        /// than a rule.
        ///
        /// Asked for on 2026-08-24.
        /// </summary>
        public bool salvageAncientWrecks;

        /// <summary>
        /// Whether sleeping in a barracks costs mood.
        ///
        /// The penalty runs from -7 to -1 across the room-quality stages, and it is charged for a decision the
        /// player made deliberately: a barracks is what an early colony can afford. On, those stages read zero;
        /// the four stages above them are a bonus rather than a penalty and are left alone.
        ///
        /// Asked for on 2026-08-24.
        /// </summary>
        public bool barracksAreNeutral;

        /// <summary>
        /// Whether livestock can be given an allowed area, which RimWorld refuses.
        ///
        /// <b>Off by default, and that is not caution for its own sake.</b> This is the only setting in this file
        /// that changes what the game's pawns are allowed to do rather than how something is drawn: vanilla gates
        /// the area control on <c>Pawn_PlayerSettings.SupportsAllowedAreas</c>, which refuses any animal with a
        /// <c>roamMtbDays</c>, because livestock is meant to be held by a pen. Turning it on means a cow can be
        /// given an area, and every part of the AI that asks whether a cell is forbidden honors it, since they all
        /// go through the same test.
        ///
        /// <b>It also holds them in, which took a second change.</b> Vanilla's roaming state asks about ropes and
        /// the reachable map edge and never about areas, so livestock with an area would have been given somewhere
        /// to be and no reason to stay: they walk off the map after day five regardless. Asked for and closed on
        /// the same day, so this setting now covers both halves. See <c>LivestockRoaming</c>: an area with
        /// anything in it, or standing in a pen that accepts them, counts as being kept, and a roam already under
        /// way ends when either becomes true.
        ///
        /// Asked for on 2026-08-22, with the default named in the same sentence.
        /// </summary>
        public bool penAnimalsUseAreas;

        /// <summary>
        /// Whether a bed can be marked communal, letting anybody sleep in it while a slot is free.
        ///
        /// <b>An owned bed that others may still use.</b> RimWorld's rule is that once a bed has an owner, nobody
        /// else may sleep in it except a love partner -- which is right for a private bedroom and wrong for the
        /// spare bunk in a barracks, the bed beside the workshop somebody naps in, or a shift-worked bunk. There
        /// is no way to say "this one is mine but help yourself when I am not in it". This adds the mark that
        /// says it.
        ///
        /// <b>It changes only who a bed will accept, not who owns it.</b> Assignment, bedroom thoughts and the
        /// rest of ownership are untouched: a communal bed with an owner still counts as that pawn's room for
        /// mood. The single rule relaxed is the refusal in <c>RestUtility.BedOwnerWillShare</c>, and the
        /// unoccupied check above it in <c>CanUseBedNow</c> is vanilla's own and still applies -- so a communal
        /// bed with somebody in it is as unavailable as any other.
        ///
        /// <b>On by default.</b> It shipped off on the reasoning that a behaviour change should be opted into,
        /// which is right for the livestock areas beside it -- that one changes what every animal in the colony
        /// does the moment it is switched on. This one changes nothing until a bed is marked, and a switch that
        /// does nothing on its own does not need guarding: a player who never touches a bed never notices it, and
        /// a player who wants it should not have to find a setting first. Turned on the same day it shipped.
        /// </summary>
        public bool allowCommunalBeds = true;

        /// <summary>
        /// Whether the character editor exists at all.
        ///
        /// <b>Off, and off means absent rather than greyed.</b> Asked for on 2026-08-22 in those words: with this
        /// false there is no button on a pawn's bio panel and no action on a corpse, not a disabled one. Every
        /// other setting in this mod changes how something is drawn; this one is the only switch that decides
        /// whether a tool that rewrites the save is reachable, and a player should have to decide that once
        /// deliberately rather than discover it by clicking a greyed control.
        ///
        /// <b>Not gated on dev mode, which was considered and rejected.</b> Dev mode turns on a hundred other
        /// things, most of them able to break a save by accident, and it is not somewhere somebody wanting to fix
        /// one colonist's nickname should have to go -- nor somewhere they should have to stay.
        ///
        /// Read the strict way round, so anything but an explicit true leaves the editor absent.
        /// </summary>
        public bool characterEditor;

        /// <summary>
        /// Whether this mod manages the music.
        ///
        /// <b>On, and off means absent.</b> With this false nothing is patched, the game picks songs the way it
        /// always did, and there is no window and no strip -- not a disabled one. Playlists the player made are
        /// kept, because switching a feature off is not a request to delete their library.
        ///
        /// <b>The one setting in this mod that another mod can force.</b> Two music players driving RimWorld's one
        /// audio source means two songs competing for it, so <c>MusicRivals</c> stands ours down when RimTunes,
        /// Music Manager, Music Expanded Framework or anything else patching the music manager is loaded. That
        /// override is not written here: this stays whatever the player set, so removing the other mod gives them
        /// their choice back rather than a silently disabled feature.
        ///
        /// Defaults on, so it reads the permissive way round: only an explicit false turns it off.
        /// </summary>
        public bool musicPlayer = true;

        /// <summary>
        /// Whether the now playing strip is drawn in the corner with the other readouts.
        ///
        /// Its own setting like every other row down there, because somebody who wants the player but not a
        /// permanent readout over their map has nowhere else to say so. The window and the playback are
        /// unaffected; this hides one block.
        /// </summary>
        public bool showMusicWidget = true;

        /// <summary>
        /// Whether this mod draws the research tab.
        ///
        /// <b>On, and off means vanilla's screen unchanged.</b> With this false nothing about the research window
        /// is patched: the category tabs come back, the hand-placed coordinates come back, and the queue is not
        /// advanced. The queue itself is kept in the save, because switching a feature off is not a request to
        /// throw away a plan.
        ///
        /// <b>Why it is worth a switch at all.</b> This one replaces a screen a player may have a thousand hours
        /// of muscle memory in, and the layout is computed rather than authored -- the arrangement will not be the
        /// one they know. That is a taste, and a taste needs an off.
        ///
        /// Defaults on, so it reads the permissive way round: only an explicit false turns it off.
        /// </summary>
        public bool researchTab = true;

        /// <summary>
        /// Which characters an undiscovered Anomaly project is written in.
        ///
        /// Generated by default: it needs no atlas, tints to the running palette, and is the only option that
        /// cannot be missing. A name here that does not match a script, or one whose atlas did not load, reads
        /// back as Generated -- the same lenient handling every other named setting in this file gets.
        /// </summary>
        public Research.ResearchScript anomalyScript = Research.ResearchScript.Generated;

        /// <summary>
        /// Which raids and incidents the player has switched off, as a comma separated list of keys.
        ///
        /// <b>Empty by default, and empty means the game is untouched.</b> Asked for in those words on
        /// 2026-08-23: nothing happens unless somebody goes and asks for it. Every patch in that feature tests
        /// this first and returns.
        ///
        /// <b>One string rather than twenty booleans.</b> The reader below is a flat switch over element names,
        /// so twenty settings would be twenty cases, twenty fields and twenty write lines -- and twenty chances
        /// for one of them to read the wrong key, which is a bug that only shows up as one switch that will not
        /// stick. The keys themselves are Raid and Event Manager's own, kept letter for letter; see
        /// <c>ThreatToggles</c>.
        /// </summary>
        public string disabledThreats = string.Empty;

        /// <summary>
        /// Whether the gravship numbers below are written onto the defs at all.
        ///
        /// <b>Off, and off means the game's own values are written back</b> rather than merely left alone -- see
        /// <c>GravshipTuning</c>. One switch in front of three settings, asked for in those terms on 2026-08-23,
        /// because these change what can be built rather than how anything is drawn: a player who has not been to
        /// this page is playing Odyssey exactly as Ludeon shipped it.
        /// </summary>
        public bool gravshipOverrides;

        /// <summary>
        /// The grav engine's substructure footprint radius, in cells. Zero means the game's own.
        ///
        /// <b>Zero rather than 18.9,</b> because vanilla's figure is not knowable when this field is declared and
        /// is not the same on every install -- another mod may have patched the engine. <c>GravshipTuning</c>
        /// reads zero as "whatever the def said before we touched it" and clamps anything else to this install's
        /// own range.
        /// </summary>
        public float gravEngineRadius;

        /// <summary>
        /// Whether the substructure tile cap is lifted, leaving the footprint radii as the only limit on size.
        ///
        /// Sets the engine's <c>SubstructureSupport</c> to 99999 and stops extenders adding any of their own, so
        /// what a gravship may cover is decided by where substructure is allowed rather than by a count.
        /// </summary>
        public bool gravshipUnlimitedTiles;

        /// <summary>How many grav extenders may link to one engine. Negative means the game's own six.</summary>
        public int gravExtenderMax = -1;

        /// <summary>
        /// How long the marker left where a colony was abandoned stays on the planet, in days. Zero keeps it.
        ///
        /// <b>Thirty days, which is RimWorld's own figure for the one leftover it does clear up</b> -- the
        /// abandoned camp, in <c>Camp.Notify_MyMapRemoved</c>. Asked for on 2026-08-23 as "set the default to 30
        /// days for all that don't clean up already", so the three kinds the game keeps forever are held to the
        /// same clock as the one it does not.
        ///
        /// <b>These four are the only settings in this file that decide whether something in the save is
        /// removed,</b> and unlike everything else of that kind in this mod they do not start switched off. The
        /// lifespan is counted from the day the marker appeared rather than from the day the setting was chosen,
        /// so an old save loses its old markers within an hour of loading. See <c>SiteFade</c> for why that is the
        /// right way round, and for the count the options window draws before a change is applied. Keep is the
        /// off switch.
        ///
        /// Read strictly and matched against the offered list, so a hand-edited file with a lifespan of one is
        /// treated as unset rather than as an instruction to clear the planet.
        /// </summary>
        public int siteFadeSettlementDays = 30;

        /// <summary>How long a gravship launch marker stays, in days. Zero keeps it.</summary>
        public int siteFadeLaunchDays = 30;

        /// <summary>
        /// How long an abandoned camp marker stays, in days.
        ///
        /// The one of the four where thirty is what the game already does rather than something asked of it, so
        /// this row starts out changing nothing at all. Moving it overrides <c>Camp.Notify_MyMapRemoved</c>, and
        /// Keep makes a camp marker permanent, which the game itself will not do.
        /// </summary>
        public int siteFadeCampDays = 30;

        /// <summary>
        /// How long the marker for a camp pitched on a landmark stays, in days. Zero keeps it.
        ///
        /// Separate from the camp above because RimWorld treats them differently: the landmark one is made by the
        /// same method and given no clock at all, so it is the only camp marker that lasts forever.
        /// </summary>
        public int siteFadeLandmarkDays = 30;

        /// <summary>
        /// How many bills one workbench may hold.
        ///
        /// <b>A setting rather than a number of ours,</b> asked for on 2026-08-19. It was a hard 120 written into
        /// the IL, which was chosen to be past anybody's real use rather than as a considered figure. Sixty is the
        /// default: comfortably past vanilla's fifteen, which is the actual complaint, without turning a smelter
        /// into a list nobody can read.
        ///
        /// Clamped where it is read rather than where it is written, so a hand-edited file with a silly number
        /// gives a sensible cap instead of an exception. Never goes below vanilla's own fifteen; see
        /// <c>BillCap.Floor</c> for why lowering a limit is a different feature from raising one.
        /// </summary>
        public int maxBillsPerBench = 60;

        /// <summary>
        /// Whether command buttons are drawn in this mod's theme.
        ///
        /// Every gizmo in the game passes through that patch, so this exists to turn all of it off in one place if
        /// it ever sits badly beside another mod.
        /// </summary>
        public bool restyleCommandButtons = true;

        /// <summary>
        /// Which typeface the floor labels are drawn in.
        ///
        /// <b>Oswald Bold by default because it is condensed.</b> A label is scaled down to fit the widest clear
        /// run of floor in its room, so a narrow face survives a long room name at a readable size where a wide
        /// one would shrink away. Hammersmith One is the wider, more geometric alternative, and the game font is
        /// there for anybody who would rather the floor matched the interface.
        ///
        /// Both shipped faces are baked atlases under the mod's Fonts folder; see FloorLabelAtlas for why a
        /// font file cannot simply be loaded.
        /// </summary>
        public FloorLabelFace roomLabelFace = FloorLabelFace.OswaldBold;

        /// <summary>
        /// Whether the save window's compression box is ticked when it opens.
        ///
        /// <b>Off by default, and that is a decision about what happens when this mod is removed.</b> A
        /// compressed save is still a <c>.rws</c> file, and RimWorld without this mod cannot read it: the
        /// player would uninstall, find every colony missing from the load list, and have no reason to connect
        /// the two. Defaulting a format change to on is asking for a trust the mod has not earned, so it is
        /// offered instead, in the window where saving is already being thought about.
        ///
        /// <b>This remembers the box rather than governing it.</b> The value that matters is the one ticked
        /// for the save being written; this is only what the box shows next time, so somebody who compresses
        /// everything does not re-tick it every save.
        /// </summary>
        public bool compressSaves;

        /// <summary>
        /// Whether autosaves are compressed too.
        ///
        /// <b>Separate from <see cref="compressSaves"/> because the cost lands differently.</b> A save the
        /// player asked for happens behind RimWorld's own saving screen while they wait for it deliberately.
        /// An autosave fires unannounced, on the main thread, in the middle of whatever is happening -- and
        /// compressing a large colony takes seconds, which turns the brief hitch autosave already causes into
        /// a freeze during a raid.
        ///
        /// Off by default for that reason, and worth switching on anyway for anybody whose autosaves are the
        /// bulk of their Saves folder, which is most people who have played one colony for a long time.
        /// </summary>
        public bool compressAutosaves;

        /// <summary>
        /// Pawn categories switched off in the pawns tab, by name, separated by commas.
        ///
        /// <b>What is hidden rather than what is shown,</b> so an empty value means everything is visible. The
        /// other way round, a fresh install and somebody who had hidden every category would be written
        /// identically, and the fresh install would open to an empty tab.
        /// </summary>
        public string hiddenPawnCategories = string.Empty;

        // ---------------------------------------------------------------------------------------
        // Notifications
        //
        // The three surfaces RimWorld raises things on: transient messages, the letter stack, and the alerts
        // readout. Each has two settings -- whether this mod draws it at all, and which corner it lives in.
        //
        // The defaults reproduce where the base game puts all three, deliberately. Installing this mod changes
        // how they look; where they are is the player's decision to make afterwards.
        // ---------------------------------------------------------------------------------------

        public bool restyleMessages = true;

        public bool restyleLetters = true;

        public bool restyleAlerts = true;

        /// <summary>
        /// Whether every mental break raises a letter, and whether those letters say how long it lasts.
        ///
        /// <b>On by default, because the half it adds is the half you act on.</b> RimWorld stays silent for any
        /// break whose state class writes no begin-letter text, and none of the letters it does send says how
        /// long the break runs -- which is the one fact that decides between arresting them, waiting, and going
        /// to fix the room.
        ///
        /// Off restores RimWorld exactly: the breaks it announces, worded the way it words them.
        /// </summary>
        public bool mentalBreakLetters = true;

        /// <summary>Messages start where vanilla puts them: top left, clear of the resource readout.</summary>
        public NotificationDock messageDock = NotificationDock.TopLeft;

        public NotificationDock letterDock = NotificationDock.BottomRight;

        public NotificationDock alertDock = NotificationDock.BottomRight;

        /// <summary>
        /// How wide a letter row is drawn.
        ///
        /// <b>A setting because the trade is the player's.</b> These rows sit over the map, so width is bought
        /// with playable screen, and how much that costs depends on the display and on how somebody plays.
        /// 250 matches the corner panel underneath, which lines the two columns up and fits most letter labels.
        ///
        /// Clamped where it is read rather than where it is written, so a hand-edited file with a silly number
        /// gives an odd looking stack instead of an unusable screen.
        /// </summary>
        public float letterRowWidth = 250f;

        // ---------------------------------------------------------------------------------------
        // Desktop widgets
        //
        // The readouts this mod draws in the corner of the screen. Each is independently switchable because they
        // are independently useful: somebody who wants the season but not the weather is not an odd case, they are
        // someone whose colony is in a biome where the weather never changes.
        //
        // Every one defaults to on. A widget nobody can see is a widget nobody knows to turn on, and the whole set
        // is one checkbox away from gone for anyone who wants their corner back.
        // ---------------------------------------------------------------------------------------

        // showSpeedGlyphs was here, and is retired rather than defaulted: the drawn speed glyphs are simply how
        // this mod looks now. It was never a real choice -- the two options were this mod's icons and the ones
        // they were drawn to replace -- and a switch that nobody has a reason to move is a line of settings a
        // player has to read past to reach the ones that matter.

        /// <summary>
        /// The year bar in the corner: the growing season, today's place in the year, and the door to the
        /// calendar window.
        ///
        /// The first widget in the corner that is this mod's own rather than a restyling of RimWorld's.
        /// </summary>
        public bool showCalendarWidget = true;

        /// <summary>
        /// Whether the calendar names what the storyteller has scheduled, rather than only its kind.
        ///
        /// <b>Off by default because the honest default is vague.</b> The storyteller settles an incident's
        /// timing well before it fires and settles which incident only at the last moment, so the calendar can
        /// say "major threat on day 43" as a fact and cannot say "raid on day 43" at all. Switching this on adds
        /// the most specific true thing available: the exact incident where a component fires only one, and the
        /// category and component where it picks from a pool.
        ///
        /// Some players will read a spoiler into knowing a threat is coming. That is the point of the switch,
        /// and it is why the default is the coarse view rather than this one.
        /// </summary>
        public bool showExplicitStoryEvents;

        /// <summary>
        /// Whether the calendar leaves colonist birthdays out.
        ///
        /// <b>Phrased as hiding rather than showing, because that is what it is for.</b> Birthdays are on by
        /// default and should stay that way: they are the entries most players opened the calendar to find. This
        /// exists for the colony where they have stopped being information -- past a few dozen colonists every
        /// day carries one, and a fifteen day view becomes a wall of them with the quest deadline that mattered
        /// buried somewhere inside. Asked for on 2026-08-28.
        ///
        /// <b>It hides them rather than capping them per day.</b> A cap would have to choose whose birthday to
        /// drop, and there is no defensible answer -- a player who wants fewer birthdays wants none, not an
        /// arbitrary three.
        /// </summary>
        public bool hideCalendarBirthdays;

        /// <summary>
        /// The strip of common orders in the bottom left when nothing is selected.
        ///
        /// Claim, deconstruct, mine, mine vein, allow and forbid are each four clicks deep in the Architect
        /// menu and are given dozens of times an hour. On by default, because the corner it uses is empty
        /// whenever it is shown -- select anything and the inspect pane takes that space back.
        /// </summary>
        public bool showQuickOrders = true;

        /// <summary>
        /// Whether frames wait for the monitor.
        ///
        /// <b>Ours to store because RimWorld does not have one.</b> There is no vsync field anywhere in
        /// <c>Prefs</c>, <c>PrefsData</c> or <c>ResolutionUtility</c> -- it is set once by Unity from the quality
        /// level and never written back, so a change made at runtime is gone at the next launch. Keeping it here
        /// is what makes the switch stay where it was put.
        ///
        /// <b>On by default, which is what the game already does.</b> Installing this mod must not change how
        /// anybody's game renders; the default exists to match the state we found, not to express a preference.
        ///
        /// <b>Off means uncapped, not faster.</b> Nothing else in RimWorld limits the frame rate, so switching
        /// this off lets the card draw as many frames as it can -- useful for measuring, and a way to make a
        /// quiet machine loud for no visible gain.
        /// </summary>
        public bool vsync = true;

        /// <summary>
        /// The real time clock: vanilla's HH:mm line, drawn by <c>DoRealtimeClock</c>.
        ///
        /// <b>This switch governs, in both directions.</b> Ticked shows the clock even when vanilla's own
        /// preference is off; cleared hides it even when that preference is on. An earlier version only ever
        /// added, and that was wrong for the obvious reason: a cleared box with a clock sitting above it reads as
        /// a broken setting, whatever the reasoning behind it was.
        ///
        /// <b>Seeded from the vanilla preference rather than from a constant,</b> which is what makes governing
        /// safe to do. A fixed default is wrong whichever way it points -- true forces a clock onto every colony
        /// that installs this mod, false takes it away from everyone who had asked vanilla for one. Reading
        /// <c>Prefs.ShowRealtimeClock</c> when this mod first writes its config means installing changes nothing,
        /// and the switch takes over from there.
        ///
        /// In the field initializer so it covers every path into <see cref="Load"/> at once: no config file, a
        /// config file predating this setting, and an unreadable one all construct the object and then overwrite
        /// only what they actually read.
        ///
        /// This is deliberately not what the performance meter does, and the difference is the flag rather than
        /// the feature. <c>Prefs.ShowRealtimeClock</c> is a saved player preference, so there is something
        /// meaningful to inherit. <c>DebugViewSettings</c> is session state that resets every launch, so there
        /// would be nothing to seed from and nothing a player had deliberately kept -- which is why that one only
        /// ever adds.
        /// </summary>
        public bool showTimeWidget = InheritedRealtimeClock();

        /// <summary>
        /// Vanilla's real time clock preference, or false if it cannot be read.
        ///
        /// Guarded because this runs from a field initializer, which is as early as this type can be touched.
        /// <c>Prefs.ShowRealtimeClock</c> reads through <c>Prefs.data</c>, and a null there would throw out of a
        /// constructor -- taking the whole settings object with it, over a clock.
        /// </summary>
        private static bool InheritedRealtimeClock()
        {
            try
            {
                return Prefs.ShowRealtimeClock;
            }
            catch
            {
                return false;
            }
        }

        public bool showSpeedControlsWidget = true;

        /// <summary>
        /// Vanilla's date block, which is one switch because it is one call.
        ///
        /// <c>DoDate</c> hands the whole thing to <c>DateReadout.DateOnGUI</c>, which draws the hour, the date and
        /// the season together and reports one height for all three. Separate switches for the date and the season
        /// would mean reimplementing that readout, and a readout that shows the wrong day is worse than one that
        /// shows a line somebody did not ask for.
        /// </summary>
        public bool showDateWidget = true;

        // Kept, with no switch in front of them yet, because the rows they name are drawn inside
        // GlobalControlsOnGUI itself rather than through a call of their own -- the temperature is an inline
        // Widgets.Label, and the weather and the conditions have their layout cursor moved by the caller rather
        // than by the method being called, so skipping either leaves a hole where it was. Both become reachable
        // when that method is replaced, and the choices a player makes now should still be here when it is.
        public bool showTemperatureWidget = true;

        public bool showWeatherWidget = true;

        public bool showConditionsWidget = true;

        /// <summary>
        /// Whether vanilla's own row of play settings toggles is drawn in the corner.
        ///
        /// The one switch here that hides something the base game draws rather than something this mod adds, because
        /// the Global Controls tab holds the same toggles and a player who uses the tab has no reason to keep the row
        /// over their map.
        ///
        /// <b>Hiding this and removing the tab from the bar at the same time is allowed.</b> It looks like it strands
        /// the toggles, and does not: this mod's settings are always reachable from the bar's options button, which
        /// deliberately cannot be hidden, so either can be restored from there. No combination of these settings
        /// produces a state a player cannot get out of.
        /// </summary>
        public bool showGlobalControlsWidget = true;

        /// <summary>
        /// Whether the performance meter is drawn: frames per second and ticks per second.
        ///
        /// <b>The one widget here that defaults to off</b>, unlike the six above. A readout of the game's own frame
        /// rate is a diagnostic rather than something a colony is played with, and a permanent number in the corner
        /// invites watching it. Someone chasing late game slowdown will go looking for this; nobody else needs it
        /// sitting there.
        /// </summary>
        public bool showPerformanceWidget;

        // There was a master switch here, and a ShowsWidget helper that folded it into every read. Both are gone:
        // one box that clears the rest is a different control from a box per widget, and having both meant a
        // player who cleared one thing and a player who cleared everything left the settings in states that read
        // the same. Each widget answers for itself now.

        // There is deliberately no option to hide the bar's UI options button. It used to exist, back when
        // these settings were also reachable from the vanilla Options window; that route turned out to be
        // impossible -- Dialog_Options ignores any OptionCategoryDef from a mod -- which leaves the bar
        // button as the only way in. An option to remove the only way in is a trap, not a preference.

        public static string FilePath => Path.Combine(GenFilePaths.ConfigFolderPath, FileName);

        private static UIOverhaulSettingsFile current;

        /// <summary>
        /// The loaded settings, read from disk on first use and after any <see cref="Reload"/>.
        ///
        /// Handing the debug flag to <see cref="UIDebug"/> happens here rather than in <see cref="ApplyTheme"/>,
        /// because unlike the theme it does not need defs and so should not wait for them -- instrumentation is
        /// most wanted during startup, which is over before ApplyTheme can run.
        ///
        /// This also covers the config watcher: it calls Reload, which drops the instance, so the next read
        /// re-pushes whatever the edited file now says.
        /// </summary>
        public static UIOverhaulSettingsFile Current
        {
            get
            {
                if (current == null)
                {
                    current = Load();
                    UIDebug.Enabled = current.debugLogging;
                }

                return current;
            }
        }

        public static void Reload()
        {
            current = null;
        }

        /// <summary>
        /// Pushes the stored theme into the framework. Called once the def database exists, since a
        /// palette is a Def and cannot be resolved before then.
        /// </summary>
        public void ApplyTheme()
        {
            UIColorPaletteDef.ActiveDefName = activePalette.NullOrEmpty() ? null : activePalette;

            if (UIColorPaletteDef.ActiveIsMissing)
            {
                Log.Warning(UILogTag.Prefix + $"Palette '{activePalette}' is not loaded -- the mod that "
                            + "supplied it may be disabled. Falling back to the default theme.");
                UIColorPaletteDef.ActiveDefName = null;
            }
        }

        /// <summary>
        /// A dock name from the file, falling back rather than complaining.
        ///
        /// Same reasoning as the clock format above: this is a hand-editable file, and a misspelled corner is not
        /// worth a warning popup on the way into the game. The fallback is the surface's own default, which is
        /// where RimWorld would have put it.
        /// </summary>
        /// <summary>
        /// An enum value from the file, falling back rather than complaining.
        ///
        /// Generic because there are now three of these and a fourth would have been a fourth copy of the same
        /// six lines. <see cref="ParseDock"/> stays as it is: it is called from three places and reads better
        /// named than parameterised.
        /// </summary>
        /// <summary>
        /// A number from the file, in the invariant culture.
        ///
        /// Invariant is the point: a settings file is shared and hand-edited, and a value written with a
        /// decimal point should not stop parsing because the game is running in a language that writes it with
        /// a comma.
        /// </summary>
        private static float ParseFloat(string value, float fallback)
        {
            float parsed;

            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : fallback;
        }

        /// <summary>
        /// A marker lifespan in days, accepted only if it is one of the lifespans the window offers.
        ///
        /// <b>Stricter than the other parsers here, and deliberately so.</b> Every other lenient read in this
        /// file costs a wrong colour or a wrong font on a typo; this one decides whether things in the save are
        /// removed, and a stray digit turning fifteen days into one day would clear a planet within the minute.
        /// Anything not on the offered list reads back as the thirty days a fresh install starts on.
        /// </summary>
        private static int ParseFadeDays(string value, int fallback)
        {
            int parsed;

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return fallback;

            int[] offered = WorldSites.SiteFadeKinds.Choices;

            for (int i = 0; i < offered.Length; i++)
            {
                if (offered[i] == parsed)
                    return parsed;
            }

            return fallback;
        }

        private static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            if (value.NullOrEmpty())
                return fallback;

            foreach (T candidate in (T[]) Enum.GetValues(typeof(T)))
            {
                if (candidate.ToString().EqualsIgnoreCase(value))
                    return candidate;
            }

            return fallback;
        }

        private static NotificationDock ParseDock(string value, NotificationDock fallback)
        {
            if (value.NullOrEmpty())
                return fallback;

            foreach (NotificationDock dock in (NotificationDock[]) Enum.GetValues(typeof(NotificationDock)))
            {
                if (dock.ToString().EqualsIgnoreCase(value))
                    return dock;
            }

            return fallback;
        }

        /// <summary>
        /// A face name from the file, falling back to the default rather than complaining.
        ///
        /// Same reasoning as the dock and clock parsers above: this file is meant to be readable and editable by
        /// hand, and an unrecognized value should cost the setting rather than the launch.
        /// </summary>
        private static FloorLabelFace ParseFace(string value)
        {
            if (value.NullOrEmpty())
                return FloorLabelFace.OswaldBold;

            foreach (FloorLabelFace face in (FloorLabelFace[]) Enum.GetValues(typeof(FloorLabelFace)))
            {
                if (face.ToString().EqualsIgnoreCase(value))
                    return face;
            }

            return FloorLabelFace.OswaldBold;
        }
        private static UIOverhaulSettingsFile Load()
        {
            string path = FilePath;

            try
            {
                if (!File.Exists(path))
                    return new UIOverhaulSettingsFile();

                XmlDocument doc = new XmlDocument();
                doc.Load(path);

                UIOverhaulSettingsFile settings = new UIOverhaulSettingsFile();
                XmlElement root = doc.DocumentElement;
                if (root == null)
                    return settings;

                foreach (XmlNode node in root.ChildNodes)
                {
                    if (!(node is XmlElement field))
                        continue;

                    string value = field.InnerText?.Trim();

                    switch (field.Name)
                    {
                        case "activePalette":
                            settings.activePalette = value ?? "";
                            break;

                        case "debugLogging":
                            settings.debugLogging = value.EqualsIgnoreCase("true");
                            break;

                        // Reads "absent means off", like the other diagnostics.
                        case "showLoadingConsole":
                            settings.showLoadingConsole = value.EqualsIgnoreCase("true");
                            break;

                        // Absent means off, which here means confirmations stay on. A settings file written
                        // before this existed therefore keeps the safe behavior rather than inheriting vanilla's.
                        case "skipDevActionConfirm":
                            settings.skipDevActionConfirm = value.EqualsIgnoreCase("true");
                            break;


                        case "fullscreenOnStartup":
                            settings.fullscreenOnStartup = value.EqualsIgnoreCase("true");
                            break;

                        // Anything unrecognized parses back to the default rather than raising a problem.
                        // This is a hand-editable file and a misspelled clock format is not worth a warning
                        // popup on the way into the game.
                        case "timeFormat":
                            settings.timeFormat = UIClock.Parse(value);
                            break;

                        // The widget switches. Absent means on, which is what makes a config file written before
                        // these existed read as "show everything" rather than silently hiding the lot.
                        // showDesktopWidgets, the old master switch, is retired rather than read; it is listed
                        // with the other retired names below. A file written before it was removed loads with
                        // each widget's own choice intact -- which is the right answer even for someone who had
                        // the master off, because the control they used to turn everything off no longer exists
                        // to turn it back on.
                        case "showConditionsWidget":
                            settings.showConditionsWidget = !value.EqualsIgnoreCase("false");
                            break;

                        case "resizableTabs":
                            settings.resizableTabs = !value.EqualsIgnoreCase("false");
                            break;

                        case "showStuffDetails":
                            settings.showStuffDetails = !value.EqualsIgnoreCase("false");
                            break;

                        // Absent means on, so a config written before this existed keeps the strip the player
                        // already had rather than silently taking it away.
                        case "showArchitectInfoPanel":
                            settings.showArchitectInfoPanel = !value.EqualsIgnoreCase("false");
                            break;

                        case "richInspectPane":
                            settings.richInspectPane = !value.EqualsIgnoreCase("false");
                            break;

                        // Invariant, like every other number in this file: a height written on a machine that
                        // uses a comma for the decimal point should still parse here.
                        case "inspectPaneHeight":
                            settings.inspectPaneHeight = ParseFloat(value, 300f);
                            break;

                        case "showMinimapWidget":
                            settings.showMinimapWidget = !value.EqualsIgnoreCase("false");
                            break;

                        // Both fall back to their default rather than complaining, like the docks and the clock
                        // format above: this is a hand-editable file and a misspelled corner is not worth a
                        // warning on the way into the game.
                        case "minimapCorner":
                            settings.minimapCorner = ParseEnum(value, MinimapCorner.BottomRight);
                            break;

                        case "minimapSize":
                            settings.minimapSize = ParseEnum(value, MinimapSize.Medium);
                            break;

                        // Absent means on, so a config written before this existed keeps showing hostiles
                        // rather than silently hiding them from somebody who never asked.
                        case "showMinimapEnemies":
                            settings.showMinimapEnemies = !value.EqualsIgnoreCase("false");
                            break;

                        // Absent means on, matching the default, so an existing config gets the grouped bar
                        // rather than being opted out of the feature by omission.
                        case "showGroupedColonistBar":
                            settings.showGroupedColonistBar = !value.EqualsIgnoreCase("false");
                            break;

                        // Absent means off here, also matching the default: this one costs frames, so silence
                        // has to mean the cheap answer.
                        case "livePawnView":
                            settings.livePawnView = value.EqualsIgnoreCase("true");
                            break;

                        case "pawnViewRefresh":
                            settings.pawnViewRefresh = ParseEnum(value, PawnViewRefresh.Ms250);
                            break;

                        case "barWeaponDisplay":
                            settings.barWeaponDisplay = ParseEnum(value, BarWeaponDisplay.Never);
                            break;

                        case "barHideHeadgear":
                            settings.barHideHeadgear = value.EqualsIgnoreCase("true");
                            break;

                        // Invariant, like letterRowWidth above and for the same reason: a position written on
                        // a machine that uses a comma for the decimal point should still parse here.
                        case "minimapX":
                            settings.minimapX = ParseFloat(value, -1f);
                            break;

                        case "minimapY":
                            settings.minimapY = ParseFloat(value, -1f);
                            break;

                        case "favoritePlants":
                            settings.favoritePlants = value ?? string.Empty;
                            break;

                        case "hiddenPawnCategories":
                            settings.hiddenPawnCategories = value ?? string.Empty;
                            break;

                        // Absent means on, so a config file written before this existed gets the feature
                        // rather than having it silently withheld.
                        case "roomNameLabels":
                            settings.roomNameLabels = !value.EqualsIgnoreCase("false");
                            break;

                        case "roomLabelMinimumCells":
                            int cells;

                            // Invariant, like every other number in this file: a shared config must not
                            // reparse differently because the machine writes digits another way.
                            settings.roomLabelMinimumCells = int.TryParse(value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out cells)
                                ? Mathf.Clamp(cells, MinimumRoomCellsFloor, MinimumRoomCellsCeiling)
                                : 12;

                            break;

                        // Parsed by name and falling back rather than complaining, like the clock format: this
                        // is a hand editable file and a misspelled font is not worth a popup on the way in.
                        case "roomLabelFace":
                            settings.roomLabelFace = ParseFace(value);

                            break;

                        // Both read "absent means off", which is what makes a config file written before
                        // compression existed keep writing plain saves rather than silently changing format
                        // on a player who never asked.
                        case "compressSaves":
                            settings.compressSaves = value.EqualsIgnoreCase("true");
                            break;

                        case "compressAutosaves":
                            settings.compressAutosaves = value.EqualsIgnoreCase("true");
                            break;

                        case "notifyPhinixChat":
                            settings.notifyPhinixChat = !value.EqualsIgnoreCase("false");
                            break;

                        case "warnStalledBills":
                            settings.warnStalledBills = !value.EqualsIgnoreCase("false");
                            break;

                        // Read the strict way round, so anything but an explicit true leaves the game's own rules
                        // alone. This one changes behavior rather than appearance and defaults off.
                        case "penAnimalsUseAreas":
                            settings.penAnimalsUseAreas = value.EqualsIgnoreCase("true");
                            break;

                        // Read the permissive way round, matching the default: anything but an explicit false
                        // leaves the feature on. It grants a switch rather than changing behaviour on its own,
                        // so a file written before this setting existed should come back with it available.
                        case "allowCommunalBeds":
                            settings.allowCommunalBeds = !value.EqualsIgnoreCase("false");
                            break;

                        case "salvageAncientWrecks":
                            settings.salvageAncientWrecks = value.EqualsIgnoreCase("true");
                            break;

                        case "barracksAreNeutral":
                            settings.barracksAreNeutral = value.EqualsIgnoreCase("true");
                            break;

                        // The same strict reading, and for a stronger reason: this one decides whether a tool
                        // that rewrites pawns exists. A malformed value must leave it absent.
                        case "characterEditor":
                            settings.characterEditor = value.EqualsIgnoreCase("true");
                            break;

                        // Both default on, so both read the permissive way: absent means the player gets the
                        // feature rather than having it withheld by a config file written before it existed.
                        case "musicPlayer":
                            settings.musicPlayer = !value.EqualsIgnoreCase("false");
                            break;

                        case "showMusicWidget":
                            settings.showMusicWidget = !value.EqualsIgnoreCase("false");
                            break;

                        case "researchTab":
                            settings.researchTab = !value.EqualsIgnoreCase("false");
                            break;

                        case "anomalyScript":
                            settings.anomalyScript = Research.ResearchScripts.Parse(value);
                            break;

                        // Taken as written, because the keys are validated where they are used: a key naming a
                        // switch we do not have resolves to no defs and is ignored, which is what should happen
                        // to a setting written by a newer version of the mod than this one.
                        case "disabledThreats":
                            settings.disabledThreats = value ?? string.Empty;
                            break;

                        case "gravshipOverrides":
                            settings.gravshipOverrides = value.EqualsIgnoreCase("true");
                            break;

                        // Both of these carry a sentinel for "the game's own", so an unparseable value falls back
                        // to the sentinel rather than to a number: a typo here must not quietly resize somebody's
                        // gravship.
                        case "gravEngineRadius":
                            settings.gravEngineRadius = ParseFloat(value, 0f);
                            break;

                        case "gravshipUnlimitedTiles":
                            settings.gravshipUnlimitedTiles = value.EqualsIgnoreCase("true");
                            break;

                        case "gravExtenderMax":
                            int extenders;

                            settings.gravExtenderMax = int.TryParse(value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out extenders)
                                ? Mathf.Clamp(extenders, -1, Gravships.GravshipTuning.ExtenderCeiling)
                                : -1;

                            break;

                        // Thirty on all four, matching the fields, so a file written before these existed reads
                        // as a fresh install rather than as four decisions the player never made. An element
                        // holding something not on the offered list is treated the same way.
                        case "siteFadeSettlementDays":
                            settings.siteFadeSettlementDays = ParseFadeDays(value, 30);
                            break;

                        case "siteFadeLaunchDays":
                            settings.siteFadeLaunchDays = ParseFadeDays(value, 30);
                            break;

                        case "siteFadeCampDays":
                            settings.siteFadeCampDays = ParseFadeDays(value, 30);
                            break;

                        case "siteFadeLandmarkDays":
                            settings.siteFadeLandmarkDays = ParseFadeDays(value, 30);
                            break;

                        // Defaults on, so it is read the other way round: only an explicit false turns it off.
                        case "autoCutBlightedPlants":
                            settings.autoCutBlightedPlants = !value.EqualsIgnoreCase("false");
                            break;

                        case "quietResearchCompletion":
                            settings.quietResearchCompletion = !value.EqualsIgnoreCase("false");
                            break;

                        case "researchGrouping":
                            settings.researchGrouping =
                                Research.ResearchGroupings.Store(Research.ResearchGroupings.Parse(value));
                            break;

                        case "showMineableOverlay":
                            settings.showMineableOverlay = !value.EqualsIgnoreCase("false");
                            break;
                        case "showBlastRadius":
                            settings.showBlastRadius = !value.EqualsIgnoreCase("false");
                            break;
                        case "pawnDetailsOnOffers":
                            settings.pawnDetailsOnOffers = !value.EqualsIgnoreCase("false");
                            break;
                        case "quietIdleAlert":
                            settings.quietIdleAlert = !value.EqualsIgnoreCase("false");
                            break;

                        case "customTradeWindow":
                            settings.customTradeWindow = !value.EqualsIgnoreCase("false");
                            break;
                        case "customCaravanWindow":
                            settings.customCaravanWindow = !value.EqualsIgnoreCase("false");
                            break;
                        case "customCommsWindow":
                            settings.customCommsWindow = !value.EqualsIgnoreCase("false");
                            break;
                        case "beaconReadout":
                            settings.beaconReadout = !value.EqualsIgnoreCase("false");
                            break;

                        case "tradeBeaconRadius":
                            // An unreadable value gives RimWorld's own radius rather than the smallest or the
                            // largest, which is the answer that changes nothing for somebody whose file was
                            // corrupted. The range is clamped again where it is read.
                            settings.tradeBeaconRadius =
                                float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                                    out float beacon)
                                    ? Mathf.Clamp(beacon, 3f, 23.7f)
                                    : 7.9f;

                            break;

                        case "maxBillsPerBench":
                            int bills;

                            // Invariant like every other number here. Clamped by BillCap on read rather than
                            // here, so this only has to decide what an unparseable value means, which is the
                            // default rather than vanilla's fifteen: a corrupted line should not quietly take a
                            // feature away.
                            settings.maxBillsPerBench = int.TryParse(value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out bills)
                                ? bills
                                : 60;

                            break;

                        case "favoriteRecipes":
                            settings.favoriteRecipes = value ?? string.Empty;
                            break;

                        case "restyleCommandButtons":
                            settings.restyleCommandButtons = !value.EqualsIgnoreCase("false");
                            break;

                        case "defaultIngredientRadius":
                            // Clamped to what the bill dialog itself allows, so a hand edited file cannot produce
                            // a radius no bill could ever have been given through the interface.
                            settings.defaultIngredientRadius =
                                float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                                    out float radius)
                                    ? Mathf.Clamp(radius, 3f, 999f)
                                    : 999f;

                            break;

                        // Absent means on, matching the default: a config written before this existed gets the
                        // quiet log rather than silently keeping the noisy one.
                        case "suppressPhinixInfoLog":
                            settings.suppressPhinixInfoLog = !value.EqualsIgnoreCase("false");
                            break;

                        // The notification settings. The three restyle switches read "absent means on", so a
                        // config written before they existed keeps the drawing the player already had.
                        case "restyleMessages":
                            settings.restyleMessages = !value.EqualsIgnoreCase("false");
                            break;

                        case "restyleLetters":
                            settings.restyleLetters = !value.EqualsIgnoreCase("false");
                            break;

                        case "mentalBreakLetters":
                            settings.mentalBreakLetters = !value.EqualsIgnoreCase("false");
                            break;

                        case "restyleAlerts":
                            settings.restyleAlerts = !value.EqualsIgnoreCase("false");
                            break;

                        case "messageDock":
                            settings.messageDock = ParseDock(value, NotificationDock.TopLeft);
                            break;

                        case "letterDock":
                            settings.letterDock = ParseDock(value, NotificationDock.BottomRight);
                            break;

                        case "alertDock":
                            settings.alertDock = ParseDock(value, NotificationDock.BottomRight);
                            break;

                        case "letterRowWidth":
                            float width;

                            // Invariant, not the machine's locale. A settings file is shared and hand-edited, and
                            // a number written with a decimal point should not stop parsing because the game is
                            // running in a language that writes it with a comma.
                            settings.letterRowWidth = float.TryParse(value, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out width)
                                ? width
                                : 250f;

                            break;

                        // Reads the opposite way round to the widgets below it, because this one defaults to off:
                        // absent means off here.
                        case "showTimeWidget":
                            settings.showTimeWidget = value.EqualsIgnoreCase("true");
                            break;

                        case "showTemperatureWidget":
                            settings.showTemperatureWidget = !value.EqualsIgnoreCase("false");
                            break;

                        case "showSpeedControlsWidget":
                            settings.showSpeedControlsWidget = !value.EqualsIgnoreCase("false");
                            break;

                        case "showDateWidget":
                            settings.showDateWidget = !value.EqualsIgnoreCase("false");
                            break;

                        case "showWeatherWidget":
                            settings.showWeatherWidget = !value.EqualsIgnoreCase("false");
                            break;

                        // showSeasonWidget is retired, and listed with the other retired names below. The season
                        // is drawn by DateReadout as part of the date block, so it is the date switch that
                        // governs it.

                        case "showGlobalControlsWidget":
                            settings.showGlobalControlsWidget = !value.EqualsIgnoreCase("false");
                            break;

                        // Reads the opposite way round to the others, because this one defaults to off: absent
                        // means off here, where absent means on for every widget above.
                        case "showPerformanceWidget":
                            settings.showPerformanceWidget = value.EqualsIgnoreCase("true");
                            break;

                        // Retired settings. Accepted silently so an older config file does not raise a warning
                        // about something the player never chose to write -- these were written by a previous
                        // version of this mod, not typed by anyone, so there is nothing for them to act on.
                        //
                        // A name has to be listed here rather than merely dropped from the switch above: falling
                        // through to default is what produces the warning, and a setting this mod removed is not
                        // an unknown one. The warning is worth keeping for names that really are unrecognized,
                        // which is what a typo or a config from a newer version looks like.
                        //
                        // They stay listed permanently. The file is only rewritten when something is saved, so a
                        // player who never changes a setting keeps the old element indefinitely, and a list that
                        // was pruned after a release or two would start warning about it again.
                        case "showCalendarWidget":
                            settings.showCalendarWidget = !value.EqualsIgnoreCase("false");
                            break;

                        case "vsync":
                            settings.vsync = !value.EqualsIgnoreCase("false");

                            break;

                        case "showQuickOrders":
                            settings.showQuickOrders = !value.EqualsIgnoreCase("false");

                            break;

                        case "hideCalendarBirthdays":
                            settings.hideCalendarBirthdays = value.EqualsIgnoreCase("true");

                            break;

                        case "showExplicitStoryEvents":
                            settings.showExplicitStoryEvents = value.EqualsIgnoreCase("true");
                            break;

                        // modernDebugLog and minimapScale were each here briefly and never shipped. Listed
                        // anyway: a local test run may have written them, and a warning about a setting nobody
                        // chose is exactly the noise this list exists to prevent.
                        case "modernDebugLog":
                        case "minimapScale":
                        case "showBarButton":
                        case "showDesktopWidgets":
                        case "showSeasonWidget":
                        case "showSpeedGlyphs":
                            break;

                        default:
                            Log.Warning(UILogTag.Prefix + $"Unknown setting <{field.Name}>; ignored.");
                            break;
                    }
                }

                return settings;
            }
            catch (Exception ex)
            {
                // Reported rather than logged and forgotten. Discarding the file silently would look
                // like a hand-edit had no effect.
                UIConfigProblems.Report(path, new List<string>
                {
                    "Could not be read, so the previous settings are still in use: " + ex.Message
                });

                return new UIOverhaulSettingsFile();
            }
        }

        public void Save()
        {
            string path = FilePath;

            try
            {
                // So the watcher does not mistake our own write for someone editing the file.
                UIConfigWatcher.NotifySelfWrite();

                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

                XmlWriterSettings writerSettings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    Encoding = new UTF8Encoding(false)
                };

                using (XmlWriter writer = XmlWriter.Create(path, writerSettings))
                {
                    writer.WriteStartDocument();
                    writer.WriteComment(" Settings for Gideon's UI Overhaul. Written by the UI options "
                                        + "page; safe to hand-edit. ");
                    writer.WriteStartElement("UIOverhaulSettings");
                    writer.WriteElementString("activePalette", activePalette ?? "");
                    writer.WriteElementString("debugLogging", debugLogging ? "true" : "false");
                    writer.WriteElementString("showLoadingConsole", showLoadingConsole ? "true" : "false");
                    writer.WriteElementString("skipDevActionConfirm",
                        skipDevActionConfirm ? "true" : "false");
                    writer.WriteElementString("fullscreenOnStartup",
                        fullscreenOnStartup ? "true" : "false");
                    writer.WriteElementString("timeFormat", timeFormat.ToString());

                    writer.WriteElementString("resizableTabs", resizableTabs ? "true" : "false");
                    writer.WriteElementString("showStuffDetails", showStuffDetails ? "true" : "false");
                    writer.WriteElementString("showArchitectInfoPanel",
                        showArchitectInfoPanel ? "true" : "false");
                    writer.WriteElementString("richInspectPane", richInspectPane ? "true" : "false");
                    writer.WriteElementString("inspectPaneHeight",
                        inspectPaneHeight.ToString(CultureInfo.InvariantCulture));
                    writer.WriteElementString("showMinimapWidget",
                        showMinimapWidget ? "true" : "false");
                    writer.WriteElementString("minimapCorner", minimapCorner.ToString());
                    writer.WriteElementString("minimapSize", minimapSize.ToString());
                    writer.WriteElementString("showMinimapEnemies",
                        showMinimapEnemies ? "true" : "false");
                    writer.WriteElementString("showGroupedColonistBar",
                        showGroupedColonistBar ? "true" : "false");
                    writer.WriteElementString("livePawnView", livePawnView ? "true" : "false");
                    writer.WriteElementString("pawnViewRefresh", pawnViewRefresh.ToString());
                    writer.WriteElementString("barWeaponDisplay", barWeaponDisplay.ToString());
                    writer.WriteElementString("barHideHeadgear", barHideHeadgear ? "true" : "false");
                    writer.WriteElementString("minimapX",
                        minimapX.ToString(CultureInfo.InvariantCulture));
                    writer.WriteElementString("minimapY",
                        minimapY.ToString(CultureInfo.InvariantCulture));
                    writer.WriteElementString("favoritePlants", favoritePlants ?? string.Empty);
                    writer.WriteElementString("hiddenPawnCategories",
                        hiddenPawnCategories ?? string.Empty);
                    writer.WriteElementString("roomNameLabels", roomNameLabels ? "true" : "false");
                    writer.WriteElementString("roomLabelMinimumCells",
                        roomLabelMinimumCells.ToString(CultureInfo.InvariantCulture));
                    writer.WriteElementString("roomLabelFace", roomLabelFace.ToString());
                    writer.WriteElementString("compressSaves", compressSaves ? "true" : "false");
                    writer.WriteElementString("compressAutosaves",
                        compressAutosaves ? "true" : "false");
                    writer.WriteElementString("notifyPhinixChat", notifyPhinixChat ? "true" : "false");
                    writer.WriteElementString("warnStalledBills", warnStalledBills ? "true" : "false");
                    writer.WriteElementString("penAnimalsUseAreas", penAnimalsUseAreas ? "true" : "false");
                    writer.WriteElementString("allowCommunalBeds", allowCommunalBeds ? "true" : "false");
                    writer.WriteElementString("salvageAncientWrecks",
                        salvageAncientWrecks ? "true" : "false");
                    writer.WriteElementString("barracksAreNeutral", barracksAreNeutral ? "true" : "false");
                    writer.WriteElementString("characterEditor", characterEditor ? "true" : "false");
                    writer.WriteElementString("musicPlayer", musicPlayer ? "true" : "false");
                    writer.WriteElementString("showMusicWidget", showMusicWidget ? "true" : "false");
                    writer.WriteElementString("researchTab", researchTab ? "true" : "false");
                    writer.WriteElementString("anomalyScript", anomalyScript.ToString());
                    writer.WriteElementString("disabledThreats", disabledThreats ?? string.Empty);
                    writer.WriteElementString("gravshipOverrides", gravshipOverrides ? "true" : "false");
                    writer.WriteElementString("gravEngineRadius",
                        gravEngineRadius.ToString(CultureInfo.InvariantCulture));
                    writer.WriteElementString("gravshipUnlimitedTiles",
                        gravshipUnlimitedTiles ? "true" : "false");
                    writer.WriteElementString("gravExtenderMax",
                        gravExtenderMax.ToString(CultureInfo.InvariantCulture));
                    writer.WriteElementString("siteFadeSettlementDays",
                        siteFadeSettlementDays.ToString(CultureInfo.InvariantCulture));
                    writer.WriteElementString("siteFadeLaunchDays",
                        siteFadeLaunchDays.ToString(CultureInfo.InvariantCulture));
                    writer.WriteElementString("siteFadeCampDays",
                        siteFadeCampDays.ToString(CultureInfo.InvariantCulture));
                    writer.WriteElementString("siteFadeLandmarkDays",
                        siteFadeLandmarkDays.ToString(CultureInfo.InvariantCulture));
                    writer.WriteElementString("autoCutBlightedPlants",
                        autoCutBlightedPlants ? "true" : "false");
                    writer.WriteElementString("quietResearchCompletion",
                        quietResearchCompletion ? "true" : "false");
                    writer.WriteElementString("researchGrouping", researchGrouping ?? "theme");
                    writer.WriteElementString("showMineableOverlay",
                        showMineableOverlay ? "true" : "false");
                    writer.WriteElementString("showBlastRadius", showBlastRadius ? "true" : "false");
                    writer.WriteElementString("pawnDetailsOnOffers",
                        pawnDetailsOnOffers ? "true" : "false");
                    writer.WriteElementString("quietIdleAlert", quietIdleAlert ? "true" : "false");
                    writer.WriteElementString("tradeBeaconRadius",
                        tradeBeaconRadius.ToString(CultureInfo.InvariantCulture));
                    writer.WriteElementString("customTradeWindow",
                        customTradeWindow ? "true" : "false");
                    writer.WriteElementString("customCaravanWindow",
                        customCaravanWindow ? "true" : "false");
                    writer.WriteElementString("customCommsWindow",
                        customCommsWindow ? "true" : "false");
                    writer.WriteElementString("beaconReadout", beaconReadout ? "true" : "false");
                    writer.WriteElementString("maxBillsPerBench",
                        maxBillsPerBench.ToString(CultureInfo.InvariantCulture));
                    writer.WriteElementString("favoriteRecipes", favoriteRecipes ?? string.Empty);
                    writer.WriteElementString("restyleCommandButtons",
                        restyleCommandButtons ? "true" : "false");
                    writer.WriteElementString("defaultIngredientRadius",
                        defaultIngredientRadius.ToString(CultureInfo.InvariantCulture));
                    writer.WriteElementString("suppressPhinixInfoLog",
                        suppressPhinixInfoLog ? "true" : "false");

                    writer.WriteElementString("restyleMessages", restyleMessages ? "true" : "false");
                    writer.WriteElementString("restyleLetters", restyleLetters ? "true" : "false");
                    writer.WriteElementString("restyleAlerts", restyleAlerts ? "true" : "false");
                    writer.WriteElementString("mentalBreakLetters",
                        mentalBreakLetters ? "true" : "false");
                    writer.WriteElementString("messageDock", messageDock.ToString());
                    writer.WriteElementString("letterDock", letterDock.ToString());
                    writer.WriteElementString("alertDock", alertDock.ToString());

                    // Invariant, matching how it is read. A width written with the machine's decimal separator
                    // would fail to parse on a machine that writes it differently, which is a settings file that
                    // silently resets when it is shared.
                    writer.WriteElementString("letterRowWidth",
                        letterRowWidth.ToString(CultureInfo.InvariantCulture));

                    writer.WriteElementString("showCalendarWidget", showCalendarWidget ? "true" : "false");
                    writer.WriteElementString("showExplicitStoryEvents",
                        showExplicitStoryEvents ? "true" : "false");
                    writer.WriteElementString("vsync", vsync ? "true" : "false");
                    writer.WriteElementString("showQuickOrders", showQuickOrders ? "true" : "false");
                    writer.WriteElementString("hideCalendarBirthdays",
                        hideCalendarBirthdays ? "true" : "false");
                    writer.WriteElementString("showTimeWidget", showTimeWidget ? "true" : "false");
                    writer.WriteElementString("showTemperatureWidget", showTemperatureWidget ? "true" : "false");
                    writer.WriteElementString("showSpeedControlsWidget",
                        showSpeedControlsWidget ? "true" : "false");
                    writer.WriteElementString("showDateWidget", showDateWidget ? "true" : "false");
                    writer.WriteElementString("showWeatherWidget", showWeatherWidget ? "true" : "false");
                    writer.WriteElementString("showConditionsWidget",
                        showConditionsWidget ? "true" : "false");
                    writer.WriteElementString("showGlobalControlsWidget",
                        showGlobalControlsWidget ? "true" : "false");
                    writer.WriteElementString("showPerformanceWidget",
                        showPerformanceWidget ? "true" : "false");
                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
            }
            catch (Exception ex)
            {
                Log.Error(UILogTag.Prefix + $"Could not write {path}.\n{ex}");
            }
        }
    }
}
