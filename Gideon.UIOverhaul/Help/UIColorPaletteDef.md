# UIColorPaletteDef

A named set of colors, authored in XML, that controls draw from instead of holding color values of
their own. Retuning the whole UI is an XML edit; adding a theme is a new def. Neither needs a
recompile.

- **Namespace:** `Gideon.UIFramework`
- **Base type:** `Verse.Def`
- **XML location:** any `Defs` file in your mod, e.g. `1.6/Defs/MyPalettes.xml`
- **Assembly reference required:** no, for authoring palettes; yes, for reading colors in C#
- **Copy-and-go examples:** [Examples/](Examples/) — see the table at the end of this document

## The idea

A control never asks for "dark gray". It asks for `PanelBackground` and gets whatever the active
palette says that role looks like. Because every role is filled by the palette rather than by the
control, a single palette swap restyles everything consistently — including controls written by other
mods, provided they follow the same rule.

Every role has a **built-in default in code**. A palette only names the roles it wants to change, so
the smallest useful palette is a couple of lines.

## Minimal example

```xml
<?xml version="1.0" encoding="utf-8"?>
<Defs>
  <Gideon.UIFramework.Defs.UIColorPaletteDef>
    <defName>MyMod_Crimson</defName>
    <label>crimson</label>
    <accent>#C0392B</accent>
  </Gideon.UIFramework.Defs.UIColorPaletteDef>
</Defs>
```

Everything except `accent` uses the built-in values. That is a complete, usable palette.

## Color formats

Any of these may be used in any color field. Hex is recommended: it is unambiguous and it is what a
color picker gives you.

| Form | Example | Notes |
|---|---|---|
| `#RGB` | `#1A2` | Shorthand; each digit is doubled, so this is `#11AA22` |
| `#RRGGBB` | `#15191D` | Opaque |
| `#RRGGBBAA` | `#15191D80` | Alpha in the last two digits |
| `r,g,b` | `0.08,0.10,0.11` | Floats 0–1 |
| `r,g,b,a` | `21,25,29,255` | If **any** component exceeds 1, the whole set is read as 0–255 |
| `(r,g,b,a)` | `(0.1,0.2,0.3,1)` | Parentheses tolerated, for RimWorld's native style |

The leading `#` is optional and parsing is case-insensitive.

> **Watch out:** `1,1,1` is read as floats, giving white, because no component exceeds 1. If you
> think in 0–255, write `255,255,255` — or just use hex and avoid the question.

An unparseable value is reported at startup with the def name and the field name, and that role falls
back to bright magenta so the mistake is visible on screen rather than silently invisible.

## Roles

Alpha matters on the three overlay roles: they are drawn *on top of* what is already there.

| Field | Role | Purpose | Built-in default |
|---|---|---|---|
| `windowBackground` | `WindowBackground` | Base fill behind a whole window or tab | `#15191D` |
| `panelBackground` | `PanelBackground` | Panel, card or row sitting on the window background | `#1B1F23` |
| `surfaceRaised` | `SurfaceRaised` | Surfaces standing above the panel: button fills, header strips, dividers | `#2F3337` |
| `surfaceSunken` | `SurfaceSunken` | Surfaces cut into the panel: field interiors, bar troughs, chrome | `#0E1013` |
| `border` | `Border` | One-pixel border at rest | `#345673` |
| `borderFocused` | `BorderFocused` | Border when focused or active | `#73BFFF` |
| `textPrimary` | `TextPrimary` | Body and label text | `#E3E3E3` |
| `textSecondary` | `TextSecondary` | Subtitles, units, field labels | `#9EA6B2` |
| `textDisabled` | `TextDisabled` | Text for something unusable | `#6C7480` |
| `accent` | `Accent` | Identity color: selection, focus, links, primary buttons | `#73BFFF` |
| `accentMuted` | `AccentMuted` | Dimmed accent for fills and borders that would overpower | `#274157` |
| `success` | `Success` | Worked, complete, within healthy range | `#61C461` |
| `warning` | `Warning` | Needs attention but not broken | `#CCA633` |
| `danger` | `Danger` | Failed, forbidden, out of range | `#E54D33` |
| `info` | `Info` | Neutral information; the cold end of a hot/cold scale | `#4A90D9` |
| `mood` | `Mood` | A pawn's inner state: mood bars and similar readings | `#9B72D9` |
| `hoverOverlay` | `HoverOverlay` | Wash over a hovered control | `#FFFFFF0C` |
| `pressedOverlay` | `PressedOverlay` | Wash over a pressed control | `#FFFFFF1F` |
| `selectionOverlay` | `SelectionOverlay` | Wash marking a selected row or card | `#73BFFF24` |

