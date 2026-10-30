using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticalMaps.Editor
{
    // Greedy multi-view coverage of the ORIGINAL alpha-tested cards. No card enlargement,
    // movement or UV/normal changes. Strongly weight the external and internal silhouette.
    internal static class TreeCanopyCoverage
    {
        private const int Resolution = 80, Azimuths = 12, Elevations = 3;

        internal static int[] Select(Mesh source, int[][] leaves, Material material, int budget)
        {
            int pixelCount = Resolution * Resolution * Azimuths * Elevations;
            var owners = new List<int>[pixelCount];
            var pixels = new HashSet<int>[leaves.Length];
            var frontDepth = Enumerable.Repeat(float.NegativeInfinity, pixelCount).ToArray();
            var frontOwner = Enumerable.Repeat(-1, pixelCount).ToArray();
            var positions = source.vertices; var uv = source.uv;
            var bounds = source.bounds; float scale = (Resolution - 4) / (bounds.extents.magnitude * 2);
            var texture = ReadTexture(material.GetTexture("_Leaf_Texture"));
            var tiling = material.GetVector("_Leaf_Tiling"); var offset = material.GetVector("_Leaf_Offset");
            float cutoff = material.GetFloat("_Alpha_Clip_Threshold");
            try
            {
                for (int leaf = 0; leaf < leaves.Length; leaf++)
                {
                    var covered = pixels[leaf] = new HashSet<int>();
                    for (int row = 0; row < Elevations; row++) for (int view = 0; view < Azimuths; view++)
                    {
                        float angle = view * Mathf.PI * 2 / Azimuths, elevation = (10 + row * 30) * Mathf.Deg2Rad;
                        var forward = new Vector3(Mathf.Sin(angle) * Mathf.Cos(elevation), Mathf.Sin(elevation), Mathf.Cos(angle) * Mathf.Cos(elevation));
                        var right = Vector3.Cross(Vector3.up, forward).normalized; var up = Vector3.Cross(forward, right);
                        Vector2 Project(int i) { var p = positions[i] - bounds.center; return new Vector2(Vector3.Dot(p, right), Vector3.Dot(p, up)) * scale + Vector2.one * (Resolution * .5f); }
                        int basePixel = (row * Azimuths + view) * Resolution * Resolution;
                        var indices = leaves[leaf];
                        for (int t = 0; t < indices.Length; t += 3)
                        {
                            int ia = indices[t], ib = indices[t + 1], ic = indices[t + 2];
                            var a = Project(ia); var b = Project(ib); var c = Project(ic);
                            float area = Cross(b - a, c - a); if (Mathf.Abs(area) < .00001f) continue;
                            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x)), 0, Resolution - 1);
                            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x, c.x)), 0, Resolution - 1);
                            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y, c.y)), 0, Resolution - 1);
                            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y, c.y)), 0, Resolution - 1);
                            for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
                            {
                                var p = new Vector2(x + .5f, y + .5f);
                                float wb = Cross(p - a, c - a) / area, wc = Cross(b - a, p - a) / area, wa = 1 - wb - wc;
                                if (wa < 0 || wb < 0 || wc < 0) continue;
                                var texcoord = uv[ia] * wa + uv[ib] * wb + uv[ic] * wc;
                                texcoord = Vector2.Scale(texcoord, tiling) + (Vector2)offset;
                                if (texture != null && texture.GetPixelBilinear(Mathf.Repeat(texcoord.x, 1), Mathf.Repeat(texcoord.y, 1)).a < cutoff) continue;
                                int pixel = basePixel + y * Resolution + x; covered.Add(pixel);
                                float depth = Vector3.Dot(positions[ia] * wa + positions[ib] * wb + positions[ic] * wc - bounds.center, forward);
                                if (depth > frontDepth[pixel]) { frontDepth[pixel] = depth; frontOwner[pixel] = leaf; }
                            }
                        }
                    }
                    foreach (int p in covered) (owners[p] ??= new List<int>()).Add(leaf);
                }
            }
            finally { if (texture != null) UnityEngine.Object.DestroyImmediate(texture); }

            var weights = new double[pixelCount]; var score = new double[leaves.Length];
            for (int p = 0; p < pixelCount; p++)
            {
                if (owners[p] == null) continue;
                int x = p % Resolution, y = p / Resolution % Resolution;
                bool edge = x == 0 || x == Resolution - 1 || y == 0 || y == Resolution - 1 || owners[p - 1] == null || owners[p + 1] == null || owners[p - Resolution] == null || owners[p + Resolution] == null;
                weights[p] = edge ? 8 : 1;
                foreach (int leaf in owners[p]) score[leaf] += weights[p] * (frontOwner[p] == leaf ? 2.5 : 1) / (leaves[leaf].Length / 3.0);
            }
            var queue = new SortedSet<(double score, int leaf)>();
            for (int i = 0; i < leaves.Length; i++) queue.Add((score[i], i));
            var selected = new bool[leaves.Length]; var result = new List<int>(); int triangles = 0;
            while (queue.Count > 0 && triangles < budget)
            {
                var best = queue.Max; queue.Remove(best); int leaf = best.leaf;
                selected[leaf] = true; result.Add(leaf); triangles += leaves[leaf].Length / 3;
                foreach (int p in pixels[leaf])
                {
                    // Diminishing returns retain several layers, rather than a thin shell.
                    double delta = weights[p] * .65; weights[p] *= .35;
                    foreach (int other in owners[p])
                    {
                        if (selected[other]) continue;
                        queue.Remove((score[other], other)); score[other] -= delta * (frontOwner[p] == other ? 2.5 : 1) / (leaves[other].Length / 3.0);
                        queue.Add((score[other], other));
                    }
                }
            }
            return result.ToArray();
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static Texture2D ReadTexture(Texture source)
        {
            if (source == null) return null;
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target); RenderTexture.active = target;
                var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
                texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); texture.Apply(); return texture;
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }
    }
}
