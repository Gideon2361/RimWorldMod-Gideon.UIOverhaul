# Typefaces

Seven faces from Google Fonts, all under the SIL Open Font License 1.1, plus the tool that turns them into
something Unity can actually draw. Two are for the floor labels; three are the scripts the research tab writes
undiscovered Anomaly projects in; one is a display face kept as a candidate; one is the interface face.

**Barlow Condensed is the odd one out and the reason the baker grew.** The other six are baked over a fixed set
of code points -- printable ASCII and Latin-1 for the Latin faces, one script block each for the masks. This one
is interface text, so it is baked over everything the face actually covers, in four weights. Asked for on
2026-08-25: "I really dislike the stock RimWorld font."

| Folder | Face | Shipped weight | Used for | License |
|---|---|---|---|---|
| `Oswald/` | Oswald | Bold | floor labels | OFL 1.1, no reserved font name declared |
| `HammersmithOne/` | Hammersmith One | Regular | floor labels | OFL 1.1, Reserved Font Name "Hammersmith" |
| `NotoSansImperialAramaic/` | Noto Sans Imperial Aramaic | Regular | research mask | OFL 1.1 |
| `NotoSansMendeKikakui/` | Noto Sans Mende Kikakui | Regular | research mask | OFL 1.1 |
| `NotoSansSiddham/` | Noto Sans Siddham | Regular | research mask | OFL 1.1 |
| `SlacksideOne/` | Slackside One | Regular | nothing yet | OFL 1.1 |
| `BarlowCondensed/` | Barlow Condensed | Regular, SemiBold, Bold, Italic | trade window | OFL 1.1 |

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
