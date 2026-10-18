# Arena — правила для агентов Claude Code

Мобильный арена-шутер 3×3 на Unity 6 (вид сверху, бойцы-животные). Главный источник правды — `Docs/GDD.md`.
Архитектурные решения и их причины — `Docs/Decisions.md` (читать перед любым шагом). Текущие планы —
`Docs/Plan-Stage1.md`, `Docs/Plan-Stage2.md`. Документы — на русском; код, идентификаторы, пути, коммиты — на английском.

## 1. Обязательные правила (GDD §2, §12.2–12.3) — важнее всего остального

- **Оригинальный контент.** Никаких имён, персонажей, иконок, текстов, звуков, шрифтов, раскладок экранов и карт из
  Brawl Stars и других игр Supercell — даже как заглушек. Механики жанра (3×3, вид сверху, два стика) — можно.
- Временный арт — только примитивы (капсулы, кубы) или ассеты с лицензией на коммерческое использование.
  Каждый ассет → строка в `Docs/AssetLicenses.md`.
- **Возраст и приватность.** Нет свободного текстового чата и ввода текста, нет внешних ссылок, рекламы и сбора
  данных. Общение — только эмодзи и метки (после MVP-прототипа).
- **Честная монетизация.** Никаких случайных наград за деньги. Экономику в коде не менять без вопроса человеку.
- Не подключать платные ассеты и сервисы.
- Работать строго по этапам GDD §3. В конце этапа — пройти его критерии готовности: что проверено, что нет,
  что требует проверки на телефоне (раздел «Итог этапа» в `Docs/Plan-StageN.md`).
- **На эту ночь** правило «дождись подтверждения плана» отменено: планы Stage1/Stage2 уже утверждены, выполняй.
- Сомнение → решить разумно, записать решение в `Docs/Decisions.md`, вопрос в `Docs/Questions.md`, продолжать.

### Правила кода (GDD §12.2)
- C#, пространства имён `Arena.*`, один класс/struct/enum — один файл (имя файла = имя типа).
- Бой пишется сразу под сеть: ввод — структура `PlayerInput`, симуляция по фиксированным тикам 30 Гц,
  состояние отдельно от отображения (Fusion подключится на этапе 3 без переписывания, см. D-001).
- MonoBehaviour — тонкий слой отображения/ввода. Логика — в `Arena.Core` / `Arena.Bots` и покрыта EditMode-тестами.
- Ни одного «магического числа» в логике: параметры — в ScriptableObject-конфигах (`Assets/_Project/Data`).
  Сиды значений допустимы только в Editor-коде (`DataSeeder`). Исключение — ёмкости массивов в `SimLimits` (D-004).
- Способности — из данных + эффекты-компоненты. Новый боец = новые данные, не новый класс.
- В бою ноль аллокаций на кадр/тик: пулы, предвыделенные массивы, кэш строк чисел, без LINQ/лямбд с захватом в горячем пути.
- Все тексты UI — через ключи локализации (`Loc.Get("key")`), EN и RU.

## 2. Окружение

- Unity 6.3 LTS `6000.3.26f1`: `C:\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe` (+ Android Build Support, SDK/NDK, OpenJDK).
  Если Unity.exe нет — установка ещё идёт: ждать (проверка раз в 5 мин, до 60 мин), затем вопрос в Questions.md.
- **Редактор с GUI не открывать.** Только batchmode: `-batchmode -nographics -projectPath C:\Dev\Arena -buildTarget Android -logFile <файл>`.
  Компиляция: `-quit`. Тесты: `-runTests -testPlatform EditMode -testResults <xml>` (с `-runTests` **НЕ** ставить `-quit`).
  Прочее: `-executeMethod Namespace.Class.Method`. Активная платформа проекта всегда Android (D-020).
- **Только один процесс Unity на проект.** Перед запуском проверить, что нет `Unity.exe` с этим `-projectPath`;
  `Temp/UnityLockfile` при отсутствии процесса можно удалить. Скрипты `Tools/*.ps1` делают это сами.
