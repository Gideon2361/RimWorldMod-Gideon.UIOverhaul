# UIDesignatorTabControl

A grid for a main tab: a column per thing being set, a row per thing being set on, and a heading row that
stays put while the rows scroll under it.

```csharp
using Gideon.UIFramework.Controls;

private static readonly UIDesignatorTabControl Grid = new UIDesignatorTabControl
{
    HeaderLabelOrientation = UIHeaderAngle.Diagonal,
    RowHeight = 62f
};

public override void DoWindowContents(Rect inRect)
{
    Grid.Rows.Clear();

    foreach (Pawn pawn in Pawns)
        Grid.Rows.Add(new UIDesignatorTabRow { Payload = pawn, DrawBackground = DrawCard });

    Grid.Draw(inRect);
}
```

Written for the work tab and then made general, because the shape recurs — assign, animals, wildlife and
schedule are the same figure, and every one of them wants its heading visible while the list scrolls.

The **work tab is built on this**, and is the worked example: see `UIOverhaul/Features/Work/WorkPanel.cs` for a
grid with a control in one heading, a status-striped row background, two unbanded columns at the left and a
leaning column per work type.

## What it owns, and what you own

**The control**: layout, the scroll view, the pinned heading, heading rotation and its banding, section
headings, and the order the layers are painted in.

**You**: the contents of every cell, and the two lists.

That split is the point. A control that also decided what a cell contains would only ever suit the tab it
was written for.

## The pinned heading

The heading is drawn *before* the scroll view and outside it, which is the whole mechanism: a vertical scroll
moves the rows, and it cannot move something that is not in the view. It still follows the **horizontal**
scroll, because a heading has to stay over the column it names.

`HasHeaderRow = false` skips it entirely and gives the whole rect to the rows, for a tab that draws its own
heading or wants none.

## Two different "columns"

The word does double duty here, so the API keeps them apart by name:

| Type | Is | Set via |
|---|---|---|
| `UIDesignatorTabLayoutColumn` | A **panel** of the tab — left, middle, right | `LayoutColumns` |
| `UIDesignatorTabColumn` | A **column of the grid** — one per thing being set | `Columns` |

A grid lives inside one panel. A flat tab has one panel, so the distinction only starts to matter at
`TwoColumn`.

## Layout

```csharp
Grid.Layout = UIDesignatorTabLayout.ThreeColumn;

Grid.LayoutColumns.Add(new UIDesignatorTabLayoutColumn
{
    Width = 260f, DrawPanelBackground = true, DrawContent = DrawCategories
});
Grid.LayoutColumns.Add(new UIDesignatorTabLayoutColumn());              // flexible, hosts the grid
Grid.LayoutColumns.Add(new UIDesignatorTabLayoutColumn
{
    Width = 300f, DrawPanelBackground = true, DrawContent = DrawOptions
});
```

| Value | Divides the tab into |
|---|---|
| `Flat` *(default)* | One region, undivided. The grid fills the tab |
| `TwoColumn` | Left and right, as the grow-zone tab has a list and a detail pane |
| `ThreeColumn` | Left, middle and right, as the architect tab has categories, contents and options |

`LayoutColumns` is **optional**. Leave it empty and each layout gets sensible panels: a flexible pane beside a
320px one for two columns, 220 and 280 either side of a flexible middle for three. Fill it in to set widths,
which is the usual reason to touch it — how wide a list of categories needs to be is a property of the
categories, not of this control. Entries past what the layout uses are ignored, and missing ones fall back to
the default for that slot, so a tab draws before it is fully configured.

### Width 0 means "take what is left"

That is what makes a three-column tab work: the side panels are the size their contents need, and only the
middle's width depends on the window. Several flexible panels share the remainder equally.

### Which panel gets the grid

A panel with `HostsGrid = true` wins. Otherwise it goes where the layout implies: the only panel when flat,
the **left** one of two, the **middle** of three. `DrawContent` is called after the grid, so a panel that hosts
one can still draw over it — a footer, a count, an overlay while something is dragged.

A panel that should hold something other than a grid just sets `DrawContent` and never claims the grid.

## Properties

