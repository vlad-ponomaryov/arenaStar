using System;

namespace Arena.Gameplay.View
{
    /// <summary>View prefab of one fighter, by the fighter's data id (D-113); see <see cref="ViewCatalog.FighterPrefab"/>.</summary>
    [Serializable]
    public struct FighterVisual
    {
        public string FighterId;
        public FighterView Prefab;
        /// <summary>Showcase model for menus, fighter select and results (finer, about 15-30k triangles, D-122); null = <see cref="Prefab"/>.</summary>
        public FighterView HeroPrefab;
    }
}
