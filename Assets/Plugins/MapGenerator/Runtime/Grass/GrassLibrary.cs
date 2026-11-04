using System.Collections.Generic;
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
        [SerializeField] private ComputeShader flowerField;
        internal ComputeShader FlowerField => flowerField;
        [SerializeField] private Texture2D turfAlbedo;
        [SerializeField] private Texture2D turfNormal;
        [SerializeField] private Mesh cardsMesh;
        [SerializeField] private Material cardsMaterial;
        [SerializeField] private Texture2D cardsAlbedo;
        [SerializeField] private Texture2D cardsNormal;
        [SerializeField] private Texture2D cardsFlowers;
        [SerializeField] private Texture2D turfFlowers;
        internal Texture2D CardsFlowers => cardsFlowers;
        internal Texture2D TurfFlowers => turfFlowers;
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

        internal static Mesh CreateReducedCards(Mesh source)
        {
            var mesh=Instantiate(source);mesh.name=source.name+" eight triangles";mesh.hideFlags=HideFlags.HideAndDontSave;
            var sourceIndices=source.triangles;var meta=source.uv2;var indices=new List<int>();
            for(int i=0;i<sourceIndices.Length;i+=3)
                if(Mathf.RoundToInt(meta[sourceIndices[i]].x)!=1)
                    indices.AddRange(new[]{sourceIndices[i],sourceIndices[i+1],sourceIndices[i+2]});
            mesh.SetTriangles(indices,0);mesh.bounds=source.bounds;return mesh;
        }
        // Compact templates owned by a view; authoring meshes and reference bakes stay intact.
        internal static Mesh CreatePartMesh(Mesh source, bool heads, bool sparse = false, bool meadow = false)
        {
            var positions=source.vertices;var uv=source.uv;var meta=source.uv2;var indices=source.triangles;
            var remap=new Dictionary<int,int>();var vertices=new List<Vector3>();
            var coords=new List<Vector2>();var parts=new List<Vector2>();var coefficients=new List<Vector2>();var triangles=new List<int>();
            for(int i=0;i<indices.Length;i+=3)
            {
                if((meta[indices[i]].y>.5f)!=heads)continue;
                int blade=Mathf.RoundToInt(meta[indices[i]].x);
                if(sparse && (meadow ? (blade>=3&&blade<=13&&blade%2==1)||(blade>=19&&blade<=29&&blade%2==1) : blade%2==1))continue;
                for(int j=0;j<3;j++)
                {
                    int old=indices[i+j];
                    if(!remap.TryGetValue(old,out int index))
                    {
                        index=vertices.Count;remap.Add(old,index);vertices.Add(positions[old]);coords.Add(uv[old]);parts.Add(meta[old]);
                        coefficients.Add(new Vector2(Mathf.Pow(Mathf.Clamp01(1-uv[old].y),.55f),uv[old].y*uv[old].y));
                    }
                    triangles.Add(index);
                }
            }
            var mesh=new Mesh {name=source.name+(heads?" heads":sparse?" sparse blades":" blades"),hideFlags=HideFlags.HideAndDontSave};
            mesh.SetVertices(vertices);mesh.SetUVs(0,coords);mesh.SetUVs(1,parts);mesh.SetUVs(2,coefficients);mesh.SetTriangles(triangles,0);
            mesh.bounds=source.bounds;mesh.UploadMeshData(false);return mesh;
        }
    }
}
