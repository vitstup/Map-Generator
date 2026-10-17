using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TacticalMaps.Generation
{
    [DisallowMultipleComponent]
    internal sealed class RockPlacementGenerator : MonoBehaviour
    {
        [SerializeField] private bool generateRocks = true;
        [SerializeField] private GameObject[] cliffPrefabs = Array.Empty<GameObject>();
        [SerializeField, Range(15, 80)] private float minimumSlope = 20;
        [SerializeField, Min(1)] private float minimumRelief = 80;
        [SerializeField, Min(8), Tooltip("Sampling distance only. Does not set the dimensions of the cliff meshes.")] private float analysisSpacing = 32;
        [SerializeField, Min(40), Tooltip("Minimum width in metres. Large height differences create still wider sections.")] private float minimumSectionWidth = 180;
        [SerializeField, Min(0), Tooltip("Discard isolated short features; retain connected escarpments and large individual massifs.")] private float minimumEscarpmentLength = 350;
        [SerializeField, Min(100), Tooltip("Maximum terrain distance to search from the crest to the foot of a slope.")] private float maximumSlopeRun = 1000;
        [SerializeField, Range(1, 20)] private float slopeEndAngle = 7;
        [SerializeField, Range(0.1f, 0.45f)] private float sectionOverlap = 0.42f;
        [SerializeField, Min(0)] private float seamClearance = 120;
        [SerializeField, Range(0.1f, 0.6f)] private float burial = 0.25f;
        [SerializeField, Min(1)] private int maximumSectionsPerLevel = 180;
        [SerializeField] private int seed = 173;

        internal RockData Generate(MapData map, CancellationToken token)
        {
            var input = Capture(map);
            return new RockData(input.Prefabs, input.Render(token));
        }
        internal async Task<RockData> GenerateAsync(MapData map, CancellationToken token)
        {
            var input = Capture(map);
            var placements = await Task.Run(() => input.Render(token), token);
            token.ThrowIfCancellationRequested();
            return new RockData(input.Prefabs, placements);
        }
        private RockEscarpmentField Capture(MapData map)
        {
            if (!generateRocks || cliffPrefabs == null || cliffPrefabs.Length == 0)
                return new RockEscarpmentField();
            if (!Finite(analysisSpacing) || !Finite(minimumRelief) || !Finite(minimumSlope) || !Finite(burial) || !Finite(seamClearance) ||
                !Finite(minimumSectionWidth) || !Finite(minimumEscarpmentLength) || !Finite(maximumSlopeRun) || !Finite(slopeEndAngle) || !Finite(sectionOverlap) ||
                analysisSpacing < 8 || minimumSectionWidth < 40 || minimumEscarpmentLength < 0 || maximumSlopeRun < 100 || slopeEndAngle < 1 || slopeEndAngle > 20 || sectionOverlap < 0.1f || sectionOverlap > 0.45f ||
                minimumRelief <= 0 || maximumSectionsPerLevel < 1 || minimumSlope < 1 || minimumSlope >= 89 || burial < 0.1f || burial > 0.6f || seamClearance < 0)
                throw new InvalidOperationException("Invalid cliff placement settings.");
            var bounds = new Bounds[cliffPrefabs.Length];
            for (int i = 0; i < bounds.Length; i++)
            {
                if (cliffPrefabs[i] == null) throw new InvalidOperationException("A cliff prefab is missing.");
                bool first = true;
                foreach (var filter in cliffPrefabs[i].GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    var b = filter.sharedMesh.bounds;
                    var matrix = cliffPrefabs[i].transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var p = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                            new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                        if (first) { bounds[i] = new Bounds(p, Vector3.zero); first = false; } else bounds[i].Encapsulate(p);
                    }
                }
                if (first || bounds[i].size.x < 0.1f || bounds[i].size.y < 0.1f || bounds[i].size.z < 0.1f)
                    throw new InvalidOperationException("Cliff prefab needs a mesh with nonzero dimensions.");
            }
            var pixels = new ushort[3][]; var widths = new int[3]; var heights = new int[3]; var sizes = new Vector2[3];
            for (int i = 0; i < 3; i++)
            { pixels[i] = map.Heightmaps[i].GetPixelData<ushort>(0).ToArray(); widths[i] = map.Heightmaps[i].width; heights[i] = map.Heightmaps[i].height; sizes[i] = map.Sizes[i]; }
            return new RockEscarpmentField { Prefabs = (GameObject[])cliffPrefabs.Clone(), Bounds = bounds, Pixels = pixels,
                Widths = widths, Heights = heights, Sizes = sizes, Height = map.TerrainHeightMeters, Sea = map.SeaLevelMeters,
                Slope = minimumSlope, Relief = minimumRelief, Spacing = analysisSpacing, MinimumWidth = minimumSectionWidth, MinimumSpan = minimumEscarpmentLength, MaximumRun = maximumSlopeRun, EndSlope = slopeEndAngle, Overlap = sectionOverlap, SeamClearance = seamClearance, Burial = burial, Limit = maximumSectionsPerLevel,
                Seed = unchecked(seed ^ map.SourcePoint.x * 73856093 ^ map.SourcePoint.y * 19349663) };
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    }
}
