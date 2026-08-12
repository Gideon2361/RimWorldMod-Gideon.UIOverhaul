# What this mod changes about the game

The [framework](README.md) is the reusable half. This page is the other half: the direct changes
Gideon's UI Overhaul makes to RimWorld itself, and the Harmony patches behind them.

It is here for two audiences — anyone diagnosing a visual conflict, and anyone patching the same
methods and needing to know we are already there.

Every patch listed here fails soft. If our drawing throws, the exception is logged once, vanilla's
drawing resumes for the rest of the session, and the game keeps running. A restyled button is never
worth a menu that will not draw.

That fallback is shared, not per patch: one failure reverts **all** of our UI element painting. A
palette that cannot be drawn from is not a problem that applies to buttons but spares option rows, and
reverting the lot keeps the UI internally consistent rather than half restyled.

## Buttons

| Patched | Kind | Effect |
|---|---|---|
| `Verse.Widgets.DrawButtonGraphic(Rect)` | Prefix, returns false | Button background drawn from the active palette |
| `Verse.Widgets.DrawOptionUnselected(Rect)` | Prefix, returns false | Option-list row background |
| `Verse.Widgets.DrawOptionSelected(Rect)` | Prefix, returns false | Selected option-list row background |

`DrawButtonGraphic` is the whole of vanilla's button background. Its entire body picks one of
`ButtonBGAtlas`, `ButtonBGAtlasMouseover` or `ButtonBGAtlasClick` by hover and mouse-button state, then
calls `DrawAtlas` — and it is the only reader of all three. Replacing it restyles every standard
button in the game in one place:

- both `Widgets.ButtonText` overloads, and both `Widgets.ButtonTextDraggable` overloads
- the three places that call it directly: the health overview tab, the entity tab, the prisoner tab
- **any mod** that calls `Widgets.ButtonText`, with no cooperation needed from it

### What stays vanilla

Only the background is ours. The mouseover sound, click and drag detection, the label and its color,
the text anchor, the word-wrap rule and inactive-button behavior all live in `Widgets.ButtonTextWorker`
and are untouched.

That is deliberate, and it is why the patch is here rather than on the worker. Patching the worker
would mean either reimplementing all of the above, or setting its `drawBackground` argument to false to
suppress the background — and that argument does double duty. It also selects the text anchor
(`MiddleCenter` when true) and whether the mouseover text color applies. Turning it off to get rid of a
background would quietly have changed how every button label in the game is aligned and tinted.

### Roles used

| Part | Role |
|---|---|
| Fill | `SurfaceRaised` |
| Hover | `HoverOverlay`, drawn over the fill |
| Pressed | `PressedOverlay`, drawn over the fill |
| Border | `Border`, or `BorderFocused` while hovered |

`HoverOverlay` and `PressedOverlay` carry alpha as part of their value — they are washes over the fill,
not replacements for it — so a palette decides for itself how strong its own feedback is. Restyle
buttons by editing a [UIColorPaletteDef](UIColorPaletteDef.md); no code change is needed.

Buttons come out square rather than subtly rounded like vanilla's atlas, which is the flat look the
rest of this mod uses.

## Option list rows

The selectable rows in an option list are **not buttons**, which is why restyling buttons left them
looking untouched — most visibly the mod list in Options, sitting directly beside real buttons that had
been restyled. Vanilla draws them from four static `Color` fields (`OptionSelectedBGFillColor`,
`OptionSelectedBGBorderColor`, and the `Unselected` pair) and never reads a button atlas:

```
DrawOptionBackground(rect, selected)
  -> selected ? DrawOptionSelected(rect) : DrawOptionUnselected(rect)
  -> DrawHighlightIfMouseover(rect)
```

The two leaf methods are patched rather than `DrawOptionBackground`, so vanilla still calls
`DrawHighlightIfMouseover` afterwards and hover feedback is unchanged. That is also why these rows draw
no `HoverOverlay` of their own — it would light the row up twice.

They use the **button** fill deliberately, so a row and a button beside it match:

| Part | Role |
|---|---|
| Fill | `SurfaceRaised` |
| Selected | `SelectionOverlay`, drawn over the fill |
| Border | `Border`, or `BorderFocused` when selected |

One patch pair covers every option list in the game: the mod list and category rows in Options,
scenario selection, storyteller selection, starting pawns, the entity codex, xenotype creation,
ideoligion presets, and choosing new wanderers.

## Checkboxes

| Patched | Kind | Effect |
|---|---|---|
| `Verse.Widgets.CheckboxDraw` | Prefix, returns false | Every boolean checkbox, drawn from the palette |
| `Verse.Widgets.CheckboxMulti` | Postfix, paints over | The tri-state boxes in thing filter trees |

All three states are drawn by one method, `UIElementPainter.PaintCheckbox`, which is also what
[`UICheckboxControl`](UICheckboxControl.md) draws with. That is the point of the arrangement: a change to
what a checkbox looks like happens in one place and reaches our windows and the game's at once. It takes
vanilla's own `MultiCheckboxState` as its state, so neither seam needs a translation layer.

