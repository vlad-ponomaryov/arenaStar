# План этапа 1 — Подготовка

Цель этапа (GDD §3.1): проект Unity, git, структура папок (§12.1), система конфигов, автосборка Android.
**Готово, когда:** пустая сцена собирается в APK одной командой и запускается на телефоне.
Запуск на телефоне — **утром, человеком**.

Общие правила, окружение и рабочий цикл — в `CLAUDE.md`. Решения — в `Docs/Decisions.md`.
Шаги выполняются строго по порядку: S1.1 → S1.2 → S1.3.

---

## S1.1 — Создание проекта Unity, пакеты, скрипты compile/test

**Цель.** В `C:\Dev\Arena` (где уже есть `.git`, `Docs/`) появляется проект Unity 6000.3.26f1 с нужными пакетами,
который компилируется в batchmode; есть `Tools/compile.ps1`, `Tools/test.ps1`, `Tools/unity-method.ps1`.

**Файлы/папки.** `Assets/`, `Packages/manifest.json`, `Packages/packages-lock.json`, `ProjectSettings/`,
`Tools/UnityCommon.ps1`, `Tools/compile.ps1`, `Tools/test.ps1`, `Tools/unity-method.ps1`,
`Assets/_Project/Editor/Arena.Editor.asmdef`, `Assets/_Project/Editor/Setup/PackageInstaller.cs`.

**Что сделать.**
1. Убедиться, что `Unity.exe` существует (если нет — ждать по правилу из CLAUDE.md) и что Unity для проекта не запущен.
2. Создать проект: `Unity.exe -batchmode -nographics -quit -createProject C:\Dev\Arena -logFile Tools\out\create.log`.
   Шаблон URP **не** использовать (D-021). Если Unity отказывается создавать проект в непустой папке —
   создать в `C:\Dev\ArenaTmp`, перенести `Assets/`, `Packages/`, `ProjectSettings/` в `C:\Dev\Arena`, временную папку удалить.
   Если в логе ошибка лицензии — записать в `Questions.md` (Q: активировать Personal-лицензию в Hub) и остановить ночь.
3. Написать `Tools/UnityCommon.ps1` (PowerShell 5.1):
   - `Get-ArenaUnityPath` (env `ARENA_UNITY` или путь по умолчанию), `$ProjectPath = C:\Dev\Arena`, `$OutDir = Tools\out` (создать).
   - `Test-ArenaUnityRunning`: `Get-CimInstance Win32_Process -Filter "Name='Unity.exe'"`, совпадение `CommandLine` с путём проекта
     (без учёта регистра и слешей). Если процесса нет, а `Temp\UnityLockfile` есть — удалить lockfile.
   - `Invoke-ArenaUnity -Args <string[]> -LogFile <path> -TimeoutMinutes <n>`: `Start-Process -PassThru -NoNewWindow`,
     ожидание с таймаутом (`WaitForExit(ms)`), при таймауте — `Stop-Process` и код 2. Всегда добавлять
     `-batchmode -nographics -projectPath C:\Dev\Arena -buildTarget Android -logFile <LogFile>`.
   - `Get-ArenaCsErrors -LogFile`: уникальные строки `error CS\d{4}` (regex), плюс признаки `Scripts have compiler errors`.
   - `Test-ArenaLicenseError -LogFile`: строки `No valid Unity Editor license` / `License` + `error` → код 2 с понятным текстом.
4. `Tools/compile.ps1`: `-quit`, лог `Tools/out/compile.log`; вывести первые 20 ошибок CS и итог
   `COMPILE: OK, CS errors: 0` / `COMPILE: FAILED, CS errors: N`. Коды — как в CLAUDE.md.
5. `Tools/test.ps1`: `-runTests -testPlatform <EditMode> -testResults Tools/out/test-results.xml` (без `-quit`),
   опционально `-testFilter <Filter>`; лог `Tools/out/test.log`. Разобрать XML (`[xml]`, узел `test-run`: `total`, `passed`,
   `failed`, `skipped`; упавшие `test-case[@result='Failed']` — имя + первая строка `message`). 0 тестов — код 0 с
   предупреждением `TESTS: none found`. Нет XML → проверить ошибки CS → код 2.
