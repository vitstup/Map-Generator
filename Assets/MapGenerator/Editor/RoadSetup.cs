using TacticalMaps.Generation;
using TacticalMaps.Construction;
using TacticalMaps.Testing;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class RoadSetup
    {
        private const string Source="Assets/Sprites/Maps/japan_roads_RG_4096.png";
        [MenuItem("Tools/Tactical Maps/Set Up Roads")]
        internal static void Install()
        {
            if(Application.isPlaying)throw new System.InvalidOperationException("Exit Play mode before configuring roads.");
            var generator=Object.FindAnyObjectByType<MapGenerator>();var builder=Object.FindAnyObjectByType<MapBuilder>();var workbench=Object.FindAnyObjectByType<MapWorkbench>();
            if(generator==null||builder==null||workbench==null)throw new System.InvalidOperationException("Open the map workbench scene.");
            var importer=(TextureImporter)AssetImporter.GetAtPath(Source);
            bool importNeeded=!importer.isReadable||importer.sRGBTexture||importer.textureCompression!=TextureImporterCompression.Uncompressed||importer.GetDefaultPlatformTextureSettings().format!=TextureImporterFormat.RGBA32;
            importer.isReadable=true;importer.sRGBTexture=false;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.maxTextureSize=4096;importer.filterMode=FilterMode.Point;importer.wrapMode=TextureWrapMode.Clamp;
            var platform=importer.GetDefaultPlatformTextureSettings();platform.maxTextureSize=4096;platform.format=TextureImporterFormat.RGBA32;platform.textureCompression=TextureImporterCompression.Uncompressed;importer.SetPlatformTextureSettings(platform);if(importNeeded)importer.SaveAndReimport();
            var roads=generator.GetComponent<RoadGenerator>();if(roads==null)roads=Undo.AddComponent<RoadGenerator>(generator.gameObject);
            Set(roads,"roadSource",AssetDatabase.LoadAssetAtPath<Texture2D>(Source));Set(generator,"roadGenerator",roads);
            var construction=builder.GetComponent<RoadBuilder>();if(construction==null)construction=Undo.AddComponent<RoadBuilder>(builder.gameObject);Set(builder,"roadBuilder",construction);
            Set(construction,"largeSurface",Layer("LargeRoad",4));Set(construction,"mediumSurface",Layer("MediumRoad",3));
            var settings=new SerializedObject(roads);
            if(Mathf.Approximately(settings.FindProperty("largeWidth").floatValue,6))settings.FindProperty("largeWidth").floatValue=8;
            if(Mathf.Approximately(settings.FindProperty("shoulderWidth").floatValue,1))settings.FindProperty("shoulderWidth").floatValue=1.8f;
            settings.ApplyModifiedProperties();
            SetColor(construction,"largeTint",new Color(.94f,.85f,.68f));SetColor(construction,"mediumTint",new Color(.78f,.66f,.48f));
            SetColor(workbench,"largeRoadColor",new Color(.70f,.52f,.32f));SetColor(workbench,"mediumRoadColor",new Color(.44f,.30f,.18f));
            UpgradeSurfaces();
            var serialized=new SerializedObject(workbench);
            var existing=(UnityEngine.UI.RawImage)serialized.FindProperty("globalOverlay").objectReferenceValue;
            var map=existing.transform.parent;var overlay=map.Find("RoadOverlay");
            if(overlay==null)
            {
                var go=new GameObject("RoadOverlay",typeof(RectTransform),typeof(UnityEngine.UI.RawImage));Undo.RegisterCreatedObjectUndo(go,"Add road map overlay");go.transform.SetParent(map,false);
                var r=(RectTransform)go.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
                go.GetComponent<UnityEngine.UI.RawImage>().raycastTarget=false;overlay=go.transform;
            }
            overlay.SetSiblingIndex(existing.transform.GetSiblingIndex()+1);Set(workbench,"roadOverlay",overlay.GetComponent<UnityEngine.UI.RawImage>());
            Transform canvas=workbench.transform;
            var toggle=canvas.Find("RoadVisibility");
            if(toggle==null)
            {
                var go=new GameObject("RoadVisibility",typeof(RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Toggle));Undo.RegisterCreatedObjectUndo(go,"Add road visibility toggle");go.transform.SetParent(canvas,false);Place((RectTransform)go.transform,30,915,22,22);
                var background=go.GetComponent<UnityEngine.UI.Image>();background.color=new Color(.13f,.2f,.24f);
                var check=new GameObject("Check",typeof(RectTransform),typeof(UnityEngine.UI.Image));check.transform.SetParent(go.transform,false);var r=(RectTransform)check.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=new Vector2(4,4);r.offsetMax=new Vector2(-4,-4);var image=check.GetComponent<UnityEngine.UI.Image>();image.color=new Color(1,.8f,.35f);image.raycastTarget=false;
                var t=go.GetComponent<UnityEngine.UI.Toggle>();t.targetGraphic=background;t.graphic=image;t.isOn=true;toggle=go.transform;
                Label(canvas,"RoadLegend","Дороги: <color=#B38552>крупные</color> / <color=#704D2E>средние</color>",62,914,570,26);
            }
            var legend=canvas.Find("RoadLegend");if(legend!=null)legend.GetComponent<TMP_Text>().text="Дороги: <color=#B38552>крупные</color> / <color=#704D2E>средние</color>";
            Set(workbench,"showRoadsToggle",toggle.GetComponent<UnityEngine.UI.Toggle>());
            workbench.RefreshRoadOverlay();workbench.RefreshSelection();EditorUtility.SetDirty(workbench);
            SaveTexture(overlay.GetComponent<UnityEngine.UI.RawImage>(),"Assets/MapGenerator/Generated/RoadOverview.asset");
            SaveTexture((UnityEngine.UI.RawImage)new SerializedObject(workbench).FindProperty("cropImage").objectReferenceValue,"Assets/MapGenerator/Generated/SelectedPixels.asset");
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);EditorSceneManager.SaveScene(generator.gameObject.scene);AssetDatabase.SaveAssets();
            Debug.Log("Road components, source import and workbench overlays configured.");
        }
        private static void SetColor(Object owner,string name,Color value){var s=new SerializedObject(owner);s.FindProperty(name).colorValue=value;s.ApplyModifiedProperties();EditorUtility.SetDirty(owner);}
        private static void UpgradeSurfaces()
        {
            const string folder="Assets/MapGenerator/Materials/TerrainLayers/";
            var large=AssetDatabase.LoadAssetAtPath<TerrainLayer>(folder+"LargeRoad.terrainlayer");
            var medium=AssetDatabase.LoadAssetAtPath<TerrainLayer>(folder+"MediumRoad.terrainlayer");
            var rock=AssetDatabase.LoadAssetAtPath<TerrainLayer>(folder+"Rock.terrainlayer");
            if(rock!=null){large.diffuseTexture=rock.diffuseTexture;large.normalMapTexture=rock.normalMapTexture;large.normalScale=.35f;large.tileSize=new Vector2(.9f,.9f);EditorUtility.SetDirty(large);}
            medium.tileSize=new Vector2(2.4f,2.4f);medium.normalScale=.65f;EditorUtility.SetDirty(medium);
        }
        private static void SaveTexture(UnityEngine.UI.RawImage image,string path)
        {
            var texture=image.texture as Texture2D;if(texture==null||EditorUtility.IsPersistent(texture))return;
            texture.name=System.IO.Path.GetFileNameWithoutExtension(path);
            var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(existing==null)AssetDatabase.CreateAsset(texture,path);
            else{EditorUtility.CopySerialized(texture,existing);image.texture=existing;Object.DestroyImmediate(texture);EditorUtility.SetDirty(existing);}
            EditorUtility.SetDirty(image);
        }
        private static TerrainLayer Layer(string name,float tiles)
        {
            string path="Assets/MapGenerator/Materials/TerrainLayers/"+name+".terrainlayer";var layer=AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if(layer!=null)return layer;
            var earth=AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/MapGenerator/Materials/TerrainLayers/Earth.terrainlayer");
            layer=earth!=null?Object.Instantiate(earth):new TerrainLayer();layer.name=name;layer.tileSize=new Vector2(tiles,tiles);AssetDatabase.CreateAsset(layer,path);return layer;
        }
        private static void Set(Object owner,string name,Object value){var s=new SerializedObject(owner);s.FindProperty(name).objectReferenceValue=value;s.ApplyModifiedProperties();EditorUtility.SetDirty(owner);}
        private static void Place(RectTransform r,float x,float y,float w,float h){r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);}
        private static void Label(Transform parent,string name,string text,float x,float y,float w,float h)
        {var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);Place((RectTransform)go.transform,x,y,w,h);var label=go.GetComponent<TextMeshProUGUI>();var reference=parent.GetComponentInChildren<TextMeshProUGUI>();if(reference!=null)label.font=reference.font;label.text=text;label.fontSize=18;label.color=Color.white;label.raycastTarget=false;}
    }
}