A light theme must override the overlays — white washes do nothing useful on a pale surface.

`Mood` is the one role that is about *how someone feels* rather than about whether something succeeded, which is
why it is not folded into `Info` or `Accent`: a mood bar next to a health bar must not read as another health bar,
and must not read as the accent either, since the accent means "selected".

## Templates

Templates are RimWorld's own def inheritance; the framework adds nothing. Mark a parent
`Abstract="True"` and give it a `Name`, then have children declare `ParentName`. An abstract def never
enters the database, so it can never be selected as a palette.

```xml
<Gideon.UIFramework.Defs.UIColorPaletteDef Abstract="True" Name="MyMod_PaletteBase">
  <success>#61C461</success>
  <warning>#CCA633</warning>
  <danger>#E54D33</danger>
</Gideon.UIFramework.Defs.UIColorPaletteDef>

<Gideon.UIFramework.Defs.UIColorPaletteDef ParentName="MyMod_PaletteBase">
  <defName>MyMod_Slate</defName>
  <label>slate</label>
  <windowBackground>#1C2126</windowBackground>
  <accent>#7FA8C9</accent>
</Gideon.UIFramework.Defs.UIColorPaletteDef>
```

Put shared, meaning-carrying colors in the template and let each variant own its surfaces and text —
those are exactly what a variant exists to change. A child may override anything it inherits.

The shipped `UIPaletteBase` template is available as a parent, but it belongs to this mod and its
contents may change between versions. For a palette you intend to ship, define your own template.

## Which palette is active

`UIColorPaletteDef.Active` is what controls read. It resolves in this order:

1. Whatever was last assigned to `Active` in code — which in practice means the player's choice in the
   theme selector.
2. Otherwise `UIPalette_Default`, the palette this mod ships. Its defName is hardcoded as
   `UIColorPaletteDef.DefaultPaletteDefName`.
3. Otherwise `UIColorPaletteDef.BuiltIn`, the compiled-in fallback, with an error in the log — a
   missing `UIPalette_Default` means a broken install rather than an ordinary state.

Step 3 matters more than it looks: a control drawn before defs have finished loading still gets
sensible colors rather than transparent black. `BuiltIn` carries the same values as
`UIPalette_Default`, so the fallback looks correct rather than merely functional.

**There is no way for a palette to claim the default in XML, by design.** No `isDefault`, no
`priority`, nothing to bid on. A theme mod ships its palette; the player selects it. That keeps the
decision with the player instead of with whichever mod declared itself most important, and it means a
newly installed mod cannot silently restyle a colony.

**To offer a theme, just define it.** Every loaded palette shows up in `All`, which is what the
selector lists — no flag needed to be selectable.

**To switch palette at runtime:**

```csharp
UIColorPaletteDef.Active = UIColorPaletteDef.Named("UIPalette_Light");
UIColorPaletteDef.Active = null;   // revert to the default resolution above
```

Assigning `Active` is global, because a theme is meant to apply to everything. A control that must
*not* follow the global theme should take a palette as a parameter instead of reading `Active`.

