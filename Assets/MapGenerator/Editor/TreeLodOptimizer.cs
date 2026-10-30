using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TacticalMaps.Construction;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Editor
{
    internal static class TreeLodOptimizer
    {
        internal const int Version = 1;
        private const string Folder = TreeInstancingSetup.Folder + "/Optimized";

        [MenuItem("Tools/Tactical Maps/Optimize Tree Meshes And Transitions")]
        internal static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before optimizing trees.");
            var library = AssetDatabase.LoadAssetAtPath<TreeRenderLibrary>(TreeInstancingSetup.Folder + "/TreeRenderLibrary.asset");
            if (library == null) throw new InvalidOperationException("Configure the tree library first.");
            Optimize(library);
            TreeImpostorBaker.Bake(library);
            foreach (var component in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (!(component is InstancedTreeRenderer) && !(component is TreeBuilder)) continue;
                var serialized = new SerializedObject(component);
                serialized.FindProperty("meshScreenHeight").floatValue = InstancedTreeRenderer.DefaultMeshScreenHeight;
                serialized.ApplyModifiedProperties();
                if (component is InstancedTreeRenderer renderer) { renderer.enabled = false; renderer.enabled = true; }
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
            }
            AssetDatabase.SaveAssets();
        }

        internal static void Optimize(TreeRenderLibrary library)
        {
            EnsureFolder(Folder); EnsureFolder(Folder + "/Meshes"); EnsureFolder(Folder + "/Materials");
            if (Shader.Find("TacticalMaps/Trees/Foliage") == null) throw new InvalidOperationException("Tree foliage shader is missing.");
            var report = new StringBuilder();
            foreach (var species in library.SpeciesList)
            {
                if (species.originalLevels == null || species.originalLevels.Length == 0) species.originalLevels = species.levels;
                var original = species.originalLevels[0].parts;
                long triangles = original.Sum(p => (long)p.mesh.GetIndexCount(p.submesh) / 3);
                int[] budgets = triangles > 5000 ? new[] { 6000, 1800, 500 } : triangles > 2500 ? new[] { 1800, 650, 220 } : triangles > 1000 ? new[] { 1000, 400, 140 } : new[] { (int)triangles, 300, 120 };
                var levels = new TreeRenderLibrary.Level[4];
                levels[0] = new TreeRenderLibrary.Level { screenHeight = 0.95f, parts = original.Select(p => CopyPart(p, p.mesh, OptimizedMaterial(p.material))).ToArray() };
                float[] thresholds = { 0.36f, 0.18f, 0.006f };
                for (int level = 0; level < budgets.Length; level++)
                {
                    var authored = species.originalLevels[Mathf.Min(level + 1, species.originalLevels.Length - 1)].parts;
                    long authoredTriangles = authored.Sum(p => (long)p.mesh.GetIndexCount(p.submesh) / 3);
                    var parts = new List<TreeRenderLibrary.MeshPart>();
                    foreach (var part in authored)
                    {
                        long sourceCount = part.mesh.GetIndexCount(part.submesh) / 3;
                        int target = Mathf.Max(12, Mathf.RoundToInt(budgets[level] * (float)sourceCount / authoredTriangles));
                        var mesh = FoliageMesh(part.mesh, part.submesh, target);
                        string path = Folder + "/Meshes/" + species.source.name + "_LOD" + (level + 1) + "_" + parts.Count + ".asset";
                        mesh.name = System.IO.Path.GetFileNameWithoutExtension(path);
                        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                        if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
                        else
                        {
                            saved.Clear();
                            saved.indexFormat = mesh.indexFormat;
                            saved.vertices = mesh.vertices; saved.normals = mesh.normals; saved.tangents = mesh.tangents; saved.colors32 = mesh.colors32;
                            for (int channel = 0; channel < 8; channel++)
                            { var uv = new List<Vector4>(); mesh.GetUVs(channel, uv); if (uv.Count > 0) saved.SetUVs(channel, uv); }
                            saved.SetIndices(mesh.GetIndices(0), MeshTopology.Triangles, 0); saved.bounds = mesh.bounds;
                            saved.UploadMeshData(false);
                            UnityEngine.Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved);
                        }
                        var output = CopyPart(part, saved, OptimizedMaterial(part.material, level == 0)); output.submesh = 0;
                        parts.Add(output);
                    }
                    levels[level + 1] = new TreeRenderLibrary.Level { screenHeight = thresholds[level], parts = parts.ToArray() };
                }
                species.levels = levels;
                report.AppendLine(species.source.name + ": " + string.Join(" / ", levels.Select(l => l.parts.Sum(p => (long)p.mesh.GetIndexCount(p.submesh) / 3))) + " triangles");
            }
            var used = new HashSet<string>(library.SpeciesList.SelectMany(s => s.levels).SelectMany(l => l.parts).Select(p => AssetDatabase.GetAssetPath(p.mesh)));
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { Folder + "/Meshes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!used.Contains(path)) AssetDatabase.DeleteAsset(path);
            }
            library.MarkOptimized(Version); EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();
            System.IO.File.WriteAllText("Temp/tree-lod-generation.txt", report.ToString());
            Debug.Log(report.ToString());
        }

        private static TreeRenderLibrary.MeshPart CopyPart(TreeRenderLibrary.MeshPart source, Mesh mesh, Material material) => new TreeRenderLibrary.MeshPart { mesh = mesh, material = material, submesh = source.submesh, localToRoot = source.localToRoot };

        private static Material OptimizedMaterial(Material original, bool detailed = true)
        {
            string path = Folder + "/Materials/" + original.name + (detailed ? "" : "_Far") + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = material == null;
            if (create) material = new Material(Shader.Find("TacticalMaps/Trees/Foliage")) { name = original.name + "_Simple", enableInstancing = true };
            material.SetTexture("_LeafMap", original.GetTexture("_Leaf_Texture"));
            material.SetTexture("_TrunkMap", original.GetTexture("_Trunk_Texture"));
            Vector4 uv = original.GetVector("_Leaf_Tiling"), offset = original.GetVector("_Leaf_Offset");
            material.SetVector("_LeafUV", new Vector4(uv.x, uv.y, offset.x, offset.y));
            uv = original.GetVector("_Trunk_Tiling"); offset = original.GetVector("_Trunk_Offset");
            material.SetVector("_TrunkUV", new Vector4(uv.x, uv.y, offset.x, offset.y));
            Color leaf = original.GetColor("_Leaf_Base_Color"), trunk = original.GetColor("_Trunk_Base_Color");
            if (original.GetFloat("_Use_Color_Noise") > 0.5f)
            {
                // Average the source's two nested noise lerps in linear color space.
                leaf = (leaf.linear * 0.25f + original.GetColor("_Leaf_Noise_Color").linear * 0.32f + original.GetColor("_Leaf_Noise_Large_Color").linear * 0.43f).gamma;
                trunk = Color.Lerp(trunk, original.GetColor("_Trunk_Noise_Color"), 0.3f);
            }
            material.SetColor("_LeafColor", leaf); material.SetColor("_TrunkColor", trunk);
            material.SetColor("_FrostColor", original.GetColor("_Frosting_Color"));
            material.SetFloat("_FrostHeight", original.GetFloat("_Frosting_Height"));
            material.SetFloat("_FrostFalloff", original.GetFloat("_Enable_Frosting") > 0.5f ? original.GetFloat("_Frosting_Falloff") : 0);
            material.SetFloat("_Cutoff", original.GetFloat("_Alpha_Clip_Threshold"));
            material.SetTexture("_LeafNormal", original.GetTexture("_Leaf_Normal"));
            material.SetFloat("_NormalStrength", original.GetFloat("_Leaf_Normal_Strength"));
            if (detailed && original.GetFloat("_Enable_Leaf_Normal") > 0.5f && original.GetTexture("_Leaf_Normal") != null) material.EnableKeyword("_TREE_LEAF_NORMAL");
            else material.DisableKeyword("_TREE_LEAF_NORMAL");
            if (create) AssetDatabase.CreateAsset(material, path); else EditorUtility.SetDirty(material);
            return material;
        }

        private sealed class LeafGroup
        {
            internal readonly List<int> Indices = new List<int>();
            internal Vector3 Centre;
        }

        private static Mesh FoliageMesh(Mesh source, int submesh, int target)
        {
            var indices = source.GetIndices(submesh);
            if (indices.Length / 3 <= target) return Compact(source, indices, null);
            var positions = source.vertices; var colors = source.colors32;
            int[] parent = Enumerable.Range(0, positions.Length).ToArray();
            int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
            bool Leaf(int i) => colors.Length == positions.Length && colors[i].b > 127;
            var weld = new Dictionary<Vector3, int>();
            for (int i = 0; i < positions.Length; i++) if (Leaf(i))
            { if (weld.TryGetValue(positions[i], out int other)) parent[Find(i)] = Find(other); else weld.Add(positions[i], i); }
            var trunk = new List<int>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                if (Leaf(a) && Leaf(b) && Leaf(c)) { parent[Find(a)] = Find(b); parent[Find(b)] = Find(c); }
                else { trunk.Add(a); trunk.Add(b); trunk.Add(c); }
            }
            var groups = new Dictionary<int, LeafGroup>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                if (!Leaf(a) || !Leaf(b) || !Leaf(c)) continue;
                int id = Find(a);
                if (!groups.TryGetValue(id, out var group)) groups.Add(id, group = new LeafGroup());
                group.Indices.Add(a); group.Indices.Add(b); group.Indices.Add(c);
            }
            var leaves = groups.Values.ToArray();
            foreach (var leaf in leaves)
            { var unique = leaf.Indices.Distinct().ToArray(); foreach (int i in unique) leaf.Centre += positions[i]; leaf.Centre /= unique.Length; }
            int trunkBudget = Mathf.Min(trunk.Count / 3, Mathf.Max(80, target / 5));
            float ratio = leaves.Length == 0 ? 0 : Mathf.Clamp((target - trunkBudget) / Mathf.Max(1f, (indices.Length - trunk.Count) / 3f), leaves.Length < 150 ? 0.6f : 0.3f, 1);
            int keep = Mathf.CeilToInt(leaves.Length * ratio);
            // Farthest-point selection covers the whole canopy instead of losing complete clusters.
            var distances = Enumerable.Repeat(float.MaxValue, leaves.Length).ToArray();
            var selected = new bool[leaves.Length];
            var output = new List<int>(); var moved = new Dictionary<int, Vector3>();
            int next = 0;
            for (int i = 1; i < leaves.Length; i++) if (leaves[i].Centre.y > leaves[next].Centre.y) next = i;
            float expansion = Mathf.Min(1.8f, Mathf.Sqrt(1 / Mathf.Max(ratio, 0.001f)));
            for (int n = 0; n < keep; n++)
            {
                selected[next] = true; var leaf = leaves[next]; output.AddRange(leaf.Indices);
                foreach (int i in leaf.Indices) moved[i] = leaf.Centre + (positions[i] - leaf.Centre) * expansion;
                int farthest = -1; float distance = -1;
                for (int i = 0; i < leaves.Length; i++)
                {
                    if (selected[i]) continue;
                    distances[i] = Mathf.Min(distances[i], (leaves[i].Centre - leaf.Centre).sqrMagnitude);
                    if (distances[i] > distance) { distance = distances[i]; farthest = i; }
                }
                if (farthest < 0) break; next = farthest;
            }
            var foliage = Compact(source, output.ToArray(), moved);
            if (trunk.Count == 0) return foliage;
            var wood = Compact(source, trunk.ToArray(), null);
            Mesh simplified = null;
            try
            {
                MeshLodUtility.GenerateMeshLods(wood, 8);
                int chosen = 0;
                for (int i = 1; i < wood.lodCount; i++) { chosen = i; if (wood.GetLod(0, i).indexCount / 3 <= trunkBudget) break; }
                simplified = Extract(wood, 0, chosen);
                var combined = new Mesh();
                combined.CombineMeshes(new[] { new CombineInstance { mesh = foliage }, new CombineInstance { mesh = simplified } }, true, false);
                // Expanded cards must remain inside conservative renderer bounds.
                combined.RecalculateBounds();
                return combined;
            }
            finally { UnityEngine.Object.DestroyImmediate(foliage); UnityEngine.Object.DestroyImmediate(wood); if (simplified != null) UnityEngine.Object.DestroyImmediate(simplified); }
        }

        // Compact each generated index range into an ordinary mesh. No runtime Mesh LOD dependency,
        // no unused vertices, and no changes to imported meshes or their importer settings.
        private static Mesh Extract(Mesh source, int submesh, int lod)
        {
            var range = source.GetLod(submesh, lod);
            var indices = new int[range.indexCount];
            using (var data = Mesh.AcquireReadOnlyMeshData(source))
            {
                int baseVertex = (int)source.GetBaseVertex(submesh);
                if (source.indexFormat == IndexFormat.UInt16)
                { var raw = data[0].GetIndexData<ushort>(); for (int i = 0; i < indices.Length; i++) indices[i] = raw[(int)range.indexStart + i] + baseVertex; }
                else
                { var raw = data[0].GetIndexData<int>(); for (int i = 0; i < indices.Length; i++) indices[i] = raw[(int)range.indexStart + i] + baseVertex; }
            }
            return Compact(source, indices, null);
        }

        private static Mesh Compact(Mesh source, int[] indices, Dictionary<int, Vector3> moved)
        {
            var remap = new Dictionary<int, int>(); var used = new List<int>();
            for (int i = 0; i < indices.Length; i++)
            {
                int index = indices[i];
                if (!remap.TryGetValue(index, out int compact)) { compact = used.Count; remap.Add(index, compact); used.Add(index); }
                indices[i] = compact;
            }
            var result = new Mesh { indexFormat = used.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            var positions = source.vertices; result.SetVertices(used.Select(i => moved != null && moved.TryGetValue(i, out var p) ? p : positions[i]).ToArray());
            var normals = source.normals; if (normals.Length > 0) result.SetNormals(used.Select(i => normals[i]).ToArray());
            var tangents = source.tangents; if (tangents.Length > 0) result.SetTangents(used.Select(i => tangents[i]).ToArray());
            var colors = source.colors32; if (colors.Length > 0) result.SetColors(used.Select(i => colors[i]).ToArray());
            for (int channel = 0; channel < 8; channel++)
            {
                var uv = new List<Vector4>(); source.GetUVs(channel, uv);
                if (uv.Count > 0) result.SetUVs(channel, used.Select(i => uv[i]).ToArray());
            }
            result.SetIndices(indices, MeshTopology.Triangles, 0); result.bounds = source.bounds;
            return result;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
