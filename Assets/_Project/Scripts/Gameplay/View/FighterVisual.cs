using System;

namespace Arena.Gameplay.View
{
    /// <summary>View prefab of one fighter, by the fighter's data id (D-113); see <see cref="ViewCatalog.FighterPrefab"/>.</summary>
    [Serializable]
    public struct FighterVisual
    {
        public string FighterId;
        public FighterView Prefab;
    }
}
