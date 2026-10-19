# Бриф F2 — важные находки `Docs/Stage3-TestAudit.md`

Исполнитель: Opus, medium. Цикл и правила — как в `Docs/Briefs/F1-Fixes.md` (верх файла). Прочитай заметки F1 внизу этого файла.

## Пункты (по `Docs/Stage3-TestAudit.md`)
1. Утечки через события/AOI и `HitMask` (ревью снапшотов п.1–2).
2. Откат часов рендера при остановке снапшотов (ревью клиента п.1–2).
3. «Оживление» соединения после паузы > 5 с и лимит по подменённому IP (ревью транспорта п.1–2).
4. Бот прячется в куст в тумане (ревью Core п.1).
5. Мелочи из аудита — по возможности, если дёшево.
Каждый пункт — с тестом, воспроизводящим проблему. Сервер трогаешь → `Tools/server-test.ps1`.
Отчёт до 150 слов.

## Заметки предыдущих шагов
- (F1) Полный EditMode ≈ 75 с, PlayMode ≈ 85 с, `Tools/server-test.ps1` ≈ 1 мин; после PlayMode — `git checkout ProjectSettings/QualitySettings.asset`. `GraphicsSettings.asset` и два неотслеживаемых `ProjectSettings/*.asset` грязные с прошлого дня — не коммитить.
- (F1) Сетевые тесты берут настройки из `MatchServerRig.CreateSettings()` — там закреплены боты Novice (по умолчанию теперь Medium) и журнал ввода; `LobbyRig` берёт их же. Тест, которому важен порядок «таймаут обрыва 5 с → добор», сам ставит `BotFillSeconds = 15` (по умолчанию 5).
- (F1) Любое поле в `ContentPackWriter` → поднять `ContentPackHeader.CurrentFormatVersion` (сейчас 4) и пересидить `DataSeeder.SeedAll` (он же экспортирует `ArenaContent.bytes`); хэш пакета меняется — сервер и клиент должны быть из одного коммита.
- (F1) Ввод клавиатуры в тестах — `InputTestFixture` (сборка `Unity.InputSystem.TestFramework` уже в EditMode asmdef); `wasPressedThisFrame` живёт одно `InputSystem.Update`, поэтому `Press` двух клавиш подряд — опрашивать после каждой. Имя `PlayerInput` конфликтует с `UnityEngine.InputSystem.PlayerInput` — алиас.
- (F1) Шанс-броски ботов: новый бросок делать только при шансе > 0, иначе меняется RNG и детерминированные матчи Novice/Easy во всех тестах. Сила ботов шумная — сравнивать минимум на 30–50 матчах (свои стороны поменять местами).
- (F1) PlayMode-сервер в процессе: `InProcessServerPump` + `PortableUdpServerSocket` на 127.0.0.1:0; «убить сервер» = уничтожить pump и закрыть сокет (см. `RejoinFlowTests.ServerGone_…`).