The choice is held internally as a **defName**, not as a def reference, and `ActiveDefName` exposes it
for saving to settings. A reference would go stale across a def reload, and would be unrestorable if
the theme's mod were briefly disabled.

**If the stored name matches no loaded palette, `Active` falls back to the default** and the stored
name is kept, not cleared. Disabling a theme's mod is usually temporary, so the choice comes back by
itself when the mod does. Clearing on mismatch would also be unsafe outright: before defs load *no*
name resolves, so the setting would be wiped on every launch.

`ActiveIsMissing` reports that situation — a theme was chosen but is not loaded — so a selector can
say so instead of silently showing the wrong theme. It stays false until def loading has finished,
since an unresolved name before then means nothing.

## Availability during startup

**`Active` never returns null and never throws, at any point in the game's lifetime.** You can read it
from a static constructor, from a Harmony patch on early startup code, or while the loading screen is
drawing, without a guard.

That matters because of when defs actually exist. RimWorld's startup runs:

```
PlayDataLoader.DoPlayLoad
  └── LoadedModManager.LoadAllActiveMods
        ├── InitializeMods
        ├── LoadModContent          ← assemblies loaded
        ├── CreateModClasses        ← Mod constructors run (no XML has been read yet)
        ├── LoadModXML              ← XML read from disk
        ├── ApplyPatches
        └── ParseAndProcessXML      ← defs created, DefDatabase populated
```

The loading screen is drawn by `LongEventHandler.LongEventsOnGUI`, called from `Root.OnGUI` every
frame — including every frame of the sequence above. So anything drawn during loading is drawing
*before* `ParseAndProcessXML` has run, and no palette def exists yet.

**No Harmony patch can change this.** The earliest mod code that runs is the `Mod` constructor in
`CreateModClasses`, which is before `LoadModXML` has opened a single file. A def cannot exist before
the XML it is built from has been read, so there is no ordering trick that makes one available sooner.

What covers that window is `BuiltIn`, the compiled-in palette. Its values are kept identical to the
shipped `UIPalette_Default`, so the loading screen and the main menu look the same even though they
are drawing from different objects. Lookups on an unpopulated `DefDatabase` return null rather than
throwing, which is what makes the fallback chain safe rather than merely lucky.

Two consequences worth knowing:

- **A palette edit does not affect the earliest frames.** Retuning `UIPalette_Default` in XML changes
  everything from `ParseAndProcessXML` onward, but the first moments of the loading screen still use
  the compiled-in values. If you need those to match a heavily customized theme, that is a framework
  change, not an XML one.
- **Missing-palette errors are suppressed until loading finishes.** `Default` only logs when
  `PlayDataLoader.Loaded` is true, so the normal pre-def window stays silent and a genuinely broken
  install still reports.

## Reading colors in C#

```csharp
using Gideon.UIFramework;                    // UIColorPaletteDef
using Gideon.UIFramework.Components.Colors;  // UIColorRole
using UnityEngine;
using Verse;

UIColorPaletteDef palette = UIColorPaletteDef.Active;

Widgets.DrawBoxSolid(rect, palette.WindowBackground);
Widgets.DrawBoxSolid(inner, palette.Get(UIColorRole.PanelBackground));   // same thing

if (Mouse.IsOver(rect))
    Widgets.DrawBoxSolid(rect, palette.HoverOverlay);
```

