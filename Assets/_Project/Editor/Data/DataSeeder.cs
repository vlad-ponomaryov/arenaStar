using Arena.Bots;
using Arena.Core.Defs;
using Arena.Data;
using Arena.Data.Localization;
using Arena.Editor.Net;
using Arena.Editor.Setup;
using Arena.Net.Client;
using UnityEditor;
using UnityEngine;

namespace Arena.Editor.Data
{
    /// <summary>
    /// Creates or overwrites all config assets (D-010: on stage 2 the seeder is the source of truth, idempotent).
    /// Numbers from GDD §5.2 / §6; numbers that are not in the GDD are marked PROTO here and in Decisions.md (D-013).
    /// Run: Tools/unity-method.ps1 -Method Arena.Editor.Data.DataSeeder.SeedAll
    /// </summary>
    public static class DataSeeder
    {
        public const string DataRoot = "Assets/_Project/Data";
        public const string CombatFolder = DataRoot + "/Combat";
        public const string FightersFolder = DataRoot + "/Fighters";
        public const string FeelFolder = DataRoot + "/Feel";
        public const string MapsFolder = DataRoot + "/Maps";
        public const string AcornsGladePath = MapsFolder + "/Acorns_Glade.asset";
        public const string CameraConfigPath = FeelFolder + "/CameraConfig.asset";
        public const string FeelConfigPath = FeelFolder + "/Feel.asset";
        public const string MatchRunnerConfigPath = CombatFolder + "/MatchRunnerConfig.asset";
        public const string CombatRulesPath = CombatFolder + "/CombatRules.asset";
        public const string GameDatabasePath = DataRoot + "/GameDatabase.asset";
        public const string ModesFolder = DataRoot + "/Modes";
        public const string AcornsModePath = ModesFolder + "/Acorns.asset";
        public const string LastBeastModePath = ModesFolder + "/LastBeast.asset";
        public const string RoyaleThicketPath = MapsFolder + "/Royale_Thicket.asset";
        public const string LocalizationFolder = DataRoot + "/Localization";
        public const string StringsPath = LocalizationFolder + "/Strings.asset";
        public const string BotsFolder = DataRoot + "/Bots";
        public const string BotNovicePath = BotsFolder + "/Novice.asset";
        public const string BotEasyPath = BotsFolder + "/Easy.asset";
        public const string BotMediumPath = BotsFolder + "/Medium.asset";
        public const string BotBehaviourPath = BotsFolder + "/Behaviour.asset";
        public const string NetFolder = DataRoot + "/Net";
        public const string NetConfigPath = NetFolder + "/NetConfig.asset";

        // Online client (Server-Spec §13 "C"): the prediction, interpolation and lobby numbers come from
        // ClientMatchSettings / LobbyClientSettings.CreateDefault (the single place for them). Game port 7777 (§13, server.json);
        // the first region is "Local (LAN)" — found by the Discover broadcast (§10.1). Rejoin window 60 s (GDD §8.3).
        // PROTO (D-101): the dev overlay refreshes twice a second; the dev simulator defaults to the CI conditions of GDD §8.3
        // (RTT 150 ms = 2 × 75, jitter 50 = 2 × 25, loss 2 % = 2 × 1) but is off.
        private const int GamePort = 7777;
        private const string LanRegionId = "lan";
        private const float ReconnectWindowSeconds = 60f;
        private const bool DevOverlayOn = true;
        private const float DevOverlayRefreshSeconds = 0.5f;
        private const bool DevSimOn = false;
        private const int DevSimLatencyMs = 75;
        private const int DevSimJitterMs = 25;
        private const float DevSimLossPercent = 1f;
        // PROTO (S3.11, D-102): the match HUD says "connection lost" after 1 s without packets (snapshots come 20-30 times
        // a second; the transport drops the connection after 5 s), the ping badge refreshes twice a second.
        private const float ConnectionLostSeconds = 1f;
        private const float HudPingRefreshSeconds = 0.5f;

        // Bots (GDD §7): Novice ±25° / 600 ms, Easy ±15° / 450 ms, decisions every 0.2 s, retreat below 35 % health.
        private const float NoviceSpread = 25f;
        private const float NoviceReaction = 0.6f;
        private const float EasySpread = 15f;
        private const float EasyReaction = 0.45f;
        private const float MediumSpread = 8f; // GDD §7 "Средний" (F1: "Normal" in the menu)
        private const float MediumReaction = 0.3f;
        private const float BotDecisionSeconds = 0.2f;
        private const float BotRetreatHealth = 0.35f;

        // PROTO (D-013, D-057): view radius ≈ the visible part of the map on a phone; retreat until 80 % health; the last
        // charge only to finish or against an enemy closer than 3 cells; keep 80 % of the attack range, shoot within 95 %;
        // ≥ 0.3 s between presses; strafe side kept 0.8 s; utility scores (retreat > attack > objective > wander, a near
        // enemy beats the objective); A* node limit = the largest map, 64 waypoints per bot.
        private const float BotViewRadius = 10f;
        private const float BotRecoverHealth = 0.8f;
        private const float BotLastChargeDistance = 3f;
        private const float BotEngageRange = 0.8f;
        private const float BotFireRange = 0.95f;
        private const float BotMinShotInterval = 0.3f;
        private const float BotStrafeSwitch = 0.8f;
        private const float BotWaypointReach = 0.3f;
        private const int BotWanderRadius = 4;
        private const int BotWanderAttempts = 8;
        private const float BotRetreatScore = 1f;
        private const float BotAttackScore = 0.6f;
        private const float BotAttackProximityBonus = 0.3f;
        private const float BotObjectiveScore = 0.5f;
        private const float BotWanderScore = 0.1f;
        private const int BotPathNodeLimit = 2048;
        private const int BotMaxPathLength = 64;
        // F1 (D-107): offline training and online fill bots play Normal (Medium) by default.
        private const BotDifficulty DefaultBots = BotDifficulty.Medium;

        // PROTO (D-058): ult / trick / mistakes by difficulty — chances per decision (5 per second) while the situation fits;
        // "misuse" = the ult at a lone tanky target in its zone / the trick at any perceived enemy; empty-bush shots without
        // a reason; retreat threshold shifted per life by a random amount within ± jitter (sometimes too late).
        // GDD §7: Novice "rarely and off the mark", Easy "sometimes".
        private const float NoviceUltChance = 0.25f;
        private const float NoviceUltMisuse = 0.05f;
        private const float NoviceTrickChance = 0.25f;
        private const float NoviceTrickMisuse = 0.02f;
        private const float NoviceBushShot = 0.06f;
        private const float NoviceRetreatJitter = 0.12f;
        private const float EasyUltChance = 0.5f;
        private const float EasyUltMisuse = 0.02f;
        private const float EasyTrickChance = 0.5f;
        private const float EasyTrickMisuse = 0.01f;
        private const float EasyBushShot = 0.03f;
        private const float EasyRetreatJitter = 0.06f;
        // GDD §7: Medium "as the situation demands"; F1: lead on moving targets, focus on the weak, sidestepping projectiles.
        private const float MediumUltChance = 0.85f;
        private const float MediumUltMisuse = 0.01f;
        private const float MediumTrickChance = 0.8f;
        private const float MediumTrickMisuse = 0.005f;
        private const float MediumBushShot = 0.02f;
        private const float MediumRetreatJitter = 0.03f;
        private const float MediumLead = 0.8f;
        private const float MediumFocusHealthCells = 2.5f;
        private const float MediumDodgeChance = 0.8f;
        private const float MediumDodgeLookaheadSeconds = 0.5f;
        private const float MediumDodgeMarginCells = 0.3f;
        // Novice and Easy keep the stage-2 play: no lead, the nearest target, no dodging.
        private const float NoLead = 0f;
        private const float NoFocus = 0f;
        private const float NoDodge = 0f;

        // PROTO (D-058): ult at 2+ enemies (GDD §7); "focused" = hit by 2+ enemies within 1.5 s (GDD §7); escape roll with an
        // enemy within 5 cells; "close" = 2.5 cells; heal below 60 %; mine within 1.5 cells of the objective; memory of a lost
        // enemy 3 s, extrapolated by its last velocity for ≤ 0.5 s, bush within 1 cell of that point; hide in a bush within
        // 6 cells when retreating; 8 random cells tried for an empty-bush shot; carrier of 8+ acorns (GDD §7) retreats below
        // 60 % and keeps 1.5 cells behind the allies; scores: hide 1.05 (> retreat 1), check bush / stay with team 0.55
        // (> objective 0.5, < attack 0.6).
        private const int BotUltMinEnemies = 2;
        private const int BotFocusMinEnemies = 2;
        private const float BotFocusWindow = 1.5f;
        private const float BotEscapeDistance = 5f;
        private const float BotEnemyCloseDistance = 2.5f;
        private const float BotHealAllyHealth = 0.6f;
        private const float BotObjectivePlaceDistance = 1.5f;
        private const float BotMemorySeconds = 3f;
        private const float BotMemoryPredictSeconds = 0.5f;
        private const int BotBushNearCells = 1;
        private const int BotHideBushRadius = 6;
        private const int BotEmptyBushShotAttempts = 8;
        private const int BotCarrierAcorns = 8;
        private const float BotCarrierRetreatHealth = 0.6f;
        private const float BotCarrierBehind = 1.5f;
        private const float BotHideInBushScore = 1.05f;
        private const float BotCheckBushScore = 0.55f;
        private const float BotStayWithTeamScore = 0.55f;
        private const bool RandomBotFighters = true;

