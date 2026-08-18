# Typefaces for the floor labels

Two fonts from Google Fonts, both under the SIL Open Font License 1.1, plus the tool that turns them into
something Unity can actually draw.

| Folder | Face | Shipped weight | License |
|---|---|---|---|
| `Oswald/` | Oswald | Bold | OFL 1.1, no reserved font name declared |
| `HammersmithOne/` | Hammersmith One | Regular | OFL 1.1, Reserved Font Name "Hammersmith" |

Each folder keeps the upstream `OFL.txt` unchanged. Neither font is modified, so the Reserved Font Name clause
does not restrict us -- it only bites if you alter a font and keep its name. The OFL permits bundling and
redistribution, which is why these can ship where an installed-only system font could not.

Only the weight we actually use is kept. Oswald's download has six statics plus a variable font; carrying the
five we do not draw would be dead weight in every subscriber's download.

## Why a baked atlas rather than the font file

**Unity cannot load a font from a file or a byte array at runtime.** `UnityEngine.Font`'s entire construction
surface is `Font()`, `Font(string name)` and `CreateDynamicFontFromOSFont` -- and all three want the typeface
installed on the player's machine, which is no way to ship a consistent look.

The two ways out are an AssetBundle or a baked atlas.

An AssetBundle has to be built with the Unity editor at RimWorld's *exact* version (2022.3.35f1 as of writing);
a bundle from any other version will not load. That is a multi-gigabyte dependency and a build step outside this
repo.

Baking needs neither, and it happens to suit this feature: the label renderer already builds its own meshes, so
it only ever wanted glyph rectangles and advances. That is exactly what the metrics file holds. `FloorLabelAtlas`
reads the output; `FloorLabelFont` picks between the atlases and RimWorld's own font.

## Regenerating

The baker is `Baker/BakeAtlas.cs`. It is a standalone console program, not part of the mod assembly -- it
references `System.Drawing`, which RimWorld does not ship and must never be referenced from mod code.

```
csc /optimize+ /langversion:7.3 /r:System.Drawing.dll /out:bakeatlas.exe Baker/BakeAtlas.cs

bakeatlas.exe Oswald/Oswald-Bold.ttf                      ../../../../Gideon.UIOverhaul/Fonts OswaldBold
bakeatlas.exe HammersmithOne/HammersmithOne-Regular.ttf   ../../../../Gideon.UIOverhaul/Fonts HammersmithOne
```

It loads the TTF with `PrivateFontCollection.AddFontFile`, so nothing is installed on the machine doing the
baking.

Output per face, into the mod's `Fonts/` folder:

- `<name>.png` -- **white RGB, glyph coverage in alpha.** The white matters: a shader that multiplies the texture
  by a tint then yields the tint itself. Unity's own dynamic atlases are black-with-alpha, and that is precisely
  what made an early version of this feature render every label solid black whatever color it was given.
- `<name>.txt` -- one `atlas` header line, then a `g` line per glyph. Tab separated, invariant culture, because a
  shipped data file must not reparse differently on a machine that writes decimals with a comma.

Metrics are **pixels with y up from the baseline**, converted once during baking. GDI+ measures y down from the
em box top; doing that conversion at draw time is how text ends up sitting a few pixels off with nobody able to
say why.

## If you add a third face

1. Drop the TTF and its license in a folder here.
2. Bake it with the command above.
3. Add a member to `FloorLabelFace`, a case to `FloorLabelFont.For`, and a display name to `Named` in
   `Dialog_UIOptions`. The options list and its previews are generated from the enum, so nothing else needs
   touching.

Labels are drawn upper cased, so the baked lowercase range is currently unused. It is baked anyway: it costs
little and stops the atlas being wasted if that decision ever changes. A character outside the baked set makes
that one label fall back to RimWorld's own font rather than showing blanks.