- Шелл — Windows PowerShell 5.1 (нет `&&`, `?:`, `??`). Скрипты в `Tools/` должны работать в 5.1.
- Сцены, префабы, ScriptableObject-ассеты, материалы и настройки проекта создаются **кодом** (Editor-скрипты через
  `-executeMethod`). YAML сцен/префабов/`ProjectSettings/*.asset` руками не писать. `Packages/manifest.json` (JSON) — можно.
- Photon Fusion 2 SDK нет и не ставим (нужна регистрация). Бой — своя симуляция (D-001).

### Скрипты Tools (создаются в S1.1/S1.3, дальше пользуются все)
Вывод в `Tools/out/` (в .gitignore). Последняя строка консоли — краткий итог.

| Скрипт | Что делает | Коды возврата |
| --- | --- | --- |
| `Tools/compile.ps1 [-TimeoutMinutes 15]` | Открыть проект с `-quit`, собрать скрипты. Итог: `COMPILE: OK, CS errors: 0` или список первых 20 `error CS####` | 0 ок · 1 ошибки CS · 2 сбой Unity (лицензия, таймаут, крэш) · 3 Unity уже запущен |
| `Tools/test.ps1 [-Filter <regex>] [-Platform EditMode\|PlayMode] [-TimeoutMinutes 20]` | Прогнать тесты, разобрать `Tools/out/test-results.xml`. Итог: `TESTS: passed N, failed M, skipped K` + имена и сообщения упавших | 0 все зелёные · 1 есть падения · 2 ошибки компиляции/нет XML · 3 Unity занят |
| `Tools/unity-method.ps1 -Method <Ns.Class.Method> [-TimeoutMinutes 15]` | Запустить Editor-метод (`-executeMethod … -quit`): сидер данных, сборщик сцен/префабов, настройки | 0 ок · 1 метод упал/исключение в логе · 2 сбой Unity · 3 Unity занят |
| `Tools/build-android.ps1 [-Development] [-Install] [-TimeoutMinutes 60]` | `Arena.Editor.Build.BuildScript.BuildAndroid` → `Builds/Android/Arena.apk` (`Arena-dev.apk` с `-Development`). Итог: путь, размер, время. `-Install` — `adb install -r`, если подключён телефон (ночью не использовать) | 0 ок · 1 сборка упала · 2 сбой Unity · 3 Unity занят |

Общая логика (поиск процесса, запуск Unity с таймаутом, разбор лога) — в `Tools/UnityCommon.ps1` (dot-source).
Путь к Unity можно переопределить переменной окружения `ARENA_UNITY`.

## 3. Рабочий цикл шага

1. Прочитать шаг в `Docs/Plan-StageN.md`, последние строки `Docs/NightLog.md`, `Docs/Decisions.md`, `Docs/Questions.md`.
2. Реализовать. Если менялись данные/сцены/префабы — обновить `DataSeeder` / `SceneBuilder` / `ViewAssetBuilder` и
   перезапустить их через `Tools/unity-method.ps1` (генерируемые ассеты коммитятся вместе с `.meta`).
