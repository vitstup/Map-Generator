using System;
using System.IO;
using System.Linq;
using System.Text;
using TacticalMaps.Construction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Editor
{
    internal static class TreeLodQualityChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Tree LOD Visual Quality")]
        internal static void Run()
        {
            var library = AssetDatabase.LoadAssetAtPath<TreeRenderLibrary>(TreeInstancingSetup.Folder + "/TreeRenderLibrary.asset");
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("LOD quality check"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var report = new StringBuilder("24 unseen directions per species; background-separated silhouette IoU and mean visible RGB relative to main mesh.\n");
            var sourceSun = RenderSettings.sun;
            var cameraObject = new GameObject("Camera", typeof(Camera)); cameraObject.transform.SetParent(root.transform);
            var camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.scene = scene;
            camera.orthographic = true; camera.nearClipPlane = .01f; camera.farClipPlane = 2000;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f,.18f,.22f,0); camera.cullingMask = 1 << 31;
            camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderShadows = false;
            var sunObject = new GameObject("Sun", typeof(Light)); sunObject.transform.SetParent(root.transform);
            var sun = sunObject.GetComponent<Light>(); sun.type = LightType.Directional; sun.cullingMask = 1 << 31; sun.shadows = LightShadows.None;
            sun.color = sourceSun != null ? sourceSun.color : Color.white; sun.intensity = sourceSun != null ? sourceSun.intensity : 2;
            sun.transform.rotation = sourceSun != null ? sourceSun.transform.rotation : Quaternion.Euler(50, -30, 0);
            try
            {
                foreach (var species in library.SpeciesList)
                {
                    var objects = species.levels.Select(level => Geometry(level, root.transform)).ToArray();
                    var bounds = objects[0].GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a,b) => { a.Encapsulate(b); return a; });
                    camera.orthographicSize = bounds.extents.magnitude * 1.04f;
                    double[] sum = new double[2], worst = { 1, 1 }, colorError = new double[2];
                    for (int row = 0; row < 2; row++) for (int azimuth = 0; azimuth < 12; azimuth++)
                    {
                        float angle = (17 + azimuth * 30) * Mathf.Deg2Rad, elevation = (row == 0 ? 20 : 55) * Mathf.Deg2Rad;
                        camera.transform.position = bounds.center + new Vector3(Mathf.Sin(angle) * Mathf.Cos(elevation), Mathf.Sin(elevation), Mathf.Cos(angle) * Mathf.Cos(elevation)) * bounds.size.magnitude * 3;
                        camera.transform.LookAt(bounds.center);
                        for (int level = 1; level <= 2; level++)
                        {
                            int resolution = level == 1 ? 192 : 80;
                            for (int i = 0; i < objects.Length; i++) objects[i].SetActive(i == 0);
                            var baseline = Capture(camera, resolution);
                            for (int i = 0; i < objects.Length; i++) objects[i].SetActive(i == level);
                            var candidate = Capture(camera, resolution);
                            int intersection = 0, union = 0, aCount = 0, bCount = 0; Vector3 aColor = Vector3.zero, bColor = Vector3.zero;
                            var background = baseline[0];
                            bool Visible(Color32 p) => Math.Abs(p.r-background.r) + Math.Abs(p.g-background.g) + Math.Abs(p.b-background.b) > 12;
                            for (int i = 0; i < baseline.Length; i++)
                            {
                                bool a = Visible(baseline[i]), b = Visible(candidate[i]);
                                if (a || b) union++; if (a && b) intersection++;
                                if (a) { aCount++; aColor += (Vector3)(Vector4)(Color)baseline[i]; }
                                if (b) { bCount++; bColor += (Vector3)(Vector4)(Color)candidate[i]; }
                            }
                            if (aCount < 20 || aCount > baseline.Length * .8 || bCount < 20) throw new InvalidOperationException("Invalid background/silhouette separation");
                            double iou = (double)intersection / Math.Max(1, union);
                            sum[level - 1] += iou; worst[level - 1] = Math.Min(worst[level - 1], iou);
                            colorError[level - 1] += (aColor / Math.Max(1, aCount) - bColor / Math.Max(1, bCount)).magnitude / Mathf.Sqrt(3);
                        }
                    }
                    report.AppendLine($"{species.source.name}: middle IoU mean={sum[0]/24:F3}, min={worst[0]:F3}, RGB error={colorError[0]/24:F3}; far IoU mean={sum[1]/24:F3}, min={worst[1]:F3}, RGB error={colorError[1]/24:F3}");
                    foreach (var go in objects)
                    { foreach (var r in go.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) UnityEngine.Object.DestroyImmediate(m); UnityEngine.Object.DestroyImmediate(go); }
                    File.WriteAllText("Temp/tree-lod-quality.txt", report.ToString());
                    if (sum[0]/24 < .95 || worst[0] < .90 || sum[1]/24 < .92 || worst[1] < .88 || colorError.Any(e => e/24 > .07))
                        throw new InvalidOperationException("LOD visual quality outside bounds: " + species.source.name);
                }
                CheckImpostors(library, root.transform, camera, sun, report);
                report.AppendLine($"PASS: {library.SpeciesList.Length} species; mesh silhouette/color at 24 directions per species; impostor silhouette/color at three instance rotations and two sun colors.");
                File.WriteAllText("Temp/tree-lod-quality.txt", report.ToString());
            }
            finally { UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void CheckImpostors(TreeRenderLibrary library, Transform root, Camera camera, Light sun, StringBuilder report)
        {
            var data = new TerrainData { heightmapResolution = 33, size = new Vector3(256, 1, 256) };
            var terrainObject = new GameObject("Placement data", typeof(Terrain)); terrainObject.transform.SetParent(root, false); terrainObject.transform.localPosition = new Vector3(-128,0,-128); terrainObject.layer = 31;
            var terrain = terrainObject.GetComponent<Terrain>(); terrain.terrainData = data; terrain.drawHeightmap = false; terrain.drawTreesAndFoliage = false;
            var rendererObject = new GameObject("Impostor test"); rendererObject.transform.SetParent(root, false);
            var trees = rendererObject.AddComponent<InstancedTreeRenderer>();
            var sunColor = sun.color;
            try
            {
                foreach (var species in library.SpeciesList)
                {
                    var geometry = Geometry(species.levels[0], root); double worstIou = 1, worstColor = 0, orientationAdvantage = double.NegativeInfinity;
                    data.treePrototypes = new[] { new TreePrototype { prefab = species.source } };
                    foreach (float yaw in new[] { 0f, 73f, 155f }) foreach (bool blue in new[] { false, true })
                    {
                        sun.color = blue ? new Color(.25f,.5f,1) : sunColor;
                        geometry.transform.localRotation = Quaternion.Euler(0,yaw,0);
                        data.SetTreeInstances(new[] { new TreeInstance { position = new Vector3(.5f,0,.5f), prototypeIndex = 0, rotation = yaw * Mathf.Deg2Rad, widthScale = 1, heightScale = 1, color = Color.white, lightmapColor = Color.white } }, false);
                        trees.Configure(new[] { terrain }, library, 6000, 64, 0, .3f);
                        camera.orthographicSize = species.size * QualitySettings.lodBias / (2 * .10f);
                        camera.transform.position = species.referencePoint + new Vector3(.25f,.4f,-1).normalized * species.size * 4;
                        camera.transform.LookAt(species.referencePoint);
                        trees.enabled = false; geometry.SetActive(true); var baseline = Capture(camera, 1024);
                        geometry.SetActive(false); trees.enabled = true; var candidate = Capture(camera, 1024);
                        int intersection = 0, union = 0, mirrorIntersection = 0, mirrorUnion = 0, ac = 0, bc = 0; Vector3 aColor = Vector3.zero, bColor = Vector3.zero; var background = baseline[0];
                        float captureX = camera.WorldToViewportPoint(geometry.transform.TransformPoint((Vector3)species.impostorCapture)).x * 1024 - .5f;
                        bool Visible(Color32 p) => Math.Abs(p.r-background.r) + Math.Abs(p.g-background.g) + Math.Abs(p.b-background.b) > 12;
                        for (int i = 0; i < baseline.Length; i++)
                        {
                            bool a = Visible(baseline[i]), b = Visible(candidate[i]); if (a || b) union++; if (a && b) intersection++;
                            int mirrorX = Mathf.RoundToInt(2 * captureX - i % 1024);
                            bool mirrored = mirrorX >= 0 && mirrorX < 1024 && Visible(candidate[i / 1024 * 1024 + mirrorX]);
                            if (a || mirrored) mirrorUnion++; if (a && mirrored) mirrorIntersection++;
                            if (a) { ac++; aColor += (Vector3)(Vector4)(Color)baseline[i]; }
                            if (b) { bc++; bColor += (Vector3)(Vector4)(Color)candidate[i]; }
                        }
                        double iou = (double)intersection / Math.Max(1,union);
                        double mirrorIou = (double)mirrorIntersection / Math.Max(1,mirrorUnion);
                        orientationAdvantage = Math.Max(orientationAdvantage, iou - mirrorIou);
                        double error = (aColor / Math.Max(1,ac) - bColor / Math.Max(1,bc)).magnitude / Mathf.Sqrt(3);
                        worstIou = Math.Min(worstIou,iou); worstColor = Math.Max(worstColor,error);
                        if (ac < 20 || bc < 20 || trees.IndirectDraws == 0) throw new InvalidOperationException("Missing impostor pixels");
                    }
                    report.AppendLine($"{species.source.name} relit impostor (deliberately oversized): min IoU={worstIou:F3}, max RGB error={worstColor:F3}, correct-versus-mirrored advantage={orientationAdvantage:F3}");
                    File.WriteAllText("Temp/tree-lod-quality.txt",report.ToString());
                    foreach (var r in geometry.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) UnityEngine.Object.DestroyImmediate(m);
                    UnityEngine.Object.DestroyImmediate(geometry); trees.enabled = false;
                    if (worstIou < .75 || worstColor > .15) throw new InvalidOperationException("Impostor color/silhouette mismatch: " + species.source.name);
                    if ((species.source.name.Contains("Pine_02") || species.source.name.Contains("Pine_03")) && orientationAdvantage < .08)
                        throw new InvalidOperationException("Asymmetric tree must match its real orientation better than its mirror: " + species.source.name);
                }
            }
            finally { sun.color = sunColor; UnityEngine.Object.DestroyImmediate(rendererObject); UnityEngine.Object.DestroyImmediate(terrainObject); UnityEngine.Object.DestroyImmediate(data); }
        }

        private static GameObject Geometry(TreeRenderLibrary.Level level, Transform root)
        {
            var go = new GameObject("Level"); go.transform.SetParent(root, false);
            foreach (var part in level.parts)
            {
                var child = new GameObject("Part", typeof(MeshFilter), typeof(MeshRenderer)); child.transform.SetParent(go.transform, false); child.layer = 31;
                child.transform.localPosition = part.localToRoot.GetColumn(3); child.transform.localRotation = part.localToRoot.rotation; child.transform.localScale = part.localToRoot.lossyScale;
                child.GetComponent<MeshFilter>().sharedMesh = part.mesh;
                var material = new Material(part.material); material.SetFloat("_WindStrength", 0); material.SetFloat("_TreeFade", 1);
                var renderer = child.GetComponent<MeshRenderer>(); renderer.sharedMaterials = Enumerable.Repeat(material, part.mesh.subMeshCount).ToArray(); renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            return go;
        }

        private static Color32[] Capture(Camera camera, int resolution)
        {
            var target = RenderTexture.GetTemporary(resolution, resolution, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active; var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            try
            {
                camera.aspect = 1; camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0,0,resolution,resolution),0,0); texture.Apply(); return texture.GetPixels32();
            }
            finally { camera.targetTexture = null; RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); RenderTexture.ReleaseTemporary(target); }
        }
    }
}
