using System;
using System.Collections.Generic;
using System.Threading;
using TacticalMaps.Generation;
using TacticalMaps.Grass;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Editor
{
    internal static class GrassCardBaker
    {
        private const int Tile = 256, Width = Tile * 4, Height = Tile * 2;

        [MenuItem("Tools/Tactical Maps/Bake Distant Grass Groups")]
        internal static void Bake()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before baking.");
            var library=GrassSetup.EnsureLibrary();
            var scene=EditorSceneManager.NewPreviewScene();
            RenderTexture target=null; Texture2D image=null; MapData map=null;
            try
            {
                var go=new GameObject("GrassGroupBakeSystem") {layer=30};
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
                var system=go.AddComponent<GrassSystem>();
                var settings=new SerializedObject(system);
                settings.FindProperty("library").objectReferenceValue=library;
                settings.FindProperty("density").floatValue=1;
                settings.FindProperty("windStrength").floatValue=0;
                settings.FindProperty("chunksPreparedPerFrame").intValue=16;
                settings.ApplyModifiedPropertiesWithoutUndo();
                system.MatureForChecks=true;
                system.GeometryMaskForChecks=1;
                var root=new GameObject("GrassGroupBakeGround");root.SetActive(false);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
                var weights=new float[3][,,];var sizes=new Vector2[3];
                var names=new[]{"Terrain_Main","Terrain_Surroundings","Terrain_Horizon"};
                for(int i=0;i<3;i++)
                {
                    var ground=new GameObject(names[i]) {layer=31};ground.transform.SetParent(root.transform);
                    var data=new TerrainData {heightmapResolution=33,size=new Vector3(128*Mathf.Pow(4,i),10,128*Mathf.Pow(4,i))};
                    var terrain=ground.AddComponent<Terrain>();terrain.terrainData=data;
                    ground.transform.position=new Vector3(-data.size.x*.5f,2,-data.size.z*.5f);
                    var heights=new float[33,33];for(int z=0;z<33;z++)for(int x=0;x<33;x++)heights[z,x]=.5f;
                    data.SetHeights(0,0,heights);terrain.Flush();
                    weights[i]=new float[16,16,6];for(int z=0;z<16;z++)for(int x=0;x<16;x++)weights[i][z,x,0]=1;
                    sizes[i]=new Vector2(data.size.x,data.size.z);
                }
                var surface=new SurfaceData(new TerrainLayer[6],weights,Vector2.zero);
                map=new MapData(new Texture2D[3],sizes,10,0,4000,Vector2Int.zero,grass:GrassCoverage.Generate(surface,CancellationToken.None));
                system.Stage(map,root.transform);root.SetActive(true);
                var cameraObject=new GameObject("GrassGroupBakeCamera");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);
                var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;
                camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=20;
                camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
                target=new RenderTexture(Tile,Tile,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
                image=new Texture2D(Tile,Tile,TextureFormat.RGBA32,false,true);
                var albedo=new Color32[Width*Height];var normals=new Color32[Width*Height];var flowers=new Color32[Width*Height];
                for(int row=0;row<2;row++)for(int variant=0;variant<4;variant++)
                {
                    var center=new Vector3(variant*8,7,0);
                    system.BakeRegion=new Vector4(center.x,center.z,1.6f,1);
                    if(row==0)
                    {
                        camera.orthographicSize=.55f;camera.aspect=3.2f/1.1f;
                        camera.transform.SetPositionAndRotation(center+new Vector3(0,.525f,-8),Quaternion.identity);
                    }
                    else
                    {
                        camera.orthographicSize=1.6f;camera.aspect=1;
                        camera.transform.SetPositionAndRotation(center+Vector3.up*8,Quaternion.Euler(90,0,0));
                    }
                    system.BakeMode=1;
                    for(int frame=0;frame<36;frame++) {camera.Render();AsyncGPUReadback.Request(target,0).WaitForCompletion();}
                    Read(albedo,variant,row);
                    system.BakeMode=2;Read(normals,variant,row);
                    system.BakeMode=3;Read(flowers,variant,row);
                }
                Dilate(albedo,normals);
                var a=Save("CardsAlbedo",albedo,false);var n=Save("CardsNormal",normals,true);
                var mask=SaveMask("CardsFlowers",flowers);
                var mesh=EnsureMesh();
                var shader=Shader.Find("TacticalMaps/Grass/DistantCards");
                if(shader==null) throw new InvalidOperationException("Distant grass shader was not imported.");
                string materialPath=GrassSetup.Folder+"/GrassCards.mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if(material==null) {material=new Material(shader) {enableInstancing=true};AssetDatabase.CreateAsset(material,materialPath);}
                var lib=new SerializedObject(library);
                lib.FindProperty("cardsMesh").objectReferenceValue=mesh;
                lib.FindProperty("cardsMaterial").objectReferenceValue=material;
                lib.FindProperty("cardsAlbedo").objectReferenceValue=a;
                lib.FindProperty("cardsNormal").objectReferenceValue=n;
                lib.FindProperty("cardsFlowers").objectReferenceValue=mask;
                lib.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();
                foreach(var live in UnityEngine.Object.FindObjectsByType<GrassSystem>(FindObjectsSortMode.None))
                    if(live!=system&&live.CurrentMap!=null) live.Attach(live.CurrentMap);
                System.IO.File.WriteAllBytes("Temp/grass-cards-atlas.png",a.EncodeToPNG());
                System.IO.File.WriteAllBytes("Temp/grass-cards-flowers.png",mask.EncodeToPNG());
                Debug.Log("Baked four real grass groups from side and top; ten triangles per distant group.");

                void Read(Color32[] destination,int column,int row)
                {
                    camera.Render();var previous=RenderTexture.active;
                    try {RenderTexture.active=target;image.ReadPixels(new Rect(0,0,Tile,Tile),0,0);image.Apply();}
                    finally {RenderTexture.active=previous;}
                    var pixels=image.GetPixels32();
                    int covered=0;foreach(var pixel in pixels) if(pixel.a>0) covered++;
                    if(covered<pixels.Length*.08f) throw new InvalidOperationException("Grass group bake is empty or too sparse: "+column+", "+row);
                    for(int y=0;y<Tile;y++)Array.Copy(pixels,y*Tile,destination,(row*Tile+y)*Width+column*Tile,Tile);
                }
            }
            finally
            {
                map?.Release();if(image!=null) UnityEngine.Object.DestroyImmediate(image);
                if(target!=null) {target.Release();UnityEngine.Object.DestroyImmediate(target);}
                foreach(var root in scene.GetRootGameObjects())foreach(var terrain in root.GetComponentsInChildren<Terrain>(true))
                    if(terrain.terrainData!=null) UnityEngine.Object.DestroyImmediate(terrain.terrainData);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        private static Texture2D SaveMask(string name,Color32[] pixels)
        {
            string path=GrassSetup.Folder+"/"+name+".asset";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);bool create=texture==null;
            if(create) texture=new Texture2D(Width,Height,TextureFormat.R8,true,true);
            texture.name=name;texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Trilinear;texture.anisoLevel=8;
            texture.SetPixels32(pixels);texture.Apply(true,false);
            if(create)AssetDatabase.CreateAsset(texture,path);else EditorUtility.SetDirty(texture);
            return texture;
        }
        private static Mesh EnsureMesh()
        {
            string path=GrassSetup.Folder+"/GrassCards.asset";
            var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old!=null) return old;
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var meta=new List<Vector2>();var indices=new List<int>();
            for(int card=0;card<5;card++)
            {
                int start=vertices.Count;
                foreach(var p in new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up})
                {vertices.Add(new Vector3(p.x,p.y,0));uv.Add(p);meta.Add(new Vector2(card,card<3?0:1));}
                indices.AddRange(new[]{start,start+2,start+1,start,start+3,start+2});
            }
            var mesh=new Mesh {name="DistantMeadowGroups"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetUVs(1,meta);mesh.SetTriangles(indices,0);
            mesh.bounds=new Bounds(Vector3.up*.5f,new Vector3(7,3,7));AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        private static Texture2D Save(string name,Color32[] pixels,bool normal)
        {
            string path=GrassSetup.Folder+"/"+name+".asset";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            bool create=texture==null;
            if(create) texture=new Texture2D(Width,Height,TextureFormat.RGBA32,true,true);
            texture.name=name;texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Trilinear;texture.anisoLevel=8;
            texture.SetPixels32(pixels);
            // Straight-alpha box filtering mixes empty black texels into distant blades.
            // Filter covered samples only, and renormalize normals at every mip level.
            int width=Width,height=Height;
            for(int mip=1;mip<texture.mipmapCount;mip++)
            {
                int nextWidth=Mathf.Max(1,width/2),nextHeight=Mathf.Max(1,height/2);
                var next=new Color32[nextWidth*nextHeight];
                for(int y=0;y<nextHeight;y++)for(int x=0;x<nextWidth;x++)
                {
                    Vector3 rgb=Vector3.zero;float alpha=0;
                    for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++)
                    {
                        var c=pixels[Mathf.Min(height-1,y*2+dy)*width+Mathf.Min(width-1,x*2+dx)];
                        float a=c.a/255f;rgb+=new Vector3(c.r,c.g,c.b)*(a/255f);alpha+=a;
                    }
                    if(alpha>0)rgb/=alpha;
                    if(normal)
                    {
                        var n=alpha>0?(rgb*2-Vector3.one).normalized:Vector3.forward;
                        rgb=n*.5f+Vector3.one*.5f;
                    }
                    next[y*nextWidth+x]=new Color(rgb.x,rgb.y,rgb.z,alpha*.25f);
                }
                texture.SetPixels32(next,mip);pixels=next;width=nextWidth;height=nextHeight;
            }
            texture.Apply(false,false);
            if(create) AssetDatabase.CreateAsset(texture,path);else EditorUtility.SetDirty(texture);
            return texture;
        }
        private static void Dilate(Color32[] albedo,Color32[] normals)
        {
            // Extend RGB and normals into transparent texels without changing the cutout silhouette.
            var filled=new bool[albedo.Length];for(int i=0;i<filled.Length;i++)filled[i]=albedo[i].a>0;
            for(int pass=0;pass<6;pass++)
            {
                var next=(bool[])filled.Clone();
                for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
                {
                    int i=y*Width+x;if(filled[i])continue;
                    foreach(var d in new[]{Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down})
                    {
                        int xx=x+d.x,yy=y+d.y;
                        if(xx<0||xx>=Width||yy<0||yy>=Height||xx/Tile!=x/Tile||yy/Tile!=y/Tile)continue;
                        int j=yy*Width+xx;if(!filled[j])continue;
                        var a=albedo[j];a.a=albedo[i].a;albedo[i]=a;
                        var n=normals[j];n.a=normals[i].a;normals[i]=n;next[i]=true;break;
                    }
                }
                filled=next;
            }
        }
    }
}
