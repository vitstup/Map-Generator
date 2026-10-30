using System;
using System.Collections.Generic;
using TacticalMaps.Construction;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class TreeInstancingSetup
    {
        internal const string Folder = "Assets/Prefabs/Trees";
        private const string LibraryPath = Folder + "/TreeRenderLibrary.asset";

        internal static TreeRenderLibrary EnsureLibrary(IReadOnlyList<GameObject> sources)
        {
            var library = AssetDatabase.LoadAssetAtPath<TreeRenderLibrary>(LibraryPath);
            bool current = library != null && library.SpeciesList.Length == sources.Count;
            for (int i = 0; current && i < sources.Count; i++)
            {
                var species = library.SpeciesList[i];
                current = species.source == sources[i] && species.levels != null && species.levels.Length > 0;
                if (current && library.OptimizationVersion > 0)
                {
                    foreach (var level in species.levels)
                        current &= level.parts != null && level.parts.Length > 0 && Array.TrueForAll(level.parts, p => p.mesh != null && p.material != null && p.material.enableInstancing);
                    continue;
                }
                if (current) foreach (var level in species.levels)
                {
                    if (level.prefab == null) { current = false; break; }
                    foreach (var renderer in level.prefab.GetComponentsInChildren<MeshRenderer>(true))
                        foreach (var material in renderer.sharedMaterials) current &= material != null && material.enableInstancing;
                }
            }
            if (current)
            {
                bool baked = library.ImpostorCulling != null && library.ImpostorMesh != null;
                foreach (var species in library.SpeciesList)
                {
                    baked &= species.impostorMaterial != null;
                    foreach (var level in species.levels) baked &= level.parts != null && level.parts.Length > 0;
                }
                if (library.OptimizationVersion < TreeLodOptimizer.Version) { TreeLodOptimizer.Optimize(library); baked = false; }
                if (!baked) TreeImpostorBaker.Bake(library);
                return library;
            }
            EnsureFolder(Folder); EnsureFolder(Folder + "/Materials");
            var entries = new TreeRenderLibrary.Species[sources.Count];
            var materials = new Dictionary<Material, Material>();
            for (int i = 0; i < sources.Count; i++)
            {
                // Extending the palette must retain the established LOD chain of existing species.
                var existing = library == null ? null : Array.Find(library.SpeciesList, s => s.source == sources[i]);
                if (existing != null && existing.originalLevels != null && existing.originalLevels.Length > 0)
                { entries[i] = existing; continue; }
                var source = sources[i]; var groups = source.GetComponentsInChildren<LODGroup>(true);
                if (groups.Length != 1) throw new InvalidOperationException(source.name + " must have exactly one LODGroup.");
                var group = groups[0]; var lods = group.GetLODs();
                var groupToRoot = source.transform.worldToLocalMatrix * group.transform.localToWorldMatrix;
                var entry = new TreeRenderLibrary.Species
                {
                    source = source, referencePoint = groupToRoot.MultiplyPoint3x4(group.localReferencePoint),
                    rootScale = source.transform.localScale,
                    size = group.size * MaxAbs(groupToRoot.lossyScale), levels = new TreeRenderLibrary.Level[lods.Length]
                };
                for (int lod = 0; lod < lods.Length; lod++)
                {
                    string path = Folder + "/" + source.name + "_LOD" + lod + ".prefab";
                    var root = new GameObject(source.name + "_LOD" + lod);
                    try
                    {
                        foreach (var renderer in lods[lod].renderers)
                        {
                            if (renderer == null || !(renderer is MeshRenderer)) throw new InvalidOperationException("Only mesh tree LODs are supported.");
                            var filter = renderer.GetComponent<MeshFilter>();
                            if (filter == null || filter.sharedMesh == null) throw new InvalidOperationException("Tree LOD mesh is missing.");
                            var part = new GameObject(renderer.name, typeof(MeshFilter), typeof(MeshRenderer)); part.transform.SetParent(root.transform, false);
                            var relative = source.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                            part.transform.localPosition = relative.GetColumn(3); part.transform.localRotation = relative.rotation; part.transform.localScale = relative.lossyScale;
                            part.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                            var output = part.GetComponent<MeshRenderer>(); var assigned = renderer.sharedMaterials;
                            for (int m = 0; m < assigned.Length; m++)
                            {
                                var original = assigned[m];
                                if (original == null) throw new InvalidOperationException("Tree LOD material is missing.");
                                if (!materials.TryGetValue(original, out var instanced))
                                {
                                    string materialPath = Folder + "/Materials/" + original.name + "_" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original)).Substring(0, 8) + "_Instanced.mat";
                                    instanced = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                                    if (instanced == null) { instanced = new Material(original); AssetDatabase.CreateAsset(instanced, materialPath); }
                                    else instanced.CopyPropertiesFromMaterial(original);
                                    instanced.name = System.IO.Path.GetFileNameWithoutExtension(materialPath); instanced.enableInstancing = true;
                                    EditorUtility.SetDirty(instanced); materials.Add(original, instanced);
                                }
                                assigned[m] = instanced;
                            }
                            output.sharedMaterials = assigned; output.shadowCastingMode = renderer.shadowCastingMode;
                            output.receiveShadows = renderer.receiveShadows;
                        }
                        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                        entry.levels[lod] = new TreeRenderLibrary.Level { prefab = prefab, screenHeight = lods[lod].screenRelativeTransitionHeight, parts = PartsOf(prefab) };
                    }
                    finally { UnityEngine.Object.DestroyImmediate(root); }
                }
                entries[i] = entry;
            }
            if (library == null) { library = ScriptableObject.CreateInstance<TreeRenderLibrary>(); AssetDatabase.CreateAsset(library, LibraryPath); }
            library.Configure(entries); EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();
            TreeLodOptimizer.Optimize(library);
            TreeImpostorBaker.Bake(library);
            return library;
        }
        private static TreeRenderLibrary.MeshPart[] PartsOf(GameObject prefab)
        {
            var parts = new List<TreeRenderLibrary.MeshPart>();
            foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                var materials = renderer.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    parts.Add(new TreeRenderLibrary.MeshPart { mesh = mesh, material = materials[Mathf.Min(sub, materials.Length - 1)], submesh = sub,
                        localToRoot = prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix });
            }
            return parts.ToArray();
        }
        private static float MaxAbs(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int split = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, split));
            AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
        }
    }
}
