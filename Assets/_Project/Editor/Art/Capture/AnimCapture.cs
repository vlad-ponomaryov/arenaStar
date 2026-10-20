using System;
using System.Collections.Generic;
using System.IO;
using Arena.Core.Defs;
using Arena.Data;
using Arena.Editor.Build;
using Arena.Editor.Data;
using Arena.Gameplay.Cameras;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Match;
using Arena.Gameplay.View;
using Arena.Gameplay.View.Anim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Arena.Editor.Art.Capture
{
    /// <summary>
    /// Preview of the procedural fighter animation (brief A-TheoAnim §8): one fighter on the capture stage, close up from the
    /// gameplay camera angle, scripted by hand-stepped time (no play mode) — idle with blinks, a run in a circle, three shots,
    /// ult, two hits, roll with a dash, stunned, victory, defeat. Frames every 1/30 s → <c>Tools/out/art/&lt;out&gt;/frames/f_000.png</c>
    /// plus a storyboard sheet; Tools/anim-gif.ps1 turns the frames into a GIF.
    /// Run: Tools/unity-method.ps1 -Graphics -Method Arena.Editor.Art.Capture.AnimCapture.CaptureAll [-Extra '-animFighter pix -animOut theo-anim']
    /// </summary>
    public static class AnimCapture
    {
        public const string FighterArgument = "-animFighter";
        public const string OutArgument = "-animOut";
        public const string DefaultFighter = "pix";
        public const string DefaultOut = "theo-anim";
        public const string DoneMarker = "ARENA_ANIM_CAPTURE_DONE";

        private const float Dt = 1f / 30f;
        private const int FrameWidth = 640;
        private const int FrameHeight = 480;
        private const int Supersample = 2;
        private const int SheetColumns = 5;
        private const int SheetCellWidth = 320;
        private const int SheetCellHeight = 240;
        // Close-up of the gameplay angle: the camera this far from the focus (half the model height above the ground).
        private const float CameraDistance = 4.6f;
        private const int CloseUpWidth = 480;
        private const int CloseUpHeight = 360;
        private const float CloseUpFieldOfView = 30f;
        private const float CloseUpDistance = 0.7f;
        private const float CloseUpRise = 0.1f;
        private const float RunRadius = 1.0f;
        private const float RunSpeed = 3.5f;
        private const float DashSpeed = 5f;
        private static readonly Vector3 Spot = new Vector3(4.5f, 0f, CaptureStage.RowZ);

        private enum Clip { Idle, Run, Settle, Shots, Ult, Hurt, Roll, Stunned, Victory, Defeat }

        private struct Segment
        {
            public Clip Clip;
            public float Seconds;
            /// <summary>Second within the segment that goes into the storyboard sheet (negative = none).</summary>
            public float SheetAt;
            public Segment(Clip clip, float seconds, float sheetAt) { Clip = clip; Seconds = seconds; SheetAt = sheetAt; }
        }

        private static readonly Segment[] Timeline =
        {
            new Segment(Clip.Idle, 2.0f, 0.62f),
            new Segment(Clip.Run, 2.4f, 1.2f),
            new Segment(Clip.Settle, 0.4f, -1f),
            new Segment(Clip.Shots, 1.3f, 0.03f),
            new Segment(Clip.Ult, 1.0f, 0.3f),
            new Segment(Clip.Hurt, 1.0f, 0.57f),
            new Segment(Clip.Roll, 0.8f, 0.2f),
            new Segment(Clip.Stunned, 1.2f, 0.4f),
            new Segment(Clip.Victory, 1.6f, 0.3f),
            new Segment(Clip.Defeat, 1.0f, 0.35f)
        };

        public static void CaptureAll()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("AnimCapture: no graphics device (run with -Graphics, without -nographics)");
            string[] args = Environment.GetCommandLineArgs();
            string fighterId = ReadArg(args, FighterArgument, DefaultFighter);
            string dir = Path.Combine(ArtCapture.OutRoot, ReadArg(args, OutArgument, DefaultOut));
            string framesDir = Path.Combine(dir, "frames");
            if (Directory.Exists(framesDir)) Directory.Delete(framesDir, true);
            Directory.CreateDirectory(framesDir);

            EditorSceneManager.OpenScene(SceneBuilder.ScenePath(SceneNames.Match), OpenSceneMode.Single);
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>(ViewAssetBuilder.ViewCatalogPath);
            if (database == null || catalog == null || database.Camera == null || database.FighterAnim == null)
                throw new InvalidOperationException("AnimCapture: missing GameDatabase/ViewCatalog/CameraConfig/FighterAnim");
            Camera camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("AnimCapture: Match scene has no main camera");
            var runner = Object.FindFirstObjectByType<MatchRunner>();
            Light light = runner != null ? runner.MainLight : null;
            SceneBuilder.ApplyMatchEnvironment();
            MatchLightRig.Apply(light, 0f, catalog);

            // Shaders compile synchronously, so the first frame is not rendered with invisible placeholders.
            bool asyncShaders = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            CaptureStage stage = CaptureStage.Build(database, catalog);
            MuzzleFlashPool muzzles = null;
            var sheetFrames = new List<Texture2D>();
            try
            {
                FighterView view = null;
                for (int i = 0; i < stage.FighterCount; i++)
                {
                    if (stage.FighterId(i) == fighterId && view == null) view = stage.Fighter(i);
                    else stage.Fighter(i).gameObject.SetActive(false);
                }
                if (view == null || view.Rig == null) throw new InvalidOperationException("AnimCapture: no rigged fighter '" + fighterId + "' on the stage");
                FighterAnimConfig config = database.FighterAnim;
                if (catalog.MuzzleFlashPrefab != null) muzzles = new MuzzleFlashPool(catalog.MuzzleFlashPrefab, stage.Root.transform, config);

                CameraConfig cam = database.Camera;
                camera.orthographic = false;
                camera.fieldOfView = cam.FieldOfView;
                Quaternion rotation = CameraMath.Rotation(cam.PitchDegrees, false);
                // A fixed camera (the GIF stays small): aimed between the spot and the centre of the run circle.
                Vector3 focus = Spot + Vector3.forward * RunRadius * 0.5f + Vector3.up * view.Rig.TopHeight * 0.5f;
                camera.transform.SetPositionAndRotation(CameraMath.Position(focus, rotation, CameraDistance), rotation);

                var animator = new FighterAnimator(config, 0);
                animator.Bind(view.Rig);
                Run(view, animator, config, muzzles, camera, framesDir, sheetFrames);
                SaveSheet(sheetFrames, dir, "theo_anim_sheet");
            }
            finally
            {
                for (int i = 0; i < sheetFrames.Count; i++) Object.DestroyImmediate(sheetFrames[i]);
                stage.Destroy();
                ShaderUtil.allowAsyncCompilation = asyncShaders;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            Debug.Log(DoneMarker + " " + Path.GetFullPath(dir));
        }

        private static void Run(FighterView view, FighterAnimator animator, FighterAnimConfig config, MuzzleFlashPool muzzles,
            Camera camera, string framesDir, List<Texture2D> sheet)
        {
            FighterRig rig = view.Rig;
            // Edit mode has no player loop: a skinned mesh is not re-skinned when its bones move. Each frame the skin is baked
            // into a plain mesh renderer that stands in for it (same materials and property block).
            var skin = view.GetComponentInChildren<SkinnedMeshRenderer>();
            var baked = new GameObject("BakedSkin");
            var bakedMesh = new Mesh { name = "BakedSkin" };
            MeshRenderer bakedRenderer = null;
            if (skin != null)
            {
                skin.BakeMesh(bakedMesh, true); // a mesh given to the filter while empty is not drawn after its first bake
                baked.AddComponent<MeshFilter>().sharedMesh = bakedMesh;
                bakedRenderer = baked.AddComponent<MeshRenderer>();
                bakedRenderer.sharedMaterials = skin.sharedMaterials;
                var block = new MaterialPropertyBlock();
                skin.GetPropertyBlock(block);
                bakedRenderer.SetPropertyBlock(block);
                bakedRenderer.shadowCastingMode = skin.shadowCastingMode;
                skin.enabled = false;
            }
            try
            {
                SaveLidCloseUp(view, skin, baked.transform, bakedMesh, camera, Path.GetDirectoryName(framesDir));
                RunFrames(view, animator, config, muzzles, camera, framesDir, sheet, skin, baked.transform, bakedMesh);
            }
            finally
            {
                if (skin != null) skin.enabled = true;
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(bakedMesh);
            }
        }

        /// <summary>
        /// The face from the front, eyelids open (rest, scale 0) and closed (scale 1), side by side → <c>theo_lids.png</c> (AN4 check).
        /// The camera is put back afterwards.
        /// </summary>
        private static void SaveLidCloseUp(FighterView view, SkinnedMeshRenderer skin, Transform baked, Mesh bakedMesh, Camera camera,
            string dir)
        {
            FighterRig rig = view.Rig;
            Transform lidL = rig.Bones[(int)FighterBone.LidL];
            Transform lidR = rig.Bones[(int)FighterBone.LidR];
            Transform eyeL = rig.Bones[(int)FighterBone.EyeL];
            Transform eyeR = rig.Bones[(int)FighterBone.EyeR];
            if (lidL == null || lidR == null || eyeL == null || eyeR == null || skin == null) return;
            Vector3 savedPosition = camera.transform.position;
            Quaternion savedRotation = camera.transform.rotation;
            float savedFov = camera.fieldOfView;
            view.SetPose(Spot, Vector3.back);
            rig.ResetPose();
            Vector3 face = (eyeL.position + eyeR.position) * 0.5f;
            camera.fieldOfView = CloseUpFieldOfView;
            camera.transform.SetPositionAndRotation(face + Vector3.back * CloseUpDistance + Vector3.up * CloseUpRise,
                Quaternion.LookRotation(face - (face + Vector3.back * CloseUpDistance + Vector3.up * CloseUpRise), Vector3.up));
            var sheet = new Texture2D(CloseUpWidth * 2, CloseUpHeight, TextureFormat.RGBA32, false);
            try
            {
                for (int k = 0; k < 2; k++)
                {
                    Vector3 scale = k == 0 ? rig.RestScales[(int)FighterBone.LidL] : Vector3.one;
                    lidL.localScale = scale;
                    lidR.localScale = scale;
                    skin.BakeMesh(bakedMesh, true);
                    baked.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
                    Texture2D big = ArtCapture.Render(camera, CloseUpWidth * Supersample, CloseUpHeight * Supersample);
                    Texture2D small = ArtCapture.Downscale(big, Supersample);
                    sheet.SetPixels32(k * CloseUpWidth, 0, CloseUpWidth, CloseUpHeight, small.GetPixels32());
                    Object.DestroyImmediate(big);
                    Object.DestroyImmediate(small);
                }
                sheet.Apply();
                ArtCapture.Save(sheet, dir, "theo_lids");
            }
            finally
            {
                Object.DestroyImmediate(sheet);
                rig.ResetPose();
                camera.fieldOfView = savedFov;
                camera.transform.SetPositionAndRotation(savedPosition, savedRotation);
            }
        }

        private static void RunFrames(FighterView view, FighterAnimator animator, FighterAnimConfig config, MuzzleFlashPool muzzles,
            Camera camera, string framesDir, List<Texture2D> sheet, SkinnedMeshRenderer skin, Transform baked, Mesh bakedMesh)
        {
            FighterRig rig = view.Rig;
            int frame = 0;
            Vector3 position = Spot;
            Vector3 facing = Vector3.back; // toward the camera
            float runAngle = 0f;
            Vector3 runCentre = Spot + Vector3.forward * RunRadius;
            for (int s = 0; s < Timeline.Length; s++)
            {
                Segment seg = Timeline[s];
                int frames = Mathf.RoundToInt(seg.Seconds / Dt);
                int sheetFrame = seg.SheetAt >= 0f ? Mathf.RoundToInt(seg.SheetAt / Dt) : -1;
                bool stunned = false, dashing = false;
                for (int f = 0; f < frames; f++)
                {
                    float t = f * Dt;
                    Vector3 previous = position;
                    switch (seg.Clip)
                    {
                        case Clip.Idle:
                            if (f == Mathf.RoundToInt(0.5f / Dt) || f == Mathf.RoundToInt(1.4f / Dt)) animator.TriggerBlink();
                            break;
                        case Clip.Run:
                        {
                            // Accelerate for 0.3 s, then a steady circle counter-clockwise seen from above.
                            float speed = RunSpeed * Mathf.Clamp01(t / 0.3f);
                            runAngle += speed * Dt / RunRadius;
                            position = runCentre + new Vector3(-Mathf.Sin(runAngle), 0f, -Mathf.Cos(runAngle)) * RunRadius;
                            break;
                        }
                        case Clip.Settle:
                        {
                            // Walk back to the spot and stop.
                            position = Vector3.MoveTowards(position, Spot, RunSpeed * Dt);
                            break;
                        }
                        case Clip.Shots:
                            facing = Vector3.right;
                            if (f == 0 || f == Mathf.RoundToInt(0.4f / Dt) || f == Mathf.RoundToInt(0.8f / Dt))
                                animator.OnShoot(AbilitySlot.Attack, facing);
                            break;
                        case Clip.Ult:
                            if (f == 0) animator.OnShoot(AbilitySlot.Ult, facing);
                            break;
                        case Clip.Hurt:
                            facing = Vector3.back;
                            if (f == 0) animator.OnHurt(Vector3.left, false);
                            if (f == Mathf.RoundToInt(0.5f / Dt)) animator.OnHurt(Vector3.back, true);
                            break;
                        case Clip.Roll:
                            facing = Vector3.right;
                            if (f == 0) animator.OnShoot(AbilitySlot.Trick, facing);
                            dashing = t < 0.3f;
                            if (dashing) position += Vector3.right * DashSpeed * Dt;
                            else position = Vector3.MoveTowards(position, Spot, RunSpeed * 0.5f * Dt);
                            break;
                        case Clip.Stunned:
                            facing = Vector3.back;
                            position = Vector3.MoveTowards(position, Spot, RunSpeed * Dt);
                            stunned = true;
                            break;
                        case Clip.Victory:
                            if (f == 0) animator.OnMatchEnd(true);
                            break;
                        case Clip.Defeat:
                            if (f == 0)
                            {
                                animator.ResetAll();
                                animator.OnDeath();
                            }
                            break;
                    }

                    Vector3 delta = position - previous;
                    float distance = delta.magnitude;
                    if (seg.Clip == Clip.Run && distance > 0f) facing = delta / distance;
                    animator.OnMove(distance / Dt, distance > 0f ? delta / distance : Vector3.zero, Dt);
                    animator.SetLook(facing);
                    animator.SetStatus(stunned, dashing);
                    muzzles?.Update(Dt);
                    animator.Evaluate(Dt);
                    view.SetPose(position, facing);
                    animator.Apply(rig);
                    if (skin != null)
                    {
                        skin.BakeMesh(bakedMesh, true);
                        baked.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
                    }
                    if (animator.ConsumeMuzzle(out float scale) && muzzles != null
                        && MuzzleFlashPool.MuzzlePoint(rig, config.MuzzleOffset, out Vector3 at, out Quaternion rot))
                        muzzles.Spawn(at, rot, scale);

                    Texture2D big = ArtCapture.Render(camera, FrameWidth * Supersample, FrameHeight * Supersample);
                    Texture2D small = ArtCapture.Downscale(big, Supersample);
                    Object.DestroyImmediate(big);
                    File.WriteAllBytes(Path.Combine(framesDir, "f_" + frame.ToString("000") + ".png"), small.EncodeToPNG());
                    if (f == sheetFrame) sheet.Add(small);
                    else Object.DestroyImmediate(small);
                    frame++;
                }
                Debug.Log("AnimCapture: " + seg.Clip + " → frames up to " + (frame - 1));
            }
            Debug.Log("AnimCapture: wrote " + frame + " frames to " + Path.GetFullPath(framesDir));
        }

        private static void SaveSheet(List<Texture2D> frames, string dir, string name)
        {
            if (frames.Count == 0) return;
            int rows = (frames.Count + SheetColumns - 1) / SheetColumns;
            var sheet = new Texture2D(SheetCellWidth * SheetColumns, SheetCellHeight * rows, TextureFormat.RGBA32, false);
            var clear = new Color32[sheet.width * sheet.height];
            sheet.SetPixels32(clear);
            int factor = FrameWidth / SheetCellWidth;
            for (int i = 0; i < frames.Count; i++)
            {
                Texture2D cell = ArtCapture.Downscale(frames[i], factor);
                int x = (i % SheetColumns) * SheetCellWidth;
                int y = (rows - 1 - i / SheetColumns) * SheetCellHeight;
                sheet.SetPixels32(x, y, cell.width, cell.height, cell.GetPixels32());
                Object.DestroyImmediate(cell);
            }
            sheet.Apply();
            ArtCapture.Save(sheet, dir, name);
            Object.DestroyImmediate(sheet);
        }

        private static string ReadArg(string[] args, string name, string fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name && !string.IsNullOrEmpty(args[i + 1])) return args[i + 1];
            return fallback;
        }
    }
}
