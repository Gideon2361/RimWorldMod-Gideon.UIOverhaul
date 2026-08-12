# UICheckboxControl

A themed checkbox, in place of `Widgets.CheckboxLabeled`.

```csharp
using Gideon.UIFramework.Controls;

if (UICheckboxControl.Draw(rect, ref autoUnsuspend, label: "Auto-unsuspend"))
    Save();
```

Returns `true` on the frame the value changed, and flips the `ref` bool itself, so the caller's `if` is
free for whatever should happen on a change rather than for the toggle.

## Why it is static

There is no object to construct, unlike [`UIRichButtonControl`](UIRichButtonControl.md) or
`UICardControl`. A checkbox holds nothing between frames — its state is the bool the caller already owns
— so an instance would carry no state and buy nothing. `UIProgressBarControl` is static for the same
reason.

## Draw

```csharp
public static bool Draw(Rect rect, ref bool value, UIColorPaletteDef palette = null,
    string label = null, string tooltip = null, UICheckboxSide side = UICheckboxSide.Left,
    bool disabled = false)
```

| Parameter | Effect |
|---|---|
| `rect` | The whole row, which is the hit target — a label is as clickable as its box, as in vanilla |
| `value` | Flipped in place when clicked |
| `palette` | Null uses `UIColorPaletteDef.Active` |
| `label` | Null or empty draws the box alone, **centered** in `rect`. That is the grid-cell case |
| `tooltip` | Registered over the whole row |
| `side` | `Left` puts the box first; `Right` puts the label first and the box against the right edge, which is vanilla's settings-page arrangement |
| `disabled` | Drawn dimmed, returns `false`, and still consumes the click rather than letting it fall through to whatever is underneath |

`BoxSize` is a public `const` at 20f. The row may be taller; the box is centered vertically in it.

## DrawBox

```csharp
public static void DrawBox(Rect box, bool value, UIColorPaletteDef palette = null, bool disabled = false)
public static void DrawBox(Rect box, MultiCheckboxState state, UIColorPaletteDef palette = null,
    bool disabled = false)
```

The box alone, with no hit handling, for a caller that already owns the click — a grid cell whose whole
rect does something larger, or a read-only indicator for a state the player changes elsewhere.

The second overload takes vanilla's `MultiCheckboxState`, so a window of yours can show the `Partial`
state that thing filter trees use. `Draw` itself is boolean, since a tri-state box needs a cycling rule
that belongs to the caller rather than to the control.

## Appearance

A sunken box, `Accent` border and a filled `Accent` square inside when checked, `Border` when not, and a
horizontal `Accent` bar for `Partial`. Not vanilla's `CheckboxOnTex` and `CheckboxOffTex`: those are stock
chrome, and in a themed window they would be the only piece of it left.

Everything comes from the active [palette](UIColorPaletteDef.md), so a light template needs no changes
here.

### Shared with the game's own checkboxes

The drawing is not in this control. It lives in one internal painter that this control and the patches on
`Widgets.CheckboxDraw` and `Widgets.CheckboxMulti` all call, so a vanilla checkbox and one of ours are the
same pixels rather than two implementations that agree for now. See
[GameChanges.md](GameChanges.md#checkboxes).

The practical consequence for a modder: you do not need this control to get the look. Calling
`Widgets.CheckboxLabeled` gives you a themed checkbox already. Reach for `UICheckboxControl` when you want
the row behavior — the label side, the centered label-less box for grid cells, or the change-reporting
return value.
