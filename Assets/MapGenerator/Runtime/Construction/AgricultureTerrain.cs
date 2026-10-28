using TacticalMaps.Generation;
using UnityEngine;

namespace TacticalMaps.Construction
{
    internal static class AgricultureTerrain
    {
        // Edit the already constructed CPU height array before uploading it once. No per-field
        // TerrainData.GetHeights/SetHeights or repeated terrain LOD recalculations.
        internal static void Apply(MapData map, float[,] heights, int level, float overlap)
        {
            var data = map.Agriculture; if (data == null) return;
            Vector2 size = map.Sizes[level]; int n = heights.GetLength(0);
            foreach (var field in data.Fields)
            {
                if (field.crop != 3 || Mathf.Abs(field.center.x) > map.Sizes[0].x / 2 + 600 || Mathf.Abs(field.center.y) > map.Sizes[0].y / 2 + 600) continue;
                Stamp(field.center, field.halfSize, field.angle, field.CornerRadius, field.height - .14f, -.6f, 1.7f);
            }
            foreach (var farm in data.Farms)
            {
                if (Mathf.Abs(farm.center.x) > map.Sizes[0].x / 2 + 300 || Mathf.Abs(farm.center.y) > map.Sizes[0].y / 2 + 300) continue;
                foreach (var building in farm.Buildings)
                    Stamp(building.center, building.halfSize + Vector2.one * .35f, building.angle, 1, building.height, -.2f, 1.2f);
            }
            void Stamp(Vector2 center, Vector2 half, float angle, float radius, float elevation, float inner, float outer)
            {
                float c = Mathf.Cos(angle), s = Mathf.Sin(angle); var e = new Vector2(Mathf.Abs(c) * half.x + Mathf.Abs(s) * half.y, Mathf.Abs(s) * half.x + Mathf.Abs(c) * half.y) + Vector2.one * outer;
                if (center.x - e.x > size.x / 2 || center.x + e.x < -size.x / 2 || center.y - e.y > size.y / 2 || center.y + e.y < -size.y / 2) return;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(((center.x - e.x) / size.x + .5f) * (n - 1)), 0, n - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(((center.x + e.x) / size.x + .5f) * (n - 1)), 0, n - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt(((center.y - e.y) / size.y + .5f) * (n - 1)), 0, n - 1), z1 = Mathf.Clamp(Mathf.CeilToInt(((center.y + e.y) / size.y + .5f) * (n - 1)), 0, n - 1);
                float target = (elevation + level * overlap) / map.TerrainHeightMeters;
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                {
                    var d = new Vector2(((float)x / (n - 1) - .5f) * size.x, ((float)z / (n - 1) - .5f) * size.y) - center;
                    var q = new Vector2(Mathf.Abs(d.x * c + d.y * s), Mathf.Abs(-d.x * s + d.y * c)) - half + Vector2.one * radius;
                    float distance = Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - radius;
                    float weight = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(inner, outer, distance));
                    heights[z, x] = Mathf.Lerp(heights[z, x], target, weight);
                }
            }
        }
    }
}