| Part | Role |
|---|---|
| Fill | `SurfaceSunken` |
| Border | `Accent` when checked or partial, `Border` when off, `TextDisabled` when disabled |
| Mark | `Accent`, a filled square when checked and a bar when partial |

Every color is multiplied by the ambient `GUI.color`, because vanilla's textures were drawn at it — a
caller that dims a whole row dimmed its checkbox too, and painting at full strength would leave a bright
box on a greyed-out row. Sizes are proportional to the box rather than fixed, since callers of
`CheckboxDraw` choose their own size.

### Why one is a prefix and the other a postfix

`CheckboxDraw` is pure drawing: dim if disabled, pick one of two textures, draw it, restore the color. It
is the only reader of `CheckboxOnTex` and `CheckboxOffTex` on that path, and replacing it restyles every
checkbox reached through `Widgets.Checkbox`, `Widgets.CheckboxLabeled` and
`Widgets.CheckboxLabeledSelectable` — which is all of them, in options, mod settings, bill details and
storage filters, plus any mod that draws one. Behavior is untouched: click handling, paint-dragging across
a column of boxes, the mouseover sound and the label all live in the callers.

A caller that passed its own `texChecked` or `texUnchecked` is handed straight back to vanilla. Supplying
art is a request for that specific image, and overpainting it would lose the meaning it carried.

`CheckboxMulti` is not a drawing method. It bundles the draw with `ButtonImageDraggable`, the
paint-dragging state held in two private statics, the mouseover sound and the state cycling, and it
returns the new state. Replacing it would mean reimplementing all of that and keeping the
reimplementation correct across game updates — for a widget whose failure mode is a filter tree that no
longer sets filters, not a cosmetic one. Painting over it leaves every bit of that vanilla; our box is
opaque and fills the same rect, so the stock texture is simply covered.

One visible difference falls out of that: the tri-state box shows the state the method just returned
rather than the one it was called with, so a click registers a frame earlier than it used to.

## Radio buttons

| Patched | Kind | Effect |
|---|---|---|
| `Verse.Widgets.RadioButtonDraw` | Prefix, returns false | Every radio button, drawn from the palette |

`RadioButtonDraw` is the whole of vanilla's radio button drawing and the only reader of `RadioButOnTex`
and `RadioButOffTex`. Its body picks one of the two, greys the color if disabled, draws it into a 24px
square, restores the color. Its only callers are `Widgets.RadioButton` and `Widgets.RadioButtonLabeled`, so
replacing it covers every radio button in the game and in any mod. Nothing behavioral is touched: the row
hit target, the `Tick_Tiny` click sound and the label all live in those callers.

It is **private**, which is a version risk taken deliberately. The alternative is patching the two public
callers and reimplementing their label layout and hit handling to reach the drawing. If a future version
renames it, the patch fails to apply, Harmony reports that at startup, and radio buttons stay vanilla —
the same state as before this existed.

| Part | Role |
|---|---|
| Outer circle | `Accent` selected, `Accent` at 55% hovered and unselected, `TextDisabled` disabled, `Border` otherwise |
| Interior, including the 2px gap | `WindowBackground` when unselected, `AccentMuted` when selected |
| Inner circle | Absent when unselected; `WindowBackground` when selected |

Selection is carried by the ring, and outranks hover. Hover is the accent at part strength rather than at
full, because full accent is what selected means — lighting an unselected button to the same value made the
two indistinguishable until the pointer moved away. Hover is tested
against the circle here, because `RadioButtonDraw` is handed a position and no row —
[`UIRadioButtonControl`](UIRadioButtonControl.md) passes row hover instead, since its whole row is
clickable.

Drawn as three concentric discs, because IMGUI cannot stroke a circle: outer disc in the ring color, a
second covering all but a rim of it, and the mark inside that. Each is a tint of one generated white disc
texture (`UIShapes.Disc`) rather than an art file, so it follows the palette and there is no file to go
missing. Ring thickness and gap are derived from the drawn size and come out at 2px each at the 24px these
actually draw at.

## Growing zones

Ported from Growing Zones Plus, in `UIOverhaul/Features/GrowZones`. A growing-zone bill system, a
colony-wide grow-zone tab, and a plant picker that shows light, temperature, skill, work, lifespan,
yield and hazard information per plant instead of a flat list of names.

> **Do not run this alongside Growing Zones Plus.** Both define `GZP_GrowPlant`, `GZP_GrowZones` and
> the same repeat modes, both patch the same growing-zone methods, and both register a growing-zone
> tab. Enable one or the other.

`GZP_` defNames were kept exactly as they were rather than renamed to match this mod. Saved games
store bill and repeat-mode defNames, so renaming them would silently drop every existing bill on load.

### Theme

All of it draws from the active [UIColorPaletteDef](UIColorPaletteDef.md) — nothing is hardcoded. The
framework palette was originally derived from this feature's own hex values, so each old constant maps
onto the role that already held its value:

