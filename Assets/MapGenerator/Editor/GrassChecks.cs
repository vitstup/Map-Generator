using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using TacticalMaps.Grass;
using TacticalMaps.Generation;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class GrassChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify And Capture Grass")]
        internal static void Verify()
        {
            var report = new StringBuilder();
            GameObject cameraObject = null; RenderTexture target = null; Texture2D pixels = null;
            var system = UnityEngine.Object.FindAnyObjectByType<GrassSystem>();
            bool wasEnabled = system != null && system.enabled;
            try
            {
                Require(system != null && system.CurrentMap != null, "Configure grass first.");
                system.MatureForChecks=true;
                Require(system.Library.Valid, "Resource library is complete.");
                Require(system.Library.TurfAlbedo!=null&&system.Library.TurfNormal!=null,"Baked far meadow textures exist.");
                foreach (var name in new[] { "TacticalMaps/Grass/LowPolyIndirect", "TacticalMaps/Terrain/MatteStochastic" })
                {
                    foreach (var message in ShaderUtil.GetShaderMessages(Shader.Find(name)))
                        Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, name + ": " + message.message);
                }
                CoverageChecks(report);
                LifecycleChecks(system,report);
                var map = system.CurrentMap; var terrain = map.Terrains[0]; var data = terrain.terrainData;
                var mask = map.Coverage[0]; var colors = mask.GetPixels32(); Vector2 uv = new Vector2(.5f,.5f); float best = -1;
                for (int z = 10; z < mask.height - 10; z += 3) for (int x = 10; x < mask.width - 10; x += 3)
                {
                    float u = (x + .5f) / mask.width, v = (z + .5f) / mask.height;
                    float h = terrain.transform.position.y + data.GetInterpolatedHeight(u,v);
                    float score = colors[z * mask.width + x].r / 255f - Vector2.Distance(new Vector2(u,v),new Vector2(.5f,.5f))*.2f;
                    if (h > map.SeaLevel + 3 && data.GetInterpolatedNormal(u,v).y > .98f && score > best) { best = score; uv = new Vector2(u,v); }
                }
                Require(best > .1f, "Found suitable meadow.");
                var ground = terrain.transform.position + new Vector3(uv.x * data.size.x,data.GetInterpolatedHeight(uv.x,uv.y),uv.y * data.size.z);
                report.AppendLine("Meadow position: " + ground.ToString("F2"));
                cameraObject = new GameObject("GrassVerificationCamera") { hideFlags = HideFlags.HideAndDontSave };
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 55; camera.nearClipPlane = .15f; camera.farClipPlane = 23000;
                target = new RenderTexture(1280,720,24); target.Create(); camera.targetTexture = target;
                pixels = new Texture2D(1280,720,TextureFormat.RGB24,false);
                System.IO.Directory.CreateDirectory("Temp");
                foreach (var view in new[] { "close", "rts", "top", "wide" })
                {
                    Vector3 offset = view == "close" ? new Vector3(0,2.2f,-6) : view == "rts" ? new Vector3(0,35,-55) : view=="top"?new Vector3(0,45,0):new Vector3(0,260,-380);
                    camera.transform.position = ground + offset; camera.transform.LookAt(ground + (view == "close" ? Vector3.forward * 5 : Vector3.zero));
                    for (int i = 0; i < 45; i++) camera.Render();
                    var counts = system.ReadCountsForChecks(camera);
                    if (view == "close") Require(counts[0] > 0, "Near GPU draw contains visible grass.");
                    if (view == "rts") Require(counts[1] > 0, "Middle GPU draw contains visible grass.");
                    report.AppendLine($"{view}: near={counts[0]}, middle={counts[1]}, residentChunks={system.ResidentChunks}, GPU buffers={system.BufferBytes / 1048576f:F2} MiB, draws={system.SubmittedDraws}");
                    var full = Capture(camera,target,pixels,"Temp/grass-"+view+".png");
                    system.SuppressGeometryForChecks = true;
                    var surface = Capture(camera,target,pixels,"Temp/grass-"+view+"-surface.png");
                    system.SuppressGeometryForChecks = false;
                    double difference = Difference(full,surface);
                    if (view != "wide") Require(difference > .005, "Geometry changes rendered image: " + view);
                    report.AppendLine($"{view}: geometry image difference={difference:F4}");
                    system.enabled=false;
                    var bare=Capture(camera,target,pixels,"Temp/grass-"+view+"-bare.png");
                    system.enabled=true;
                    double turfDifference=Difference(surface,bare);
                    Require(turfDifference>.15,"Meadow surface visibly replaces bare Terrain: "+view);
                    report.AppendLine($"{view}: Terrain meadow image difference={turfDifference:F4}");
                    if (view == "rts")
                    {
                        double fullTime = Time(camera,8); system.SuppressGeometryForChecks = true; double surfaceTime = Time(camera,8);
                        system.enabled = false; double bareTime = Time(camera,8); system.enabled = true; system.SuppressGeometryForChecks = false;
                        report.AppendLine($"Offscreen render submission: full={fullTime:F2} ms, surface={surfaceTime:F2} ms, disabled={bareTime:F2} ms. Editor/pipeline overhead included; not isolated GPU time or gameplay FPS.");
                    }
                }
                camera.transform.position = ground + new Vector3(120,20,-20); camera.transform.LookAt(ground); camera.Render();
                Require(system.ResidentChunks <= new SerializedObject(system).FindProperty("maximumResidentChunks").intValue+160, "Both residency pools remain bounded.");
                system.enabled = false; Require(system.BufferBytes == 0, "Disable releases buffers."); system.enabled = true;
                report.AppendLine("PASS: coverage, cancellation, shader compilation, near/middle/wide rendering, camera movement, disable cleanup.");
            }
            catch (Exception e) { report.AppendLine("FAIL: " + e); UnityEngine.Debug.LogException(e); }
            finally
            {
                if (system != null) { system.SuppressGeometryForChecks = false; system.MatureForChecks=false;system.enabled = wasEnabled; }
                if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
                System.IO.File.WriteAllText("Temp/grass-checks.txt",report.ToString()); UnityEngine.Debug.Log(report.ToString());
            }
        }
        private static void CoverageChecks(StringBuilder report)
        {
            var weights = new float[3][,,];
            for (int i=0;i<3;i++) { weights[i]=new float[4,4,6]; for(int z=0;z<4;z++) for(int x=0;x<4;x++) weights[i][z,x,x<2?0:3]=1; }
            var surface = new SurfaceData(new TerrainLayer[6],weights,Vector2.zero);
            var a=GrassCoverage.Generate(surface,CancellationToken.None); var b=GrassCoverage.Generate(surface,CancellationToken.None);
            for(int i=0;i<3;i++) for(int p=0;p<16;p++) Require(a.Pixels[i][p].Equals(b.Pixels[i][p]),"Repeatability.");
            Require(a.Pixels[0][0].r==255 && a.Pixels[0][3].r==0,"Meadow allowed, rock excluded.");
            using(var cancel=new CancellationTokenSource()) { cancel.Cancel(); bool caught=false; try { GrassCoverage.Generate(surface,cancel.Token); } catch(OperationCanceledException) { caught=true; } Require(caught,"Cancellation."); }
            report.AppendLine("PASS: deterministic coverage; meadow/rock rules; cancellation.");
        }
        private static void LifecycleChecks(GrassSystem source,StringBuilder report)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene(); MapData map=null;
            try
            {
                var root=new GameObject("GrassBuildCheck"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
                var builder=root.AddComponent<TacticalMaps.Construction.MapBuilder>(); ConstructionSetup.Configure(builder);
                var grassObject=new GameObject("GrassSystemCheck"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(grassObject,scene);
                var grass=grassObject.AddComponent<GrassSystem>(); var gs=new SerializedObject(grass);
                gs.FindProperty("library").objectReferenceValue=source.Library; gs.ApplyModifiedPropertiesWithoutUndo();
                var bs=new SerializedObject(builder); bs.FindProperty("grassSystem").objectReferenceValue=grass; bs.ApplyModifiedPropertiesWithoutUndo();
                var textures=new Texture2D[3]; var sizes=new Vector2[3]; var weights=new float[3][,,];
                for(int i=0;i<3;i++)
                {
                    textures[i]=new Texture2D(33,33,TextureFormat.R16,false,true); var heights=new ushort[33*33];
                    for(int p=0;p<heights.Length;p++) heights[p]=19660;
                    textures[i].SetPixelData(heights,0); textures[i].Apply(); sizes[i]=Vector2.one*(128*Mathf.Pow(4,i));
                    weights[i]=new float[16,16,6]; for(int z=0;z<16;z++) for(int x=0;x<16;x++) weights[i][z,x,0]=1;
                }
                var surface=new SurfaceData(source.CurrentMap.Terrains[0].terrainData.terrainLayers,weights,Vector2.zero);
                map=new MapData(textures,sizes,100,5,4000,Vector2Int.zero,surface:surface,grass:GrassCoverage.Generate(surface,CancellationToken.None));
                builder.Build(map); var first=grass.CurrentMap; Require(first!=null,"Built grass attached.");
                builder.Build(map); Require(first==null && grass.CurrentMap!=null,"Rebuild replaces owned grass.");
                var stable=grass.CurrentMap;
                gs.Update(); gs.FindProperty("library").objectReferenceValue=null; gs.ApplyModifiedPropertiesWithoutUndo();
                bool failed=false; try { builder.Build(map); } catch(InvalidOperationException) { failed=true; }
                Require(failed && grass.CurrentMap==stable && stable.gameObject.activeInHierarchy,"Failed grass stage preserves previous map.");
                map.Release(); Require(stable.Coverage[0]!=null,"Grass survives MapData.Release.");
                grass.Exclude(new Bounds(new Vector3(0,30,0),new Vector3(40,100,40)));
                Require(stable.Coverage[0].GetPixel(8,8).a==0,"Local exclusion updates coverage.");
                report.AppendLine("PASS: build/rebuild ownership; rollback; independent result lifetime; local exclusion.");
            }
            finally { map?.Release(); UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        }
        private static Color32[] Capture(Camera camera,RenderTexture target,Texture2D pixels,string path)
        {
            camera.Render(); var previous=RenderTexture.active;
            try { RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0); pixels.Apply(); System.IO.File.WriteAllBytes(path,pixels.EncodeToPNG()); return pixels.GetPixels32(); }
            finally { RenderTexture.active=previous; }
        }
        private static double Difference(Color32[] a,Color32[] b)
        { double sum=0; for(int i=0;i<a.Length;i++) sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b); return sum/(a.Length*3); }
        private static double Time(Camera camera,int frames)
        {
            var clock=Stopwatch.StartNew(); for(int i=0;i<frames;i++) camera.Render();
            UnityEngine.Rendering.AsyncGPUReadback.Request(camera.targetTexture,0).WaitForCompletion();
            return clock.Elapsed.TotalMilliseconds/frames;
        }
        private static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    }
}
