# UICardControl

A filled panel with an optional accent stripe down its left edge, an optional background image, and any number
of elements inside it. The building block behind the grow-zone list, the work tab's rows and the pawns tab's rows.

```csharp
private static readonly UICardControl card = new UICardControl
{
    Height = 52f,
    AccentWidth = 3f
};

// Per frame: assign what changed, then draw.
card.AccentColor = zone.Healthy ? palette.Success : palette.Warning;

if (card.Draw(rowRect, palette))
    Select(zone);
```

Construct one and **reuse it across frames**, assigning only what changed. That is the point of the card being an
object rather than a static helper: a list of fifty rows reconfigures one card fifty times instead of allocating
fifty.

Every color property is `Color?` defaulting to null, meaning "ask the palette". A card follows the active theme
unless deliberately overridden.

## Card properties

| Member | Default | Meaning |
|---|---|---|
| `Width` / `Height` | `0` / `130` | Preferred size. `Height` is what a scrolling list needs to lay cards out. |
| `Padding` | `8` | Inset between the card's edge and its content, accent stripe included. |
| `AccentColor` | `null` | Stripe down the left edge, to categorize at a glance. Null draws none. |
| `AccentWidth` | `3` | Width of that stripe. |
| `BackgroundColor` | `null` → `PanelBackground` | Fill behind everything. |
| `BackgroundTexture` | `null` | Image over the fill, under the content. |
| `BackgroundFit` | `Stretch` | `Cover`, `Contain` or `Stretch`. |
| `BackgroundTint` | `null` → white | Tint for that image. |
| `BorderColor` | `null` | Single-pixel border. Null draws none. |
| `Selected` | `false` | Draws the palette's `SelectionOverlay` over the card. |
| `HoverHighlight` | `true` | Draws `HoverOverlay` when the cursor is over the card. |
| `Tooltip` | `null` | Hover text for the card as a whole. |
| `Elements` | empty | Content, drawn in order. |

## Methods

| Method | Does |
|---|---|
| `Draw(Rect, palette)` | Draws the card **and claims the click**. Returns whether it was clicked. |
| `DrawChrome(Rect, palette)` | Draws the card **without** claiming the click. |
| `ContentRect(Rect)` | The card inset by `Padding` and the accent stripe. |
| `Add<T>(T element)` | Appends an element and returns it, so you can keep the reference. |

### Draw or DrawChrome

This is the one choice that will bite you, so it is worth stating plainly.

`Draw` ends with `Widgets.ButtonInvisible(card)`, which **consumes the click event**. Any button drawn inside the
card afterwards never sees its own click. So:

- A card that is itself one big button → **`Draw`**.
- A card containing controls of its own — icon buttons, a checkbox, a text box → **`DrawChrome`**, and handle
  whatever clicks you want yourself.

The work and pawns tabs both use `DrawChrome`, because their rows contain priority boxes, tool buttons and fold
arrows that need their own clicks.

## Content, from either direction

Content can come from elements or from your own drawing code, and mixing them is fine — **elements draw first**.

```csharp
// Declarative: the card lays these out and draws them.
UICardLabel name = card.Add(new UICardLabel { Bounds = new Rect(0, 0, 200, 22) });
UICardMeter bar  = card.Add(new UICardMeter { Bounds = new Rect(0, 26, 200, 12) });

name.Text = pawn.LabelShortCap;      // assign between frames
bar.Fraction = pawn.health.summaryHealth.SummaryHealthPercent;
```

```csharp
// By hand: ignore Elements and draw into the content rect.
card.DrawChrome(rowRect, palette);
Widgets.Label(card.ContentRect(rowRect), "whatever you like");
```

Element positions are in **card-content space**, so an element never needs to know where its card landed on
screen. A zero-size `Bounds` fills the whole content rect, which is what a single-element card almost always
wants.

## Element types

All share `Bounds`, `Visible` (keep it in the card but do not draw it) and `Tooltip` (hover text for that element
alone; null falls back to the card's).

| Type | Properties |
|---|---|
| `UICardLabel` | `Text`, `Color?` (null → `TextPrimary`), `Font`, `Anchor`, `WrapText` |
| `UICardImage` | `Texture`, `Fit`, `Tint?` |
| `UICardMeter` | `Fraction` (0–1, clamped), `FillColor?` |

`UICardImage.Tint` defaults to **null, meaning untinted** — deliberately, because most card images are full-color
art rather than silhouettes, and tinting those to a theme color destroys them.

`UICardMeter` delegates to `UIProgressBarControl`, so a meter in a card and a bar drawn directly look identical.

## Draw order

Worth knowing if you are layering something yourself:

1. `BackgroundColor` fill
2. `BackgroundTexture`
3. Accent stripe
4. `SelectionOverlay` **or** `HoverOverlay` — selection wins; a selected card does not also take a hover wash
5. Elements, in list order
6. `BorderColor`
7. Tooltips

`GUI.color` is saved and restored around the whole of it, so a card never leaks a tint into whatever draws next.
