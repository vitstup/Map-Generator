using System;
using UnityEngine;

namespace TacticalMaps.Generation
{
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom("TacticalMaps")]
    internal sealed class MapLevel
    {
        internal Vector2Int Resolution { get => resolution; set => resolution = value; }
        internal Vector2 Size { get => size; set => size = value; }
        [SerializeField] private Vector2Int resolution = new Vector2Int(2049, 2049);
        [SerializeField] private Vector2 size = new Vector2(2048, 2048);
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom("TacticalMaps")]
    internal sealed class MapSettings
    {
        [SerializeField, Range(1, 512)] private int sourceRadius = 50;
        [SerializeField] private MapLevel[] levels = {
            new MapLevel(),
            new MapLevel { Size = new Vector2(8096, 8096) },
            new MapLevel { Size = new Vector2(20000, 20000) }
        };
        [SerializeField, Min(1)] private float globalMaxHeightMeters = 4000;
        [SerializeField, Min(1), UnityEngine.Serialization.FormerlySerializedAs("heightMeters")] private float terrainHeightMeters = 4000;
        [SerializeField, Min(0)] private float seaLevelMeters;
        [SerializeField, Range(0.25f, 12)] private float coastPixels = 2;
        [SerializeField, Range(0.25f, 16)] private float smoothingPixels = 3;
        [SerializeField, Range(1, 40)] private float maxSlope = 12;
        [SerializeField, Range(10, 5000)] private float transitionMeters = 1500;

        internal int SourceRadius { get => sourceRadius; set => sourceRadius = value; }
        internal MapLevel[] Levels { get => levels; set => levels = value; }
        internal float GlobalMaxHeightMeters { get => globalMaxHeightMeters; set => globalMaxHeightMeters = value; }
        internal float TerrainHeightMeters { get => terrainHeightMeters; set => terrainHeightMeters = value; }
        internal float SeaLevelMeters { get => seaLevelMeters; set => seaLevelMeters = value; }
        internal float CoastPixels { get => coastPixels; set => coastPixels = value; }
        internal float SmoothingPixels { get => smoothingPixels; set => smoothingPixels = value; }
        internal float MaxSlope { get => maxSlope; set => maxSlope = value; }
        internal float TransitionMeters { get => transitionMeters; set => transitionMeters = value; }

        internal MapSettings Copy() => JsonUtility.FromJson<MapSettings>(JsonUtility.ToJson(this));

        internal void Validate(int width, int height)
        {
            if (levels == null || levels.Length != 3) throw new ArgumentException("Exactly three levels are required.");
            if (sourceRadius < 1 || sourceRadius > 512) throw new ArgumentException("Source radius: 1–512 pixels.");
            if (width < 2 || height < 2) throw new ArgumentException("Source textures are too small.");
            if (!Finite(globalMaxHeightMeters) || globalMaxHeightMeters <= 0 || !Finite(terrainHeightMeters) || terrainHeightMeters <= 0 || !Finite(seaLevelMeters) || seaLevelMeters < 0 || !Finite(coastPixels) || coastPixels <= 0 ||
                !Finite(smoothingPixels) || smoothingPixels <= 0 || !Finite(maxSlope) || maxSlope <= 0 || maxSlope >= 89 ||
                !Finite(transitionMeters) || transitionMeters <= 0) throw new ArgumentException("Invalid smoothing or height settings.");
            if (seaLevelMeters + globalMaxHeightMeters > terrainHeightMeters)
                throw new ArgumentException("Высота Terrain должна вмещать уровень моря и максимальную высоту глобальной карты.");
            for (int i = 0; i < 3; i++)
            {
                var l = levels[i];
                if (l == null || l.Resolution.x < 33 || l.Resolution.y < 33 || l.Resolution.x > 4097 || l.Resolution.y > 4097)
                    throw new ArgumentException("Texture dimensions must be between 33 and 4097.");
                if (!Finite(l.Size.x) || !Finite(l.Size.y) || l.Size.x <= 0 || l.Size.y <= 0)
                    throw new ArgumentException("Terrain dimensions must be positive.");
                if (i > 0 && (l.Size.x <= levels[i - 1].Size.x || l.Size.y <= levels[i - 1].Size.y))
                    throw new ArgumentException("Each next terrain must be larger along both axes.");
            }
        }
        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }

}
