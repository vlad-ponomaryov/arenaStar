"""Convert Theo v8 (red-gold + third color) downloads to PNG and build a 4-up sheet with palette strips:
Tools/out/art/theo_v8/sheet.png. Usage: python Tools/art_theo_v8_sheet.py"""
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Art", "Source", "Flow", "2026-10-19_theo_v8")
RAW = os.path.join(ROOT, "Tools", "out", "art", "theo_v8")
OUT = os.path.join(RAW, "sheet.png")
RED, GOLD = "#B3141C", "#E0A93A"
ITEMS = [("1", "1  графит-ганметалл", "#3D3836"), ("2", "2  слоновая кость", "#F1E6CF"),
         ("3", "3  шоколад-бронза", "#4B2E20"), ("4", "4  королевская слива", "#4A2148")]
TW, TH, PAD, CAP, STRIP = 600, 800, 16, 48, 70


def main():
    os.makedirs(SRC, exist_ok=True)
    sheet = Image.new("RGB", (PAD + len(ITEMS) * (TW + PAD), PAD + CAP + TH + PAD + STRIP + PAD), (34, 32, 28))
    d = ImageDraw.Draw(sheet)
    font = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", 24)
    small = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 18)
    for i, (key, caption, third) in enumerate(ITEMS):
        png = os.path.join(SRC, f"theo_v8_{key}.png")
        raw = os.path.join(RAW, f"raw_{key}.jpg")
        if os.path.exists(raw):
            Image.open(raw).convert("RGB").save(png)  # same pixels, PNG container
        im = Image.open(png).convert("RGB")
        im.thumbnail((TW, TH), Image.LANCZOS)
        x, y = PAD + i * (TW + PAD), PAD
        sheet.paste(im, (x + (TW - im.width) // 2, y + CAP))
        d.text((x + 2, y + 8), caption, fill=(250, 236, 200), font=font)
        sy = y + CAP + TH + PAD
        sw = TW // 3
        for j, hexc in enumerate((RED, GOLD, third)):
            sx = x + j * sw
            d.rectangle((sx, sy, sx + sw - 4, sy + STRIP - 26), fill=hexc, outline=(90, 86, 80))
            d.text((sx + 4, sy + STRIP - 24), hexc, fill=(230, 220, 200), font=small)
    sheet.save(OUT)
    print(OUT, sheet.size)


if __name__ == "__main__":
    main()