        // Acorns mode (GDD §5.4): acorn every 6 s, golden every 60 s worth 3, 12 to hold for 12 s, 2:30 match.
        private const float AcornSpawnSeconds = 6f;
        private const float AcornGoldenSeconds = 60f;
        private const int AcornGoldenValue = 3;
        private const int AcornTarget = 12;
        private const float AcornHoldSeconds = 12f;
        private const float AcornMatchSeconds = 150f;

        // PROTO (D-013, D-025): scatter around the hollow / the defeated fighter, acorn radius, pickup delay of dropped
        // acorns, landing attempts, MVP weights (a takedown ≈ 1000 damage, an acorn ≈ 400, a knockout −500).
        private const float AcornSpawnScatter = 1.5f;
        private const float AcornDropScatter = 1.2f;
        private const float AcornPickupRadius = 0.3f;
        private const float AcornPickupDelaySeconds = 0.5f;
        private const int AcornScatterAttempts = 8;
        private const float MvpKillWeight = 1000f;
        private const float MvpDamageWeight = 1f;
        private const float MvpHealWeight = 1f;
        private const float MvpAcornWeight = 400f;
        private const float MvpDeathWeight = 500f;

        // Last Beast (GDD §5.4): fog from 0:40, every 20 s 2 cells from each side, 500 per second; berry +8 % damage and max
        // health (additive); a defeated fighter drops its berries + 1.
        private const float FogStartSeconds = 40f;
        private const float FogStepSeconds = 20f;
        private const float FogShrinkCells = 2f;
        private const int FogDamagePerSecond = 500;
        private const int BerryDamagePercent = 8;
        private const int BerryHealthPercent = 8;
        private const int ExtraBerriesOnDefeat = 1;

        // PROTO (D-089): box durability ≈ 2.5 Pix bursts, 1 berry per box, fog +250 per second per step in pulses of 0.5 s,
        // smallest safe square 5×5 cells, 6 min safety limit; berry radius / scatter / pickup delay as acorns (D-025).
        private const int BoxHealth = 3600;
        private const int BerriesPerBox = 1;
        private const int FogDamageGrowthPerStep = 250;
        private const float FogPulseSeconds = 0.5f;
        private const float FogMinHalfSize = 2.5f;
        private const float LastBeastMaxSeconds = 360f;
        private const float BerryPickupRadius = 0.3f;
        private const float BerryDropScatter = 1.2f;
        private const float BerryPickupDelaySeconds = 0.5f;
        private const int BerryScatterAttempts = 8;

        // PROTO (S3.3): bots flee the fog above everything else (retreat 1, hide 1.05) and 3 s before a step that covers them.
        private const float BotFleeFogScore = 1.2f;
        private const float BotFogWarnSeconds = 3f;

        // PROTO (S3.14, D-105): Last Beast pace — loot (boxes, berries) within 12 cells; a box is broken when nothing better is
        // around (score 0.4: above wander 0.1, below a berry 0.5 and an attack 0.6); fleeing the fog goes to the nearest point
        // 1.5 cells inside the next safe square (not all to the centre); wandering explores safe cells within 6 cells.
        private const float BotBreakBoxScore = 0.4f;
        private const int BotLootRadius = 12;
        private const float BotFogSafeMargin = 1.5f;
        private const int BotExploreRadius = 6;

        // PROTO (S3.14, D-105): caution — while more than 4 are alive, enemies farther than 5 cells are left alone unless they
        // hit the bot within 3 s (the match does not end before the fog).
        private const float BotCautiousEngageDistance = 4f;
        private const int BotCautiousUntilAlive = 4;
        private const float BotProvokedSeconds = 3f;

        // UI strings EN / RU (D-019). Fighter names are original (GDD §6).
        private static readonly string[][] Strings =
        {
            new[] { LocKeys.MenuPlay, "Play", "Играть" },
            new[] { LocKeys.MenuFighter, "Fighter", "Боец" },
            new[] { LocKeys.MenuBots, "Bots", "Боты" },
            new[] { LocKeys.MenuLanguage, "Language", "Язык" },
            new[] { LocKeys.MenuVersion, "Version", "Версия" },
            new[] { LocKeys.BotsNovice, "Novice", "Новичок" },
            new[] { LocKeys.BotsEasy, "Easy", "Лёгкий" },
            new[] { LocKeys.BotsMedium, "Normal", "Средний" },
            new[] { LocKeys.LanguageEn, "English", "English" },
            new[] { LocKeys.LanguageRu, "Русский", "Русский" },
            new[] { LocKeys.ModeAcorns, "Acorns", "Жёлуди" },
            new[] { LocKeys.HudHoldAlly, "We hold:", "Держим:" },
            new[] { LocKeys.HudHoldEnemy, "They hold:", "Держат враги:" },
            new[] { LocKeys.ResultsVictory, "Victory!", "Победа!" },
            new[] { LocKeys.ResultsDefeat, "Defeat", "Поражение" },
            new[] { LocKeys.ResultsDraw, "Draw", "Ничья" },
            new[] { LocKeys.ResultsMvp, "Best player", "Лучший игрок" },
            new[] { LocKeys.ResultsAgain, "Play again", "Ещё раз" },
            new[] { LocKeys.ResultsMenu, "Menu", "Меню" },
            new[] { LocKeys.ResultsKills, "Takedowns", "Победы" },
            new[] { LocKeys.ResultsDeaths, "Knocked out", "Выбыл" },
            new[] { LocKeys.ResultsDamage, "Damage", "Урон" },
            new[] { LocKeys.ResultsAcorns, "Acorns", "Жёлуди" },
            new[] { LocKeys.MenuSettings, "Settings", "Настройки" },
            new[] { LocKeys.SettingsTitle, "Settings", "Настройки" },
            new[] { LocKeys.SettingsMirror, "Mirrored layout", "Зеркальная раскладка" },
            new[] { LocKeys.SettingsVibration, "Vibration", "Вибрация" },
            new[] { LocKeys.SettingsAimMagnet, "Aim assist", "Примагничивание прицела" },
            new[] { LocKeys.SettingsColorblind, "Colour-blind palette", "Палитра для дальтоников" },
            new[] { LocKeys.SettingsBatterySaver, "Battery saver (30 FPS)", "Экономия батареи (30 FPS)" },
            new[] { LocKeys.SettingsOn, "On", "Вкл" },
            new[] { LocKeys.SettingsOff, "Off", "Выкл" },
            new[] { LocKeys.SettingsClose, "Close", "Закрыть" },
            new[] { LocKeys.HudFps, "FPS", "FPS" },
            new[] { LocKeys.MenuPlayOnline, "Play 3 vs 3", "Играть 3 на 3" },
            new[] { LocKeys.RegionLan, "Local network", "Локальная сеть" },
            new[] { LocKeys.QueueSearching, "Searching for players…", "Ищем игроков…" },
            new[] { LocKeys.QueueCancel, "Cancel", "Отмена" },
            new[] { LocKeys.NetConnecting, "Connecting…", "Подключение…" },
            new[] { LocKeys.ErrorServerUnreachable, "Server unavailable", "Сервер недоступен" },
            new[] { LocKeys.ErrorUpdateRequired, "Update the game", "Обновите игру" },
            new[] { LocKeys.ErrorMaintenance, "Maintenance", "Технические работы" },
            new[] { LocKeys.DevNetRtt, "RTT", "RTT" },
            new[] { LocKeys.DevNetJitter, "jitter", "джиттер" },
            new[] { LocKeys.DevNetLoss, "loss %", "потери %" },
            new[] { LocKeys.DevNetMargin, "input margin", "запас ввода" },
            new[] { LocKeys.DevNetInterp, "interp.", "интерп." },
            new[] { LocKeys.DevNetCorrections, "corrections", "поправки" },
            new[] { LocKeys.DevNetMs, "ms", "мс" },
            new[] { LocKeys.DevNetSimulator, "simulator", "симулятор" },
            new[] { LocKeys.MenuLastBeast, "Last Beast", "Последний зверь" },
            new[] { LocKeys.MenuTraining, "Training", "Тренировка" },
            new[] { LocKeys.HudAliveLeft, "Left", "Осталось" },
            new[] { LocKeys.HudFogIn, "Fog in", "Туман через" },
            new[] { LocKeys.HudBerries, "Berries", "Ягоды" },
            new[] { LocKeys.MenuTrainingMode, "Training mode:", "Режим тренировки:" },
            new[] { LocKeys.PauseTitle, "Pause", "Пауза" },
            new[] { LocKeys.PauseMenuTitle, "Menu", "Меню" },
            new[] { LocKeys.PauseResume, "Resume", "Продолжить" },
            new[] { LocKeys.PauseLeave, "Leave match", "Выйти из матча" },
            new[] { LocKeys.PauseOnlineHint, "The battle goes on", "Бой продолжается" },
            new[] { LocKeys.PauseLeaveConfirm, "Leave the match?", "Выйти из матча?" },
            new[] { LocKeys.PauseLeaveYes, "Leave", "Выйти" },
            new[] { LocKeys.PauseLeaveNo, "Stay", "Остаться" },
            new[] { LocKeys.MenuRegion, "Region:", "Регион:" },
            new[] { LocKeys.ModeLastBeast, "Last Beast", "Последний зверь" },
            new[] { LocKeys.QueueFound, "Found {0} of {1}", "Найдено {0} из {1}" },
            new[] { LocKeys.NetReconnecting, "Connection lost — reconnecting…", "Связь потеряна — переподключаемся…" },
            new[] { LocKeys.NetBotPlaying, "A bot is playing your fighter", "Вашим бойцом управляет бот" },
            new[] { LocKeys.NetRejoin, "Back to the match", "Вернуться в бой" },
            new[] { LocKeys.ErrorUdpBlocked, "The network blocks the game — try another Wi-Fi or mobile data",
                "Сеть не пропускает игру — попробуйте другую Wi-Fi или мобильный интернет" },
            new[] { LocKeys.ErrorMatchOver, "The match is already over", "Матч уже закончился" },
            new[] { LocKeys.HudPingMs, "ms", "мс" },
            new[] { LocKeys.ResultsPlace, "Place {0}", "{0}-е место" },
            new[] { "fighter.pix.name", "Pix", "Пикс" },
            new[] { "fighter.bruno.name", "Bruno", "Бруно" },
            new[] { "fighter.chomp.name", "Chomp", "Чомп" },
            new[] { "fighter.kora.name", "Kora", "Кора" },
            new[] { "fighter.nova.name", "Nova", "Нова" },
            new[] { "fighter.vex.name", "Vex", "Векс" }
        };

