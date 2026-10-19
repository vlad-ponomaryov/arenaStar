using System;
using System.Collections.Generic;
using System.IO;
using Arena.Data;
using Arena.Editor.Build;
using Arena.Editor.Data;
using Arena.Gameplay.Cameras;
using Arena.Gameplay.Flow;
using Arena.Gameplay.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Arena.Editor.Art.Capture
{
    /// <summary>
    /// Control screenshots of the art (Art-Spec §6.5). Needs a graphics device: run with
    /// <c>Tools/unity-method.ps1 -Graphics -Method Arena.Editor.Art.Capture.ArtCapture.CaptureAll [-Extra '-artStep A.1a']</c>.
    /// Opens the Match scene (its camera and light, nothing saved), builds the <see cref="CaptureStage"/> and writes PNGs to
    /// <c>Tools/out/art/&lt;step&gt;/</c>: lineup (team 0 camera), lineup_team1 (the other side), lineup_zoom2 (the same view at 2×
    /// around the fighters), silhouettes64 (black fills, fighter 64 px high, gameplay angle and straight from above), map (top view)
    /// and protan/deutan versions of lineup, lineup_team1 and map.
    /// </summary>
    public static class ArtCapture
    {
        public const string OutRoot = "Tools/out/art";
        public const string StepArgument = "-artStep";
        public const string DefaultStep = "latest";
        public const string DoneMarker = "ARENA_CAPTURE_DONE";

        private const int ShotWidth = 1920;
        private const int ShotHeight = 1080;
        private const int SilhouetteHeightPx = 64;
        private const int SilhouetteCellPx = 96;
        private const int SilhouetteUpscale = 3;
        private const float MapMarginCells = 0.5f;
        private const float OrthoCameraDistance = 30f;
        private static readonly Color SilhouetteBackground = Color.white;

        public static void CaptureAll()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Arena.Editor.Art.Capture.ArtCapture: no graphics device (run with -Graphics, without -nographics)");

            string dir = Path.Combine(OutRoot, ReadStep(Environment.GetCommandLineArgs()));
            Directory.CreateDirectory(dir);

            EditorSceneManager.OpenScene(SceneBuilder.ScenePath(SceneNames.Match), OpenSceneMode.Single);
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>(ViewAssetBuilder.ViewCatalogPath);
            if (database == null || catalog == null || database.Camera == null)
                throw new InvalidOperationException("Arena.Editor.Art.Capture.ArtCapture: missing GameDatabase/ViewCatalog/CameraConfig");
            Camera camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("Arena.Editor.Art.Capture.ArtCapture: Match scene has no main camera");

            CaptureStage stage = CaptureStage.Build(database, catalog);
            try
            {
                CameraConfig config = database.Camera;
                stage.SetLocalTeam(0);
                SaveWithColorBlind(RenderGameplay(camera, config, stage, false, 1), dir, "lineup");
                SaveZoom(camera, config, stage, dir);
                stage.SetLocalTeam(1);
                SaveWithColorBlind(RenderGameplay(camera, config, stage, true, 1), dir, "lineup_team1");
                stage.SetLocalTeam(0);
                SaveWithColorBlind(RenderMap(camera, stage), dir, "map");
                SaveSilhouettes(camera, config, stage, dir);
            }
            finally
            {
                stage.Destroy();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            Debug.Log(DoneMarker + " " + Path.GetFullPath(dir));
        }

        /// <summary>The value after <see cref="StepArgument"/>, or <see cref="DefaultStep"/>; only letters, digits, '.', '-', '_'.</summary>
        public static string ReadStep(string[] args)
        {
            for (int i = 0; args != null && i + 1 < args.Length; i++)
            {
                if (args[i] != StepArgument) continue;
                string v = args[i + 1];
                bool ok = v.Length > 0 && !v.Contains("..");
                foreach (char ch in v)
                    if (!char.IsLetterOrDigit(ch) && ch != '.' && ch != '-' && ch != '_') ok = false;
                return ok ? v : DefaultStep;
            }
            return DefaultStep;
        }

        private static Vector3 GameplayFocus(CameraConfig config, CaptureStage stage, bool rotated) =>
            CameraMath.Focus(new Vector3(stage.Width * 0.5f, 0f, CaptureStage.RowZ), new Vector2(stage.Width, stage.Height), rotated,
                config.ForwardOffset, 0f);

        private static void PlaceGameplay(Camera camera, CameraConfig config, CaptureStage stage, bool rotated)
        {
            camera.orthographic = false;
            camera.fieldOfView = config.FieldOfView;
            Quaternion rotation = CameraMath.Rotation(config.PitchDegrees, rotated);
            camera.transform.SetPositionAndRotation(CameraMath.Position(GameplayFocus(config, stage, rotated), rotation, config.Distance), rotation);
        }

        private static Texture2D RenderGameplay(Camera camera, CameraConfig config, CaptureStage stage, bool rotated, int scale)
        {
            PlaceGameplay(camera, config, stage, rotated);
            return Render(camera, ShotWidth * scale, ShotHeight * scale);
        }

        /// <summary>The team 0 view rendered at 2× and cropped to 1920×1080 around the fighter row: real perspective, more detail.</summary>
        private static void SaveZoom(Camera camera, CameraConfig config, CaptureStage stage, string dir)
        {
            Texture2D big = RenderGameplay(camera, config, stage, false, 2);
            Vector3 p = camera.WorldToViewportPoint(new Vector3(stage.Width * 0.5f, 0.5f, CaptureStage.RowZ));
            int x = Mathf.Clamp(Mathf.RoundToInt(p.x * big.width) - ShotWidth / 2, 0, big.width - ShotWidth);
            int y = Mathf.Clamp(Mathf.RoundToInt(p.y * big.height) - ShotHeight / 2, 0, big.height - ShotHeight);
            var crop = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGBA32, false);
            crop.SetPixels32(Crop(big, x, y, ShotWidth, ShotHeight));
            crop.Apply();
            Object.DestroyImmediate(big);
            Save(crop, dir, "lineup_zoom2");
            Object.DestroyImmediate(crop);
        }

        private static Texture2D RenderMap(Camera camera, CaptureStage stage)
        {
            camera.orthographic = true;
            float aspect = ShotWidth / (float)ShotHeight;
            camera.orthographicSize = Mathf.Max(stage.Height * 0.5f, stage.Width * 0.5f / aspect) + MapMarginCells;
            var centre = new Vector3(stage.Width * 0.5f, 0f, stage.Height * 0.5f);
            Quaternion rotation = Quaternion.Euler(90f, 0f, 0f);
            camera.transform.SetPositionAndRotation(centre + Vector3.up * OrthoCameraDistance, rotation);
            Texture2D tex = Render(camera, ShotWidth, ShotHeight);
            camera.orthographic = false;
            return tex;
        }

        /// <summary>
        /// Row 1: each fighter from the gameplay angle (orthographic), black on white, 64 px high in a 96 px cell; row 2: the same
        /// fighter straight from above at the same scale. The ring, the blob shadow and the map are hidden.
        /// </summary>
        private static void SaveSilhouettes(Camera camera, CameraConfig config, CaptureStage stage, string dir)
        {
            int n = stage.FighterCount;
            var sheet = new Texture2D(SilhouetteCellPx * n, SilhouetteCellPx * 2, TextureFormat.RGBA32, false);
            var black = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            black.SetColor("_BaseColor", Color.black);
            CameraClearFlags oldFlags = camera.clearFlags;
            Color oldBackground = camera.backgroundColor;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = SilhouetteBackground;
            camera.orthographic = true;
            stage.MapRoot.SetActive(false);
            var saved = new List<KeyValuePair<Renderer, Material[]>>();
            try
            {
                for (int i = 0; i < n; i++) stage.Fighter(i).gameObject.SetActive(false);
                for (int i = 0; i < n; i++)
                {
                    FighterView view = stage.Fighter(i);
                    view.gameObject.SetActive(true);
                    view.SetPose(view.transform.position, Vector3.back);
                    Bounds bounds = Blacken(view, black, saved);
                    Quaternion side = CameraMath.Rotation(config.PitchDegrees, false);
                    camera.orthographicSize = OrthoSizeFor(bounds, side);
                    Blit(sheet, RenderOrtho(camera, bounds.center, side), i * SilhouetteCellPx, SilhouetteCellPx);
                    Blit(sheet, RenderOrtho(camera, bounds.center, Quaternion.Euler(90f, 0f, 0f)), i * SilhouetteCellPx, 0);
                    Restore(saved);
                    view.gameObject.SetActive(false);
                }
            }
            finally
            {
                Restore(saved);
                for (int i = 0; i < n; i++) stage.Fighter(i).gameObject.SetActive(true);
                stage.MapRoot.SetActive(true);
                camera.orthographic = false;
                camera.clearFlags = oldFlags;
                camera.backgroundColor = oldBackground;
                Object.DestroyImmediate(black);
            }
            sheet.Apply();
            Save(sheet, dir, "silhouettes64");
            Texture2D upscaled = Upscale(sheet, SilhouetteUpscale);
            Save(upscaled, dir, "silhouettes64_x3");
            Object.DestroyImmediate(upscaled);
            Object.DestroyImmediate(sheet);
        }

        /// <summary>Swaps every renderer of the fighter except the ring and the shadow to black; returns their world bounds.</summary>
        private static Bounds Blacken(FighterView view, Material black, List<KeyValuePair<Renderer, Material[]>> saved)
        {
            var bounds = new Bounds(view.transform.position, Vector3.zero);
            bool first = true;
            foreach (Renderer r in view.GetComponentsInChildren<Renderer>(true))
            {
                if (r == view.Ring || r == view.Shadow) { saved.Add(new KeyValuePair<Renderer, Material[]>(r, null)); r.enabled = false; continue; }
                saved.Add(new KeyValuePair<Renderer, Material[]>(r, r.sharedMaterials));
                var mats = new Material[r.sharedMaterials.Length];
                for (int m = 0; m < mats.Length; m++) mats[m] = black;
                r.sharedMaterials = mats;
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            return bounds;
        }

        private static void Restore(List<KeyValuePair<Renderer, Material[]>> saved)
        {
            foreach (KeyValuePair<Renderer, Material[]> kv in saved)
            {
                if (kv.Key == null) continue;
                if (kv.Value == null) kv.Key.enabled = true;
                else kv.Key.sharedMaterials = kv.Value;
            }
            saved.Clear();
        }

        /// <summary>Orthographic size that makes the bounds' vertical extent on screen (camera up axis) 64 px of a 96 px cell.</summary>
        private static float OrthoSizeFor(Bounds b, Quaternion rotation)
        {
            Vector3 up = rotation * Vector3.up;
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                float d = Vector3.Dot(corner, up);
                min = Mathf.Min(min, d);
                max = Mathf.Max(max, d);
            }
            float extent = Mathf.Max(max - min, 0.01f);
            return extent * SilhouetteCellPx / (2f * SilhouetteHeightPx);
        }

        private static Texture2D RenderOrtho(Camera camera, Vector3 centre, Quaternion rotation)
        {
            camera.transform.SetPositionAndRotation(centre - rotation * Vector3.forward * OrthoCameraDistance, rotation);
            return Render(camera, SilhouetteCellPx, SilhouetteCellPx);
        }

        private static void Blit(Texture2D sheet, Texture2D cell, int x, int y)
        {
            sheet.SetPixels32(x, y, cell.width, cell.height, cell.GetPixels32());
            Object.DestroyImmediate(cell);
        }

        private static Color32[] Crop(Texture2D src, int x0, int y0, int w, int h)
        {
            Color32[] from = src.GetPixels32();
            var to = new Color32[w * h];
            for (int y = 0; y < h; y++)
                Array.Copy(from, (y0 + y) * src.width + x0, to, y * w, w);
            return to;
        }

        private static Texture2D Upscale(Texture2D src, int factor)
        {
            Color32[] from = src.GetPixels32();
            int w = src.width * factor, h = src.height * factor;
            var to = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    to[y * w + x] = from[(y / factor) * src.width + x / factor];
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(to);
            tex.Apply();
            return tex;
        }

        /// <summary>Renders the camera into an sRGB render texture and reads it back (opaque).</summary>
        private static Texture2D Render(Camera camera, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            camera.targetTexture = rt;
            camera.aspect = width / (float)height;
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
                else camera.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                Color32[] px = tex.GetPixels32();
                for (int i = 0; i < px.Length; i++) px[i].a = 255;
                tex.SetPixels32(px);
                tex.Apply();
                return tex;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.ResetAspect();
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(rt);
            }
        }

        private static void SaveWithColorBlind(Texture2D tex, string dir, string name)
        {
            Save(tex, dir, name);
            Color32[] source = tex.GetPixels32();
            SaveFiltered(tex, source, ColorBlindFilter.Protanopia, dir, name + "_protan");
            SaveFiltered(tex, source, ColorBlindFilter.Deuteranopia, dir, name + "_deutan");
            Object.DestroyImmediate(tex);
        }

        private static void SaveFiltered(Texture2D tex, Color32[] source, float[] matrix, string dir, string name)
        {
            var px = (Color32[])source.Clone();
            ColorBlindFilter.Apply(px, matrix);
            tex.SetPixels32(px);
            tex.Apply();
            Save(tex, dir, name);
        }

        private static void Save(Texture2D tex, string dir, string name)
        {
            string path = Path.Combine(dir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log("ArtCapture: wrote " + path);
        }
    }
}
