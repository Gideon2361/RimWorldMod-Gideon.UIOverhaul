# Gideon's UI Framework — modder reference

This mod is two things at once:

- **A framework.** Reusable UI controls other mods can instantiate, configure and extend. Everything
  under the `Gideon.UIFramework` namespace is public API, documented here, and used by this mod
  through exactly the same surface your mod would use. There is no private fast path.
- **A UI overhaul.** This mod's own tabs and windows, plus the direct restyling of RimWorld's, built by
  consuming those controls. Namespace `Gideon.UIOverhaul`. Not API — do not depend on it. What it
  changes about the game, and the patches behind it, are listed in [GameChanges.md](GameChanges.md).

If something is awkward to use from outside, it is awkward from inside too, and that is a bug worth
reporting rather than working around.

## Referencing the framework

Add a reference to `Gideon.UIOverhaul.dll` (found in this mod's `1.6/Assemblies` folder) and declare
this mod as a dependency in your `About.xml` so load order is right:

```xml
<modDependencies>
  <li>
    <packageId>gideon.uioverhaul</packageId>
    <displayName>Gideon's UI Overhaul</displayName>
  </li>
</modDependencies>
<loadAfter>
  <li>gideon.uioverhaul</li>
</loadAfter>
```

Much of the framework is configurable from XML alone, with no assembly reference needed — a new
color palette, for instance, is pure XML.

## Controls

| Control | Kind | Documentation |
|---|---|---|
| `UIColorPaletteDef` | Def, XML-authored | [UIColorPaletteDef.md](UIColorPaletteDef.md) |
| `UILoadingScreenConfig` | Plain XML at the mod root — **not** a Def | [LoadingScreenConfig.md](LoadingScreenConfig.md) |
| `UILoadingScreenControl` | Class, subclass to customize | [LoadingScreenConfig.md](LoadingScreenConfig.md#writing-a-custom-drawer) |
| `UIProgressBarControl` | Static drawing helper | [LoadingScreenConfig.md](LoadingScreenConfig.md) |
| `UILoadingScreen` | Static progress state, drivable by any mod | [LoadingScreenConfig.md](LoadingScreenConfig.md#driving-the-display-yourself) |
| `UIImageLoader` / `UIImage` | Loads a texture off disk, PNG/JPG/DDS | [LoadingScreenConfig.md](LoadingScreenConfig.md#image-files) |
| `UIRichButtonControl` | Class, construct and reuse | [UIRichButtonControl.md](UIRichButtonControl.md) |
| `UICheckboxControl` | Static drawing helper | [UICheckboxControl.md](UICheckboxControl.md) |
| `UIRadioButtonControl` | Static drawing helper | [UIRadioButtonControl.md](UIRadioButtonControl.md) |
| `UIDesignatorTabControl` | Class, construct and reuse | [UIDesignatorTabControl.md](UIDesignatorTabControl.md) |
| `UITextBoxControl` | Class, construct and reuse | [UITextBoxControl.md](UITextBoxControl.md) |
| `UIDebug` | Static switch for diagnostic logging | [UIDebug.md](UIDebug.md) |

Two different config mechanisms, for one reason: **defs do not exist for most of a load.** Anything
consumed during startup cannot be a Def, so the loading screen is read straight off disk from
`Mods/YourMod/Mods/gideon.uioverhaul/LoadingScreen.xml`. Everything else is a normal Def. See
[Why this is not a Def](LoadingScreenConfig.md#why-this-is-not-a-def).

Copy-and-go XML lives in [Examples/](Examples/). Nothing there is loaded by the game, so the files are
safe to edit while you work out what you need.

## Namespaces

Namespaces follow folders.

| Namespace | Holds |
|---|---|
| `Gideon.UIFramework.Defs` | Def types: `UIColorPaletteDef` |
| `Gideon.UIFramework.Controls` | Drawing controls: `UILoadingScreenControl`, `UIProgressBarControl`, `UIRichButtonControl`, `UICardControl`, `UICheckboxControl`, `UIRadioButtonControl`, `UIDesignatorTabControl`, `UITextBoxControl` |
| `Gideon.UIFramework.Stages` | Loading-time types: `UILoadingScreenConfig`, `UILoadingScreen`, `UILoadingSnapshot` |
| `Gideon.UIFramework.Components.Colors` | Color supporting types: `UIColorRole`, `UIColorEntry`, `UIColorParser` |
| `Gideon.UIFramework.Components.Images` | Image supporting types: `UIImageFit`, `UIImage`, `UIImageLoader` |
| `Gideon.UIFramework.Helpers` | Shared drawing and diagnostics: `UIElementPainter`, `UIShapes`, `UISkinRestyler`, `UIDebug` |
| `Gideon.UIFramework.Patches.*` | Harmony patches the framework needs to collect its data. Not API — never call into these |
| `Gideon.UIOverhaul` | This mod's own screens. Not API |

Def element names in XML are fully qualified and follow the same split, so a palette is
`<Gideon.UIFramework.Defs.UIColorPaletteDef>`. Enum values such as `Cover` are written by name and are
unaffected.

The patch namespace is worth one note. The framework applies patches of its own — reading RimWorld's
profiler labels and def-loading calls — because that is where a loading screen's stage and progress
data come from. They are a requirement of the controls, not a modification anyone opts into
separately, which is why they ship with the framework rather than with the mod that draws the screen.

## Conventions

- **Def types are suffixed `Def`**, e.g. `UIColorPaletteDef`, following RimWorld's own `ThingDef` /
  `RecipeDef` naming so nothing here reads oddly next to vanilla.
- **XML element names are given fully qualified** in the examples, e.g.
  `<Gideon.UIFramework.Defs.UIColorPaletteDef>`. The short form works too, but the qualified name cannot
  be made ambiguous by another mod defining a type of the same name.
- **Roles are named for the job, not the appearance.** A color role is `TextPrimary`, never
  `OffWhite`, so a light template can fill it with something dark without the name becoming a lie.
- **Defaults are complete.** Anything you leave unset has a working built-in value. A palette, a
  style, or a control constructed with no configuration produces something usable, so you only ever
  write down what you want to be different.

## Licensing

Third-party material included in this mod, and the licenses it carries, are listed in
`THIRD-PARTY-NOTICES.txt` in the mod root.
