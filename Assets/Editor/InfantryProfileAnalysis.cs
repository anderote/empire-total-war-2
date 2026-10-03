using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditorInternal;
using UnityEngine;

// Offline inspection of captured native CPU timelines; inclusive samples overlap.
public static class InfantryProfileAnalysis
{
    sealed class Sample { public double Total,Max;public long Calls; }
    public static void Analyze()
    {
        var labels=new[]{"baseline-combat-v2","optimized-combat-profile","final-combat-profile"};
        var args=Environment.GetCommandLineArgs();
        for(int i=0;i+1<args.Length;i++)if(args[i]=="--profile-label")labels=new[]{args[i+1]};
        foreach(string label in labels)
        {
            string folder="Artifacts/InfantryOptimization/"+label,path=folder+"/timeline.raw";
            if(!File.Exists(path))continue;
            ProfilerDriver.ClearAllFrames();
            if(!ProfilerDriver.LoadProfile(path,false))throw new Exception("Cannot load "+path);
            var samples=new Dictionary<string,Sample>();int frames=0;
            for(int frame=ProfilerDriver.firstFrameIndex;frame<=ProfilerDriver.lastFrameIndex;frame++)
            {
                if(frame<ProfilerDriver.firstFrameIndex+60)continue;
                frames++;
                for(int thread=0;;thread++)
                {
                    using(var data=ProfilerDriver.GetRawFrameDataView(frame,thread))
                    {
                        if(!data.valid)break;
                        if(data.threadName!="Main Thread")continue;
                        for(int i=0;i<data.sampleCount;i++)
                        {
                            string name=data.GetSampleName(i);double ms=data.GetSampleTimeMs(i);
                            if(!samples.TryGetValue(name,out var value)){value=new Sample();samples.Add(name,value);}
                            value.Total+=ms;value.Max=Math.Max(value.Max,ms);value.Calls++;
                        }
                    }
                }
            }
            using(var writer=new StreamWriter(folder+"/cpu-markers.csv"))
            {
                writer.WriteLine("marker,calls,totalInclusiveMs,meanPerFrameMs,maxCallMs");
                foreach(var entry in samples.OrderByDescending(x=>x.Value.Total))
                    writer.WriteLine("\""+entry.Key.Replace("\"","\"\"")+"\","+entry.Value.Calls+","+entry.Value.Total.ToString("R",CultureInfo.InvariantCulture)+","+(entry.Value.Total/Math.Max(1,frames)).ToString("R",CultureInfo.InvariantCulture)+","+entry.Value.Max.ToString("R",CultureInfo.InvariantCulture));
            }
            Debug.Log("PROFILE_ANALYZED "+label+" frames="+frames);
        }
    }
}
