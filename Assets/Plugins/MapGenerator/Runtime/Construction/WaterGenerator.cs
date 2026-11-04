using System;
using System.Collections.Generic;
using TacticalMaps.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Construction
{
    internal sealed class WaterGenerator : MonoBehaviour
    {
        [SerializeField, LayerIndex] private int waterLayer = 4;
        [SerializeField] private Material waterMaterial;
        [SerializeField, Range(16, 192)] private int innerSegments = 96;
        [SerializeField, Range(4, 48)] private int middleBandSegments = 24;
        [SerializeField, Range(4, 32)] private int outerBandSegments = 16;

        internal void Generate(MapData map, Transform parent, Vector3 center)
        {
            if (waterLayer < 0 || waterLayer > 31) throw new ArgumentException("Water layer must be 0–31.");
            if (waterMaterial == null) throw new InvalidOperationException("Назначьте материал воды на WaterGenerator.");
            if (innerSegments < 16 || innerSegments > 192 || middleBandSegments < 4 || middleBandSegments > 48 || outerBandSegments < 4 || outerBandSegments > 32)
                throw new ArgumentException("Invalid water tessellation settings.");
            var go = new GameObject("Water", typeof(MeshFilter), typeof(MeshRenderer));
            go.layer = waterLayer; go.transform.SetParent(parent, false);
            go.transform.localPosition = center + Vector3.up * map.SeaLevelMeters;
            var filter = go.GetComponent<MeshFilter>();
            filter.sharedMesh = CreateMesh(map);
            var renderer = go.GetComponent<MeshRenderer>();
            var material = new Material(waterMaterial) { name = "WaterSurface" };
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Vector3 worldCenter = parent.TransformPoint(center);
            material.SetVector("_MapCenter", worldCenter);
            material.SetFloat("_HeightRange", map.TerrainHeightMeters);
            material.SetFloat("_WaterLevel", worldCenter.y + map.SeaLevelMeters);
            for (int i = 0; i < 3; i++)
            {
                var size = map.Sizes[i];
                material.SetVector("_MapSize" + i, new Vector4(size.x, size.y, 1 / size.x, 1 / size.y));
                material.SetTexture("_Depth" + i, CopyDepth(map.Heightmaps[i], i == 0 ? 1025 : i == 1 ? 513 : 257));
            }
        }

        private Mesh CreateMesh(MapData map)
        {
            var xs = CreateAxis(map.Sizes[0].x, map.Sizes[1].x, map.Sizes[2].x);
            var zs = CreateAxis(map.Sizes[0].y, map.Sizes[1].y, map.Sizes[2].y);
            var vertices = new Vector3[xs.Count * zs.Count];
            var normals = new Vector3[vertices.Length];
            for (int z = 0; z < zs.Count; z++) for (int x = 0; x < xs.Count; x++)
            { int index = z * xs.Count + x; vertices[index] = new Vector3(xs[x], 0, zs[z]); normals[index] = Vector3.up; }
            var triangles = new int[(xs.Count - 1) * (zs.Count - 1) * 6];
            int t = 0;
            for (int z = 0; z < zs.Count - 1; z++) for (int x = 0; x < xs.Count - 1; x++)
            {
                int a = z * xs.Count + x, b = a + 1, c = a + xs.Count, d = c + 1;
                // Alternate diagonals to keep a regular grid from dominating the faceted highlights.
                if (((x + z) & 1) == 0)
                { triangles[t++] = a; triangles[t++] = c; triangles[t++] = b; triangles[t++] = b; triangles[t++] = c; triangles[t++] = d; }
                else
                { triangles[t++] = a; triangles[t++] = c; triangles[t++] = d; triangles[t++] = a; triangles[t++] = d; triangles[t++] = b; }
            }
            var mesh = new Mesh { name = "LowPolyWater", indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = vertices; mesh.normals = normals; mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(map.Sizes[2].x, 128, map.Sizes[2].y));
            return mesh;
        }
        private List<float> CreateAxis(float inner, float middle, float outer)
        {
            var values = new List<float>();
            AddBand(values, -outer / 2, -middle / 2, outerBandSegments);
            AddBand(values, -middle / 2, -inner / 2, middleBandSegments);
            AddBand(values, -inner / 2, inner / 2, innerSegments);
            AddBand(values, inner / 2, middle / 2, middleBandSegments);
            AddBand(values, middle / 2, outer / 2, outerBandSegments);
            values.Add(outer / 2); return values;
        }
        private static void AddBand(List<float> values, float from, float to, int count)
        { for (int i = 0; i < count; i++) values.Add(Mathf.Lerp(from, to, (float)i / count)); }

        private static Texture2D CopyDepth(Texture2D source, int maximum)
        {
            int width = Mathf.Min(source.width, maximum), height = Mathf.Min(source.height, maximum);
            var raw = source.GetPixelData<ushort>(0); var pixels = new ushort[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                float px = (float)x / (width - 1) * (source.width - 1), py = (float)y / (height - 1) * (source.height - 1);
                int sx = Mathf.Min((int)px, source.width - 2), sy = Mathf.Min((int)py, source.height - 2);
                float h = Mathf.Lerp(Mathf.Lerp(raw[sy * source.width + sx], raw[sy * source.width + sx + 1], px - sx),
                    Mathf.Lerp(raw[(sy + 1) * source.width + sx], raw[(sy + 1) * source.width + sx + 1], px - sx), py - sy);
                pixels[y * width + x] = (ushort)Mathf.RoundToInt(h);
            }
            var texture = new Texture2D(width, height, TextureFormat.R16, false, true)
            { name = "WaterDepth", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixelData(pixels, 0); texture.Apply(false, false); return texture;
        }
    }
}
