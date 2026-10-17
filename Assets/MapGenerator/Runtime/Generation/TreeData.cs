using System;
using System.Collections.Generic;
using UnityEngine;

namespace TacticalMaps.Generation
{
    internal sealed class TreeData
    {
        internal IReadOnlyList<GameObject> Prefabs { get; }
        internal IReadOnlyList<TreePlacement> Placements { get; }
        internal IReadOnlyList<TreeZone> Zones { get; }
        internal IReadOnlyList<bool> ForestMask => Zones[0].Mask;
        internal int MaskWidth => Zones[0].Width;
        internal int MaskHeight => Zones[0].Height;
        internal float Coverage => Zones[0].Coverage;

        internal TreeData(GameObject[] prefabs, TreePlacement[] placements, bool[] mask, int width, int height)
            : this(prefabs, placements, new[] { new TreeZone(mask, width, height), TreeZone.Empty, TreeZone.Empty }) { }

        internal TreeData(GameObject[] prefabs, TreePlacement[] placements, TreeZone[] zones)
        {
            Prefabs = Array.AsReadOnly((GameObject[])prefabs.Clone());
            Placements = Array.AsReadOnly((TreePlacement[])placements.Clone());
            Zones = Array.AsReadOnly((TreeZone[])zones.Clone());
        }
    }

    internal sealed class TreeZone
    {
        internal static readonly TreeZone Empty = new TreeZone(Array.Empty<bool>(), 0, 0);
        internal IReadOnlyList<bool> Mask { get; }
        internal int Width { get; }
        internal int Height { get; }
        internal float Coverage { get; }
        internal TreeZone(bool[] mask, int width, int height)
        {
            Mask = Array.AsReadOnly((bool[])mask.Clone()); Width = width; Height = height;
            int occupied = 0;
            foreach (bool cell in mask) if (cell) occupied++;
            Coverage = mask.Length == 0 ? 0 : (float)occupied / mask.Length;
        }
    }

    internal readonly struct TreePlacement
    {
        // Metres relative to the common map centre; rotation is in radians, as Terrain expects.
        internal readonly Vector3 Position;
        internal readonly int Prefab, Group, Level;
        internal readonly float Rotation, WidthScale, HeightScale;
        internal TreePlacement(Vector3 position, int prefab, int group, float rotation, float width, float height, int level = 0)
        { Position = position; Prefab = prefab; Group = group; Rotation = rotation; WidthScale = width; HeightScale = height; Level = level; }
    }
}
