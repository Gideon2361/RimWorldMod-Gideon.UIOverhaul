# UIRadioButtonControl

A themed radio button, in place of `Widgets.RadioButtonLabeled`.

```csharp
using Gideon.UIFramework.Controls;

if (UIRadioButtonControl.Draw(rect, mode == Mode.Fast, label: "Fast"))
    mode = Mode.Fast;
```

## It does not change your value

Unlike [`UICheckboxControl`](UICheckboxControl.md), which takes a `ref bool` and flips it, this reports the
click and nothing more. A radio button belongs to a group, and only the caller knows what else is in that
group and what selecting this one should turn off. Vanilla's `Widgets.RadioButton` works the same way.

That is why `selected` is passed by value: it is what to draw, not something to write to.

## Draw

```csharp
public static bool Draw(Rect rect, bool selected, UIColorPaletteDef palette = null,
    string label = null, string tooltip = null, UICheckboxSide side = UICheckboxSide.Left,
    bool disabled = false)
```

| Parameter | Effect |
|---|---|
| `rect` | The whole row, which is the hit target — the label is as clickable as the circle |
| `selected` | Whether this option is the chosen one |
| `palette` | Null uses `UIColorPaletteDef.Active` |
| `label` | Null or empty draws the circle alone, **centered** in `rect` |
| `tooltip` | Registered over the whole row |
| `side` | Which edge the circle sits on. `Right` matches vanilla's labeled radio button |
| `disabled` | Drawn dimmed, returns `false`, and still consumes the click |

`ButtonSize` is a public `const` at 24f, the same as vanilla's `Widgets.RadioButtonSize`, so a column of
ours lines up with a column of theirs.

`UICheckboxSide` is shared with the checkbox control rather than duplicated under a radio-specific name —
it answers the same question for both.

Clicking plays `Tick_Tiny`, which is what vanilla's radio buttons play. There is no second sound, because
there is no "off" click on a radio button.

## DrawButton

```csharp
public static void DrawButton(Rect circle, bool selected, UIColorPaletteDef palette = null,
    bool disabled = false, bool over = false)
```

The circle alone, with no hit handling, for a caller that already owns the click.

Pass `over` yourself. What counts as hovered is whatever region responds to a click, which for a labeled
row is the row and not the circle — the control passes row hover for exactly that reason.

## Appearance

| Part | Role |
|---|---|
| Outer circle | `Accent` when selected, `Accent` at 55% when hovered and unselected, `TextDisabled` when disabled, `Border` otherwise |
| Interior, including the 2px gap | `WindowBackground` when unselected, `AccentMuted` when selected |
| Inner circle | Absent when unselected; `WindowBackground` when selected |

The ring is what carries selection, so a selected button is unmistakable across a column of them.

Two details in that first row are there for a reason:

- **Selection outranks hover.** A hovered button that is already selected has nothing to promise.
- **Hover is the accent at part strength**, not at full. Full accent is what selected means, and lighting an
  unselected button to the same value made the two indistinguishable until the pointer moved away — which
  defeats coloring the selected one at all.

Everything comes from the active [palette](UIColorPaletteDef.md), so a light template needs no changes here.

### Shared with the game's own radio buttons

The drawing lives in one internal painter that this control and the patch on `Widgets.RadioButtonDraw` both
call, so a vanilla radio button and one of ours are the same pixels rather than two implementations that
agree for now. See [GameChanges.md](GameChanges.md#radio-buttons).

As with checkboxes, that means you do not need this control to get the look — `Widgets.RadioButtonLabeled`
is already themed. Reach for this one when you want the row behavior: the label side, the centered
label-less circle for grid cells, or a hover state driven by the row rather than the circle.
