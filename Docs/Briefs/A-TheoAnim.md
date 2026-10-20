# A-TheoAnim — процедурная анимация FBX-бойцов (бриф, = A.2a/A.2b Art-Spec для Тео)

Разбор кода 2026-10-20 (только чтение). Основание: Art-Spec §3.1–3.2 (D-109), §8; вид — `A-Theo3D-unity.md`.

## 1. Как сейчас
- **Кости не двигает никто.** `FighterSpring.cs:9` — только данные (Bone, Frequency, Damping, Amount); `FighterRig.cs:28` —
  `Bones[]` по `FighterBone`, rest-поза (`RestPositions/Rotations/Scales`), `ResetPose()` :47, `TopHeight`, `AttackStyle/UltStyle/
  TrickStyle`, `Springs[]`. Пишет `FighterModelBuilder.cs:196–233` (веки `LidL/R` масштаб 0 — :215; Тео: `Shoot/Shoot/Roll`,
  пружина Head 3.5 Гц/0.55/6° — `TheoRecipe.cs:57–62`). Рантайм-читателей `Springs` и стилей нет; `IFighterAnimSink` — только в доках.
- `FighterView.SetPose` (:103) ставит позицию/поворот корня; `MatchRunner.RenderViews` (:263–280) — каждый кадр, позиция из
  `MatchViewState.Position(i, alpha)` (интерполяция двух тиков, D-015), `Facing` — без интерполяции. Во время `HitStop` виды не
  обновляются (:211). Смерть: `IsShown` → `SetVisible(false)` сразу (:270).
- События тика: `MatchRunner.RunTick` (:233) → `HitFeedback.Process` (:57: `Damaged` → вспышка-материал `FighterFlash`, цифры,
  стоп-кадр/тряска ульты; `AbilityFired` ульты → звук; `UltReady`). Онлайн доходят только `Damaged/Healed/AbilityFired/UltReady`
  (+`TileBroken`) — Art-Spec §2.2. Состояние: `FighterState.Alive`, `Facing`, `Stunned` :99, `IsDashing` :148; итог — `ModeState.WinnerTeam`.
- **Эффекта «вспышка у дула» в проекте нет** (нет ни одного `ParticleSystem`; VFX — шаг A.5b). Есть только белая вспышка тела.
- Конфиги чувств — `Data/FeelConfig.cs` (`GameDatabase.Feel`, `Data/Feel/Feel.asset`, `DataSeeder.SeedFeel` :902).

## 2. Архитектура
- Папка `Scripts/Gameplay/View/Anim/`, namespace `Arena.Gameplay.View.Anim` (D-109). Не Core: анимация — только вид, в
  симуляцию/снапшот не входит. Чистая часть **без Transform/MonoBehaviour/Time** — только `Vector3/Quaternion/Mathf` (значимые
  типы, тестируются в EditMode без сцены):
  - `BonePose` (struct: `Position`, `Rotation`, `Scale` — смещения от rest-позы); `PoseBuffer` — `BonePose[14]` по `FighterBone`.
  - `PuppetPose` (static) — функции слоёв: `Idle(t)`, `Run(phase, speed01, lean)`, `Shot(t01, style)`, `Ult(t01)`, `Trick(t01, style)`,
    `Hurt(t01, dir)`, `Defeat(t01)`, `Victory(t)`, `Blink(t01)`; каждая **добавляет** в `PoseBuffer` с весом.
  - `SpringState` (struct: angle, velocity) + `SpringMath.Step(ref, target, freq, damping, dt)` — полунеявный Эйлер, dt ограничен.
  - `FighterAnimState` (struct: таймеры клипов, фаза шага, сглаженная скорость, прошлая позиция, пружины `SpringState[4]`,
    таймер моргания, xorshift-сид) и `FighterAnimator` (класс, один на слот, создаётся в `StartMatch`): реализует `IFighterAnimSink`,
    `Evaluate(dt, PoseBuffer)` — собирает слои; `Apply(FighterRig)` — **единственное** место, где пишутся `localPosition/Rotation/Scale`
    (rest × смещение). Тонкий слой = этот метод + вызов из `MatchRunner`; отдельный MonoBehaviour не нужен.
- `IFighterAnimSink` (Art-Spec §3.2: за ним же может стоять запасной путь с `Animator`/UAL):
  `OnMove(float speed, Vector3 worldDir, float dt)` · `OnShoot(AbilitySlot slot, Vector3 aimDir)` (атака/ульта/трюк — стиль из рига) ·
  `OnHurt(Vector3 fromDir, bool heavy)` · `OnSpawn()` · `OnDeath()` · `OnMatchEnd(bool won)` · `SetStatus(bool stunned, bool dashing)` ·
  `SetLook(Vector3 worldDir)` · `ResetAll()`.
