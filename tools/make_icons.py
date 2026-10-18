#!/usr/bin/env python3
"""Generates the mod's images with Pillow:

  assets/lock.png  64x64 lock badge drawn over Karma icons (embedded into the DLL)
  icon.png         256x256 Thunderstore package icon

Usage: python3 tools/make_icons.py
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

GOLD = (255, 214, 102, 255)
GOLD_DARK = (196, 140, 32, 255)
OUTLINE = (20, 14, 30, 255)
LUNAR = (120, 170, 255, 255)


def draw_lock(size):
    """Padlock with a dark outline so it reads on both light and dark icons."""
    s = size * 4  # supersample, then downscale for smooth edges
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    def box(x0, y0, x1, y1):
        return [x0 * s, y0 * s, x1 * s, y1 * s]

    o = 0.07  # outline thickness, fraction of size
    shackle_w = 0.11

    # Shackle: outline pass, then gold pass.
    d.arc(box(0.24 - o / 2, 0.06 - o / 2, 0.76 + o / 2, 0.62 + o / 2), 180, 360, fill=OUTLINE, width=int((shackle_w + o) * s))
    d.rectangle(box(0.24 - o / 2, 0.33, 0.24 + shackle_w + o / 2, 0.52), fill=OUTLINE)
    d.rectangle(box(0.76 - shackle_w - o / 2, 0.33, 0.76 + o / 2, 0.52), fill=OUTLINE)
    d.arc(box(0.24, 0.06, 0.76, 0.62), 180, 360, fill=GOLD_DARK, width=int(shackle_w * s))
    d.rectangle(box(0.24, 0.33, 0.24 + shackle_w, 0.52), fill=GOLD_DARK)
    d.rectangle(box(0.76 - shackle_w, 0.33, 0.76, 0.52), fill=GOLD_DARK)

    # Body.
    d.rounded_rectangle(box(0.10, 0.42, 0.90, 0.96), radius=int(0.12 * s), fill=OUTLINE)
    d.rounded_rectangle(box(0.10 + o, 0.42 + o, 0.90 - o, 0.96 - o), radius=int(0.08 * s), fill=GOLD)
    # Lower half slightly darker for some depth.
    d.rounded_rectangle(box(0.10 + o, 0.70, 0.90 - o, 0.96 - o), radius=int(0.08 * s), fill=(236, 186, 70, 255))

    # Keyhole.
    d.ellipse(box(0.42, 0.55, 0.58, 0.71), fill=OUTLINE)
    d.polygon([(0.46 * s, 0.66 * s), (0.54 * s, 0.66 * s), (0.565 * s, 0.84 * s), (0.435 * s, 0.84 * s)], fill=OUTLINE)

    return img.resize((size, size), Image.LANCZOS)


def draw_package_icon():
    s = 256
    img = Image.new("RGBA", (s, s), (0, 0, 0, 255))

    # Dark lunar radial background.
    bg = Image.new("RGBA", (s, s))
    px = bg.load()
    for y in range(s):
        for x in range(s):
            r = math.hypot(x - s / 2, y - s / 2) / (s / 2)
            t = max(0.0, min(1.0, r))
            px[x, y] = (int(34 - 22 * t), int(28 - 18 * t), int(64 - 40 * t), 255)
    img.alpha_composite(bg)

    # Eight karma "pips" around a ring: seven lit, the last one dimmed.
    glow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    cx, cy, ring = 118, 118, 78
    gd.ellipse([cx - ring, cy - ring, cx + ring, cy + ring], outline=LUNAR, width=6)
    for i in range(8):
        a = -math.pi / 2 + i * 2 * math.pi / 8
        px_, py_ = cx + ring * math.cos(a), cy + ring * math.sin(a)
        rr = 15
        color = LUNAR if i < 7 else (60, 70, 110, 255)
        gd.ellipse([px_ - rr, py_ - rr, px_ + rr, py_ + rr], fill=color)
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(6)))
    img.alpha_composite(glow)

    # Centre: a large "7".
    d = ImageDraw.Draw(img)
    d.polygon([(92, 86), (148, 86), (148, 100), (118, 158), (100, 158), (128, 101), (92, 101)], fill=(225, 235, 255, 255))

    lock = draw_lock(120)
    img.alpha_composite(lock, (128, 128))
    return img.convert("RGB")


def main():
    os.makedirs(os.path.join(ROOT, "assets"), exist_ok=True)
    draw_lock(64).save(os.path.join(ROOT, "assets", "lock.png"))
    draw_package_icon().save(os.path.join(ROOT, "icon.png"))
    print("Wrote assets/lock.png and icon.png")


if __name__ == "__main__":
    main()
