# A-Theo3D — шаг 3: FBX Тео в Unity (план)

Разбор кода 2026-10-19 (только чтение). Бриф образа — `A-Theo3D.md`; конвейер — `Docs/Spike-ImageTo3D.md` §5.

## Как сейчас устроен вид бойца
- `Scripts/Gameplay/View/FighterView.cs` — тонкий вид: `_body` (Transform с **одним** Renderer — `SetBodyMaterial` :29 меняет
  `sharedMaterial` у `_body.GetComponent<Renderer>()`: обычный / вспышка / призрак в кусте), `_ring`, `_shadow`, `_rig`.
- `FighterRig.cs` — `Bones[]` по индексу `FighterBone` (= массив костей SkinnedMeshRenderer), rest-поза, `TopHeight`
  (полоска HP, `MatchRunner.cs:412`), стили атаки/трюка, `Springs`. `FighterBone.cs` — 14 костей: Root, Body, Head, ArmL/R,
  LegL/R, Tail, Prop (масштаб 0 = скрыт), Extra, EyeL/R, LidL/R (масштаб 0 = открыты).
- **Аниматора пока нет**: ни один рантайм-код не двигает кости (`IFighterAnimSink` — только план в Art-Spec §A.2). Значит,
  FBX сейчас достаточно статичной rest-позы; ориентация костей важна на будущее.
- `ViewCatalog.cs:28` `FighterVisuals[]` (`FighterVisual.cs`: FighterId, Prefab, HeroPrefab); `FighterPrefab(id)` :224,
  `FighterHeroPrefab(id)` :234, иначе капсула `FighterViewPrefab`. Спавн — `MatchRunner.cs:401`.
- Сборка: `Editor/Art/ViewAssetBuilder.cs:389 BuildAll` → материал `FighterBody` (:400, `Arena/ToonCharacter`, белый)
  → `FighterModelBuilder.BuildAll` (:510) → `Editor/Art/Fighters/FighterModelBuilder.cs`: рецепт → один меш (вес 1 на
  кость, `SkinQuality.Bone1`), иерархия костей с **единичными поворотами** по пивотам рецепта, bindposes, `FighterRig`,
  `FighterView`, кольцо+тень (`ViewAssetBuilder.AddRingAndShadow` :1014). Пути: `Art/Meshes/Fighters/<id>[_hero].asset`,
  `Art/Prefabs/Fighters/Fighter_<id>[_hero].prefab`. Рецепты: только `Recipes/PixRecipe.cs` (blaster — группа на Prop,
  глаза/веки — группы EyeL/R, LidL/R, пружины Tail/Head).
- **Стартовый стрелок в данных — id `"pix"`** (`DataSeeder.cs:317, 612`, лок-ключ `fighter.pix.name` = Pix/Пикс). Тео
  ещё нигде в коде нет. ModelImporter/AssetPostprocessor в проекте нет.
- Шейдер `Art/Shaders/ToonCharacter.shader`: albedo = `_BaseColor × vertex color.rgb` (:112), **alpha вершины = блеск** (:113),
  контур выдавливается по `normalOS` (:143). Вершинные цвета проекта — **линейные** (`FighterPartSpec.VertexColor` :108).
- Git LFS: `*.fbx`, `*.glb`, `*.blend` уже в `.gitattributes`.

## Что выдаёт Blender и что поправить (`Tools/Blender/prepare_fighter.py`)
- Высота нормирована в **1.0** (:48), ноги на y=0, центр по XZ; фронт −Y Blender → FBX `axis_forward=-Z, axis_up=Y` (:246) →
  в Unity лицом к +Z, правая рука на +X (как у рецептов) — проверить на скриншоте. Пикс-процедурный ≈ 1.45 высоты
  (тест `TopHeight` 1.2–2.2): масштаб Тео задать в Unity (поле рецепта `Scale`), не в Blender.
- Кости: только из json (8 у Пикса: без Prop/Extra/Eye/Lid); имена = `FighterBone`; у Blender-костей свой roll.
- Цвет: `colors_type="SRGB"` (:247) → в Unity будет sRGB при линейном ожидании (Тео выйдет бледным). Решение: экспорт
  `colors_type="LINEAR"` или линеаризация в Unity; alpha = 1 → блеск везде максимум — задать блеск по палитре (таблица
  цвет→gloss в рецепте) при импорте.
- `mesh_smooth_type="FACE"`: при импорте пересчитать нормали (сглаживание 180°/60°), иначе контур рвётся на швах.
- Один меш, один материал (требование `SetBodyMaterial`). Бластер — отдельно (не в теле), вешается на Prop.

## Шаги реализации (Editor-код, без ручного YAML)
1. FBX → `Assets/_Project/Art/Models/Fighters/theo_game4k.fbx` и `theo_show20k.fbx` (LFS), исходники — `Art/Source/3D/theo/`
   + `source.md`, строка в `Docs/AssetLicenses.md`.
