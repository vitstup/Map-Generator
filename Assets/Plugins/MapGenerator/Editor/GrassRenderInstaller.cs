using System;
using System.Collections.Generic;
using System.IO;
using TacticalMaps.Grass;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace TacticalMaps.Editor
{
    public sealed class GrassRenderInstaller : ScriptableObject
    {
        [SerializeField] private Shader compositeShader;
        [SerializeField] private StyleSheet windowStyle;
        internal Shader Shader => compositeShader != null ? compositeShader : Shader.Find("Hidden/TacticalMaps/GrassComposite");
        internal StyleSheet WindowStyle => windowStyle;

        [OnOpenAsset]
        private static bool OpenAsset(int instanceId, int line)
        {
            if(EditorUtility.InstanceIDToObject(instanceId) is not GrassRenderInstaller installer)return false;
            GrassRenderInstallerWindow.Open(installer);return true;
        }

        internal string Install(string folder)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Сначала выйдите из Play Mode.");
            if(!AssetDatabase.IsValidFolder(folder)||(folder!="Assets"&&!folder.StartsWith("Assets/",StringComparison.Ordinal)))
                throw new ArgumentException("Выберите папку внутри Assets.");
            var shader=Shader;
            if(shader==null)throw new InvalidOperationException("Не найден GrassComposite.shader. Импортируйте шейдеры MapGenerator.");
            var guids=AssetDatabase.FindAssets("t:UniversalRendererData",new[]{folder});
            int added=0,updated=0,ready=0;var failures=new List<string>();
            Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Install Grass Render Optimization");
            try
            {
                foreach(var guid in guids)
                {
                    string path=AssetDatabase.GUIDToAssetPath(guid);
                    try
                    {
                        var data=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                        if(!AssetDatabase.IsOpenForEdit(data,out string reason))throw new InvalidOperationException(reason);
                        var feature=data.rendererFeatures.Find(item=>item is GrassRenderFeature) as GrassRenderFeature;
                        bool created=feature==null;
                        if(!created)
                        {
                            var current=new SerializedObject(feature);
                            if(feature.isActive&&current.FindProperty("compositeShader").objectReferenceValue==shader&&
                                current.FindProperty("rendererData").objectReferenceValue==data){ready++;continue;}
                        }
                        Undo.RecordObject(data,"Install Grass Render Optimization");
                        if(created)
                        {
                            feature=CreateInstance<GrassRenderFeature>();feature.name="Grass Render Optimization";
                            AssetDatabase.AddObjectToAsset(feature,data);
                            Undo.RegisterCreatedObjectUndo(feature,"Install Grass Render Optimization");
                            data.rendererFeatures.Add(feature);
                        }
                        else Undo.RecordObject(feature,"Configure Grass Render Optimization");
                        var settings=new SerializedObject(feature);
                        settings.FindProperty("compositeShader").objectReferenceValue=shader;
                        settings.FindProperty("rendererData").objectReferenceValue=data;
                        settings.ApplyModifiedPropertiesWithoutUndo();feature.SetActive(true);feature.Create();
                        var renderer=new SerializedObject(data);var map=renderer.FindProperty("m_RendererFeatureMap");
                        map.arraySize=data.rendererFeatures.Count;
                        for(int i=0;i<map.arraySize;i++)
                        {
                            long id=0;
                            if(data.rendererFeatures[i]!=null)AssetDatabase.TryGetGUIDAndLocalFileIdentifier(data.rendererFeatures[i],out string _,out id);
                            map.GetArrayElementAtIndex(i).longValue=id;
                        }
                        renderer.ApplyModifiedPropertiesWithoutUndo();
                        data.SetDirty();EditorUtility.SetDirty(feature);EditorUtility.SetDirty(data);
                        AssetDatabase.SaveAssetIfDirty(data);
                        if(created)added++;else updated++;
                    }
                    catch(Exception ex){failures.Add(path+": "+ex.Message);}
                }
            }
            finally{Undo.CollapseUndoOperations(undo);}
            string result=guids.Length==0?"URP Renderer Data в этой папке не найдены.":
                $"Добавлено: {added}. Обновлено: {updated}. Уже настроено: {ready}.";
            if(failures.Count>0)result+="\nНе удалось настроить:\n"+string.Join("\n",failures);
            return result;
        }
    }

    internal sealed class GrassRenderInstallerWindow : EditorWindow
    {
        [SerializeField] private GrassRenderInstaller installer;
        [SerializeField] private string folder="Assets/Settings";
        private Label folderLabel;
        private HelpBox result;

        [MenuItem("Tools/Tactical Maps/Install Grass Render Feature...")]
        private static void OpenMenu()
        {
            var assets=AssetDatabase.FindAssets("t:GrassRenderInstaller");
            if(assets.Length==0)throw new InvalidOperationException("Импортируйте Install Grass Rendering.asset из MapGenerator.");
            Open(AssetDatabase.LoadAssetAtPath<GrassRenderInstaller>(AssetDatabase.GUIDToAssetPath(assets[0])));
        }
        internal static void Open(GrassRenderInstaller source)
        {
            var window=GetWindow<GrassRenderInstallerWindow>();window.installer=source;
            window.titleContent=new GUIContent("Grass Rendering");window.minSize=new Vector2(420,230);
            window.CreateGUI();window.Show();
        }
        public void CreateGUI()
        {
            var root=rootVisualElement;root.Clear();root.AddToClassList("grass-installer");
            if(installer!=null&&installer.WindowStyle!=null&&!root.styleSheets.Contains(installer.WindowStyle))root.styleSheets.Add(installer.WindowStyle);
            root.Add(new HelpBox("Подключает Grass Render Optimization ко всем URP Renderer Data в папке и подпапках. Повторный запуск не создаёт дубликаты. Параметры качества сохраняются.",HelpBoxMessageType.Info));
            if(!AssetDatabase.IsValidFolder(folder))folder="Assets";
            folderLabel=new Label(folder);folderLabel.AddToClassList("folder-path");root.Add(folderLabel);
            root.Add(new Button(ChooseFolder){text="Выбрать папку…"});
            var install=new Button(Install){text="Установить / обновить",name="installGrassFeature"};root.Add(install);
            result=new HelpBox("",HelpBoxMessageType.Info);result.AddToClassList("result");root.Add(result);
        }
        private void ChooseFolder()
        {
            string selected=EditorUtility.OpenFolderPanel("Папка с URP Renderer Data",Path.GetFullPath(folder),"");
            if(string.IsNullOrEmpty(selected))return;
            string relative=FileUtil.GetProjectRelativePath(selected.Replace('\\','/'));
            if(!AssetDatabase.IsValidFolder(relative)||(relative!="Assets"&&!relative.StartsWith("Assets/",StringComparison.Ordinal)))
            {result.text="Выберите папку внутри Assets текущего проекта.";return;}
            folder=relative;folderLabel.text=folder;result.text="";
        }
        private void Install()
        {
            try{result.text=installer.Install(folder);}
            catch(Exception ex){result.text=ex.Message;}
        }
    }
}
