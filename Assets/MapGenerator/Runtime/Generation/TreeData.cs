using System;
using System.Collections.Generic;
using UnityEngine;

namespace TacticalMaps.Generation
{
    internal sealed class TreeData
    {
        internal IReadOnlyList<GameObject> Prefabs { get; }
        internal IReadOnlyList<TreePlacement> Placements { get; }
        internal IReadOnlyList<bool> ForestMask { get; }
        internal int MaskWidth { get; }
        internal int MaskHeight { get; }
        internal float Coverage { get; }

        internal TreeData(GameObject[] prefabs, TreePlacement[] placements, bool[] mask, int width, int height)
        {
            Prefabs = Array.AsReadOnly((GameObject[])prefabs.Clone());
            Placements = Array.AsReadOnly((TreePlacement[])placements.Clone());
            ForestMask = Array.AsReadOnly((bool[])mask.Clone());
            MaskWidth = width; MaskHeight = height;
            int occupied = 0;
            foreach (bool cell in mask) if (cell) occupied++;
            Coverage = mask.Length == 0 ? 0 : (float)occupied / mask.Length;
        }
    }

    internal readonly struct TreePlacement
    {
        // Metres relative to the common map centre; rotation is in radians, as Terrain expects.
        internal readonly Vector3 Position;
        internal readonly int Prefab, Group;
        internal readonly float Rotation, WidthScale, HeightScale;
        internal TreePlacement(Vector3 position, int prefab, int group, float rotation, float width, float height)
        { Position = position; Prefab = prefab; Group = group; Rotation = rotation; WidthScale = width; HeightScale = height; }
    }
}
