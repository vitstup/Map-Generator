using System;
using System.Collections.Generic;
using UnityEngine;

namespace TacticalMaps.Generation
{
    // A completed map is owned by its caller, independently of subsequent generations.
    public sealed class MapData
    {
        internal IReadOnlyList<Texture2D> Heightmaps { get; private set; }
        internal IReadOnlyList<Vector2> Sizes { get; private set; }
        internal float TerrainHeightMeters { get; private set; }
        internal float SeaLevelMeters { get; private set; }
        internal float GlobalMaxHeightMeters { get; private set; }
        internal Vector2Int SourcePoint { get; private set; }
        internal float HeightCompression { get; private set; }
        internal SurfaceData Surface { get; private set; }
        internal RockData Rocks { get; private set; }
        internal TreeData Trees { get; private set; }
        private bool released;

        internal MapData(Texture2D[] heightmaps, Vector2[] sizes, float terrainHeightMeters,
            float seaLevelMeters, float globalMaxHeightMeters, Vector2Int sourcePoint, float heightCompression = 1, SurfaceData surface = null, RockData rocks = null, TreeData trees = null)
        {
            if (heightmaps == null || sizes == null || heightmaps.Length != 3 || sizes.Length != 3)
                throw new ArgumentException("A map requires three heightmaps and three world sizes.");
            Heightmaps = Array.AsReadOnly((Texture2D[])heightmaps.Clone());
            Sizes = Array.AsReadOnly((Vector2[])sizes.Clone());
            TerrainHeightMeters = terrainHeightMeters; SeaLevelMeters = seaLevelMeters;
            GlobalMaxHeightMeters = globalMaxHeightMeters; SourcePoint = sourcePoint; HeightCompression = heightCompression;
            Surface = surface;
            Rocks = rocks;
            Trees = trees;
        }

        internal void Release()
        {
            if (released) return;
            released = true;
            Surface = null;
            Rocks = null;
            Trees = null;
            foreach (var texture in Heightmaps)
            {
                if (texture == null) continue;
#if UNITY_EDITOR
                if (UnityEditor.EditorUtility.IsPersistent(texture)) continue;
#endif
                if (Application.isPlaying) UnityEngine.Object.Destroy(texture); else UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
