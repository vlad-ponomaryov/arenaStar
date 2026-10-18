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

        public static readonly string[] All =
        {
            MenuPlay, MenuFighter, MenuBots, MenuLanguage, MenuVersion, BotsNovice, BotsEasy, LanguageEn, LanguageRu, ModeAcorns,
            HudHoldAlly, HudHoldEnemy, ResultsVictory, ResultsDefeat, ResultsDraw, ResultsMvp, ResultsAgain, ResultsMenu,
            ResultsKills, ResultsDeaths, ResultsDamage, ResultsAcorns
        };
    }
}
