using System;
using System.Collections.Generic;
using System.Threading;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using TacticalMaps.Grass;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class GrassSetup
    {
        internal const string Folder = "Assets/MapGenerator/Generated/Grass";
        [MenuItem("Tools/Tactical Maps/Upgrade Meadow Rendering")]
        internal static void Upgrade()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            var system=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();
            if(system==null) {Setup();system=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();}
            var settings=new SerializedObject(system);
            settings.FindProperty("chunkSize").intValue=16;
            settings.FindProperty("chunksPreparedPerFrame").intValue=8;
            settings.FindProperty("candidatesPerAxis").intValue=64;
            settings.FindProperty("maximumResidentChunks").intValue=416;
            settings.FindProperty("geometryDistance").floatValue=144;
            settings.FindProperty("nearDistance").floatValue=84;
            settings.FindProperty("meadowDistance").floatValue=420;
            settings.FindProperty("minimumPixels").floatValue=.7f;
            settings.FindProperty("middleDensity").floatValue=1;
            settings.FindProperty("heightRange").vector2Value=new Vector2(.22f,.72f);
            settings.FindProperty("meadowColor").colorValue=new Color(.32f,.43f,.19f);
            settings.FindProperty("dryColor").colorValue=new Color(.49f,.45f,.25f);
            settings.FindProperty("turfStrength").floatValue=1;
            BindFocus(settings);settings.ApplyModifiedPropertiesWithoutUndo();
            GrassTurfBaker.Bake();
            if(system.CurrentMap!=null) system.Attach(system.CurrentMap);
            EditorUtility.SetDirty(system); AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(system.gameObject.scene);EditorSceneManager.SaveScene(system.gameObject.scene);
        }
        [MenuItem("Tools/Tactical Maps/Apply Smooth Grass Transitions")]
        internal static void SmoothTransitions()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            var system=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();
            if(system==null||system.CurrentMap==null) throw new InvalidOperationException("Configure grass first.");
            EnsureLibrary();
            var so=new SerializedObject(system);
            so.FindProperty("maximumResidentChunks").intValue=416;
            so.FindProperty("geometryDistance").floatValue=144;
            so.FindProperty("nearDistance").floatValue=84;
            so.FindProperty("chunksPreparedPerFrame").intValue=8;
            so.FindProperty("meadowDistance").floatValue=420;
            BindFocus(so);so.ApplyModifiedPropertiesWithoutUndo();
            var built=system.CurrentMap; var weights=new float[3][,,];
            for(int i=0;i<3;i++) {var data=built.Terrains[i].terrainData;weights[i]=data.GetAlphamaps(0,0,data.alphamapWidth,data.alphamapHeight);}
            var surface=new SurfaceData(built.Terrains[0].terrainData.terrainLayers,weights,Vector2.zero);
            var coverage=GrassCoverage.Generate(surface,CancellationToken.None);
            for(int i=0;i<3;i++)
            {
                var texture=built.Coverage[i]; var old=texture.GetPixels32();var updated=coverage.Pixels[i];
                if(updated.Length!=old.Length) throw new InvalidOperationException("Coverage resolution changed. Rebuild grass instead.");
                for(int j=0;j<updated.Length;j++) updated[j].a=old[j].a;
                texture.SetPixels32(updated);texture.Apply(false,false);EditorUtility.SetDirty(texture);
            }
            system.Attach(built); EditorUtility.SetDirty(system);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(system.gameObject.scene);EditorSceneManager.SaveScene(system.gameObject.scene);
            Debug.Log("Smooth grass transitions applied. Beach masks refreshed; existing local exclusions preserved.");
        }
        [MenuItem("Tools/Tactical Maps/Set Up Low Poly Grass")]
        internal static void Setup()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before setup.");
            System.IO.Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var library = EnsureLibrary();
            var builder = UnityEngine.Object.FindAnyObjectByType<MapBuilder>();
            if (builder == null) throw new InvalidOperationException("MapBuilder is required.");
            var root = new SerializedObject(builder).FindProperty("builtRoot").objectReferenceValue as Transform;
            if (root == null) throw new InvalidOperationException("Build a map preview first.");
            var system = UnityEngine.Object.FindAnyObjectByType<GrassSystem>();
            if (system == null) system = new GameObject("GrassSystem").AddComponent<GrassSystem>();
            var so = new SerializedObject(system); so.FindProperty("library").objectReferenceValue = library; so.ApplyModifiedPropertiesWithoutUndo();
            var bs = new SerializedObject(builder); bs.FindProperty("grassSystem").objectReferenceValue = system; bs.ApplyModifiedPropertiesWithoutUndo();
            var terrains = new Terrain[3]; var weights = new float[3][,,]; var sizes = new Vector2[3];
            var names = new[] { "Terrain_Main", "Terrain_Surroundings", "Terrain_Horizon" };
            for (int i = 0; i < 3; i++)
            {
                terrains[i] = root.Find(names[i]).GetComponent<Terrain>(); var data = terrains[i].terrainData;
                weights[i] = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
                sizes[i] = new Vector2(data.size.x, data.size.z);
            }
            var surface = new SurfaceData(terrains[0].terrainData.terrainLayers, weights, Vector2.zero);
            var cover = GrassCoverage.Generate(surface, CancellationToken.None);
            var generator = UnityEngine.Object.FindAnyObjectByType<MapGenerator>();
            var map = new MapData(new Texture2D[3], sizes, terrains[0].terrainData.size.y,
                generator != null ? generator.Settings.SeaLevelMeters : 0, 4000, Vector2Int.zero, surface: surface, grass: cover);
            var old = root.GetComponent<GrassMap>(); if (old != null) UnityEngine.Object.DestroyImmediate(old);
            system.Stage(map, root); var built = root.GetComponent<GrassMap>();
            for (int i = 0; i < 3; i++) Save(built.Coverage[i], Folder + "/Coverage" + i + ".asset");
            system.Attach(built);
            foreach (var t in terrains) EditorUtility.SetDirty(t.materialTemplate);
            EditorUtility.SetDirty(system); EditorUtility.SetDirty(builder); EditorUtility.SetDirty(built);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(root.gameObject.scene); EditorSceneManager.SaveScene(root.gameObject.scene);
            Debug.Log("Low-poly grass configured: GPU near/middle geometry and Terrain coverage. Saved MainScene.");
        }
        internal static GrassLibrary EnsureLibrary()
        {
            var shader = Shader.Find("TacticalMaps/Grass/LowPolyIndirect");
            if (shader == null) throw new InvalidOperationException("Grass shader was not imported.");
            var near = EnsureMesh("GrassNear", 14, true); var middle = EnsureMesh("GrassMiddle", 9, false);
            string materialPath = Folder + "/Grass.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) { material = new Material(shader) { enableInstancing = true }; AssetDatabase.CreateAsset(material, materialPath); }
            var library = AssetDatabase.LoadAssetAtPath<GrassLibrary>(Folder + "/GrassLibrary.asset");
            if (library == null) { library = ScriptableObject.CreateInstance<GrassLibrary>(); AssetDatabase.CreateAsset(library, Folder + "/GrassLibrary.asset"); }
            var so = new SerializedObject(library);
            so.FindProperty("nearMesh").objectReferenceValue = near; so.FindProperty("middleMesh").objectReferenceValue = middle;
            so.FindProperty("simplifiedMesh").objectReferenceValue = GrassMeshFactory.Ensure(Folder + "/GrassSimplified.asset",false,true);
            so.FindProperty("material").objectReferenceValue = material;
            so.FindProperty("compute").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MapGenerator/Shaders/GrassCulling.compute");
            so.ApplyModifiedPropertiesWithoutUndo(); return library;
        }
        private static void BindFocus(SerializedObject settings)
        {
            foreach(var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if(behaviour.GetType().Name!="RTSCameraController") continue;
                var target=new SerializedObject(behaviour).FindProperty("target");
                if(target!=null) settings.FindProperty("focusTarget").objectReferenceValue=target.objectReferenceValue;
                break;
            }
        }
        private static Mesh EnsureMesh(string name, int blades, bool bent) => GrassMeshFactory.Ensure(Folder + "/" + name + ".asset", bent);
        private static void Save(Texture2D texture, string path)
        {
            // Keep existing GUIDs so old previews remain valid; bind the persistent object explicitly.
            var previous = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (previous == null) AssetDatabase.CreateAsset(texture,path);
            else
            {
                // A new unique snapshot avoids mutating another map's shared saved coverage.
                AssetDatabase.CreateAsset(texture,AssetDatabase.GenerateUniqueAssetPath(path));
            }
        }
    }
}

