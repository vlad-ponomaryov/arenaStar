using UnityEditor;

namespace Arena.Editor.Art.Fighters
{
    /// <summary>
    /// Import settings of the fighter source models in <see cref="Folder"/> (A-Theo3D): the FBX is only a source of the mesh,
    /// bones and vertex colours for <see cref="FighterModelBuilder"/> (which saves its own mesh asset), so no materials, no
    /// animation, no avatar, axis conversion baked, readable mesh, normals recalculated (the Blender export has flat faces).
    /// </summary>
    public sealed class FighterModelImporter : AssetPostprocessor
    {
        public const string Folder = "Assets/_Project/Art/Models/Fighters";
        /// <summary>Textures of the source bodies: RGB = sRGB colour, A = gloss (data, not transparency).</summary>
        public const string TextureFolder = "Assets/_Project/Art/Textures/Fighters";
        public const int TextureMaxSize = 1024;
        /// <summary>Smoothing angle of the recalculated normals: smooth surfaces, hard edges on plates and boots.</summary>
        public const float NormalSmoothingAngle = 60f;

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder + "/")) return;
            var importer = (ModelImporter)assetImporter;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.bakeAxisConversion = true;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importNormals = ModelImporterNormals.Calculate;
            importer.normalSmoothingAngle = NormalSmoothingAngle;
            importer.importTangents = ModelImporterTangents.None;
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshVertices = false;
            importer.optimizeMeshPolygons = false;
            importer.optimizeGameObjects = false;
            importer.skinWeights = ModelImporterSkinWeights.Standard;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(TextureFolder + "/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.maxTextureSize = TextureMaxSize;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = TextureMaxSize, format = TextureImporterFormat.ASTC_5x5,
                textureCompression = TextureImporterCompression.Compressed
            });
        }
    }
}
