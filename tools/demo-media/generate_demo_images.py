#!/usr/bin/env python3
"""
Generates the demonstration imagery that DemoMediaSeeder uploads.

Everything here is drawn procedurally - flat block-print, weave, glaze and wood-grain motifs - so
there is no third-party artwork and no licence to carry. The only external input is Pillow's
bundled default font (Aileron, SIL OFL) for the product-name labels.

Run from the repository root:

    python tools/demo-media/generate_demo_images.py

Output goes to src/backend/modules/KlaraHome.Modules.Media/Infrastructure/Seeding/DemoImages and is
committed: the seeder embeds the JPEGs, so nothing is generated or downloaded at seed time. The
file names are the contract with the seeder (see DemoMediaSeeder.Keys), and the output is
deterministic - a fixed random seed per image - so re-running produces a clean diff.
"""

from __future__ import annotations

import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

OUT = Path("src/backend/modules/KlaraHome.Modules.Media/Infrastructure/Seeding/DemoImages")
SS = 2  # supersampling factor, for anti-aliased edges

CREAM = (244, 237, 224)
INK = (44, 38, 34)

# (key, label) - keys are the file stems the seeder looks for.
CATEGORIES = [
    ("category-demo-cushion-covers", "Cushion Covers", "silk"),
    ("category-demo-throws-and-quilts", "Throws & Quilts", "dohar"),
    ("category-demo-stoneware", "Stoneware", "bowl"),
    ("category-demo-serveware", "Serveware", "spoons"),
]

PRODUCTS = [
    ("product-DEMO-CC-0001", "Jaipur Block-Print Cushion Cover", "block"),
    ("product-DEMO-CC-0002", "Handwoven Linen Cushion Cover", "linen"),
    ("product-DEMO-CC-0003", "Raw Silk Bolster Cover", "silk"),
    ("product-DEMO-TQ-0001", "Cotton Dohar Throw", "dohar"),
    ("product-DEMO-TQ-0002", "Hand-Quilted Razai", "razai"),
    ("product-DEMO-SW-0001", "Stoneware Dinner Plate", "plate"),
    ("product-DEMO-SW-0002", "Stoneware Serving Bowl", "bowl"),
    ("product-DEMO-SW-0003", "Stoneware Mug, Set of 2", "mugs"),
    ("product-DEMO-SV-0001", "Mango Wood Serving Board", "board"),
    ("product-DEMO-SV-0002", "Brass Serving Spoons", "spoons"),
]


def font(size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.load_default(size=size)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def canvas(w, h, top, bottom):
    """A vertical gradient, drawn at supersampled size."""
    img = Image.new("RGB", (w * SS, h * SS), top)
    d = ImageDraw.Draw(img)
    for y in range(h * SS):
        d.line([(0, y), (w * SS, y)], fill=lerp(top, bottom, y / (h * SS)))
    return img


def finish(img: Image.Image, w: int, h: int, path: Path, quality=84):
    img = img.resize((w, h), Image.LANCZOS)
    img.save(path, "JPEG", quality=quality, optimize=True, progressive=True)


def label(img: Image.Image, text: str, w: int, h: int):
    """A cream caption strip along the bottom with the name baked in."""
    d = ImageDraw.Draw(img)
    size = int(h * 0.045) * SS
    f = font(size)
    bar_h = int(h * 0.12) * SS
    d.rectangle([0, h * SS - bar_h, w * SS, h * SS], fill=CREAM)
    d.line([(0, h * SS - bar_h), (w * SS, h * SS - bar_h)], fill=(214, 202, 182), width=2 * SS)
    box = d.textbbox((0, 0), text, font=f)
    tw, th = box[2] - box[0], box[3] - box[1]
    d.text(((w * SS - tw) / 2 - box[0], h * SS - bar_h + (bar_h - th) / 2 - box[1]), text, font=f, fill=INK)


# --------------------------------------------------------------------------- motifs

def shadow(img, box, radius, blur=18):
    from PIL import ImageFilter

    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).rounded_rectangle(
        [box[0] + 8 * SS, box[1] + 14 * SS, box[2] + 8 * SS, box[3] + 14 * SS], radius, fill=(0, 0, 0, 90)
    )
    layer = layer.filter(ImageFilter.GaussianBlur(blur * SS))
    img.paste(layer, (0, 0), layer)


