using System.Threading;
using TacticalMaps.Generation;
using UnityEngine;

namespace TacticalMaps.Grass
{
    // Pure CPU result. No Unity objects or placements are retained here.
    internal sealed class GrassCoverage
    {
        internal readonly Color32[][] Pixels;
        internal readonly int[] Resolutions;
        internal GrassCoverage(Color32[][] pixels, int[] resolutions) { Pixels = pixels; Resolutions = resolutions; }

        internal static GrassCoverage Generate(SurfaceData surface, CancellationToken token)
        {
            if (surface == null) return null;
            var pixels = new Color32[3][]; var sizes = new int[3];
            for (int level = 0; level < 3; level++)
            {
                var weights = surface.Weights[level];
                int source = weights.GetLength(0), n = Mathf.Min(512, source); sizes[level] = n;
                pixels[level] = new Color32[n * n];
                for (int z = 0; z < n; z++)
                {
                    token.ThrowIfCancellationRequested();
                    for (int x = 0; x < n; x++)
                    {
                        int sx = Mathf.Min(source - 1, (int)((x + .5f) * source / n));
                        int sz = Mathf.Min(source - 1, (int)((z + .5f) * source / n));
                        float wet = weights[sz, sx, 0], dry = weights[sz, sx, 1];
                        float density = Mathf.Clamp01(wet + dry * .72f + weights[sz, sx, 2] * .08f);
                        pixels[level][z * n + x] = new Color(density, dry / Mathf.Max(.001f, wet + dry), weights[sz, sx, 4], 1);
                    }
                }
            }
            return new GrassCoverage(pixels, sizes);
        }
    }
}
