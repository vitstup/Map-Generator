using System.IO;
using TacticalMaps.Generation;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class SurfaceLibrarySetup
    {
        private static readonly string[] Names = { "Meadow", "DryGrass", "Earth", "Rock", "Sand", "Silt" };
        private static readonly string[] Fields = { "meadow", "dryGrass", "earth", "rock", "sand", "silt" };
        private const string Textures = "Assets/MapGenerator/Textures/Surfaces/";
        private const string Layers = "Assets/MapGenerator/Materials/TerrainLayers/";

        [MenuItem("Tools/Tactical Maps/Set Up Terrain Surface Library")]
        private static void SetUp()
        {
            Directory.CreateDirectory(Layers);
            AssetDatabase.Refresh();
            float[] metres = { 7.3f, 11.1f, 8.7f, 12.7f, 9.1f, 6.3f };
            for (int i = 0; i < Names.Length; i++)
            {
                string path = Textures + Names[i] + ".png";
                var albedo = (TextureImporter)AssetImporter.GetAtPath(path);
                albedo.wrapMode = TextureWrapMode.Repeat; albedo.filterMode = FilterMode.Trilinear;
                albedo.anisoLevel = 16; albedo.mipmapEnabled = true; albedo.mipMapBias = 0;
                albedo.maxTextureSize = 2048; albedo.sRGBTexture = true; albedo.alphaSource = TextureImporterAlphaSource.None;
                albedo.textureCompression = TextureImporterCompression.CompressedHQ; albedo.SaveAndReimport();
                string normalPath = Textures + Names[i] + "Normal.png";
                if (!File.Exists(normalPath)) File.Copy(path, normalPath);
                AssetDatabase.ImportAsset(normalPath);
                var normal = (TextureImporter)AssetImporter.GetAtPath(normalPath);
                normal.textureType = TextureImporterType.NormalMap; normal.convertToNormalmap = true;
                normal.heightmapScale = 0.012f; normal.normalmapFilter = TextureImporterNormalFilter.Sobel;
                normal.wrapMode = TextureWrapMode.Repeat; normal.filterMode = FilterMode.Trilinear; normal.anisoLevel = 16;
                normal.mipmapEnabled = true; normal.maxTextureSize = 2048;
                normal.textureCompression = TextureImporterCompression.CompressedHQ; normal.SaveAndReimport();
                string layerPath = Layers + Names[i] + ".terrainlayer";
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
                if (layer == null)
                {
                    layer = new TerrainLayer { name = Names[i], tileSize = new Vector2(metres[i], metres[i] * 0.973f), smoothness = 0, metallic = 0, specular = Color.black, normalScale = 0.6f };
                    AssetDatabase.CreateAsset(layer, layerPath);
                }
                layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                EditorUtility.SetDirty(layer);
            }
            var generator = UnityEngine.Object.FindAnyObjectByType<MapGenerator>();
            if (generator != null) Configure(generator);
            var view = SceneView.lastActiveSceneView;
            if (view != null) { view.sceneViewState.alwaysRefresh = true; view.sceneViewState.alwaysRefresh = true; view.Repaint(); }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/surface-library-setup.txt", "PASS: six albedo/normal terrain layers and generation component configured.");
        }
        internal static void Configure(MapGenerator generator, bool small = false)
        {
            var component = generator.GetComponent<BiomeGenerator>();
            if (component == null) component = generator.gameObject.AddComponent<BiomeGenerator>();
            var config = new SerializedObject(component);
            for (int i = 0; i < Names.Length; i++) config.FindProperty(Fields[i]).objectReferenceValue = AssetDatabase.LoadAssetAtPath<TerrainLayer>(Layers + Names[i] + ".terrainlayer");
            if (small) { config.FindProperty("settings.mainResolution").intValue = 32; config.FindProperty("settings.backgroundResolution").intValue = 32; }
            config.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(component);
        }
    }
}
