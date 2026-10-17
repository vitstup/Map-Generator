using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TacticalMaps.Generation
{
    [DisallowMultipleComponent]
    internal sealed class TreePlacementGenerator : MonoBehaviour
    {
        [SerializeField] private bool generateTrees = true;
        [SerializeField] private GameObject[] treePrefabs = Array.Empty<GameObject>();
        [SerializeField, Range(0, 1), Tooltip("Maximum fraction of the entire main terrain occupied by forest zones, including water in the denominator.")] private float maximumCoverage = 0.30f;
        [SerializeField, Min(2)] private float zoneCellSize = 12;
        [SerializeField, Min(10)] private float minimumGroveRadius = 65;
        [SerializeField, Min(10)] private float maximumGroveRadius = 180;
        [SerializeField, Min(0)] private float minimumGroveArea = 2000;
        [SerializeField, Min(2)] private float treeSpacing = 10;
        [SerializeField, Range(0.3f, 1)] private float minimumSpacingRatio = 0.7f;
        [SerializeField, Range(0, 60)] private float maximumSlope = 28;
        [SerializeField, Min(0)] private float shoreClearance = 12;
        [SerializeField, Min(0)] private float minimumHeightAboveWater = 2;
        [SerializeField] private Vector2 scaleRange = new Vector2(0.85f, 1.25f);
        [SerializeField, Min(1)] private int maximumTrees = 25000;
        [SerializeField] private int seed = 5843;

        internal TreeData Generate(MapData map, CancellationToken token, Bounds[] additionalBlockers = null)
        {
            var input = Capture(map);
            if (additionalBlockers != null) input.Rocks.AddRange(additionalBlockers);
            return input.Generate(token);
        }
        internal async Task<TreeData> GenerateAsync(MapData map, CancellationToken token)
        {
            var input = Capture(map);
            var result = await Task.Run(() => input.Generate(token), token);
            token.ThrowIfCancellationRequested();
            return result;
        }

        private ForestField Capture(MapData map)
        {
            if (!generateTrees || treePrefabs == null || treePrefabs.Length == 0) return new ForestField();
            foreach (float v in new[] { maximumCoverage, zoneCellSize, minimumGroveRadius, maximumGroveRadius,
                minimumGroveArea, treeSpacing, minimumSpacingRatio, maximumSlope, shoreClearance, minimumHeightAboveWater, scaleRange.x, scaleRange.y })
                if (float.IsNaN(v) || float.IsInfinity(v)) throw new InvalidOperationException("Tree settings must be finite.");
            if (maximumCoverage < 0 || maximumCoverage > 1 || zoneCellSize < 2 || minimumGroveRadius < 10 || maximumGroveRadius < minimumGroveRadius ||
                minimumGroveArea < 0 || treeSpacing < 2 || minimumSpacingRatio < 0.3f || minimumSpacingRatio > 1 || maximumSlope < 0 || maximumSlope > 60 ||
                shoreClearance < 0 || minimumHeightAboveWater < 0 || scaleRange.x <= 0 || scaleRange.y < scaleRange.x || maximumTrees < 1)
                throw new InvalidOperationException("Invalid forest placement settings.");
            foreach (var prefab in treePrefabs)
                if (prefab == null || prefab.GetComponentInChildren<MeshRenderer>(true) == null)
                    throw new InvalidOperationException("Assign tree prefabs with mesh renderers.");
            var texture = map.Heightmaps[0];
            var input = new ForestField
            {
                Prefabs = (GameObject[])treePrefabs.Clone(), Pixels = texture.GetPixelData<ushort>(0).ToArray(),
                Width = texture.width, Height = texture.height, Size = map.Sizes[0], VerticalSize = map.TerrainHeightMeters,
                Sea = map.SeaLevelMeters, Coverage = maximumCoverage, CellSize = zoneCellSize,
                MinimumRadius = minimumGroveRadius, MaximumRadius = maximumGroveRadius, MinimumArea = minimumGroveArea,
                Spacing = treeSpacing, SpacingRatio = minimumSpacingRatio, Slope = maximumSlope, Shore = shoreClearance,
                WaterHeight = minimumHeightAboveWater, ScaleRange = scaleRange, Limit = maximumTrees,
                Seed = unchecked(seed ^ map.SourcePoint.x * 73856093 ^ map.SourcePoint.y * 19349663)
            };
            // Conservative projected bounds keep trunks out of the generated cliff masses.
            if (map.Rocks != null)
            {
                var bounds = new Bounds[map.Rocks.Prefabs.Count];
                for (int i = 0; i < bounds.Length; i++)
                {
                    var prefab = map.Rocks.Prefabs[i]; bool first = true;
                    foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (filter.sharedMesh == null) continue;
                        var b = filter.sharedMesh.bounds;
                        var matrix = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                        for (int c = 0; c < 8; c++)
                        {
                            var p = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, Corner(c)));
                            if (first) { bounds[i] = new Bounds(p, Vector3.zero); first = false; } else bounds[i].Encapsulate(p);
                        }
                    }
                }
                foreach (var rock in map.Rocks.Placements)
                {
                    if (rock.Level != 0) continue;
                    var matrix = Matrix4x4.TRS(rock.Position, rock.Rotation, rock.Scale);
                    var b = bounds[rock.Prefab]; var projected = new Bounds(matrix.MultiplyPoint3x4(b.center), Vector3.zero);
                    for (int c = 0; c < 8; c++) projected.Encapsulate(matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, Corner(c))));
                    projected.Expand(4); input.Rocks.Add(projected);
                }
            }
            return input;
        }
        private static Vector3 Corner(int c) => new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1);
    }
}
