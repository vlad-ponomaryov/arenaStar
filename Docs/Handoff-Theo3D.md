# Handoff — 3D Тео (2026-10-20)

## Сделано
- Референс Flow `Art/Source/Flow/2026-10-19_theo-3dref/` (001 A-поза — одобрен, 002 виды, 003 бластер).
- Модели Blender-скриптами, `Tools/Blender/fighters/`: v1 `build_theo.py` (в игре сейчас), v2 (отвергнут),
  **v3 `build_theo_v3.py` — одобрен пользователем** (после доработки 5 пунктов, 4005b40): game 3904 / show 15616 треуг.,
  текстура 1024 `theo_v3_tex.png`, IoU силуэта 0,91. Бластер — `theo_blaster.fbx` (v1, 1178 треуг.). Исходники и
  правки Unity для v3 — `Art/Source/3D/theo/source.md`. Сверка силуэта — `theo_silhouette.py`.
- Unity: FBX-путь — `FighterModelImporter`, `FighterModelSource`, `TheoRecipe`, поля `SourceModel`/`SourceProp` в
  `FighterModelBuilder`; вид стартового стрелка (id данных `pix` не переименован). План — `Docs/Briefs/A-Theo3D-unity.md`.
- 20.10: агент оркестратора ставит v3 в игру (текстура `_BaseMap`, UV, скриншоты `Tools/out/art/theo3d/v3/`) — признак
  окончания: коммит `docs: night log A.theo3d v3` в `git log`.

## Решения и отзывы пользователя — `Docs/Briefs/A-Theo3D.md` («Отзывы пользователя»), D-137/D-138.

## Открыто
- Бюджет: тело 3904 + бластер 1178 > прежних 4000 на бойца (сейчас отдельный лимит бластера 1200) — нужно решение в Decisions.
- TRELLIS.2 — квота ZeroGPU скользящая (повтор после ≈ 15:00 UTC); после одобрения v3 не обязателен.
- Переименование данных `pix` → `theo` — отдельный вопрос.
- Анимация (кости есть: Root, Body, Head, ArmR/L, LegR/L, Prop, EyeR/L, LidR/L; `IFighterAnimSink` пока только в плане),
  моргание (LidL/R), звуки Тео — следующий шаг (новая беседа).