- Кто вызывает — `FighterAnimDriver` (Gameplay/View/Anim, массив аниматоров по слотам, владелец — `MatchRunner`):
  - `ProcessEvents(SimEventBuffer, MatchState)` — в `RunTick` рядом с `_feedback.Process` (:238): `AbilityFired` → `OnShoot`
    (слот по `MatchDefs.SlotOf(Value)`, `Direction`); `Damaged` (Value>0) → `OnHurt` (направление от Source, heavy = ульта).
  - `Render(...)` — в `RenderViews` после `view.SetPose` (:279): скорость из интерполированной позиции (Δпозиции/dt, скачок > 1 клетки
    = телепорт/поправка → фаза не двигается), `SetStatus` из `Stunned/IsDashing`, `SetLook` = `Facing`, затем `Evaluate` + `Apply`.
  - Переходы `Alive` (из `MatchViewState`) → `OnSpawn/OnDeath`; `IsEnded` false→true → `OnMatchEnd(team == WinnerTeam)`.
- **Пружины — наслоить, не заменять:** `FighterRig.Springs` — последний слой поверх клипов (отставание кости от ускорения корпуса
  + поворот корня), `Amount` — град. на ед. ускорения, клэмп `SpringMaxDegrees`. Формат `FighterSpring` не меняется.

## 3. Состояния и слои (порядок сложения)
1. **База** (взаимоисключающие, кроссфейд `BlendSeconds`): Idle — дыхание `Body` масштабом ±2 % (`BreathHz`), лёгкое покачивание
   Head; Run (speed > `RunThreshold`) — фаза += путь / (`StrideLengthPerHeight × TopHeight`), LegL/R — ±`LegSwingDeg` в противофазе
   (sin фазы), ArmL — встречно ногам, ArmR (с бластером) — половина амплитуды, подпрыгивание `Root` |sin|×`BounceHeight`, наклон `Body`
   вперёд `LeanForwardDeg`×speed01 и в поворот по угловой скорости (`LeanTurnDeg`); Stunned — шатание Body/Head; Victory — прыжки,
   лапы вверх; Defeat — кувырок назад + уменьшение до 0 за `DefeatSeconds`.
2. **Действия** (однократные, вес по кривой огибающей): Shot (style `Shoot`) — ArmR+Prop назад `RecoilDeg`, Prop сдвиг `-RecoilDistance`
   по своей оси, Body откат 2°, длительность `ShotSeconds`; Ult — присед (Root y −) → подскок + `UltSpin`, затем Shot ×1.5;
   Trick `Roll` — кувырок `Body` 360° за `TrickSeconds` (если `IsDashing` — тянуть на длительность рывка); прочие стили — заготовки A.2b.
3. **Реакции:** Hurt — сжатие Body (сквош `HurtSquash`), откл. назад от удара `HurtTiltDeg`, веки закрыты, `HurtSeconds`.
   Spawn — растяжение из 0 за `SpawnSeconds`. Сквош при старте/остановке бега — по скачку сглаженной скорости.
4. **Лицо:** моргание LidL/R — масштаб 0→1→0 за `BlinkSeconds`, интервал `BlinkInterval` (3–5 с, xorshift по слоту — без аллокаций,
   детерминированно в тестах), двойное моргание с шансом `DoubleBlinkChance`; при Hurt/Defeat — закрыты. Взгляд: EyeL/R — поворот
   до `LookMaxDeg` к цели `SetLook` относительно корпуса (пока = направление прицела ≈ facing; при беге — в сторону поворота).
   У Тео глаза нарисованы в текстуре — двигать EyeL/R бессмысленно, пока нет геометрии; взгляд = доворот Head на `HeadLookDeg`.
5. **Пружины** (п. 2) и **ограничение** итоговых углов.
- **Веки Тео** (геометрии нет): процедурная группа в `TheoRecipe` на кости LidL и LidR — сплюснутая полусфера-«колпачок» чуть больше
  и чуть впереди нарисованного глаза (радиус ≈ 1.15 глаза, выпуклость наружу, тёмная кромка-ресницы снизу), вес 1 на LidL/R.
  Масштаб 0 (rest, уже ставит сборщик :215) = схлопнута в пивот внутри головы — невидима; 1 = закрыт глаз. Цвет: при `_BaseMapOn=1`
  шейдер берёт текстуру → UV всех вершин века = UV ближайшей FBX-вершины над глазом (мех лба), вершинный цвет белый — нужна
  поддержка «UV из ближайшей вершины источника» для процедурных групп в `FighterModelBuilder`. Проверка — скриншот «глаза закрыты».
