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

## Loading screen

See [LoadingScreenConfig.md](LoadingScreenConfig.md). The patches involved are framework requirements
rather than changes anyone opts into separately, and are listed there.

## Not yet restyled

Known gaps, so nobody reports them as bugs:

- **`Widgets.ButtonTextSubtle`** draws from `ButtonSubtleAtlas`, a separate texture, and is still
  vanilla. It is what the architect category buttons use.
- **Checkboxes and radio buttons** read `CheckboxOnTex`, `CheckboxOffTex`, `CheckboxPartialTex`,
  `RadioButOnTex` and `RadioButOffTex`. Themed textures for these already ship in
  `Textures/UIOverhaul/UI/` (`Control.Checkbox.*`, `Control.RadioButton.*`) but nothing points the game
  at them yet.
- **Window backgrounds**, sliders, and scrollbars.
