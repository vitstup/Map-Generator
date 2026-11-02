using System;
using System.Collections.Generic;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class FarmBuildingColliderBaker
    {
        private sealed class Slope
        {
            internal readonly List<Vector3> points = new List<Vector3>();
            internal Vector3 normal, direction;
            internal float distance, area;
        }

        internal static void Bake(GameObject root, GameObject source, Vector3 pivot)
        {
            var previous = root.transform.Find("Colliders");
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            foreach (var old in root.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(old);
            var parent = new GameObject("Colliders"); parent.transform.SetParent(root.transform, false);
            Bounds? walls = null, floor = null, steps = null, eaves = null;
            var floors = new List<Bounds>();
            Vector3 stairRise = Vector3.zero;
            var slopes = new List<Slope>[4];
            for (int i = 0; i < slopes.Length; i++) slopes[i] = new List<Slope>();
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !filter.gameObject.activeSelf) continue;
                string name = mesh.name;
                var matrix = Matrix4x4.Translate(-pivot) * source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var bounds = TransformBounds(mesh.bounds, matrix);
                // Platform railings and upright floor modules are not part of the enclosed house.
                if ((name.StartsWith("SM_Bld_Wall_", StringComparison.Ordinal) && !name.Contains("Pillar") && !name.Contains("Glass")) || name.Contains("Alcove")) Include(ref walls, bounds);
                if (name.Contains("Base_Floor") && bounds.size.y < .6f) floors.Add(bounds);
                if (name.Contains("Steps") || name.Contains("Stairs"))
                {
                    Include(ref steps, bounds);
                    using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
                    using (var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
                    {
                        data[0].GetVertices(vertices);
                        Vector3 high = Vector3.zero, low = Vector3.zero; int highCount = 0, lowCount = 0;
                        foreach (var vertex in vertices)
                        {
                            var point = matrix.MultiplyPoint3x4(vertex);
                            if (point.y > bounds.max.y - .08f) { high += point; highCount++; }
                            if (point.y < bounds.min.y + .08f) { low += point; lowCount++; }
                        }
                        if (highCount > 0 && lowCount > 0) stairRise = high / highCount - low / lowCount;
                    }
                }
                if (name.StartsWith("SM_Bld_Roof_Edge_Small", StringComparison.Ordinal) || name.StartsWith("SM_Bld_Roof_Corner_Small", StringComparison.Ordinal)) Include(ref eaves, bounds);
                if (!(name.StartsWith("SM_Bld_Roof_0", StringComparison.Ordinal) || name.StartsWith("SM_Bld_Roof_Half_", StringComparison.Ordinal))) continue;
                using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
                using (var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
                {
                    data[0].GetVertices(vertices);
                    for (int sub = 0; sub < data[0].subMeshCount; sub++)
                    using (var indices = new NativeArray<int>(data[0].GetSubMesh(sub).indexCount, Allocator.Temp))
                    {
                        data[0].GetIndices(indices, sub);
                        for (int i = 0; i + 2 < indices.Length; i += 3)
                        {
                            var a = matrix.MultiplyPoint3x4(vertices[indices[i]]);
                            var b = matrix.MultiplyPoint3x4(vertices[indices[i + 1]]);
                            var c = matrix.MultiplyPoint3x4(vertices[indices[i + 2]]);
                            var normal = Vector3.Cross(b - a, c - a);
                            if (normal.y < 0) normal = -normal;
                            var unit = normal.normalized;
                            if (normal.sqrMagnitude < .00001f || unit.y < .35f || unit.y > .94f) continue;
                            int group = Mathf.Abs(unit.x) > Mathf.Abs(unit.z) ? (unit.x > 0 ? 0 : 1) : (unit.z > 0 ? 2 : 3);
                            // Keep separate roof levels/dormers separate rather than fitting a slab through empty air.
                            float distance = Vector3.Dot(unit, a);
                            var patch = slopes[group].Find(p => Vector3.Dot(p.direction, unit) > .985f && Mathf.Abs(p.distance - distance) < .2f);
                            if (patch == null) { patch = new Slope { direction = unit, distance = distance }; slopes[group].Add(patch); }
                            patch.normal += normal; patch.area += normal.magnitude;
                            patch.points.AddRange(new[] { a, b, c });
                        }
                    }
                }
            }
            float lowest = float.MaxValue;
            foreach (var f in floors) lowest = Mathf.Min(lowest, f.min.y);
            foreach (var f in floors) if (f.min.y < lowest + .25f) Include(ref floor, f);
            if (!walls.HasValue) throw new InvalidOperationException("Cannot identify house walls: " + source.name);
            Box("Body", walls.Value.center, walls.Value.size, Quaternion.identity);
            int roofCount = 0;
            float largestArea = 0;
            foreach (var group in slopes) foreach (var patch in group) largestArea = Mathf.Max(largestArea, patch.area);
            foreach (var group in slopes)
            {
                Slope slope = null;
                foreach (var patch in group) if (slope == null || patch.area > slope.area) slope = patch;
                if (slope == null || slope.area < largestArea * .05f) continue;
                var normal = slope.normal.normalized;
                var along = Mathf.Abs(normal.x) > Mathf.Abs(normal.z) ? Vector3.forward : Vector3.right;
                var rotation = Quaternion.LookRotation(along, normal);
                var inverse = Quaternion.Inverse(rotation);
                Bounds projected = new Bounds(inverse * slope.points[0], Vector3.zero);
                foreach (var point in slope.points) projected.Encapsulate(inverse * point);
                var center = projected.center;
                // Fill the roof volume, including both gables. The upper face stays on the
                // slope while the deep inner face overlaps Body; this is not a hollow roof shell.
                float roofTop = float.MinValue;
                foreach (var point in slope.points) roofTop = Mathf.Max(roofTop, point.y);
                float thickness = Mathf.Max(.6f, (roofTop - walls.Value.max.y) * normal.y + .45f);
                center.y = projected.max.y - thickness * .5f - .02f;
                Box("Roof_" + roofCount++, rotation * center,
                    new Vector3(Mathf.Max(.1f, projected.size.x + .12f), thickness, Mathf.Max(.1f, projected.size.z + .5f)), rotation);
            }
            if (roofCount < 2) throw new InvalidOperationException("Cannot identify at least two roof slopes: " + source.name);
            if (floor.HasValue && parent.transform.childCount < 6) Box("Platform", floor.Value.center, floor.Value.size, Quaternion.identity);
            if (steps.HasValue && parent.transform.childCount < 6)
            {
                var bounds = steps.Value;
                // A buried ramp follows the treads instead of blocking air above the lower steps.
                bool alongX = Mathf.Abs(stairRise.x) > Mathf.Abs(stairRise.z);
                var rise = alongX ? Vector3.right * Mathf.Sign(stairRise.x) : Vector3.forward * Mathf.Sign(stairRise.z);
                float run = alongX ? bounds.size.x : bounds.size.z, width = alongX ? bounds.size.z : bounds.size.x;
                var normal = (Vector3.up * run - rise * bounds.size.y).normalized;
                var rotation = Quaternion.LookRotation(alongX ? Vector3.forward : Vector3.right, normal);
                float depth = Mathf.Max(.2f, bounds.size.y * normal.y + .1f);
                Box("Steps", bounds.center - normal * (depth * .5f + .015f), new Vector3(Mathf.Sqrt(run * run + bounds.size.y * bounds.size.y), depth, width), rotation);
            }
            if (eaves.HasValue && parent.transform.childCount < 6)
            {
                var bounds = eaves.Value;
                Box("Eaves", new Vector3(bounds.center.x, bounds.min.y + .2f, bounds.center.z), new Vector3(bounds.size.x, .4f, bounds.size.z), Quaternion.identity);
            }

            void Box(string name, Vector3 center, Vector3 size, Quaternion rotation)
            {
                var go = new GameObject(name, typeof(BoxCollider)); go.transform.SetParent(parent.transform, false);
                go.transform.localPosition = center; go.transform.localRotation = rotation;
                go.GetComponent<BoxCollider>().size = Vector3.Max(size, Vector3.one * .08f);
            }
        }

        private static void Include(ref Bounds? target, Bounds value)
        { if (target.HasValue) { var b = target.Value; b.Encapsulate(value); target = b; } else target = value; }

        private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            var result = new Bounds(matrix.MultiplyPoint3x4(bounds.center), Vector3.zero);
            for (int i = 0; i < 8; i++) result.Encapsulate(matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return result;
        }

        [MenuItem("Tools/Tactical Maps/Configure House Colliders")]
        internal static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            for (int i = 0; i < 5; i++)
            {
                string path = AgricultureSetup.Folder + "/Buildings/Farmhouse_" + i + ".prefab";
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Synty/PolygonSamuraiEmpire/Prefabs/Buildings/Presets/Building_House_ Small_" + (i + 1).ToString("00") + ".prefab");
                var root = PrefabUtility.LoadPrefabContents(path);
                try { Bake(root, source, -root.transform.Find("Detailed").localPosition); PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            var generator = UnityEngine.Object.FindAnyObjectByType<AgricultureGenerator>();
            var config = new SerializedObject(generator);
            int layer = LayerMask.NameToLayer("Buildings");
            if (layer < 0)
            {
                var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
                var layers = tags.FindProperty("layers");
                for (int i = 8; i < layers.arraySize; i++) if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                { layers.GetArrayElementAtIndex(i).stringValue = "Buildings"; layer = i; break; }
                if (layer < 0) throw new InvalidOperationException("No free Buildings layer.");
                tags.ApplyModifiedPropertiesWithoutUndo();
            }
            config.FindProperty("buildingLayer").intValue = layer; config.ApplyModifiedProperties();
            var library = AssetDatabase.LoadAssetAtPath<AgricultureLibrary>(AgricultureSetup.Folder + "/AgricultureLibrary.asset");
            foreach (var map in UnityEngine.Object.FindObjectsByType<AgricultureMap>(FindObjectsSortMode.None))
            {
                var main = map.transform.Find("Terrain_Main").GetComponent<Terrain>();
                var surroundings = map.transform.Find("Terrain_Surroundings").GetComponent<Terrain>();
                var agriculture = map.transform.Find("Agriculture"); if (agriculture == null) continue;
                foreach (Transform building in agriculture)
                {
                    if (!building.name.StartsWith("FarmBuilding_", StringComparison.Ordinal)) continue;
                    int variant = int.Parse(building.name.Substring("FarmBuilding_".Length));
                    var position = main.transform.InverseTransformPoint(building.position);
                    var local = new Vector2(position.x - main.terrainData.size.x * .5f, position.z - main.terrainData.size.z * .5f);
                    var mainSize = new Vector2(main.terrainData.size.x, main.terrainData.size.z);
                    var surroundingSize = new Vector2(surroundings.terrainData.size.x, surroundings.terrainData.size.z);
                    var oldPhysics = building.Find("Colliders");
                    if (oldPhysics != null) UnityEngine.Object.DestroyImmediate(oldPhysics.gameObject);
                    FarmBuildingPhysics.Configure(building.gameObject, library.houses[variant % library.houses.Length], FarmBuildingPhysics.Level(local, mainSize, surroundingSize),
                        layer, config.FindProperty("surroundingsBuildingColliders").boolValue);
                }
                EditorSceneManager.MarkSceneDirty(map.gameObject.scene); EditorSceneManager.SaveScene(map.gameObject.scene);
            }
            EditorUtility.SetDirty(generator); AssetDatabase.SaveAssets();
        }
    }
}
