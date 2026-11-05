"""Render the Fiscal Overhaul mod icon (Paradox Mods thumbnail) and its alternative concepts.

    python art/icon/make_icon.py            # writes Mod/Properties/Thumbnail.png and art/icon/concepts/*.png
    python art/icon/make_icon.py --preview  # also writes 64 and 259 px previews to art/icon/preview/ (gitignored)

Needs only Pillow (`python -m pip install --user Pillow`). Shapes are drawn from geometry; the lettering (the
wordmark and the coin's currency sign) uses the game's UI font, Overpass Bold, committed under fonts/ with its OFL
licence. No external art, so the output is the same on any machine. See README.md beside this file.
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

SIZE = 1024  # Paradox Mods shows covers square (background-size: cover); see README.md
SS = 3  # supersampling factor: draw at SIZE*SS, then downscale for smooth edges
W = SIZE * SS

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
PRIMARY_OUT = REPO / "Mod" / "Properties" / "Thumbnail.png"
CONCEPTS_DIR = HERE / "concepts"
PREVIEW_DIR = HERE / "preview"
FONT = HERE / "fonts" / "Overpass-Bold.ttf"  # the game's UI font (SIL OFL 1.1, fonts/OFL.txt)
CAP = 0.70  # Overpass's cap height, as a fraction of the font size
CENT = "\u00a2"  # the game's money sign: it formats amounts as "¢1,234" in Overpass

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
WHITE = (244, 247, 252)


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


def font(cap: float) -> ImageFont.FreeTypeFont:
    """Overpass Bold sized so its capitals are cap tall (1024-space), on the supersampled canvas."""
    return ImageFont.truetype(str(FONT), s(cap / CAP))


def cent(d: ImageDraw.ImageDraw, cx: float, cy: float, size: float, colour: tuple) -> None:
    """The game's money sign, the font's own glyph, its ink centred on (cx, cy); size is its ink height."""
    f = font(size * 0.7)  # the glyph's bar runs past cap height: probe the ink box to fit size exactly
    x0, y0, x1, y1 = f.getbbox(CENT, anchor="ls")
    f = font(size * 0.7 * s(size) / (y1 - y0))
    x0, y0, x1, y1 = f.getbbox(CENT, anchor="ls")
    d.text((s(cx) - (x0 + x1) / 2, s(cy) - (y0 + y1) / 2), CENT, font=f, fill=colour, anchor="ls")


def word(img: Image.Image, text: str, cx: float, baseline: float, cap: float, colour: tuple,
         tracking: float = 0, outline: float = 0) -> None:
    """A word in capitals centred on cx, sitting on baseline, letter-spaced by tracking (1024-space px),
    with a soft dark shadow under it so it reads on the gradient at any size."""
    f = font(cap)
    widths = [f.getlength(ch) for ch in text]
    total = sum(widths) + s(tracking) * (len(text) - 1)
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    x = s(cx) - total / 2
    for ch, w in zip(text, widths):
        d.text((x, s(baseline)), ch, font=f, fill=colour + (255,), anchor="ls",
               stroke_width=s(outline), stroke_fill=INK + (255,))
        x += w + s(tracking)
    sh = Image.new("RGBA", img.size, (0, 0, 0, 0))
    sh.putalpha(layer.getchannel("A").point(lambda v: v * 170 // 255))
    sh = sh.filter(ImageFilter.GaussianBlur(s(cap * 0.08)))
    img.alpha_composite(sh, (0, s(cap * 0.05)))
    img.alpha_composite(layer)


def coin(img: Image.Image, cx: float, cy: float, r: float) -> None:
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
    cent(d, cx, cy, r * 1.05, GOLD_DARK + (255,))


def finish(img: Image.Image) -> Image.Image:
    return img.resize((SIZE, SIZE), Image.Resampling.LANCZOS).convert("RGB")


# --- The chart: a rising income line over a falling-behind expense line ----------------------------------------

INCOME_LINE = [(0, 0.786), (0.2, 0.634), (0.4, 0.679), (0.6, 0.411), (0.8, 0.259), (1, 0.036)]  # x, y as panel fractions
EXPENSE_LINE = [(0, 0.920), (0.2, 0.884), (0.4, 0.848), (0.6, 0.804), (0.8, 0.764), (1, 0.696)]


def chart(img: Image.Image, left: float, top: float, right: float, bottom: float, rows: int, line: float) -> None:
    """The Budget tab's chart in the box; line is the income line's width (the rest scales with it)."""
    grid(img, left, top, right, bottom, rows=rows, cols=5)

    def place(points: list[tuple[float, float]]) -> list[tuple[float, float]]:
        return [(left + (right - left) * x, top + (bottom - top) * y) for x, y in points]

    income, expense = place(INCOME_LINE), place(EXPENSE_LINE)
    area(img, income, bottom, INCOME, 110)
    polyline(img, expense, EXPENSE, width=line * 0.65, glow=line * 0.4)
    polyline(img, income, INCOME, width=line, glow=line * 0.56, dots=line * 0.74)


# --- Primary: the wordmark, FISCAL over a squat chart over OVERHAUL, with a small tax coin ----------------------

def wordmark() -> Image.Image:
    img = background()
    chart(img, 120, 292, 904, 770, rows=3, line=42)
    coin(img, 210, 368, 100)
    word(img, "FISCAL", 512, 222, cap=158, colour=WHITE, tracking=20)
    word(img, "OVERHAUL", 512, 958, cap=117, colour=GOLD, tracking=2)
    return finish(img)


# --- Concept A: the chart and the coin, no text -----------------------------------------------------------------

def concept_chart_coin() -> Image.Image:
    img = background()
    chart(img, 120, 300, 904, 860, rows=4, line=46)
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
    parser.add_argument("--preview", action="store_true", help="also write 64 and 259 px previews to art/icon/preview/")
    args = parser.parse_args()

    outputs = {
        PRIMARY_OUT: wordmark,
        CONCEPTS_DIR / "chart-coin.png": concept_chart_coin,
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
            for px in (64, 259):  # the in-game mod list's icon; a Paradox Mods card
                img.resize((px, px), Image.Resampling.LANCZOS).save(PREVIEW_DIR / f"{path.stem}-{px}.png")


if __name__ == "__main__":
    main()
