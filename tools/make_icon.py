"""Generates src/icon.ico (and docs/icon.png) for Windows Hello Brightener.

A warm orange tile with a glowing white "sun" holding the Windows Hello smile.
Small sizes (<= 24 px) get a simplified drawing with no rays so they stay crisp.
Usage: python tools/make_icon.py
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TOP, BOTTOM = (255, 196, 77), (255, 112, 26)
FACE = (232, 96, 20)


def gradient_tile(size, radius):
    tile = Image.new("RGBA", (size, size))
    px = tile.load()
    for y in range(size):
        t = y / (size - 1)
        c = tuple(round(TOP[i] + (BOTTOM[i] - TOP[i]) * t) for i in range(3))
        for x in range(size):
            px[x, y] = c + (255,)
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    tile.putalpha(mask)
    return tile


def draw(size, detailed):
    s = size * 4  # supersample, then downscale for smooth edges
    img = gradient_tile(s, round(s * 0.22))
    cx = cy = s / 2
    r = s * (0.27 if detailed else 0.36)

    if detailed:
        # soft glow behind the sun
        glow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
        ImageDraw.Draw(glow).ellipse([cx - r * 1.45, cy - r * 1.45, cx + r * 1.45, cy + r * 1.45], fill=(255, 255, 255, 120))
        img = Image.alpha_composite(img, glow.filter(ImageFilter.GaussianBlur(s * 0.06)))
        # rays
        d = ImageDraw.Draw(img)
        w = s * 0.045
        for i in range(8):
            a = math.radians(i * 45 + 22.5)
            r1, r2 = r * 1.28, r * 1.62
            p1 = (cx + math.cos(a) * r1, cy + math.sin(a) * r1)
            p2 = (cx + math.cos(a) * r2, cy + math.sin(a) * r2)
            d.line([p1, p2], fill=(255, 255, 255, 235), width=round(w))
            for p in (p1, p2):
                d.ellipse([p[0] - w / 2, p[1] - w / 2, p[0] + w / 2, p[1] + w / 2], fill=(255, 255, 255, 235))

    d = ImageDraw.Draw(img)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(255, 255, 255, 255))

    # Windows Hello style face: two short vertical eyes and a smile
    ew, eh = r * 0.17, r * 0.36
    for ex in (cx - r * 0.36, cx + r * 0.36):
        d.rounded_rectangle([ex - ew / 2, cy - r * 0.42, ex + ew / 2, cy - r * 0.42 + eh], radius=ew / 2, fill=FACE)
    sw = r * 0.16
    d.arc([cx - r * 0.55, cy - r * 0.45, cx + r * 0.55, cy + r * 0.62], start=30, end=150, fill=FACE, width=round(sw))
    # round the smile's ends
    for ang in (30, 150):
        a = math.radians(ang)
        rx, ry = r * 0.55 - sw / 2, (r * 0.62 + r * 0.45) / 2 - sw / 2
        ccy = cy + (r * 0.62 - r * 0.45) / 2
        px, py = cx + math.cos(a) * rx, ccy + math.sin(a) * ry
        d.ellipse([px - sw / 2, py - sw / 2, px + sw / 2, py + sw / 2], fill=FACE)

    return img.resize((size, size), Image.LANCZOS)


sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
images = [draw(n, detailed=n > 24) for n in sizes]
os.makedirs(os.path.join(ROOT, "src"), exist_ok=True)
images[-1].save(os.path.join(ROOT, "src", "icon.ico"), sizes=[(n, n) for n in sizes], append_images=images[:-1])
images[-1].save(os.path.join(ROOT, "docs", "icon.png"))

# preview sheet for checking the small sizes
sheet = Image.new("RGBA", (sum(sizes) + 20 * len(sizes) + 20, 300), (32, 32, 32, 255))
x = 20
for n, im in zip(sizes, images):
    sheet.alpha_composite(im, (x, 20))
    x += n + 20
sheet.save(os.path.join(ROOT, "tools", "icon-preview.png"))
print("ok")
