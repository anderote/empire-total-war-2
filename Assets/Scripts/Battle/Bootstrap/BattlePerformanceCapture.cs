using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace EmpireBattle
{
    // Collect the completed previous frame before any battle Update/LateUpdate work.
    [DefaultExecutionOrder(-10000)]
    public sealed class BattlePerformanceCapture : MonoBehaviour
    {
        readonly List<double> rows = new List<double>(4000000);
        readonly FrameTiming[] timing = new FrameTiming[1];
        ProfilerRecorder main, render, gc, draws, triangles;
        BattleRuntime runtime;
        string output;
        float started;
        int columns;
        bool binaryLog;
        public void Initialize(BattleRuntime owner,string path,string profilePath)
        {
            runtime=owner; output=Path.GetFullPath(path); started=Time.realtimeSinceStartup;
            long allocationBefore=GC.GetAllocatedBytesForCurrentThread();
            var allocationProbe=new byte[1024];GC.KeepAlive(allocationProbe);
            BattleMetrics.AllocationCountersSupported=GC.GetAllocatedBytesForCurrentThread()>allocationBefore;
            main=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);
            render=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Render Thread",1);
            gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1);
            draws=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count",1);
            triangles=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Triangles Count",1);
            BattleMetrics.Reset(); BattleMetrics.Enabled=true;
            if(!string.IsNullOrEmpty(profilePath) && Debug.isDebugBuild)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(profilePath)));
                Profiler.logFile=Path.GetFullPath(profilePath);Profiler.enableBinaryLog=true;Profiler.enabled=true;binaryLog=true;
            }
        }
        static double Value(ProfilerRecorder recorder,double scale=1) => recorder.Valid?recorder.LastValue*scale:-1;
        void Update()
        {
            if(!runtime)return;
            FrameTimingManager.CaptureFrameTimings();
            uint count=FrameTimingManager.GetLatestTimings(1,timing);
            if(Time.realtimeSinceStartup-started>2f)
            {
                int before=rows.Count;
                rows.Add(Time.frameCount-1); rows.Add(Time.realtimeSinceStartup-started);rows.Add(runtime.World.Time);
                rows.Add(Time.unscaledDeltaTime*1000); rows.Add(Value(main,.000001));rows.Add(Value(render,.000001));
                rows.Add(count>0&&timing[0].gpuFrameTime>0?timing[0].gpuFrameTime:-1);
                rows.Add(count>0?timing[0].cpuFrameTime:-1);
                rows.Add(Value(gc));rows.Add(Value(draws));rows.Add(Value(triangles));
                rows.Add(Profiler.GetTotalAllocatedMemoryLong());rows.Add(runtime.World.DeathCount);rows.Add(BattleMetrics.CrowdDrawCalls);rows.Add(BattleMetrics.CrowdInstances);rows.Add(runtime.ActiveRagdolls);rows.Add(runtime.FrozenRagdolls);
                for(int stage=0;stage<(int)BattleMetrics.Stage.Count;stage++)
                { rows.Add(BattleMetrics.Milliseconds[stage]);rows.Add(BattleMetrics.Calls[stage]);rows.Add(BattleMetrics.Maximum[stage]);rows.Add(BattleMetrics.AllocationCountersSupported?BattleMetrics.AllocatedBytes[stage]:-1); }
                columns=rows.Count-before;
            }
            BattleMetrics.Reset();
        }
        public void Write()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            using(var writer=new StreamWriter(output,false,new UTF8Encoding(false)))
            {
                writer.Write("frame,wallSeconds,simulationSeconds,frameMs,mainThreadMs,renderThreadMs,gpuMs,frameTimingCpuMs,gcBytes,drawCalls,triangles,allocatedBytes,deaths,crowdDrawCalls,crowdInstances,activeRagdolls,frozenRagdolls");
                for(int stage=0;stage<(int)BattleMetrics.Stage.Count;stage++)
                {string name=((BattleMetrics.Stage)stage).ToString();writer.Write($",{name}Ms,{name}Calls,{name}MaxMs,{name}GcBytes");}
                writer.WriteLine();
                for(int offset=0;columns>0&&offset<rows.Count;offset+=columns)
                {for(int i=0;i<columns;i++){if(i>0)writer.Write(',');writer.Write(rows[offset+i].ToString("R",CultureInfo.InvariantCulture));}writer.WriteLine();}
            }
            Debug.Log("BATTLE_PROFILE_WRITTEN "+output+" gpuTimingEnabled="+FrameTimingManager.IsFeatureEnabled());
        }
        void OnDestroy()
        {
            BattleMetrics.Enabled=false;main.Dispose();render.Dispose();gc.Dispose();draws.Dispose();triangles.Dispose();
            if(binaryLog){Profiler.enabled=false;Profiler.enableBinaryLog=false;}
        }
    }
}
