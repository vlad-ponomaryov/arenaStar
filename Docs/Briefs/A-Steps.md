# Бриф шагов арта A.x — общие правила

Исполнитель шага — один агент Opus medium, один шаг (раздел `### A.x` в `Docs/Art-Spec.md` §9). Перед работой прочитай:
`CLAUDE.md` (§1–§3), свой раздел шага, разделы ТЗ, на которые он ссылается, конец `Docs/NightLog.md`, раздел «Заметки шагов» ниже
и `Docs/Briefs/Notes.md`. Экономь контекст: остальное ТЗ — только по ссылкам.

- Пользователь: «делай всё, чтобы было хорошо и качественно». Аудитория — дети: ярко, дружелюбно, понятно, но ничего из Brawl Stars/Supercell.
- Разрешено человеком (19.10): Unity с графикой в batchmode (без `-nographics`) для скриншотов; скачать шрифт Nunito (OFL);
  CC0-наборы (Quaternius/Kenney) и Blender — только если реально поднимают качество, с записью лицензии в `Docs/AssetLicenses.md`;
  правки GDD под ТЗ арта.
- Ориентиры пользователя (персонажи, арены, HUD): `C:\Users\Computer\AppData\Roaming\Claude\side-session-notes\local_906d5e56-6c85-4e57-98a5-5eb029403c09\refs\README.md`
  (разбор «почему круто» + PNG). Только изучение приёмов: в репозиторий не класть, Flow как образец не давать, ничего не копировать.
  Читать только шагам персонажей, окружения и HUD.
- Цикл: compile → `Tools/test.ps1` EditMode + PlayMode (+ `server-test.ps1`, если трогал сервер/общий код сети) → контрольные
  скриншоты, если требует шаг (смотри их сам, Read по PNG) → commit (только свои файлы; `git commit -- <пути>` или аккуратный `git add`;
  не коммитить `ProjectSettings/QualitySettings.asset` после PlayMode, `GraphicsSettings.asset` и неотслеживаемые
  `ProjectSettings/PackageManagerSettings.asset`, `URPProjectSettings.asset`, если шаг их сознательно не меняет) → строка в
  `Docs/NightLog.md` + коммит `docs: night log A.x`. Последняя строка каждого коммита: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Unity — один процесс на проект; параллельно может работать агент без Unity (Flow в браузере). Если Unity занят (код 3) — подожди и повтори.
- Новые решения — `Docs/Decisions.md`, номер = последний в файле + 1 (перечитай конец файла прямо перед записью). Вопросы — `Docs/Questions.md`.
- Не используй `git stash` и `git checkout`/`reset` чужих файлов: параллельно может работать другой агент. Для проверки «тест падал без фикса» — временно верни код правкой, не stash.
- 3 неудачи подряд на пункте → Questions.md, откатить незаконченное, проект компилируется, тесты зелёные.
- В конце допиши в «Заметки шагов» ниже 2–5 строк для следующих шагов (ловушки, где что лежит, что отложено и куда).
- Отчёт оркестратору — до 150 слов: что сделано, хэши, compile/tests, путь к скриншотам, что не сделано.

