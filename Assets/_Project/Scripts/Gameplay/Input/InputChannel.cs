namespace Arena.Gameplay.Input
{
    /// <summary>Source of local input; the touch channel wins over keyboard/mouse when both are active.</summary>
    public enum InputChannel
    {
        Touch = 0,
        KeyboardMouse = 1
    }
}