3. `Tools/compile.ps1` → 0 ошибок CS (предупреждения новых файлов тоже убрать по возможности).
4. `Tools/test.ps1` → всё зелёное. Новая логика боя — с новыми EditMode-тестами.
5. `git add` (только свои файлы, без `Library/`, `Temp/`, `Builds/`, `Tools/out/`) и `git commit`. Мелкие коммиты,
   понятное сообщение на английском (`S2.3: add projectile system with pooling`), последняя строка сообщения:
   `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
6. Дописать строку в `Docs/NightLog.md`: `HH:MM — S2.3 — итог (ок / частично: что не сделано) — <short hash>`
   и закоммитить отдельным коммитом `docs: night log S2.3`.
- 3 неудачи подряд на одном пункте → записать в `Questions.md` (что пробовал, лог), откатить/отключить незаконченное
  так, чтобы проект компилировался и тесты были зелёными, и идти дальше.
- После каждого шага проект компилируется, тесты зелёные, сцена Match запускается.

## 4. Запреты

- Ничего из Brawl Stars / Supercell (см. §1). Не подключать платные сервисы и ассеты. Не создавать аккаунты.
- Ничего не публиковать и не отправлять в интернет. Скачивать можно только пакеты Unity из официального реестра.
- `git remote`, `git push` — не делать. Не переписывать историю (`--amend` только для своего последнего непушнутого коммита, лучше не надо).
- Не открывать Unity с GUI, не запускать два Unity на проект, не трогать телефон (`-Install`) ночью.

## 5. Архитектура (кратко; подробно и с причинами — `Docs/Decisions.md`)

| asmdef | Папка | Зависит от | Что внутри |
| --- | --- | --- | --- |
| `Arena.Core` (`noEngineReferences: true`) | `Assets/_Project/Scripts/Core` | — | `Vec2`, RNG, `PlayerInput`, состояние матча (blittable struct в массивах), Defs, системы тика, карта/коллизии, видимость, правила режимов |
| `Arena.Bots` (`noEngineReferences: true`) | `Assets/_Project/Scripts/Bots` | Core | A*, восприятие, utility AI → `PlayerInput` |
| `Arena.Data` | `Assets/_Project/Scripts/Data` | Core, Bots | ScriptableObject-конфиги, `GameDatabase`, конвертеры в Core-Defs, `LocTable` |
| `Arena.Gameplay` | `Assets/_Project/Scripts/Gameplay` | Core, Bots, Data, Unity.InputSystem | `MatchRunner` (тики), виды и пулы, камера, локальный ввод, поток сцен |
| `Arena.Modes` | `Assets/_Project/Scripts/Modes` | Core, Data, Gameplay | Отображение режимов (жёлуди, дупло) |
| `Arena.UI` | `Assets/_Project/Scripts/UI` | Core, Data, Gameplay, Modes, Unity.InputSystem, UnityEngine.UI | Стики, HUD, меню, итоги (строятся кодом из `UiStyle`) |
| `Arena.Editor` (Editor only) | `Assets/_Project/Editor` | все | `ProjectConfigurator`, `SceneBuilder`, `ViewAssetBuilder`, `DataSeeder`, `BuildScript`, редактор карт |
| `Arena.Tests.EditMode` | `Assets/Tests/EditMode` | все + NUnit | Тесты (обязательны для логики боя) |

- **Симуляция:** `MatchSimulation.Tick(PlayerInput[] inputs)`, dt = 1/30 с. Всё изменяемое состояние — в `MatchState`
  (массивы struct фиксированной ёмкости из `SimLimits`), снимается/восстанавливается `CopyFrom` без аллокаций.
  Время — в тиках (`int`), здоровье/урон — `int`. Детерминированный RNG — в состоянии. События тика — в
  `SimEventBuffer` (не часть снапшота) для визуальной обратной связи.
- **Ввод:** Input System. Виртуальные стики (uGUI) + WASD/мышь → `LocalInputCollector` → `PlayerInput`. Боты выдают тот же `PlayerInput`.
- **Данные:** SO-ассеты в `Assets/_Project/Data/{Fighters,Maps,Modes,Combat,Bots,Feel,Economy}`, создаёт
  `Arena.Editor.Data.DataSeeder.SeedAll` (на этапе 2 сидер перезаписывает значения — он источник правды, D-010).
- **Сцены** `Boot`, `Menu`, `Match` в `Assets/_Project/Scenes` генерирует `Arena.Editor.Build.SceneBuilder.BuildAll`
  (включает их в Build Settings). Сцены содержат только бутстрап-объекты; карта, бойцы, UI создаются в рантайме.
- **Рендер:** URP, примитивы. Материалы — ассеты в `Assets/_Project/Art/Materials` (никакого `Shader.Find` в рантайме),
  префабы видов — в `Assets/_Project/Art/Prefabs`, ссылки — через `ViewCatalog`.
- Координаты: Core `Vec2(x, y)` ↔ Unity `(x, 0, y)`; 1 клетка = 1 юнит; клетка (c, r) — центр `(c+0.5, r+0.5)`.
- Пустые будущие папки (`Net`, `Meta`, `Services`, `Server`, `Localization`) не создавать, пока в них нечего положить.
