"""Render the Fiscal Overhaul mod icon (Paradox Mods thumbnail) and its alternative concepts.

    python art/icon/make_icon.py            # writes Mod/Properties/Thumbnail.png and art/icon/concepts/*.png
    python art/icon/make_icon.py --preview  # also writes 64 px previews to art/icon/preview/ (gitignored)

Needs only Pillow (`python -m pip install --user Pillow`). Everything is drawn from geometry, no fonts and no
external art, so the output is the same on any machine. See README.md beside this file.
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

SIZE = 1024  # Paradox Mods shows covers square (background-size: cover); see README.md
SS = 3  # supersampling factor: draw at SIZE*SS, then downscale for smooth edges
W = SIZE * SS

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
PRIMARY_OUT = REPO / "Mod" / "Properties" / "Thumbnail.png"
CONCEPTS_DIR = HERE / "concepts"
PREVIEW_DIR = HERE / "preview"

# Palette: the Budget tab's chart colours (UI/src/chart/chart-math.ts) and the game's dark slate-blue panels.
BG_TOP = (34, 48, 74)
BG_BOTTOM = (12, 18, 30)
GRID = (255, 255, 255, 22)
INCOME = (113, 241, 168)
EXPENSE = (236, 72, 112)
BALANCE = (132, 202, 255)
ACCENT = (52, 150, 230)
GOLD = (246, 196, 82)
GOLD_DARK = (176, 122, 30)
GOLD_LIGHT = (255, 228, 140)
INK = (24, 33, 51)


def s(v: float) -> int:
    """A coordinate in 1024-space, scaled to the supersampled canvas."""
    return round(v * SS)


def pts(points: list[tuple[float, float]]) -> list[tuple[int, int]]:
    return [(s(x), s(y)) for x, y in points]


def vertical_gradient(top: tuple, bottom: tuple, size: tuple[int, int] = (W, W)) -> Image.Image:
    mask = Image.linear_gradient("L").resize(size)
    return Image.composite(Image.new("RGBA", size, bottom + (255,)), Image.new("RGBA", size, top + (255,)), mask)


def background() -> Image.Image:
    img = vertical_gradient(BG_TOP, BG_BOTTOM)
    # a soft cyan glow behind the chart, upper right
    glow = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse([s(380), s(80), s(1100), s(760)], fill=ACCENT + (70,))
    glow = glow.filter(ImageFilter.GaussianBlur(s(140)))
    return Image.alpha_composite(img, glow)


def grid(img: Image.Image, left: float, top: float, right: float, bottom: float, rows: int, cols: int) -> None:
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for i in range(rows + 1):
        y = top + (bottom - top) * i / rows
        d.line([(s(left), s(y)), (s(right), s(y))], fill=GRID, width=s(3))
    for j in range(cols + 1):
        x = left + (right - left) * j / cols
        d.line([(s(x), s(top)), (s(x), s(bottom))], fill=GRID, width=s(3))
    # the zero axis, brighter
    d.line([(s(left), s(bottom)), (s(right), s(bottom))], fill=(255, 255, 255, 90), width=s(5))
    img.alpha_composite(layer)


def area(img: Image.Image, line: list[tuple[float, float]], base_y: float, colour: tuple, alpha_top: int) -> None:
    """Fill under a line with a gradient that fades to nothing at base_y."""
    poly = line + [(line[-1][0], base_y), (line[0][0], base_y)]
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).polygon(pts(poly), fill=255)
    top = min(y for _, y in line)
    fade = Image.new("L", img.size, 0)
    ramp = Image.linear_gradient("L").transpose(Image.Transpose.FLIP_TOP_BOTTOM)
    ramp = ramp.resize((W, s(base_y - top))).point(lambda v: v * alpha_top // 255)
    fade.paste(ramp, (0, s(top)))
    alpha = ImageChops.multiply(mask, fade)
    fill = Image.new("RGBA", img.size, colour + (0,))
    fill.putalpha(alpha)
    img.alpha_composite(fill)


def polyline(img: Image.Image, line: list[tuple[float, float]], colour: tuple, width: float,
             glow: float = 0, dots: float = 0) -> None:
    if glow:
        layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
        ImageDraw.Draw(layer).line(pts(line), fill=colour + (150,), width=s(width * 2.2), joint="curve")
        img.alpha_composite(layer.filter(ImageFilter.GaussianBlur(s(glow))))
    d = ImageDraw.Draw(img)
    d.line(pts(line), fill=colour + (255,), width=s(width), joint="curve")
    r = width / 2
    for x, y in (line[0], line[-1]):  # round caps
        d.ellipse([s(x - r), s(y - r), s(x + r), s(y + r)], fill=colour + (255,))
    if dots:
        for x, y in line[1:-1]:
            d.ellipse([s(x - dots), s(y - dots), s(x + dots), s(y + dots)], fill=colour + (255,))
            ri = dots * 0.45
            d.ellipse([s(x - ri), s(y - ri), s(x + ri), s(y + ri)], fill=INK + (255,))


def arrow(img: Image.Image, a: tuple[float, float], b: tuple[float, float], length: float, half_width: float,
          colour: tuple) -> None:
    """An arrow head on the end b of the segment a-b, its base centred on b."""
    dx, dy = b[0] - a[0], b[1] - a[1]
    n = (dx * dx + dy * dy) ** 0.5
    ux, uy = dx / n, dy / n
    px, py = -uy, ux
    tip = (b[0] + ux * length, b[1] + uy * length)
    head = [tip, (b[0] + px * half_width, b[1] + py * half_width), (b[0] - px * half_width, b[1] - py * half_width)]
    ImageDraw.Draw(img).polygon(pts(head), fill=colour + (255,))


def shadow(img: Image.Image, box: tuple[float, float, float, float], blur: float, offset: float, alpha: int) -> None:
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    x0, y0, x1, y1 = box
    ImageDraw.Draw(layer).ellipse([s(x0), s(y0 + offset), s(x1), s(y1 + offset)], fill=(0, 0, 0, alpha))
    img.alpha_composite(layer.filter(ImageFilter.GaussianBlur(s(blur))))


def percent(d: ImageDraw.ImageDraw, cx: float, cy: float, size: float, colour: tuple) -> None:
    """A % sign from two rings and a bar, centred on (cx, cy); size is its height."""
    ring_r = size * 0.2
    ring_w = size * 0.085
    for dx, dy in ((-0.26, -0.27), (0.26, 0.27)):
        x, y = cx + dx * size, cy + dy * size
        d.ellipse([s(x - ring_r), s(y - ring_r), s(x + ring_r), s(y + ring_r)], outline=colour, width=s(ring_w))
    bar = [(cx + 0.30 * size, cy - 0.46 * size), (cx - 0.30 * size, cy + 0.46 * size)]
    d.line(pts(bar), fill=colour, width=s(size * 0.11))
    r = size * 0.055
    for x, y in bar:
        d.ellipse([s(x - r), s(y - r), s(x + r), s(y + r)], fill=colour)


def coin(img: Image.Image, cx: float, cy: float, r: float, symbol: str = "%") -> None:
    shadow(img, (cx - r, cy - r, cx + r, cy + r), blur=r * 0.18, offset=r * 0.10, alpha=150)
    d = ImageDraw.Draw(img)
    # edge (the coin's thickness), then face, then an inner ring
    d.ellipse([s(cx - r), s(cy - r + r * 0.06), s(cx + r), s(cy + r + r * 0.06)], fill=GOLD_DARK + (255,))
    d.ellipse([s(cx - r), s(cy - r), s(cx + r), s(cy + r)], fill=GOLD + (255,))
    # a light upper-left highlight
    hl = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(hl).ellipse([s(cx - r * 0.95), s(cy - r * 0.95), s(cx + r * 0.35), s(cy + r * 0.35)],
                               fill=GOLD_LIGHT + (120,))
    hl = hl.filter(ImageFilter.GaussianBlur(s(r * 0.18)))
    face = Image.new("L", img.size, 0)
    ImageDraw.Draw(face).ellipse([s(cx - r), s(cy - r), s(cx + r), s(cy + r)], fill=255)
    hl.putalpha(ImageChops.multiply(hl.getchannel("A"), face))
    img.alpha_composite(hl)
    d = ImageDraw.Draw(img)
    ri = r * 0.80
    d.ellipse([s(cx - ri), s(cy - ri), s(cx + ri), s(cy + ri)], outline=GOLD_DARK + (255,), width=s(r * 0.06))
    if symbol == "%":
        percent(d, cx, cy, r * 0.95, GOLD_DARK + (255,))


def finish(img: Image.Image) -> Image.Image:
    return img.resize((SIZE, SIZE), Image.Resampling.LANCZOS).convert("RGB")


# --- Primary: a rising income line over a falling-behind expense line, with a tax coin ---------------------------

def primary() -> Image.Image:
    img = background()
    left, top, right, bottom = 120, 300, 904, 860
    grid(img, left, top, right, bottom, rows=4, cols=5)
    income = [(120, 740), (277, 655), (434, 680), (591, 530), (748, 445), (904, 320)]
    expense = [(120, 815), (277, 795), (434, 775), (591, 750), (748, 728), (904, 690)]
    area(img, income, bottom, INCOME, 110)
    polyline(img, expense, EXPENSE, width=30, glow=18)
    polyline(img, income, INCOME, width=46, glow=26, dots=34)
    coin(img, 300, 290, 190)
    return finish(img)


# --- Concept B: density bars (low/medium/high buildings) under a rising line -----------------------------------

def concept_density() -> Image.Image:
    img = background()
    left, bottom, right = 130, 860, 894
    grid(img, left, 200, right, bottom, rows=4, cols=3)
    bars = [(170, 640, BALANCE), (400, 470, ACCENT), (630, 300, (90, 220, 220))]
    d = ImageDraw.Draw(img)
    win = INK + (200,)
    for x, y, colour in bars:
        x1 = x + 200
        d.rounded_rectangle([s(x), s(y), s(x1), s(bottom)], radius=s(18), fill=colour + (255,))
        # window rows: more floors for denser buildings
        for wy in range(int(y) + 50, bottom - 40, 80):
            for wx in (x + 40, x + 120):
                d.rounded_rectangle([s(wx), s(wy), s(wx + 40), s(wy + 36)], radius=s(6), fill=win)
    line = [(150, 610), (270, 560), (380, 590), (500, 400), (620, 330), (740, 250), (874, 150)]
    polyline(img, line, INCOME, width=44, glow=26, dots=0)
    arrow(img, line[-2], line[-1], length=110, half_width=72, colour=INCOME)
    return finish(img)


# --- Concept C: stacks of coins growing under a rising line ---------------------------------------------------

GOLD_MID = (214, 156, 48)


def coin_stack(img: Image.Image, x: float, base: float, count: int, w: float = 210, t: float = 46,
               e: float = 70) -> float:
    """A stack of count coins seen from slightly above, bottom at base; returns the top face's centre y."""
    shadow(img, (x - 10, base - e / 2, x + w + 10, base + e / 2), blur=18, offset=8, alpha=140)
    d = ImageDraw.Draw(img)
    y = base
    for k in range(count):
        top = y - t - e / 2  # top face's upper edge
        side = GOLD_DARK if k % 2 == 0 else GOLD_MID
        d.ellipse([s(x), s(y - e / 2), s(x + w), s(y + e / 2)], fill=side + (255,))
        d.rectangle([s(x), s(top + e / 2), s(x + w), s(y)], fill=side + (255,))
        d.ellipse([s(x), s(top), s(x + w), s(top + e)], fill=GOLD + (255,))
        y -= t
    # the top coin's rim ring
    top = y - e / 2
    inset = w * 0.12
    d.ellipse([s(x + inset), s(top + e * 0.14), s(x + w - inset), s(top + e * 0.86)], outline=GOLD_DARK + (255,),
              width=s(6))
    return y


