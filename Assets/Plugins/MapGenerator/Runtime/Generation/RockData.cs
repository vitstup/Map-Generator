using System;
using System.Collections.Generic;
using UnityEngine;

namespace TacticalMaps.Generation
{
    internal sealed class RockData
    {
        internal IReadOnlyList<GameObject> Prefabs { get; private set; }
        internal IReadOnlyList<RockPlacement> Placements { get; private set; }
        internal RockData(GameObject[] prefabs, RockPlacement[] placements)
        { Prefabs = Array.AsReadOnly((GameObject[])prefabs.Clone()); Placements = Array.AsReadOnly(placements); }
    }

    internal readonly struct RockPlacement
    {
        internal readonly int Level, Prefab;
        internal readonly Vector3 Position, Scale;
        internal readonly Quaternion Rotation;
        internal RockPlacement(int level, int prefab, Vector3 position, Quaternion rotation, Vector3 scale)
        { Level = level; Prefab = prefab; Position = position; Rotation = rotation; Scale = scale; }
    }
}
