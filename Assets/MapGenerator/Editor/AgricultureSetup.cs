using System;
using System.Collections.Generic;
using System.IO;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using TacticalMaps.Testing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TacticalMaps.Editor
{
    internal static class AgricultureSetup
    {
        internal const string Folder = "Assets/MapGenerator/Generated/Agriculture";
        private const string Source = "Assets/Sprites/Maps/japan_agriculture_density_4096_16bit.png";
        [MenuItem("Tools/Tactical Maps/Set Up Agriculture")]
        internal static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            Directory.CreateDirectory(Folder); Directory.CreateDirectory(Folder + "/Buildings"); AssetDatabase.Refresh();
            var importer = (TextureImporter)AssetImporter.GetAtPath(Source);
            importer.textureType = TextureImporterType.SingleChannel;
            var importSettings = new TextureImporterSettings(); importer.ReadTextureSettings(importSettings);
            importSettings.singleChannelComponent = TextureImporterSingleChannelComponent.Red; importer.SetTextureSettings(importSettings);
            importer.sRGBTexture = false; importer.isReadable = true; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = 4096; importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp;
            var platform = importer.GetDefaultPlatformTextureSettings(); platform.format = TextureImporterFormat.R16; platform.maxTextureSize = 4096; platform.textureCompression = TextureImporterCompression.Uncompressed; importer.SetPlatformTextureSettings(platform);
            importer.SaveAndReimport();
            var library = AssetDatabase.LoadAssetAtPath<AgricultureLibrary>(Folder + "/AgricultureLibrary.asset");
            if (library == null) { library = ScriptableObject.CreateInstance<AgricultureLibrary>(); AssetDatabase.CreateAsset(library, Folder + "/AgricultureLibrary.asset"); }
            library.bankMaterial = Material("FarmEarth", "TacticalMaps/Agriculture/Banks");
            library.waterMaterial = Material("PaddyWater", "TacticalMaps/Agriculture/Water");
            library.cropMaterial = Material("Rice", "TacticalMaps/Agriculture/Rice");
            library.cropMaterial.enableInstancing = true;
            library.culling = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MapGenerator/Shaders/CropCulling.compute");
            library.closeRice = SaveMesh(RiceMesh(false), Folder + "/RiceClose.asset"); library.distantRice = SaveMesh(RiceMesh(true), Folder + "/RiceDistant.asset");
            library.houses = new GameObject[5];
            for (int i = 0; i < 5; i++) library.houses[i] = BakeHouse(i);
            EditorUtility.SetDirty(library);
            var generator = Object.FindAnyObjectByType<MapGenerator>(); var builder = Object.FindAnyObjectByType<MapBuilder>();
            if (generator == null || builder == null) throw new InvalidOperationException("Open MainScene with the map workbench.");
            library.soilProfile = new SerializedObject(generator.GetComponent<BiomeGenerator>()).FindProperty("earth").objectReferenceValue as TerrainLayer; EditorUtility.SetDirty(library);
            library.bankMaterial.SetTexture("_BaseMap", library.soilProfile.diffuseTexture);
            library.bankMaterial.SetTexture("_NormalMap", library.soilProfile.normalMapTexture != null ? library.soilProfile.normalMapTexture : Texture2D.normalTexture);
            var agriculture = generator.GetComponent<AgricultureGenerator>(); if (agriculture == null) agriculture = Undo.AddComponent<AgricultureGenerator>(generator.gameObject);
            Set(agriculture, "densitySource", AssetDatabase.LoadAssetAtPath<Texture2D>(Source)); Set(generator, "agricultureGenerator", agriculture);
            var configuration = new SerializedObject(agriculture);
            if (configuration.FindProperty("layoutVersion").intValue < 4)
            {
                configuration.FindProperty("districtSize").floatValue = 600; configuration.FindProperty("coverage").floatValue = .9f;
                configuration.FindProperty("maximumFields").intValue = 2400; configuration.FindProperty("maximumFarms").intValue = 500;
                configuration.FindProperty("layoutVersion").intValue = 4;
            }
            var footprints = configuration.FindProperty("houseFootprints"); footprints.arraySize = library.houses.Length;
            for (int i = 0; i < library.houses.Length; i++) footprints.GetArrayElementAtIndex(i).vector2Value = library.houses[i].GetComponent<FarmBuildingGrounding>().halfFootprint;
            configuration.ApplyModifiedPropertiesWithoutUndo();
            var construction = builder.GetComponent<AgricultureBuilder>(); if (construction == null) construction = Undo.AddComponent<AgricultureBuilder>(builder.gameObject);
            Set(construction, "library", library); Set(builder, "agricultureBuilder", construction);
            ConfigureOverlay();
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(generator.gameObject.scene); EditorSceneManager.SaveScene(generator.gameObject.scene);
            Debug.Log("Agriculture installed: linear R16 density, five combined houses with proxy LODs, rice patches and field materials.");
        }
        internal static void Set(Object owner, string name, Object value)
        { var s = new SerializedObject(owner); s.FindProperty(name).objectReferenceValue = value; s.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(owner); }
        [MenuItem("Tools/Tactical Maps/Set Up Agriculture Density Overlay")]
        internal static void ConfigureOverlay()
        {
            var workbench = Object.FindAnyObjectByType<MapWorkbench>(); if (workbench == null) throw new InvalidOperationException("Open the workbench scene.");
            var serialized = new SerializedObject(workbench); var heightOverlay = serialized.FindProperty("globalOverlay").objectReferenceValue as UnityEngine.UI.RawImage;
            if (heightOverlay == null || heightOverlay.transform.parent == null) throw new InvalidOperationException("Workbench global map is missing.");
            var parent = heightOverlay.transform.parent; var child = parent.Find("AgricultureDensity");
            if (child == null)
            {
                var go = new GameObject("AgricultureDensity", typeof(RectTransform), typeof(UnityEngine.UI.RawImage)); go.transform.SetParent(parent, false);
                var rect = (RectTransform)go.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; child = go.transform;
            }
            child.SetSiblingIndex(heightOverlay.transform.GetSiblingIndex() + 1);
            var image = child.GetComponent<UnityEngine.UI.RawImage>(); image.raycastTarget = false;
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(Source); var data = source.GetPixelData<ushort>(0);
            const int n = 1024; var pixels = new Color32[n * n]; int scale = source.width / n;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float value = 0; for (int b = 0; b < scale; b++) for (int a = 0; a < scale; a++) value += data[(y * scale + b) * source.width + x * scale + a] / 65535f;
                pixels[y * n + x] = new Color(.84f, .65f, .22f, Mathf.Sqrt(value / (scale * scale)) * .8f);
            }
            string path = Folder + "/DensityOverview.asset";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) { texture = new Texture2D(n, n, TextureFormat.RGBA32, false, true) { name = "AgricultureDensityOverview", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp }; AssetDatabase.CreateAsset(texture, path); }
            texture.SetPixels32(pixels); texture.Apply(false, false); EditorUtility.SetDirty(texture); image.texture = texture;
            var canvas = workbench.transform; var toggleTransform = canvas.Find("AgricultureVisibility");
            if (toggleTransform == null)
            {
                var go = new GameObject("AgricultureVisibility", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Toggle)); go.transform.SetParent(canvas, false); Place((RectTransform)go.transform, 30, 1044, 22, 22);
                var background = go.GetComponent<UnityEngine.UI.Image>(); background.color = new Color(.13f, .2f, .24f);
                var check = new GameObject("Check", typeof(RectTransform), typeof(UnityEngine.UI.Image)); check.transform.SetParent(go.transform, false);
                var rect = (RectTransform)check.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(4, 4); rect.offsetMax = new Vector2(-4, -4);
                var graphic = check.GetComponent<UnityEngine.UI.Image>(); graphic.color = new Color(.84f, .65f, .22f); graphic.raycastTarget = false;
                var t = go.GetComponent<UnityEngine.UI.Toggle>(); t.targetGraphic = background; t.graphic = graphic; t.isOn = false; toggleTransform = go.transform;
                var labelObject = new GameObject("AgricultureLegend", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI)); labelObject.transform.SetParent(canvas, false); Place((RectTransform)labelObject.transform, 62, 1041, 580, 28);
                var label = labelObject.GetComponent<TMPro.TextMeshProUGUI>(); label.font = canvas.GetComponentInChildren<TMPro.TMP_Text>().font; label.fontSize = 18; label.color = new Color(.84f, .75f, .49f); label.text = "Плотность сельского хозяйства"; label.raycastTarget = false;
            }
            var controller = workbench.GetComponent<AgricultureOverlay>(); if (controller == null) controller = workbench.gameObject.AddComponent<AgricultureOverlay>();
            Set(controller, "overlay", image); Set(controller, "visibility", toggleTransform.GetComponent<UnityEngine.UI.Toggle>()); image.enabled = toggleTransform.GetComponent<UnityEngine.UI.Toggle>().isOn;
            EditorSceneManager.MarkSceneDirty(workbench.gameObject.scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(workbench.gameObject.scene);
        }
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        { rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); }
        private static Material Material(string name, string shader)
        {
            string path = Folder + "/" + name + ".mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var target = Shader.Find(shader); if (target == null) throw new InvalidOperationException("Missing shader: " + shader);
            if (material == null) { material = new Material(target) { name = name }; AssetDatabase.CreateAsset(material, path); }
            material.shader = target; material.enableInstancing = true; EditorUtility.SetDirty(material); return material;
        }
        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(existing); return existing;
        }
        private static GameObject BakeHouse(int index)
        {
            string source = "Assets/Synty/PolygonSamuraiEmpire/Prefabs/Buildings/Presets/Building_House_ Small_" + (index + 1).ToString("00") + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(source); if (prefab == null) throw new InvalidOperationException(source);
            var groups = new Dictionary<Material, List<CombineInstance>>();
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>(); if (renderer == null || filter.sharedMesh == null || !filter.gameObject.activeSelf) continue;
                var materials = renderer.sharedMaterials;
                for (int s = 0; s < filter.sharedMesh.subMeshCount && s < materials.Length; s++)
                {
                    if (materials[s] == null) continue;
                    if (!groups.TryGetValue(materials[s], out var list)) groups.Add(materials[s], list = new List<CombineInstance>());
                    list.Add(new CombineInstance { mesh = filter.sharedMesh, subMeshIndex = s, transform = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix });
                }
            }
            var root = new GameObject("Farmhouse_" + index); var detailed = new GameObject("Detailed"); detailed.transform.SetParent(root.transform, false);
            var closeRenderers = new List<Renderer>(); bool first = true; Bounds bounds = default; int part = 0;
            try
            {
                foreach (var pair in groups)
                {
                    var mesh = new Mesh { name = "House" + index + "_Part" + part, indexFormat = IndexFormat.UInt32 }; mesh.CombineMeshes(pair.Value.ToArray(), true, true); mesh.RecalculateBounds();
                    if (first) { bounds = mesh.bounds; first = false; } else bounds.Encapsulate(mesh.bounds);
                    mesh = SaveMesh(mesh, Folder + "/Buildings/House" + index + "_" + part + ".asset");
                    string materialPath = Folder + "/Buildings/HouseMaterial_" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(pair.Key)) + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material == null) { material = new Material(pair.Key) { enableInstancing = true }; AssetDatabase.CreateAsset(material, materialPath); }
                    var go = new GameObject("Part_" + part++, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(detailed.transform, false);
                    go.GetComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material; closeRenderers.Add(renderer);
                }
                Vector3 pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z); detailed.transform.localPosition = -pivot;
                var proxy = new GameObject("Distant", typeof(MeshFilter), typeof(MeshRenderer)); proxy.transform.SetParent(root.transform, false);
                var meshProxy = HouseProxy(bounds.size);
                proxy.GetComponent<MeshFilter>().sharedMesh = SaveMesh(meshProxy, Folder + "/Buildings/House" + index + "_Proxy.asset");
                var proxyRenderer = proxy.GetComponent<MeshRenderer>(); proxyRenderer.sharedMaterial = Material("DistantHouse", "TacticalMaps/Agriculture/Simple"); proxyRenderer.shadowCastingMode = ShadowCastingMode.Off;
                var lod = root.AddComponent<LODGroup>(); lod.SetLODs(new[] { new LOD(.055f, closeRenderers.ToArray()), new LOD(.0008f, new Renderer[] { proxyRenderer }) }); lod.RecalculateBounds();
                var collider = root.AddComponent<BoxCollider>(); collider.center = new Vector3(0, bounds.size.y * .4f, 0); collider.size = new Vector3(bounds.size.x * .78f, bounds.size.y * .8f, bounds.size.z * .78f);
                var grounding = root.AddComponent<FarmBuildingGrounding>(); grounding.halfFootprint = new Vector2(collider.size.x, collider.size.z) * .5f;
                var supports = new List<Vector3>();
                foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    var sourceBounds = filter.sharedMesh.bounds; var matrix = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    Bounds box = default; bool initial = true;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var p = matrix.MultiplyPoint3x4(sourceBounds.center + Vector3.Scale(sourceBounds.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                        if (initial) { box = new Bounds(p, Vector3.zero); initial = false; } else box.Encapsulate(p);
                    }
                    if (box.min.y <= bounds.min.y + .8f && box.size.y > .8f && box.size.x < 1.5f && box.size.z < 1.5f)
                        supports.Add(new Vector3(box.center.x, box.min.y, box.center.z) - pivot);
                }
                if (supports.Count == 0)
                    for (int z = -1; z <= 1; z += 2) for (int x = -1; x <= 1; x += 2) supports.Add(new Vector3(x * grounding.halfFootprint.x, 0, z * grounding.halfFootprint.y));
                grounding.supports = supports.ToArray();
                return PrefabUtility.SaveAsPrefabAsset(root, Folder + "/Buildings/Farmhouse_" + index + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static Mesh HouseProxy(Vector3 size)
        {
            var vertices = new List<Vector3>(); var colors = new List<Color>(); var triangles = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            { int n = vertices.Count; color = color.linear; vertices.AddRange(new[] { a, b, c, d }); colors.AddRange(new[] { color, color, color, color }); triangles.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 }); }
            float x = size.x * .4f, z = size.z * .4f, h = size.y * .6f, top = size.y;
            Color wall = new Color(.46f, .36f, .23f), roof = new Color(.27f, .25f, .22f);
            Quad(new Vector3(-x, 0, -z), new Vector3(-x, h, -z), new Vector3(x, h, -z), new Vector3(x, 0, -z), wall);
            Quad(new Vector3(x, 0, z), new Vector3(x, h, z), new Vector3(-x, h, z), new Vector3(-x, 0, z), wall);
            Quad(new Vector3(-x, 0, z), new Vector3(-x, h, z), new Vector3(-x, h, -z), new Vector3(-x, 0, -z), wall);
            Quad(new Vector3(x, 0, -z), new Vector3(x, h, -z), new Vector3(x, h, z), new Vector3(x, 0, z), wall);
            x *= 1.25f; z *= 1.25f;
            Quad(new Vector3(-x, h, -z), new Vector3(-x * .64f, top, 0), new Vector3(x * .64f, top, 0), new Vector3(x, h, -z), roof);
            Quad(new Vector3(x, h, z), new Vector3(x * .64f, top, 0), new Vector3(-x * .64f, top, 0), new Vector3(-x, h, z), roof);
            Quad(new Vector3(-x, h, z), new Vector3(-x * .64f, top, 0), new Vector3(-x * .64f, top, 0), new Vector3(-x, h, -z), roof);
            Quad(new Vector3(x, h, -z), new Vector3(x * .64f, top, 0), new Vector3(x * .64f, top, 0), new Vector3(x, h, z), roof);
            var mesh = new Mesh { name = "FarmhouseProxy" }; mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static Mesh RiceMesh(bool distant)
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>(); var roots = new List<Vector2>(); var triangles = new List<int>();
            int n = distant ? 3 : 5;
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
            {
                var root = new Vector2((x + .5f) * 4 / n - 2, (z + .5f) * 4 / n - 2);
                int blades = distant ? 3 : 5;
                for (int b = 0; b < blades; b++)
                {
                    float a = b * 2.399963f + x * .17f + z * .33f; var axis = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    float width = distant ? .2f : .08f, height = .65f + .1f * (b % 3); int k = vertices.Count;
                    Vector3 P(float w, float h, float bend) => new Vector3(root.x + axis.x * w + axis.y * bend, h, root.y + axis.y * w - axis.x * bend);
                    vertices.AddRange(new[] { P(-width, 0, 0), P(width, 0, 0), P(0, height, .25f) });
                    var normal = new Vector3(-axis.y * .7f, .45f, axis.x * .7f).normalized;
                    normals.AddRange(new[] { normal, normal, normal }); uv.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(.5f, 1) }); roots.AddRange(new[] { root, root, root }); triangles.AddRange(new[] { k, k + 1, k + 2 });
                    if (!distant && b < 2)
                    {
                        k = vertices.Count; vertices.AddRange(new[] { P(-.035f, height * .85f, .19f), P(.04f, height * .85f, .19f), P(0, height * .72f, .36f) });
                        normals.AddRange(new[] { normal, normal, normal }); uv.AddRange(new[] { new Vector2(2, .86f), new Vector2(2, .86f), new Vector2(2, 1) }); roots.AddRange(new[] { root, root, root }); triangles.AddRange(new[] { k, k + 1, k + 2 });
                    }
                }
            }
            var mesh = new Mesh { name = distant ? "RicePatchDistant" : "RicePatchClose" }; mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetUVs(1, roots); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); return mesh;
        }
        [MenuItem("Tools/Tactical Maps/Rebuild Agriculture Preview")]
        internal static void Rebuild()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            var workbench = Object.FindAnyObjectByType<MapWorkbench>(); var builder = Object.FindAnyObjectByType<MapBuilder>();
            var grass = Object.FindAnyObjectByType<TacticalMaps.Grass.GrassSystem>();
            var map = workbench.Generator.Generate(workbench.SelectedPoint);
            try
            {
                builder.Build(map); grass.Build(map);
                string folder = Folder + "/Preview"; Directory.CreateDirectory(folder); AssetDatabase.Refresh();
                for (int i = 0; i < 3; i++)
                {
                    SavePreview(map.Heightmaps[i], folder + "/Heightmap" + i + ".asset");
                    SavePreview(grass.CurrentMap.Coverage[i], folder + "/GrassCoverage" + i + ".asset");
                }
                SaveFieldMasks(builder, map.BuiltRoot, folder);
                ConstructionSetup.SavePreviewResources(builder, folder, true);
                PruneUnusedBanks(map.BuiltRoot);
                workbench.DisplayResults(map); workbench.SyncControls(); EditorUtility.SetDirty(workbench); EditorUtility.SetDirty(builder);
                AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(builder.gameObject.scene); EditorSceneManager.SaveScene(builder.gameObject.scene);
                Directory.CreateDirectory("Logs/Agriculture"); File.WriteAllText("Logs/Agriculture/preview.txt", $"Fields: {map.Agriculture?.Fields.Length}; farms: {map.Agriculture?.Farms.Length}; tracks: {map.Agriculture?.Tracks.Length}; source: {map.SourcePoint}; crop patches: {map.BuiltRoot.GetComponentInChildren<CropRenderer>()?.PatchCount ?? 0}");
                Debug.Log(File.ReadAllText("Logs/Agriculture/preview.txt"));
            }
            catch { map.Release(); throw; }
        }
        private static void SavePreview(Object asset, string path)
        { if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(asset, path); }
        private static void SaveFieldMasks(MapBuilder builder, Transform root, string folder)
        {
            var agriculture = root.GetComponent<AgricultureMap>(); var terrains = root.GetComponentsInChildren<Terrain>();
            if (agriculture == null) return;
            for (int i = 0; i < 3; i++)
            {
                var transient = agriculture.Masks[i]; string path = folder + "/AgricultureMask_" + i + ".png";
                File.WriteAllBytes(path, transient.EncodeToPNG()); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default; importer.sRGBTexture = false; importer.isReadable = false; importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = 2048;
                importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 4;
                var settings = importer.GetDefaultPlatformTextureSettings(); settings.format = TextureImporterFormat.RGBA32; settings.maxTextureSize = 2048; settings.textureCompression = TextureImporterCompression.Uncompressed; importer.SetPlatformTextureSettings(settings); importer.SaveAndReimport();
                var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(path); agriculture.Masks[i] = saved; terrains[i].materialTemplate.SetTexture("_FarmMap", saved);
                var serialized = new SerializedObject(builder); var resources = serialized.FindProperty("builtResources");
                for (int j = 0; j < resources.arraySize; j++) if (resources.GetArrayElementAtIndex(j).objectReferenceValue == transient) resources.GetArrayElementAtIndex(j).objectReferenceValue = saved;
                serialized.ApplyModifiedPropertiesWithoutUndo(); Object.DestroyImmediate(transient);
            }
            EditorUtility.SetDirty(agriculture);
        }
        private static void PruneUnusedBanks(Transform root)
        {
            var used = new HashSet<string>(); foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)) used.Add(AssetDatabase.GetAssetPath(filter.sharedMesh));
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { Folder + "/Preview" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid), name = Path.GetFileName(path);
                    if ((name.StartsWith("FieldBanks_") || name.StartsWith("PaddyWater_")) && !used.Contains(path)) AssetDatabase.DeleteAsset(path);
                }
                for (int i = 0; i < 3; i++)
                {
                    string legacy = Folder + "/Preview/AgricultureMask_" + i + ".asset";
                    if (AssetDatabase.LoadMainAssetAtPath(legacy) != null) AssetDatabase.DeleteAsset(legacy);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
        }
    }
}
