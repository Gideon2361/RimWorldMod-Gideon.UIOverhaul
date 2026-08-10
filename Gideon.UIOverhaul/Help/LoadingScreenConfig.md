# UILoadingScreenConfig

Replaces RimWorld's loading screen with a full-screen background, the stage being loaded, the step
being performed inside it, and a progress bar.

- **Namespace:** `Gideon.UIFramework.Stages`
- **Base type:** none — this is **not** a Def
- **File location:** `Mods/YourMod/Mods/gideon.uioverhaul/LoadingScreen.xml` — inside your mod, not a `Defs` folder
- **Assembly reference required:** no, unless you write a custom drawer class
- **Copy-and-go example:** [Examples/LoadingScreen.xml](Examples/LoadingScreen.xml)

## Why this is not a Def

Every other control in this framework is a Def. This one cannot be, and the reason is timing.

Defs are not created until `LoadedModManager.ParseAndProcessXML`, roughly three quarters of the way
through startup. The loading screen has been drawing since long before that. A screen described by a
def therefore spends almost the entire load as a compiled-in fallback and only becomes itself in the
instant before the main menu appears — which looks exactly like the backdrop failing to load, then
flashing on at the end.

Reading the file ourselves, off disk, on first use, means the screen is correct on the very first
frame it draws.

What that costs, all of it intended:

- **No `PatchOperation` support.** Patches are applied during `ApplyPatches`, which is *after* the
  window this file matters in. A patch could never have taken effect in time, so supporting them
  would only have implied a capability that does not exist.
- **No `ParentName` inheritance**, and no entry in the def database.
- **Unknown fields warn** rather than being silently accepted.

### Where the file goes, and why it is nested

```
Mods/YourMod/
  About/
  Defs/
  Textures/
  Mods/
    gideon.uioverhaul/
      LoadingScreen.xml      <- here
```

The nested `Mods/<packageId>` folder names **the mod you are handing data to**, not the mod you are
writing. That gives three things: the game never scans it, so nothing is parsed twice or reported as
an unrecognized def type; a mod contributing to several frameworks keeps their files apart; and each
framework only ever reads its own subtree.

`gideon.uioverhaul` is this mod's packageId, available in code as
`UILoadingScreenConfig.OwnerPackageId`.

## Minimal example

`Mods/YourMod/Mods/gideon.uioverhaul/LoadingScreen.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<LoadingScreens>
  <LoadingScreen>
    <name>MyMod_Backdrop</name>
    <label>my backdrop</label>
    <background>MyMod/Loading/Backdrop</background>
    <overlay>#000000A0</overlay>
  </LoadingScreen>
</LoadingScreens>
```

Everything else uses built-in values. Colors come from the active
[UIColorPaletteDef](UIColorPaletteDef.md), so a screen with no background image is still themed
rather than blank.

One file may define any number of screens. Every active mod is scanned, so a mod shipping only this
file — no assembly at all — is a complete loading-screen mod.

## Fields

| Field | Type | Default | Purpose |
|---|---|---|---|
| `name` | string | *required* | Lookup key, unique across all mods. What a selector persists |
| `label` | string | `name` | Display name for a selector |
| `description` | string | none | Longer text for a selector |
| `background` | texture path | none | Image under `Textures/`, no file extension. Omit for a flat palette fill |
| `backgroundFit` | enum | `Cover` | How the image maps to the screen — see below |
| `overlay` | color | none | Wash over the image. Low-alpha dark keeps text readable |
| `backgroundFlipVertical` | bool | *automatic* | Mirrors the image top to bottom. Leave unset — see below |
| `showStage` | bool | `true` | The phase name, e.g. "Processing definitions" |
| `showStep` | bool | `true` | What that phase is on right now — a mod name, a defName |
| `showProgressBar` | bool | `true` | The bar |
| `showPanel` | bool | `true` | Panel behind the stage, step and bar |
| `panelColor` | color | palette | Panel fill. Unset uses the palette's panel background at 85% alpha |
| `panelPadding` | float | `16` | How far the panel extends past the text and bar |
| `drawerClass` | type name | stock | Class that draws the screen |

