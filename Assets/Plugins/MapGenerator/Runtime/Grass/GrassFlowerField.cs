using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Grass
{
    // A bounded 20 MiB cache per active view, at most two views. No readbacks, mip generation, or per-frame allocation.
    internal sealed class GrassFlowerField : IDisposable
    {
        internal const int Resolution=1024, Levels=5;
        private readonly ComputeShader compute;
        private readonly RenderTexture texture;
        private readonly Vector4[] rects=new Vector4[Levels];
        private readonly bool[] valid=new bool[Levels];
        private int signature=int.MinValue;
        internal int Builds {get;private set;}
        internal float LastUsed {get;private set;}
        internal GrassFlowerField(ComputeShader source)
        {
            compute=UnityEngine.Object.Instantiate(source);
            texture=new RenderTexture(Resolution,Resolution,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear)
            {name="Flower site cache (20 MiB)",dimension=TextureDimension.Tex2DArray,volumeDepth=Levels,
                enableRandomWrite=true,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            texture.Create();
        }
        internal void Prepare(Camera camera,Vector4 flowers,Vector4[] colors,int count,int seed)
        {
            LastUsed=Time.realtimeSinceStartup;
            int hash=HashCode.Combine(flowers.x,flowers.y,count,seed);
            for(int i=0;i<count;i++)hash=HashCode.Combine(hash,colors[i].w);
            if(hash!=signature){Array.Clear(valid,0,valid.Length);signature=hash;}
            compute.SetVector("_GrassFlowers",flowers);compute.SetVectorArray("_GrassFlowerColors",colors);
            compute.SetInt("_GrassFlowerCount",count);compute.SetFloat("_FlowerSeed",seed);
            compute.SetTexture(0,"_FlowerOutput",texture);
            // Bias the window towards visible ground, keeping a generous margin behind the camera.
            var direction=camera.transform.forward;var center=camera.transform.position+new Vector3(direction.x,0,direction.z)*220;
            for(int i=0;i<Levels;i++)
            {
                float spacing=1<<i,step=128*spacing;
                var rect=new Vector4(Mathf.Floor(center.x/step)*step-Resolution*.5f*spacing,
                    Mathf.Floor(center.z/step)*step-Resolution*.5f*spacing,spacing,Resolution*spacing);
                if(valid[i]&&rects[i]==rect)continue;
                var previous=rects[i];
                int dx=Mathf.RoundToInt((rect.x-previous.x)/spacing),dz=Mathf.RoundToInt((rect.y-previous.y)/spacing);
                if(!valid[i]||Mathf.Abs(dx)>=Resolution||Mathf.Abs(dz)>=Resolution)
                    Build(i,rect,0,0,Resolution,Resolution);
                else
                {
                    // Toroidal addressing retains every overlapping texel. Generate only
                    // the strips exposed by camera movement, never a full moving window.
                    if(dx!=0)Build(i,rect,dx>0?Resolution-dx:0,0,Mathf.Abs(dx),Resolution);
                    if(dz!=0)Build(i,rect,dx<0?-dx:0,dz>0?Resolution-dz:0,Resolution-Mathf.Abs(dx),Mathf.Abs(dz));
                }
                rects[i]=rect;valid[i]=true;
            }
            Shader.SetGlobalTexture("_GrassFlowerField",texture);
            Shader.SetGlobalVectorArray("_GrassFlowerFieldRects",rects);
            Shader.SetGlobalFloat("_GrassFlowerFieldReady",1);
        }
        private void Build(int slice,Vector4 rect,int x,int z,int width,int height)
        {
            // Limit each dispatch even for a cold cache or a teleport.
            for(int row=0;row<height;row+=64)
            {
                int rows=Mathf.Min(64,height-row);
                compute.SetVector("_FlowerBuildRect",new Vector4(rect.x+x*rect.z,rect.y+(z+row)*rect.z,rect.z,0));
                compute.SetInts("_FlowerBuildSize",width,rows);compute.SetInt("_FlowerSlice",slice);
                compute.Dispatch(0,(width+7)/8,(rows+7)/8,1);
            }
            Builds++;
        }
        public void Dispose()
        {
            Shader.SetGlobalFloat("_GrassFlowerFieldReady",0);
            texture.Release();GrassMap.Dispose(texture);GrassMap.Dispose(compute);
        }
    }
}
