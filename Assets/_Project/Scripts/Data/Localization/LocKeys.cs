namespace Arena.Data.Localization
{
    /// <summary>
    /// Keys of the UI strings (D-019). Every key used by the UI is a constant here and is listed in <see cref="All"/>;
    /// tests check that <c>Strings.asset</c> has non-empty EN and RU for each. Fighter names use <c>FighterConfig.NameKey</c>.
    /// </summary>
    public static class LocKeys
    {
        public const string MenuPlay = "menu.play";
        public const string MenuFighter = "menu.fighter";
        public const string MenuBots = "menu.bots";
        public const string MenuLanguage = "menu.language";
        public const string MenuVersion = "menu.version";
        public const string BotsNovice = "bots.novice";
        public const string BotsEasy = "bots.easy";
        public const string LanguageEn = "language.en";
        public const string LanguageRu = "language.ru";
        public const string ModeAcorns = "mode.acorns";
        public const string HudHoldAlly = "hud.hold.ally";
        public const string HudHoldEnemy = "hud.hold.enemy";
        public const string ResultsVictory = "results.victory";
        public const string ResultsDefeat = "results.defeat";
        public const string ResultsDraw = "results.draw";
        public const string ResultsMvp = "results.mvp";
        public const string ResultsAgain = "results.again";
        public const string ResultsMenu = "results.menu";
        public const string ResultsKills = "results.kills";
        public const string ResultsDeaths = "results.deaths";
        public const string ResultsDamage = "results.damage";
        public const string ResultsAcorns = "results.acorns";
        public const string MenuSettings = "menu.settings";
        public const string SettingsTitle = "settings.title";
        public const string SettingsMirror = "settings.mirror";
        public const string SettingsVibration = "settings.vibration";
        public const string SettingsAimMagnet = "settings.aim_magnet";
        public const string SettingsColorblind = "settings.colorblind";
        public const string SettingsBatterySaver = "settings.battery_saver";
        public const string SettingsOn = "settings.on";
        public const string SettingsOff = "settings.off";
        public const string SettingsClose = "settings.close";
        public const string HudFps = "hud.fps";
        public const string MenuPlayOnline = "menu.play_online";
        public const string RegionLan = "region.lan";
        public const string QueueSearching = "queue.searching";
        public const string QueueCancel = "queue.cancel";
        public const string NetConnecting = "net.connecting";
        public const string ErrorServerUnreachable = "error.server_unreachable";
        public const string ErrorUpdateRequired = "error.update_required";
        public const string ErrorMaintenance = "error.maintenance";
        public const string DevNetRtt = "dev.net_rtt";
        public const string DevNetJitter = "dev.net_jitter";
        public const string DevNetLoss = "dev.net_loss";
        public const string DevNetMargin = "dev.net_margin";
        public const string DevNetInterp = "dev.net_interp";
        public const string DevNetCorrections = "dev.net_corrections";
        public const string DevNetMs = "dev.net_ms";
        public const string DevNetSimulator = "dev.net_simulator";

        // S3.11 (Server-Spec §12.4): the online menu, matchmaking, connection overlay, ping badge, online results.
        public const string MenuLastBeast = "menu.last_beast";
        public const string MenuTraining = "menu.training";
        public const string MenuRegion = "menu.region";
        public const string ModeLastBeast = "mode.last_beast";

        /// <summary>Format: {0} found of {1} needed.</summary>
        public const string QueueFound = "queue.found";

        public const string NetReconnecting = "net.reconnecting";
        public const string NetBotPlaying = "net.bot_playing";

        /// <summary>"Back to the match": the menu button and the gave-up banner (S3.12).</summary>
        public const string NetRejoin = "net.rejoin";

        public const string ErrorUdpBlocked = "error.udp_blocked";

        /// <summary>"The match is already over" (a return refused with MatchOver, S3.12).</summary>
        public const string ErrorMatchOver = "error.match_over";

        /// <summary>The unit after a ping number.</summary>
        public const string HudPingMs = "hud.ping_ms";

        /// <summary>Format: {0} — the place (Last Beast).</summary>
        public const string ResultsPlace = "results.place";

        public static readonly string[] All =
        {
            MenuPlay, MenuFighter, MenuBots, MenuLanguage, MenuVersion, BotsNovice, BotsEasy, LanguageEn, LanguageRu, ModeAcorns,
            HudHoldAlly, HudHoldEnemy, ResultsVictory, ResultsDefeat, ResultsDraw, ResultsMvp, ResultsAgain, ResultsMenu,
            ResultsKills, ResultsDeaths, ResultsDamage, ResultsAcorns, MenuSettings, SettingsTitle, SettingsMirror, SettingsVibration,
            SettingsAimMagnet, SettingsColorblind, SettingsBatterySaver, SettingsOn, SettingsOff, SettingsClose, HudFps,
            MenuPlayOnline, RegionLan, QueueSearching, QueueCancel, NetConnecting, ErrorServerUnreachable, ErrorUpdateRequired,
            ErrorMaintenance, DevNetRtt, DevNetJitter, DevNetLoss, DevNetMargin, DevNetInterp, DevNetCorrections, DevNetMs,
            DevNetSimulator, MenuLastBeast, MenuTraining, MenuRegion, ModeLastBeast, QueueFound, NetReconnecting, NetBotPlaying,
            NetRejoin, ErrorUdpBlocked, ErrorMatchOver, HudPingMs, ResultsPlace
        };
    }
}
