# Mod icon

The source of the mod's icon: `Mod/Properties/Thumbnail.png`, which `PublishConfiguration.xml` uploads to Paradox
Mods as the mod's cover, and which the in-game mod list shows small.

## Regenerate

```
python -m pip install --user Pillow     # once; the script needs nothing else
python art/icon/make_icon.py            # writes Mod/Properties/Thumbnail.png and concepts/*.png
python art/icon/make_icon.py --preview  # also writes 64 px copies to preview/ (gitignored) to judge small sizes
```

Everything is drawn from geometry in `make_icon.py` (no fonts, no external art), supersampled 3x and downscaled, so
any machine renders the same image. Change the drawing there, rerun, and look at both the 1024 px image and the 64 px
preview before committing.

## Size: 1024x1024, square

Paradox Mods shows a mod's cover square. Measured on the Cities: Skylines II listing (2026-11): each card's thumbnail
is a square `background-image` with `background-size: cover`, centred, served at 600x600
(`?class=modsui_browse_thumbnail`); the mod page shows it square too (`object-fit: cover`). The covers uploaded by
popular mods are square: 1024x1024, 1080x1080 and 1254x1254. A non-square image (the toolchain's 950x500 placeholder)
would lose its sides. So the icon is 1024x1024, full-bleed (no transparent corners), with everything that matters
inside the central ~80%.

## Designs

- **Primary** (`Thumbnail.png`): a rising income line (green) over a flatter expense line (red), the Budget tab's
  colours, on a gridded dark slate-blue panel, with a gold tax coin marked `%`.
- **Concepts** (`concepts/`), alternatives to pick from:
  - `density-bars.png`: three buildings of rising density (low, medium, high) under a rising arrow line.
  - `coin-stacks.png`: three growing stacks of coins under a rising arrow line.

To make a concept the primary, swap which function `main()` writes to `PRIMARY_OUT`.