## Заметки шагов
- (A.0) Скриншоты: `Tools/unity-method.ps1 -Graphics -Method Arena.Editor.Art.Capture.ArtCapture.CaptureAll -Extra '-artStep A.x'` (≈ 20 с) → `Tools/out/art/A.x/`. Стенд — `Editor/Art/Capture/CaptureStage.cs` (раскладка карты, `ResolvePrefab` — A.1a переключает на `FighterVisuals` по id); свет и камера — из сцены Match, поэтому `MatchLightRig` (A.1a) нужно применить и на стенде (в рантайме он не сработает в edit mode).
- (A.0) Рендер с графикой и PlayMode переписывают `Art/Materials/BerryBox.mat`, `BlobShadow.mat` (служебные поля URP) и `QualitySettings.asset` — `git checkout --` их, если шаг не меняет материалы сознательно (после пересборки `ViewAssetBuilder` смотри diff).
- (A.0) На «до»: команда 1 видит бойцов в тени (lineup_team1), при дейтеранопии зелёный куст сливается с полом (map_deutan) — ориентиры для A.1a/A.3a. `ShaderSanityTests` требует, чтобы любой `.shader` в `Assets/_Project` назывался `Arena/*`.
- (A.9a) Flow: исходники и отбор — `Art/Source/Flow/README.md`; скачивание — ПКМ по карточке → «Скачать» → «1K» (JPEG в `.playwright-mcp/`, 2K — апскейл, 4K — платно), конвертировать в PNG; режим агента Flow сам подставляет прошлые листы как референсы — писать «do not use the character references».
- (A.9a) Для A.9b: выбранные варианты — в README (трава 002, доски 002, корзина 001, фон меню 001, декали 4×4 на белом); Бруно на листах не «игрушечный» — пропорции брать из ТЗ.
- (A.1a) Toon-материалы бойцов пересоздаются из дефолтов шейдера при каждом `ViewAssetBuilder.BuildAll` (`SaveToonMaterial`): числа вида (контур, ступень, блик) правь в `Properties` шейдера, затем перезапусти билдер — иначе материал хранит старые значения. Новые меши под `Arena/*` — только с вершинными цветами (α = глянец).
- (A.1a) Свет: `MatchLightRig.Apply` вызывается в `MatchRunner.StartMatch` и в `ArtCapture.PlaceGameplay`; `SceneBuilder.ApplyMatchEnvironment` — Trilight (фон камеры по-прежнему тёмно-синий — A.3a). `ArtCapture` пишет ещё `states_zoom2.png` (боец 0 — вспышка, 1 — призрак).
- (A.1a) A.1b: заполни `catalog.FighterVisuals` в `ViewAssetBuilder.BuildAll` (сейчас там `Array.Empty`); `MatchRunner`/`CaptureStage` уже берут префаб по id. Нос-кубик фолбэка — URP Unlit, просвечивает сквозь призрака (у моделей носа не будет).
- (A.1a) После `SceneBuilder.BuildAll` Boot/Menu получают новые fileID без смысловых изменений — `git checkout --` их, если шаг их не меняет.
- (A.1b) Модель = рецепт `Editor/Art/Fighters/Recipes/<Name>Recipe.cs` + строка в `FighterModelBuilder.Recipes()`; пересборка —
  `ViewAssetBuilder.BuildAll` (в логе: треугольники и шаг сетки по группам). Новый `ArtCapture` пишет `model_<id>.png` (спереди, 3/4,
  сзади, сверху, игровой угол) — сверяй с листом Flow. Части задаются в модельном пространстве (y вверх, лицом +Z, +X — правая рука).
- (A.1b) Ловушки: деталь тоньше шага лепки (≈ 0,06) пропадает — делай её `Parts` или толще; «краска» (`AsPaint`) — дешёвый способ
  пятен; бюджеты групп проверяет `FighterModelTests` (голова ≤ 1400 вместе с `Parts` на кости Head). Hex — sRGB, сборщик сам переводит в linear.
- (A.1b) Для A.2a: `FighterRig` хранит кости (индекс = `FighterBone`), позу покоя, `TopHeight`, стили и пружины (у Пикса — Tail, Head);
  веки `LidL/LidR` в покое масштаб 0. Полоска HP пока не читает `TopHeight`. Силуэты 64 px нормируются по рамке с хвостом и бластером — Пикс там мельче капсул.
- (A.1b2) `SdfSculpt.Build` = лепка в 8× бюджета → `MeshDecimator.Simplify` → проекция на SDF (D-121): бюджет группы заполняется
  ровно, шаг в логе — мелкий. Тонкие детали теперь живут до ≈ 0,02, но после упрощения их треугольники всё равно делят бюджет группы.
  Конечности лучше лепить (`FighterGroupSpec.Sculpt`, blend 0,04–0,05), а не `Pieces` — у восьмигранных конусов видны грани.
- (A.1b2) Рубленый силуэт чаще от рецепта, чем от сетки: основание уха шире головы на этой высоте даёт прямую стенку «куб» — держи
  основание уха внутри контура головы. Сравнение до/после — `Tools/out/art/A.1b2/compare_model_pix.png`. Силуэты 64 px теперь по телу.