6. `Tools/unity-method.ps1 -Method X`: `-quit -executeMethod X`, лог `Tools/out/method-<X>.log`; код 1, если Unity вернул ≠0
   или в логе `Exception` из `Arena.` / `executeMethod` failed.
7. Пакеты (D-023): `Assets/_Project/Editor/Arena.Editor.asmdef` (только Editor) + `Arena.Editor.Setup.PackageInstaller.Install`:
   `Client.AddAndRemove(add: com.unity.render-pipelines.universal, com.unity.inputsystem, com.unity.test-framework, com.unity.ugui;
   remove: com.unity.collab-proxy, com.unity.visualscripting, com.unity.multiplayer.center, com.unity.timeline — только если есть)`,
   опрос в `EditorApplication.update`, по завершении `EditorApplication.Exit(0|1)`. Запуск — **без** `-quit`
   (через `Invoke-ArenaUnity` с `-executeMethod`), версии выбирает Package Manager под редактор.
   Запасной путь: прописать версии в `Packages/manifest.json` вручную (URP — версия из
   `Editor\Data\Resources\PackageManager\BuiltInPackages\com.unity.render-pipelines.universal\package.json`, если есть).
8. Прогнать `Tools/compile.ps1` и `Tools/test.ps1` (0 тестов — ок). Проверить `.gitignore` (Library, Temp, Logs, UserSettings,
   Builds, Tools/out игнорируются; `Packages/packages-lock.json` и `ProjectSettings/` — коммитятся).

**EditMode-тесты.** Нет (тестовой сборки ещё нет) — проверяется работоспособность `test.ps1` на пустом наборе.

**Критерий готовности.** `Tools/compile.ps1` → код 0, `CS errors: 0`; `manifest.json` содержит URP, Input System,
Test Framework, uGUI; `Tools/test.ps1` отрабатывает (код 0); коммит `S1.1: create Unity project, packages and tool scripts`.

---

## S1.2 — Настройки проекта кодом, структура папок, asmdef, сцены, первые тесты

**Цель.** Проект настроен кодом (URP, Input System, Android-параметры), есть структура папок §12.1 и все asmdef,
сцены Boot/Menu/Match генерируются и стоят в Build Settings, есть зелёные smoke-тесты.

**Файлы/папки.**
- `Assets/_Project/Scripts/{Core,Bots,Data,Gameplay,Modes,UI}/` — по asmdef в каждой (`Arena.Core.asmdef` с
  `"noEngineReferences": true`, `Arena.Bots.asmdef` тоже; зависимости — таблица в CLAUDE.md §5) и по одному минимальному файлу:
  `Core/Sim/SimLimits.cs` (`TickRate = 30`, ёмкости — см. D-004), `Bots/BotsAssemblyInfo.cs` (пустой internal класс-маркер),
  `Data/GameDatabase.cs` (пустой SO), `Gameplay/Flow/BootLoader.cs`, `Modes/ModesAssemblyInfo.cs`, `UI/Menu/MenuScreen.cs`.
- `Assets/_Project/Editor/Setup/ProjectConfigurator.cs`, `Assets/_Project/Editor/Build/SceneBuilder.cs`.
- `Assets/_Project/Settings/URP/Arena_URP.asset`, `Arena_Renderer.asset` (создаются кодом).
- `Assets/_Project/Scenes/{Boot,Menu,Match}.unity` (создаются кодом).
- `Assets/Tests/EditMode/Arena.Tests.EditMode.asmdef`, `Assets/Tests/EditMode/Setup/ProjectSetupTests.cs`.