def booti(d, cx, cy, r, col):
    """A small block-print flower: four petals and a centre."""
    for k in range(8):
        a = k * math.pi / 4
        px, py = cx + math.cos(a) * r * 0.62, cy + math.sin(a) * r * 0.62
        pr = r * 0.34
        d.ellipse([px - pr, py - pr, px + pr, py + pr], fill=col)
    d.ellipse([cx - r * 0.3, cy - r * 0.3, cx + r * 0.3, cy + r * 0.3], fill=col)


def block_print(d, box, bg, ink, ink2, rnd, step):
    x0, y0, x1, y1 = box
    d.rectangle(box, fill=bg)
    row = 0
    y = y0 + step / 2
    while y < y1 + step:
        x = x0 + (step / 2 if row % 2 == 0 else step)
        while x < x1 + step:
            jitter = rnd.uniform(-2, 2) * SS
            booti(d, x + jitter, y + jitter, step * 0.30, ink)
            d.ellipse([x - step * 0.07, y - step * 0.07, x + step * 0.07, y + step * 0.07], fill=ink2)
            # small leaf between motifs
            lx, ly = x + step / 2, y + step / 2
            d.polygon(
                [(lx, ly - step * 0.14), (lx + step * 0.08, ly), (lx, ly + step * 0.14), (lx - step * 0.08, ly)],
                fill=ink2,
            )
            x += step
        y += step * 0.5
        row += 1


def mask_to(img, box, radius, painter):
    """Paints into a rounded rect only."""
    layer = Image.new("RGB", img.size, (0, 0, 0))
    painter(ImageDraw.Draw(layer))
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle(box, radius, fill=255)
    img.paste(layer, (0, 0), mask)


