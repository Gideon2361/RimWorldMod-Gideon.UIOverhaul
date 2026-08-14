# Button bar widgets

A **widget** is a readout or a control that sits on the button bar beside the tabs: the play speed
buttons, the date, the outdoor temperature, the weather.

RimWorld puts all four of those in the bottom right of the screen, stacked under the alerts. That
corner is also where letters queue up, where game conditions list themselves and where the play
settings toggles live, so the information competes with everything else for the same space and moves
as the pile above it grows. A slot on the bar does not move, is always the same width, and is next to
the buttons the player is already looking at.

Four widgets ship with this mod. Any mod can add more.

- [Putting one on the bar](#putting-one-on-the-bar)
- [What ships](#what-ships)
- [Why nothing is on the bar by default](#why-nothing-is-on-the-bar-by-default)
- [How they are laid out](#how-they-are-laid-out)
- [Writing your own](#writing-your-own)
- [The stored layout](#the-stored-layout)
- [Design notes](#design-notes)

## Putting one on the bar

The bar button at the far right of the bar opens **UI Options**. Choose **Manage Tabs** from the
category list down the left, then press **Open Manager**. The right-hand column lists everything not
currently on the bar: tabs first,
then a **Widgets** heading with the widgets, each showing what it displays. Press `+` on one to put
it on the bar, then drag its row to where you want it and press **Save**.

Widgets behave like tabs in that editor. They can be dragged to reorder, they can be dropped on the
right-hand column to take them back off, and `–` on their row does the same thing. They cannot be put
inside a menu: a menu reveals tabs, and a readout inside one would never be drawn.

There is no icon, no rename and no display mode on a widget row, because a widget draws its own
content rather than a label and an icon.

## What ships

| Widget | defName | Shows |
|---|---|---|
| Speed controls | `Gideon_BarWidget_TimeSpeed` | Pause and the three play speeds |
| Date and time | `Gideon_BarWidget_Date` | The colony date and time, behind a calendar icon |
| Outdoor temperature | `Gideon_BarWidget_Temperature` | The current map's outdoor temperature |
| Weather | `Gideon_BarWidget_Weather` | The weather as it currently appears, with an icon for it |

**Speed controls** mark the speed in effect and draw a rule across the buttons whenever something
else is deciding it: a window that forces pause, or a threat that forces normal speed. Clicks are
ignored while pause is forced, which is what vanilla does too, and the rule across the buttons is
what explains it.

The widget handles clicks and nothing else. Vanilla's own time controls still draw in the bottom
right and still own every time-related key binding, so space, the number keys, faster, slower and the
dev-mode single tick all keep working and cannot disagree with these buttons about what the speed is.
Ultrafast is not offered, matching vanilla: the enum has five values and the base game's own control
deliberately draws four.

**Date and time** is one line where vanilla stacks three, because the bar is 35 pixels tall. The date
is `GenDate.DateFullStringAt`, vanilla's own translated string; the season and the quadrum-to-season
calendar move into the tooltip, which is vanilla's own tooltip text. It is read at the current map's
longitude, so two colonies on opposite sides of the planet do not read the same clock.

The time of day is written on a **24-hour clock with minutes** by default. UI options has the choice,
under "Clock":

| Setting | Shows |
|---|---|
| 24-hour | `14:30` |
| 12-hour | `2:30 PM` |
| RimWorld's | `14h` |

The last is character for character what vanilla writes, for anyone who wants the readout the widget
had before there was a choice.

RimWorld has **no minute**. A day is 60000 ticks and an hour is 2500, so the minute is the position
within the current hour divided into sixty, and it exists so that the readout looks like a clock.
Nothing in the game changes on that boundary, which is worth knowing before reading anything into
`:59`. The reason to show it anyway is that a bare hour cannot tell you how much of itself is left,
and shift ends, caravan arrivals and growing seasons are all read off this line.

**Outdoor temperature** is always the outdoor temperature. Vanilla's readout reports the room under
the cursor and only falls back to outdoors when there is no room, which is right for something you
consult while pointing at a wall. On the bar it would be wrong: the number would change every time
the pointer crossed a doorway, so a glance at the bar would tell you about the mouse rather than
about the weather. It is blue below what a human finds comfortable and red above, with the band read
off `ComfyTemperatureMin` and `ComfyTemperatureMax` on the human def rather than hardcoded, so a mod
that changes what people can stand changes what this calls cold.

**Weather** reports `CurWeatherPerceived` rather than `curWeather`. Weather changes over 4000 ticks,
and during that transition the actual value has already flipped while the sky and the sound are still
arriving; the perceived value is the one that agrees with what the player is looking at. It hides
itself on pocket maps, as vanilla hides its own weather line there, because a pocket map has a
weather manager only by virtue of being a map and has no sky.

It draws an **icon** beside the name. `WeatherDef` carries no art, so the icons are generated at
startup rather than shipped as files, the same bargain the framework's other generated shapes make: a
generated glyph is tinted to whatever palette role the widget draws in, so it is legible on a light
theme and a dark one without a second set of files, and there is nothing on disk for another mod to
shadow with a texture of the same path.

There are **thirteen glyphs for twenty-two weathers**, because the icon sits immediately to the left
of the weather's own name. Its job is to be recognizable at sixteen pixels, not to be uniquely
decodable: blood rain and toxic rain both get the rain glyph, and the word beside it says which.

A weather this mod has never heard of still gets a sensible icon. Every Core, Anomaly and Odyssey
weather is mapped by name; anything else is classified from the def's own fields, in this order:

| Test | Glyph |
|---|---|
| `snowRate > 0` | Snow, or blizzard when `windSpeedFactor > 1.5` |
| `sandRate > 0` | Sand |
| `rainRate > 0` | Rain, or heavy rain at `rainRate >= 1.5` |
| `windSpeedFactor > 1.5` | Wind |
| otherwise | Overcast |

Overcast is the fallback rather than clear, because a clear sky is a specific claim and the sun would
be wrong for the darkness weathers several mods add.

**Drawn art wins.** A texture at `UI/WeatherIcons/<defName>` is used in preference to the generated
glyph, so a mod can ship real art for its own weather without a code change, and without this mod
having to know the name.

## Why nothing is on the bar by default

A fresh install has no widgets on the bar, and installing a mod that adds one does not put it there.

That is the opposite of how tabs work. `UIButtonBarConfig.Resolve` appends any `MainButtonDef` the
saved layout does not name, because a tab that failed to appear after installing a mod would look
like the mod was broken. Widgets get the reverse treatment: the game already shows all four of these
somewhere, so a mod putting them on the bar uninvited is adding clutter rather than fixing an
absence. A widget is drawn only where the layout names it.

That is also why there is no `hidden` list for widgets as there is for tabs. Absence is the default,
so absence needs no record.

## How they are laid out

Widget slots are **fixed width**. Tab buttons showing text divide up whatever is left of the bar
between them; a readout cannot work that way, because sized to a share it would have its text cut off
on a crowded bar and swim in space on an empty one.

Each widget measures what it needs, and the bar then does two things to that number:

- **Rounds it up to a multiple of 8.**
- **Never lets it shrink.** The width a widget asks for is the widest it has ever needed.

Both exist to stop the bar twitching. Text width changes as the text does: an hour ticks over, a
temperature loses a digit, the weather turns from "Clear" to "Foggy". Measured exactly, every one of
those would shift the widget and every button to its right by a pixel or two. Rounding absorbs the
small changes and the high-water mark absorbs the rest; a widget converges within the first few
seconds of play and then stops changing size. Reclaiming eight pixels later is not worth a bar that
never settles.

A widget that has nothing to report hides itself, and the slot goes with it rather than leaving a
gap. The remaining buttons share the width.

## Writing your own

Two pieces: a class and a def. No Harmony patch, and no reference to this assembly beyond the base
type.

### 1. The worker

```csharp
using Gideon.UIFramework.Defs;
using Gideon.UIOverhaul.Features.ButtonBar;
using UnityEngine;
using Verse;

namespace YourMod
{
    public class BarWidget_Silver : UIBarWidgetWorker
    {
        // Hide the slot when there is nothing to say. Optional; the default is always visible.
        protected override bool ShouldShow => Find.CurrentMap != null;

        // How much room you want, before the bar quantizes it.
        protected override float MeasureWidth() => TextWidth(Reading()) + 16f;

        public override void Draw(Rect rect, UIColorPaletteDef palette)
        {
            // The bar has already painted a sunken tray behind you. The rect is yours.
            DrawReadout(rect, Reading(), palette.TextSecondary, "Silver in storage.");
        }

        private static string Reading() =>
            Find.CurrentMap?.resourceCounter.Silver.ToString() ?? "";
    }
}
```

`TextWidth` and `DrawReadout` are helpers on the base class, for the common case of one line of text
with a tooltip. `DrawReadout` sets the font and anchor, restores both, and takes the tooltip so you
do not have to remember `TooltipHandler`.

Take colors from the `palette` you are handed, not from `Color.white` or from your own constants. It
is the theme the player chose, and a widget that ignores it is the one thing on the bar that does not
change when they switch themes.

### 2. The def

```xml
<?xml version="1.0" encoding="utf-8"?>
<Defs>

  <Gideon.UIOverhaul.Features.ButtonBar.UIBarWidgetDef>
    <defName>YourMod_BarWidget_Silver</defName>
    <label>silver</label>
    <description>How much silver is in storage on this map.</description>
    <workerClass>YourMod.BarWidget_Silver</workerClass>
    <minWidth>64</minWidth>
    <order>50</order>
  </Gideon.UIOverhaul.Features.ButtonBar.UIBarWidgetDef>

</Defs>
```

| Field | Meaning |
|---|---|
| `label` | Shown in the bar editor. Lowercase, as RimWorld labels are; it is capitalized where it is drawn |
| `description` | Shown under the label in the editor's widget list. Say what the widget displays |
| `workerClass` | Your `UIBarWidgetWorker` subclass. Required |
| `minWidth` | Floor for the measured width, so briefly short text does not collapse the slot. Default 40 |
| `order` | Sort position in the editor's list. Presentation only; where it sits on the bar is the player's business |

Your mod does not need to load after this one, and does not need to be a dependency of it. The def
is read whenever it loads and the widget appears in the editor's list the next time it is opened.

### What the framework guarantees you

- **One instance per def**, created on first use and kept for the session, so caching in a field is
  safe and expected.
- **Main thread, inside the bar's OnGUI pass.** `MeasureWidth` is called first, then `Draw`.
- **Failures are contained.** Anything thrown from `ShouldShow`, `MeasureWidth` or `Draw` logs once,
  switches that one widget off for the rest of the session, and leaves the bar working. A widget can
  come from any mod, and one bad frame must not cost the player the bar they navigate the game with.
- **Clicks are absorbed.** The bar consumes any click in your slot that you did not take, so a
  readout cannot leak a click through to the map and issue an order behind the bar.
- **The tray and its rule are drawn for you**, before `Draw` is called. The top 3 pixels of your slot are
  the rule, so inset past it if you are drawing controls rather than text. The shipped speed controls take
  `UIButtonBarRenderer.AccentRuleHeight` off the top and then their own padding, which keeps a visible gap
  between the rule and the buttons instead of welding the two together.

### A trap worth knowing about

The widget implementations in this mod live in a namespace called `BarWidgets`, not `Widgets`. A
child namespace named `Widgets` shadows `Verse.Widgets` for every file in the parent namespace, so
`Widgets.Label` in the bar's own renderer and editor stops resolving the moment such a folder exists.
If you are adding a widget folder inside a namespace that draws anything, do not call it `Widgets`.

## The stored layout

Widgets are slots in the same file as everything else on the bar,
`UIOverhaul_ButtonBar.xml` in RimWorld's config folder:

```xml
<entry><tab>Architect</tab></entry>
<entry><widget>Gideon_BarWidget_TimeSpeed</widget></entry>
<entry><widget>Gideon_BarWidget_Date</widget></entry>
<entry>
  <tab>Menu</tab>
  <last>true</last>
</entry>
```

An `<entry>` is exactly one of `<tab>`, `<menu>` or `<widget>`. A widget entry ignores `<label>`,
`<icon>` and `<mode>`, which have nothing to act on. A `<menu>` carries its contents as nested
`<entry>` elements under `<children>`; see [GameChanges.md](GameChanges.md#menus-on-the-button-bar).

An entry naming a widget whose mod is no longer installed is skipped and **left in the file**, the
same as an entry naming a missing tab, so turning that mod off and on again does not cost the player
the slot.

## Design notes

**Why a def rather than an enum.** The bar's display modes are an enum, and four widgets could have
been one too. A widget is different in kind: it is a piece of UI with behavior, and the interesting
ones are the ones nobody has thought of yet. A def with a `workerClass` is the bargain RimWorld
already offers for designators, main button tabs and stat workers, and it means a widget costs
another mod one class and six lines of XML.

**Why the vanilla readouts are left alone.** With a date widget on the bar, the date also still shows
in the bottom right. Suppressing the duplicate is a separate change and a larger one than it looks:
that corner is a stack, and removing one line from the middle of it moves the alerts, the letters and
the game conditions above it. Nothing has been patched out, so nothing has been broken; the widgets
add a place to read this, they do not take the old place away.

**Why the tray is sunken.** Tab buttons are raised; widgets get a sunken tray. That is a deliberate
visual grammar: raised means you can press it, sunken means it is telling you something. The speed
controls sit inside their tray as raised buttons, so within one slot the distinction still reads
correctly.

**Why the rule is gray.** Every slot on the bar carries a rule along its top edge, widgets included, so
the strip has one continuous edge rather than rules that stop and start across it. The *color* is what
carries meaning, and it means the same thing on a widget as on a tab: the accent says "this leads
somewhere". A readout leads nowhere, so it takes the disabled gray.

Handling a click is not the same as leading somewhere. The speed controls take clicks all day and stay
gray, because pressing one changes the speed and nothing else — the widget only ever acts on itself.
Override `OpensMenu` to return true where a click actually puts something new on screen:

```csharp
public override bool OpensMenu => true;
```

That is the only thing that changes the color, and it is worth being strict about. If a widget claims the
accent for reacting to a click at all, the accent stops distinguishing anything.
