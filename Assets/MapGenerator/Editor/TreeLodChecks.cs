using System;
using System.IO;
using System.Linq;
using System.Text;
using TacticalMaps.Construction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Editor
{
    internal static class TreeLodChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Optimized Tree LODs")]
        internal static void Run()
        {
            var library = AssetDatabase.LoadAssetAtPath<TreeRenderLibrary>(TreeInstancingSetup.Folder + "/TreeRenderLibrary.asset");
            var report = new StringBuilder();
            Check(library != null && library.OptimizationVersion == TreeLodOptimizer.Version, "Optimized tree library exists");
            foreach (var shader in new[] { Shader.Find("TacticalMaps/Trees/Foliage"), Shader.Find("TacticalMaps/Trees/Impostor") })
                Check(shader != null && !ShaderUtil.ShaderHasError(shader), "Tree shader compiles");
            foreach (var species in library.SpeciesList)
            {
                Check(species.levels.Length == 3 && species.originalLevels.Length >= 2, "Main / middle / far meshes and preserved sources");
                Check(species.impostorMaterial.GetTexture("_NormalMap") != null, "Relightable impostor normals exist");
                long previous = long.MaxValue;
                float threshold = float.MaxValue;
                foreach (var level in species.levels)
                {
                    long count = level.parts.Sum(p => (long)p.mesh.GetIndexCount(p.submesh) / 3);
                    Check(count > 0 && count <= previous && level.screenHeight < threshold, "Decreasing geometry and thresholds");
                    foreach (var part in level.parts)
                    {
                        Check(part.material.enableInstancing && part.material.HasProperty("_TreeFade"), "Instanced fade material");
                        Check(part.mesh.vertexCount > 0 && part.mesh.bounds.size.sqrMagnitude > 0, "Valid generated geometry");
                    }
                    previous = count; threshold = level.screenHeight;
                }
            }
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Tree LOD verification") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var atlas = new Texture2D(1280, library.SpeciesList.Length * 320, TextureFormat.RGB24, false);
            var sourceSun = RenderSettings.sun;
            TerrainData data = null; TreeRenderLibrary fallback = null;
            try
            {
                var cameraObject = new GameObject("Tree verification camera", typeof(Camera)); cameraObject.transform.SetParent(root.transform);
                var camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.scene = scene;
                camera.orthographic = true; camera.nearClipPlane = 0.01f; camera.farClipPlane = 20000;
                camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.12f, 0.18f, 0.22f, 0);
                camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderShadows = false;
                var sunObject = new GameObject("Verification sun", typeof(Light)); sunObject.transform.SetParent(root.transform);
                var sun = sunObject.GetComponent<Light>(); sun.type = LightType.Directional; sun.cullingMask = 1 << 31; sun.shadows = LightShadows.None;
                sun.intensity = sourceSun != null ? sourceSun.intensity : 2;
                sun.color = sourceSun != null ? sourceSun.color : Color.white;
                sun.transform.rotation = sourceSun != null ? sourceSun.transform.rotation : Quaternion.Euler(50, -30, 0);
                for (int row = 0; row < library.SpeciesList.Length; row++)
                {
                    var species = library.SpeciesList[row];
                    var bounds = BoundsOf(species.levels[0]);
                    camera.orthographicSize = bounds.extents.magnitude * 1.04f;
                    camera.transform.position = bounds.center + new Vector3(0, 0.35f, -1).normalized * bounds.size.magnitude * 2;
                    camera.transform.LookAt(bounds.center);
                    for (int col = 0; col < 4; col++)
                    {
                        var level = col == 0 ? species.originalLevels[0] : species.levels[col - 1];
                        var geometry = new GameObject("Representation"); geometry.transform.SetParent(root.transform);
                        try
                        {
                            foreach (var part in level.parts)
                            {
                                var go = new GameObject("Part", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(geometry.transform);
                                go.layer = 31; go.transform.localPosition = part.localToRoot.GetColumn(3); go.transform.localRotation = part.localToRoot.rotation; go.transform.localScale = part.localToRoot.lossyScale;
                                go.GetComponent<MeshFilter>().sharedMesh = part.mesh;
                                var renderer = go.GetComponent<MeshRenderer>();
                                renderer.sharedMaterials = Enumerable.Repeat(part.material, part.mesh.subMeshCount).ToArray(); renderer.shadowCastingMode = ShadowCastingMode.Off;
                            }
                            var pixels = Capture(camera, null, 320, 320);
                            atlas.SetPixels32(col * 320, (library.SpeciesList.Length - 1 - row) * 320, 320, 320, pixels);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(geometry); }
                    }
                }
                atlas.Apply(); File.WriteAllBytes("Temp/tree-lod-contact-sheet.png", atlas.EncodeToPNG());
                report.AppendLine("Contact sheet rows: " + string.Join(", ", library.SpeciesList.Select(s => s.source.name)));
                report.AppendLine("Columns: original source shading / main mesh / generated middle / generated far.");

                data = new TerrainData { heightmapResolution = 33, size = new Vector3(256, 1, 256) };
                var terrainObject = Terrain.CreateTerrainGameObject(data); terrainObject.transform.SetParent(root.transform); terrainObject.transform.position = new Vector3(-128, 0, -128); terrainObject.layer = 31;
                var terrain = terrainObject.GetComponent<Terrain>(); terrain.drawHeightmap = false; terrain.drawTreesAndFoliage = false;
                var rendererObject = new GameObject("Instanced verification trees"); rendererObject.transform.SetParent(root.transform);
                var trees = rendererObject.AddComponent<InstancedTreeRenderer>();
                foreach (var species in library.SpeciesList)
                {
                    data.treePrototypes = new[] { new TreePrototype { prefab = species.source } };
                    data.SetTreeInstances(new[] { new TreeInstance { position = new Vector3(0.5f,0,0.5f), prototypeIndex = 0, widthScale = 1, heightScale = 1, color = Color.white, lightmapColor = Color.white } }, false);
                    trees.Configure(new[] { terrain }, library, 6000, 64, 0);
                    float transition = InstancedTreeRenderer.DefaultMeshScreenHeight;
                    foreach (float height in new[] { 1.2f, 0.448f, 0.40f, 0.352f, 0.20f, 0.1792f, 0.16f, 0.1408f, transition*1.121f, transition, transition*.879f, transition*.65f })
                    {
                        Aim(camera, species, height);
                        var pixels = Capture(camera, height == transition ? "Temp/tree-transition-" + species.source.name + ".png" : null, 768, 768);
                        uint impostors = trees.ReadImpostorCountForChecks(camera);
                        Check(trees.VisibleTrees + impostors > 0, species.source.name + " remains submitted at " + height);
                        int covered = pixels.Count(p => p.a > 127);
                        Check(covered > 10, species.source.name + " has a visible silhouette at " + height);
                        Check(pixels.Count(p => p.r > 220 && p.b > 220 && p.g < 30) < covered / 5, "No shader error pixels");
                        if (height == transition) Check(trees.VisibleTrees == 1 && impostors == 1, "Mesh and impostor overlap at transition");
                        if (height == transition*.65f) Check(trees.VisibleTrees == 0 && impostors == 1, "Pure impostor below mesh transition");
                    }
                    report.AppendLine(species.source.name + ": main / middle / far / both sides of mesh and impostor transitions render.");
                }
                foreach (float threshold in new[] { 0.02f, 0.14f, 0.2f, 0.3f })
                {
                    trees.Configure(new[] { terrain }, library, 6000, 64, 0, threshold);
                    Aim(camera, library.SpeciesList.Last(), threshold); Capture(camera, null, 768, 768);
                    Check(trees.VisibleTrees == 1 && trees.ReadImpostorCountForChecks(camera) == 1, "Editable threshold preserves complementary overlap");
                }
                fallback = UnityEngine.Object.Instantiate(library); fallback.ConfigureImpostors(null, null);
                trees.Configure(new[] { terrain }, fallback, 6000, 64, 0);
                Aim(camera, library.SpeciesList.Last(), 0.02f); Capture(camera, null, 768, 768);
                Check(trees.VisibleTrees == 1 && trees.IndirectDraws == 0, "No-compute fallback retains generated mesh LODs");
                Capture(camera, null, 768, 768); Check(trees.CullMilliseconds == 0, "Stationary view reuses fade batches");
                trees.enabled = false; trees.enabled = true; Capture(camera, null, 768, 768);
                Check(trees.VisibleTrees == 1, "Disable/enable recreates resources and renders");
                report.AppendLine("PASS: assets, shader compilation, all six species at 12 screen sizes, mesh/impostor overlap, mesh-only fallback, stationary cache and resource rebuild.");
                File.WriteAllText("Temp/tree-lod-checks.txt", report.ToString()); Debug.Log(report.ToString());
            }
            catch (Exception e) { File.WriteAllText("Temp/tree-lod-checks.txt", report + e.ToString()); throw; }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
                if (data != null) UnityEngine.Object.DestroyImmediate(data);
                if (fallback != null) UnityEngine.Object.DestroyImmediate(fallback);
                UnityEngine.Object.DestroyImmediate(atlas);
            }
        }

        private static Bounds BoundsOf(TreeRenderLibrary.Level level)
        {
            var bounds = new Bounds(); bool first = true;
            foreach (var part in level.parts)
            {
                var b = part.mesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = part.localToRoot.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
                }
            }
            return bounds;
        }
        private static void Aim(Camera camera, TreeRenderLibrary.Species species, float height)
        {
            camera.orthographicSize = species.size * QualitySettings.lodBias / (2 * height);
            camera.transform.position = species.referencePoint + new Vector3(0, species.size * 0.8f, -species.size * 4);
            camera.transform.LookAt(species.referencePoint);
        }
        private static Color32[] Capture(Camera camera, string path, int width, int height)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                camera.aspect = (float)width / height; camera.targetTexture = target; camera.Render();
                RenderTexture.active = target; texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply();
                if (path != null) File.WriteAllBytes(path, texture.EncodeToPNG());
                return texture.GetPixels32();
            }
            finally { RenderTexture.active = previous; camera.targetTexture = null; target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(texture); }
        }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
