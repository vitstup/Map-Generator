using UnityEngine;

namespace TacticalMaps.Grass
{
    internal sealed class GrassLibrary : ScriptableObject
    {
        [SerializeField] private Mesh nearMesh;
        [SerializeField] private Mesh middleMesh;
        [SerializeField] private Mesh simplifiedMesh;
        [SerializeField] private Material material;
        [SerializeField] private ComputeShader compute;
        [SerializeField] private Texture2D turfAlbedo;
        [SerializeField] private Texture2D turfNormal;
        [SerializeField] private Mesh cardsMesh;
        [SerializeField] private Material cardsMaterial;
        [SerializeField] private Texture2D cardsAlbedo;
        [SerializeField] private Texture2D cardsNormal;
        internal Mesh CardsMesh => cardsMesh;
        internal Material CardsMaterial => cardsMaterial;
        internal Texture2D CardsAlbedo => cardsAlbedo;
        internal Texture2D CardsNormal => cardsNormal;
        internal bool CardsValid => cardsMesh != null && cardsMaterial != null && cardsAlbedo != null && cardsNormal != null;
        internal Mesh Mesh(int lod) => lod == 0 ? nearMesh : middleMesh;
        internal Mesh Simplified => simplifiedMesh != null ? simplifiedMesh : nearMesh;
        internal Material Material => material;
        internal ComputeShader Compute => compute;
        internal Texture2D TurfAlbedo => turfAlbedo;
        internal Texture2D TurfNormal => turfNormal;
        internal bool Valid => nearMesh != null && middleMesh != null && simplifiedMesh != null && material != null && compute != null;
    }
}
