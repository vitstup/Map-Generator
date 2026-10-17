using System;
using System.IO;
using TacticalMaps.Construction;
using TacticalMaps.Testing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class MapPreviewTools
    {
        [MenuItem("Tools/Tactical Maps/Rebuild Saved Map Preview")]
        private static void Rebuild()
        {
            try
            {
                if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (scene.path != "Assets/Scenes/MainScene.unity") throw new InvalidOperationException("Open MainScene.");
                var view = UnityEngine.Object.FindAnyObjectByType<MapWorkbench>();
                var builder = UnityEngine.Object.FindAnyObjectByType<MapBuilder>();
                if (view == null || builder == null) throw new InvalidOperationException("Map workbench and builder are required.");
                TreeSetup.Configure(view.Generator, builder);
                var map = view.Generator.Generate(view.SelectedPoint);
                try { builder.Build(map); }
                catch { map.Release(); throw; }
                const string folder = "Assets/MapGenerator/Generated/MapPreview";
                Directory.CreateDirectory(folder); AssetDatabase.Refresh();
                // Replace only files produced by this tool, after a successful transactional build.
                for (int i = 0; i < 3; i++) Save(map.Heightmaps[i], folder + "/Heightmap" + (i + 1) + ".asset");
                ConstructionSetup.SavePreviewResources(builder, folder, true);
                view.DisplayResults(map); view.SyncControls();
                EditorUtility.SetDirty(view); EditorUtility.SetDirty(builder);
                AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                File.WriteAllText("Temp/map-preview-result.txt", "PASS: saved rebuilt map preview, color maps and transparent water.");
            }
            catch (Exception e) { File.WriteAllText("Temp/map-preview-result.txt", e.ToString()); Debug.LogException(e); }
        }
        private static void Save(UnityEngine.Object asset, string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }
    }
}