| Property | Default | Effect |
|---|---|---|
| `Layout` | `Flat` | How the tab is divided into panels |
| `LayoutColumns` | empty | The panels, left to right. Optional; defaults fill in |
| `LayoutGap` | `8` | Space between panels |
| `Columns` | empty | Grid columns, left to right. You own the list |
| `Rows` | empty | Top to bottom, section headings in place. You own the list |
| `HasHeaderRow` | `true` | False skips the heading row |
| `HeaderLabelOrientation` | `Horizontal` | Which way the column headings run |
| `HeaderHeight` | `null` | Null derives it: 30 level, 76 turned |
| `RowHeight` | `62` | Overridable per row |
| `RowGap` | `2` | Between data rows |
| `SectionHeaderHeight` | `30` | |
| `CollapsibleSections` | `true` | Clicking a section heading folds the rows under it |
| `CollapsedSections` | empty | Which sections are folded, **by label**. Public, so you can persist or preset it |
| `SuppressCollapse` | `false` | Draws everything expanded without forgetting the folds — set it while filtering |
| `AlternatingColumnBands` | `true` | Tints every other bandable column |
| `ColumnBandAlpha` | `0.85` | How strongly a band tints the rows |
| `ScrollBarWidth` | `20` | Reserved when reporting `RequestedWidth` |
| `Scroll` | `(0,0)` | Public, so you can restore or reset it |

Read-only: `ColumnsWidth`, `RequestedWidth`, `HeaderOverhang`, `RowsHeight`, `HeaderHeightResolved`.

### Sizing a window to the grid

```csharp
public override Vector2 InitialSize =>
    new Vector2(Mathf.Min(Grid.RequestedWidth + Margin * 2f, UI.screenWidth - 16f), 700f);
```

`RequestedWidth` is the columns, the scrollbar, and `HeaderOverhang`. Add whatever your own chrome costs and cap
it at the screen; anything that still does not fit scrolls sideways.

**`HeaderOverhang`** is room for the last column's heading, and it exists because a leaning heading runs up and
to the *right* of its own column — so the last one ends outside the columns entirely, and a grid sized to its
columns alone cuts that heading's tail off at the panel edge. It is zero for `Horizontal` and `Vertical`,
neither of which travels sideways.

The scrollbar and the overhang share one allowance rather than adding up: the scrollbar is at the right of the
**rows** and the heading's tail is at the right of the **heading row**, never at the same height. Adding them
made the tab wider than its own contents by the smaller of the two. For the same reason the scrollbar's width is
only taken out of the rows when there is going to be a scrollbar — reserving it unconditionally left a strip of
nothing down the right of every row.

## Columns

| Field | Default | Effect |
|---|---|---|
| `Label` | `null` | Heading text. Null leaves the heading blank |
| `Width` | `52` | Per column, because a name column and a column holding one number have nothing in common |
| `Tooltip` | `null` | Over the whole heading |
| `DrawCell` | `null` | `(Rect cell, UIDesignatorTabRow row, UIColorPaletteDef palette)`. Null draws nothing |
| `DrawHeader` | `null` | `(Rect cell, UIColorPaletteDef palette)`. Draws the heading **instead of** `Label` |
| `Bandable` | `true` | False excludes it from the alternation **and** from the count |
| `RotateLabel` | `true` | False keeps this heading level in a grid whose others are turned |

`Bandable` and `RotateLabel` exist for the same situation: the columns at the left that are not part of the
repeating grid. A name column and a row of tool buttons should not be striped as though they were a column of
like values, and they have room for a level heading where a 44px column does not. Setting `Bandable = false`
also keeps them out of the parity count, so the alternation starts at the first real column.

## Rows

| Field | Effect |
|---|---|
| `Payload` | Whatever the row is about. The control never looks at it |
| `Height` | Overrides `RowHeight` for this row |
| `SectionLabel` | Non-empty makes this a **section heading** instead of a data row |
| `SectionSuffix` | Right-aligned text on a section heading — a count, usually |
| `DrawBackground` | `(Rect row, UIDesignatorTabRow row, UIColorPaletteDef palette)`, before any cell |

`Payload` is typed `object` so a cell drawer can be a static method instead of a closure allocated per row per
frame. Cast it in the drawer. `DrawBackground` takes the row for the same reason.

