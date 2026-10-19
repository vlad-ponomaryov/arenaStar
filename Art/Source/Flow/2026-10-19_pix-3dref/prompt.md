# pix-3dref — референс Пикса под image-to-3D (спайк, `Docs/Spike-ImageTo3D.md`)

- Дата генерации: 2026-10-19 (≈ 12:13–12:15 по местному времени).
- Инструмент: Google Flow (https://flow.google.com), проект «Arena», режим агента Flow; модель изображений **Nano Banana 2.1**
  (по умолчанию проекта). Агент Flow сам взял лист `2026-10-19_pix-fox/001` как референс персонажа. Видео не генерировалось;
  скачано «1K — исходный размер» (JPEG 1024×1024 и 1376×768), сохранено в PNG без изменения пикселей.
- Аккаунт: Google-аккаунт сотрудника пользователя (как в A.9a), без покупок.
- Условия: Условия использования Google, действуют с 30.07.2026 (https://policies.google.com/terms, перечитаны 19.10.2026) —
  Google не претендует на право собственности; Generative AI Prohibited Use Policy ред. 17.12.2024. Видимого водяного знака нет.
- Использование: только референс для генерации 3D (Q-019, D-111); в сборку не попадает.

## Промпт 1 (001–002, 1:1)

```
Generate 2 still image variants (images only, no video), square 1:1 aspect ratio. Use the orange fox fighter from the fox character turnaround sheet in this project as the exact character reference (same design, colors, goggles, proportions). Image for 3D modeling reference: a single full-body character, front view facing the camera straight on, neutral A-pose standing straight, arms held slightly away from the body, paws open and empty (no blaster, no props), legs slightly apart, the big fluffy white-tipped tail visible to one side, yellow goggles pushed up on the forehead, friendly calm expression. Centered, whole character in frame with margin, plain pure white background, flat even studio lighting, no cast shadow on the ground, no ground, no other objects. Keep the same cute chunky vinyl-toy proportions, smooth rounded merged shapes, soft cel shading and thin warm dark-brown outline. No text, no logos, no labels.
```

## Промпт 2 (003–004, 16:9)

```
Generate 2 still image variants (images only, no video), 16:9: orthographic model sheet of exactly the same fox character in exactly the same neutral A-pose as the previous front-view image (empty paws, no props, goggles on the forehead), shown four times side by side at the same scale and height: front view, three-quarter view, side profile view (facing left) and back view. Plain pure white background, flat even studio lighting, no cast shadows, no ground, same cute chunky vinyl-toy proportions, soft cel shading and thin warm dark-brown outline. No text, no logos, no labels.
```

## Файлы и отбор

- `001.png` — **выбран** для image-to-3D: ровный фронт, A-поза, хвост сбоку, белый фон. Вход генераторов — его RGBA-вырезка
  (белый фон → альфа), чтобы демо не включали своё удаление фона (у TRELLIS.2 это BRIA RMBG-2.0, CC BY-NC).
- `002.png` — запасной (руки ниже, хвост меньше).
- `003.png`, `004.png` — листы «спереди / 3/4 / сбоку / сзади»; **004** — источник цвета для вида сзади и сбоку в
  `Tools/Blender/prepare_fighter.py` (рамки фигур — в `Tools/Blender/fighters/pix.json`).
