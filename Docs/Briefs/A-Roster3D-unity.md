# A-Roster3D-unity — установка новых героев в игру (2026-10-20, автономно)

Модели и звуки 8 героев готовы (`Docs/Briefs/A-Roster3D.md`, `Art/Source/3D/<id>/source.md`, `Assets/_Project/Audio/Fighters/<Name>/`).
Образец установки — Тео: `Docs/Briefs/A-Theo3D-unity.md` (FBX → `Art/Models/Fighters`, `FighterModelImporter`,
`TheoRecipe`, веки `UvProbe`, `Muzzle`, rest-поза `FighterBonePose`), анимация `Gameplay/View/Anim` (общая), звуки
`FighterSoundSet` + `AudioDataSeeder` (D-147).

## Шаг 1 — стартовая шестёрка (сейчас)
1. Сопоставить героев местам данных по механикам из `Docs/Roster-v4.md` (у каждого героя указано «механики X»):
   `pix`=Тео (уже), `bruno`=Ковач (танк), `chomp`/`kora`/`nova`/`vex` = Гала/Эликса/Ларк/Зип — по их описаниям.
   **id данных не переименовывать** (как с `pix`); сопоставление → `Docs/Decisions.md` (новый D-номер) + вопрос
   «переименовать id/лок-имена» в `Docs/Questions.md`. Лок-имена (`fighter.<id>.name`) — можно обновить на новые имена EN/RU,
   если это одна строка в сидере/таблице; иначе — вопрос.
2. Для каждого: FBX → `Art/Models/Fighters/<hero>_game.fbx`, `<hero>_weapon.fbx`; рецепт `Recipes/<Hero>Recipe.cs` (Scale по
   росту из source.md: Ковач ≈ 1.3× Тео, остальные ≈ Тео), rest-поза с оружием в руках, `Muzzle` у стрелков, веки, если есть
   кости LidL/R; включить в `FighterModelBuilder.Recipes()` вместо процедурных. `FighterSoundSet` для каждого.
3. `Tools/unity-method.ps1 -Method Arena.Editor.Data.DataSeeder.SeedAll`, затем `ViewAssetBuilder.BuildAll`; compile/test
   зелёные (тесты бюджета/костей должны пройти для всех FBX — тело ≤ 4000, оружие ≤ 1200).
4. Скриншоты `ArtCapture.CaptureAll -artStep roster3d` → `Tools/out/art/roster3d/` (`lineup`, `lineup_zoom2`,
   `model_<id>.png`) и, если просто, `AnimCapture` на 1–2 героях.
5. Коммиты по героям `A.roster3d: install <Name> as <id>`, отдельные от чужих правок.

## Шаг 2 — Рен, Тесс, Брумм — НЕ делать: нужны новые места в данных (способности, баланс) — вопрос человеку.

## Правила
Unity — один процесс. Не трогать `Berry.mat`, `Fog.mat`, `ProjectSettings/*`, `Branding`. Экономию не трогать.

## Отзывы пользователя
(пока нет)