- (A.1b2) Hero-LOD (D-122): `ViewAssetBuilder.BuildAll` строит и `Fighter_<id>_hero` (≈ 23k треуг.); меню/выбор/итоги (A.6/A.7) берут
  `catalog.FighterHeroPrefab(id)`. Бюджеты `Pieces`-групп в hero только информативны (лог «900/780» — не ошибка), тест проверяет 15–30k.
- (A.3a) Окружение: `Arena/ToonEnv` (мировые XZ-UV, `_MapContrast`, макро-сэмпл) — для кустов/воды/ящиков A.3b бери его же или
  include; меши — через `MapMeshBuilder` (рантайм, вершинные цвета линейные, α = 0), параметры — `MapMeshStyle` (`ViewCatalog.MapStyle`).
  Новые Flow-текстуры — `FlowImageImporter.ImportTileable` (доски 002 и корзина 001 для A.3b — тем же вызовом).
- (A.3a) Ловушка стенда: в `lineup.png` (1×) материалы URP Lit (куст, вода) иногда рисуются коричневыми, в `lineup_zoom2`/`map` — верно;
  у `Arena/*` такого нет. A.3b переводит их на `Arena/*` — проблема уйдёт; сравнивай по zoom2.
- (A.3a) Луг — 14 клеток вокруг карты (`FloorPrefab` теперь луг, меш в `MapView`); декор A.4 ставь на нём, вне поля. Край карты у стен —
  открытые боковины (тест `SharedSides_AreSkipped_MapEdgeSides_AreBuilt`).- (A.3b) Пропсы карты — `Editor/Art/MapProps`: `EnvMeshBuilder` (куст 3 варианта ≤ 250, ящик, корзина; `uv0.x = 1` — вершина без
  рисунка в `Arena/ToonEnv`), `EnvTextureBaker` (плиты верха стен, листва, рябь — **GLSL-smoothstep свой**: `Mathf.SmoothStep`
  интерполирует значения, а не порог). Меши — ассеты `Art/Meshes/Env`, пересоздаются `ViewAssetBuilder.BuildAll`.
- (A.3b) Тени стен — `MapShadowMask` (глобал) + `ArenaEnv.hlsl`; материал получает их через `_ReceiveMapShadow` (пол, луг; вода — всегда).
  Направление — `MapMeshStyle.ShadowFall` в кадре камеры (`MatchLightRig.Apply`), не от света. Декор A.4 на луге получит тени, если
  поставить его в маску (`MapShadowMask.CasterValue`) — иначе только тень-кружок.
- (A.3b) Вода утоплена (`ViewCatalog.WaterHeight` < 0): пола под водой нет, берега — `BuildBanks` материалом стены. Пространство имён
  `Arena.Editor.Art.Environment` нельзя: ломает `System.Environment` в соседних файлах. До/после — `Tools/out/art/A.3b/compare_*.png`.
- (A.3b, разбор «красочности» по ориентиру пользователя — для A.3c/A.4, приёмы жанра, не копия) Сделано в A.3b: пол теплее
  (`FloorTint` к жёлтому), кусты к бирюзе (`#1FB06A`/`#105E40`) — контраст есть, но всё ещё «зелёное на зелёном». **A.3c:** пол арены —
  тёплый (песок/земля/плиточная дорожка Flow) с рисунком (плитки, трещины, камешки, тропинки, декали), трава — только пятнами и
  на луге; кусты — высокие плотные «пучки» с ярким верхом и тёмным низом; препятствия нескольких типов (каменный блок с узором
  верха — есть, ящик — есть; добавить бочку, пень, заборчик, валун) — вариант по клетке как у кустов; у стен тёмная полоса у земли
  уже есть, контактная тень — `MapShadowMask`. Камера 55° — в диапазоне жанра (D-125). **A.4:** рамка арены — высокая тематическая
  декорация (деревья-шары, скалы, пни, фонарики, заборы) выше поля. Свет: high-key, тёплый; bloom/грейдинг URP — только после замера
  A.10 (ТЗ §7 запрещает пост-эффекты). Щепки/кляксы попаданий — A.5.
