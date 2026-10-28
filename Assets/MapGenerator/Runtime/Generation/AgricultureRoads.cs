using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
namespace TacticalMaps.Generation
{
    internal static class AgricultureRoads
    {
        internal static RoadData Combine(RoadData original,FarmTrack[] tracks,Vector2 size,float width,Func<Vector2,bool> dry,CancellationToken token)
        {
            if(original==null||tracks.Length==0)return null;
            var segments=new List<RoadSegment>(original.Segments);int start=segments.Count;
            foreach(var track in tracks)
            {
                bool straight=(track.controlA==Vector2.zero&&track.controlB==Vector2.zero)||((track.controlA-Vector2.Lerp(track.a,track.b,1f/3)).sqrMagnitude<.001f&&(track.controlB-Vector2.Lerp(track.a,track.b,2f/3)).sqrMagnitude<.001f);
                int n=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(track.a,track.b)/(straight?32:6)));var before=track.a;
                for(int i=1;i<=n;i++){var p=track.Point(i/(float)n);if((p-before).sqrMagnitude>.01f)segments.Add(new RoadSegment(before,p,width,2));before=p;}
            }
            var result=new RoadData(segments.ToArray(),size,original.Shoulder,original.Clearance){GlobalSegmentCount=start};
            var changed=new HashSet<int>();
            foreach(var pair in result.Bins)foreach(int index in pair.Value)if(index>=start){changed.Add(pair.Key);break;}
            Array.Copy(original.FarCoverage,result.FarCoverage,original.FarCoverage.Length);
            var previous=new Dictionary<int,int>();for(int i=0;i<original.TileKeys.Count;i++)previous[original.TileKeys[i]]=i;
            var keys=new List<int>(result.Bins.Keys);keys.Sort();if(keys.Count>4096)throw new InvalidOperationException("Combined agricultural road coverage exceeds 4096 tiles.");
            var tiles=new Color32[keys.Count][];
            Parallel.For(0,keys.Count,new ParallelOptions{CancellationToken=token,MaxDegreeOfParallelism=Math.Max(1,Math.Min(4,Environment.ProcessorCount-1))},i=>
            {int key=keys[i];tiles[i]=!changed.Contains(key)&&previous.TryGetValue(key,out int old)?original.Tiles[old]:RoadRasterizer.Tile(result,key,dry,token);});
            result.TileKeys.AddRange(keys);result.Tiles.AddRange(tiles);return result;
        }
    }
}
