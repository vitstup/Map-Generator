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
        [SerializeField] internal FarmField[] fields;
        [SerializeField] internal Farmstead[] farms;
        [SerializeField] internal FarmTrack[] tracks;
        internal Texture2D[] Masks => masks;
        internal void AddMesh(Mesh mesh) => meshes.Add(mesh);
        internal IEnumerable<Object> Resources { get { foreach (var t in masks) yield return t; foreach (var m in meshes) yield return m; } }
    }
}
