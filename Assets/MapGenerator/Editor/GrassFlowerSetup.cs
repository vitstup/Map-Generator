using System;
using TacticalMaps.Grass;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Editor
{
    internal static class GrassFlowerSetup
    {
        [MenuItem("Tools/Tactical Maps/Set Up Meadow Flower Palette")]
        internal static void Setup()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play mode first.");
            var system=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();
            if(system==null||system.CurrentMap==null)throw new InvalidOperationException("Build the meadow first.");
            bool enabled=system.enabled;system.enabled=false;
            try
            {
                var settings=new SerializedObject(system);var palette=settings.FindProperty("flowerPalette");
                bool oldDefaults=palette.arraySize==3&&palette.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue=="Warm white";
                if(oldDefaults)
                {
                    palette.arraySize=4;
                    palette.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue="White rosette";
                    palette.GetArrayElementAtIndex(0).FindPropertyRelative("weight").floatValue=.5f;
                    palette.GetArrayElementAtIndex(1).FindPropertyRelative("name").stringValue="Gold cup";
                    palette.GetArrayElementAtIndex(2).FindPropertyRelative("name").stringValue="Violet bell";
                    var red=palette.GetArrayElementAtIndex(3);red.FindPropertyRelative("name").stringValue="Red star";
                    red.FindPropertyRelative("color").colorValue=new Color(.65f,.12f,.1f);red.FindPropertyRelative("weight").floatValue=.08f;red.FindPropertyRelative("size").floatValue=1;
                }
                for(int i=0;i<palette.arraySize;i++)
                    if(oldDefaults)palette.GetArrayElementAtIndex(i).FindPropertyRelative("form").enumValueIndex=i%4;
                settings.ApplyModifiedPropertiesWithoutUndo();
                // Drain submitted draws before updating the shared native mesh/index buffers.
                var sync=new RenderTexture(1,1,0);sync.Create();
                try {Graphics.Blit(Texture2D.blackTexture,sync);AsyncGPUReadback.Request(sync,0).WaitForCompletion();}
                finally {sync.Release();UnityEngine.Object.DestroyImmediate(sync);}
                GrassSetup.EnsureLibrary();
                GrassCardBaker.Bake();GrassTurfBaker.Bake();
                EditorUtility.SetDirty(system);
            }
            finally {system.enabled=enabled;if(system.CurrentMap!=null)system.Attach(system.CurrentMap);}
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(system.gameObject.scene);EditorSceneManager.SaveScene(system.gameObject.scene);
        }
    }
}
