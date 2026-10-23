using UnityEngine;

namespace TacticalMaps.Grass
{
    internal sealed class GrassLibrary : ScriptableObject
    {
        [SerializeField] private Mesh nearMesh;
        [SerializeField] private Mesh middleMesh;
        [SerializeField] private Material material;
        [SerializeField] private ComputeShader compute;
        [SerializeField] private Texture2D turfAlbedo;
        [SerializeField] private Texture2D turfNormal;
        internal Mesh Mesh(int lod) => lod == 0 ? nearMesh : middleMesh;
        internal Material Material => material;
        internal ComputeShader Compute => compute;
        internal Texture2D TurfAlbedo => turfAlbedo;
        internal Texture2D TurfNormal => turfNormal;
        internal bool Valid => nearMesh != null && middleMesh != null && material != null && compute != null;
    }
}