        /// <summary>Fighter ids in menu order (asset names are the capitalized ids).</summary>
        public static readonly string[] FighterIds = { "pix", "bruno", "chomp", "kora", "nova", "vex" };

        // GDD §5.2
        private const int MaxCharges = 3;
        private const float RegenDelaySeconds = 3f;
        private const float RegenFractionPerSecond = 0.12f;
        private const float LevelBonusPerLevel = 0.06f;
        private const float SlowSpeed = 2.2f;
        private const float NormalSpeed = 2.5f;
        private const float FastSpeed = 2.8f;
        private const float RespawnSeconds = 3f;
        private const float UltLossOnDeath = 0.5f;
        private const int TrickUses = 3;
        private const float TrickCooldownSeconds = 5f;

        // GDD §9.4: trick opens at level 5, talent at 8 (the prototype unlocks all, D-012).
        private const int TrickUnlockLevel = 5;
        private const int TalentUnlockLevel = 8;
        private const bool UnlockAllAbilities = true;

        // GDD §5.2: a bush hides unless an enemy is closer than 2 cells; an attack reveals for 1 s (D-024).
        private const float BushRevealDistance = 2f;
        // D-129: tap auto-aim — enemy in the open > berry box in the open > enemy behind a wall; wounded preferred; sticky target.
        private const bool AutoAimPreferClearShot = true;
        private const bool AutoAimTargetsBoxes = true;
        private const float AutoAimHealthWeightCells = 1.5f;
        private const float AutoAimStickySeconds = 2f;
        private const float AutoAimStickyMarginCells = 2f;
        private const float RevealSeconds = 1f;

        // Acorns map "Glade" (original layout, 21×31, GDD §5.4–5.5): three lanes split by walls and ponds, bushes on the
        // flanks and next to the centre (equal for both sides), breakable cover in front of each base, the hollow in the
        // centre. Only the bottom half (team 0, rows 0–14, written top row first) and the middle row are written here;
        // the top half is generated as the central mirror (A → B), so the map is symmetric by construction.
        private const string AcornsGladeId = "acorns_glade";
        private const int SpawnsPerTeam = 3;
        private static readonly string[] AcornsGladeBottomHalf =
        {
            "..~~...\"\"...\"\"...~~..", // row 14
            ".......\"\"...\"\".......",
            ".....#..%...%..#.....",
            "...###.........###...",
            "..........#..........",
            "\"\"....~~..#..~~....\"\"",
            "\"\"\"...~~.....~~...\"\"\"",
            "\"\"\"...##.....##...\"\"\"",
            ".....................",
            "..#......%%%......#..",
            "..###...........###..",
            ".....................",
            "......A...A...A......",
            "..\"\".............\"\"..",
            "....................." // row 0
        };
        private const string AcornsGladeMiddleRow = "~~...#...\"H\"...#...~~"; // row 15, its own mirror

        // Last Beast map "Thicket" (original layout, 45×45, GDD §5.4): open ground with cover clusters, ponds, bush patches and
        // berry boxes spread evenly; 10 shared spawns along the edges; the last 5×5 safe square in the centre is open floor.
        // Only the top-left quarter (23×23, text rows top first; its last column and last row are the mirror axes) is written
        // here; the map is mirrored left↔right and top↔bottom (MirrorQuarter), so it is fair by construction.
        private const string RoyaleThicketId = "royale_thicket";
        private const int RoyaleSharedSpawns = 10;
        private static readonly string[] RoyaleThicketQuarter =
        {
            ".......................", // text row 0 (top edge)
            "..........\"\"\"..........",
            "..S.......\"\"\".........S",
            ".......................",
            "....##.......$.........",
            "....#......~~~.....%%..",
            "...........~~~.........",
            ".....$.................",
            "\"\"............###......",
            "\"\"\"...%%.........$.....",
            "\"\"\"....................",
            "..........$.....\"\"\"....",
            ".......................",
            "..S....##.......\"\"\".#..",
            ".......#..$............",
            ".......................",
            "....~~......#..........",
            "....~~......#....$.....",
            ".........\"\"\"...........",
            "...$.....\"\"\"......##...",
            "...................#...",
            ".............%.........",
            "......\"\"..............." // text row 22: the middle row (its own mirror)
        };

        // PROTO (D-013): body radii in cells.
        private const float RadiusDefault = 0.45f;
        private const float RadiusLarge = 0.55f;
        private const float RadiusMedium = 0.5f;

        // PROTO (D-013): attack delivery (projectile speeds/radii, the pause between Vex's two strikes).
        private const float PixDropSpeed = 12f;
        private const float PixDropRadius = 0.2f;
        private const int PixBurstCount = 4;
        private const float PixBurstInterval = 0.1f;
        private const float BrunoConeAngle = 90f;
        private const float KoraCupSpeed = 10f;
        private const float KoraCupRadius = 0.25f;
        private const float NovaConeSpeed = 18f;
        private const float NovaConeRadius = 0.15f;
        private const float VexClawAngle = 90f;
        private const int VexClawHits = 2;
        private const float VexClawInterval = 0.15f;

        // Pix full set (GDD §6): ult "Downpour" — fan of 10 drops, 8 cells, 400 each, light knockback; trick "Roll" — dash
        // 3 cells along movement; talent "Tailwind" — +10 % speed while the ult is charged. PROTO: fan angle, knockback,
        // roll speed, ult charge.
        private const int PixUltCharge = 7600;      // PROTO ≈ 5 full bursts hitting (4 × 380 × 5)
        private const int PixUltDrops = 10;
        private const float PixUltSpread = 50f;     // PROTO
        private const int PixUltDamage = 400;
        private const float PixUltPush = 0.5f;      // PROTO "light knockback", cells
        private const float PixUltPushTime = 0.1f;  // PROTO
        private const float PixRollDistance = 3f;
        private const float PixRollSpeed = 15f;     // PROTO: 3 cells in 0.2 s
        private const int PixTalentSpeedPercent = 10;

        // Bruno full set (GDD §6): ult "Ram" — dash 6 cells, 1000, knockback 2 cells, stun 0.5 s, breaks walls; trick
        // "Hard Hat" — −40 % damage taken for 2 s; talent "Stubborn" — +15 % damage below 40 % health. PROTO: speeds, charge.
        private const int BrunoUltCharge = 7200;    // PROTO ≈ 6 swings hitting (6 × 1200)
        private const float BrunoRamDistance = 6f;
        private const float BrunoRamSpeed = 15f;    // PROTO: 6 cells in 0.4 s
        private const int BrunoRamDamage = 1000;
        private const float BrunoRamPush = 2f;
        private const float BrunoRamPushTime = 0.2f; // PROTO
        private const float BrunoRamStun = 0.5f;
        private const int BrunoHelmetPercent = 40;
        private const float BrunoHelmetSeconds = 2f;
        private const int BrunoTalentHealthPercent = 40;
        private const int BrunoTalentDamagePercent = 15;

