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
        internal const int Version = 2;
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
                // Retain foliage density as well as the outer silhouette. More aggressive budgets
                // pass a contour-only comparison but expose holes inside maple and pine crowns.
                float farFraction = .24f;
                int[] budgets = { Mathf.RoundToInt(triangles * .42f), Mathf.RoundToInt(triangles * farFraction) };
                var levels = new TreeRenderLibrary.Level[3];
                levels[0] = new TreeRenderLibrary.Level { screenHeight = 0.40f, parts = original.Select(p => CopyPart(p, p.mesh, OptimizedMaterial(p.material))).ToArray() };
                float[] thresholds = { 0.16f, 0.001f };
                for (int level = 0; level < budgets.Length; level++)
                {
                    // Always start at LOD0. Authored lower LODs already enlarge the leaf cards.
                    var authored = original;
                    long authoredTriangles = authored.Sum(p => (long)p.mesh.GetIndexCount(p.submesh) / 3);
                    var parts = new List<TreeRenderLibrary.MeshPart>();
                    foreach (var part in authored)
                    {
                        long sourceCount = part.mesh.GetIndexCount(part.submesh) / 3;
                        int target = Mathf.Max(12, Mathf.RoundToInt(budgets[level] * (float)sourceCount / authoredTriangles));
                        var mesh = FoliageMesh(part.mesh, part.submesh, target, part.material);
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
                        var output = CopyPart(part, saved, OptimizedMaterial(part.material)); output.submesh = 0;
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
        }

        private static Mesh FoliageMesh(Mesh source, int submesh, int target, Material material)
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
            var leaves = groups.Values.Select(g => g.Indices.ToArray()).ToArray();
            int trunkBudget = Mathf.Min(trunk.Count / 3, Mathf.Max(24, Mathf.RoundToInt(trunk.Count / 3f * target / (indices.Length / 3f))));
            var pieces = new List<Mesh>();
            try
            {
                if (leaves.Length > 200)
                {
                    var selected = TreeCanopyCoverage.Select(source, leaves, material, Mathf.Max(1, target - trunkBudget));
                    pieces.Add(Compact(source, selected.SelectMany(i => leaves[i]).ToArray(), null));
                }
                else
                {
                    float fraction = target / (indices.Length / 3f);
                    // The tiny maple keeps every card. Ginkgo has larger connected clusters:
                    // retain their multi-view coverage, then simplify inside each selected cluster.
                    var selected = leaves.Length > 100 ? TreeCanopyCoverage.Select(source, leaves, material,
                        Mathf.RoundToInt(leaves.Sum(l => l.Length / 3) * (fraction > .3f ? .85f : .45f))) : Enumerable.Range(0, leaves.Length).ToArray();
                    foreach (int index in selected)
                    {
                        var leaf = leaves[index]; var part = Compact(source, (int[])leaf.Clone(), null);
                        pieces.Add(Simplify(part, Mathf.Max(2, Mathf.RoundToInt(leaf.Length / 3f * fraction))));
                        UnityEngine.Object.DestroyImmediate(part);
                    }
                }
                if (trunk.Count > 0)
                {
                    var wood = Compact(source, trunk.ToArray(), null);
                    pieces.Add(Simplify(wood, trunkBudget)); UnityEngine.Object.DestroyImmediate(wood);
                }
                var combined = new Mesh();
                combined.CombineMeshes(pieces.Select(p => new CombineInstance { mesh = p }).ToArray(), true, false);
                combined.RecalculateBounds();
                return combined;
            }
            finally { foreach (var piece in pieces) UnityEngine.Object.DestroyImmediate(piece); }
        }

        private static Mesh Simplify(Mesh mesh, int target)
        {
            if (mesh.triangles.Length / 3 <= target) return UnityEngine.Object.Instantiate(mesh);
            MeshLodUtility.GenerateMeshLods(mesh, 8);
            int chosen = 0;
            long error = Math.Abs(mesh.GetIndexCount(0) / 3 - target);
            for (int i = 1; i < mesh.lodCount; i++)
            {
                long count = mesh.GetLod(0, i).indexCount / 3;
                long candidate = Math.Abs(count - target);
                if (count >= 2 && candidate < error) { error = candidate; chosen = i; }
            }
            return Extract(mesh, 0, chosen);
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
