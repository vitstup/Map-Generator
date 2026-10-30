using System;
using System.Collections.Generic;

namespace TacticalMaps.Generation
{
    internal static class BambooMix
    {
        internal static void Apply(IList<TreePlacement> placements, int ordinaryCount, int totalCount, float fraction, int seed)
        {
            if (ordinaryCount <= 0 || totalCount <= ordinaryCount || fraction <= 0) return;
            var random = new Random(unchecked(seed ^ 0x2731B));
            var groups = new HashSet<(int level, int group)>();
            for (int i = 0; i < placements.Count; i++)
            {
                var p = placements[i];
                // Every grove retains an ordinary tree, including the smallest three-tree groups.
                bool first = groups.Add((p.Level, p.Group));
                if (first || random.NextDouble() >= fraction) continue;
                int prefab = ordinaryCount + random.Next(totalCount - ordinaryCount);
                placements[i] = new TreePlacement(p.Position, prefab, p.Group, p.Rotation, p.WidthScale, p.HeightScale, p.Level);
            }
        }
    }
}
