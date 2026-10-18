using UnityEngine;

namespace Arena.Data
{
    /// <summary>Root of all game configs (fighters, maps, modes...). Filled by DataSeeder in stage 2.</summary>
    [CreateAssetMenu(fileName = "GameDatabase", menuName = "Arena/Game Database")]
    public sealed class GameDatabase : ScriptableObject
    {
    }
}
