# UITextBoxControl

A themed text box that keeps its keyboard focus, and that the game's key bindings respect.

```csharp
private static readonly UITextBoxControl Search = new UITextBoxControl
{
    Placeholder = "Search",
    Icon = TexButton.Search,
    MaxLength = 30
};

if (Search.Draw(rect))
    scroll = Vector2.zero;   // the text changed this frame
```

Construct one and keep it. It is a class rather than a static helper because it holds three things
between frames: its text, its control name, and whether the player means it to be focused. The last of
those is why the type exists.

## Properties

| Member | Default | Meaning |
|---|---|---|
| `Text` | `""` | The current text. Never null; assigning does not raise a change. |
| `IsEmpty` | — | Whether `Text` is empty. |
| `Placeholder` | `null` | Drawn in `TextDisabled` when the box is empty and unfocused. |
| `MaxLength` | `60` | Longest accepted text. Vanilla's search widget caps at 30. |
| `Icon` | `null` | Drawn inside the left edge. `TexButton.Search` makes it a search field. |
| `ShowClearButton` | `true` | A small x at the right, once there is something to clear. |
| `Focused` | — | Whether this box believes it holds focus. |
| `AnyFocused` | — | **Static.** Whether any text box of ours holds focus. |

## Methods

| Method | Does |
|---|---|
| `Draw(Rect, palette)` | Draws the box. **Returns whether the text changed this frame.** |
| `Focus()` / `Unfocus()` | Takes or gives up focus. `Focus` applies on the next draw. |
| `Clear()` | Empties the box without disturbing focus. |
| `Matches(string)` | Case-insensitive substring test. An empty box matches everything. |

`Matches` is there for the common case of a box that filters a list, so a caller can pass every
candidate through it without first testing whether the box is empty:

```csharp
foreach (Pawn pawn in pawns)
    if (Search.Matches(pawn.LabelShortCap) || Search.Matches(pawn.LabelCap))
        Draw(pawn);
```

## Why this exists rather than QuickSearchWidget

Two problems, and vanilla's widget only solves one of them.

### Focus survives a control-id shift

IMGUI keys keyboard focus on `GUIUtility.keyboardControl`, an **integer derived from draw order**.
`GUI.SetNextControlName` does not change that; it only hangs a name on whichever id the control
received that frame. So if the id shifts — because a control ahead of it appeared, disappeared or was
reordered — `keyboardControl` still points at the old integer, `GUI.GetNameOfFocusedControl()` stops
returning the box's name, and the field goes dead mid-word. Vanilla's widget has no defense: it asks
whether it is focused and believes the answer.

This control reconciles the two every frame, and the third case below is the repair:

| Unity says | This box wants | Result |
|---|---|---|
| this box is focused | — | recorded as focused |
| **nothing** is focused | focus | **focus re-asserted by name** |
| another named control | focus | concedes — the player clicked away |

The middle row fixes the fault without needing to know which control shifted, which matters because
the cause is inside Unity's id allocation where a mod cannot see it.

The last row is what stops the repair from becoming a bug of its own. A box that simply re-focused
itself whenever it was not focused would fight every other text field in the game, every frame.

Escape and a click outside are handled before any of this, so a deliberate blur always beats a repair
in the same frame. The click test reads `OriginalEventUtility.EventType` rather than
`Event.current.type`, because by then another control may have consumed the event — the same source
vanilla's widget consults for this.

### Key bindings are suppressed while it has focus

Every `KeyBindingDef` in the game — `IsDown`, `IsDownEvent`, `KeyDownEvent`, `JustPressed` — returns
false while `WindowStack.AnySearchWidgetFocused` is true. That single gate is the whole mechanism
stopping W and A from panning the map as you type, and camera dolly is only its most obvious member.

The gate walks the window stack asking each window for its `CommonSearchWidget`, so it can only ever
see a `QuickSearchWidget` **owned by a window**. A control is neither — and the panel drawing it is
often not the window either. `Patch_WindowStack_AnySearchWidgetFocused` ORs `AnyFocused` into the
gate, which gives every text box of ours the same protection vanilla gives its own, anywhere in the
game, with no per-window wiring.

`AnyFocused` is a tracked reference rather than a live `GUI.GetNameOfFocusedControl()` call, because
the gate is read from `CameraDriver.Update` — outside OnGUI, where that query is not dependable. It is
also frame-stamped: a window closing while its box was focused would otherwise leave the gate stuck
shut, since the box stops drawing and nothing clears the reference. Staleness is judged, not trusted.

## Drawing order inside the control

Worth knowing if you subclass or imitate it:

1. **Focus is resolved first**, before the field is drawn, so a repair is in place by the time Unity
   allocates the field's id.
2. The chrome, then the icon.
3. The clear button's lane is reserved whether or not the button is drawn, so text does not reflow the
   moment the first character arrives.
4. **The clear button is drawn last.** A control that comes and goes *ahead* of the field is exactly
   what shifts its id — the fault this control exists to survive rather than to cause.

## Notes

- The caret and text selection use vanilla's field style, so editing feels like the rest of the game.
  Only the colors are ours; a themed caret would mean reimplementing text editing.
- Clearing with the x keeps focus, on the grounds that clearing is usually the start of a new search
  rather than the end of one.
