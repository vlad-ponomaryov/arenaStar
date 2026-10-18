using System;
using Arena.Core.Defs;
using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// Passive talent in designer units, serialized inside <see cref="FighterConfig"/> (D-011); converted to
    /// <see cref="TalentDef"/>. <see cref="TalentModifier.None"/> = no talent.
    /// </summary>
    [Serializable]
    public sealed class TalentConfig
    {
        public TalentCondition Condition = TalentCondition.Always;
        public TalentModifier Modifier = TalentModifier.None;

        [Tooltip("Size of the modifier in percent (+10 = +10 %).")]
        public int ValuePercent;

        [Tooltip("Condition parameter: health threshold in percent for HealthBelow.")]
        [Min(0)] public int Param;
    }
}
