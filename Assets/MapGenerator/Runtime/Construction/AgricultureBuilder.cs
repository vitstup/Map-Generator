using System;
using System.Collections.Generic;
using System.Diagnostics;
using TacticalMaps.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Construction
{
    [DisallowMultipleComponent]
    internal sealed class AgricultureBuilder : MonoBehaviour
    {
        [SerializeField] private AgricultureLibrary library;
        [SerializeField, Min(50)] private float detailedCropDistance = 110;
        [SerializeField, Min(100)] private float cropDistance = 330;
        [SerializeField, Min(0)] private float detailedBuildingMargin = 200;
        [SerializeField, Min(1)] private int maximumCropPatches = 450000;
        internal double LastBuildMilliseconds { get; private set; }
        private static Terrain[] cachedGround;
        private static Vector3[] cachedOrigins, cachedSizes;
        internal void Generate(MapData map, Transform root, Vector3 center)
        {
            if (map.Agriculture == null) return;
            if (library == null || library.cropMaterial == null || library.culling == null || library.houses == null || library.houses.Length == 0)
                throw new InvalidOperationException("Set up the agriculture resource library first.");
            var clock = Stopwatch.StartNew(); var owned = root.gameObject.AddComponent<AgricultureMap>();
            owned.fields = map.Agriculture.Fields; owned.farms = map.Agriculture.Farms; owned.tracks = map.Agriculture.Tracks;
            var terrains = new Terrain[3];
            for (int l = 0; l < 3; l++)
            {
                terrains[l] = root.Find(l == 0 ? "Terrain_Main" : l == 1 ? "Terrain_Surroundings" : "Terrain_Horizon").GetComponent<Terrain>();
                int n = map.Agriculture.Resolutions[l];
                var texture = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { name = "AgricultureMask_" + l, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 };
                owned.Masks[l] = texture; texture.SetPixels32(map.Agriculture.Masks[l]); texture.Apply(true, false);
                var material = terrains[l].materialTemplate; material.SetTexture("_FarmMap", texture); material.SetFloat("_FarmEnabled", 1);
                material.SetTexture("_FarmSoil", library.soilProfile != null ? library.soilProfile.diffuseTexture : Texture2D.grayTexture);
                material.SetTexture("_FarmSoilNormal", library.soilProfile != null && library.soilProfile.normalMapTexture != null ? library.soilProfile.normalMapTexture : Texture2D.normalTexture);
                var p = terrains[l].transform.position; material.SetVector("_FarmRect", new Vector4(p.x, p.z, map.Sizes[l].x, map.Sizes[l].y));
            }
            owned.BuildSurface(map.Agriculture.Surface, terrains, center);
            var group = new GameObject("Agriculture"); group.transform.SetParent(root, false);
            var render = group.AddComponent<CropRenderer>(); render.Configure(library, map.Agriculture.Fields, terrains, center, map.Sizes[0], detailedCropDistance, cropDistance, maximumCropPatches);
            var banks = new Dictionary<Vector2Int, Geometry>(); var water = new Dictionary<Vector2Int, Geometry>();
            Geometry BankAt(Vector2 p)
            { var key = new Vector2Int(Mathf.FloorToInt(p.x / 256), Mathf.FloorToInt(p.y / 256)); if (!banks.TryGetValue(key, out var g)) banks.Add(key, g = new Geometry()); return g; }
            foreach (var field in map.Agriculture.Fields)
            {
                if (Mathf.Abs(field.center.x) > map.Sizes[0].x / 2 + 600 || Mathf.Abs(field.center.y) > map.Sizes[0].y / 2 + 600) continue;
                var geometry = BankAt(field.center); var contour = Contour(field); float[] offsets = { -1.65f, -.5f, .5f, 1.7f };
                var rings = new Vector3[contour.Count * 4];
                for (int i = 0; i < contour.Count; i++)
                {
                    var p = contour[i]; var middle = Ground(p.Position, terrains, center);
                    float variation = .04f * Mathf.Sin(p.Position.x * .11f + p.Position.y * .07f);
                    float crest = Mathf.Max(middle.y + .48f + variation, field.crop == 3 ? field.height + center.y + .38f : float.MinValue);
                    for (int ring = 0; ring < 4; ring++)
                    {
                        var q = p.Position + p.Normal * offsets[ring];
                        rings[i * 4 + ring] = ring == 0 || ring == 3 ? Ground(q, terrains, center, -.13f) : new Vector3(q.x + center.x, crest, q.y + center.z);
                    }
                }
                for (int i = 0; i < contour.Count; i++)
                {
                    int a = i * 4, b = ((i + 1) % contour.Count) * 4;
                    for (int ring = 0; ring < 3; ring++) geometry.Quad(rings[a + ring], rings[b + ring], rings[b + ring + 1], rings[a + ring + 1], new Color(1, 1, 1, 0));
                    if (i % 3 == 0 && Mathf.Sin(contour[i].Position.x * 1.7f + contour[i].Position.y * 2.3f) > .3f)
                    {
                        var p = rings[a + 2]; geometry.Tuft(p, .18f + .1f * Mathf.Abs(Mathf.Sin(p.x)), new Color(.28f, .37f, .14f));
                    }
                }
                if (field.crop == 3)
                {
                    var key = new Vector2Int(Mathf.FloorToInt(field.center.x / 256), Mathf.FloorToInt(field.center.y / 256));
                    if (!water.TryGetValue(key, out var g)) water.Add(key, g = new Geometry());
                    var middle = new Vector3(field.center.x + center.x, field.height + center.y, field.center.y + center.z);
                    for (int i = 0; i < contour.Count; i++)
                    {
                        var a = contour[i]; var b = contour[(i + 1) % contour.Count]; var p = a.Position - a.Normal * .22f; var q = b.Position - b.Normal * .22f;
                        g.Triangle(middle, new Vector3(q.x + center.x, middle.y, q.y + center.z), new Vector3(p.x + center.x, middle.y, p.y + center.z), Color.white);
                    }
                }
            }
            foreach (var farm in map.Agriculture.Farms)
            {
                bool detailed = Mathf.Abs(farm.center.x) < map.Sizes[0].x / 2 + detailedBuildingMargin && Mathf.Abs(farm.center.y) < map.Sizes[0].y / 2 + detailedBuildingMargin;
                var house = library.houses[farm.variant % library.houses.Length];
                for (int i = 0; i < 2; i++)
                {
                    float angle = farm.angle + (i == 0 ? 0 : Mathf.PI / 2); var offset = i == 0 ? Vector2.zero : new Vector2(Mathf.Cos(farm.angle), Mathf.Sin(farm.angle)) * 20;
                    GameObject go;
                    if (detailed) go = Instantiate(house, group.transform);
                    else
                    {
                        var proxy = house.transform.Find("Distant"); go = new GameObject("DistantFarm", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(group.transform, false);
                        go.GetComponent<MeshFilter>().sharedMesh = proxy.GetComponent<MeshFilter>().sharedMesh;
                        var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = proxy.GetComponent<MeshRenderer>().sharedMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off;
                        var lod = go.AddComponent<LODGroup>(); lod.SetLODs(new[] { new LOD(.0008f, new Renderer[] { renderer }) }); lod.RecalculateBounds();
                    }
                    go.name = i == 0 ? "Farmhouse" : "Storehouse";
                    go.transform.position = Ground(farm.center + offset, terrains, center, -.035f); go.transform.rotation = Quaternion.Euler(0, -angle * Mathf.Rad2Deg, 0);
                    if (i == 1) go.transform.localScale = Vector3.one * .65f;
                    if (detailed)
                    {
                        var grounding = go.GetComponent<FarmBuildingGrounding>(); var geometry = BankAt(farm.center + offset);
                        if (grounding != null && grounding.supports != null)
                            foreach (var support in grounding.supports)
                            {
                                var p = go.transform.TransformPoint(support); var floor = Ground(new Vector2(p.x - center.x, p.z - center.z), terrains, center);
                                if (p.y > floor.y + .015f) geometry.Box(p, floor.y - .12f, p.y + .04f, .32f * go.transform.lossyScale.x, new Color(.48f, .46f, .39f, .5f));
                            }
                    }
                }
            }
            foreach (var pair in banks) owned.AddMesh(pair.Value.Build(group.transform, "FieldBanks_" + pair.Key, library.bankMaterial, true));
            foreach (var pair in water) owned.AddMesh(pair.Value.Build(group.transform, "PaddyWater_" + pair.Key, library.waterMaterial, false));
            LastBuildMilliseconds = clock.Elapsed.TotalMilliseconds;
        }
        private struct ContourPoint { internal Vector2 Position, Normal; }
        private static List<ContourPoint> Contour(FarmField field)
        {
            var local = new List<ContourPoint>(); float r = field.CornerRadius;
            for (int corner = 0; corner < 4; corner++)
            {
                var origin = new Vector2(corner == 0 || corner == 3 ? field.halfSize.x - r : -field.halfSize.x + r, corner < 2 ? field.halfSize.y - r : -field.halfSize.y + r);
                for (int i = 0; i <= 3; i++) { float angle = (corner * 90 + i * 30) * Mathf.Deg2Rad; var normal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)); local.Add(new ContourPoint { Position = origin + normal * r, Normal = normal }); }
            }
            var result = new List<ContourPoint>(); var axis = field.Axis; var side = new Vector2(-axis.y, axis.x);
            for (int i = 0; i < local.Count; i++)
            {
                var a = local[i]; var b = local[(i + 1) % local.Count]; int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a.Position, b.Position) / 3));
                for (int j = 0; j < steps; j++)
                {
                    float t = (float)j / steps; var p = Vector2.Lerp(a.Position, b.Position, t); var n = Vector2.Lerp(a.Normal, b.Normal, t).normalized;
                    result.Add(new ContourPoint { Position = field.center + axis * p.x + side * p.y, Normal = axis * n.x + side * n.y });
                }
            }
            return result;
        }
        internal static Vector3 Ground(Vector2 local, Terrain[] terrains, Vector3 center, float offset = 0)
        {
            if (!ReferenceEquals(cachedGround, terrains))
            {
                cachedGround = terrains; cachedOrigins = new Vector3[terrains.Length]; cachedSizes = new Vector3[terrains.Length];
                for (int i = 0; i < terrains.Length; i++) { cachedOrigins[i] = terrains[i].transform.position; cachedSizes[i] = terrains[i].terrainData.size; }
            }
            Vector3 p = center + new Vector3(local.x, 0, local.y);
            for (int i = 0; i < terrains.Length; i++)
            { var o = cachedOrigins[i]; var s = cachedSizes[i]; if (p.x < o.x || p.z < o.z || p.x > o.x + s.x || p.z > o.z + s.z) continue; p.y = terrains[i].SampleHeight(p) + o.y + offset; return p; }
            return p;
        }
        private sealed class Geometry
        {
            private readonly List<Vector3> vertices = new List<Vector3>(); private readonly List<int> indices = new List<int>(); private readonly List<Color> colors = new List<Color>();
            internal void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
            { int n = vertices.Count; color = Linear(color); vertices.AddRange(new[] { a, b, c }); colors.AddRange(new[] { color, color, color }); indices.AddRange(new[] { n, n + 1, n + 2 }); }
            internal void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            { int n = vertices.Count; color = Linear(color); vertices.AddRange(new[] { a, b, c, d }); colors.AddRange(new[] { color, color, color, color }); indices.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 }); }
            private static Color Linear(Color c) { float a = c.a; c = c.linear; c.a = a; return c; }
            internal void Tuft(Vector3 p, float height, Color color)
            { color.a = 1; for (int i = 0; i < 3; i++) { float a = i * 2.4f; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .07f; Triangle(p - d, p + d, p + Vector3.up * height + d * 2, color); } }
            internal void Box(Vector3 p, float bottom, float top, float half, Color color)
            {
                Vector3 V(int x, int z, float y) => new Vector3(p.x + x * half, y, p.z + z * half);
                Quad(V(-1, -1, bottom), V(1, -1, bottom), V(1, -1, top), V(-1, -1, top), color); Quad(V(1, -1, bottom), V(1, 1, bottom), V(1, 1, top), V(1, -1, top), color);
                Quad(V(1, 1, bottom), V(-1, 1, bottom), V(-1, 1, top), V(1, 1, top), color); Quad(V(-1, 1, bottom), V(-1, -1, bottom), V(-1, -1, top), V(-1, 1, top), color); Quad(V(-1, -1, top), V(1, -1, top), V(1, 1, top), V(-1, 1, top), color);
            }
            internal Mesh Build(Transform parent, string name, Material material, bool shadows)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 }; mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false); go.transform.position = Vector3.zero;
                go.GetComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off; return mesh;
            }
        }
    }
}