- (A.3c) Целевой концепт — `Art/Source/Flow/2026-10-19_arena-target/` (001 — ракурс/кусты, 004 — препятствия; prompt.md). Скриншоты
  `arena.png` / `arena_base.png` (реальная карта по умолчанию, без бойцов) пишет `ArtCapture.SaveArena`; сравнения — `Tools/out/art/A.3c/compare_*.png`.
  Кусты: `EnvMeshBuilder.BuildBush(mask, variant)`, `ViewCatalog.BushVariants[mask * BushVariantsPerMask + v]`, `MapView.BushMask(i)`
  для тестов (после статического батчинга `sharedMesh` уже общий — не проверяй по нему). Пропы стен: `ViewCatalog.ObstacleProps`
  (бочка, пень, валун) для групп ≤ `ObstaclePropMaxCells`; добавить тип = меш в `BuildObstacleProps` (цикл подхватит сам).
- (A.3c) Ловушки: PowerShell-переменные регистронезависимы (`$w` = `$W`); here-string замены в CRLF-файлах молча не срабатывают — проверяй
  счётчиком. `ViewAssetBuilder.cs` и `ViewCatalog.*` правит и A.6 (шрифты, `SeedUi`) — коммить только свои ханки (`git apply --cached`).
- (A.3c, разбор жанра по скриншотам из сети — только приёмы, ничего не копировали) Красочность у лучших игр жанра даёт: (1) **разделение по
  яркости/теплу** — пол светлый и тёплый (песок, плитка), укрытия тёмные и холоднее (бирюза), стены светлый верх / тёмные бока;
  (2) **группировка травы** — трава пятнами и массивами, не равномерным шумом (иначе «зелёное на зелёном»); (3) **контактная тень/AO**
  под каждым объектом и тёмный низ объёмов; (4) **крупный рисунок верхних граней** (плиты, кольца пня, крышки бочек) — сверху видно
  в основном верх; (5) **богатая рамка** выше поля — она даёт «картинку»; (6) умеренный общий контраст — бойцы самые контрастные.
  У нас 1–4 сделаны в A.3c; осталось: **A.4 — рамка** (деревья-шары, домики-грибы, фонарики, заборы, цветы на луге — главный разрыв с
  концептом), резной лист на верхах каменных блоков (сейчас мох), заборчики-штакетник как тип пропа на 1 клетку, кусты — ещё
  насыщеннее и с отдельными высокими травинками по краю (бюджет ≈ 60 треуг. свободен). Пост-эффекты — только после замера A.10.
- (A.6) UI: стиль/спрайты — `ViewAssetBuilder.BuildUi` (только UiStyle, спрайты `Art/UI/Sprites`, шрифт каталога, ≈ 17 с); значения —
  `SeedUiStyle`, формы — `UiSpriteBaker`. Кнопки красить только `UiFactory.SetButtonColor` (лицо, кромка, контраст подписи), не
  `targetGraphic.color`. Панели — `UiFactory.CreatePanel` (вертикальный layout + ContentSizeFitter), ряды с подписью —
  `UiFactory.AlignRowLeft`. Табло/стики/HP (A.7a/A.7b) пока старые — бери те же спрайты/шрифт.
- (A.6) Скриншоты UI: `$env:ARENA_ART_STEP='A.x'; Tools/test.ps1 -Platform PlayMode -Category Capture -Graphics` → `Tools/out/art/A.x/`
  (`UiCaptureTests`). Overlay-канвас в RT не рисуется, стек камер URP render request игнорирует, `WaitForEndOfFrame` в batchmode
  нет — поэтому UI снимается отдельной камерой на чёрном/белом и матируется на 3D-кадр; A.8 может добавить свои кадры туда же.
- (A.6) Шрифт-ассет TTF содержит подматериал «Font Material» (GUI/Text Shader) — `ShaderSanityTests` пропускает `TrueTypeFontImporter`.
- (A.3d) Сравнение с концептом: `ArtCapture` → `env_close.png` (витрина 9×7, `Editor/Art/Capture/EnvShowcase.cs`, раскладка там же),
  `env_stats.txt`, `arena_fov50/arena_fov30.png`; затем `python Tools/art-compare.py Tools/out/art/A.x --before Tools/out/art/A.y` →
  `compare_env_close.png`, `compare_arena.png`, `camera_ab.png`, `env_report.txt` (ΔE к целям брифа §4). Точки замера — в `EnvShowcase.WriteStats`
  (сдвинешь стены/кусты витрины — поправь точки). Луг `#73A44E` vs `#5BB56B` — единственный промах, это A.4b.