| Was | Hex | Now |
|---|---|---|
| `BG` | `#15191D` | `WindowBackground` |
| `PanelBG` | `#1B1F23` | `PanelBackground` |
| `BGD` | `#0E1013` | `SurfaceSunken` |
| `BGL` | `#2F3337` | `SurfaceRaised` — the role the button patch paints with, so buttons match |
| `Stat` | `#E3E3E3` | `TextPrimary` |
| `TextDim` | `#9EA6B2` | `TextSecondary` |
| `Good` / `Bad` / `Warn` / `Cold` | | `Success` / `Danger` / `Warning` / `Info` |
| hazard washes | | `Danger` / `Warning` / `Success` at the original alphas |

Three things that were fixed rather than transcribed, because they only worked on a dark theme:

- **Button hover** brightened the fill by a fixed 1.25×, which *darkens* a light theme. It now draws
  the palette's `HoverOverlay` over the fill, the same two steps the vanilla button patch takes.
- **The scrollbar thumb** was translucent white, invisible on a light background. It now comes from
  `TextSecondary`.
- **The dim scrim** on an inactive row was flat black, which reads as a hole punched in a light page.
  It is now the window color at low alpha, so the row recedes instead.

### Why `Command_SetPlantToGrow` is not patched

Vanilla opens a `FloatMenu` of plant names from `Verse.Command_SetPlantToGrow.ProcessInput`. That is
left alone on purpose: with this feature enabled every growing zone is a `Zone_GrowingPlus`, which
carries its own gizmo, so for zones the vanilla command is never reached and replacing it would be dead
code.

It is still reached for `Building_PlantGrower` things this feature does not replace. The hydroponics
patch only retargets `HydroponicsBasin`, so `PlantPot` and any modded plant grower keep the vanilla
float menu. That is acceptable — a pot grows one plant, whereas the zone picker exists to compare crops
against each other — but it does mean the float menu is not gone from the game.

### Plant notices

Hazard and benefit data is read from `Mods/gideon.uioverhaul/PlantNotices.xml` in any active mod — the
same convention as the loading screen, where the nested folder names the mod the data is handed to. See
[PlantNotices_Integration.txt](PlantNotices_Integration.txt).

The old Growing Zones Plus path, `Mods/babylettuce.growingzone/GZP_PlantHazardCache.xml`, is **still
read**, so mods that already ship a table for that mod keep working. It is read first, so a mod
shipping both files has its newer one win.

## Work tab

Replaced outright, in `UIOverhaul/Features/Work`. A card per colonist with a face portrait, grouped by
which map they are on, and a priority box per work type with the pawn's relevant skill under it.

The portrait is a face crop — `PortraitsCache.Get` takes a camera offset and zoom, which is what makes that
possible with no patching — cropped to a circle and sitting on a `SurfaceSunken` disc.

The crop is done by **covering, not clipping**, because IMGUI cannot mask one texture with another and there is
no shader to clip a RenderTexture with. Three draws, in order:

1. `UIShapes.Disc` in `SurfaceSunken` — the render is transparent everywhere the pawn is not, so this is what
   the head sits on rather than the bare card.
2. the square render.
3. `UIShapes.DiscCutout` — opaque everywhere *except* an inscribed circle — tinted with the color behind it,
   painting out the corners.

Both shapes are generated, and the cutout's alpha ramp is the complement of the disc's, so the two meet without
a seam.

Step 3's tint has to be whatever the row would have shown there, which is the one wrinkle: plain
`PanelBackground` for most rows, but a downed row's card is under a stripe wash, and flat card color over that
would leave four clean triangles around the head. The tint for those rows is the wash at **half** alpha, which
is that pattern's average since the tile is half stripe and half gap. Indistinguishable from stripes across the
7px it covers — and cheaper than re-drawing the wash, which would put stripes over the pawn's face.

The grid is [`UIDesignatorTabControl`](UIDesignatorTabControl.md). Everything true of any tab of this shape is
there — the pinned heading, the leaning titles and their bands, the section headings, the column banding, the
scroll view, the layer order. What stays in the work tab is what only it knows: which pawns there are, what a
cell contains, and what a row's status means. So if the *shape* of the grid looks wrong, the fix belongs in the
control and every tab built on it gets it.

The window asks for whatever width the grid needs — name column, tools column, and 44px per work type —
capped at the screen. That is derived rather than a fixed number because the column count is not ours to
know: 23 work types ship with Core and the DLCs, and every mod that adds one widens the grid. Anything that
still does not fit scrolls sideways as before. With the DLCs installed that comes to about 1344px.

### Column titles run diagonally

Titles lean 60 degrees up from horizontal, rotated about the bottom of their own column, and are
capitalized per word — "Bed Rest", not "bed rest". `labelShort` is the compact name the game already keeps
for this purpose, authored lowercase for use mid-sentence; a column heading is not mid-sentence.

The rotation is what buys the narrow columns. Upright, a column had to be as wide as its title, so
"Construction" set the width of a column whose contents are a 26px box — and the grid paid for the longest
word in every column across its whole height. On the diagonal a title can be as long as it likes.

