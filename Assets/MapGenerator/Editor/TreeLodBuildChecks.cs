using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

namespace TacticalMaps.Editor
{
    internal static class TreeLodBuildChecks
    {
        [MenuItem("Tools/Tactical Maps/Build Tree Performance Check")]
        internal static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before building.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/MainScene.unity") throw new InvalidOperationException("Open MainScene for the performance build.");
            EditorSceneManager.SaveScene(scene);
            bool timing = PlayerSettings.enableFrameTimingStats;
            try
            {
                PlayerSettings.enableFrameTimingStats = true;
                var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { scene.path }, locationPathName = "Builds/TreeOptimized/Map.exe",
                    target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development | BuildOptions.StrictMode
                });
                File.WriteAllText("Temp/tree-build-check.txt", result.summary.result + "; errors=" + result.summary.totalErrors + "; warnings=" + result.summary.totalWarnings + "; time=" + result.summary.totalTime);
                if (result.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Tree performance build failed.");
            }
            finally
            {
                PlayerSettings.enableFrameTimingStats = timing;
                EditorApplication.ExecuteMenuItem("File/Save Project");
            }
        }
    }
}
