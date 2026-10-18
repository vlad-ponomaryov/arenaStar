using System;

namespace Arena.Core.Defs
{
    /// <summary>
    /// Whom an effect may affect, relative to the source fighter (D-011). Flags: Kora's tea is <c>Enemy</c> damage plus
    /// <c>Ally</c> heal in one ability. There is no friendly fire (GDD §5.2): damage effects use <c>Enemy</c>.
    /// </summary>
    [Flags]
    public enum TargetFilter : byte
    {
        None = 0,
        Enemy = 1 << 0,
        Ally = 1 << 1,
        Self = 1 << 2
    }
}
