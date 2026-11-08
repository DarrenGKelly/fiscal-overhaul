# Mod icon

The source of the mod's icon: `Mod/Properties/Thumbnail.png`, which `PublishConfiguration.xml` uploads to Paradox
Mods as the mod's cover, and which the in-game mod list shows small.

## Regenerate

```
python -m pip install --user Pillow     # once; the script needs nothing else
python art/icon/make_icon.py            # writes Mod/Properties/Thumbnail.png and concepts/*.png
python art/icon/make_icon.py --preview  # also writes 64 and 259 px copies to preview/ (gitignored)
```

Shapes are drawn from geometry in `make_icon.py`, supersampled 3x and downscaled; the lettering uses the font in
`fonts/` (no other external art), so any machine renders the same image. Change the drawing there, rerun, and look at
the 1024 px image and both previews before committing: 64 px is the in-game mod list's icon, 259 px a Paradox Mods
card.

## Font and currency sign

`fonts/Overpass-Bold.ttf` is Overpass Bold (Red Hat, SIL Open Font License 1.1, `fonts/OFL.txt`), the font the
game's UI is set in, copied from the game's own `Cities2_Data/Content/Game/UI/Fonts/`. The OFL allows bundling and
redistribution as long as the licence travels with the font, which `OFL.txt` does.

The coin carries the game's money sign `¢`: the game writes amounts as `¢1,234` in Overpass (its `VALUE_MONEY`
format; the mod's own chart labels copy it), so the coin draws that same glyph from the same font. The game's
`Media/Glyphs/Money.svg` icon is something else, an isometric banknote, and is not used.

## Size: 1024x1024, square

Paradox Mods shows a mod's cover square. Measured on the Cities: Skylines II listing (2026-11): each card's thumbnail
is a square `background-image` with `background-size: cover`, centred, served at 600x600
(`?class=modsui_browse_thumbnail`); the mod page shows it square too (`object-fit: cover`). The covers uploaded by
popular mods are square: 1024x1024, 1080x1080 and 1254x1254. A non-square image (the toolchain's 950x500 placeholder)
would lose its sides. So the icon is 1024x1024, full-bleed (no transparent corners), with everything that matters
inside the central ~90% (the wordmark's `OVERHAUL` spans 88% of the width, as wide as it can go and stay legible at
64 px).

## Designs

- **Primary** (`Thumbnail.png`, `wordmark()`): `FISCAL` (white) along the top and `OVERHAUL` (gold) along the
  bottom, around a squat chart: a rising income line (green) over a flatter expense line (red), each ending in an
  arrow head, the Budget tab's colours, on a gridded dark slate-blue panel with the mod's y-axis labels (`¢0` to `¢3M`). The
  chart is shown hovered as in the mod: a white hover line runs from the zero axis up through `FISCAL`'s C, which is
  gold and whose bar the line becomes, making the money sign `¢`; the hover dot sits on the income line with a
  mouse pointer on it. Both words stay legible at 64 px.
  Popular CS2 mods mostly name themselves on the cover: 28 of the 30 most popular on Paradox Mods (2026-11) do.
- **Concepts** (`concepts/`), alternatives to pick from:
  - `chart-coin.png`: the primary without the words, the chart taller and the `¢` coin larger.
  - `density-bars.png`: three buildings of rising density (low, medium, high) under a rising arrow line.
  - `coin-stacks.png`: three growing stacks of coins under a rising arrow line.

To make a concept the primary, swap which function `main()` writes to `PRIMARY_OUT`.
