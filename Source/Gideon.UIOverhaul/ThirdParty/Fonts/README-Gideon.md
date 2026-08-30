# Typefaces

**Interface text no longer uses baked atlases -- it ships as a Unity AssetBundle.** The headless project in
`BundleProject/` bakes every TTF into `Gideon.UIOverhaul/AssetBundles/gideonfonts` as dynamic fonts, which the
engine renders itself: FreeType hinting at every size, bold and italic from tags, per-glyph fallback for
characters a face lacks. That file must stay extensionless; RimWorld's bundle loader only accepts files with no
extension. To rebake after adding a TTF to `BundleProject/Assets/Fonts/`:

```
"C:\Program Files\Unity\Hub\Editor\2022.3.35f1\Editor\Unity.exe" -batchmode -nographics -projectPath BundleProject -executeMethod BundleBuilder.Build -logFile bake.log
```

The editor version must be exactly the game's own (2022.3.35f1 for RimWorld 1.6; read it from
`globalgamemanagers` after a game update). Three other roads were tried and are closed: drawing glyphs
ourselves from baked sheets reimplemented a font engine and showed it; `Font(path)` routes to a native call
that is a stub in the shipped player; registering a TTF with the OS is invisible to an engine whose font list
is sealed before mod code runs.

**Everything below concerns the baker and the baked atlases, which now serve only the floor labels and the
research scripts.**

Nine families, all under the SIL Open Font License 1.1, plus the tool that turns them into something Unity can
actually draw. Two are for the floor labels; three are the scripts the research tab writes undiscovered Anomaly
projects in; one is a display face kept as a candidate; the rest are interface text.

**Only the atlases ship.** No TTF is in the mod folder and none is modified, so what a subscriber downloads is a
rasterization rather than a redistribution. The attributions live in `Gideon.UIOverhaul/THIRD-PARTY-NOTICES.txt`
with the license reproduced once, since it is byte for byte identical in all nine.

**The interface faces are the reason the baker grew.** A floor label or a research mask is baked over a fixed
set of code points -- printable ASCII and Latin-1, or one script block. Interface text is baked over everything
the face covers, and at 32 rather than 64. Asked for on 2026-08-25: "I really dislike the stock RimWorld font."

| Folder | Face | Shipped weight | Used for | License |
|---|---|---|---|---|
| `Oswald/` | Oswald | Bold | floor labels | OFL 1.1, no reserved font name declared |
| `HammersmithOne/` | Hammersmith One | Regular | floor labels | OFL 1.1, Reserved Font Name "Hammersmith" |
| `NotoSansImperialAramaic/` | Noto Sans Imperial Aramaic | Regular | research mask | OFL 1.1 |
| `NotoSansMendeKikakui/` | Noto Sans Mende Kikakui | Regular | research mask | OFL 1.1 |
| `NotoSansSiddham/` | Noto Sans Siddham | Regular | research mask | OFL 1.1 |
| `SlacksideOne/` | Slackside One | Regular | nothing yet | OFL 1.1 |
| `BarlowCondensed/` | Barlow Condensed | Regular, SemiBold, Bold, Italic, SemiBold Italic, Thin, Thin Italic | interface | OFL 1.1 |
| `CascadiaMono/` | Cascadia Mono | Regular, from the variable font's default instance | interface, monospaced | OFL 1.1 |
| `IBMPlexMono/` | IBM Plex Mono | Regular, SemiBold | interface, monospaced | OFL 1.1, Reserved Font Name "Plex" |

Oswald and Hammersmith One appear twice in `Fonts/`: once at 64 for the floor labels, and once more at 32 under
`OswaldRegular` and `HammersmithOneRegular` for interface text. Same typeface, different bake, different job --
and they must stay separate, because re-baking the floor sheets at 32 would soften labels drawn across a room.

**A variable font bakes as its default instance and nothing else.** GDI+ has no axis control, so
`Oswald-VariableFont_wght.ttf` yields Regular and the weight axis is unreachable. To ship a second weight of one,
add that weight's static TTF; the variable file cannot stand in for it.

**Slackside One has no script block.** It is a Latin display face, so it cannot mask anything, and it is kept
here as a candidate third floor label face rather than as a research option.

Each folder keeps the upstream `OFL.txt` unchanged. No font is modified, so the Reserved Font Name clause
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

The three script faces take a fourth argument: the code point ranges to bake, in hex. Without it the baker emits
printable ASCII and Latin-1, which is right for the two Latin faces above and useless for these.

```
bakeatlas.exe NotoSansImperialAramaic/NotoSansImperialAramaic-Regular.ttf ../../../../Gideon.UIOverhaul/Fonts NotoSansImperialAramaic 10840-10855
bakeatlas.exe NotoSansMendeKikakui/NotoSansMendeKikakui-Regular.ttf       ../../../../Gideon.UIOverhaul/Fonts NotoSansMendeKikakui    1E800-1E8C4
bakeatlas.exe NotoSansSiddham/NotoSansSiddham-Regular.ttf                 ../../../../Gideon.UIOverhaul/Fonts NotoSansSiddham         11580-115AE
```

Those ranges are the letters of each script and nothing else: 22 Imperial Aramaic letterforms, 197 Mende Kikakui
syllables, 47 Siddham letters. Each block continues past them into digits, vowel signs and combining marks, all
deliberately left out -- a non-spacing mark drawn on its own is a speck rather than a character, and a run of
masking glyphs has to read as writing.

