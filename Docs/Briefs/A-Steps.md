# Бриф шагов арта A.x — общие правила

Исполнитель шага — один агент Opus medium, один шаг (раздел `### A.x` в `Docs/Art-Spec.md` §9). Перед работой прочитай:
`CLAUDE.md` (§1–§3), свой раздел шага, разделы ТЗ, на которые он ссылается, конец `Docs/NightLog.md`, раздел «Заметки шагов» ниже
и `Docs/Briefs/Notes.md`. Экономь контекст: остальное ТЗ — только по ссылкам.

- Пользователь: «делай всё, чтобы было хорошо и качественно». Аудитория — дети: ярко, дружелюбно, понятно, но ничего из Brawl Stars/Supercell.
- Разрешено человеком (19.10): Unity с графикой в batchmode (без `-nographics`) для скриншотов; скачать шрифт Nunito (OFL);
  CC0-наборы (Quaternius/Kenney) и Blender — только если реально поднимают качество, с записью лицензии в `Docs/AssetLicenses.md`;
  правки GDD под ТЗ арта.
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