        // Chomp full set (GDD §6): attack — pine-cone bomb lobbed over walls up to 7 cells, blast r 1.2, 1100, the zone shows
        // 0.5 s before (= the flight time); ult "Log Roll" — log along a line 9 cells, 1600, knockback 2 cells, passes through
        // everyone, breaks walls; trick "Mine" — 900 when stepped on, lasts 10 s; talent "Big Boom" — area radius +20 %.
        // PROTO: log speed/width/push time, mine radius, ult charge (D-060).
        private const float ChompLobRange = 7f;
        private const float ChompBlastRadius = 1.2f;
        private const float ChompLobFlight = 0.5f;
        private const int ChompBombDamage = 1100;
        private const int ChompUltCharge = 6600;    // PROTO ≈ 6 bombs hitting (6 × 1100)
        private const float ChompLogRange = 9f;
        private const float ChompLogSpeed = 10f;    // PROTO: 9 cells in 0.9 s
        private const float ChompLogRadius = 0.6f;  // PROTO: a log ~1.2 cells wide
        private const int ChompLogDamage = 1600;
        private const float ChompLogPush = 2f;
        private const float ChompLogPushTime = 0.2f; // PROTO
        private const float ChompMineRadius = 1f;   // PROTO: trigger and blast radius
        private const float ChompMineSeconds = 10f;
        private const int ChompMineDamage = 900;
        private const int ChompTalentRadiusPercent = 20;

        // Kora full set (GDD §6): attack — tea cup bouncing up to 3 targets, −600 enemies / +500 allies, 7 cells; ult "Dome" —
        // r 2.5 for 3 s, blocks projectiles from outside, allies inside restore 1000 in all; trick "Brew" — instant +1200 to self
        // and allies within 3; talent "Herbal Blend" — allies within 3 start regenerating after 2 s instead of 3.
        // PROTO: bounce radius, dome pulse interval, ult charge (D-060).
        private const int KoraBounces = 2;           // 3 targets in all
        private const float KoraBounceRange = 3f;    // PROTO
        private const int KoraCupDamage = 600;
        private const int KoraCupHeal = 500;
        private const int KoraUltCharge = 4800;      // PROTO ≈ 8 enemy hits (8 × 600); bounces hit several
        private const float KoraDomeRadius = 2.5f;
        private const float KoraDomeSeconds = 3f;
        private const float KoraDomePulse = 0.5f;    // PROTO: 6 pulses of ~167
        private const int KoraDomeHeal = 1000;
        private const float KoraBrewRadius = 3f;
        private const int KoraBrewHeal = 1200;
        private const int KoraAuraRadius = 3;
        private const float KoraAuraRegenDelay = 2f;

        // Nova full set (GDD §6): attack — slingshot cone 10 cells, 1500, pierces the first enemy, the second takes 50 %, the
        // pierced (hit) targets are marked for 4 s; ult "Owl Eye" — the team sees every enemy for 5 s, the next 3 shots +30 %;
        // trick "Wing Flap" — enemies within 2 cells are pushed 3 cells; talent "Sharp Eye" — +20 % damage to marked targets.
        // PROTO: push time, ult charge (D-061).
        private const int NovaPierce = 1;
        private const int NovaPierceFalloff = 50;
        private const int NovaConeDamage = 1500;
        private const float NovaMarkSeconds = 4f;
        private const int NovaUltCharge = 9000;      // PROTO ≈ 1.6 ults in 30 s at 50 % hits (BalanceProbe, D-061)
        private const float NovaRevealSeconds = 5f;
        private const int NovaBoostPercent = 30;
        private const int NovaBoostShots = 3;
        private const float NovaFlapRadius = 2f;
        private const float NovaFlapPush = 3f;
        private const float NovaFlapPushTime = 0.3f; // PROTO: 3 cells in 0.3 s
        private const int NovaTalentDamagePercent = 20;

        // Vex full set (GDD §6): attack — two claw strikes of 520 (1.8 cells); ult "Hook" — 7 cells, wall: pulls Vex to it,
        // enemy: pulls Vex to it, 800 and −30 % speed for 1 s; trick "Smoke" — 1.5 s invisible; talent "Agility" — a defeat
        // refills all attack charges. PROTO: hook flight / pull speed, hook radius, ult charge (D-061).
        private const float VexHookRange = 7f;
        private const float VexHookSpeed = 20f;      // PROTO: 7 cells in 0.35 s
        private const float VexHookRadius = 0.3f;    // PROTO
        private const float VexPullSpeed = 20f;      // PROTO
        private const int VexHookDamage = 800;
        private const int VexHookSlowPercent = 30;
        private const float VexHookSlowSeconds = 1f;
        private const int VexUltCharge = 10400;      // PROTO ≈ 1.7 ults in 30 s at 50 % hits (BalanceProbe, D-061)
        private const float VexSmokeSeconds = 1.5f;

        // Camera (GDD §5.5: ~55°); the rest PROTO (D-013).
        private const float CameraPitch = 55f;
        private const float CameraDistance = 26.6f;   // A.3d (D-128): long lens — same frame height at the focus (16.8 cells)
        private const float CameraFieldOfView = 35f;
        private const float CameraForwardOffset = 2f;
        private const float CameraEdgeInset = 7f;
        private const float CameraFollowSharpness = 8f;

        // PROTO (S3.14): the 45×45 free-for-all map is followed along both axes, focus 1 cell ahead, 10 cells from the sides.
        private const float CameraFreeForwardOffset = 1f;
        private const float CameraFreeSideInset = 10f;

        // Feel (GDD §5.3): hit stop 40 ms, telegraph 0.5 s, aim magnet up to 5° (GDD §5.1); the rest PROTO (D-013, D-059):
        // short flash, numbers float up 0.7 s, small shake on ult hits, light 20 ms buzz per hit (60 ms per ult hit, at most
        // one per 0.08 s), vibration choices 0/25/50/75/100 %, clips at 22 kHz (hit = noisy blip, ult = falling boom,
        // ult ready = two rising notes), ult stick pulses twice a second by 8 %, FPS averaged over 0.5 s.
        private const float FeelFlashSeconds = 0.1f;
        private const float FeelNumberSeconds = 0.7f;
        private const float FeelNumberRiseSpeed = 1.6f;
        private const float FeelNumberHeight = 2.3f;
        private const float FeelNumberScatter = 0.3f;
        private const int FeelNumberFontSize = 40;
        private const int FeelUltNumberFontSize = 56;
        private const int FeelNumberPoolPrewarm = 24;
        private static readonly Color FeelDamageColor = new Color(1f, 1f, 1f, 1f);
        private static readonly Color FeelDamageTakenColor = new Color(1f, 0.38f, 0.32f, 1f);
        private static readonly Color FeelHealColor = new Color(0.45f, 1f, 0.5f, 1f);
        private static readonly Vector2 FeelTextReferenceResolution = new Vector2(1920f, 1080f);
        private const float FeelTextMatchWidthOrHeight = 0.5f;
        private const float FeelOwnHealthHeight = 2.55f;
        private const int FeelOwnHealthFontSize = 30;
        private static readonly Color FeelOwnHealthColor = new Color(1f, 1f, 1f, 1f);
        private const float FeelShakeAmplitude = 0.25f;
        private const float FeelShakeSeconds = 0.25f;
        private const float FeelShakeFrequency = 28f;
        private const float FeelHitStopSeconds = 0.04f;
        private const int FeelHitVibrationMs = 20;
        private const int FeelUltVibrationMs = 60;
        private const float FeelVibrationMinInterval = 0.08f;
        private static readonly int[] FeelVibrationSteps = { 0, 25, 50, 75, 100 };
        private const int FeelSampleRate = 22050;
        private static readonly ProceduralSoundSettings FeelHitSound = new ProceduralSoundSettings(700f, 300f, 0f, 0.07f, 0.45f, 0.6f);
        private static readonly ProceduralSoundSettings FeelUltSound = new ProceduralSoundSettings(220f, 70f, 0f, 0.4f, 0.8f, 0.25f);
        private static readonly ProceduralSoundSettings FeelUltReadySound = new ProceduralSoundSettings(880f, 880f, 1320f, 0.3f, 0.5f, 0f);
        private const float FeelHitSoundMinInterval = 0.05f;
        private const float FeelTelegraphSeconds = 0.5f;
        private const float FeelUltGlowFrequency = 2f;
        private const float FeelUltGlowScale = 0.08f;
        private const float FeelFpsUpdateSeconds = 0.5f;
        private const float FeelAimMagnetDegrees = 5f;
        private const float FeelAutoAimMarkerRadiusScale = 1.6f; // D-129: disc under the tap target while the stick is held still
        private const float FeelAutoAimMarkerFill = 0.55f;

        // Settings defaults of a first start (GDD §5.1: aim magnet on; full vibration) and frame rate (S2.13, D-062).
        private const bool FeelDefaultMirroredLayout = false;
        private const int FeelDefaultVibrationPercent = 100;
        private const bool FeelDefaultAimMagnet = true;
        private const bool FeelDefaultColorblindPalette = false;
        private const bool FeelDefaultBatterySaver = false;
        private const int FeelTargetFrameRate = 60;
        private const int FeelBatterySaverFrameRate = 30;