**Что сделать.**
1. `Arena.Editor.Setup.ProjectConfigurator.Apply` (идемпотентно, `-executeMethod`):
   - `PlayerSettings`: companyName `Arena`, productName `Arena`, applicationIdentifier Android `com.arena.prototype` (D-034),
     bundleVersion `0.1.0`; ориентация — авто, только landscape; цветовое пространство Linear.
   - Android: `SetScriptingBackend(NamedBuildTarget.Android, IL2CPP)`, `Android.targetArchitectures = ARM64`,
     `minSdkVersion = AndroidApiLevel26`, графические API `[Vulkan, OpenGLES3]`, `EditorUserBuildSettings.androidBuildSubtarget = ASTC`.
   - URP: `UniversalRendererData` + `UniversalRenderPipelineAsset.Create(rendererData)` → сохранить ассеты в
     `Assets/_Project/Settings/URP/`; назначить `GraphicsSettings.defaultRenderPipeline` и `QualitySettings.renderPipeline`
     для каждого уровня качества (пройти уровни через `QualitySettings.SetQualityLevel`). Мобильные настройки: HDR off,
     MSAA off, тени off (D-027 — тени-кружки), доп. источники света off.
   - Input: `activeInputHandler = 1` (только Input System) через
     `new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0])` (D-017).
   - `AssetDatabase.SaveAssets()`; вывести в лог `ARENA_CONFIG_APPLIED`.
2. `Arena.Editor.Build.SceneBuilder.BuildAll`: через `EditorSceneManager.NewScene(EmptyScene)` создать и сохранить
   - `Boot`: объект `Boot` с `BootLoader` (в `Start` грузит `Menu` через `SceneManager.LoadScene`).
   - `Menu`: камера, `EventSystem` + `InputSystemUIInputModule`, объект `MenuScreen` (в рантайме строит Canvas с
     надписью-версией и кнопкой Play → `Match`; временный текст допустим с пометкой `// TODO(S2.7): Loc`).
   - `Match`: камера (55°), направленный свет, плоскость-пол, пустой объект `MatchRoot`.
   - `EditorBuildSettings.scenes = [Boot, Menu, Match]` (все enabled). Повторный запуск полностью пересоздаёт сцены.
3. Запустить: `unity-method.ps1 -Method Arena.Editor.Setup.ProjectConfigurator.Apply`, затем `…SceneBuilder.BuildAll`.
4. Тестовая сборка `Arena.Tests.EditMode`: `includePlatforms: ["Editor"]`, `defineConstraints: ["UNITY_INCLUDE_TESTS"]`,
   `overrideReferences: true`, `precompiledReferences: ["nunit.framework.dll"]`, ссылки на `UnityEngine.TestRunner`,
   `UnityEditor.TestRunner` и все `Arena.*`.

**EditMode-тесты** (`ProjectSetupTests`): `SimLimits.TickRate == 30`; Build Settings содержит ровно Boot, Menu, Match по порядку
и файлы существуют; `GraphicsSettings.defaultRenderPipeline` — `UniversalRenderPipelineAsset`; scripting backend Android —
IL2CPP, архитектура — ARM64; `activeInputHandler == 1`; сборка `Arena.Core` не ссылается на `UnityEngine`
(проверка `typeof(SimLimits).Assembly.GetReferencedAssemblies()`).

**Критерий готовности.** compile — 0 ошибок; test — ≥6 passed, 0 failed; сцены и URP-ассеты в git (с `.meta`);
коммит `S1.2: configure project via code, add asmdefs and scene builder`.

---

## S1.3 — Android-сборка одной командой, GameCI, итог этапа

**Цель.** `Tools/build-android.ps1` собирает APK из сцен Build Settings; есть workflow для GameCI (без пуша);
заполнен итог этапа 1.

**Файлы/папки.** `Assets/_Project/Editor/Build/BuildScript.cs`, `Tools/build-android.ps1`,
`.github/workflows/android-build.yml`, `Builds/Android/` (в .gitignore), раздел «Итог этапа» в этом файле.

