"""Draws the installer's WixUI bitmaps from the app icon (needs Pillow: pip install pillow).

    python build/make-installer-bitmaps.py

Writes installer/Assets/banner.png (493x58, top of the progress page) and installer/Assets/dialog.png
(493x312, behind the welcome and finish pages; the dialog's text starts 164 px in, so the art stays in the
left-hand panel). Flat art only, no text: MSI shows these at 96 DPI and stretches them on high-DPI screens.
"""
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
ICON = ROOT / "src" / "PromptLibrary.App" / "Assets" / "app-icon.png"
OUT = ROOT / "installer" / "Assets"

WHITE = (255, 255, 255)
BRAND = (0x43, 0x38, 0xCA)       # Brush.Brand
BRAND_SOFT = (0xE8, 0xE7, 0xFB)  # Brush.BrandSoft

PANEL_WIDTH = 164


def icon(size):
    return Image.open(ICON).convert("RGBA").resize((size, size), Image.LANCZOS)


def dialog():
    image = Image.new("RGB", (493, 312), WHITE)
    draw = ImageDraw.Draw(image)
    draw.rectangle([0, 0, PANEL_WIDTH - 1, 311], fill=BRAND_SOFT)
    draw.rectangle([PANEL_WIDTH - 4, 0, PANEL_WIDTH - 1, 311], fill=BRAND)
    art = icon(96)
    image.paste(art, ((PANEL_WIDTH - 4 - 96) // 2, 72), art)
    return image


def banner():
    image = Image.new("RGB", (493, 58), WHITE)
    art = icon(40)
    image.paste(art, (493 - 40 - 12, 9), art)
    return image


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    dialog().save(OUT / "dialog.png")
    banner().save(OUT / "banner.png")
    print(f"Wrote {OUT / 'dialog.png'} and {OUT / 'banner.png'}")
