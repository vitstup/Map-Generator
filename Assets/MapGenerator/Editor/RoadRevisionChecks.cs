using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using TacticalMaps.Generation;
using TacticalMaps.Testing;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class RoadRevisionChecks
    {
        internal static string Report = "Not run";
        [MenuItem("Tools/Tactical Maps/Measure Road Generation")]
        internal static async void Measure()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Measure in Edit mode.");
            var workbench=UnityEngine.Object.FindAnyObjectByType<MapWorkbench>();
            Report="Running";string report="Selection: "+workbench.SelectedPoint+"\n";
            try
            {
                for(int run=0;run<2;run++)
                {
                    var watch=Stopwatch.StartNew();MapData map=null;
                    try
                    {
                        map=await workbench.Generator.GenerateAsync(workbench.SelectedPoint,CancellationToken.None);
                        report+=$"Run {run+1}: {watch.Elapsed.TotalMilliseconds:F0} ms; roads {map.Roads?.Segments.Length}; tiles {map.Roads?.Tiles.Count}; {workbench.Generator.Roads.LastTimings}\n";
                    }
                    finally{map?.Release();}
                }
                Directory.CreateDirectory("Logs/Roads");File.WriteAllText("Logs/Roads/generation-timing.txt",report);Report=report;UnityEngine.Debug.Log(report);
            }
            catch(Exception e){Report="FAILED: "+e;UnityEngine.Debug.LogException(e);}
        }
    }
}