def draw_cushion(img, w, h, style, rnd):
    top = int(h * 0.12) * SS
    side = int(w * 0.14) * SS
    bottom = int(h * 0.30) * SS
    box = [side, top, w * SS - side, h * SS - bottom]
    if style != "silk":
        shadow(img, box, 36 * SS)
    d = ImageDraw.Draw(img)

    if style == "block":
        def paint(dd):
            block_print(dd, [0, 0, w * SS, h * SS], (233, 220, 196), (31, 58, 96), (178, 74, 46), rnd, 78 * SS)
    elif style == "linen":
        def paint(dd):
            dd.rectangle([0, 0, w * SS, h * SS], fill=(214, 200, 172))
            for y in range(0, h * SS, 5 * SS):
                dd.line([(0, y), (w * SS, y)], fill=(201, 186, 156), width=SS)
            for x in range(0, w * SS, 6 * SS):
                dd.line([(x, 0), (x, h * SS)], fill=(222, 210, 184), width=SS)
            for _ in range(160):
                x, y = rnd.randint(0, w * SS), rnd.randint(0, h * SS)
                dd.line([(x, y), (x + rnd.randint(10, 40) * SS, y)], fill=(188, 172, 140), width=SS)
            # a hand-stitched border
            m = 26 * SS
            for band_y, band_col in ((0.30, (122, 138, 110)), (0.36, (176, 112, 74)), (0.66, (176, 112, 74)), (0.72, (122, 138, 110))):
                yy = box[1] + (box[3] - box[1]) * band_y
                dd.rectangle([box[0], yy, box[2], yy + 8 * SS], fill=band_col)
            dd.rectangle([box[0] + m, box[1] + m, box[2] - m, box[3] - m], outline=(120, 98, 70), width=2 * SS)
    else:  # silk
        def paint(dd):
            for x in range(w * SS):
                t = (math.sin(x / (w * SS) * math.pi * 2.6) + 1) / 2
                dd.line([(x, 0), (x, h * SS)], fill=lerp((120, 28, 52), (196, 86, 96), t))
            for _ in range(500):
                x, y = rnd.randint(0, w * SS), rnd.randint(0, h * SS)
                dd.line([(x, y), (x + rnd.randint(2, 14) * SS, y + rnd.randint(-2, 2) * SS)], fill=(150, 48, 66), width=SS)

    if style == "silk":
        # a bolster: narrower, tall pill rather than a square
        bw = int(w * 0.30) * SS
        box = [(w * SS - bw) // 2 - 70 * SS, int(h * 0.10) * SS, (w * SS + bw) // 2 + 70 * SS, int(h * 0.62) * SS]
        box = [int(w * 0.12) * SS, int(h * 0.26) * SS, int(w * 0.88) * SS, int(h * 0.66) * SS]
        shadow(img, box, 120 * SS)
        mask_to(img, box, 120 * SS, paint)
        d = ImageDraw.Draw(img)
        for ex in (box[0] + 30 * SS, box[2] - 30 * SS):
            d.line([(ex, box[1] + 14 * SS), (ex, box[3] - 14 * SS)], fill=(110, 22, 44), width=3 * SS)
    else:
        mask_to(img, box, 36 * SS, paint)
        d = ImageDraw.Draw(img)
        d.rounded_rectangle(box, 36 * SS, outline=(255, 255, 255), width=0)


def draw_quilt(img, w, h, kind, rnd):
    top, bot = int(h * 0.14) * SS, int(h * 0.30) * SS
    box = [int(w * 0.10) * SS, top, int(w * 0.90) * SS, h * SS - bot]
    shadow(img, box, 18 * SS)

    if kind == "dohar":
        def paint(dd):
            dd.rectangle([0, 0, w * SS, h * SS], fill=(226, 214, 190))
            stripes = [(46, 94, 98), (226, 214, 190), (176, 80, 52), (226, 214, 190), (46, 94, 98)]
            sx = box[0]
            sw = (box[2] - box[0]) / 14
            col = 0
            i = 0
            while sx < box[2]:
                dd.rectangle([sx, 0, sx + sw, h * SS], fill=stripes[col % len(stripes)] if i % 3 == 0 else (226, 214, 190))
                if i % 3 == 0:
                    col += 1
                sx += sw
                i += 1
            for y in range(box[1], box[3], 7 * SS):
                dd.line([(box[0], y), (box[2], y)], fill=(255, 255, 255), width=1)
    else:  # quilt / razai
        quilt_fill, quilt_line = ((139, 156, 126), (96, 116, 86)) if kind == "quilt" else ((184, 98, 72), (140, 66, 46))

        def paint(dd):
            dd.rectangle([0, 0, w * SS, h * SS], fill=quilt_fill)
            step = 56 * SS
            for k in range(-20, 40):
                dd.line([(box[0] + k * step, box[1]), (box[0] + k * step + (box[3] - box[1]), box[3])], fill=quilt_line, width=3 * SS)
                dd.line([(box[0] + k * step + (box[3] - box[1]), box[1]), (box[0] + k * step, box[3])], fill=quilt_line, width=3 * SS)
            # stitch dots at each crossing, for the hand-quilted look
            for i in range(0, 30):
                for j in range(0, 30):
                    px = box[0] + i * step / 2
                    py = box[1] + j * step / 2
                    if (i + j) % 2 == 0 and box[0] < px < box[2] and box[1] < py < box[3]:
                        dd.ellipse([px - 3 * SS, py - 3 * SS, px + 3 * SS, py + 3 * SS], fill=(236, 230, 210))
    mask_to(img, box, 18 * SS, paint)
    d = ImageDraw.Draw(img)
    # a folded edge
    d.rounded_rectangle([box[0], box[1], box[2], box[1] + 26 * SS], 18 * SS, fill=None, outline=(0, 0, 0), width=0)
    fold = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(fold).rectangle([box[0], box[1], box[2], box[1] + 26 * SS], fill=(255, 255, 255, 46))
    img.paste(fold, (0, 0), fold)


def speckle(d, box_fn, rnd, n, cols, clip=None):
    for _ in range(n):
        x, y = box_fn(rnd)
        if clip and math.hypot(x - clip[0], y - clip[1]) > clip[2]:
            continue
        r = rnd.uniform(0.8, 2.4) * SS
        d.ellipse([x - r, y - r, x + r, y + r], fill=rnd.choice(cols))


def disc(d, cx, cy, r, fill, outline=None, width=0):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill, outline=outline, width=width)


