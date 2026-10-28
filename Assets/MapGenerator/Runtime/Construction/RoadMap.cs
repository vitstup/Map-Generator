using TacticalMaps.Generation;
using UnityEngine;

namespace TacticalMaps.Construction
{
    // Resources belong to the successful construction, independently of MapData.Release().
    // The workbench renders one active tactical map, just as GrassSystem does.
    [ExecuteAlways]
    internal sealed class RoadMap : MonoBehaviour
    {
        [SerializeField] private Texture2D pages;
        [SerializeField] private Texture2DArray distances;
        [SerializeField] private Texture2DArray extraDistances;
        [SerializeField] private Texture2D farCoverage;
        [SerializeField] private Vector4 grid, options;
        [SerializeField] private TerrainLayer largeSurface,mediumSurface;
        [SerializeField] private Color largeTint,mediumTint;
        private static RoadMap active;
        internal Texture2D Pages=>pages;
        internal Texture2DArray Distances=>distances;
        internal Texture2DArray ExtraDistances=>extraDistances;
        internal Texture2D FarCoverage=>farCoverage;
        internal void Configure(RoadData data,Vector3 center,float sea,TerrainLayer large,TerrainLayer medium,Color bigTint,Color smallTint)
        {
            largeSurface=large;mediumSurface=medium;largeTint=bigTint;mediumTint=smallTint;
            grid=new Vector4(data.Origin.x+center.x,data.Origin.y+center.z,data.Columns,data.Rows);
            options=new Vector4(data.Shoulder,sea+center.y,.2f,0);
            pages=new Texture2D(data.Columns,data.Rows,TextureFormat.RGBA32,false,true){name="RoadTileLookup",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            int bank=Mathf.Min(2048,SystemInfo.maxTextureArraySlices);
            distances=new Texture2DArray(RoadData.TilePixels,RoadData.TilePixels,Mathf.Min(bank,data.Tiles.Count),TextureFormat.RGBA32,false,true){name="RoadSignedDistances",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            if(data.Tiles.Count>bank)extraDistances=new Texture2DArray(RoadData.TilePixels,RoadData.TilePixels,data.Tiles.Count-bank,TextureFormat.RGBA32,false,true){name="RoadSignedDistancesExtra",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            var lookup=new Color32[data.Columns*data.Rows];
            for(int i=0;i<data.Tiles.Count;i++){int code=i+1;lookup[data.TileKeys[i]]=new Color32((byte)(code&255),(byte)(code>>8),0,255);if(i<bank)distances.SetPixels32(data.Tiles[i],i);else extraDistances.SetPixels32(data.Tiles[i],i-bank);}
            pages.SetPixels32(lookup);pages.Apply(false,false);distances.Apply(false,false);if(extraDistances!=null)extraDistances.Apply(false,false);
            farCoverage=new Texture2D(data.FarWidth,data.FarHeight,TextureFormat.RGBA32,true,true){name="RoadFarCoverage",filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp};
            farCoverage.SetPixels32(data.FarCoverage);farCoverage.Apply(true,false);
            if(isActiveAndEnabled)Publish();
        }
        private void OnEnable(){if(pages!=null&&distances!=null)Publish();}
        private void Publish()
        {
            options.w=distances.depth;Shader.SetGlobalTexture("_RoadDistancesExtra",extraDistances!=null?extraDistances:distances);
            active=this;Shader.SetGlobalFloat("_RoadEnabled",1);Shader.SetGlobalTexture("_RoadPages",pages);Shader.SetGlobalTexture("_RoadDistances",distances);
            Shader.SetGlobalVector("_RoadGrid",grid);Shader.SetGlobalVector("_RoadOptions",options);
            Shader.SetGlobalTexture("_RoadFarCoverage",farCoverage!=null?farCoverage:Texture2D.blackTexture);
            Shader.SetGlobalTexture("_RoadLargeAlbedo",largeSurface!=null&&largeSurface.diffuseTexture!=null?largeSurface.diffuseTexture:Texture2D.whiteTexture);
            Shader.SetGlobalTexture("_RoadMediumAlbedo",mediumSurface!=null&&mediumSurface.diffuseTexture!=null?mediumSurface.diffuseTexture:Texture2D.whiteTexture);
            Shader.SetGlobalColor("_RoadLargeTint",largeTint);Shader.SetGlobalColor("_RoadMediumTint",mediumTint);
            Shader.SetGlobalTexture("_RoadLargeNormal",largeSurface!=null&&largeSurface.normalMapTexture!=null?largeSurface.normalMapTexture:Texture2D.normalTexture);
            Shader.SetGlobalTexture("_RoadMediumNormal",mediumSurface!=null&&mediumSurface.normalMapTexture!=null?mediumSurface.normalMapTexture:Texture2D.normalTexture);
            Shader.SetGlobalVector("_RoadNormalScale",new Vector4(largeSurface!=null?largeSurface.normalScale:1,mediumSurface!=null?mediumSurface.normalScale:1,0,0));
            Vector2 a=largeSurface!=null?largeSurface.tileSize:new Vector2(4,4),b=mediumSurface!=null?mediumSurface.tileSize:new Vector2(3,3);
            Shader.SetGlobalVector("_RoadTiling",new Vector4(Mathf.Max(.1f,a.x),Mathf.Max(.1f,a.y),Mathf.Max(.1f,b.x),Mathf.Max(.1f,b.y)));
        }
        private void OnDisable()
        {
            if(active!=this)return;active=null;Shader.SetGlobalFloat("_RoadEnabled",0);
            foreach(var map in FindObjectsByType<RoadMap>(FindObjectsSortMode.None))
                if(map!=this&&map.isActiveAndEnabled&&map.pages!=null&&map.distances!=null){map.Publish();break;}
        }
        private void OnDestroy(){Dispose(pages);Dispose(distances);Dispose(extraDistances);Dispose(farCoverage);}
        private static void Dispose(Object value)
        {
            if(value==null)return;
#if UNITY_EDITOR
            if(UnityEditor.EditorUtility.IsPersistent(value))return;
#endif
            if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);
        }
    }
}
