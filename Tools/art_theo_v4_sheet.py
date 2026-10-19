"""Build a comparison sheet of Theo v4 variants: Tools/out/art/theo_v4/sheet.png.
Usage: python Tools/art_theo_v4_sheet.py"""
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Art", "Source", "Flow", "2026-10-19_theo_v4")
OUT = os.path.join(ROOT, "Tools", "out", "art", "theo_v4", "sheet.png")
NAMES = {
    "1": "мандарин + графит + крем",
    "2": "белый + золото + оранж. свечение",
    "3": "бирюза/мята + графит + оранжевый",
    "4": "слива + серебро + лаймовое свечение",
}
TW, TH, PAD, CAP = 600, 800, 16, 48

def main():
    files = sorted(f for f in os.listdir(SRC) if f.startswith("theo_v4_") and f.endswith(".png"))
    sheet = Image.new("RGB", (PAD + len(files) * (TW + PAD), PAD + TH + CAP + PAD), (34, 32, 28))
    d = ImageDraw.Draw(sheet)
    font = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", 24)
    for i, f in enumerate(files):
        x, y = PAD + i * (TW + PAD), PAD
        im = Image.open(os.path.join(SRC, f)).convert("RGB")
        im.thumbnail((TW, TH), Image.LANCZOS)
        sheet.paste(im, (x + (TW - im.width) // 2, y + CAP))
        num = f[len("theo_v4_"):-4]
        d.text((x + 2, y + 8), f"{num}  {NAMES.get(num, f)}", fill=(250, 236, 200), font=font)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    sheet.save(OUT)
    print(OUT, sheet.size)

if __name__ == "__main__":
    main()
