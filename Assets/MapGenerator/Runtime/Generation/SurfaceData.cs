using System;
using System.Collections.Generic;
using UnityEngine;

namespace TacticalMaps.Generation
{
    internal sealed class SurfaceData
    {
        internal IReadOnlyList<TerrainLayer> Layers { get; private set; }
        internal IReadOnlyList<float[,,]> Weights { get; private set; }
        internal Vector2 TextureOrigin { get; private set; }

        internal SurfaceData(TerrainLayer[] layers, float[][,,] weights, Vector2 textureOrigin)
        {
            Layers = Array.AsReadOnly((TerrainLayer[])layers.Clone());
            Weights = Array.AsReadOnly(weights);
            TextureOrigin = textureOrigin;
        }
    }
}