`overlay` takes any form [UIColorParser](UIColorPaletteDef.md#color-formats) accepts, and the alpha is
the point of it — `#000000A0` is a 63% black wash.

### The panel

Text over a photograph is legible only where the photograph is dark, and which part that is changes
with every backdrop — for some art there is no such part at all. The panel makes readability a
property of the screen rather than of the art, which is why it is on by default.

It takes its fill from the palette, so it stays themed. An `overlay` dims the entire image to achieve
the same thing; the panel does it only where it is needed. Use one or the other, rarely both.

**Its size is fixed for the whole load.** The panel is measured from the rows the screen is
*configured* to show — `showStage`, `showProgressBar`, `showStep` — not from whether those lines
have text on any given frame. Both the stage and the step go briefly empty during a load, and a panel
measured from the text would resize, and shift the bar, every time they did. An empty line leaves its
row blank instead.

The row heights come from the fonts (`Text.LineHeightOf`), so the panel also tracks RimWorld's UI
scale rather than assuming 1.0.

Turn it off with `showPanel` if your backdrop was designed with a dark band where the text goes.

A duplicate `name` is replaced by whichever mod loads later, with a warning naming both — the same
last-wins rule load order uses everywhere else.

### `backgroundFit`

| Value | Behavior |
|---|---|
| `Cover` | Scale to fill, cropping the overflow. Keeps aspect, no bars. **Use this for a backdrop** |
| `Contain` | Scale until it all fits. Keeps aspect, leaves bars |
| `Stretch` | Fill exactly, distorting the image |
| `Center` | Native size, centered, clipped if larger |
| `Tile` | Repeat at native size from the top-left |

`Cover` is the default because a loading screen has to fill whatever aspect ratio the player's monitor
happens to be, and bars down the sides of a backdrop look like a bug.

### Image files

Put the file under your mod's `Textures/` folder, at the mod root. Write the path **without an
extension**; `.dds`, `.png`, `.jpg` and `.jpeg` are tried in that order.

For a full-screen backdrop, `.dds` is worth the conversion: `.png` and `.jpg` both decode to RGBA32 on
load (~32 MB at 3840×2160), while DXT-compressed `.dds` stays compressed in VRAM and skips the decode
entirely. `.jpg` shrinks the download but not the memory. Save DDS as BC1/DXT1, BC3/DXT5 or BC7 —
DXT3 has no Unity equivalent and is rejected with a message saying so.

`.psd` is not supported here even though RimWorld lists it, because Unity's runtime decoder cannot
read it. It would not have worked whatever we did.

**The image is loaded by us, not by RimWorld.** This is not a preference; it is the only thing that
works. `ModContentPack.ReloadContent` hands all content loading to
`LongEventHandler.ExecuteWhenFinished`, and that queue does not run until the long event *ends* — so
for the whole of a load, `ContentFinder<Texture2D>.Get` returns null for every mod texture in the
game. A backdrop fetched that way appears for a single frame, after the screen it was meant to
decorate has already gone. [UIImage](../Source/Gideon.UIOverhaul/UIFramework/Components/Images/) reads
the file off disk and decodes it, so the backdrop is there on the first frame.

That reader is public. Any mod needing a texture during loading has the same problem and can use
`UIImageLoader.Load("YourMod/Path/Image")`.

### `backgroundFlipVertical`

Leave this unset. Unity's texture origin is the bottom-left and DDS stores its rows top-down, so a
DDS handed to the GPU unchanged draws mirrored; the loader knows the file's format and corrects for
it. PNG and JPG are decoded by Unity, which already resolves this.

Set it to `false` only if a DDS of yours appears upside down, which means it was saved bottom-up.

## What the stage and step say

Both come from RimWorld's own instrumentation. Every load phase is bracketed by
`DeepProfiler.Start(label)` / `End()`, and the framework reads those labels rather than keeping its
own list of phases — a list that would quietly describe the wrong sequence after a game update.

- **Stage** is a known phase, reworded for players: `ParseAndProcessXML()` shows as
  "Processing definitions".
- **Step** is whatever the phase reports underneath — the mod being read, the node count, or the
  defName currently being built.

A phase the framework does not recognize still appears as the step; it just does not move the bar.
That is the intended behavior on a future game version: the bar gets coarser, nothing breaks.

## What the progress bar measures

Fixed weights per known phase, plus real counting inside def processing — the phase that takes most of
the time. The def total is the top-level node count of the unified XML document, so it is a true count
for the player's actual mod list rather than a tuned guess.

The bar only ever moves forward. Loading is not perfectly ordered, and a bar that jumps backwards reads
as a bug even when the numbers behind it are honest.

## Writing a custom drawer

Set `drawerClass` to the name of a `UILoadingScreenControl` subclass. Override one method rather than
`Draw` when you only want to change one part — `DrawBackground`, `DrawForeground` or `DrawPanel`:

```csharp
using Gideon.UIFramework.Controls;   // UILoadingScreenControl
using Gideon.UIFramework.Defs;       // UIColorPaletteDef
using Gideon.UIFramework.Stages;     // UILoadingScreenConfig, UILoadingSnapshot
using UnityEngine;

public class MyLoadingScreen : UILoadingScreenControl
{
    protected override void DrawForeground(Rect screen, UILoadingScreenConfig config,
        UILoadingSnapshot progress, UIColorPaletteDef palette)
    {
        // your layout; call base.DrawForeground(...) to keep the stock one underneath
    }
}
```

```xml
<drawerClass>MyMod.MyLoadingScreen</drawerClass>
```

Resolved with `GenTypes.GetTypeInAnyAssembly`, so a bare or namespace-qualified name both work. The
class needs a parameterless constructor. A name that does not resolve, or resolves to something that
is not a `UILoadingScreenControl`, logs once and falls back to the stock drawer.

`UILoadingSnapshot` carries `Stage`, `Step`, `Fraction` and `HasProgress`. It is a struct taken under
lock, so the fields are always consistent with each other.

## Driving the display yourself

Any mod can push its own text and progress, with or without our patches behind it:

```csharp
UILoadingScreen.Report("Rebuilding caches", "region 12 of 40", 0.3f);
UILoadingScreen.Report("Rebuilding caches", "region 13 of 40");   // leave the bar alone
UILoadingScreen.Reset();
```

Useful for a long operation of your own that puts up a loading screen.

## Which screen is active

`UILoadingScreenConfig.Active` resolves to:

1. Whatever was assigned to `Active` — the player's choice.
2. Otherwise `UILoadingScreen_Default`, hardcoded as `UILoadingScreenConfig.DefaultScreenName`.
3. Otherwise any screen that was found.
4. Otherwise `UILoadingScreenConfig.BuiltIn`, the compiled-in screen.

There is no flag to claim the default — a newly installed mod should not silently take over the
loading screen. Ship your screen, and let the player select it.

| Member | Returns |
|---|---|
| `Active` | The screen being drawn. Assign null to revert to `Default` |
| `ActiveName` | The chosen screen's name — what to persist to settings |
| `Default` / `BuiltIn` | Fallbacks, as above |
| `Named(string)` | A specific screen, without disturbing `Active` |
| `All` | Every screen found — use it to build a selector |
| `Reload()` | Re-reads every file from disk. For development |

## Limits worth knowing

- **The first moments stay vanilla.** Our assembly is loaded by `LoadModContent` and our patches are
  applied in `CreateModClasses` immediately after. Everything before that draws RimWorld's own screen,
  because our code is not in memory yet. No patch changes this. In practice it is a fraction of a
  second; every phase that takes real time comes after.
- **Palette colors during loading are the built-in ones.** `UIColorPaletteDef` *is* a Def, so it has
  exactly the timing problem described at the top of this page: while the loading screen is drawing,
  `UIColorPaletteDef.Active` is still the compiled-in fallback. A player who has chosen a light theme
  will still see a dark loading screen. Supply a `background` and an `overlay` if you need the screen
  to look a specific way regardless.
- **If the screen throws, vanilla comes back.** A drawing exception is logged once and the stock
  loading screen resumes for the rest of the load. A broken loading screen must not become a game that
  will not start.
