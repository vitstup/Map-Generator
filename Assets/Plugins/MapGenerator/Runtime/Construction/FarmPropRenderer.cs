using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace TacticalMaps.Construction
{
    [ExecuteAlways] internal sealed class FarmPropRenderer:MonoBehaviour
    {
        [SerializeField] private AgricultureLibrary library;
        [SerializeField] internal FarmPropInstance[] placements;
        [SerializeField] private float drawDistance=180;
        private readonly Plane[] planes=new Plane[6];
        private sealed class Bucket{internal Mesh Mesh;internal Material[] Materials;internal Matrix4x4 Local;internal Matrix4x4[] Matrices=new Matrix4x4[511];internal Bounds Bounds;internal int Count;}
        private Bucket[] buckets;
        internal int LastVisible {get;private set;}
        internal int LastDraws {get;private set;}
        internal void Configure(AgricultureLibrary assets,FarmPropInstance[] instances,float distance){library=assets;placements=instances;drawDistance=distance;buckets=null;}
        private void OnEnable()=>RenderPipelineManager.beginCameraRendering+=Render;
        private void OnDisable(){RenderPipelineManager.beginCameraRendering-=Render;buckets=null;LastVisible=LastDraws=0;}
        private void Render(ScriptableRenderContext context,Camera camera)
        {
            if(camera==null||camera.cameraType==CameraType.Reflection||camera.cameraType==CameraType.Preview||(camera.cullingMask&(1<<gameObject.layer))==0||library==null||placements==null)return;
#if UNITY_EDITOR
            bool preview=UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(gameObject.scene),cameraPreview=camera.scene.IsValid()&&UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(camera.scene);
            if(preview?camera.scene!=gameObject.scene:cameraPreview)return;
#endif
            LastVisible=LastDraws=0;if(buckets==null)
            {
                buckets=new Bucket[library.props.Length*2];
                for(int i=0;i<library.props.Length;i++)for(int mode=0;mode<2;mode++){var prefab=library.props[i];var f=prefab.GetComponentInChildren<MeshFilter>();buckets[i*2+mode]=new Bucket{Mesh=f.sharedMesh,Materials=f.GetComponent<Renderer>().sharedMaterials,Local=prefab.transform.worldToLocalMatrix*f.transform.localToWorldMatrix};}
            }
            foreach(var bucket in buckets)bucket.Count=0;
            GeometryUtility.CalculateFrustumPlanes(camera,planes);var matrix=transform.localToWorldMatrix;float limit=drawDistance*drawDistance;
            foreach(var p in placements)
            {
                var position=matrix.MultiplyPoint3x4(p.position);float d=(position-camera.transform.position).sqrMagnitude;if(d>limit)continue;
                float radius=library.propRadii[p.variant]*p.scale;var bounds=new Bounds(position+Vector3.up*radius,new Vector3(radius*2,radius*3,radius*2));if(!GeometryUtility.TestPlanesAABB(planes,bounds))continue;
                int mode=d<45*45?0:1;var bucket=buckets[p.variant*2+mode];
                if(bucket.Count==511){Submit(bucket,mode);bucket.Count=0;}
                bucket.Matrices[bucket.Count]=matrix*Matrix4x4.TRS(p.position,Quaternion.Euler(0,p.angle,0),Vector3.one*p.scale)*bucket.Local;
                if(bucket.Count++==0)bucket.Bounds=bounds;else bucket.Bounds.Encapsulate(bounds);LastVisible++;
            }
            for(int i=0;i<buckets.Length;i++)Submit(buckets[i],i%2);
            void Submit(Bucket b,int mode)
            {
                if(b.Count==0)return;
                for(int sub=0;sub<b.Mesh.subMeshCount;sub++)
                {
                    var parameters=new RenderParams(b.Materials[sub]){camera=camera,worldBounds=b.Bounds,layer=gameObject.layer,receiveShadows=true,shadowCastingMode=mode==0?ShadowCastingMode.On:ShadowCastingMode.Off,lightProbeUsage=LightProbeUsage.Off,reflectionProbeUsage=ReflectionProbeUsage.Off,renderingLayerMask=uint.MaxValue};
                    Graphics.RenderMeshInstanced(parameters,b.Mesh,sub,b.Matrices,b.Count);LastDraws++;
                }
            }
        }
    }
}
