using System;
using System.IO;
using TacticalMaps.Testing;
using TacticalMaps.Construction;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class TerrainWorkbenchSetup
    {
        private const string Root = "Assets/MapGenerator/Generated/Terrains";

        [MenuItem("Tools/Tactical Maps/Set Up Terrain Construction")]
        private static void SetUp()
        {
            try
            {
                if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (scene.path != "Assets/Scenes/MainScene.unity") throw new InvalidOperationException("Open MainScene.");
                var view = UnityEngine.Object.FindAnyObjectByType<MapWorkbench>();
                if (view == null) throw new InvalidOperationException("Workbench is missing.");
                var builder = UnityEngine.Object.FindAnyObjectByType<TerrainBuilder>();
                if (builder == null)
                {
                    builder = new GameObject("TerrainBuilder").AddComponent<TerrainBuilder>();
                    Undo.RegisterCreatedObjectUndo(builder.gameObject, "Create terrain builder");
                }
                Directory.CreateDirectory(Root); AssetDatabase.Refresh();
                ConfigureSurface(builder);
                var serialized = new SerializedObject(view);
                serialized.FindProperty("terrainBuilder").objectReferenceValue = builder;
                if (serialized.FindProperty("buildTerrainButton").objectReferenceValue == null)
                {
                    var button = CreateButton(view.transform);
                    serialized.FindProperty("buildTerrainButton").objectReferenceValue = button;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var map = view.Generator.Generate(view.SelectedPoint);
                view.DisplayResults(map);
                for (int i = 0; i < 3; i++) AssetDatabase.CreateAsset(map.Heightmaps[i], AssetDatabase.GenerateUniqueAssetPath(Root + "/Heightmap" + (i + 1) + ".asset"));
                view.SendMessage("BuildTerrains", SendMessageOptions.RequireReceiver);
                var terrains = builder.GetComponentsInChildren<Terrain>();
                if (terrains.Length != 3) throw new InvalidOperationException("Expected three built terrains.");
                foreach (var terrain in terrains)
                {
                    string path = Root + "/" + terrain.name + ".asset";
                    if (!AssetDatabase.Contains(terrain.terrainData))
                    {
                        if (File.Exists(path)) path = AssetDatabase.GenerateUniqueAssetPath(path);
                        AssetDatabase.CreateAsset(terrain.terrainData, path);
                    }
                }
                EditorUtility.SetDirty(builder); EditorUtility.SetDirty(view);
                AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                Selection.activeGameObject = builder.gameObject;
                File.WriteAllText("Temp/terrain-setup-result.txt", "PASS: TerrainBuilder, controller button and three saved TerrainData assets in MainScene.");
            }
            catch (Exception e) { File.WriteAllText("Temp/terrain-setup-result.txt", e.ToString()); Debug.LogException(e); }
        }

        private static void ConfigureSurface(TerrainBuilder builder)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/SurfaceColor.asset");
            if (texture == null)
            {
                texture = new Texture2D(2, 2) { name = "TerrainSurfaceColor" };
                var color = new Color(0.36f, 0.43f, 0.28f);
                texture.SetPixels(new[] { color, color, color, color }); texture.Apply();
                AssetDatabase.CreateAsset(texture, Root + "/SurfaceColor.asset");
            }
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(Root + "/Surface.terrainlayer");
            if (layer == null)
            {
                layer = new TerrainLayer { name = "TerrainSurface", diffuseTexture = texture, tileSize = new Vector2(10, 10) };
                AssetDatabase.CreateAsset(layer, Root + "/Surface.terrainlayer");
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Terrain.mat");
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")) { name = "TacticalTerrain", enableInstancing = true };
                material.EnableKeyword("_ALPHATEST_ON");
                AssetDatabase.CreateAsset(material, Root + "/Terrain.mat");
            }
            var serialized = new SerializedObject(builder);
            serialized.FindProperty("surfaceLayer").objectReferenceValue = layer;
            serialized.FindProperty("terrainMaterial").objectReferenceValue = material;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static UnityEngine.UI.Button CreateButton(Transform parent)
        {
            var go = new GameObject("BuildTerrainsButton", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(1400, -35); rect.sizeDelta = new Vector2(450, 44);
            var image = go.GetComponent<UnityEngine.UI.Image>(); image.color = new Color(0.14f, 0.42f, 0.33f);
            var button = go.GetComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform; labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            var label = labelGo.GetComponent<TextMeshProUGUI>();
            label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/MapGenerator/Generated/WorkbenchFont.asset");
            label.text = "ПОСТРОИТЬ TERRAIN"; label.fontSize = 19; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            return button;
        }
    }
}