- (A.3d) Куст = `EnvMeshBuilder.BushBladeLayout` (6×8 листьев, сетка сквозная) → `BuildBush`; tangent.xyz = нормаль листа, без неё
  `Arena/Bush` (Cull Off) освещает куст неверно — новые меши под этот шейдер обязаны иметь tangents. Бюджет занят ровно (48×5 + ядро ≤ 10 = 250):
  добавить листья можно только за счёт сегментов. Цвета — константы `Bush*` в EnvMeshBuilder, `_MassNormal` в шейдере.
- (A.3d) Пол — `EnvTextureBaker.BakeSand` (абсолютные цвета, `_BaseNormalize` 0, `FloorTint` подгоняет к `#F9D28A`); `FloorSandTexturePath`
  теперь `Art/Textures/Env/Sand.png`. Мох с `WallTop` снят, плиты верха остались — лист вырезом в A.3f. Тени/AO ещё лиловые — A.3e.
- (A.3d) Ловушки: python на Windows пишет CRLF — править файлы скриптом с `newline=''`; параллельный агент правит `DataSeeder.cs`,
  `ViewAssetBuilder.cs`, `Decisions.md` — свои ханки в индекс через `git hash-object -w --stdin --path` + `git update-index --cacheinfo`.
- (A.4a) Модели окружения — `Tools/Blender/env/`: `build_props.py`+`props.json` (15 мелких пропов, ≈ 8 с), `tree_puff.py`+`puff.json`
  (деревья/валуны из примитивов), `build_houses.py`+`houses.json` (домики-грибы, дупло), `prepare_env.py`+`env_models.json` (GLB
  TRELLIS.2 → ремеш, цвет из текстуры, палитра, AO), общее — `envcommon.py`; запуск `& ./Tools/blender.ps1 -Script <py> [-ScriptArgs @(name)]`.
  Выход — `Art/Source/3D/env/export/<name>.fbx` (цвет `Col` в углах граней, sRGB, α = глянец, у стекла фонаря α = 1; Z-up в Blender → Y-up FBX),
  лист `render_sheet.py`+`sheet.json` → `Tools/out/art/A.4a/models/sheet_vs_002.png`; FBX в `Assets` переносит A.4b (жёлуди/дупло — A.4c).
- (A.4a) TRELLIS.2: вход — своя RGBA-вырезка (`env/cutout.py`, белый ключ с заливкой от края), демо в iframe страницы huggingface.co
  (квота считается по аккаунту), загрузка `setInputFiles` в `input[type=file]` фрейма, GLB — ссылка `a[download]` после Extract GLB.
  **Бесплатный аккаунт: ≈ 1 генерация + извлечение в сутки** (Q-026). Текстура официального демо рабочая (в отличие от копии `Upsampler`).
- (A.4a) Для A.4b: мелкие пропы в реальном масштабе мельче концепта (цветок 0,35, грибочек 0,3) — масштабируй экземпляры ×1,3–1,6 при
  расстановке; кочки `tuft_*` имеют α = глянец 0,1 (если пойдут на `Arena/Bush`, α нужно = вес качания); дупло `stump_hollow` — диск, не сквозная дыра.
- (A.3e) Тени/AO — `MapShadowMask` (RG8, 8 тексел/клетку): R = высота по форме отпечатка (круг — клетки из `propCells` и корзина), G = контактный AO;
  параметры — `MapMeshStyle` (`ShadowBlur`, `ContactAo*`, `Footprint*`, `PropShadowHeight`, `Grade`). Новые модели A.3f: если блок станет
  уже клетки — поправь `FootprintInset`; декор рамки A.4b получит AO, только если его отпечаток попадёт в маску (сейчас только клетки карты).
- (A.3e) Детали пола — `FloorDecorPlanner` (сид = хеш тайлов, двойник на 180°, только `TileType.Floor`), стиль — `ViewCatalog.DecorStyle`
  (сид `ViewAssetBuilder.DecorStyle()`); бюджет 5000 съедают в основном камни (24 треуг.) — новые пути добавлять за счёт `PathStep`/`PathPairChance`.