What sets the width now is the skill readout under each box: "skill 12" at `Tiny`. Narrowing further would
clip the number, which is the one thing in a cell that has to be read rather than recognized.

Its **height** is taken from the cell rather than fixed, because the font is not fixed either.
`Text.Font = GameFont.Tiny` is a request: the setter substitutes `Small` whenever `TinyFontSupported` is
false, which covers a language whose `canBeTiny` is false, the "disable tiny text" preference, the Steam
Deck, and any draw during a long event. `Small`'s line box is around half again `Tiny`'s. Since
`Widgets.Label` hands its rect to `GUI.Label` as the clip rectangle — untouched apart from UI-scale
snapping — a height tuned for one font clips the other, and a `MiddleCenter` anchor spends the overflow at
both ends, shaving the tops of the digits. The readout therefore takes the whole strip between the box and
the bottom of the cell, floored at `Text.LineHeight`: its top is below the box by construction, so no font
size can push the number back into it.

Every other column carries a band behind its title, in `SurfaceSunken` against the header's
`PanelBackground`, so each title has a lane to follow down to the column it names. Alternating rather than
banding every column: consecutive stripes need a separator between them, and a stripe beside a stripe reads
as neither.

The banding continues down the grid, but as a **wash** over the row card rather than a solid fill — that is
the difference between reading as a column and reading as a hole cut in the row. It is drawn after the row's
status wash and before its cells, so the two washes layer instead of one replacing the other and nothing is
painted over a number. It covers the gap below each row as well, so a lane is continuous rather than dashed.

The header's own band is opaque and the column wash is not, because up there the band *is* the background
while down here the card is.

The band is **sheared with the title**, not an upright rectangle. Upright, it would be crossed by three or
four titles on the way up and link its title to nothing. Its cross-section is the column width measured
across the lean, which is what makes consecutive bands tile: the intersection of a band with any horizontal
line is exactly one column wide, so the header's bottom edge shows alternating blocks the width of the
columns they belong to. The banding stops at the header — the rows below own their own background.

Three details that matter if this is ever adjusted:

- **Tooltips are registered on the upright cell**, outside the rotation. A tooltip region is taken in screen
  space, so one registered under a rotated matrix sits somewhere the pointer is not.
- **Title length is bounded** by how far the header can reach at that angle, with word wrap off, so an
  over-long name from a mod is clipped rather than drawn outside the panel or wrapped into the first row.
