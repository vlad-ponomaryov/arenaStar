namespace Arena.UI.Controls
{
    /// <summary>
    /// Floating: the stick appears where the finger touched its zone (movement, GDD §5.1). Fixed: stays in place, the offset is
    /// measured from its centre. Anchored (attack / ult, D-129): stays in place, but the offset is measured from the point the
    /// finger touched — a tap anywhere on the zone is a tap, not a drag towards the edge where it landed.
    /// </summary>
    public enum StickMode
    {
        Floating = 0,
        Fixed = 1,
        Anchored = 2
    }
}