        // Match runner: technical limit + prototype setup (3×3: the local player in slot 0, bots in the others; the line-up is
        // the fallback when bots do not pick random fighters).
        private const int MaxTicksPerFrame = 4;
        private const int MatchSeed = 1;
        private const int LocalSlot = 0;
        private const int TeamSize = 3;
        private const int FighterLevel = 1;
        private static readonly string[] SlotFighterIds = { "pix", "bruno", "chomp", "kora", "nova", "vex" };

        public static string FighterAssetPath(string id) => FightersFolder + "/" + AssetNameFor(id) + ".asset";

        public static void SeedAll()
        {
            ProjectConfigurator.EnsureFolder(CombatFolder);
            ProjectConfigurator.EnsureFolder(FightersFolder);
            ProjectConfigurator.EnsureFolder(FeelFolder);
            ProjectConfigurator.EnsureFolder(MapsFolder);
            ProjectConfigurator.EnsureFolder(ModesFolder);
            ProjectConfigurator.EnsureFolder(LocalizationFolder);
            ProjectConfigurator.EnsureFolder(BotsFolder);

            CombatRulesConfig rules = SeedCombatRules();

            FighterConfig[] fighters =
            {
                //                     id       HP    speed               radius         recharge  attack (range, damage — GDD §6)
                SeedFighter("pix",   3400, SpeedClass.Normal, RadiusDefault, 1.4f, Projectile(8f, PixDropSpeed, PixDropRadius, 0, PixBurstCount, PixBurstInterval, 380)),
                SeedFighter("bruno", 5600, SpeedClass.Normal, RadiusLarge,   1.1f, Cone(2.5f, BrunoConeAngle, 1, 0f, 1200)),
                SeedFighter("chomp", 3000, SpeedClass.Normal, RadiusDefault, 1.6f, ChompBomb()),
                SeedFighter("kora",  4200, SpeedClass.Slow,   RadiusMedium,  1.5f, KoraCup()),
                SeedFighter("nova",  2600, SpeedClass.Normal, RadiusDefault, 2.0f, NovaCone()),
                SeedFighter("vex",   3600, SpeedClass.Fast,   RadiusDefault, 0.9f, Cone(1.8f, VexClawAngle, VexClawHits, VexClawInterval, 520))
            };

            SeedPixAbilities(fighters[0]);
            SeedBrunoAbilities(fighters[1]);
            SeedChompAbilities(fighters[2]);
            SeedKoraAbilities(fighters[3]);
            SeedNovaAbilities(fighters[4]);
            SeedVexAbilities(fighters[5]);

            GameDatabase db = LoadOrCreate<GameDatabase>(GameDatabasePath);
            db.CombatRules = rules;
            if (db.Fighters == null) db.Fighters = new System.Collections.Generic.List<FighterConfig>();
            db.Fighters.Clear();
            db.Fighters.AddRange(fighters);
            db.DefaultMap = SeedAcornsGlade();
            if (db.AcornsMaps == null) db.AcornsMaps = new System.Collections.Generic.List<MapConfig>();
            db.AcornsMaps.Clear();
            db.AcornsMaps.Add(db.DefaultMap);
            db.Camera = SeedCamera();
            db.Feel = SeedFeel();
            db.MatchRunner = SeedMatchRunner();
            db.AcornsMode = SeedAcornsMode();
            db.LastBeastMode = SeedLastBeastMode();
            if (db.LastBeastMaps == null) db.LastBeastMaps = new System.Collections.Generic.List<MapConfig>();
            db.LastBeastMaps.Clear();
            db.LastBeastMaps.Add(SeedRoyaleThicket());
            db.Strings = SeedStrings();
            if (db.BotDifficulties == null) db.BotDifficulties = new System.Collections.Generic.List<BotDifficultyConfig>();
            db.BotDifficulties.Clear();
            db.BotDifficulties.Add(SeedBotDifficulty(BotNovicePath, BotDifficulty.Novice, NoviceSpread, NoviceReaction,
                NoviceUltChance, NoviceUltMisuse, NoviceTrickChance, NoviceTrickMisuse, NoviceBushShot, NoviceRetreatJitter,
                NoLead, NoFocus, NoDodge, NoDodge, NoDodge));
            db.BotDifficulties.Add(SeedBotDifficulty(BotEasyPath, BotDifficulty.Easy, EasySpread, EasyReaction,
                EasyUltChance, EasyUltMisuse, EasyTrickChance, EasyTrickMisuse, EasyBushShot, EasyRetreatJitter,
                NoLead, NoFocus, NoDodge, NoDodge, NoDodge));
            db.BotDifficulties.Add(SeedBotDifficulty(BotMediumPath, BotDifficulty.Medium, MediumSpread, MediumReaction,
                MediumUltChance, MediumUltMisuse, MediumTrickChance, MediumTrickMisuse, MediumBushShot, MediumRetreatJitter,
                MediumLead, MediumFocusHealthCells, MediumDodgeChance, MediumDodgeLookaheadSeconds, MediumDodgeMarginCells));
            db.BotBehaviour = SeedBotBehaviour();
            EditorUtility.SetDirty(db);

            AssetDatabase.SaveAssets();
            Debug.Log("ARENA_DATA_SEEDED fighters=" + db.Fighters.Count);

            // The content pack of the online client and the server is derived from these assets (Server-Spec §5.4).
            ContentPackExporter.Export();

            // The online client settings reference the exported pack.
            db.Net = SeedNetConfig();
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
        }