- **Nothing here relies on clipping**, because rotation defeats it — see
  [the control's note](UIDesignatorTabControl.md#rotation-defeats-clipping--read-this-before-changing-the-header).
  Bands are built from axis-aligned strips whose bounds are arithmetic, which is why they stop exactly at the
  header's edges and why each band's width matches its column's at every height.

| Patched | Kind | Effect |
|---|---|---|
| `Pawn_WorkSettings.SetPriority` | Transpiler | Raises the accepted priority ceiling from 4 to 9 |
| `MainTabWindow_Work.DoWindowContents` | Prefix | Draws our panel instead of the vanilla table |
| `MainTabWindow_PawnTable.get_RequestedTabSize` | Postfix | Sizes the window, work tab instances only |
| `WindowStack.get_AnySearchWidgetFocused` | Postfix | Makes key bindings yield to any `UITextBoxControl` (framework-wide) |
| `Game.InitNewGame` | Postfix | Starts a new game with manual priorities on |

### Priorities run 0 to 9

`Pawn_WorkSettings.LowestPriority` is a public `const`, so every consumer in the game compiled its own
copy of the literal `4`. There is no field to reassign; the range has to be widened per place that
cares. Only one place does: the bounds check in `SetPriority`, which is why the transpiler is the whole
of it.

Three things need no patch at all, which is what makes this cheap:

- **Saving.** The values live in a `DefMap<WorkTypeDef, int>` of plain ints, and `ExposeData` writes
  that map directly rather than routing through `SetPriority`. Priorities of 5 through 9 save and load
  with no involvement from us.
- **Zero.** Vanilla already treats 0 as "not assigned", which is exactly the disabled state the tab
  draws faded.
- **Job assignment.** `JobGiver_Work` sorts by the stored value and does not clamp it, so a pawn set to
  7 or 9 takes the work in the right order. Confirmed in play, not only by reading the IL — the
  comparator and `CacheWorkGiversInOrder` both contain literal 4s, and whether either was a bound was
  the one thing here that reading could not settle.

If the mod is removed, stored priorities above 4 stay in the save. Vanilla reads them back through the
same map, so nothing breaks, but its own UI cannot display or edit them.

Bands: 1-3 draws in `Success`, 4-6 in `Accent`, 7-9 in `Danger`, and 0 in `TextDisabled`. Left click
raises, right click lowers, both wrapping, so any value is reachable without a modifier key.

### Why the whole vanilla method is skipped

`MainTabWindow_Work` overrides `DoWindowContents` and, after calling its base, draws three more things:
the manual-priorities checkbox, the `PriorityOneDoneFirst` line under it — "Priority 1 is done first.
Priority 4 is done last." — and the `HigherPriority` / `LowerPriority` hints. Patching the base only
intercepts the base call, leaving all three on screen, with a range that is now wrong.

Skipping the override drops them together. That also settles the wording problem without touching a
translated string: those are keyed vanilla assets in every shipped language, so correcting the text
would mean maintaining a "1 to 9" rewrite per language, while not drawing it costs nothing. The panel
draws its own themed toggle, whose tooltip states the real range.

### Manual priorities off

The mode is on by default for new games — patched on `InitNewGame` rather than on `PlaySettings`, so an
existing save keeps whatever the player chose. With it off, a number would be a lie, since the game
treats every non-zero value alike; the cells become
[`UICheckboxControl`](UICheckboxControl.md) instead, and enabling one writes
`Pawn_WorkSettings.DefaultPriority`.

Switching the mode runs vanilla's `Notify_UseWorkPrioritiesChanged`, which flattens every non-zero
priority to the default. The panel copies the numbers out before the switch and writes them back after,
so a round trip through the checkbox mode is lossless. That snapshot lasts the session, not the save:
it exists for the accidental click. Saving while switched off still loses the detail, and making that
survive a reload would need a `GameComponent`.

### Searching and folding

The name column's heading holds two controls, both there because each governs the whole grid rather than one
column — and it is the only heading cell wide enough for a control. A search field on top, the
manual-priorities toggle beneath it.

Search **filters which pawns get rows**, rather than dimming the ones that do not match: this tab is read by
comparing rows against each other, and a list with gaps in it is harder to compare than a short one. A group
whose every pawn is filtered out drops its heading too, and the heading's count reflects what is shown.

It matches **first name, nickname and last name, and nothing else** — read off `Pawn.Name` rather than any label
property. A pawn's label is not its name: `Pawn.LabelNoCount` is the name followed by the backstory title, as
`LabelPrefix + Name.ToStringShort + (", " + story.TitleShortCap).Colorize(...)`. Filtering on that matched a
colonist's *profession* — searching "sa" returned a sailor named Maxwell along with Sam — and because the title
half is colorized, the string also carries `<color=#...>` markup, so "col" would have matched every titled pawn
in the colony. The three name parts are tested separately rather than against an assembled full name, so a
search cannot match across the join between two of them.

The field is a [`UITextBoxControl`](UITextBoxControl.md). It was vanilla's `QuickSearchWidget` first, reported to
the game through a postfix on `Window.get_CommonSearchWidget`, and that fixed exactly one of the two bugs here:

- **The keys stop reaching the game.** `KeyBindingDef.IsDown`, `IsDownEvent`, `KeyDownEvent` and `JustPressed`
  all consult `WindowStack.AnySearchWidgetFocused`, which walks the window stack asking each window for its
  `CommonSearchWidget`. Every binding, camera dolly included, is suppressed while one of those has focus — and
  only one of *those*, which is why a field the game could not see let W and A pan the map as you typed.
- **Focus survives typing.** This is the half the widget did *not* fix. `GUI.SetNextControlName` does not make a
  control's identity independent of draw order, which I had assumed it did: Unity keys focus on an integer
  `keyboardControl` id derived from draw order and only hangs the name on it. An id shift therefore still drops
  focus mid-word, and vanilla's widget has no defense — it asks whether it is focused and believes the answer.

`UITextBoxControl` re-asserts focus by name when it believes it is focused and nothing else is, which repairs the
fault without needing to know which control shifted. `Patch_WindowStack_AnySearchWidgetFocused` then ORs its
`AnyFocused` into the gate, so the key-binding protection covers every text box of ours anywhere in the game
rather than this one window — and the `CommonSearchWidget` postfix is gone.

Group headings fold, and searching **suppresses** folding for as long as there is a search — a match inside a
folded group would otherwise be invisible, which reads as the search being broken. The folds come back when the
search is cleared.

## Steam Workshop uploads

Moved here from Gideon's Misc Patches, unchanged in behavior — what it fixes is a dialog flow, so it belongs
with the rest of the UI work.

Publishing a mod normally means clearing two dialogs. The terms-of-service prompt
(`Dialog_ConfirmModUpload`) is always kept; it also carries the "tag as translation" checkbox. The second is a
`Dialog_MessageBox` asking "Did you create this content yourself?", built with `interactionDelay = 6f`, which
greys out the Yes button for six seconds.

| Case | Result |
|---|---|
| Re-uploading a mod already published from this machine | Prompt skipped, straight to upload |
| First upload, or the target mod cannot be identified | Prompt shown, **countdown removed** |

That split is deliberate rather than incidental. The prompt is an authorship attestation, so answering it for
the player is only defensible where they have already answered it for that exact mod — which is what
`About/PublishedFileId.txt` records. A first upload still asks.

| Patched | Kind | Effect |
|---|---|---|
| `WindowStack.Add` | Prefix | Skips or de-delays the upload prompts |

`WindowStack.Add` is the interception point because the delay is assigned *after* the constructor returns, so a
constructor postfix would be overwritten; and because the code building the dialog lives in a lambda whose
compiler-generated name shifts between game builds. `Add` is the first stable public member that runs afterwards.

The patch was **removed from Misc Patches** rather than left in both, so there is deliberately no guard against
that mod being installed. An earlier version had one — a Harmony `Prepare` that stood down when Misc Patches was
present — and it would have become a bug immediately: that mod lives on as the home for future patches, so the
guard would see it installed, stand down, and leave nothing handling the dialog at all.

## The pawns tab

A tab of ours rather than a replacement for a vanilla one: `MainButtonDef` `Gideon_Pawns`, order 51, just below
Work. A status board for every colonist — condition, health, mood, activity — with the day's schedule one click
away.

Grouped by map through the same `MapLabels.NameOf` the work tab uses, so a pocket dimension is named after the
entrance that opens it. That code, the portrait, and the deferred camera jump were **extracted** into
`UIOverhaul/Shared/` when this tab was built rather than copied: three panels reading the same game state and
disagreeing about it is exactly the failure mode worth designing out.

| Column | Reads |
|---|---|
| Colonist | Portrait, name, backstory title |
| Condition | Worst-first status, see below |
| Health | `summaryHealth.SummaryHealthPercent` |
| Mood | `needs.mood.CurLevelPercentage` |
| Activity | `jobs.curDriver.GetReport()` |
| Schedule | The current hour's assignment |

### Condition is severity-ordered, not combined

A bleeding, downed, freezing pawn is three problems, but a column that says all three says nothing at a glance —
and only one is what you would act on first. So the most urgent wins the line and the tooltip carries the rest.
The order is by how soon it kills: **bleeding out → vacuum → downed → needs treatment → temperature → healthy.**

Two readings are worth naming. Bleeding only reports as *bleeding out* when
`HealthUtility.TicksUntilDeathDueToBloodLoss` is under a day, because a scratch bleeds too and a column that
cries wolf over a scratch is one players learn to ignore; below that threshold it falls back to a plain
"Bleeding". Vacuum uses `Pawn.HarmedByVacuum`, which is exactly "exposed *and* unprotected" — not
`ConcernedByVacuum`, which is the weaker "would care about it". Temperature reads the **Hypothermia** and
**Heatstroke** hediffs rather than comparing ambient temperature to the comfortable range: the comparison answers
"is this uncomfortable", which is true of a pawn walking briskly across a cold map in no danger at all.

### Bars

Health is green above 90%, blue through the middle, red when low, so a scan down the column finds the casualties
without reading a number. The thresholds are on the fill only; the track stays neutral so the bar's length stays
readable.

Mood uses a **palette custom color**, `Gideon.Mood` (`#9B72D9`), not a new `UIColorRole`. Mood is this tab's idea
rather than one of the framework's meanings, and `Custom` is the seam for exactly that — a theme can restate it
and nothing in the framework had to grow a slot. It sits on the `UIPaletteBase` template so both shipped themes
inherit it. A pawn with no mood need shows no bar rather than an empty one, which would read as despair.

### The schedule strip

Clicking a row expands it. The 24 hours are drawn as a `DrawOverlay` on the row rather than as a column, because
they span the whole grid and splitting them across columns would tie the schedule's layout to the column widths.
`DrawOverlay` is a new hook on `UIDesignatorTabRow`, the counterpart to `DrawBackground`, drawn after the cells so
what it draws takes clicks first. Cells keep to the top band so a portrait does not drift downward when a row
opens.

The dropdown at the start of the strip picks a **brush**; clicking an hour is what writes. That separation is
what makes painting a block of hours one choice and several clicks rather than a choice per click — and the brush
is shared across rows, because it is a tool the player picks up, not a property of one pawn. Every dropdown entry
carries its own swatch in the def's own color, via `FloatMenuOption.extraPartOnGUI`, so the menu and the strip
cannot disagree about what a color means.

Hours **paint on drag**, not just on click, so eight hours of sleep is one gesture. That is why the strip reads
`Input.GetMouseButton` directly rather than using `ButtonInvisible`: a button reports a completed click, and a
drag never completes one per cell. The current hour is outlined in `Accent` so the strip can be read against the
clock without counting cells.

Expansion is a **set** of pawns rather than a single selection, so opening one does not close another — comparing
two colonists' days side by side is most of the reason to look at this.

### Schedule assignment colors

Restyled to suit the rest of the UI — **on the defs**, not in a private table:

| Assignment | Color | |
|---|---|---|
| Anything | `#39434F` | dim slate; deliberately not a hue, so an unscheduled day reads as empty |
| Work | `#4A90D9` | the palette's information blue |
| Joy | `#D98C3F` | warm amber, clear of the danger red |
| Sleep | `#5B4A99` | deep violet, a sibling of the mood color |
| Meditate | `#3FA39B` | teal, distinct from both the blue and the green |

`TimeAssignmentDef.color` is a plain field that every schedule widget in the game reads, so recoloring the defs
restyles **vanilla's schedule tab, the inspect pane's timetable strip and the assignment selector** along with
this tab's strip. A private table would have left the vanilla views on the old colors, which is the
split-personality look an overhaul exists to remove.

They are hand-picked rather than mapped onto palette roles, because an assignment is a category and not a state —
putting recreation on `Warning` would assert something untrue. What they share with the palette is the *family*:
mid saturation, similar lightness, and nothing near the danger red, so a schedule strip never looks like it is
reporting a problem.

**Mod-added assignments keep the color their author chose.** The whole database is walked but only the five above
are touched. A mod author picking a color has made a decision, and overriding it to suit our palette is a worse
outcome than one swatch that does not match; vanilla is the exception because restyling the base game is the
point. The dropdown still lists every loaded assignment, so a mod's types remain selectable.

One trap worth recording: `ColorTexture` builds its swatch once on first read and caches it, so the cache has to
be invalidated after changing `color`. Setting the field alone recolors our strip — which reads the field — and
nothing else, which is a subtler bug than no recolor at all.

### Clicking a face centers the view on that colonist

The portrait is a button. Clicking it closes the tab and centers the camera on the pawn.

The close is ours to do — nothing in `CameraJumper` touches the main tabs, and this tab covers the map, so a jump
on its own would center the camera behind a full-screen window. It is `EscapeCurrentTab(playSound: false)`,
because `CameraJumper` plays its own sound on arrival and vanilla's tab-close click on top of that reads as two
clicks for one action.

Both halves are **deferred to the end of the frame**, for the reason the architect tab defers its close: the
click is handled inside the grid's scroll view, and closing the window from in there leaves this panel drawing
into a window no longer on the stack with a `BeginScrollView` still to be matched. The jump reaches outside too —
it calls `TryHideWorld` and can reassign `Game.CurrentMap`.

Cross-map works without any extra handling: `CameraJumper` hides the world view and switches the current map when
the target is on a different one, which this tab needs because it lists colonists from every map. `PositionHeld`
and `MapHeld` mean a pawn in a caravan or a container still resolves to a real location.

The hover hint is the ring of backing disc around the head, lerped toward `Accent`. That ring is the only part of
the portrait that *can* carry feedback — the face is a `RenderTexture` and has to be drawn untinted.

It **selects the pawn as well as centering on them**, which is what vanilla's colonist bar does. Centering alone
left the colonist under the cursor but not acted upon, and the next thing wanted after finding someone is to give
them an order — which needs them selected and their inspect pane open.

### Pocket maps are named after their entrance

Colonists are grouped by map, and a group is headed with `MapParent.LabelCap` — the colony's given name, or
whatever a caravan site or ship calls itself. Pocket maps break that: a `PocketMapParent` takes its label from
its **def**, so every pocket map in the game is headed "Pocket map" regardless of which entrance opened it. With
one it is merely uninformative; with several — the normal case for the mods that add them — the headings are
indistinguishable.

They are now named after the entrance that opens them. Nothing about this is mod-specific: a pocket map can only
be generated through `MapPortal.GeneratePocketMap`, so **every** entrance is a `MapPortal`, and one lookup covers
vanilla pit gates and ancient hatches along with anything a mod adds. Referencing a particular mod's types would
have meant a hard dependency, a version to track, and no benefit to the next mod that adds pocket maps.

The lookup asks the pocket map's own `PocketMapExit` first, since `PocketMapExit.entrance` is a direct reference;
failing that — the API permits a pocket map with no exit — it asks the source map which of its portals leads
back. Both go through the `MapPortal` `ThingRequestGroup`, which is a cached list, rather than walking every
thing on the map.

The name is read from `IRenameable.RenamableLabel` when the entrance implements it, falling back to `LabelCap`.
That matters because `Thing.LabelCap` only reflects a rename if the subclass happens to route `LabelNoCount`
through it, which is not guaranteed.

**Renames need no detection.** The label is recomputed every frame rather than cached, so renaming an entrance
appears in the heading on the next frame by construction. A cache would have needed an invalidation hook, and
vanilla raises no notification on rename to hang one on.

### Row status

The card's accent stripe carries a status, the same three-way one the grow-zone plant cards use:

| State | Stripe | Wash |
|---|---|---|
| Nothing to report | `SurfaceRaised` | — |
| Has work types the pawn cannot do | `Warning` | Warning stripes behind each of those cells |
| Incapacitated (`Pawn.Downed`) | `Danger` | Danger stripes across the row |

Incapacitated outranks missing work types: a downed pawn is not doing any of it regardless of what they are
capable of. The stripe had been the pawn's favorite color, which made a long list colorful and said nothing
— and a stripe that means something everywhere else in the mod cannot be decorative in one tab.

The washes are the notice alphas the plant cards already use (0.22 warning, 0.24 danger) over
`UIShapes.Stripes`, tiled at a fixed pitch and anchored to position so adjacent cells continue one pattern
rather than each restarting it at their own edge. The row wash is inset past the accent bar, which is
already that color at full strength.

### Edit tools and templates

An edit tools column sits between the name and the grid, with five buttons per pawn in two rows — the top
row edits this pawn's numbers, the bottom row deals in saved templates. Two rows rather than one because
five in a line would cost another 60px of a window that is already as wide as the grid needs.

| Button | Art | Does |
|---|---|---|
| Clear | `UIOverhaul/Work/Clear` | Sets every priority to 0. Confirmed first, and skipped silently when there is nothing set |
| Copy | `UIOverhaul/Work/Copy` | Lifts the pawn's priorities onto a session clipboard |
| Paste | `UIOverhaul/Work/Paste` | Writes the clipboard onto this pawn. Disabled, not hidden, when nothing has been copied |
| Save | `UIOverhaul/Work/SaveTemplate` | Captures the pawn's priorities as a named template and opens the manager on it |
| Apply | `UIOverhaul/Work/ApplyTemplate` | Opens the manager to put a saved template on this pawn |

The clipboard **is** a `WorkPriorityTemplate`, which is not a coincidence: an unnamed set of priorities
lifted off one pawn to put on another is exactly what a template is. Reusing the type means copy and paste
inherit its capability handling rather than repeating it — the copy skips work the source cannot do, so
their incapabilities are not copied as zeros, and the paste leaves work the target cannot do at 0. It is
held for the session and never written to disk: a template is the named, kept version, and persisting the
clipboard too would blur the two.

Templates are stored as `UIOverhaul_WorkTemplates.xml` in RimWorld's config folder, beside the game's
own settings — not in the save. Naming an assignment is only worth doing if it can go on the next
colonist, in the next colony, in a save that does not exist yet. The file is picked up by the same
watcher as the other config files, so hand-editing it applies without a restart.

Keyed by `WorkTypeDef` defName rather than by def, so a work type belonging to a currently disabled mod
survives a read and write rather than being dropped. Zeros are stored as well as non-zeros: a template
describes a whole assignment, and "not assigned" is part of that.

Applying one:

- A work type the pawn is **incapable** of is left at 0. `SetPriority` logs an error for those, and a
  template made from a capable pawn should not fail on an incapable one.
- A work type the template has **never heard of** — added by a mod installed after it was saved — is
  left alone rather than zeroed.

## Architect tab

Redrawn as three panes in one window, in `UIOverhaul/Features/Architect`: categories on the left, the open
category's designators as cards in the middle, and — where vanilla would have opened a float menu — an option
pane on the right.

The option pane is the point of the redesign. A float menu can only list names, so choosing granite over
sandstone meant knowing the difference already or leaving the menu to look it up. The pane shows each option's
icon, name and the stats that separate it from its siblings.

Nothing about designator behavior is reimplemented. Selecting goes through `Designator.ProcessInput`, a
dropdown's choice through its own `SetActiveDesignator`, and a material through `Designator_Build.SetStuffDef`
— all public, all what vanilla's own menu calls, so a dropdown remembers its variant and a build designator
keeps its stuff color.

### The tab closes once you have chosen

**This is a change from vanilla**, which leaves the architect tab open while you place. That suits vanilla's
architect — a strip of small icons that covers little of the map. This one is a full window of cards, and
leaving it up means placing walls you cannot see.

Only a *completed* choice closes it:

| Click | Closes? |
|---|---|
| A category | No |
| A designator with no variants or materials | Yes |
| A designator that has them | No — the option pane opens to be read |
| An option in that pane | Yes |
| A right-click menu entry | No |
| A disabled designator | No |

The close is **deferred to the end of the frame** rather than done where the choice is made. The choice happens
inside a scroll view, and taking the window off the stack from in there leaves the panel drawing into a window
that is no longer on the stack, with a `BeginScrollView` still to be matched.

Two things worth knowing, both checked against the game rather than assumed:

- **Closing the tab does not deselect the designator.** `EscapeCurrentTab` goes to `SetCurrentTab(null)` and
  then `ToggleTab`; neither touches `DesignatorManager`, and nothing on the close path calls `Deselect`. If that
  ever changes, this feature would silently throw the choice away.
- **It closes without the tab-close sound**, because the designator's own activate sound has already played and
  the two together read as two clicks for one action.

## Loading screen

See [LoadingScreenConfig.md](LoadingScreenConfig.md). The patches involved are framework requirements
rather than changes anyone opts into separately, and are listed there.

## Not yet restyled

Known gaps, so nobody reports them as bugs:

- **`Widgets.ButtonTextSubtle`** draws from `ButtonSubtleAtlas`, a separate texture, and is still
  vanilla. It is what the architect category buttons use.
- The themed textures shipped in `Textures/UIOverhaul/UI/` (`Control.Checkbox.*`, `Control.RadioButton.*`)
  are **unused**, and pointing the game at them was never possible: all five texture fields on `Widgets`
  are `readonly`, so they can only be replaced by patching the methods that read them. Checkboxes and radio
  buttons are painted from the palette instead — see below.
- **Window backgrounds**, sliders, and scrollbars.