def concept_coin() -> Image.Image:
    img = background()
    left, bottom, right = 110, 880, 914
    grid(img, left, 200, right, bottom, rows=4, cols=3)
    base = 850
    tops = [coin_stack(img, x, base, n) for x, n in ((140, 4), (407, 8), (674, 12))]
    line = [(110, 560), (245, tops[0] - 140), (512, tops[1] - 150), (779, tops[2] - 150)]  # clears each stack
    polyline(img, line, INCOME, width=44, glow=26, dots=0)
    arrow(img, line[-2], line[-1], length=110, half_width=72, colour=INCOME)
    return finish(img)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--preview", action="store_true", help="also write 64 px previews to art/icon/preview/")
    args = parser.parse_args()

    outputs = {
        PRIMARY_OUT: primary,
        CONCEPTS_DIR / "density-bars.png": concept_density,
        CONCEPTS_DIR / "coin-stacks.png": concept_coin,
    }
    CONCEPTS_DIR.mkdir(exist_ok=True)
    for path, render in outputs.items():
        img = render()
        img.save(path, optimize=True)
        print(f"wrote {path.relative_to(REPO)} ({img.width}x{img.height})")
        if args.preview:
            PREVIEW_DIR.mkdir(exist_ok=True)
            small = img.resize((64, 64), Image.Resampling.LANCZOS)
            small.save(PREVIEW_DIR / f"{path.stem}-64.png")


if __name__ == "__main__":
    main()
