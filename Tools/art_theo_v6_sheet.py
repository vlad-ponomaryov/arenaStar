"""Convert Theo v6 downloads to PNG and build a side-by-side sheet: Tools/out/art/theo_v6/sheet.png.
Usage: python Tools/art_theo_v6_sheet.py"""
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Art", "Source", "Flow", "2026-10-19_theo_v6")
RAW = os.path.join(ROOT, "Tools", "out", "art", "theo_v6")
OUT = os.path.join(RAW, "sheet.png")
ITEMS = [("a", "A  белый + золото, оранж. свечение"), ("b", "B  золото + медь/янтарь, оранж. свечение")]
TW, TH, PAD, CAP = 600, 800, 16, 48


def main():
    os.makedirs(SRC, exist_ok=True)
    sheet = Image.new("RGB", (PAD + len(ITEMS) * (TW + PAD), PAD + TH + CAP + PAD), (34, 32, 28))
    d = ImageDraw.Draw(sheet)
    font = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", 24)
    for i, (key, caption) in enumerate(ITEMS):
        png = os.path.join(SRC, f"theo_v6_{key}.png")
        raw = os.path.join(RAW, f"{key}_raw.jpg")
        if os.path.exists(raw):
            Image.open(raw).convert("RGB").save(png)  # same pixels, PNG container
        im = Image.open(png).convert("RGB")
        im.thumbnail((TW, TH), Image.LANCZOS)
        x, y = PAD + i * (TW + PAD), PAD
        sheet.paste(im, (x + (TW - im.width) // 2, y + CAP))
        d.text((x + 2, y + 8), caption, fill=(250, 236, 200), font=font)
    sheet.save(OUT)
    print(OUT, sheet.size)


if __name__ == "__main__":
    main()
