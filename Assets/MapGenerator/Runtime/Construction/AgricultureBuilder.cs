using System;
using System.Collections.Generic;
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
        internal void Generate(MapData map, Transform root, Vector3 center)
        {
            if (map.Agriculture == null) return;
            if (library == null || library.cropMaterial == null || library.culling == null || library.houses == null || library.houses.Length == 0)
                throw new InvalidOperationException("Set up the agriculture resource library first.");
            var owned = root.gameObject.AddComponent<AgricultureMap>();
            owned.fields = map.Agriculture.Fields; owned.farms = map.Agriculture.Farms;
            owned.tracks = map.Agriculture.Tracks;
            var terrains = new Terrain[3];
            for (int l = 0; l < 3; l++)
            {
                terrains[l] = root.Find(l == 0 ? "Terrain_Main" : l == 1 ? "Terrain_Surroundings" : "Terrain_Horizon").GetComponent<Terrain>();
                int n = map.Agriculture.Resolutions[l];
                var texture = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { name = "AgricultureMask_" + l, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
                owned.Masks[l] = texture;
                texture.SetPixels32(map.Agriculture.Masks[l]); texture.Apply(true, false);
                var material = terrains[l].materialTemplate;
                material.SetTexture("_FarmMap", texture); material.SetFloat("_FarmEnabled", 1);
                material.SetTexture("_FarmSoil", library.soilProfile != null ? library.soilProfile.diffuseTexture : Texture2D.grayTexture);
                var p = terrains[l].transform.position;
                material.SetVector("_FarmRect", new Vector4(p.x, p.z, map.Sizes[l].x, map.Sizes[l].y));
            }
            var group = new GameObject("Agriculture"); group.transform.SetParent(root, false);
            var render = group.AddComponent<CropRenderer>();
            render.Configure(library, map.Agriculture.Fields, terrains, center, map.Sizes[0], detailedCropDistance, cropDistance, maximumCropPatches);
            var banks = new Dictionary<Vector2Int, Geometry>(); var water = new Dictionary<Vector2Int, Geometry>();
            foreach (var field in map.Agriculture.Fields)
            {
                if (Mathf.Abs(field.center.x) > map.Sizes[0].x / 2 + cropDistance + 100 || Mathf.Abs(field.center.y) > map.Sizes[0].y / 2 + cropDistance + 100) continue;
                var key = new Vector2Int(Mathf.FloorToInt(field.center.x / 256), Mathf.FloorToInt(field.center.y / 256));
                if (!banks.TryGetValue(key, out var geometry)) banks.Add(key, geometry = new Geometry());
                for (int side = 0; side < 4; side++)
                {
                    var a = field.Point(side < 2 ? -field.halfSize.x : field.halfSize.x, side == 0 || side == 3 ? -field.halfSize.y : field.halfSize.y);
                    var b = field.Point(side == 0 || side == 3 ? field.halfSize.x : -field.halfSize.x, side < 2 ? field.halfSize.y : -field.halfSize.y);
                    // Explicit edge endpoints, subdivided to follow the terrain.
                    if (side == 0) { a = field.Point(-field.halfSize.x, -field.halfSize.y); b = field.Point(field.halfSize.x, -field.halfSize.y); }
                    if (side == 1) { a = field.Point(field.halfSize.x, -field.halfSize.y); b = field.Point(field.halfSize.x, field.halfSize.y); }
                    if (side == 2) { a = field.Point(field.halfSize.x, field.halfSize.y); b = field.Point(-field.halfSize.x, field.halfSize.y); }
                    if (side == 3) { a = field.Point(-field.halfSize.x, field.halfSize.y); b = field.Point(-field.halfSize.x, -field.halfSize.y); }
                    int steps = Mathf.CeilToInt(Vector2.Distance(a, b) / 6);
                    var d = (b - a).normalized; var across = new Vector2(-d.y, d.x) * .8f;
                    for (int s = 0; s < steps; s++)
                    {
                        var p = Vector2.Lerp(a, b, (float)s / steps); var q = Vector2.Lerp(a, b, (float)(s + 1) / steps);
                        geometry.Quad(Ground(p - across, terrains, center, .03f), Ground(q - across, terrains, center, .03f), Ground(q, terrains, center, .28f), Ground(p, terrains, center, .28f), new Color(.37f, .34f, .19f));
                        geometry.Quad(Ground(p, terrains, center, .28f), Ground(q, terrains, center, .28f), Ground(q + across, terrains, center, .03f), Ground(p + across, terrains, center, .03f), new Color(.37f, .34f, .19f));
                    }
                }
                if (field.crop == 3)
                {
                    float low = float.MaxValue, high = float.MinValue;
                    for (int z = -2; z <= 2; z++) for (int x = -2; x <= 2; x++)
                    {
                        float height = Ground(field.Point(x * field.halfSize.x / 2, z * field.halfSize.y / 2), terrains, center).y;
                        low = Mathf.Min(low, height); high = Mathf.Max(high, height);
                    }
                    // Construction seam offsets/resampling are included in the local water elevation.
                    if (high - low > .32f) continue;
                    if (!water.TryGetValue(key, out var g)) water.Add(key, g = new Geometry());
                    Vector3 P(float x, float z) { var p = field.Point(x, z); return new Vector3(p.x + center.x, high + .04f, p.y + center.z); }
                    g.Quad(P(-field.halfSize.x + 1, -field.halfSize.y + 1), P(-field.halfSize.x + 1, field.halfSize.y - 1), P(field.halfSize.x - 1, field.halfSize.y - 1), P(field.halfSize.x - 1, -field.halfSize.y + 1), new Color(.24f, .29f, .21f, .65f));
                }
            }
            foreach (var pair in banks) owned.AddMesh(pair.Value.Build(group.transform, "FieldBanks_" + pair.Key, library.bankMaterial));
            foreach (var pair in water) owned.AddMesh(pair.Value.Build(group.transform, "PaddyWater_" + pair.Key, library.waterMaterial));
            foreach (var farm in map.Agriculture.Farms)
            {
                bool detailed = Mathf.Abs(farm.center.x) < map.Sizes[0].x / 2 + detailedBuildingMargin && Mathf.Abs(farm.center.y) < map.Sizes[0].y / 2 + detailedBuildingMargin;
                var house = library.houses[farm.variant % library.houses.Length];
                for (int i = 0; i < 2; i++)
                {
                    var angle = farm.angle + (i == 0 ? 0 : Mathf.PI / 2);
                    var offset = i == 0 ? Vector2.zero : new Vector2(Mathf.Cos(farm.angle), Mathf.Sin(farm.angle)) * 16;
                    GameObject go;
                    if (detailed) go = Instantiate(house, group.transform);
                    else
                    {
                        // Distant farms never instantiate the compound detailed hierarchy.
                        var proxy = house.transform.Find("Distant");
                        go = new GameObject("DistantFarm", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(group.transform, false);
                        go.GetComponent<MeshFilter>().sharedMesh = proxy.GetComponent<MeshFilter>().sharedMesh;
                        var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = proxy.GetComponent<MeshRenderer>().sharedMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off;
                        var lod = go.AddComponent<LODGroup>(); lod.SetLODs(new[] { new LOD(.0008f, new Renderer[] { renderer }) }); lod.RecalculateBounds();
                    }
                    go.name = i == 0 ? "Farmhouse" : "Storehouse";
                    go.transform.position = Ground(farm.center + offset, terrains, center, .15f);
                    go.transform.rotation = Quaternion.Euler(0, -angle * Mathf.Rad2Deg, 0);
                    if (i == 1) go.transform.localScale = Vector3.one * .65f;
                }
            }
        }
        internal static Vector3 Ground(Vector2 local, Terrain[] terrains, Vector3 center, float offset = 0)
        {
            Vector3 p = center + new Vector3(local.x, 0, local.y);
            foreach (var t in terrains)
            {
                var o = t.transform.position; var s = t.terrainData.size;
                if (p.x < o.x || p.z < o.z || p.x > o.x + s.x || p.z > o.z + s.z) continue;
                p.y = t.SampleHeight(p) + o.y + offset; return p;
            }
            return p;
        }
        private sealed class Geometry
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<int> indices = new List<int>();
            private readonly List<Color> colors = new List<Color>();
            internal void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            { int n = vertices.Count; color = color.linear; vertices.AddRange(new[] { a, b, c, d }); colors.AddRange(new[] { color, color, color, color }); indices.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 }); }
            internal Mesh Build(Transform parent, string name, Material material)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 }; mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false);
                // Geometry vertices are world positions; the owned construction root may be translated.
                go.transform.position = Vector3.zero;
                go.GetComponent<MeshFilter>().sharedMesh = mesh; var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off;
                return mesh;
            }
        }
    }
}