**Что сделать.**
1. `Arena.Editor.Build.BuildScript.BuildAndroid()`:
   - аргументы командной строки: `-arenaDevelopment` (Development Build + `allowDebugging`, `connectWithProfiler` выкл.),
     `-arenaOutput <path>` (по умолчанию `Builds/Android/Arena.apk`, для dev — `Arena-dev.apk`);
   - вызвать `ProjectConfigurator.Apply()` (гарантия настроек), `EditorUserBuildSettings.buildAppBundle = false`;
   - сцены — из `EditorBuildSettings.scenes` (enabled); `BuildPipeline.BuildPlayer(...)`;
   - напечатать `ARENA_BUILD_RESULT result=<Succeeded|Failed> size=<bytes> seconds=<n> path=<...>`;
   - `EditorApplication.Exit(0|1)`. Keystore — отладочный по умолчанию (D-034).
2. `Tools/build-android.ps1 [-Development] [-Install] [-TimeoutMinutes 60]`: `-quit -executeMethod
   Arena.Editor.Build.BuildScript.BuildAndroid`, лог `Tools/out/build-android.log`; разбор строки `ARENA_BUILD_RESULT`,
   проверка существования APK; итог `BUILD: OK Builds/Android/Arena.apk (23.4 MB, 312 s)`. `-Install`: `adb.exe` из
   `Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools`, `adb devices` → если есть устройство, `adb install -r <apk>`
   и `adb shell monkey -p com.arena.prototype 1`; если нет — сообщение, код 0. **Ночью `-Install` не запускать.**
3. Собрать APK (первая IL2CPP-сборка может идти 10–25 мин). Если упала — разобрать лог (SDK/NDK/JDK должны быть встроенные;
   при необходимости выставить `EditorPrefs` `JdkUseEmbedded/SdkUseEmbedded/NdkUseEmbedded = true` в `ProjectConfigurator`).
4. `.github/workflows/android-build.yml`: `game-ci/unity-test-runner@v4` (EditMode) и `game-ci/unity-builder@v4`
   (`targetPlatform: Android`, `buildMethod: Arena.Editor.Build.BuildScript.BuildAndroid`, `unityVersion: 6000.3.26f1`),
   кэш `Library`, секреты `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`, артефакт APK. Не пушить; вопрос про секреты — в Questions.md.
5. Заполнить «Итог этапа» ниже.

**EditMode-тесты** (`BuildScriptTests`): парсинг аргументов (`-arenaDevelopment`, `-arenaOutput`) вынесен в чистый метод
`BuildScript.ParseArgs(string[])` и проверен; путь по умолчанию — `Builds/Android/Arena.apk`; список сцен для сборки = 3.

**Критерий готовности.** `Tools/build-android.ps1` → код 0, APK существует (размер в NightLog); тесты зелёные;
коммит `S1.3: add Android build script and CI workflow`.

---

## Итог этапа (заполняет агент S1.3)

| Критерий | Статус | Как проверено |
| --- | --- | --- |
| Проект Unity + git + структура папок §12.1 | ok | Unity 6000.3.26f1, asmdef Core/Bots/Data/Gameplay/Modes/UI/Editor/Tests; `Tools/compile.ps1` → 0 CS; `ProjectSetupTests` |
| Система конфигов (SO + сидер) | частично (по плану) | каркас `GameDatabase`; наполнение — S2.1 |
| APK собирается одной командой | ok | `Tools/build-android.ps1` → `BUILD: OK Builds/Android/Arena.apk (31.7 MB, 634 s)`, IL2CPP ARM64, отладочный keystore |
| Тесты EditMode | ok | `Tools/test.ps1` → passed 12, failed 0 (`ProjectSetupTests`, `BuildScriptTests`) |
| CI (GameCI) | только конфигурация | `.github/workflows/android-build.yml`; не запускался — нет remote и секретов (Q-007) |
| APK запускается на телефоне | **утром, человеком** | `Tools/build-android.ps1 -Install` при подключённом телефоне (или `adb install -r Builds/Android/Arena.apk`) |

Не проверено ночью: запуск APK на устройстве (Boot → Menu → Play → Match), ориентация landscape, Vulkan/GLES3 на конкретном
телефоне, Development-сборка (`-Development`) — собирается тем же методом, но отдельно не прогонялась.
