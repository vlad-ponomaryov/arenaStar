using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Match;
using Arena.UI.Hud;
using Arena.UI.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Arena.Tests.PlayMode
{
    /// <summary>
    /// Control screenshots of the UI (A.6, Art-Spec §6.5; category <c>Capture</c>, needs <c>Tools/test.ps1 -Platform PlayMode
    /// -Category Capture -Graphics</c>): the menu at 1920×1080 and at a wide phone 2340×1080, the settings panel, the results of a
    /// won training match. The overlay canvases are switched to the camera for the shot (an overlay is not drawn into a render
    /// texture). PNG files go to <c>Tools/out/art/&lt;ARENA_ART_STEP or "ui"&gt;/</c>.
    /// </summary>
    [Category("Capture")]
    public sealed class UiCaptureTests
    {
        private const string StepVariable = "ARENA_ART_STEP";
        private const string DefaultStep = "ui";
        private const int Width = 1920;
        private const int Height = 1080;
        private const int PhoneWidth = 2340;
        private const float StepTimeoutSeconds = 15f;

        [UnityTest]
        public IEnumerator Capture_Menu_Settings_Results()
        {
            string dir = OutputDir();
            SceneFlow.ClearLaunchParams();
            yield return SceneManager.LoadSceneAsync(SceneNames.Menu, LoadSceneMode.Single);
            yield return null;
            MenuScreen menu = Object.FindFirstObjectByType<MenuScreen>();
            Assert.IsNotNull(menu, "MenuScreen in the Menu scene");
            menu.SelectFighter("pix");
            yield return null;

            yield return Capture(dir, "menu", Width, Height);
            yield return Capture(dir, "menu_phone", PhoneWidth, Height);
            menu.SettingsButton.onClick.Invoke();
            yield return null;
            yield return Capture(dir, "settings", Width, Height);
            menu.Settings.CloseButton.onClick.Invoke();

            menu.TrainingButton.onClick.Invoke();
            MatchRunner runner = null;
            yield return WaitFor(() => (runner = FindStartedRunner()) != null, "match started");
            MatchHud hud = Object.FindFirstObjectByType<MatchHud>();
            var state = runner.Simulation.State;
            state.Fighters[runner.LocalSlot].CarriedAcorns = runner.Simulation.Defs.Acorns.TargetAcorns;
            yield return WaitFor(() => state.Mode.CountdownTeam == runner.LocalTeam, "hold countdown started");
            state.Mode.CountdownEndTick = state.Tick + 1;
            yield return WaitFor(() => runner.IsMatchEnded && hud.Results.IsShown, "results shown");
            float until = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < until) yield return null;
            yield return Capture(dir, "results", Width, Height);
            yield return Capture(dir, "results_phone", PhoneWidth, Height);
        }

        /// <summary>
        /// A.7a: the match HUD of a training match — ult flower partly filled (<c>match_hud</c>, also at the wide phone size),
        /// then the charged ult with its glow (<c>match_hud_ult</c>).
        /// </summary>
        [UnityTest]
        public IEnumerator Capture_MatchHud()
        {
            string dir = OutputDir();
            SceneFlow.ClearLaunchParams();
            yield return SceneManager.LoadSceneAsync(SceneNames.Menu, LoadSceneMode.Single);
            yield return null;
            MenuScreen menu = Object.FindFirstObjectByType<MenuScreen>();
            Assert.IsNotNull(menu, "MenuScreen in the Menu scene");
            menu.SelectFighter("pix");
            yield return null;
            menu.TrainingButton.onClick.Invoke();
            MatchRunner runner = null;
            yield return WaitFor(() => (runner = FindStartedRunner()) != null, "match started");
            var state = runner.Simulation.State;
            var defs = runner.Simulation.Defs;
            int slot = runner.LocalSlot;

            // Required charge of the own ult, from the HUD fraction of one point.
            state.Fighters[slot].UltCharge = 1;
            float onePoint = Arena.Core.Sim.Systems.UltSystem.ChargeFraction(defs, in state.Fighters[slot]);
            Assert.Greater(onePoint, 0f, "the fighter has an ult");
            int required = Mathf.RoundToInt(1f / onePoint);
            state.Fighters[slot].UltCharge = Mathf.RoundToInt(required * 0.58f);
            float until = Time.realtimeSinceStartup + 1.2f;
            while (Time.realtimeSinceStartup < until) yield return null;
            yield return Capture(dir, "match_hud", Width, Height);
            yield return Capture(dir, "match_hud_phone", PhoneWidth, Height);

            state.Fighters[slot].UltCharge = required;
            until = Time.realtimeSinceStartup + 0.4f;
            while (Time.realtimeSinceStartup < until) yield return null;
            yield return Capture(dir, "match_hud_ult", Width, Height);
        }

        private static string OutputDir()
        {
            string step = Environment.GetEnvironmentVariable(StepVariable);
            if (string.IsNullOrEmpty(step)) step = DefaultStep;
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "out", "art", step));
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Renders the main camera with every active root canvas into a PNG of the given size. Overlay canvases are not drawn into a
        /// texture and a URP render request draws one camera (no stack), so the canvases go to a separate UI camera far from the scene,
        /// rendered over black and over white; the UI is composited over the 3D shot by difference matting:
        /// result = black + scene × (white − black), exact for the standard alpha blending.
        /// </summary>
        private static IEnumerator Capture(string dir, string name, int width, int height)
        {
            Camera camera = Camera.main;
            Assert.IsNotNull(camera, "main camera");
            var uiGo = new GameObject("UiCaptureCamera");
            uiGo.transform.SetPositionAndRotation(new Vector3(0f, -10000f, 0f), Quaternion.identity);
            Camera ui = uiGo.AddComponent<Camera>();
            ui.enabled = false;
            ui.clearFlags = CameraClearFlags.SolidColor;
            ui.nearClipPlane = 0.1f;
            ui.farClipPlane = 10f;
            var saved = new List<KeyValuePair<Canvas, RenderMode>>();
            try
            {
                Color[] scene = Shot(camera, width, height);
                foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (!canvas.isRootCanvas || !canvas.isActiveAndEnabled) continue;
                    saved.Add(new KeyValuePair<Canvas, RenderMode>(canvas, canvas.renderMode));
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = ui;
                    canvas.planeDistance = 1f;
                }
                // The canvas scaler reads the camera size: give the UI camera a target of the shot size before the layout pass.
                var sizing = new RenderTexture(width, height, 24);
                ui.targetTexture = sizing;
                Canvas.ForceUpdateCanvases();
                yield return null;
                Canvas.ForceUpdateCanvases();
                ui.targetTexture = null;
                sizing.Release();
                Object.Destroy(sizing);

                ui.backgroundColor = Color.black;
                Color[] black = Shot(ui, width, height);
                ui.backgroundColor = Color.white;
                Color[] white = Shot(ui, width, height);
                for (int i = 0; i < scene.Length; i++)
                {
                    // The sRGB targets blend in linear space: matte in linear, store as sRGB.
                    Color b = black[i].linear, w = white[i].linear, c = scene[i].linear;
                    scene[i] = new Color(b.r + c.r * (w.r - b.r), b.g + c.g * (w.g - b.g), b.b + c.b * (w.b - b.b), 1f).gamma;
                }
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.SetPixels(scene);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                Object.Destroy(tex);
                Debug.Log("UiCapture: " + Path.Combine(dir, name + ".png"));
            }
            finally
            {
                foreach (KeyValuePair<Canvas, RenderMode> p in saved)
                    if (p.Key != null) p.Key.renderMode = p.Value;
                Object.Destroy(uiGo);
            }
        }

        private static Color[] Shot(Camera camera, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt;
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
                else camera.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                return tex.GetPixels();
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = previousTarget;
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(tex);
            }
        }

        private static MatchRunner FindStartedRunner()
        {
            if (SceneManager.GetActiveScene().name != SceneNames.Match) return null;
            MatchRunner r = Object.FindFirstObjectByType<MatchRunner>();
            return r != null && r.Simulation != null ? r : null;
        }

        private static IEnumerator WaitFor(Func<bool> condition, string what)
        {
            float end = Time.realtimeSinceStartup + StepTimeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("timed out: " + what);
                yield return null;
            }
        }
    }
}