def draw_ceramic(img, w, h, kind, rnd):
    cx, cy = w * SS // 2, int(h * 0.44) * SS
    R = int(min(w, h) * 0.36) * SS
    d = ImageDraw.Draw(img)

    def blob_shadow(cx, cy, r):
        from PIL import ImageFilter

        layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
        ImageDraw.Draw(layer).ellipse([cx - r, cy - r + 16 * SS, cx + r, cy + r + 16 * SS], fill=(0, 0, 0, 85))
        layer = layer.filter(ImageFilter.GaussianBlur(16 * SS))
        img.paste(layer, (0, 0), layer)

    glaze = [(206, 196, 178), (186, 176, 158), (168, 158, 140)]
    if kind == "plate":
        blob_shadow(cx, cy, R)
        disc(d, cx, cy, R, (224, 214, 196), outline=(170, 158, 138), width=3 * SS)
        disc(d, cx, cy, int(R * 0.74), (214, 203, 183), outline=(188, 176, 156), width=3 * SS)
        disc(d, cx, cy, int(R * 0.70), (222, 212, 194))
        # speckle, clipped loosely to the disc
        speckle(d, lambda r: (cx + r.gauss(0, R * 0.45), cy + r.gauss(0, R * 0.45)), rnd, 520, [(120, 98, 78), (150, 128, 104)], clip=(cx, cy, R * 0.72))
        # iron-brown rim line
        disc(d, cx, cy, int(R * 0.95), None, outline=(122, 92, 66), width=4 * SS)
    elif kind == "bowl":
        blob_shadow(cx, cy, R)
        disc(d, cx, cy, R, (92, 112, 118))
        disc(d, cx, cy, int(R * 0.88), (110, 132, 138))
        for k in range(1, 7):
            disc(d, cx - 6 * SS, cy - 4 * SS, int(R * (0.88 - k * 0.10)), None, outline=lerp((110, 132, 138), (62, 82, 90), k / 7), width=3 * SS)
        speckle(d, lambda r: (cx + r.gauss(0, R * 0.4), cy + r.gauss(0, R * 0.4)), rnd, 380, [(236, 232, 220), (60, 78, 84)], clip=(cx, cy, R * 0.86))
        disc(d, cx, cy, int(R * 0.30), (70, 92, 98))
    else:  # mugs
        for ox, oy, sc in ((-0.50, -0.02, 0.62), (0.46, 0.06, 0.58)):
            mx, my, mr = cx + int(R * ox), cy + int(R * oy), int(R * sc)
            blob_shadow(mx, my, mr)
            # handle
            hx = mx + mr * (1.05 if ox < 0 else -1.05)
            d.ellipse([hx - mr * 0.3, my - mr * 0.34, hx + mr * 0.3, my + mr * 0.34], outline=(150, 128, 98), width=int(mr * 0.15))
            disc(d, mx, my, mr, (196, 178, 146), outline=(140, 118, 90), width=3 * SS)
            disc(d, mx, my, int(mr * 0.80), (70, 52, 40))
            disc(d, mx - 4 * SS, my - 4 * SS, int(mr * 0.74), (92, 70, 54))
            speckle(d, lambda r, mx=mx, my=my, mr=mr: (mx + r.gauss(0, mr * 0.9), my + r.gauss(0, mr * 0.9)), rnd, 90, [(120, 98, 78), (214, 196, 164)], clip=(mx, my, mr * 0.78))