The named properties (`WindowBackground`, `Accent`, …) need only `Gideon.UIFramework`. The second
using is for `UIColorRole` itself — see [Namespaces](README.md#namespaces).

Read `Active` **at draw time**. Caching a `Color` in a field defeats palette switching, because the
control keeps painting the color that was active when it was built.

Other members:

| Member | Returns |
|---|---|
| `Get(UIColorRole role)` | The color for a role. Never fails |
| `Custom(string name, Color fallback)` | A named custom color, or the fallback |
| `TryGetCustom(string name, out Color)` | Whether the palette defines that custom color |
| `Active` / `Default` / `BuiltIn` | Palette selection, described above |
| `ActiveDefName` | The chosen palette's defName — what to persist to settings |
| `ActiveIsMissing` | True when the chosen palette is not loaded and the default is being served |
| `DefaultPaletteDefName` | `"UIPalette_Default"` — the hardcoded defName of the shipped default |
| `Named(string defName)` | A specific palette, without disturbing `Active` |
| `All` | Every loaded palette — use it to build a theme picker |
| `DefaultFor(UIColorRole role)` | The compiled-in default for a role |

`Def.label` and `Def.description` are inherited, so a theme picker has display text to work with
without any extra fields.

## Custom colors

When you need a color the fixed roles do not cover, do not wait for a framework change:

```xml
<custom>
  <li>
    <name>MyMod.RadiationGlow</name>
    <value>#7FFF3C</value>
  </li>
</custom>
```

```csharp
Color glow = UIColorPaletteDef.Active.Custom("MyMod.RadiationGlow", Color.green);
```

- Prefix names with something you own. Custom colors share one flat namespace per palette.
- **Always pass a meaningful fallback.** Another palette — including the built-in one — will not know
  about your color, and your UI has to survive that.
- Names are compared case-insensitively. Defining the same name twice in one palette is a load error.

## Adding your color to somebody else's palette

Custom colors are a normal XML list, so a patch can append to a palette you do not own — which is
worth doing when the color should follow the theme. If it should look the same whatever theme is
loaded, keep it in your own code and leave the palette alone.

The shipped palettes define no `<custom>` list, and patching into a list that does not exist fails, so
a patch has to create it first:

```xml
<Operation Class="PatchOperationConditional">
  <xpath>/Defs/Gideon.UIFramework.Defs.UIColorPaletteDef[defName="UIPalette_Default"]/custom</xpath>
  <nomatch Class="PatchOperationAdd">
    <xpath>/Defs/Gideon.UIFramework.Defs.UIColorPaletteDef[defName="UIPalette_Default"]</xpath>
    <value>
      <custom />
    </value>
  </nomatch>
</Operation>

<Operation Class="PatchOperationAdd">
  <xpath>/Defs/Gideon.UIFramework.Defs.UIColorPaletteDef[defName="UIPalette_Default"]/custom</xpath>
  <value>
    <li>
      <name>MyMod.RadiationGlow</name>
      <value>#7FFF3C</value>
    </li>
  </value>
</Operation>
```

Against a palette that already has a `<custom>` list, the second operation alone is enough. Both go in
your mod's `Patches` folder, not `Defs`. Ready to copy:
[Examples/PatchAddCustomColor.xml](Examples/PatchAddCustomColor.xml).

## Load-time validation

Palettes are checked as defs load, and every problem is reported rather than only the first:

- a color value that cannot be parsed, named with its def and field
- a `custom` entry with no `<name>`
- an empty `<li>` in `custom`
- the same custom name defined twice in one palette

Bad values do not abort loading; the affected role falls back to magenta. Check the startup log if a
palette looks wrong.

## Examples

Nothing in `Help/` is loaded by the game — RimWorld only reads XML from a version folder's `Defs` and
`Patches` directories. Copy these into your own mod.

| File | Shows | Goes in |
|---|---|---|
| [UIColorPalettes.xml](Examples/UIColorPalettes.xml) | The full pattern: an abstract template with a dark and a light variant, plus a `<custom>` block | `Defs` |
| [MinimalPalette.xml](Examples/MinimalPalette.xml) | The smallest valid palette — one color overridden | `Defs` |
| [PatchAddCustomColor.xml](Examples/PatchAddCustomColor.xml) | Adding a custom color to a palette you do not own | `Patches` |

The shipped `1.6/Defs/UIColorPalettes.xml` is the live version of the first example, minus the
`<custom>` block: a named color that nothing reads is only load-time weight, so it lives here in the
example instead.