- **Вспышка у дула:** готового эффекта нет. Сейчас — пул `MuzzleFlashPool` (Gameplay/View/Anim, 6 штук, префаб-квад-звезда с
  аддитивным unlit-материалом из `ViewAssetBuilder`, ассет + `ViewCatalog.MuzzleFlashPrefab` в конце секции), ставится на
  `Prop`-кость + `MuzzleOffset` вдоль её +Z, живёт `MuzzleSeconds`, масштаб-поп; цвет — `ViewCatalog`. В A.5b заменяется частицами.

## 4. `FighterAnimConfig` (Data, `Arena.Data`, SO; ассет `Data/Feel/FighterAnim.asset`, `GameDatabase.FighterAnim` в конце)
Общий для всех бойцов; различия бойцов — из рига (стили, пружины, `TopHeight`). Значения сидит `DataSeeder.SeedFighterAnim`
(вызов рядом с `SeedFeel` :748). По умолчанию:
`BlendSeconds 0.12` · `BreathHz 0.6, BreathScale 0.02, IdleSwayDeg 2` · `RunThreshold 0.4 ед/с, RunSpeedRef 4 ед/с, SpeedSmoothing 12/с,
StrideLengthPerHeight 0.55, LegSwingDeg 35, ArmSwingDeg 25, BounceHeight 0.04, LeanForwardDeg 10, LeanTurnDeg 8, TeleportDistance 1` ·
`ShotSeconds 0.2, RecoilDeg 18, RecoilDistance 0.05, ShotEnvelope (AnimationCurve)` · `UltSeconds 0.5, UltCrouch 0.08, UltJump 0.15` ·
`TrickSeconds 0.4` · `HurtSeconds 0.2, HurtSquash 0.12, HurtTiltDeg 12` · `SpawnSeconds 0.3` · `DefeatSeconds 0.6` ·
`VictoryHopHz 1.6, VictoryHopHeight 0.12` · `BlinkSeconds 0.12, BlinkIntervalMin 3, BlinkIntervalMax 5, DoubleBlinkChance 0.15` ·
`LookMaxDeg 20, HeadLookDeg 10` · `SpringMaxDegrees 25, MaxDt 0.05` · `MuzzleSeconds 0.08, MuzzleOffset 0.18, MuzzleScale 0.35` ·
`CullOffscreen true`. Кривые — `AnimationCurve.Evaluate` (без аллокаций). Поля вида в пакет данных не входят.

## 5. Производительность и порядок
- Ноль аллокаций в кадре: аниматоры, `PoseBuffer`, пружины — создаются в `StartMatch`; без LINQ/лямбд/`GetComponent` в цикле
  (кэш `FighterRig` и `SkinnedMeshRenderer` при создании видов); RNG — свой xorshift в struct.
- Порядок кадра (`MatchRunner.Update`): тики (`RunTick` → события → `OnShoot/OnHurt`) → `RenderViews(alpha)`: `SetPose` корня →
  `Render` аниматора (поза кости от интерполированной позиции, `dt = Time.deltaTime`). Отдельный `LateUpdate` не нужен: всё в
  одном месте и замирает вместе со стоп-кадром (`HitStop`, D-015). Таймеры — секунды кадра; события лишь запускают клипы.
- Смерть: при `Alive → false` вид, если был показан, остаётся `DefeatSeconds` (клип Defeat, полоска HP скрыта), затем скрывается.
- LOD/отсечение: скрытый вид — не считаем, при показе `ResetAll`; `CullOffscreen` и `!smr.isVisible` — двигаем только таймеры/фазу,
  `Apply` пропускаем. 6 бойцов × 14 костей — дёшево; `Apply` пишет только кости, что есть у рига (`Bones[i] != null`).

## 6. Тесты EditMode (`Assets/Tests/EditMode/Gameplay/Anim/`)
- `PuppetPoseTests`: детерминизм (одинаковые входы → одинаковые позы); Idle при t=0 и t=1/BreathHz совпадают; Run — фаза растёт
  ровно на путь/шаг (нет скольжения), стоит на месте → фаза не меняется, телепорт > `TeleportDistance` не двигает фазу; ноги в
  противофазе; Shot — смещение Prop при t01=0 и 1 равно 0, максимум внутри; Defeat(1) — масштаб Body ≈ 0.