def wood_fill(dd, box, rnd, base, dark):
    x0, y0, x1, y1 = box
    dd.rectangle(box, fill=base)
    for k in range(60):
        y = y0 + (y1 - y0) * k / 60 + rnd.uniform(-3, 3) * SS
        pts = []
        for x in range(int(x0), int(x1) + 1, 24 * SS):
            pts.append((x, y + math.sin(x / (90 * SS) + k) * 5 * SS))
        dd.line(pts, fill=lerp(base, dark, rnd.uniform(0.2, 0.9)), width=rnd.choice([1, 1, 2]) * SS)


def draw_board(img, w, h, rnd):
    d = ImageDraw.Draw(img)
    box = [int(w * 0.10) * SS, int(h * 0.30) * SS, int(w * 0.92) * SS, int(h * 0.62) * SS]
    shadow(img, box, 70 * SS)

    def paint(dd):
        wood_fill(dd, [0, 0, w * SS, h * SS], rnd, (176, 122, 70), (112, 72, 38))

    mask_to(img, box, 70 * SS, paint)
    d = ImageDraw.Draw(img)
    # handle hole
    hx, hy = box[0] + 54 * SS, (box[1] + box[3]) // 2
    d.ellipse([hx - 22 * SS, hy - 22 * SS, hx + 22 * SS, hy + 22 * SS], fill=(238, 228, 208), outline=(96, 60, 30), width=3 * SS)
    d.rounded_rectangle(box, 70 * SS, outline=(98, 62, 32), width=3 * SS)


def spoon(d, x, y, length, angle, col, hi):
    """A serving spoon: a handle and an oval bowl, rotated about its tip."""
    ca, sa = math.cos(angle), math.sin(angle)

    def pt(u, v):
        return (x + u * ca - v * sa, y + u * sa + v * ca)

    handle = [pt(0, -5 * SS), pt(length * 0.7, -8 * SS), pt(length * 0.7, 8 * SS), pt(0, 5 * SS)]
    d.polygon(handle, fill=col)
    # bowl
    pts = []
    for k in range(40):
        a = k / 40 * 2 * math.pi
        pts.append(pt(length * 0.82 + math.cos(a) * length * 0.2, math.sin(a) * length * 0.13))
    d.polygon(pts, fill=col)
    inner = []
    for k in range(40):
        a = k / 40 * 2 * math.pi
        inner.append(pt(length * 0.82 + math.cos(a) * length * 0.15, math.sin(a) * length * 0.085))
    d.polygon(inner, fill=hi)


def draw_spoons(img, w, h, rnd):
    d = ImageDraw.Draw(img)
    base = int(h * 0.20) * SS
    for i, (ang, dx) in enumerate(((-0.10, -0.12), (0.0, 0.0), (0.10, 0.12))):
        sx = int(w * (0.14 + 0.1 * i)) * SS
        sy = base + int(h * 0.16 * i) * SS
        # soft shadow copy, then the spoon
        spoon(d, sx + 8 * SS, sy + 10 * SS, int(w * 0.62) * SS, ang, (140, 112, 56), (140, 112, 56))
        spoon(d, sx, sy, int(w * 0.62) * SS, ang, (200, 160, 70), (232, 200, 112))


MOTIF_BG = {
    "block": ((222, 206, 178), (198, 176, 144)),
    "linen": ((226, 216, 196), (206, 192, 164)),
    "silk": ((236, 224, 208), (214, 196, 176)),
    "dohar": ((228, 220, 204), (206, 194, 172)),
    "quilt": ((232, 226, 210), (208, 198, 176)),
    "razai": ((222, 212, 200), (198, 184, 168)),
    "plate": ((90, 84, 78), (60, 56, 52)),
    "bowl": ((212, 198, 176), (186, 170, 144)),
    "mugs": ((206, 214, 214), (176, 188, 190)),
    "board": ((84, 92, 84), (56, 64, 58)),
    "spoons": ((70, 76, 90), (44, 48, 60)),
}


