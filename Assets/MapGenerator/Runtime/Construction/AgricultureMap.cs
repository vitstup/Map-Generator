using System.Collections.Generic;
using UnityEngine;
using TacticalMaps.Generation;

namespace TacticalMaps.Construction
{
    // Resources belong to MapBuilder's transaction and are persisted by its existing preview saver.
    internal sealed class AgricultureMap : MonoBehaviour
    {
        [SerializeField] private Texture2D[] masks = new Texture2D[3];
        [SerializeField] private List<Mesh> meshes = new List<Mesh>();
        [SerializeField] internal Texture2D pages, profiles;
        [SerializeField] internal Texture2DArray distances;
        [SerializeField] internal FarmField[] fields;
        [SerializeField] internal Farmstead[] farms;
        [SerializeField] internal FarmTrack[] tracks;
        internal Texture2D[] Masks => masks;
        internal void AddMesh(Mesh mesh) => meshes.Add(mesh);
        internal IEnumerable<Object> Resources { get { foreach (var t in masks) yield return t; foreach (var m in meshes) yield return m; yield return pages; yield return profiles; yield return distances; } }
        internal void BuildSurface(AgricultureSurfaceData surface, Terrain[] terrains, Vector3 center)
        {
            pages = new Texture2D(Mathf.Max(1, surface.Columns), Mathf.Max(1, surface.Rows), TextureFormat.RFloat, false, true) { name = "FarmDistancePages", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var lookup = new float[pages.width * pages.height]; for (int i = 0; i < surface.Keys.Length; i++) lookup[surface.Keys[i]] = i + 1;
            pages.SetPixelData(lookup, 0); pages.Apply(false, false);
            distances = new Texture2DArray(130, 130, Mathf.Max(1, surface.Tiles.Length), TextureFormat.RGBA32, false, true) { name = "FarmDistanceTiles", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            if (surface.Tiles.Length == 0) distances.SetPixelData(new Color32[130 * 130], 0, 0);
            for (int i = 0; i < surface.Tiles.Length; i++) distances.SetPixelData(surface.Tiles[i], 0, i);
            distances.Apply(false, false);
            profiles = new Texture2D(2048, Mathf.Max(1, (surface.Primitives.Length + 2047) / 2048) * 3, TextureFormat.RGBAFloat, false, true) { name = "FarmProfiles", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var parameters = new Vector4[profiles.width * profiles.height];
            for (int i = 0; i < surface.Primitives.Length; i++) { int at = i % 2048 + i / 2048 * 2048 * 3; parameters[at] = surface.Primitives[i].Shape; parameters[at + 2048] = surface.Primitives[i].Style; parameters[at + 4096] = surface.Primitives[i].Detail; }
            profiles.SetPixelData(parameters, 0); profiles.Apply(false, false);
            foreach (var terrain in terrains)
            {
                var material = terrain.materialTemplate; material.SetTexture("_FarmPages", pages); material.SetTexture("_FarmDistances", distances); material.SetTexture("_FarmProfiles", profiles);
                material.SetVector("_FarmDetailGrid", new Vector4(surface.Origin.x, surface.Origin.y, surface.Columns, surface.Rows));
                material.SetVector("_FarmCenter", new Vector4(center.x, center.z, 0, 0)); material.SetFloat("_FarmVersion", 3);
            }
        }
    }
}