- `SpringMathTests`: затухание к цели, устойчивость при dt=MaxDt, клэмп угла.
- `FighterAnimatorTests`: `OnShoot` слота атаки запускает Shot, ульты — Ult; `OnHurt` закрывает веки; моргание в интервале
  [Min, Max] на 1000 с; `OnDeath` → Defeat, затем «скрыть»; `ResetAll` → rest-поза.
- `FighterAnimDriverTests`: из `SimEventBuffer` с `AbilityFired/Damaged` вызывается нужный слот (фейковый `IFighterAnimSink`).
- `AllocationAssert.NoGcAllocations` вокруг `FighterAnimator.Evaluate` и `Apply` (на GameObject-риге из кода), `Driver.ProcessEvents`.
- `DataSeederTests`/`GameDatabase`: `FighterAnim` не null, `RunThreshold < RunSpeedRef`, `BlinkIntervalMin ≤ Max`.
- `FighterModelTests` (чужой файл, только после того агента): у Тео группы LidL/R есть, вес 1, в rest не видны (масштаб 0).

## 7. Подшаги
1. **AN1 — математика и конфиг** (не трогает чужие файлы): новые `Gameplay/View/Anim/{BonePose, PoseBuffer, PuppetPose, SpringState,
   SpringMath, FighterAnimState, IFighterAnimSink, FighterAnimator}.cs`, `Data/FighterAnimConfig.cs`; правки `GameDatabase.cs`
   (поле в конце), `DataSeeder.cs` (`SeedFighterAnim`); тесты п. 6 (кроме драйвера). Idle/Run/Hurt/Blink/пружины.
2. **AN2 — подключение в матч:** новый `FighterAnimDriver.cs`; правки `MatchRunner.cs` (создание в `CreateFighterViews`, вызовы в
   `RunTick` и `RenderViews`, задержка скрытия при смерти, `OnMatchEnd`); `FighterView.cs` — кэш SMR (`BodyRenderer`). Тест драйвера,
   PlayMode smoke. `FighterRig.cs` не меняется.
3. **AN3 — действия и вспышка:** Shot/Ult/Trick(Roll)/Defeat/Victory/Stunned в `PuppetPose`; `MuzzleFlashPool.cs`; `ViewAssetBuilder`
   (меш/материал/префаб вспышки) + `ViewCatalog.MuzzleFlashPrefab` (в конце секции); перезапуск `ViewAssetBuilder.BuildAll`.
4. **AN4 — веки Тео (после того, как другой агент закончит `FighterModel*.cs`/`TheoRecipe.cs`/`FighterSkin.cs`):** `TheoRecipe.cs` — +2
   группы `FighterGroupSpec` на LidL/LidR (только список групп, ~15 строк); `FighterModelBuilder.BuildGeometry` — для процедурных
   групп FBX-модели UV из ближайшей вершины источника (одна функция, ~20 строк); `FighterSkin.cs` — не трогать. Пересборка моделей,
   тест в `FighterModelTests` (добавить метод в конец файла).

## 8. Превью (кадры → GIF)
- Новый Editor-класс `Editor/Art/Capture/AnimCapture.cs` (по образцу `ArtCapture.SaveHeroShots` :389, использует его `internal static
  Render(camera, w, h)` :655, `Downscale`, `PlaceGameplay`): открыть сцену `Match`, `CaptureStage.Build(database, catalog)`, взять вид
  бойца (`stage.Fighter(i)` с `Rig`), создать `FighterAnimator` с конфигом из `GameDatabase`, крупный план (как hero-ракурс, 3/4 и
  игровая камера). Сценарии — ручное время, без Play Mode: на кадр `t += 1/30`, вызвать `OnMove`/`OnShoot`/`OnHurt`/… по таймлайну,
  `Evaluate(1/30)` + `Apply`, сдвигать корень по пути бега, `Render` → PNG `Tools/out/art/anim/<clip>/f_000.png`.
  Клипы: idle (3 с, с морганием), run (кругом 2 с), shoot ×3, ult, roll, hurt, defeat, victory.
- Запуск: `Tools/unity-method.ps1 -Graphics -Method Arena.Editor.Art.Capture.AnimCapture.CaptureAll [-Extra '-animFighter pix']`.
- GIF — вне Unity: `ffmpeg` есть (`WinGet\Links\ffmpeg`): `ffmpeg -framerate 30 -i f_%03d.png -vf "scale=360:-1,split[a][b];[a]palettegen[p];
  [b][p]paletteuse" <clip>.gif`; обернуть в `Tools/anim-gif.ps1` (5.1-совместимо). Плюс лист-раскадровка 8 кадров в один PNG
  (через `Blit`) — для быстрой оценки оркестратором.
