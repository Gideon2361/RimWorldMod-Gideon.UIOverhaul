# UIRichButtonControl

A button with more presentation than a plain one: three visual forms, an optional border, and an
optional slow color pulse for drawing the eye to something.

Namespace `Gideon.UIFramework.Controls`. Everything on it is public.

## Quick start

```csharp
using Gideon.UIFramework.Controls;

public class Dialog_Example : Window
{
    // A field, not a local. See "Why it is an object" below.
    private readonly UIRichButtonControl save = new UIRichButtonControl { Label = "Save" };

    public override void DoWindowContents(Rect inRect)
    {
        if (save.Draw(new Rect(inRect.x, inRect.y, 120f, 32f)))
            Save();
    }
}
```

`Draw` returns `true` on the frame it was clicked, plays its click sound, and reports nothing while
`Disabled`. It takes an optional palette; omit it and the control follows the active theme.

## Why it is an object

The pulse has to remember its phase and its toggle between frames, and a static helper has nowhere to
keep per-button state without a dictionary keyed on something fragile like a rect. So the control is
constructed once — as a field on the window or panel that owns it — and reused every frame. Building
one inside the draw call throws that state away sixty times a second, and the pulse will sit frozen at
its dark end.

This is the same pattern as [`UICardControl`](../../Source/Gideon.UIOverhaul/UIFramework/Controls/UICardControl.cs):
configure once, assign only what changes.

## Content properties

| Property | Type | Default | Notes |
|---|---|---|---|
| `Label` | `string` | `null` | The text. Optional on `Classic`, required on `Link`. |
| `Icon` | `Texture` | `null` | Drawn beside the label on `Classic`, or as the whole of an `Image`. |
| `IconFit` | `UIImageFit` | `Contain` | `Contain`, `Cover` or `Stretch`. |
| `IconTint` | `Color?` | `null` | Null tints from the palette. Set it to keep the art's own colors. |
| `Tooltip` | `string` | `null` | Hover text. |
| `Font` | `GameFont` | `Small` | One of RimWorld's fonts. |
| `Anchor` | `TextAnchor` | `MiddleCenter` | Also positions a `Link`'s underline. |
| `Disabled` | `bool` | `false` | Drawn dimmed, reports no clicks, does not pulse. |
| `ClickSound` | `SoundDef` | `SoundDefOf.Click` | Null is silent. |

A disabled button still consumes its click rather than letting it fall through to whatever is
underneath, which is what a disabled control is expected to do.

## Presentation properties

| Property | Type | Default | Notes |
|---|---|---|---|
| `HasBorder` | `bool` | `true` | The themed border. Only meaningful on `Classic`. |
| `ButtonType` | `UIButtonType` | `Classic` | See below. |

`HasBorder` routes into the same `PaintButton` the rest of the UI uses, so a bordered button here is
identical to every other button in the theme rather than a lookalike.

Set it false for buttons that sit in a continuous strip. Where buttons abut, each outline doubles
against its neighbor's into a heavy double rule and the strip reads as a grid of boxes — which is why
the main button bar draws borderless and separates its buttons with a gap instead.

## Button types

### `UIButtonType.Classic`

The themed fill, the hover and pressed wash, and the border if `HasBorder`. Behaves as any other
button in the theme. Label, icon, or both.

```csharp
new UIRichButtonControl { Label = "Apply", HasBorder = true }
```

### `UIButtonType.Image`

A bare texture that happens to be clickable, with no button surface behind it. For icon affordances
that should not look like a chrome button.

```csharp
new UIRichButtonControl
{
    ButtonType = UIButtonType.Image,
    Icon = ContentFinder<Texture2D>.Get("UI/Interface/UI.ArrowUp"),
    IconTint = Color.white,   // keeps the art's own color
    Tooltip = "Move up"
}
```

### `UIButtonType.Link`

Underlined text, the palette's `Info` color at rest and `Accent` under the cursor. For a "learn more"
or an inline navigation action.

```csharp
new UIRichButtonControl
{
    ButtonType = UIButtonType.Link,
    Label = "What does this do?",
    Anchor = TextAnchor.MiddleLeft
}
```

The rule is drawn under the text's measured width rather than the whole rect, so a centered or
right-aligned link is not underlined across empty space.

## The pulse

A slow fade back and forth between two colors. Off unless `ButtonEffectPulse` is set.

| Property | Type | Default | Notes |
|---|---|---|---|
| `ButtonEffectPulse` | `bool` | `false` | The master switch. |
| `ButtonEffectPulseCondition` | `UIButtonPulseEffectCondition` | `Always` | When it runs. |
| `ButtonEffectPulseDark` | `Color?` | `null` → palette `SurfaceSunken` | The low end. |
| `ButtonEffectPulseLight` | `Color?` | `null` → palette `SurfaceRaised` | The high end. |
| `ButtonEffectPulseGlow` | `bool` | `true` | The halo outside the button. |
| `ButtonEffectPulseGlowColor` | `Color?` | `null` → the pulse's light end | The halo's color. |
| `ButtonEffectPulseGlowSize` | `float` | `8` | How far the halo reaches, in pixels. Zero disables it. |

### Timing

Three seconds dark to light, three seconds back, repeating. Exposed as constants so a caller can
schedule against the same numbers:

```csharp
UIRichButtonControl.PulseHalfCycleSeconds   // 3
UIRichButtonControl.PulseCycleSeconds       // 6
```

