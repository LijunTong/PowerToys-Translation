#!/usr/bin/env python3
"""Generate the PowerToys Run plugin icons (48x48 RGBA PNG).

PowerToys built-in launcher plugins ship 48x48 RGBA icons, so we match that.

Run:
    <python> tools/make-icons.py
"""

import os
import sys

from PIL import Image, ImageDraw, ImageFont

ICON_SIZE = 48

# Windows fonts. msyhbd covers CJK; segoeuib covers Latin with a matching weight.
CJK_FONT = r"C:\Windows\Fonts\msyhbd.ttc"
LATIN_FONT = r"C:\Windows\Fonts\segoeuib.ttf"

OUTPUT_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "src",
    "Community.PowerToys.Run.Plugin.Translate",
    "Images",
)

# (file name, tile fill, border, glyph colour)
VARIANTS = (
    ("translate.dark.png", (32, 42, 56, 255), (86, 156, 214, 255), (240, 244, 250, 255)),
    ("translate.light.png", (232, 240, 250, 255), (0, 102, 204, 255), (22, 32, 44, 255)),
)


def load_font(path, size):
    if not os.path.exists(path):
        raise FileNotFoundError(f"required font not found: {path}")
    return ImageFont.truetype(path, size)


def render(fill, border, glyph):
    img = Image.new("RGBA", (ICON_SIZE, ICON_SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    draw.rounded_rectangle(
        (1, 1, ICON_SIZE - 2, ICON_SIZE - 2),
        radius=10,
        fill=fill,
        outline=border,
        width=2,
    )

    cjk = load_font(CJK_FONT, 26)
    latin = load_font(LATIN_FONT, 19)

    # "文" upper, "A" lower: reads as "translate" in either language.
    draw.text((ICON_SIZE / 2, 18), "\u6587", font=cjk, fill=glyph, anchor="mm")
    draw.text((ICON_SIZE / 2, 36), "A", font=latin, fill=glyph, anchor="mm")

    return img


def main():
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    for name, fill, border, glyph in VARIANTS:
        path = os.path.join(OUTPUT_DIR, name)
        img = render(fill, border, glyph)
        img.save(path, format="PNG", optimize=True)
        with Image.open(path) as check:
            assert check.size == (ICON_SIZE, ICON_SIZE), check.size
        print(f"wrote {path} ({os.path.getsize(path)} bytes, {ICON_SIZE}x{ICON_SIZE})")


if __name__ == "__main__":
    sys.exit(main())