The interface face takes `all` instead of a range, which bakes every code point the font's cmap covers, and a
fifth argument setting the size it is rasterized at. One command per weight, because a baked sheet holds one
weight at one slant and nothing downstream can embolden or slant a bitmap:

```
bakeatlas.exe BarlowCondensed/BarlowCondensed-Regular.ttf  ../../../../Gideon.UIOverhaul/Fonts BarlowCondensedRegular  all 32
bakeatlas.exe BarlowCondensed/BarlowCondensed-SemiBold.ttf ../../../../Gideon.UIOverhaul/Fonts BarlowCondensedSemiBold all 32
bakeatlas.exe BarlowCondensed/BarlowCondensed-Bold.ttf     ../../../../Gideon.UIOverhaul/Fonts BarlowCondensedBold     all 32
bakeatlas.exe BarlowCondensed/BarlowCondensed-Italic.ttf   ../../../../Gideon.UIOverhaul/Fonts BarlowCondensedItalic   all 32
```

**32 rather than the default 64, and the size matters more than it sounds.** The atlas is sampled bilinearly
with mipmaps off, and bilinear reads four texels however far it is reducing. A 64 px master drawn at the 18 px
of `GameFont.Small` has a footprint of about twelve texels, so two thirds of the coverage is thrown away -- and
which two thirds depends on where the glyph happens to land. On a condensed face that reads as stems varying in
weight from letter to letter. At 32 the reduction is 1.7x instead of 3.5x. It also fits 523 glyphs in a 1024
square sheet rather than a 2048 one, which is most of a megabyte off the download across four weights.

Floor labels and research masks stay at the default 64: they are drawn large, in world space or in a grid cell,
where the reduction never happens. Each metrics file records the size it was baked at and every reader scales
from that, so sheets of different sizes mix without anything needing to know.

`all` gets 523 glyphs from U+000D to U+FB02: ASCII, Latin-1, 141 of Latin Extended, and the f-ligatures. **No
Cyrillic and no CJK,** which the face simply does not draw. A label containing one falls back whole to RimWorld's
font rather than showing blanks, so a Russian or Chinese player sees the game's text and nothing broken.

**All three script faces also carry a full Latin set,** which is the trap: setting text in Noto Sans Imperial
Aramaic renders ordinary readable Latin. The mask draws from the script's own code points, never from the text
it stands in for.

**The baker walks code points, not `char`s.** All three scripts live above U+FFFF, where a `char` cannot reach;
it walked a `List<char>` until 2026-08-23 and could not have baked any of them.

It loads the TTF with `PrivateFontCollection.AddFontFile`, so nothing is installed on the machine doing the
baking. A code point the face does not cover has no ink and is dropped rather than baked as a blank cell.

Output per face, into the mod's `Fonts/` folder:

- `<name>.png` -- **white RGB, glyph coverage in alpha.** The white matters: a shader that multiplies the texture
  by a tint then yields the tint itself. Unity's own dynamic atlases are black-with-alpha, and that is precisely
  what made an early version of this feature render every label solid black whatever color it was given.
- `<name>.txt` -- one `atlas` header line, then a `g` line per glyph. Tab separated, invariant culture, because a
  shipped data file must not reparse differently on a machine that writes decimals with a comma.

Metrics are **pixels with y up from the baseline**, converted once during baking. GDI+ measures y down from the
em box top; doing that conversion at draw time is how text ends up sitting a few pixels off with nobody able to
say why.

## If you add another floor label face

1. Drop the TTF and its license in a folder here.
2. Bake it with the command above.
3. Add a member to `FloorLabelFace`, a case to `FloorLabelFont.For`, and a display name to `Named` in
   `Dialog_UIOptions`. The options list and its previews are generated from the enum, so nothing else needs
   touching.

## If you add another interface face

Interface text is drawn by `UITextControl`, and a control names the face it wants with a `UIFace` value. Adding a
face is meant to be additive: nothing that already draws text has to change.

1. Drop the TTF and its license in a folder here.
2. Bake it with `all`, one command per weight you intend to ship.
3. Add a member to `UIFace`, an atlas field and an `AtlasFor` case in `UIFaces`, and a display name in
   `UIFaces.Named`.

**One member per sheet, not per family.** `UIFace.BarlowCondensedBold` is a face in its own right, because a
bitmap cannot be emboldened after baking. Ship only the weights something will actually ask for -- each is a
quarter of a megabyte of PNG in every subscriber's download.

A face whose sheet is missing is not an error and is not hidden: `UIFaces.Available` reports it, and any label
asking for it is drawn in RimWorld's own font. That is also what happens to a label using a character the sheet
was not baked over, which is what makes an incomplete face safe to ship.

## If you add another masking script

1. Drop the TTF and its license in a folder here, and check what it actually covers -- a Noto script face ships
   Latin as well, and the Latin is not what you want.
2. Bake it with the code point range of the script's letters.
3. Add a member to `ResearchScript` and a case to `ResearchScripts.Named` and `ResearchScripts.AtlasFor`. The
   picker is generated from the enum and draws each option in its own characters, so nothing else needs
   touching.

A script whose atlas is missing is left out of the picker and logged once as an error, because that is a
packaging fault rather than a choice. `ResearchScriptAtlas` reads the files; `ResearchMask` decides what is
masked and holds the runs.

Labels are drawn upper cased, so the baked lowercase range is currently unused. It is baked anyway: it costs
little and stops the atlas being wasted if that decision ever changes. A character outside the baked set makes
that one label fall back to RimWorld's own font rather than showing blanks.
