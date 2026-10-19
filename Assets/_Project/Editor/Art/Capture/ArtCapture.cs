using System;
using System.Collections.Generic;
using System.IO;
using Arena.Core.Map;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Editor.Art.Fighters;
using Arena.Editor.Build;
using Arena.Editor.Data;
using Arena.Gameplay.Cameras;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Match;
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
    /// around the fighters), states_zoom2 (fighter 0 hit flash, fighter 1 ghost), model_<id> (sheets of the procedural models), model_<id>_hero (the showcase LOD, large), silhouettes64 (black fills, fighter 64 px high, gameplay angle and straight from above), map (top view)
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
        // Model sheets: far from the stage, 480×600 cells; views as (fighter yaw from facing the camera, camera pitch).
        private static readonly Vector3 SheetSpot = new Vector3(-100f, 0f, 0f);
        /// <summary>Hero shots (D-122): match model 3/4, hero model 3/4, hero model front (yaw, pitch).</summary>
        private static readonly Vector2[] HeroViews = { new Vector2(-30f, 10f), new Vector2(-30f, 10f), new Vector2(0f, 8f) };
        private const int HeroCellWidth = 800;
        private const int HeroCellHeight = 1000;
        private const int HeroSupersample = 2;
        private const float HeroDistance = 4.0f;
        private static readonly Vector2[] SheetViews = { new Vector2(0f, 8f), new Vector2(-35f, 12f), new Vector2(180f, 8f), new Vector2(0f, 89f), new Vector2(0f, 55f) };
        private const int SheetCellWidth = 480;
        private const int SheetCellHeight = 600;
        private const float SheetFieldOfView = 30f;
        private const float SheetDistance = 4.4f;

        // Set by CaptureAll for PlaceGameplay: the scene light turned with the camera (MatchLightRig) and its catalog values.
        private static Light s_light;
        private static ViewCatalog s_catalog;

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

            // The light as in a match (MatchLightRig turns it with the camera in PlaceGameplay) and the scene's ambient probe.
            var runner = Object.FindFirstObjectByType<MatchRunner>();
            s_light = runner != null ? runner.MainLight : null;
            s_catalog = catalog;
            if (s_light == null) Debug.LogWarning("Arena.Editor.Art.Capture.ArtCapture: Match scene has no MatchRunner light");
            SceneBuilder.ApplyMatchEnvironment();

            CaptureStage stage = CaptureStage.Build(database, catalog);
            try
            {
                CameraConfig config = database.Camera;
                stage.SetLocalTeam(0);
                SaveWithColorBlind(RenderGameplay(camera, config, stage, false, 1), dir, "lineup");
                SaveZoom(camera, config, stage, dir, "lineup_zoom2");
                // Body variants (D-014): fighter 0 hit flash, fighter 1 hidden in a bush (ghost), the rest normal.
                stage.Fighter(0).SetBodyMaterial(catalog.FighterFlash);
                if (stage.FighterCount > 1) stage.Fighter(1).SetBodyMaterial(catalog.FighterHidden);
                SaveZoom(camera, config, stage, dir, "states_zoom2");
                stage.ResetBodyMaterials();
                stage.SetLocalTeam(1);
                SaveWithColorBlind(RenderGameplay(camera, config, stage, true, 1), dir, "lineup_team1");
                stage.SetLocalTeam(0);
                MatchLightRig.Apply(s_light, 0f, s_catalog); // light and wall shadows as for team 0
                SaveWithColorBlind(RenderMap(camera, stage), dir, "map");
                SaveArena(camera, config, stage, database, dir);
                stage.Root.SetActive(false);
                try { EnvShowcase.Save(camera, config, s_catalog, s_light, dir); }
                finally { stage.Root.SetActive(true); }
                SaveSilhouettes(camera, config, stage, dir);
                SaveModelSheets(camera, config, stage, dir);
                SaveHeroShots(camera, config, stage, dir);
            }
            finally
            {
                stage.Destroy();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            Debug.Log(DoneMarker + " " + Path.GetFullPath(dir));
        }

        /// <summary>Folder of <see cref="CaptureLastBeastMap"/> under <see cref="OutRoot"/>.</summary>
        public const string LastBeastMapStep = "snow_lb";

        /// <summary>Spawn of the first gameplay shot of <see cref="CaptureLastBeastMap"/>: column 7, text row 2 (Map-SnowValley-LB §5 п.6).</summary>
        private static readonly Vector2Int LastBeastSpawnShot = new Vector2Int(7, 2);
        private const int TopViewSize = 2048;
        private const float TopViewMarginCells = 3f;

        /// <summary>
        /// D-140 (Map-SnowValley-LB §5 п.6): the offline Last Beast map (<c>LastBeastMaps[0]</c>) with its theme and decor through the
        /// real <see cref="MapView"/>, no fighters — <c>top.png</c> (straight from above, the whole map and its frame), <c>spawn.png</c>
        /// (the match camera at the spawn of column 7, text row 2) and <c>tree.png</c> (the match camera at the centre spruce), the
        /// gameplay shots 2× supersampled. Run with <c>Tools/unity-method.ps1 -Graphics -Method
        /// Arena.Editor.Art.Capture.ArtCapture.CaptureLastBeastMap</c>; writes to <c>Tools/out/art/snow_lb/</c>.
        /// </summary>
        public static void CaptureLastBeastMap()
        {
            CaptureMap(LastBeastMapStep, database =>
            {
                if (database.LastBeastMaps == null || database.LastBeastMaps.Count == 0)
                    throw new InvalidOperationException("Arena.Editor.Art.Capture.ArtCapture: no LastBeastMaps");
                return database.LastBeastMaps[0];
            }, LastBeastSpawnShot, new[] { "spawn", "tree" }, false);
        }

        /// <summary>Folder of <see cref="CaptureAcornsMap"/> under <see cref="OutRoot"/>.</summary>
        public const string AcornsMapStep = "snow_3x3";

        /// <summary>Middle spawn of team 0 for the base shot of <see cref="CaptureAcornsMap"/>: column 10, text row 28 (map row 2).</summary>
        private static readonly Vector2Int AcornsSpawnShot = new Vector2Int(10, 28);

        /// <summary>
        /// Map-SnowValley §6 п.5 (D-141): the offline Acorns map (<c>DefaultMap</c>) with its theme, decor and hollow (the spruce-hollow
        /// of the theme, <see cref="Arena.Modes.AcornsModeView.CreateHollow"/>), no fighters — <c>top.png</c>, <c>base.png</c> (the team 0
        /// match camera at the middle spawn) and <c>centre.png</c> (the same camera at the hollow). Run with <c>Tools/unity-method.ps1
        /// -Graphics -Method Arena.Editor.Art.Capture.ArtCapture.CaptureAcornsMap</c>; writes to <c>Tools/out/art/snow_3x3/</c>.
        /// </summary>
        public static void CaptureAcornsMap()
        {
            CaptureMap(AcornsMapStep, database =>
            {
                if (database.DefaultMap == null) throw new InvalidOperationException("Arena.Editor.Art.Capture.ArtCapture: no DefaultMap");
                return database.DefaultMap;
            }, AcornsSpawnShot, new[] { "base", "centre" }, true);
        }

        /// <summary>Top view and two match-camera shots (at <paramref name="spawnShot"/> — column, text row — and at the map centre)
        /// of one map through the real <see cref="MapView"/>; <paramref name="acorns"/>: the team 0 Acorns camera and the hollow, else
        /// the free Last Beast camera.</summary>
        private static void CaptureMap(string step, Func<GameDatabase, MapConfig> pick, Vector2Int spawnShot, string[] names, bool acorns)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Arena.Editor.Art.Capture.ArtCapture: no graphics device (run with -Graphics, without -nographics)");
            string dir = Path.Combine(OutRoot, step);
            Directory.CreateDirectory(dir);
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath(SceneNames.Match), OpenSceneMode.Single);
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>(ViewAssetBuilder.ViewCatalogPath);
            if (database == null || catalog == null || database.Camera == null)
                throw new InvalidOperationException("Arena.Editor.Art.Capture.ArtCapture: missing GameDatabase/ViewCatalog/CameraConfig");
            Camera camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("Arena.Editor.Art.Capture.ArtCapture: Match scene has no main camera");
            var runner = Object.FindFirstObjectByType<MatchRunner>();
            s_light = runner != null ? runner.MainLight : null;
            s_catalog = catalog;
            SceneBuilder.ApplyMatchEnvironment();

            MapConfig map = pick(database);
            MapDef def = MapConverter.ToMapDef(map);
            TileGrid grid = def.InitialGrid;
            var root = new GameObject("CaptureMap_" + step);
            try
            {
                MapView view = MapView.Create(root.transform, catalog, in grid, map);
                if (view.Theme != null) camera.backgroundColor = view.Theme.Background;
                if (acorns && def.HasHollow)
                    Arena.Modes.AcornsModeView.CreateHollow(root.transform, catalog, view.Theme, WorldCoords.ToWorld(def.Hollow.Center));
                MatchLightRig.Apply(s_light, 0f, s_catalog);

                camera.orthographic = true;
                camera.orthographicSize = grid.Height * 0.5f + TopViewMarginCells;
                camera.transform.SetPositionAndRotation(new Vector3(grid.Width * 0.5f, OrthoCameraDistance, grid.Height * 0.5f), Quaternion.Euler(90f, 0f, 0f));
                Texture2D top = Render(camera, TopViewSize, TopViewSize);
                Save(top, dir, "top");
                Object.DestroyImmediate(top);
                camera.orthographic = false;

                CameraConfig config = database.Camera;
                var size = new Vector2(grid.Width, grid.Height);
                var spawn = new GridCell(spawnShot.x, grid.Height - 1 - spawnShot.y);
                Vector3[] targets = { WorldCoords.ToWorld(spawn.Center), new Vector3(grid.Width * 0.5f, 0f, grid.Height * 0.5f) };
                Quaternion rotation = CameraMath.Rotation(config.PitchDegrees, false);
                for (int s = 0; s < names.Length && s < targets.Length; s++)
                {
                    Vector3 focus = acorns
                        ? CameraMath.Focus(targets[s], size, false, config.ForwardOffset, config.EdgeInset)
                        : CameraMath.FocusFree(targets[s], size, config.FreeForwardOffset, config.EdgeInset, config.FreeSideInset);
                    camera.fieldOfView = config.FieldOfView;
                    camera.transform.SetPositionAndRotation(CameraMath.Position(focus, rotation, config.Distance), rotation);
                    Texture2D big = Render(camera, ShotWidth * 2, ShotHeight * 2);
                    Texture2D shot = Downscale(big, 2);
                    Object.DestroyImmediate(big);
                    Save(shot, dir, names[s]);
                    Object.DestroyImmediate(shot);
                }
                Debug.Log("ArtCapture: map '" + map.Id + "' decor props " + view.DecorPropCount + ", frame trees " + view.FrameTreeCount +
                          ", boxes " + view.BoxCount + ", breakables " + view.BreakableCount + ", bushes " + view.BushCount);
            }
            finally
            {
                Object.DestroyImmediate(root);
                MapShadowMask.Clear();
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
            MatchLightRig.Apply(s_light, rotated ? CameraMath.RotatedYaw : 0f, s_catalog);
        }

        private static Texture2D RenderGameplay(Camera camera, CameraConfig config, CaptureStage stage, bool rotated, int scale)
        {
            PlaceGameplay(camera, config, stage, rotated);
            return Render(camera, ShotWidth * scale, ShotHeight * scale);
        }

        /// <summary>
        /// A.3c: the real default map (no fighters) through <see cref="MapView"/> from the gameplay camera of team 0 — <c>arena.png</c>
        /// looking at the map centre and <c>arena_base.png</c> at the team 0 spawn side, both 2× supersampled. The stage is hidden.
        /// </summary>
        private static void SaveArena(Camera camera, CameraConfig config, CaptureStage stage, GameDatabase database, string dir)
        {
            if (database.DefaultMap == null) return;
            MapDef def = MapConverter.ToMapDef(database.DefaultMap);
            TileGrid grid = def.InitialGrid;
            stage.Root.SetActive(false);
            var root = new GameObject("CaptureArena");
            try
            {
                MapView.Create(root.transform, s_catalog, in grid);
                var size = new Vector2(grid.Width, grid.Height);
                float[] focusZ = { grid.Height * 0.5f, grid.Height * ArenaBaseFocus };
                string[] names = { "arena", "arena_base" };
                for (int s = 0; s < names.Length; s++)
                {
                    Quaternion rotation = CameraMath.Rotation(config.PitchDegrees, false);
                    Vector3 focus = CameraMath.Focus(new Vector3(grid.Width * 0.5f, 0f, focusZ[s]), size, false, config.ForwardOffset, 0f);
                    camera.orthographic = false;
                    camera.fieldOfView = config.FieldOfView;
                    camera.transform.SetPositionAndRotation(CameraMath.Position(focus, rotation, config.Distance), rotation);
                    MatchLightRig.Apply(s_light, 0f, s_catalog);
                    Texture2D big = Render(camera, ShotWidth * 2, ShotHeight * 2);
                    Texture2D shot = Downscale(big, 2);
                    Object.DestroyImmediate(big);
                    Save(shot, dir, names[s]);
                    Object.DestroyImmediate(shot);
                    if (s != 0) continue;
                    // A.3d camera A/B (D-128): the same frame height at the focus with other lenses (FOV, distance).
                    for (int ab = 0; ab < CameraAlternatives.Length; ab++)
                    {
                        Vector2 lens = CameraAlternatives[ab];
                        camera.fieldOfView = lens.x;
                        camera.transform.SetPositionAndRotation(CameraMath.Position(focus, rotation, lens.y), rotation);
                        Texture2D abBig = Render(camera, ShotWidth * 2, ShotHeight * 2);
                        Texture2D abShot = Downscale(abBig, 2);
                        Object.DestroyImmediate(abBig);
                        Save(abShot, dir, "arena_fov" + Mathf.RoundToInt(lens.x));
                        Object.DestroyImmediate(abShot);
                    }
                    camera.fieldOfView = config.FieldOfView;
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                MapShadowMask.Clear();
                stage.Root.SetActive(true);
            }
        }

        /// <summary>A.3d: other lenses (FOV degrees, distance) of <c>arena_fov&lt;N&gt;.png</c>, all with a 16.8-cell frame at the focus.</summary>
        private static readonly Vector2[] CameraAlternatives = { new Vector2(50f, 18f), new Vector2(30f, 31.3f) };

        /// <summary>Focus of <c>arena_base.png</c> as a fraction of the map height (team 0 spawns at the bottom).</summary>
        private const float ArenaBaseFocus = 0.2f;

        /// <summary>The team 0 view rendered at 2× and cropped to 1920×1080 around the fighter row: real perspective, more detail.</summary>
        private static void SaveZoom(Camera camera, CameraConfig config, CaptureStage stage, string dir, string name)
        {
            Texture2D big = RenderGameplay(camera, config, stage, false, 2);
            Vector3 p = camera.WorldToViewportPoint(new Vector3(stage.Width * 0.5f, 0.5f, CaptureStage.RowZ));
            int x = Mathf.Clamp(Mathf.RoundToInt(p.x * big.width) - ShotWidth / 2, 0, big.width - ShotWidth);
            int y = Mathf.Clamp(Mathf.RoundToInt(p.y * big.height) - ShotHeight / 2, 0, big.height - ShotHeight);
            var crop = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGBA32, false);
            crop.SetPixels32(Crop(big, x, y, ShotWidth, ShotHeight));
            crop.Apply();
            Object.DestroyImmediate(big);
            Save(crop, dir, name);
            Object.DestroyImmediate(crop);
        }

        /// <summary>
        /// For every fighter with a procedural model (<see cref="FighterView.Rig"/>): a close sheet like the Flow concept sheets —
        /// front, 3/4, back, straight from above and the gameplay angle — <c>model_&lt;id&gt;.png</c>, the fighter alone in front of
        /// the camera background, light as for team 0.
        /// </summary>
        private static void SaveModelSheets(Camera camera, CameraConfig config, CaptureStage stage, string dir)
        {
            PlaceGameplay(camera, config, stage, false);
            float fov = camera.fieldOfView;
            camera.fieldOfView = SheetFieldOfView;
            try
            {
                for (int i = 0; i < stage.FighterCount; i++)
                {
                    FighterView view = stage.Fighter(i);
                    if (view.Rig == null) continue;
                    Vector3 home = view.transform.position;
                    Quaternion homeRotation = view.transform.rotation;
                    var sheet = new Texture2D(SheetCellWidth * SheetViews.Length, SheetCellHeight, TextureFormat.RGBA32, false);
                    for (int v = 0; v < SheetViews.Length; v++)
                    {
                        Vector2 yawPitch = SheetViews[v];
                        view.SetPose(SheetSpot, Quaternion.Euler(0f, yawPitch.x, 0f) * Vector3.back);
                        Quaternion rotation = Quaternion.Euler(yawPitch.y, 0f, 0f);
                        Vector3 target = SheetSpot + Vector3.up * view.Rig.TopHeight * 0.5f;
                        camera.transform.SetPositionAndRotation(target - rotation * Vector3.forward * SheetDistance, rotation);
                        Blit(sheet, Render(camera, SheetCellWidth, SheetCellHeight), v * SheetCellWidth, 0);
                    }
                    sheet.Apply();
                    view.transform.SetPositionAndRotation(home, homeRotation);
                    Save(sheet, dir, "model_" + stage.FighterId(i));
                    Object.DestroyImmediate(sheet);
                }
            }
            finally
            {
                camera.fieldOfView = fov;
            }
        }

        /// <summary>
        /// For every fighter with a hero model (D-122): <c>model_&lt;id&gt;_hero.png</c> — the match model at 3/4, the hero model at 3/4
        /// and from the front, large (800 × 1000 per view, 2× supersampled) as on the fighter select screen.
        /// </summary>
        private static void SaveHeroShots(Camera camera, CameraConfig config, CaptureStage stage, string dir)
        {
            PlaceGameplay(camera, config, stage, false);
            float fov = camera.fieldOfView;
            camera.fieldOfView = SheetFieldOfView;
            try
            {
                for (int i = 0; i < stage.FighterCount; i++)
                {
                    FighterView game = stage.Fighter(i);
                    if (game.Rig == null) continue;
                    string id = stage.FighterId(i);
                    var heroPrefab = AssetDatabase.LoadAssetAtPath<FighterView>(FighterModelBuilder.PrefabPath(id, FighterModelQuality.Hero));
                    if (heroPrefab == null) continue;
                    FighterView hero = Object.Instantiate(heroPrefab);
                    Vector3 home = game.transform.position;
                    Quaternion homeRotation = game.transform.rotation;
                    var sheet = new Texture2D(HeroCellWidth * HeroViews.Length, HeroCellHeight, TextureFormat.RGBA32, false);
                    try
                    {
                        for (int v = 0; v < HeroViews.Length; v++)
                        {
                            FighterView shown = v == 0 ? game : hero;
                            FighterView parked = v == 0 ? hero : game;
                            parked.transform.position = SheetSpot + Vector3.right * 50f;
                            Vector2 yawPitch = HeroViews[v];
                            shown.SetPose(SheetSpot, Quaternion.Euler(0f, yawPitch.x, 0f) * Vector3.back);
                            Quaternion rotation = Quaternion.Euler(yawPitch.y, 0f, 0f);
                            Vector3 target = SheetSpot + Vector3.up * shown.Rig.TopHeight * 0.5f;
                            camera.transform.SetPositionAndRotation(target - rotation * Vector3.forward * HeroDistance, rotation);
                            Texture2D big = Render(camera, HeroCellWidth * HeroSupersample, HeroCellHeight * HeroSupersample);
                            Blit(sheet, Downscale(big, HeroSupersample), v * HeroCellWidth, 0);
                            Object.DestroyImmediate(big);
                        }
                        sheet.Apply();
                        Save(sheet, dir, "model_" + id + "_hero");
                    }
                    finally
                    {
                        game.transform.SetPositionAndRotation(home, homeRotation);
                        Object.DestroyImmediate(hero.gameObject);
                        Object.DestroyImmediate(sheet);
                    }
                }
            }
            finally
            {
                camera.fieldOfView = fov;
            }
        }

        /// <summary>Box-filter downscale by an integer factor (antialiasing of a supersampled render).</summary>
        internal static Texture2D Downscale(Texture2D src, int factor)
        {
            int w = src.width / factor, h = src.height / factor;
            Color32[] from = src.GetPixels32();
            var to = new Color32[w * h];
            int n = factor * factor;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int r = 0, g = 0, b = 0, a = 0;
                for (int dy = 0; dy < factor; dy++)
                for (int dx = 0; dx < factor; dx++)
                {
                    Color32 c = from[(y * factor + dy) * src.width + x * factor + dx];
                    r += c.r; g += c.g; b += c.b; a += c.a;
                }
                to[y * w + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
            }
            var result = new Texture2D(w, h, TextureFormat.RGBA32, false);
            result.SetPixels32(to);
            result.Apply();
            return result;
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
        /// Row 1: each fighter from the gameplay angle (orthographic), black on white, its body (without tail and item) 64 px high
        /// in a 96 px cell; row 2: the same
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
            var points = new List<Vector3>();
            try
            {
                for (int i = 0; i < n; i++) stage.Fighter(i).gameObject.SetActive(false);
                for (int i = 0; i < n; i++)
                {
                    FighterView view = stage.Fighter(i);
                    view.gameObject.SetActive(true);
                    view.SetPose(view.transform.position, Vector3.back);
                    Blacken(view, black, saved, points);
                    Quaternion side = CameraMath.Rotation(config.PitchDegrees, false);
                    Quaternion top = Quaternion.Euler(90f, 0f, 0f);
                    camera.orthographicSize = OrthoSizeFor(points, side);
                    Blit(sheet, RenderOrtho(camera, ScreenCentre(points, side), side), i * SilhouetteCellPx, SilhouetteCellPx);
                    Blit(sheet, RenderOrtho(camera, ScreenCentre(points, top), top), i * SilhouetteCellPx, 0);
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

        /// <summary>Swaps every renderer of the fighter except the ring and the shadow to black; collects its body points.</summary>
        private static void Blacken(FighterView view, Material black, List<KeyValuePair<Renderer, Material[]>> saved, List<Vector3> points)
        {
            points.Clear();
            foreach (Renderer r in view.GetComponentsInChildren<Renderer>(true))
            {
                if (r == view.Ring || r == view.Shadow) { saved.Add(new KeyValuePair<Renderer, Material[]>(r, null)); r.enabled = false; continue; }
                saved.Add(new KeyValuePair<Renderer, Material[]>(r, r.sharedMaterials));
                var mats = new Material[r.sharedMaterials.Length];
                for (int m = 0; m < mats.Length; m++) mats[m] = black;
                r.sharedMaterials = mats;
                AddBodyPoints(r, points);
            }
            if (points.Count == 0) points.Add(view.transform.position);
        }

        /// <summary>
        /// World points of the fighter's body: a skinned model's rest-pose vertices without the tail and the held item (A.1b2: a big
        /// tail or a long weapon must not shrink the fighter in the 64 px check, and the measure is the real outline, not a box
        /// whose empty corners shrink a non-convex model), other renderers by their bounds' corners.
        /// </summary>
        private static void AddBodyPoints(Renderer r, List<Vector3> points)
        {
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                Mesh mesh = smr.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                BoneWeight[] weights = mesh.boneWeights;
                Matrix4x4 m = smr.transform.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (weights.Length == vertices.Length)
                    {
                        var bone = (FighterBone)weights[i].boneIndex0;
                        if (bone == FighterBone.Tail || bone == FighterBone.Prop) continue;
                    }
                    points.Add(m.MultiplyPoint3x4(vertices[i]));
                }
                return;
            }
            Bounds b = r.bounds;
            for (int i = 0; i < 8; i++)
                points.Add(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
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

        /// <summary>Orthographic size that makes the points' vertical extent on screen (camera up axis) 64 px of a 96 px cell.</summary>
        private static float OrthoSizeFor(List<Vector3> points, Quaternion rotation)
        {
            Extent(points, rotation * Vector3.up, out float min, out float max);
            float extent = Mathf.Max(max - min, 0.01f);
            return extent * SilhouetteCellPx / (2f * SilhouetteHeightPx);
        }

        /// <summary>A point whose screen position is the middle of the points' screen extent.</summary>
        private static Vector3 ScreenCentre(List<Vector3> points, Quaternion rotation)
        {
            Vector3 up = rotation * Vector3.up, right = rotation * Vector3.right, forward = rotation * Vector3.forward;
            Extent(points, up, out float upMin, out float upMax);
            Extent(points, right, out float rightMin, out float rightMax);
            Extent(points, forward, out float forwardMin, out float forwardMax);
            return up * ((upMin + upMax) * 0.5f) + right * ((rightMin + rightMax) * 0.5f) + forward * ((forwardMin + forwardMax) * 0.5f);
        }

        private static void Extent(List<Vector3> points, Vector3 axis, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            foreach (Vector3 p in points)
            {
                float d = Vector3.Dot(p, axis);
                min = Mathf.Min(min, d);
                max = Mathf.Max(max, d);
            }
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

        internal static Color32[] Crop(Texture2D src, int x0, int y0, int w, int h)
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
        internal static Texture2D Render(Camera camera, int width, int height)
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

        internal static void Save(Texture2D tex, string dir, string name)
        {
            string path = Path.Combine(dir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log("ArtCapture: wrote " + path);
        }
    }
}