- (A.3e) Тёплые множители (`_MapShadowColor`, `_MapAoColor`, `_ShadeColor`, `_StepSoftness`, `_SideGradient`) — дефолты `ToonEnv.shader`
  (материалы пересоздаются из них). Куст — 5×6 листьев (≈ 160 треуг.), по отзыву «рябит»; блоки/ящик/вода не трогались — это A.3f/A.4c.
- (A.3e) Ловушка: параллельная сессия F3 закоммитила рабочие копии `MapShadowMask.cs`/`MapMeshStyle.cs` в 862ce37 — коммитьте свои файлы
  явными путями сразу после правки или предупреждайте соседей. `env_stats.txt` теперь пишет `shadow_edge` (ширина края тени в клетках, поле lum).
- (A.3f) Стены = `ViewCatalog.WallBlockVariants` (меши `EnvMeshBuilder.BuildStoneBlock`, `AddRoundedBlock` — скруглённый куб из колец с гладкими
  нормалями, годится и для других пропов), `MapView.PlaceWallBlocks` — по объекту на клетку (`Block_c_r`, материал `ObstacleProp`), пол под стенами есть;
  пустой массив → старый `BuildBlocks`. Пруд — `MapMeshBuilder.BuildPond` (вода кольцами + `PondRim` + `PondFill` материалом пола), параметры `MapMeshStyle.Pond*`;
  старые `BuildSheet/BuildBanks` остались запасными (тесты). Путь — C#, не Blender/FBX: стиль совпал с бочкой, импорт FBX всё ещё не проверен (A.4b).
- (A.3f) Листы элементов `Art/Source/Flow/2026-10-19_env-elements/` (block, crate, stump, boulder…): блок и ящик сверены; **пень и валун ещё старые** —
  следующему шагу переделать по `stump_01`/`boulder_01` (корни-лапы, спираль, гладкий валун со мхом). `shadow_edge` 0,22 < 0,25 после снижения стен.
- (A.3f) Ловушка: номера решений сталкиваются с параллельной сессией (D-131 занят F4 → A.3f = D-132) — перечитывай конец Decisions.md прямо перед коммитом.
- (A-EnvElements) Тема биома — «солнечная песчаная поляна»: за краем тот же песок с градиентом темнее, бордюр из песчаника, рамка лёгкая (≤ 15k треуг., 1 модель дерева + куст-шар) — `Docs/Briefs/A-EnvElements.md` §1–§2, сцена `env-elements/theme_02.png`.
  Выбранные эталоны — таблица в `Art/Source/Flow/2026-10-19_env-elements/prompt.md`: валун — **`boulder_04`** (не `_01`), ящик — цвет `crate_02`, пруд — `pond_round_02`/`pond_long_03` (числа `MapMeshStyle` — каталог §3). TRELLIS.2: пень → дупло → (валун) → (дерево).
- (A.7a) HUD боя: спрайты — `UiSpriteBaker` (`HudStickBase/HudKnob/HudPetal/HudGlow`), иконки — `IconBaker` (слои SDF; новая иконка = массив
  `Layer` + поле `UiStyle`), всё пересобирает `ViewAssetBuilder.BuildUi`. Цветок ульты — `MatchHudControls.UltPetals` (дети зоны стика, не основы:
  пульс основы их не масштабирует), заливка — `AbilityHudView.ShowPetals`. Иконки ульт/трюков по бойцам (рендер 3D-пропсов) — ещё общие (D-134).
- (A.7a) Мировые: полоска — `HealthBarView` (`_chargeSockets`, `SetTopHeight`, `SetOwn`), текстуры `Art/Textures/Hud*.png` кодом
  (`BuildHudTexture`), кольцо команды — квад (материалы Team* прозрачные с текстурой). Скриншоты — `UiCaptureTests.Capture_MatchHud`
  (`match_hud`, `match_hud_phone`, `match_hud_ult`). Ловушка: `[IO.File]::WriteAllText` с относительным путём в PowerShell пишет в cwd процесса.
- (A.7b) Сделано частично: таблички счёта/таймер-доска (`UiFactory.CreateTablet`) и доски «Последнего зверя»; **лента побед не сделана**
  (нужны портреты A.8 + событие «кто добил»), скриншота режима «Последний зверь» нет — добавить кадр в `UiCaptureTests`.
