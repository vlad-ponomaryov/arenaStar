# Заметки для следующих агентов

- (F2) Тест, «воспроизводящий проблему», проверяй откатом: `git stash push -- <файл фикса>` → `Tools/test.ps1 -Filter` → `git stash pop`. Python на Windows пишет CRLF и не понимает пути `/c/...` — открывай файлы с `newline=''` и относительными путями.
- (F2) Снапшоты: `SnapshotMatchTests.CheckView` теперь проверяет AOI событий/зон и `HitMask` чужих снарядов (счётчики `foreign events`, `hit bits checked` в отчёте); новый фильтр — в `SnapshotBuilder.BuildTarget`/`EventJournal.Collect` (D-117).
- (F2) `ServerTransport` проверяет `TransportSettings.Validate()` в конструкторе — тестовые настройки с нулями теперь бросают. Пер-IP лимит: только проверенные `ConnectResponse` (`RateLimitedHandshakes`), пинги — `RateLimitedProbes`.
- (F2) `Tools/server-test.ps1` иногда падает на lateness p99 при занятой машине (W9) — повтори один раз; EditMode тоже трогает `ProjectSettings/QualitySettings.asset` — `git checkout` его.
