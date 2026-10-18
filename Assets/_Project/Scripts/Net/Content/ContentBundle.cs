using System;
using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Modes;

namespace Arena.Net.Content
{
    /// <summary>
    /// Everything the server and the online client need to build matches (Server-Spec §5.4, D-083): combat rules, fighters
    /// (in the order of <c>GameDatabase.Fighters</c> — views pick projectile kinds by index), maps, mode rules and their maps,
    /// bots and the match policy. Built by <c>ConfigConverter.ToContentBundle</c> in the editor and by
    /// <see cref="ContentPackReader"/> from <c>ArenaContent.bytes</c>. Immutable by convention: matches share its defs.
    /// When adding a field (here or in any def it holds): extend <see cref="ContentPackWriter"/> and
    /// <see cref="ContentPackReader"/> — ContentPackTests fills every field by reflection and fails on a missing one.
    /// </summary>
    public sealed class ContentBundle
    {
        public CombatRules Rules;
        public FighterDef[] Fighters = new FighterDef[0];
        public MapDef[] Maps = new MapDef[0];

        /// <summary>Map of the offline match (<c>GameDatabase.DefaultMap</c>), index into <see cref="Maps"/>.</summary>
        public int DefaultMapIndex;

        /// <summary>Acorns mode rules, or null if the content has no Acorns mode.</summary>
        public AcornsModeDef Acorns;

        /// <summary>«Last Beast» mode rules, or null if the content has no Last Beast mode.</summary>
        public LastBeastModeDef LastBeast;

        /// <summary>Maps per mode; a mode without an entry is not offered online.</summary>
        public ContentModeMaps[] ModeMaps = new ContentModeMaps[0];

        public BotBehaviourDef BotBehaviour;
        public BotDifficultyDef[] BotDifficulties = new BotDifficultyDef[0];
        public ContentMatchPolicy Policy;

        /// <summary>Maps of <paramref name="mode"/>, or null if the mode is not in the content.</summary>
        public ContentModeMaps MapsFor(ModeKind mode)
        {
            if (ModeMaps == null) return null;
            for (int i = 0; i < ModeMaps.Length; i++)
                if (ModeMaps[i] != null && ModeMaps[i].Mode == mode) return ModeMaps[i];
            return null;
        }

        /// <summary>Bot skill level <paramref name="difficulty"/>, or null.</summary>
        public BotDifficultyDef FindBotDifficulty(BotDifficulty difficulty)
        {
            if (BotDifficulties == null) return null;
            for (int i = 0; i < BotDifficulties.Length; i++)
                if (BotDifficulties[i] != null && BotDifficulties[i].Difficulty == difficulty) return BotDifficulties[i];
            return null;
        }

        /// <summary>
        /// Defs of one match on map <paramref name="mapIndex"/> in <paramref name="mode"/> — the same function on the server and
        /// on the online client, so both get bit-identical defs (D-083). Shares the def objects of the bundle (no copies).
        /// Called once per match start. Throws <see cref="ArgumentException"/> on an unknown map or mode.
        /// </summary>
        public MatchDefs CreateMatchDefs(int mapIndex, ModeKind mode)
        {
            if (Maps == null || mapIndex < 0 || mapIndex >= Maps.Length)
                throw new ArgumentException("ContentBundle: map index " + mapIndex + " out of range");
            var defs = new MatchDefs { Rules = Rules, Fighters = Fighters, Map = Maps[mapIndex], Mode = mode };
            switch (mode)
            {
                case ModeKind.None:
                    break;
                case ModeKind.Acorns:
                    if (Acorns == null) throw new ArgumentException("ContentBundle: no Acorns mode in the content");
                    defs.Acorns = Acorns;
                    break;
                case ModeKind.LastBeast:
                    if (LastBeast == null) throw new ArgumentException("ContentBundle: no Last Beast mode in the content");
                    defs.LastBeast = LastBeast;
                    defs.NoRespawn = true;
                    break;
                default:
                    throw new ArgumentException("ContentBundle: unsupported mode " + mode);
            }
            defs.Validate();
            return defs;
        }

        /// <summary>Checks the whole bundle (after loading a pack): every map and mode builds valid match defs.</summary>
        public void Validate()
        {
            if (Rules == null) throw new ArgumentException("ContentBundle.Rules is null");
            if (Fighters == null || Fighters.Length == 0) throw new ArgumentException("ContentBundle.Fighters is empty");
            if (Maps == null || Maps.Length == 0) throw new ArgumentException("ContentBundle.Maps is empty");
            if (DefaultMapIndex < 0 || DefaultMapIndex >= Maps.Length) throw new ArgumentException("ContentBundle.DefaultMapIndex out of range");
            if (ModeMaps == null) throw new ArgumentException("ContentBundle.ModeMaps is null");
            for (int m = 0; m < ModeMaps.Length; m++)
            {
                ContentModeMaps entry = ModeMaps[m];
                if (entry == null) throw new ArgumentException("ContentBundle.ModeMaps[" + m + "] is null");
                if (MapsFor(entry.Mode) != entry) throw new ArgumentException("ContentBundle: mode " + entry.Mode + " listed twice");
                entry.Validate(Maps.Length);
                for (int i = 0; i < entry.MapIndices.Length; i++) CreateMatchDefs(entry.MapIndices[i], entry.Mode);
            }
            CreateMatchDefs(DefaultMapIndex, ModeKind.None);
            if (BotBehaviour == null) throw new ArgumentException("ContentBundle.BotBehaviour is null");
            BotBehaviour.Validate();
            if (BotDifficulties == null) throw new ArgumentException("ContentBundle.BotDifficulties is null");
            for (int i = 0; i < BotDifficulties.Length; i++)
            {
                if (BotDifficulties[i] == null) throw new ArgumentException("ContentBundle.BotDifficulties[" + i + "] is null");
                BotDifficulties[i].Validate();
            }
            if (Policy == null) throw new ArgumentException("ContentBundle.Policy is null");
            Policy.Validate();
        }
    }
}
