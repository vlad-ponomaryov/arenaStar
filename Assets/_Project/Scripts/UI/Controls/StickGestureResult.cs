namespace Arena.UI.Controls
{
    /// <summary>Outcome of releasing an attack/ult stick (GDD §5.1).</summary>
    public enum StickGestureResult
    {
        /// <summary>Held without dragging longer than a tap: nothing happens.</summary>
        None = 0,

        /// <summary>Short tap without dragging: fire with auto-aim.</summary>
        Tap = 1,

        /// <summary>Dragged beyond the dead zone and released outside the cancel zone: fire in the dragged direction.</summary>
        Manual = 2,

        /// <summary>Dragged, then returned into the cancel zone in the centre and released: no shot.</summary>
        Cancel = 3
    }
}