        private static CombatRulesConfig SeedCombatRules()
        {
            CombatRulesConfig c = LoadOrCreate<CombatRulesConfig>(CombatRulesPath);
            c.MaxCharges = MaxCharges;
            c.RegenDelaySeconds = RegenDelaySeconds;
            c.RegenFractionPerSecond = RegenFractionPerSecond;
            c.RespawnSeconds = RespawnSeconds;
            c.LevelBonusPerLevel = LevelBonusPerLevel;
            c.UltLossOnDeath = UltLossOnDeath;
            c.TrickUses = TrickUses;
            c.TrickCooldownSeconds = TrickCooldownSeconds;
            c.TrickUnlockLevel = TrickUnlockLevel;
            c.TalentUnlockLevel = TalentUnlockLevel;
            c.BushRevealDistance = BushRevealDistance;
            c.RevealSeconds = RevealSeconds;
            c.AutoAimPreferClearShot = AutoAimPreferClearShot;
            c.AutoAimTargetsBoxes = AutoAimTargetsBoxes;
            c.AutoAimHealthWeightCells = AutoAimHealthWeightCells;
            c.AutoAimStickySeconds = AutoAimStickySeconds;
            c.AutoAimStickyMarginCells = AutoAimStickyMarginCells;
            c.SlowSpeed = SlowSpeed;
            c.NormalSpeed = NormalSpeed;
            c.FastSpeed = FastSpeed;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static AcornsModeConfig SeedAcornsMode()
        {
            AcornsModeConfig c = LoadOrCreate<AcornsModeConfig>(AcornsModePath);
            c.SpawnIntervalSeconds = AcornSpawnSeconds;
            c.GoldenIntervalSeconds = AcornGoldenSeconds;
            c.GoldenValue = AcornGoldenValue;
            c.SpawnScatterRadius = AcornSpawnScatter;
            c.TargetAcorns = AcornTarget;
            c.HoldSeconds = AcornHoldSeconds;
            c.DurationSeconds = AcornMatchSeconds;
            c.PickupRadius = AcornPickupRadius;
            c.DropScatterRadius = AcornDropScatter;
            c.PickupDelaySeconds = AcornPickupDelaySeconds;
            c.ScatterAttempts = AcornScatterAttempts;
            c.MvpKillWeight = MvpKillWeight;
            c.MvpDamageWeight = MvpDamageWeight;
            c.MvpHealWeight = MvpHealWeight;
            c.MvpAcornWeight = MvpAcornWeight;
            c.MvpDeathWeight = MvpDeathWeight;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static LocTable SeedStrings()
        {
            LocTable t = LoadOrCreate<LocTable>(StringsPath);
            if (t.Entries == null) t.Entries = new System.Collections.Generic.List<LocEntry>();
            t.Entries.Clear();
            for (int i = 0; i < Strings.Length; i++)
                t.Entries.Add(new LocEntry { Key = Strings[i][0], En = Strings[i][1], Ru = Strings[i][2] });
            EditorUtility.SetDirty(t);
            return t;
        }

        private static NetConfig SeedNetConfig()
        {
            ProjectConfigurator.EnsureFolder(NetFolder);
            NetConfig c = LoadOrCreate<NetConfig>(NetConfigPath);
            c.ContentPack = AssetDatabase.LoadAssetAtPath<TextAsset>(ContentPackExporter.PackPath);
            if (c.ContentPack == null) throw new System.InvalidOperationException("DataSeeder: missing " + ContentPackExporter.PackPath);
            if (c.Regions == null) c.Regions = new System.Collections.Generic.List<NetRegionConfig>();
            c.Regions.Clear();
            c.Regions.Add(new NetRegionConfig { Id = LanRegionId, NameKey = LocKeys.RegionLan, Host = "", Port = GamePort });

            ClientMatchSettings m = ClientMatchSettings.CreateDefault();
            c.TargetInputMarginTicks = m.TargetInputMarginTicks;
            c.MarginGain = m.MarginGain;
            c.MaxTimeDilation = m.MaxTimeDilation;
            c.HardResyncTicks = m.HardResyncTicks;
            c.MarginSettleTicks = m.MarginSettleTicks;
            c.MaxResimTicks = m.MaxResimTicks;
            c.MaxTicksPerFrame = m.MaxTicksPerFrame;
            c.FallbackRttMs = m.FallbackRttMs;
            c.CorrectionSmoothingSeconds = m.CorrectionSmoothingSeconds;
            c.SnapDistanceCells = m.SnapDistanceCells;
            c.LargeCorrectionCells = m.LargeCorrectionCells;
            c.InterpDelaySnapshots = m.InterpDelaySnapshots;
            c.InterpJitterFactor = m.InterpJitterFactor;
            c.InterpJitterSmoothing = m.InterpJitterSmoothing;
            c.InterpGain = m.InterpGain;
            c.InterpDelayMaxMs = m.InterpDelayMaxMs;
            c.MaxExtrapolationMs = m.MaxExtrapolationMs;
            c.StatsReportTicks = m.StatsReportTicks;

            LobbyClientSettings l = LobbyClientSettings.CreateDefault();
            c.PingSamples = l.PingSamples;
            c.PingIntervalMs = l.PingIntervalMs;
            c.PingTimeoutMs = l.PingTimeoutMs;
            c.PingGoodMs = l.PingGoodMs;
            c.PingFairMs = l.PingFairMs;
            c.DiscoverRepeats = l.DiscoverRepeats;
            c.DiscoverIntervalMs = l.DiscoverIntervalMs;
            c.DiscoverTimeoutMs = l.DiscoverTimeoutMs;

            c.ReconnectWindowSeconds = ReconnectWindowSeconds;
            c.ReconnectRetrySeconds = new System.Collections.Generic.List<float>(ReconnectSettings.CreateDefault().RetrySeconds);
            c.ConnectionLostSeconds = ConnectionLostSeconds;
            c.HudPingRefreshSeconds = HudPingRefreshSeconds;
            c.DevOverlay = DevOverlayOn;
            c.DevOverlayRefreshSeconds = DevOverlayRefreshSeconds;
            c.DevSimEnabled = DevSimOn;
            c.DevSimLatencyMs = DevSimLatencyMs;
            c.DevSimJitterMs = DevSimJitterMs;
            c.DevSimLossPercent = DevSimLossPercent;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static FeelConfig SeedFeel()
        {
            FeelConfig c = LoadOrCreate<FeelConfig>(FeelConfigPath);
            c.FlashSeconds = FeelFlashSeconds;
            c.NumberSeconds = FeelNumberSeconds;
            c.NumberRiseSpeed = FeelNumberRiseSpeed;
            c.NumberHeight = FeelNumberHeight;
            c.NumberScatter = FeelNumberScatter;
            c.NumberFontSize = FeelNumberFontSize;
            c.UltNumberFontSize = FeelUltNumberFontSize;
            c.NumberPoolPrewarm = FeelNumberPoolPrewarm;
            c.DamageColor = FeelDamageColor;
            c.DamageTakenColor = FeelDamageTakenColor;
            c.HealColor = FeelHealColor;
            c.TextReferenceResolution = FeelTextReferenceResolution;
            c.TextMatchWidthOrHeight = FeelTextMatchWidthOrHeight;
            c.OwnHealthHeight = FeelOwnHealthHeight;
            c.OwnHealthFontSize = FeelOwnHealthFontSize;
            c.OwnHealthColor = FeelOwnHealthColor;
            c.ShakeAmplitude = FeelShakeAmplitude;
            c.ShakeSeconds = FeelShakeSeconds;
            c.ShakeFrequency = FeelShakeFrequency;
            c.HitStopSeconds = FeelHitStopSeconds;
            c.HitVibrationMs = FeelHitVibrationMs;
            c.UltVibrationMs = FeelUltVibrationMs;
            c.VibrationMinInterval = FeelVibrationMinInterval;
            c.VibrationSteps = (int[])FeelVibrationSteps.Clone();
            c.SampleRate = FeelSampleRate;
            c.HitSound = FeelHitSound;
            c.UltSound = FeelUltSound;
            c.UltReadySound = FeelUltReadySound;
            c.HitSoundMinInterval = FeelHitSoundMinInterval;
            c.TelegraphSeconds = FeelTelegraphSeconds;
            c.UltGlowFrequency = FeelUltGlowFrequency;
            c.UltGlowScale = FeelUltGlowScale;
            c.FpsUpdateSeconds = FeelFpsUpdateSeconds;
            c.AimMagnetDegrees = FeelAimMagnetDegrees;
            c.AutoAimMarkerRadiusScale = FeelAutoAimMarkerRadiusScale;
            c.AutoAimMarkerFill = FeelAutoAimMarkerFill;
            c.DefaultMirroredLayout = FeelDefaultMirroredLayout;
            c.DefaultVibrationPercent = FeelDefaultVibrationPercent;
            c.DefaultAimMagnet = FeelDefaultAimMagnet;
            c.DefaultColorblindPalette = FeelDefaultColorblindPalette;
            c.DefaultBatterySaver = FeelDefaultBatterySaver;
            c.TargetFrameRate = FeelTargetFrameRate;
            c.BatterySaverFrameRate = FeelBatterySaverFrameRate;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static CameraConfig SeedCamera()
        {
            CameraConfig c = LoadOrCreate<CameraConfig>(CameraConfigPath);
            c.PitchDegrees = CameraPitch;
            c.Distance = CameraDistance;
            c.FieldOfView = CameraFieldOfView;
            c.ForwardOffset = CameraForwardOffset;
            c.EdgeInset = CameraEdgeInset;
            c.FollowSharpness = CameraFollowSharpness;
            c.FreeForwardOffset = CameraFreeForwardOffset;
            c.FreeSideInset = CameraFreeSideInset;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static MatchRunnerConfig SeedMatchRunner()
        {
            MatchRunnerConfig c = LoadOrCreate<MatchRunnerConfig>(MatchRunnerConfigPath);
            c.MaxTicksPerFrame = MaxTicksPerFrame;
            c.Seed = MatchSeed;
            c.LocalSlot = LocalSlot;
            if (c.SlotFighterIds == null) c.SlotFighterIds = new System.Collections.Generic.List<string>();
            c.SlotFighterIds.Clear();
            c.SlotFighterIds.AddRange(SlotFighterIds);
            c.TeamSize = TeamSize;
            c.FighterLevel = FighterLevel;
            c.UnlockAllAbilities = UnlockAllAbilities;
            c.DefaultBots = DefaultBots;
            c.RandomBotFighters = RandomBotFighters;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static BotDifficultyConfig SeedBotDifficulty(string path, BotDifficulty difficulty, float spread, float reaction,
            float ultChance, float ultMisuse, float trickChance, float trickMisuse, float bushShot, float retreatJitter, float lead,
            float focusHealthCells, float dodgeChance, float dodgeLookaheadSeconds, float dodgeMarginCells)
        {
            BotDifficultyConfig c = LoadOrCreate<BotDifficultyConfig>(path);
            c.Difficulty = difficulty;
            c.AimSpreadDegrees = spread;
            c.ReactionSeconds = reaction;
            c.UltUseChance = ultChance;
            c.UltMisuseChance = ultMisuse;
            c.TrickUseChance = trickChance;
            c.TrickMisuseChance = trickMisuse;
            c.EmptyBushShotChance = bushShot;
            c.RetreatThresholdJitter = retreatJitter;
            c.LeadFraction = lead;
            c.FocusHealthWeightCells = focusHealthCells;
            c.DodgeChance = dodgeChance;
            c.DodgeLookaheadSeconds = dodgeLookaheadSeconds;
            c.DodgeMarginCells = dodgeMarginCells;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static BotBehaviourConfig SeedBotBehaviour()
        {
            BotBehaviourConfig c = LoadOrCreate<BotBehaviourConfig>(BotBehaviourPath);
            c.DecisionIntervalSeconds = BotDecisionSeconds;
            c.ViewRadius = BotViewRadius;
            c.RetreatHealthFraction = BotRetreatHealth;
            c.RecoverHealthFraction = BotRecoverHealth;
            c.LastChargeEnemyDistance = BotLastChargeDistance;
            c.EngageRangeFraction = BotEngageRange;
            c.FireRangeFraction = BotFireRange;
            c.MinShotIntervalSeconds = BotMinShotInterval;
            c.StrafeSwitchSeconds = BotStrafeSwitch;
            c.WaypointReachDistance = BotWaypointReach;
            c.WanderRadiusCells = BotWanderRadius;
            c.WanderAttempts = BotWanderAttempts;
            c.RetreatScore = BotRetreatScore;
            c.AttackScore = BotAttackScore;
            c.AttackProximityBonus = BotAttackProximityBonus;
            c.ObjectiveScore = BotObjectiveScore;
            c.WanderScore = BotWanderScore;
            c.PathNodeLimit = BotPathNodeLimit;
            c.MaxPathLength = BotMaxPathLength;
            c.UltMinEnemies = BotUltMinEnemies;
            c.FocusMinEnemies = BotFocusMinEnemies;
            c.FocusWindowSeconds = BotFocusWindow;
            c.EscapeEnemyDistance = BotEscapeDistance;
            c.EnemyCloseDistance = BotEnemyCloseDistance;
            c.HealAllyHealthFraction = BotHealAllyHealth;
            c.ObjectivePlaceDistance = BotObjectivePlaceDistance;
            c.MemorySeconds = BotMemorySeconds;
            c.MemoryPredictSeconds = BotMemoryPredictSeconds;
            c.BushNearCells = BotBushNearCells;
            c.HideBushRadiusCells = BotHideBushRadius;
            c.EmptyBushShotAttempts = BotEmptyBushShotAttempts;
            c.CarrierAcorns = BotCarrierAcorns;
            c.CarrierRetreatHealthFraction = BotCarrierRetreatHealth;
            c.CarrierBehindDistance = BotCarrierBehind;
            c.HideInBushScore = BotHideInBushScore;
            c.CheckBushScore = BotCheckBushScore;
            c.StayWithTeamScore = BotStayWithTeamScore;
            c.FleeFogScore = BotFleeFogScore;
            c.FogWarnSeconds = BotFogWarnSeconds;
            c.BreakBoxScore = BotBreakBoxScore;
            c.LootRadiusCells = BotLootRadius;
            c.FogSafeMarginCells = BotFogSafeMargin;
            c.ExploreRadiusCells = BotExploreRadius;
            c.CautiousEngageDistance = BotCautiousEngageDistance;
            c.CautiousUntilAlive = BotCautiousUntilAlive;
            c.ProvokedSeconds = BotProvokedSeconds;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static MapConfig SeedAcornsGlade()
        {
            MapConfig c = LoadOrCreate<MapConfig>(AcornsGladePath);
            c.Id = AcornsGladeId;
            c.Rows = MirrorBottomHalf(AcornsGladeBottomHalf, AcornsGladeMiddleRow);
            c.RequiredSpawnsPerTeam = SpawnsPerTeam;
            c.RequiredSharedSpawns = 0;
            c.RequireCentralSymmetry = true;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static MapConfig SeedRoyaleThicket()
        {
            MapConfig c = LoadOrCreate<MapConfig>(RoyaleThicketPath);
            c.Id = RoyaleThicketId;
            c.Rows = MirrorQuarter(RoyaleThicketQuarter);
            c.RequiredSpawnsPerTeam = 0;
            c.RequiredSharedSpawns = RoyaleSharedSpawns;
            c.RequireCentralSymmetry = false;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static LastBeastModeConfig SeedLastBeastMode()
        {
            LastBeastModeConfig c = LoadOrCreate<LastBeastModeConfig>(LastBeastModePath);
            c.FogStartSeconds = FogStartSeconds;
            c.FogStepIntervalSeconds = FogStepSeconds;
            c.FogShrinkCells = FogShrinkCells;
            c.FogMinHalfSize = FogMinHalfSize;
            c.FogDamagePerSecond = FogDamagePerSecond;
            c.FogDamageGrowthPerStep = FogDamageGrowthPerStep;
            c.FogPulseSeconds = FogPulseSeconds;
            c.BoxHealth = BoxHealth;
            c.BerriesPerBox = BerriesPerBox;
            c.BerryDamagePercent = BerryDamagePercent;
            c.BerryHealthPercent = BerryHealthPercent;
            c.ExtraBerriesOnDefeat = ExtraBerriesOnDefeat;
            c.BerryPickupRadius = BerryPickupRadius;
            c.BerryDropScatterRadius = BerryDropScatter;
            c.BerryPickupDelaySeconds = BerryPickupDelaySeconds;
            c.ScatterAttempts = BerryScatterAttempts;
            c.MaxDurationSeconds = LastBeastMaxSeconds;
            EditorUtility.SetDirty(c);
            return c;
        }

        /// <summary>
        /// Full rows (top first) from the top-left quarter (top first) whose last column and last row lie on the mirror axes:
        /// each row is mirrored left↔right around its last character, then the rows top↔bottom around the last row
        /// (an N×N quarter gives a (2N−1)×(2N−1) map).
        /// </summary>
        public static string[] MirrorQuarter(string[] quarterTopFirst)
        {
            int half = quarterTopFirst.Length;
            var top = new string[half];
            for (int i = 0; i < half; i++)
            {
                string row = quarterTopFirst[i];
                char[] chars = new char[row.Length * 2 - 1];
                for (int k = 0; k < row.Length; k++)
                {
                    chars[k] = row[k];
                    chars[chars.Length - 1 - k] = row[k];
                }
                top[i] = new string(chars);
            }
            var rows = new string[half * 2 - 1];
            for (int i = 0; i < half; i++)
            {
                rows[i] = top[i];
                rows[rows.Length - 1 - i] = top[i];
            }
            return rows;
        }

        /// <summary>
        /// Full rows (top first) from the bottom half (top first) and the middle row: the top half is the bottom half rotated
        /// by 180° with team 0 spawns turned into team 1 spawns.
        /// </summary>
        public static string[] MirrorBottomHalf(string[] bottomHalfTopFirst, string middleRow)
        {
            int half = bottomHalfTopFirst.Length;
            var rows = new string[half * 2 + 1];
            for (int i = 0; i < half; i++)
            {
                // Text row i (top half) mirrors bottom text row (half − 1 − i) counted from the bottom.
                char[] chars = bottomHalfTopFirst[half - 1 - i].ToCharArray();
                System.Array.Reverse(chars);
                for (int k = 0; k < chars.Length; k++)
                    if (chars[k] == Arena.Data.Conversion.MapConverter.Team0SpawnChar) chars[k] = Arena.Data.Conversion.MapConverter.Team1SpawnChar;
                rows[i] = new string(chars);
            }
            rows[half] = middleRow;
            for (int i = 0; i < half; i++) rows[half + 1 + i] = bottomHalfTopFirst[i];
            return rows;
        }

        private static void SeedPixAbilities(FighterConfig c)
        {
            c.UltChargeRequired = PixUltCharge;
            c.Ult = new AbilityConfig
            {
                Delivery = DeliveryKind.Projectile, Range = 8f, ProjectileSpeed = PixDropSpeed, ProjectileRadius = PixDropRadius,
                ProjectileCount = PixUltDrops, SpreadDegrees = PixUltSpread,
                Effects = new[]
                {
                    Effect(EffectKind.Damage, TargetFilter.Enemy, PixUltDamage),
                    Effect(EffectKind.Knockback, TargetFilter.Enemy, 0, PixUltPushTime, PixUltPush)
                }
            };
            c.Trick = new AbilityConfig
            {
                Delivery = DeliveryKind.Dash, DashDistance = PixRollDistance, DashSpeed = PixRollSpeed, UsesMoveDirection = true,
                Effects = new EffectConfig[0]
            };
            c.Talent = new TalentConfig
            {
                Condition = TalentCondition.UltCharged, Modifier = TalentModifier.MoveSpeedMul, ValuePercent = PixTalentSpeedPercent
            };
            c.BotTrickHint = BotTrickHint.Escape; // roll away when retreating (GDD §7)
            EditorUtility.SetDirty(c);
        }

        private static void SeedBrunoAbilities(FighterConfig c)
        {
            c.UltChargeRequired = BrunoUltCharge;
            c.Ult = new AbilityConfig
            {
                Delivery = DeliveryKind.Dash, Range = BrunoRamDistance, DashDistance = BrunoRamDistance, DashSpeed = BrunoRamSpeed,
                BreaksWalls = true,
                Effects = new[]
                {
                    Effect(EffectKind.Damage, TargetFilter.Enemy, BrunoRamDamage),
                    Effect(EffectKind.Knockback, TargetFilter.Enemy, 0, BrunoRamPushTime, BrunoRamPush),
                    Effect(EffectKind.Stun, TargetFilter.Enemy, 0, BrunoRamStun)
                }
            };
            c.Trick = new AbilityConfig
            {
                Delivery = DeliveryKind.SelfBuff,
                Effects = new[] { Effect(EffectKind.DamageReduction, TargetFilter.Self, BrunoHelmetPercent, BrunoHelmetSeconds) }
            };
            c.Talent = new TalentConfig
            {
                Condition = TalentCondition.HealthBelow, Param = BrunoTalentHealthPercent,
                Modifier = TalentModifier.DamageMul, ValuePercent = BrunoTalentDamagePercent
            };
            c.BotTrickHint = BotTrickHint.WhenFocused; // helmet under fire of 2+ enemies (GDD §7)
            EditorUtility.SetDirty(c);
        }

        private static AbilityConfig ChompBomb() => new AbilityConfig
        {
            Delivery = DeliveryKind.Lob, Range = ChompLobRange, AreaRadius = ChompBlastRadius, AreaDelaySeconds = ChompLobFlight,
            IgnoresWalls = true, // for bots: no clear line needed
            Effects = EnemyDamage(ChompBombDamage)
        };

        private static AbilityConfig KoraCup() => new AbilityConfig
        {
            Delivery = DeliveryKind.Projectile, Range = 7f, ProjectileSpeed = KoraCupSpeed, ProjectileRadius = KoraCupRadius,
            BounceCount = KoraBounces, BounceRange = KoraBounceRange,
            Effects = new[]
            {
                Effect(EffectKind.Damage, TargetFilter.Enemy, KoraCupDamage),
                Effect(EffectKind.Heal, TargetFilter.Ally, KoraCupHeal)
            }
        };

        private static void SeedChompAbilities(FighterConfig c)
        {
            c.UltChargeRequired = ChompUltCharge;
            c.Ult = new AbilityConfig
            {
                Delivery = DeliveryKind.Projectile, Range = ChompLogRange, ProjectileSpeed = ChompLogSpeed, ProjectileRadius = ChompLogRadius,
                PierceAll = true, BreaksWalls = true, ProjectileView = ProjectileViewKind.Log,
                Effects = new[]
                {
                    Effect(EffectKind.Damage, TargetFilter.Enemy, ChompLogDamage),
                    Effect(EffectKind.Knockback, TargetFilter.Enemy, 0, ChompLogPushTime, ChompLogPush)
                }
            };
            c.Trick = new AbilityConfig
            {
                Delivery = DeliveryKind.PlaceArea, PlacedArea = AreaKind.Mine, AreaRadius = ChompMineRadius,
                AreaDurationSeconds = ChompMineSeconds, HiddenFromEnemies = true,
                Effects = EnemyDamage(ChompMineDamage)
            };
            c.Talent = new TalentConfig
            {
                Condition = TalentCondition.Always, Modifier = TalentModifier.AreaRadiusMul, ValuePercent = ChompTalentRadiusPercent
            };
            c.BotTrickHint = BotTrickHint.PlaceAtObjective; // mine at the hollow (GDD §7)
            EditorUtility.SetDirty(c);
        }

        private static void SeedKoraAbilities(FighterConfig c)
        {
            c.UltChargeRequired = KoraUltCharge;
            c.Ult = new AbilityConfig
            {
                Delivery = DeliveryKind.PlaceArea, PlacedArea = AreaKind.Field, AreaRadius = KoraDomeRadius,
                AreaDurationSeconds = KoraDomeSeconds, AreaPulseSeconds = KoraDomePulse, BlocksEnemyProjectiles = true,
                Effects = new[] { Effect(EffectKind.Heal, TargetFilter.Ally | TargetFilter.Self, KoraDomeHeal) }
            };
            c.Trick = new AbilityConfig
            {
                Delivery = DeliveryKind.SelfArea, Range = KoraBrewRadius,
                Effects = new[] { Effect(EffectKind.Heal, TargetFilter.Ally | TargetFilter.Self, KoraBrewHeal) }
            };
            c.Talent = new TalentConfig
            {
                Condition = TalentCondition.AllyAura, Param = KoraAuraRadius,
                Modifier = TalentModifier.RegenDelay, Seconds = KoraAuraRegenDelay
            };
            c.BotTrickHint = BotTrickHint.HealAllies; // brew when someone near is hurt (GDD §7)
            EditorUtility.SetDirty(c);
        }

        private static AbilityConfig NovaCone() => new AbilityConfig
        {
            Delivery = DeliveryKind.Projectile, Range = 10f, ProjectileSpeed = NovaConeSpeed, ProjectileRadius = NovaConeRadius,
            PierceCount = NovaPierce, PierceFalloffPercent = NovaPierceFalloff,
            Effects = new[]
            {
                Effect(EffectKind.Damage, TargetFilter.Enemy, NovaConeDamage),
                Effect(EffectKind.Mark, TargetFilter.Enemy, 0, NovaMarkSeconds)
            }
        };

        private static void SeedNovaAbilities(FighterConfig c)
        {
            c.UltChargeRequired = NovaUltCharge;
            c.Ult = new AbilityConfig
            {
                Delivery = DeliveryKind.SelfBuff,
                Effects = new[]
                {
                    Effect(EffectKind.TeamReveal, TargetFilter.Self, 0, NovaRevealSeconds),
                    new EffectConfig { Kind = EffectKind.BoostShots, Filter = TargetFilter.Self, Value = NovaBoostPercent, Count = NovaBoostShots }
                }
            };
            c.Trick = new AbilityConfig
            {
                Delivery = DeliveryKind.SelfArea, Range = NovaFlapRadius,
                Effects = new[] { Effect(EffectKind.Knockback, TargetFilter.Enemy, 0, NovaFlapPushTime, NovaFlapPush) }
            };
            c.Talent = new TalentConfig
            {
                Condition = TalentCondition.TargetMarked, Modifier = TalentModifier.DamageMul, ValuePercent = NovaTalentDamagePercent
            };
            c.BotTrickHint = BotTrickHint.WhenEnemiesClose; // flap when an enemy is on top of the sniper (GDD §7)
            EditorUtility.SetDirty(c);
        }

        private static void SeedVexAbilities(FighterConfig c)
        {
            c.UltChargeRequired = VexUltCharge;
            c.Ult = new AbilityConfig
            {
                Delivery = DeliveryKind.Hook, Range = VexHookRange, ProjectileSpeed = VexHookSpeed, ProjectileRadius = VexHookRadius,
                DashSpeed = VexPullSpeed,
                Effects = new[]
                {
                    Effect(EffectKind.Damage, TargetFilter.Enemy, VexHookDamage),
                    Effect(EffectKind.Slow, TargetFilter.Enemy, VexHookSlowPercent, VexHookSlowSeconds)
                }
            };
            c.Trick = new AbilityConfig
            {
                Delivery = DeliveryKind.SelfBuff,
                Effects = new[] { Effect(EffectKind.Invisibility, TargetFilter.Self, 0, VexSmokeSeconds) }
            };
            c.Talent = new TalentConfig { Condition = TalentCondition.OnKill, Modifier = TalentModifier.RefillCharges };
            c.BotTrickHint = BotTrickHint.Engage; // smoke on the way into a fight (GDD §7)
            EditorUtility.SetDirty(c);
        }

        private static EffectConfig Effect(EffectKind kind, TargetFilter filter, int value, float durationSeconds = 0f, float distance = 0f) =>
            new EffectConfig { Kind = kind, Filter = filter, Value = value, DurationSeconds = durationSeconds, Distance = distance };

        private static AbilityConfig None() => new AbilityConfig { Delivery = DeliveryKind.None, Effects = new EffectConfig[0] };

        private static AbilityConfig Projectile(float range, float speed, float radius, int pierce, int burstCount,
            float burstInterval, int damage)
        {
            return new AbilityConfig
            {
                Delivery = DeliveryKind.Projectile, Range = range, ProjectileSpeed = speed, ProjectileRadius = radius,
                PierceCount = pierce, BurstCount = burstCount, BurstIntervalSeconds = burstInterval, Effects = EnemyDamage(damage)
            };
        }

        private static AbilityConfig Cone(float range, float angle, int burstCount, float burstInterval, int damage)
        {
            return new AbilityConfig
            {
                Delivery = DeliveryKind.MeleeCone, Range = range, ConeAngleDegrees = angle,
                BurstCount = burstCount, BurstIntervalSeconds = burstInterval, Effects = EnemyDamage(damage)
            };
        }

        private static EffectConfig[] EnemyDamage(int damage) =>
            new[] { new EffectConfig { Kind = EffectKind.Damage, Filter = TargetFilter.Enemy, Value = damage } };

        private static FighterConfig SeedFighter(string id, int maxHealth, SpeedClass speed, float radius,
            float attackRechargeSeconds, AbilityConfig attack)
        {
            FighterConfig c = LoadOrCreate<FighterConfig>(FighterAssetPath(id));
            c.Id = id;
            c.NameKey = "fighter." + id + ".name";
            c.MaxHealth = maxHealth;
            c.Speed = speed;
            c.Radius = radius;
            c.AttackRechargeSeconds = attackRechargeSeconds;
            c.Attack = attack;
            // Ult, trick and talent: set per fighter after this (Seed<Fighter>Abilities).
            c.UltChargeRequired = 0;
            c.Ult = None();
            c.Trick = None();
            c.Talent = new TalentConfig();
            c.BotTrickHint = BotTrickHint.None;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static string AssetNameFor(string id) => char.ToUpperInvariant(id[0]) + id.Substring(1);
    }
}
