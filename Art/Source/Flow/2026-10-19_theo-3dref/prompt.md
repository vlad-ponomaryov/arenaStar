# theo-3dref — референс Тео под image-to-3D (`Docs/Briefs/A-Theo3D.md`, шаг 1)

- Дата генерации: 2026-10-19 (≈ 18:50–18:57 по местному времени).
- Инструмент: Google Flow (https://flow.google.com), проект «Arena», режим агента Flow (новый сеанс), модель изображений
  **Nano Banana 2.1** (по умолчанию проекта). Референс персонажа — **только** `../2026-10-19_theo_v8/theo_v8_1.png`
  (одобренный концепт, D-138), загружен в проект как `theo_v8_1.png` и приложен к каждому запросу; к запросу 2 приложен
  ещё результат запроса 1 (поза). Старые листы Тео как референс не использовались. По 1 генерации на картинку, без
  перегенераций. Видео не генерировалось; скачано «1K — исходный размер» (JPEG 1024×1024 и 1376×768), сохранено в PNG без
  изменения пикселей.
- Аккаунт: Google-аккаунт сотрудника пользователя (как в A.9a), без покупок.
- Условия: Условия использования Google, действуют с 30.07.2026 (https://policies.google.com/terms) — Google не претендует
  на право собственности; Generative AI Prohibited Use Policy ред. 17.12.2024. Видимого водяного знака нет.
- Использование: только референс для генерации 3D (Q-019, D-111); в сборку не попадает.

## Промпт 1 (001, 1:1; вложение — theo_v8_1.png)

```
Generate exactly 1 still image (one variant only, image only, no video), square 1:1 aspect ratio. Use ONLY the attached image theo_v8_1.png as the exact character reference; do not use any other character references from this project. Image for 3D modeling reference: the same blond boy space ranger (about 12 years old), single full-body character, front view facing the camera straight on, neutral A-pose standing straight, arms held slightly away from the body, both hands open and empty (no blaster, no weapon, no props), legs slightly apart, friendly calm expression. Keep exactly the same design, colors and details as the reference: glossy red armor plates, polished gold shoulder pads, collar, trims and knee pads, deep warm graphite undersuit, gloves and belt, belt with small glowing orange cells and side pouches, red and gold boots with glowing soles, orange monocle visor over one eye, headset with microphone, gold comet on orange orbit ring emblem on the chest, spiky light blond hair, the same face. Same cute chunky glossy toy-like 3D style with a big head and smooth rounded merged shapes. Centered, whole character in frame with margin on all sides, plain pure white background, flat even studio lighting, no cast shadow, no ground, no glow particles, no stars, no other objects. No cape, no helmet, no face plate, no round glowing light on the chest. No text, no logos, no labels.
```

## Промпт 2 (002, 16:9; вложения — theo_v8_1.png и результат промпта 1)

```
Generate exactly 1 still image (one variant only, image only, no video), wide 16:9 aspect ratio. Use ONLY the two attached images as references: the white-background front A-pose image for the pose, and theo_v8_1.png for the exact design and colors; do not use any other character references from this project. Orthographic character model sheet of exactly the same blond boy space ranger in exactly the same neutral A-pose as the attached front A-pose image (arms slightly away from the body, hands open and empty, no blaster, no props, legs slightly apart), shown four times side by side at the same scale and the same height, feet on one line: front view, three-quarter view, side profile view (facing left) and back view. Same glossy red armor plates, polished gold shoulder pads and trims, deep warm graphite undersuit, gloves and belt with small glowing orange cells, red and gold boots, orange monocle visor, headset with microphone, gold comet on orange orbit ring emblem on the chest, spiky light blond hair; in the back view show the back of the armor and hair. Same cute chunky glossy toy-like 3D style with a big head. Plain pure white background, flat even studio lighting, no cast shadows, no ground, no other objects. No cape, no helmet, no face plate. No text, no logos, no labels.
```

## Промпт 3 (003, 16:9; вложение — theo_v8_1.png)

```
Generate exactly 1 still image (one variant only, image only, no video), wide 16:9 aspect ratio. Use ONLY the attached image theo_v8_1.png as the reference; do not use any other references from this project. Show ONLY the big sci-fi blaster that the boy holds in that image, without the boy, without hands: the same exact blaster design, shape, proportions and colors — glossy red body, polished gold rings and trims, the wide gold muzzle with a ribbed cooling coil section behind it, glowing warm orange energy cells and light strips, the small scope on top, the grip and trigger, dark graphite details. Strict orthographic side view, the whole blaster lying horizontally with the muzzle pointing to the right, centered, whole object in frame with margin on all sides. Same glossy toy-like 3D style. Plain pure white background, flat even studio lighting, no cast shadow, no ground, no glow particles, no other objects. No text, no logos, no labels.
```

## Файлы и отбор

- `001.png` (1024×1024) — **вход image-to-3D**: ровный фронт, A-поза, руки пустые, весь в кадре, белый фон без тени.
  Палитра и детали как у v8_1: красные пластины, золото (наплечники, воротник, наколенники, канты), графитовый поддоспешник,
  пояс с оранжевыми ячейками и подсумками, визор-монокль, гарнитура, комета на кольце орбиты, светлые волосы. Отличия от
  концепта: пальцы перчаток полусогнуты (не раскрытая ладонь), подошвы светятся слабее, на наручах — бирюзовые индикаторы
  (есть и на концепте).
- `001_rgba.png` — вырезка 001: фон = пиксели, близкие к белому (расстояние до белого < 40), связанные с краями кадра
  (заливка от краёв — светлые детали внутри фигуры не трогаются), мягкая кромка 2 px с расплетением от белого. Python
  Pillow + numpy + scipy, скрипт не сохранён в проекте (одноразовый).
- `002.png` (1376×768) — лист «спереди / 3/4 / профиль влево / сзади», одна высота; источник цвета для вида сбоку и сзади
  (спина: красная пластина-ранец, золотые наплечники, задние подсумки). В 3/4 и профиле визор читается как узкие очки на
  оба глаза — по ТЗ брать монокль из 001.
- `003.png` (1376×768) — бластер отдельно, вид сбоку, дуло вправо: красный корпус, золотые кольца/канты, пружина-катушка и
  оребрение за дулом, две оранжевые ячейки, прицел сверху, рукоять графитово-серая. Небольшая перспектива на дуле (видна
  передняя кромка), в остальном — чистый профиль. Вешается на кость Prop.
