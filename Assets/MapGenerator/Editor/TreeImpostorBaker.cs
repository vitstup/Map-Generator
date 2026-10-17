using System;
using System.IO;
using TacticalMaps.Construction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Editor
{
    internal static class TreeImpostorBaker
    {
        internal const string Folder = TreeInstancingSetup.Folder + "/Impostors";
        private const int Tile = 256, Columns = 8, Rows = 3;

        [MenuItem("Tools/Tactical Maps/Bake Optimized Tree LODs")]
        private static void Run()
        {
            const string report = "Temp/tree-impostor-bake.txt";
            File.WriteAllText(report, "Running...\n");
            try
            {
                var library = AssetDatabase.LoadAssetAtPath<TreeRenderLibrary>(TreeInstancingSetup.Folder + "/TreeRenderLibrary.asset");
                if (library == null) throw new InvalidOperationException("Configure the tree library first.");
                Bake(library); File.WriteAllText(report, "PASS: six 24-view atlases, direct mesh/material LOD library, two-triangle impostor mesh and GPU culling assets.\n");
            }
            catch (Exception e) { File.WriteAllText(report, e.ToString()); Debug.LogException(e); }
        }

        internal static void Bake(TreeRenderLibrary library)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before baking.");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(TreeInstancingSetup.Folder, "Impostors");
            var shader = Shader.Find("TacticalMaps/Trees/Impostor");
            if (shader == null) throw new InvalidOperationException("Tree impostor shader is missing.");
            var culling = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MapGenerator/Shaders/TreeImpostorCulling.compute");
            string meshPath = Folder + "/TreeImpostorQuad.asset";
            var quad = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (quad == null)
            {
                quad = new Mesh { name = "TreeImpostorQuad" };
                quad.vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(-1,1,0), new Vector3(1,1,0) };
                quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
                quad.triangles = new[] { 0,2,1, 2,3,1 }; quad.RecalculateBounds();
                AssetDatabase.CreateAsset(quad, meshPath);
            }
            foreach (var species in library.SpeciesList)
            {
                // Runtime consumes mesh/material records directly. Split prefabs remain optional authoring assets.
                foreach (var level in species.levels)
                {
                    var parts = new System.Collections.Generic.List<TreeRenderLibrary.MeshPart>();
                    foreach (var renderer in level.prefab.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                        var matrix = level.prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                        var materials = renderer.sharedMaterials;
                        for (int sub = 0; sub < mesh.subMeshCount; sub++)
                            parts.Add(new TreeRenderLibrary.MeshPart { mesh = mesh, material = materials[Mathf.Min(sub, materials.Length-1)], submesh = sub, localToRoot = matrix });
                    }
                    level.parts = parts.ToArray();
                }
                BakeSpecies(species, shader);
            }
            library.ConfigureImpostors(culling, quad); EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();
        }

        private static void BakeSpecies(TreeRenderLibrary.Species species, Shader shader)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Impostor bake"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var render = new RenderTexture(Tile, Tile, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 4 };
            var tile = new Texture2D(Tile, Tile, TextureFormat.RGBA32, false, true);
            var atlas = new Texture2D(Tile * Columns, Tile * Rows, TextureFormat.RGBA32, false, true);
            var previous = RenderTexture.active;
            try
            {
                var instance = UnityEngine.Object.Instantiate(species.source, root.transform);
                instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity); instance.transform.localScale = Vector3.one;
                foreach (var t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                var group = instance.GetComponentInChildren<LODGroup>(); var keep = group.GetLODs()[0].renderers;
                group.enabled = false;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                { renderer.enabled = Array.IndexOf(keep, renderer) >= 0; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; }
                Bounds bounds = keep[0].bounds;
                foreach (var renderer in keep) bounds.Encapsulate(renderer.bounds);
                float radius = bounds.extents.magnitude * 1.04f;
                species.impostorCapture = new Vector4(bounds.center.x, bounds.center.y, bounds.center.z, radius);
                var cameraObject = new GameObject("Bake camera"); cameraObject.transform.SetParent(root.transform);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene;
                camera.orthographic = true; camera.orthographicSize = radius; camera.nearClipPlane = 0.01f; camera.farClipPlane = radius * 8;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear; camera.allowHDR = false;
                camera.cullingMask = 1 << 31; camera.targetTexture = render;
                var sunObject = new GameObject("Bake sun"); sunObject.transform.SetParent(root.transform);
                var sun = sunObject.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 2;
                sun.shadows = LightShadows.None; sun.cullingMask = 1 << 31; sun.transform.rotation = Quaternion.Euler(50, -30, 0);
                for (int row = 0; row < Rows; row++) for (int column = 0; column < Columns; column++)
                {
                    float azimuth = column * Mathf.PI * 2 / Columns, elevation = row * 40 * Mathf.Deg2Rad;
                    var direction = new Vector3(Mathf.Sin(azimuth) * Mathf.Cos(elevation), Mathf.Sin(elevation), Mathf.Cos(azimuth) * Mathf.Cos(elevation));
                    camera.transform.position = bounds.center + direction * radius * 4;
                    camera.transform.LookAt(bounds.center);
                    camera.Render(); RenderTexture.active = render;
                    tile.ReadPixels(new Rect(0,0,Tile,Tile),0,0); tile.Apply();
                    var pixels = tile.GetPixels32();
                    int opaque = 0; foreach (var pixel in pixels) if (pixel.a > 64) opaque++;
                    if (opaque < 50 || opaque > pixels.Length * 0.9f) throw new InvalidOperationException(species.source.name + " bake has invalid silhouette alpha.");
                    DilateRgb(pixels);
                    atlas.SetPixels32(column * Tile, row * Tile, Tile, Tile, pixels);
                }
                atlas.Apply(); string atlasPath = Folder + "/" + species.source.name + "_Views.png";
                File.WriteAllBytes(atlasPath, atlas.EncodeToPNG()); AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(atlasPath);
                importer.sRGBTexture = false; importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true; importer.mipMapsPreserveCoverage = true; importer.alphaTestReferenceValue = 0.3f;
                importer.npotScale = TextureImporterNPOTScale.None; importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 2; importer.SaveAndReimport();
                string materialPath = Folder + "/" + species.source.name + "_Impostor.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
                material.shader = shader; material.enableInstancing = true;
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath));
                material.SetVector("_Capture", species.impostorCapture); material.SetVector("_Atlas", new Vector4(Columns, Rows, Tile, 4));
                species.impostorMaterial = material; EditorUtility.SetDirty(material);
            }
            finally
            {
                RenderTexture.active = previous; render.Release(); UnityEngine.Object.DestroyImmediate(render);
                UnityEngine.Object.DestroyImmediate(tile); UnityEngine.Object.DestroyImmediate(atlas); UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        private static void DilateRgb(Color32[] pixels)
        {
            var valid = new bool[pixels.Length]; for (int i = 0; i < pixels.Length; i++) valid[i] = pixels[i].a > 0;
            for (int pass = 0; pass < 4; pass++)
            {
                var next = (bool[])valid.Clone();
                for (int y = 1; y < Tile - 1; y++) for (int x = 1; x < Tile - 1; x++)
                {
                    int i = y * Tile + x; if (valid[i]) continue;
                    int neighbor = valid[i-1] ? i-1 : valid[i+1] ? i+1 : valid[i-Tile] ? i-Tile : valid[i+Tile] ? i+Tile : -1;
                    if (neighbor < 0) continue;
                    var p = pixels[neighbor]; pixels[i] = new Color32(p.r,p.g,p.b,0); next[i] = true;
                }
                valid = next;
            }
        }
    }
}
