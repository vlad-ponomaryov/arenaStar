# Окружение: модели рамки арены и объектов режимов (A.4a, 2026-10-19)

Вне `Assets`, в сборку не входит. Перенос FBX в `Assets/_Project/Art/Models/Env/` и импорт — A.4b (рамка), A.4c (жёлуди, дупло).
GLB/FBX — через Git LFS. Бюджеты — `Docs/Briefs/A-EnvTarget.md` §3.3. Все модели — вершинные цвета (sRGB в FBX), α = глянец
(у стекла фонаря α = 1 — свечение), AO запечён в цвет, пивот — центр основания, 1 юнит = 1 клетка, Y вверх в FBX.

## Папки
- `input/<name>_rgba.png` — RGBA-вырезки листов Flow (`Art/Source/Flow/2026-10-19_env-props/`), белый ключ `Tools/Blender/env/cutout.py`.
- `<name>/trellis2_seed42.glb` — сырой выход TRELLIS.2 (только `tree_round`).
- `export/<name>.fbx` (+ `.glb` для превью) — готовые модели.

## Происхождение

| Модель | Путь | Вход / генерация | Лицензия результата |
| --- | --- | --- | --- |
| `tree_round` | (И) image-to-3D | `input/tree1_rgba.png` → TRELLIS.2-4B (Microsoft, MIT), **официальное демо** HF Space `microsoft/TRELLIS.2` под бесплатным аккаунтом пользователя (ZeroGPU), 19.10 ≈ 14:10; разрешение 1024, seed 42, decimation target 300 000, texture 2048; GLB 297 220 треуг. с PBR-текстурой (текстура рабочая, совпадает с UV) → `Tools/Blender/env/prepare_env.py tree_round` (ремеш 0,009, децимация, цвет из текстуры + палитра 50 %, AO) | наш (MIT не ограничивает результат); оговорка про NC-зависимости демо (nvdiffrast) — `Docs/Spike-ImageTo3D.md` §2; фон удалён нашей альфой |
| `tree_tall`, `tree_wide`, `rocks_moss` | (Б) Blender-скрипт | `Tools/Blender/env/tree_puff.py` + `puff.json` (объединение примитивов → воксельный ремеш → децимация → цвет по меткам → AO); референсы формы — листы Flow `tree2`, `tree3`, `rocks` | наш |
| `house_red`, `house_blue`, `stump_hollow` | (Б) Blender-скрипт | `Tools/Blender/env/build_houses.py` + `houses.json`; референсы — листы Flow `house_red`, `house_blue`, `stump_wide` | наш |
| заборы, фонарь, цветы, грибочки, кочки, камни, жёлуди | (Б) Blender-скрипт | `Tools/Blender/env/build_props.py` + `props.json` | наш |

Квота: после одной генерации + извлечения GLB демо ответило «You've hit your daily ZeroGPU limit» (бесплатный аккаунт).
Остальные объекты — запасным путём (Б); листы Flow для них готовы как вход TRELLIS.2 на следующий день (Q-026).
