using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class BambooSetup
    {
        [MenuItem("Tools/Tactical Maps/Add Bamboo To Existing Groves")]
        internal static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            var generator = UnityEngine.Object.FindAnyObjectByType<MapGenerator>();
            var builder = UnityEngine.Object.FindAnyObjectByType<MapBuilder>();
            if (generator == null || builder == null) throw new InvalidOperationException("Open the map scene first.");
            TreeSetup.Configure(generator, builder);
            var stage = new SerializedObject(generator.GetComponent<TreePlacementGenerator>());
            var ordinary = stage.FindProperty("treePrefabs"); var bamboo = stage.FindProperty("bambooPrefabs");
            var prefabs = new GameObject[ordinary.arraySize + bamboo.arraySize];
            for (int i = 0; i < ordinary.arraySize; i++) prefabs[i] = (GameObject)ordinary.GetArrayElementAtIndex(i).objectReferenceValue;
            for (int i = 0; i < bamboo.arraySize; i++) prefabs[ordinary.arraySize + i] = (GameObject)bamboo.GetArrayElementAtIndex(i).objectReferenceValue;
            float fraction = stage.FindProperty("bambooFraction").floatValue;
            int seed = stage.FindProperty("seed").intValue;
            float spacing = stage.FindProperty("treeSpacing").floatValue;
            var backgroundSpacing = stage.FindProperty("backgroundSpacing").vector2Value;
            var workbench = UnityEngine.Object.FindAnyObjectByType<TacticalMaps.Testing.MapWorkbench>();
            var point = workbench != null ? workbench.SelectedPoint : Vector2Int.zero;
            var built = (Transform)new SerializedObject(builder).FindProperty("builtRoot").objectReferenceValue;
            if (built == null) throw new InvalidOperationException("Build terrain first.");
            var report = new StringBuilder("Bamboo mixed into existing groves; positions, rotations and scales retained.\n");
            var names = new[] { "Terrain_Main", "Terrain_Surroundings", "Terrain_Horizon" };
            for (int level = 0; level < names.Length; level++)
            {
                var terrain = built.Find(names[level]).GetComponent<Terrain>(); var data = terrain.terrainData;
                var previous = data.treeInstances; var prototypes = data.treePrototypes;
                bool alreadyMixed = previous.Any(t => prototypes[t.prototypeIndex].prefab.name.StartsWith("SM_Env_Bamboo_",StringComparison.Ordinal));
                if (alreadyMixed) { report.AppendLine(names[level] + ": bamboo already present; placements retained."); continue; }
                var positions = previous.Select(t => new Vector3(t.position.x * data.size.x - data.size.x*.5f, t.position.y * data.size.y, t.position.z * data.size.z - data.size.z*.5f)).ToArray();
                float connectivity = spacing * (level == 0 ? 1 : level == 1 ? backgroundSpacing.x : backgroundSpacing.y) * 2.2f;
                var groups = Groups(positions, connectivity);
                var placements = previous.Select((t,i) => new TreePlacement(positions[i], Array.IndexOf(prefabs,prototypes[t.prototypeIndex].prefab),groups[i],t.rotation,t.widthScale,t.heightScale,level)).ToList();
                if (placements.Any(p => p.Prefab < 0 || p.Prefab >= ordinary.arraySize)) throw new InvalidOperationException("Existing tree is not in ordinary palette.");
                int fieldSeed = unchecked(seed ^ point.x*73856093 ^ point.y*19349663 ^ level*83492791);
                BambooMix.Apply(placements,ordinary.arraySize,prefabs.Length,fraction,fieldSeed);
                var next = (TreeInstance[])previous.Clone();
                for (int i = 0; i < next.Length; i++) next[i].prototypeIndex = placements[i].Prefab;
                foreach (var group in placements.GroupBy(p => p.Group))
                    if (!group.Any(p => p.Prefab < ordinary.arraySize)) throw new InvalidOperationException("Bamboo-only grove.");
                Undo.RecordObject(data,"Mix bamboo into groves");
                data.treePrototypes = prefabs.Select(p => new TreePrototype { prefab = p, bendFactor = 0 }).ToArray();
                data.SetTreeInstances(next,false); terrain.Flush(); EditorUtility.SetDirty(data);
                int count = next.Count(t => t.prototypeIndex >= ordinary.arraySize);
                report.AppendLine($"{names[level]}: {count} bamboo / {next.Length} placements ({(double)count/Math.Max(1,next.Length):P1}); {groups.Distinct().Count()} groups, all retain ordinary trees.");
            }
            foreach (var renderer in built.GetComponentsInChildren<InstancedTreeRenderer>()) { renderer.enabled = false; renderer.enabled = true; }
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(builder.gameObject.scene); EditorSceneManager.SaveScene(builder.gameObject.scene);
            Directory.CreateDirectory("Temp/Bamboo"); File.WriteAllText("Temp/Bamboo/Placement.txt",report.ToString()); Debug.Log(report.ToString());
        }

        private static int[] Groups(Vector3[] positions, float reach)
        {
            var parent = Enumerable.Range(0,positions.Length).ToArray();
            int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
            var bins = new Dictionary<Vector2Int,List<int>>();
            for (int i = 0; i < positions.Length; i++)
            {
                var p = positions[i]; var key = new Vector2Int(Mathf.FloorToInt(p.x/reach),Mathf.FloorToInt(p.z/reach));
                for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++)
                    if (bins.TryGetValue(key + new Vector2Int(x,z),out var nearby)) foreach (int other in nearby)
                    { var q = positions[other]; if ((p.x-q.x)*(p.x-q.x)+(p.z-q.z)*(p.z-q.z) <= reach*reach) parent[Find(i)] = Find(other); }
                if (!bins.TryGetValue(key,out var list)) bins.Add(key,list = new List<int>()); list.Add(i);
            }
            for (int i = 0; i < parent.Length; i++) parent[i] = Find(i);
            return parent;
        }
    }
}
