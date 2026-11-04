using System;
using TacticalMaps.Construction;
using TacticalMaps.Grass;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TacticalMaps.Editor
{
    internal static class ShadowQualityProfiles
    {
        [MenuItem("Tools/Tactical Maps/Shadows/Performance (object shadows)")]
        internal static void Performance() => Apply(true, false, 200, 1, 2048, false, true);

        [MenuItem("Tools/Tactical Maps/Shadows/Balanced (soft object shadows)")]
        internal static void Balanced() => Apply(true, false, 200, 2, 2048, false);

        [MenuItem("Tools/Tactical Maps/Shadows/Maximum performance (off)")]
        internal static void Off() => Apply(false, false, 200, 1, 2048, false, true);

        [MenuItem("Tools/Tactical Maps/Shadows/Include tree shadows")]
        internal static void IncludeTrees() => Apply(true, true, 200, 2, 2048, false);

        // Kept separate so comparisons can reproduce the old setup.
        [MenuItem("Tools/Tactical Maps/Shadows/Reference (4 cascades, 50 metres)")]
        internal static void Reference() => Apply(true, true, 50, 4, 2048, true);

        private static void Apply(bool enabled, bool treeShadows, float distance, int cascades, int atlas, bool reference, bool hard=false)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Apply a shadow profile outside Play Mode.");
            var asset=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(asset==null)throw new InvalidOperationException("The active quality level must use URP.");
            Undo.RecordObject(asset,"Apply shadow profile");
            var data=new SerializedObject(asset);
            data.FindProperty("m_MainLightShadowsSupported").boolValue=enabled;
            data.FindProperty("m_AdditionalLightShadowsSupported").boolValue=false;
            data.FindProperty("m_AnyShadowsSupported").boolValue=enabled;
            data.FindProperty("m_ShadowDistance").floatValue=distance;
            data.FindProperty("m_ShadowCascadeCount").intValue=cascades;
            data.FindProperty("m_MainLightShadowmapResolution").intValue=atlas;
            data.FindProperty("m_SoftShadowsSupported").boolValue=true;
            data.FindProperty("m_SoftShadowQuality").intValue=1;
            data.FindProperty("m_Cascade2Split").floatValue=.25f;
            data.FindProperty("m_CascadeBorder").floatValue=.2f;
            // Serialized application notifies the pipeline to recreate its cached renderers.
            data.ApplyModifiedProperties();EditorUtility.SetDirty(asset);
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if(light.type!=LightType.Directional||!light.enabled)continue;
                Undo.RecordObject(light,"Apply shadow profile");light.shadows=hard?LightShadows.Hard:LightShadows.Soft;light.shadowStrength=reference?1:.85f;
                if(light.TryGetComponent<UniversalAdditionalLightData>(out var extra))
                {Undo.RecordObject(extra,"Apply shadow quality");extra.softShadowQuality=SoftShadowQuality.Low;EditorUtility.SetDirty(extra);}
                EditorUtility.SetDirty(light);
            }
            foreach(var component in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if(component is TreeBuilder||component is InstancedTreeRenderer)
                {Set(component,"castShadows",treeShadows);Set(component,"shadowDistance",distance);}
                if(component is TerrainGenerator) Set(component,"shadowCasting",(int)(reference?ShadowCastingMode.TwoSided:ShadowCastingMode.On));
                if(component is GrassSystem) Set(component,"shadowFiltering",reference?2:1);
            }
            foreach(var terrain in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {Undo.RecordObject(terrain,"Apply terrain shadows");terrain.shadowCastingMode=reference?ShadowCastingMode.TwoSided:ShadowCastingMode.On;EditorUtility.SetDirty(terrain);}
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log($"Shadows: main={enabled}, trees={treeShadows}, {distance}m, {cascades} cascades, {atlas}, {(hard?"Hard":"Soft Low")}. Save the scene to persist object settings.");
        }
        private static void Set(UnityEngine.Object target,string name,bool value)
        {Undo.RecordObject(target,"Apply tree shadows");var data=new SerializedObject(target);data.FindProperty(name).boolValue=value;data.ApplyModifiedProperties();EditorUtility.SetDirty(target);}
        private static void Set(UnityEngine.Object target,string name,int value)
        {Undo.RecordObject(target,"Apply terrain shadows");var data=new SerializedObject(target);data.FindProperty(name).intValue=value;data.ApplyModifiedProperties();EditorUtility.SetDirty(target);}
        private static void Set(UnityEngine.Object target,string name,float value)
        {Undo.RecordObject(target,"Apply shadow range");var data=new SerializedObject(target);data.FindProperty(name).floatValue=value;data.ApplyModifiedProperties();EditorUtility.SetDirty(target);}
    }
}
