# env-props (A.4a)

- Дата генерации: 2026-10-19 (≈ 14:06–14:18 по местному времени).
- Инструмент: Google Flow (https://flow.google.com), проект «Arena», новый сеанс агента Flow (без референсов персонажей);
  модель изображений — Nano Banana 2.1 (по умолчанию проекта). Видео не генерировалось; скачано «1K — исходный размер»
  (JPEG 1024×1024), сохранено в PNG без изменения пикселей.
- Аккаунт и условия — как в `../2026-10-19_tex-grass/prompt.md` (Условия Google от 30.07.2026, без претензий на владение;
  решение человека Q-019, D-111). Видимого водяного знака нет. Чужие игры, студии и художники как вход не использовались.
- Назначение: **вход image-to-3D** (TRELLIS.2) и референс формы/цвета для Blender-скриптов рамки арены (A.4a). Не в игру.
  RGBA-вырезки (белый ключ, `Tools/Blender/env/cutout.py`) — `Art/Source/3D/env/input/<name>_rgba.png`.

## Промпт 1 (точно, как отправлен)

```
Generate 4 separate still images (images only, no video, do not use any existing images or character references from this project), square 1:1, one single object per image, each centered on a plain pure white background with lots of empty margin, no ground, no base plate, no cast shadow, whole object fully visible, seen from a 3/4 view from slightly above (camera about 30 degrees above horizontal). Style: stylized 3D render, clean vinyl-toy look, smooth soft rounded chunky shapes, saturated friendly colours, soft even studio lighting, for an original kids' mobile game "toy forest meadow village". No characters, no text, no logos, no watermark.
Image 1: a round fluffy cartoon tree - a big ball-shaped lumpy cloud-like canopy made of several soft round bulges in fresh green (#5BB56B light, #3D935C shadow), short thick brown trunk with a slight root flare.
Image 2: a different cartoon tree - taller, two stacked round puffy canopy blobs in emerald green (#3FA765), short warm brown trunk.
Image 3: a cute mushroom house - wide red mushroom cap (#E5483A) with big white round spots, cream cylindrical body (#F3E3C3) with a round wooden door and a small round window.
Image 4: a cute mushroom house - pale blue mushroom cap (#5FA8E8) with white spots, cream body, round wooden door, one round window, small chimney-free roof.
```

## Промпт 2 (точно, как отправлен)

```
Generate 4 more separate still images in exactly the same style and setup (one single object per image, square 1:1, centered on plain pure white background, lots of empty margin, no ground, no base plate, no cast shadow, whole object visible, 3/4 view from slightly above about 30 degrees, stylized 3D render, clean vinyl-toy look, smooth soft rounded chunky shapes, saturated friendly colours, soft even studio lighting; no characters, no text, no logos, no watermark; do not use any character references).
Image 1: a third cartoon tree - wide low round bushy canopy, a squashed ball made of many soft round bulges in deep green (#2F8A55 shadow, #4DAA62 light), very short thick brown trunk.
Image 2: a small group of three rounded smooth boulders of different sizes huddled together, light warm grey-beige stone (#CFC6B8) with soft bright green moss caps (#8CC63F) on top, chunky toy look.
Image 3: a big hollow tree stump used as a storage den - wide short stump with warm brown bark (#9A5A32), light wood top with spiral rings (#E8C08A), a large dark round hollow opening in the front side, a few chunky roots spreading at the base, a little moss.
Image 4: the same kind of hollow tree stump but taller and narrower, with a round dark hollow hole in the front, two small leaves sprouting from the rim, chunky roots.
```

## Файлы и отбор

| Файл | Что | Использование |
| --- | --- | --- |
| `tree1.png` | дерево-шар из округлых выпуклостей | **вход TRELLIS.2** → `tree_round` (единственная генерация дня) |
| `tree2.png` | дерево из двух крон | референс `tree_tall` (Blender `tree_puff.py`); вход TRELLIS.2 — когда будет квота |
| `tree3.png` | широкое приземистое дерево | референс `tree_wide` (`tree_puff.py`) |
| `house_red.png`, `house_blue.png` | домики-грибы | референс `house_red`/`house_blue` (`build_houses.py`); вход TRELLIS.2 позже |
| `rocks.png` | группа валунов со мхом | референс `rocks_moss` (`tree_puff.py`) |
| `stump_wide.png` | широкий пень-дупло | **выбран** для объекта режима: референс `stump_hollow` (`build_houses.py`) |
| `stump_tall.png` | высокий пень-дупло | запасной |

Оценка: все восемь — ровно «игрушечный» стиль концепта арены 001/002 (гладкие выпуклые формы, насыщенные цвета, мягкий свет),
один объект на белом — удобно для image-to-3D. Похожего на чужие игры не вижу: жанровые мультяшные деревья, грибы, пни.
