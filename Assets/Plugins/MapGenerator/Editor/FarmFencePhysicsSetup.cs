using System;
using System.IO;
using System.Text;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class FarmFencePhysicsSetup
    {
        [MenuItem("Tools/Tactical Maps/Configure Fence Colliders")]
        internal static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            var generator = UnityEngine.Object.FindAnyObjectByType<AgricultureGenerator>();
            if (generator == null) throw new InvalidOperationException("Open the map scene first.");
            var config = new SerializedObject(generator);
            int layer = LayerMask.NameToLayer("Fences");
            if (layer < 0)
            {
                var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
                var layers = tags.FindProperty("layers");
                for (int i = 8; i < layers.arraySize; i++) if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                { layers.GetArrayElementAtIndex(i).stringValue = "Fences"; layer = i; break; }
                if (layer < 0) throw new InvalidOperationException("No free Fences layer.");
                tags.ApplyModifiedPropertiesWithoutUndo();
            }
            var selected = config.FindProperty("fenceLayer");
            if (selected.intValue == 0) { selected.intValue = layer; config.ApplyModifiedProperties(); }
            layer = selected.intValue;
            bool surroundings = config.FindProperty("surroundingsFenceColliders").boolValue;
            var library = AssetDatabase.LoadAssetAtPath<AgricultureLibrary>(AgricultureSetup.Folder + "/AgricultureLibrary.asset");
            var report = new StringBuilder();
            foreach (var map in UnityEngine.Object.FindObjectsByType<AgricultureMap>(FindObjectsSortMode.None))
            {
                var agriculture = map.transform.Find("Agriculture"); if (agriculture == null) continue;
                var terrains = new[] { map.transform.Find("Terrain_Main").GetComponent<Terrain>(), map.transform.Find("Terrain_Surroundings").GetComponent<Terrain>(), map.transform.Find("Terrain_Horizon").GetComponent<Terrain>() };
                var size = terrains[0].terrainData.size; var around = terrains[1].terrainData.size;
                var center = terrains[0].transform.position + new Vector3(size.x * .5f, 0, size.z * .5f);
                var mainSize = new Vector2(size.x, size.z); var surroundingSize = new Vector2(around.x, around.z);
                FarmFenceBuilder.BuildColliders(map.farms, library, terrains, center, mainSize, surroundingSize, agriculture, layer, surroundings);
                int sections = 0;
                FarmFenceBuilder.VisitSections(map.farms, library, terrains, center, mainSize, _ => sections++, () => { });
                report.AppendLine(map.name + ": " + sections + " rendered sections; " + agriculture.Find("FenceColliders").childCount + " physics lines; layer " + layer);
                EditorSceneManager.MarkSceneDirty(map.gameObject.scene); EditorSceneManager.SaveScene(map.gameObject.scene);
            }
            EditorUtility.SetDirty(generator); AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene); EditorSceneManager.SaveScene(generator.gameObject.scene);
            Directory.CreateDirectory("Temp/FencePhysics"); File.WriteAllText("Temp/FencePhysics/setup.txt", report.ToString());
        }
    }
}
