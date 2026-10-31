using System;
using System.IO;
using System.Threading;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class FarmCanopyChecks
    {
        [MenuItem("Tools/Tactical Maps/Agriculture/Refresh Distant Crop Colours")]
        internal static void RefreshPreview()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play Mode first.");
            foreach(var map in UnityEngine.Object.FindObjectsByType<AgricultureMap>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                var primitives=AgricultureSurfaceData.Describe(map.fields,map.farms,map.tracks);
                var all=map.GetComponentsInChildren<Terrain>(true);
                for(int i=0;i<3;i++)
                {
                    var t=Array.Find(all,t=>t.name==(i==0?"Terrain_Main":i==1?"Terrain_Surroundings":"Terrain_Horizon"));
                    if(t==null)throw new InvalidOperationException("Missing terrain level "+i);
                    var mask=map.Masks[i]; var size=t.terrainData.size;
                    var pixels=AgricultureRaster.FarMask(primitives,new Vector2(size.x,size.z),mask.width,CancellationToken.None);
                    string path=AssetDatabase.GetAssetPath(mask);
                    if(path.EndsWith(".png",StringComparison.OrdinalIgnoreCase))
                    {
                        // Imported preview textures need not keep a readable CPU copy.
                        var writable=new Texture2D(mask.width,mask.height,TextureFormat.RGBA32,false,true);
                        try { writable.SetPixels32(pixels);writable.Apply();File.WriteAllBytes(path,writable.EncodeToPNG()); }
                        finally { UnityEngine.Object.DestroyImmediate(writable); }
                        AssetDatabase.ImportAsset(path);
                    }
                    else {mask.SetPixels32(pixels);mask.Apply(true,false);EditorUtility.SetDirty(mask);}
                }
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Tactical Maps/Agriculture/Verify Distant Crop Colours")]
        internal static void Verify()
        {
            var p=new FarmSurfacePrimitive{Shape=new Vector4(0,0,24,24),Style=new Vector4(1,0,0,.5f),Detail=new Vector4(3,0,0,0)};
            var soil=AgricultureRaster.ColorFor(p);p.Style.z=1;var green=AgricultureRaster.ColorFor(p);
            p.Style.z=2;var gold=AgricultureRaster.ColorFor(p);
            Require(green.g>soil.g && gold.r>soil.r,"Crop colour remains visible in the horizon average");
            p.Style.z=5;Require(AgricultureRaster.ColorFor(p)==soil,"Tracks preserve soil colour");
            p.Style.z=2;p.Exclusion=new Vector4(0,0,8,8);
            var mask=AgricultureRaster.FarMask(new[]{p},new Vector2(64,64),64,CancellationToken.None);
            Require(mask[32*64+32].a==0 && mask[32*64+48].a>0,"Canopy mask respects courtyard exclusions");
            foreach(var name in new[]{"TacticalMaps/Agriculture/Rice","TacticalMaps/Terrain/MatteStochastic"})
            {
                var shader=Shader.Find(name);Require(shader!=null && shader.isSupported,name);
                foreach(var m in ShaderUtil.GetShaderMessages(shader))Require(m.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,m.message);
            }
            Directory.CreateDirectory("Logs/FarmCanopy");
            File.WriteAllText("Logs/FarmCanopy/checks.txt","PASS: green/gold horizon averages, fallow/track soil colour, courtyard exclusions, supported shaders without compiler errors.\n");
        }
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