def render_object(w, h, kind, seed, caption):
    rnd = random.Random(seed)
    top, bot = MOTIF_BG[kind]
    img = canvas(w, h, top, bot)
    if kind in ("block", "linen", "silk"):
        draw_cushion(img, w, h, kind, rnd)
    elif kind in ("dohar", "quilt", "razai"):
        draw_quilt(img, w, h, kind, rnd)
    elif kind in ("plate", "bowl", "mugs"):
        draw_ceramic(img, w, h, kind, rnd)
    elif kind == "board":
        draw_board(img, w, h, rnd)
    elif kind == "spoons":
        draw_spoons(img, w, h, rnd)
    label(img, caption, w, h)
    return img


# --------------------------------------------------------------------------- hero

def render_hero(w=1600, h=900):
    """Warm, calm left half for the overlaid headline; the textile motif sits right."""
    rnd = random.Random(7)
    img = canvas(w, h, (236, 224, 202), (214, 196, 168))
    d = ImageDraw.Draw(img)

    # a block-printed length of cloth running off the right edge, slightly rotated feel via bands
    cloth = [int(w * 0.52) * SS, 0, w * SS, h * SS]
    block_print(d, cloth, (31, 58, 96), (233, 220, 196), (214, 130, 84), rnd, 96 * SS)
    # soft edge between the plain and printed halves
    for i in range(60 * SS):
        a = i / (60 * SS)
        d.line([(cloth[0] - i, 0), (cloth[0] - i, h * SS)], fill=lerp((31, 58, 96), (236, 224, 202), a), width=1)
    d.rectangle([cloth[0] - 3 * SS, 0, cloth[0], h * SS], fill=(176, 80, 52))

    # a stoneware bowl and a stack of folded cotton resting low in the plain half
    cx, cy, R = int(w * 0.40) * SS, int(h * 0.78) * SS, int(h * 0.20) * SS
    from PIL import ImageFilter

    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).ellipse([cx - R, cy - R * 0.4 + 18 * SS, cx + R, cy + R * 0.4 + 18 * SS], fill=(0, 0, 0, 90))
    layer = layer.filter(ImageFilter.GaussianBlur(14 * SS))
    img.paste(layer, (0, 0), layer)
    d = ImageDraw.Draw(img)
    d.ellipse([cx - R, cy - R * 0.55, cx + R, cy + R * 0.55], fill=(92, 112, 118))
    d.ellipse([cx - R * 0.88, cy - R * 0.46, cx + R * 0.88, cy + R * 0.46], fill=(70, 92, 98))
    speckle(d, lambda r: (cx + r.gauss(0, R * 0.4), cy + r.gauss(0, R * 0.2)), rnd, 160, [(236, 232, 220), (60, 78, 84)], clip=(cx, cy, R * 0.8))
    return img


def render_category(w, h, kind, seed, caption):
    img = render_object(w, h, kind, seed, caption)
    return img


def main():
    OUT.mkdir(parents=True, exist_ok=True)

    finish(render_hero(), 1600, 900, OUT / "hero.jpg", quality=82)

    for i, (key, name, kind) in enumerate(CATEGORIES):
        finish(render_category(800, 800, kind, 100 + i, name), 800, 800, OUT / f"{key}.jpg")

    for i, (key, name, kind) in enumerate(PRODUCTS):
        finish(render_object(800, 800, kind, 200 + i, name), 800, 800, OUT / f"{key}.jpg")

    total = sum(p.stat().st_size for p in OUT.glob("*.jpg"))
    print(f"wrote {len(list(OUT.glob('*.jpg')))} images, {total / 1024:.0f} KB")


if __name__ == "__main__":
    main()
