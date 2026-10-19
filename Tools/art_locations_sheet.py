"""Build a comparison sheet of location concepts: Tools/out/art/locations/sheet.png.
Usage: python Tools/art_locations_sheet.py"""
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Art", "Source", "Flow", "2026-10-19_locations")
OUT = os.path.join(ROOT, "Tools", "out", "art", "locations", "sheet.png")
NAMES = {
    "01": "Летний двор", "02": "Поляна Большого Пня (лагерь на бору)", "03": "Пляж и бухта",
    "04": "Сад на заднем дворе", "05": "Праздник фонариков", "06": "Крыши города",
    "07": "Снежная долина", "08": "«Мы маленькие» (детская)", "09": "Ярмарочная площадь",
    "10": "Школьный двор-сквер", "11": "Поляна Пня: Новый год",
}
COLS, TW, TH, PAD, CAP = 3, 640, 357, 16, 44

def main():
    files = sorted(f for f in os.listdir(SRC) if f.endswith(".png"))
    rows = (len(files) + COLS - 1) // COLS
    sheet = Image.new("RGB", (PAD + COLS * (TW + PAD), PAD + rows * (TH + CAP + PAD)), (34, 32, 28))
    d = ImageDraw.Draw(sheet)
    font = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", 26)
    for i, f in enumerate(files):
        r, c = divmod(i, COLS)
        x, y = PAD + c * (TW + PAD), PAD + r * (TH + CAP + PAD)
        im = Image.open(os.path.join(SRC, f)).convert("RGB").resize((TW, TH), Image.LANCZOS)
        sheet.paste(im, (x, y + CAP))
        num = f[:2]
        d.text((x + 2, y + 6), f"{num}  {NAMES.get(num, f)}", fill=(250, 236, 200), font=font)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    sheet.save(OUT)
    print(OUT, sheet.size)

if __name__ == "__main__":
    main()
