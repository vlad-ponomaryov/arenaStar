namespace Arena.Tests.EditMode.Setup
{
    /// <summary>
    /// Shaders a project material may use (Art-Spec §8): URP's own and the project's <c>Arena/*</c>; built-in and legacy shaders
    /// are not allowed (they are stripped or pink on device).
    /// </summary>
    public static class ShaderRules
    {
        public const string UrpPrefix = "Universal Render Pipeline/";
        public const string ArenaPrefix = "Arena/";

        public static bool IsAllowed(string shaderName) =>
            shaderName != null && (shaderName.StartsWith(UrpPrefix, System.StringComparison.Ordinal) ||
                                   shaderName.StartsWith(ArenaPrefix, System.StringComparison.Ordinal));
    }
}