2. `Editor/Art/Fighters/FighterModelImporter.cs` (AssetPostprocessor, только папка `Art/Models/Fighters`): `OnPreprocessModel` —
   `materialImportMode=None`, `animationType=None`, `importAnimation=false`, `importBlendShapes=false`, `bakeAxisConversion=true`,
   `globalScale=1`, `useFileScale=true`, `importNormals=Calculate`, `normalSmoothingAngle`, `isReadable=true` (нужен
   конвертеру; итоговый меш — свой `.asset`), `meshCompression=Off`, `optimizeGameObjects=false`.
3. FBX — **только источник** меша и пивотов: рецепт получает `SourceModel` (путь FBX game/hero) + `SourceScale` + gloss-таблицу;
   процедурные группы остаются для Prop (бластер), EyeL/R, LidL/R (поверх закрашенных глазниц; если глаза нарисованы в
   модели — веки/глаза можно не строить, но кости оставить). В `FighterModelBuilder.BuildGeometry`: копия вершин/треугольников/
   цветов FBX (линеаризация, alpha=gloss, × Scale), веса FBX ремапятся по **имени кости** → индекс `FighterBone` (до 4 весов),
   затем добавляются процедурные группы. Пивоты костей — из головок FBX-костей (Root..Tail), остальные — из рецепта;
   повороты костей в префабе — единичные (как сейчас), bindposes пересчитываются — roll Blender не мешает.
   `smr.quality = SkinQuality.Bone4` (или Bone2) для FBX-моделей; Bone1 — для процедурных.
4. Рецепт `Recipes/TheoRecipe.cs` с `FighterId = "pix"` (вид стартового стрелка; переименование id/лок «Pix→Theo» —
   отдельный вопрос в `Questions.md`: затрагивает DataSeeder, лок, сохранения, контент-пак сервера) — заменяет `PixRecipe`
   в `FighterModelBuilder.Recipes()` (:34); `PixRecipe` оставить запасным. Стили: Shoot/Shoot/Roll; пружины Head (+Extra, если есть).
5. Перезапуск: `Tools/unity-method.ps1 -Method Arena.Editor.Art.ViewAssetBuilder.BuildAll` → GUID префабов `Fighter_pix*`
   сохраняются, `ViewCatalog.FighterVisuals` обновится сам.
6. Тесты (`Assets/Tests/EditMode/Art/FighterModelTests.cs`): :73 `Bone1` → «Bone1 для процедурных, Bone2/4 для FBX»;
   :91 индексы весов; :105 TopHeight 1.2–2.2; :53 hero и game одной высоты (±0.03); `Pix_IsAToySizedFox…` (:130) переписать
   под Тео или ограничить PixRecipe; новые: имена костей FBX ⊂ `FighterBone`, сумма весов ≈ 1, бюджет ≤ 4000 треуг. (game),
   вершинные цвета линейные (нет «светлее палитры»), один материал.

## Скриншоты
- `Tools/unity-method.ps1 -Graphics -Method Arena.Editor.Art.Capture.ArtCapture.CaptureAll -Extra '-artStep A.Theo3D'`
  → `Tools/out/art/A.Theo3D/`: `lineup`, `lineup_team1` (Тео-союзник/враг — проверка красного, D-137/138), `lineup_zoom2`,
  `states_zoom2` (вспышка/призрак), `model_pix.png` (лист: спереди, 3/4, сзади, сверху, игровой ракурс), `model_pix_hero.png`,
  `silhouettes64` (по `boneIndex0`, без Tail/Prop — у FBX Unity сортирует веса по убыванию, ок), `map`, protan/deutan.
- Отдельных методов «только матч» / «только превью» нет — всё в `CaptureAll` (~20 с).

## Риски
- sRGB/линейные вершинные цвета и alpha=1 (блеск) — самая вероятная причина «не похоже на концепт».
- Ось/масштаб FBX (×100, поворот −90° по X) — лечится `bakeAxisConversion` + `useFileScale`; проверить тестом высоты и листом.
- Автовеса Blender: «тянущиеся» руки/голова при будущей анимации; вес на Body у бластера-руки; без аниматора не видно.
- Контур по нормалям: швы/дыры ремеша → артефакты; полупрозрачный `FighterHidden` на неманифолдной сетке.
- Красный Тео = цвет команды врага (D-137): читаемость только по кольцу и HP — смотреть `lineup_team1`.
- Hero 20k и game 4k должны совпасть по высоте/пивотам (одна нормировка в Blender — ок, но разные пивоты костей при
  перерасчёте ray cast; брать пивоты из game-FBX для обоих).
- Unity — один процесс; ArtCapture требует `-Graphics`.
