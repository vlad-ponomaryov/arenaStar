using Arena.Editor.Build;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Setup
{
    public sealed class BuildScriptTests
    {
        [Test]
        public void ParseArgs_NoCustomArgs_DefaultReleasePath()
        {
            var a = BuildScript.ParseArgs(new[] { "Unity.exe", "-batchmode", "-quit" });
            Assert.IsFalse(a.Development);
            Assert.AreEqual("Builds/Android/Arena.apk", a.OutputPath);
        }

        [Test]
        public void ParseArgs_Null_DefaultReleasePath()
        {
            var a = BuildScript.ParseArgs(null);
            Assert.IsFalse(a.Development);
            Assert.AreEqual(BuildScript.DefaultOutputPath, a.OutputPath);
        }

        [Test]
        public void ParseArgs_Development_UsesDevPath()
        {
            var a = BuildScript.ParseArgs(new[] { "Unity.exe", "-arenaDevelopment" });
            Assert.IsTrue(a.Development);
            Assert.AreEqual("Builds/Android/Arena-dev.apk", a.OutputPath);
        }

        [Test]
        public void ParseArgs_Output_OverridesPath()
        {
            var a = BuildScript.ParseArgs(new[] { "-arenaOutput", "out/x.apk", "-arenaDevelopment" });
            Assert.IsTrue(a.Development);
            Assert.AreEqual("out/x.apk", a.OutputPath);
        }

        [Test]
        public void ParseArgs_OutputWithoutValue_IsIgnored()
        {
            var a = BuildScript.ParseArgs(new[] { "-arenaOutput" });
            Assert.AreEqual(BuildScript.DefaultOutputPath, a.OutputPath);
        }

        [Test]
        public void GetBuildScenes_ReturnsThreeScenes()
        {
            Assert.AreEqual(3, BuildScript.GetBuildScenes().Length);
        }
    }
}
