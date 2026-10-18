namespace Arena.Editor.Build
{
    /// <summary>Parsed command line options for <see cref="BuildScript.BuildAndroid"/>.</summary>
    public sealed class BuildArgs
    {
        public bool Development;

        /// <summary>APK path, relative to the project root or absolute.</summary>
        public string OutputPath;
    }
}