Section headings live in the same list as the data rows rather than in a list of groups each holding their
own rows. The nested version means every layout, hit test and scroll calculation walks two levels, and the
control can do nothing with the nesting that the flat list does not give it.

### Collapsing

Clicking a section heading folds every row under it, until the next section heading. The whole heading is the
hit target, not just the arrow — a 18px arrow is a fussy thing to hit, and there is nothing else on a heading to
press by mistake. The arrows are vanilla's own `TexButton.Reveal` / `TexButton.Collapse`, so the affordance is
one players already read as foldable.

`CollapsedSections` is keyed **by label**, not by row, because most callers refill `Rows` every frame from live
data — a reference to a row object would not survive to the next frame.

If you filter the rows, set `SuppressCollapse` while the filter is active. A match inside a folded group is
otherwise invisible, which reads as the filter being broken; suppressing draws everything expanded and gives the
player's folds back when you clear it.

## Layer order

Per row, in this order:

1. `Row.DrawBackground` — card chrome, status washes, selection
2. the column banding
3. `Column.DrawCell`, left to right

The banding is in the middle deliberately: over whatever the row painted, so it is visible, and under the
cells, so nothing is ever painted over a value.

## Heading orientation

`HeaderLabelOrientation` is what stops a column having to be as wide as its own name. Level, "Construction"
sets the width of a column whose contents are a 26px box, and the grid pays for the longest word in every
column across its whole height.

```csharp
public enum UIHeaderAngle
{
    Horizontal = 0,
    Vertical   = 90,
    Diagonal   = 60
}
```

| Value | Reads | Use when |
|---|---|---|
| `Horizontal` *(default)* | Left to right | The columns are wide enough for their names |
| `Vertical` | Bottom to top | Columns are as narrow as they go and headings may be long |
| `Diagonal` | Left to right, leaning | The usual choice for a narrow grid — still reads as text |

Named orientations rather than a free angle, so the three that work are the three on offer. Each value **is**
its angle in degrees, so the control translates with a cast rather than a lookup table: add a value to the enum
and the geometry follows with no other change.

Two consequences of that worth knowing:

- **Anything other than `Horizontal` takes the turned path** — rotation, sheared bands, clipping.
- **`Vertical` is a special case within it.** At 90 degrees a sheared band is an upright stripe exactly the
  width of its column, which is the cell — so vertical fills the cell like `Horizontal` does, rather than
  rotating a rect and leaving the top of the heading row unpainted for no reason.

### Rotation defeats clipping — read this before changing the header

**`GUI.BeginGroup` stops clipping once `GUI.matrix` carries a rotation.** Everything Unity clips with is built
on that group, so rotated drawing is not bounded by it: content drawn to overhang an edge really does draw past
it, over whatever the panel sits on. This is not a quirk of this control; it is how IMGUI behaves, and it is
easy to write code that looks correct and paints over the game.

Two consequences shape the header:

- **Bands are built from horizontal strips**, not drawn as one rotated rectangle. The bottom strip is the
  column — same left edge, same width — and every strip above it is that column shifted right by its own rise.
  Strips are axis-aligned, so their bounds are arithmetic and the band ends exactly where the heading row does.
  That is also what makes a band's width match its column's at every height, so the heading's stripe and the
  row's stripe read as one lane. Mixed column widths hold too.
- **Labels are bounded, not clipped.** `LabelLength` keeps a rotated label inside the heading row by geometry,
  and text longer than that is cut by its own rect — which Unity does honor, being a text-layout limit rather
  than a clip.

A band is still *sheared* rather than upright, for the original reason: an upright stripe is crossed by three
or four headings on the way up and links its own to nothing.

Tooltips are registered on the upright cell, never inside the rotation: a tooltip region is taken in screen
space, and one registered under a rotated matrix sits where the pointer is not. If you add anything
interactive to a heading, do the same.

`HeaderHeight` bounds how long a heading can be, since the label is clipped at the edge of the heading row.
`Vertical` fits slightly less text than `Diagonal` at the same height — 70px against 80px at the derived 76 —
because a leaning heading travels further across the row than a straight one does up it. Raise `HeaderHeight`
if your names need it.