The wave is a linear triangle. Easing the turnaround would keep the same period while changing where
the color actually sits at a given moment, so it is left linear by default; `SmoothStep` on the phase
is a one-line change in `PulseColor` if a softer turn reads better.

Timing runs off `Time.realtimeSinceStartup`, not `Time.time`. `Time.time` does not advance while the
game is paused, and a paused game is exactly when a player is sitting in a dialog reading it.

### Conditions

| Value | Behavior |
|---|---|
| `Always` | Runs continuously for as long as the button is drawn. |
| `Toggle` | A click switches it on, the next click switches it off, repeatably. |
| `Hover` | Runs while the cursor is over the button. |
| `Once` | One full cycle when the button is first drawn, then not again for this control's lifetime. |

Two details about these:

**The phase resets to dark whenever the pulse stops.** Every run starts from the dark end rather than
resuming wherever the wave happened to be. This matters most for `Hover` — picking up mid-cycle would
make the button flash to a random brightness the instant the cursor arrived.

**`Once` is measured from the first draw, not from construction.** A control built in a field
initializer can exist long before its window is shown, so timing from construction would mean the
pulse had already finished by the time anyone saw it.

### What "closed and reopened" means for `Once`

It falls out of ownership. A control held as a field on a window is constructed with that window, so
reopening the window creates a new control and re-arms the pulse. Nothing special is needed.

A control cached beyond its window's lifetime — a `static` field, or one on a long-lived manager —
will not re-arm on its own. Call `ResetPulse()` when the window opens:

```csharp
public override void PreOpen()
{
    base.PreOpen();
    highlight.ResetPulse();
}
```

### The glow

The pulse carries a halo that swells as it brightens toward the light end and fades as it recedes to
dark. It is on by default: a pulse confined to the button's own fill is easy to miss against a busy
panel, and the glow is what makes it read as attention-seeking rather than as a slow recolor.

It is built from four concentric outlines stepping outward from the button rect, each fainter than the
one inside it, with the whole thing scaled by the pulse phase. Outlines rather than a soft-edged
texture because that needs no art and no shader, and four rings two pixels apart are
indistinguishable from a real gradient at this size.

Its color defaults to the pulse's light end, so the glow reads as that color bleeding outward. Override
it when the pulse is doing something other than brightening — a warning that pulses toward red, for
instance, may want a glow that stays red rather than following the fill.

```csharp
new UIRichButtonControl
{
    Label = "Abandon colony",
    ButtonEffectPulse = true,
    ButtonEffectPulseDark = new Color(0.35f, 0.10f, 0.08f),
    ButtonEffectPulseLight = new Color(0.90f, 0.30f, 0.22f),
    ButtonEffectPulseGlowSize = 12f
}
```

The halo draws **outside** the button's rect, so leave room for it in your layout. A button flush
against a panel edge will have its glow clipped by whatever group or scroll view contains it.

Set `ButtonEffectPulseGlow` false, or `ButtonEffectPulseGlowSize` to zero, for a pulse that stays
within the button.

### Where the pulse renders

It depends on the type, because the three forms have different surfaces to work with.

| Type | The pulse drives |
|---|---|
| `Classic` | The button's fill, *replacing* the normal surface color |
| `Image` | A plate drawn behind the texture |
| `Link` | The text color |

On `Classic` it replaces the fill rather than washing over it, so the two colors supplied are the two
colors that appear. Layering it over the normal surface would mean neither end of the pulse matched
what was asked for.

On `Image` it sits behind the art rather than tinting it, because an image button is usually chosen
precisely to keep the art's own colors.

On `Link` it drives the text, since there is no surface to carry it. **A pulsing link should set its
own colors** — the defaults are surface tones and read as muddy text.

### Driving a toggle from outside

`PulseToggled` is readable and settable, so a `Toggle` pulse can be turned on by something other than a
click — a validation failure drawing attention to the button that fixes it, for instance:

```csharp
saveButton.ButtonEffectPulse = true;
saveButton.ButtonEffectPulseCondition = UIButtonPulseEffectCondition.Toggle;
saveButton.PulseToggled = hasUnsavedChanges;
```

## Worked example

A save button that pulses gently until the changes are written:

```csharp
private readonly UIRichButtonControl save = new UIRichButtonControl
{
    Label = "Save",
    ButtonEffectPulse = true,
    ButtonEffectPulseCondition = UIButtonPulseEffectCondition.Toggle
};

public override void DoWindowContents(Rect inRect)
{
    save.PulseToggled = dirty;

    if (save.Draw(new Rect(inRect.x, inRect.yMax - 40f, 120f, 32f)))
    {
        Commit();
        dirty = false;
    }
}
```

Note the pulse is driven from `dirty` every frame rather than being toggled by the click. With
`Toggle`, a click flips `PulseToggled` as well — assigning it each frame keeps the state owned by the
data rather than by the click history.

## Gotchas

- **Construct once, draw many.** A control built inside the draw call loses its pulse phase and its
  toggle every frame.
- **A pulsing `Link` needs explicit colors.** The surface-tone defaults are close to unreadable as
  text.
- **`HasBorder` does nothing on `Image` or `Link`.** Neither has a surface for a border to outline.
- **`Disabled` suppresses the pulse.** A disabled control drawing attention to itself is a
  contradiction.
