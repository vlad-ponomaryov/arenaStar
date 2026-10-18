using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Arena.Editor.Setup;
using UnityEditor;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace Arena.Editor.Build
{
    /// <summary>
    /// One-command Android build (APK) from the enabled scenes in Build Settings.
    /// Run: Tools/build-android.ps1 [-Development]  (or -executeMethod Arena.Editor.Build.BuildScript.BuildAndroid
    /// [-arenaDevelopment] [-arenaOutput path]). Also used by GameCI (.github/workflows/android-build.yml).
    /// </summary>
    public static class BuildScript
    {
        public const string DevelopmentArg = "-arenaDevelopment";
        public const string OutputArg = "-arenaOutput";
        public const string DefaultOutputPath = "Builds/Android/Arena.apk";
        public const string DefaultDevelopmentOutputPath = "Builds/Android/Arena-dev.apk";
        public const string ResultMarker = "ARENA_BUILD_RESULT";

        public static void BuildAndroid()
        {
            int exitCode = 1;
            try
            {
                exitCode = Build(ParseArgs(Environment.GetCommandLineArgs())) ? 0 : 1;
            }
            catch (Exception e)
            {
                Debug.LogError("Arena.Editor.Build.BuildScript: build threw " + e);
                Debug.Log(ResultMarker + " result=Failed size=0 seconds=0 path=");
            }
            EditorApplication.Exit(exitCode);
        }

        /// <summary>Pure parser for the custom command line arguments (tested in BuildScriptTests).</summary>
        public static BuildArgs ParseArgs(string[] args)
        {
            var result = new BuildArgs();
            string output = null;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    if (string.Equals(a, DevelopmentArg, StringComparison.OrdinalIgnoreCase))
                        result.Development = true;
                    else if (string.Equals(a, OutputArg, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                        output = args[++i];
                }
            }
            if (string.IsNullOrEmpty(output))
                output = result.Development ? DefaultDevelopmentOutputPath : DefaultOutputPath;
            result.OutputPath = output;
            return result;
        }

        /// <summary>Enabled scene paths from Build Settings, in order.</summary>
        public static string[] GetBuildScenes()
        {
            var list = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                    list.Add(scene.path);
            return list.ToArray();
        }

        private static bool Build(BuildArgs args)
        {
            ProjectConfigurator.Apply();
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.Android.useCustomKeystore = false; // debug keystore (D-034)

            string fullPath = Path.GetFullPath(args.OutputPath);
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string[] scenes = GetBuildScenes();
            if (scenes.Length == 0)
                throw new InvalidOperationException("Arena.Editor.Build.BuildScript: no enabled scenes in Build Settings");

            var options = BuildOptions.None;
            if (args.Development) options |= BuildOptions.Development | BuildOptions.AllowDebugging;

            var playerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = fullPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = options
            };

            var sw = Stopwatch.StartNew();
            BuildReport report = BuildPipeline.BuildPlayer(playerOptions);
            sw.Stop();

            bool ok = report.summary.result == BuildResult.Succeeded && File.Exists(fullPath);
            long size = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
            Debug.Log(string.Format("{0} result={1} size={2} seconds={3} path={4}", ResultMarker,
                ok ? "Succeeded" : "Failed", size, (int)sw.Elapsed.TotalSeconds, fullPath.Replace('\\', '/')));
            if (!ok)
                Debug.LogError("Arena.Editor.Build.BuildScript: build result " + report.summary.result +
                               ", errors: " + report.summary.totalErrors);
            return ok;
        }
    }
}
