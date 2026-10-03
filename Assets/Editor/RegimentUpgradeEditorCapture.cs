using System;
using System.IO;
using Empire.UI;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// Runs the production campaign panel in a temporary Editor Play Mode scene,
// then stops only this bounded review and exits the dedicated editor process.
public static class RegimentUpgradeEditorCapture
{
    const string ActiveKey="ETW2.RegimentUpgradeEditorCapture.Active";
    const string OutputKey="ETW2.RegimentUpgradeEditorCapture.Output";
    const string StartedKey="ETW2.RegimentUpgradeEditorCapture.Started";
    const string StopKey="ETW2.RegimentUpgradeEditorCapture.Stop";
    const string ExitCodeKey="ETW2.RegimentUpgradeEditorCapture.ExitCode";
    static readonly string[] Images={"01-wide-stats-and-live-model.png","02-wide-purchase-comparison.png"};

    [Serializable] sealed class CaptureReport
    {
        public bool passed=false;
        public int captureCount=0,width=0,height=0;
        public bool campaignEntered=false,persistentSavesModified=false;
    }

    [InitializeOnLoadMethod]
    static void ResumeAfterReload()
    {
        if(SessionState.GetBool(ActiveKey,false))Hook();
    }

    public static void Start()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("A Unity play session is already active; the capture runner will not replace it.");
        string[] args=Environment.GetCommandLineArgs();int flag=Array.IndexOf(args,"--regiment-upgrade-ui");
        if(flag<0||flag+1>=args.Length||Array.IndexOf(args,"--regiment-upgrade-focused-capture")<0)
            throw new InvalidOperationException("Pass --regiment-upgrade-ui <task-local-output> and --regiment-upgrade-focused-capture.");
        string output=args[flag+1];Directory.CreateDirectory(output);
        string resultPath=Path.Combine(output,"result.json");if(File.Exists(resultPath))File.Delete(resultPath);
        foreach(string image in Images){string path=Path.Combine(output,image);if(File.Exists(path))File.Delete(path);}
        SessionState.SetString(OutputKey,output);SessionState.SetString(StartedKey,DateTime.UtcNow.Ticks.ToString());
        SessionState.SetBool(StopKey,false);SessionState.SetInt(ExitCodeKey,1);SessionState.SetBool(ActiveKey,true);

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=new GameObject("Temporary regiment upgrade capture");
        var camera=root.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=EmpireTheme.Ink;camera.cullingMask=0;
        root.AddComponent<RegimentUpgradeReview>();
        Hook();EditorApplication.EnterPlaymode();
    }

    public static void BuildFocusedReviewApp()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("A Unity play session is active; the review build will not replace it.");
        string[] args=Environment.GetCommandLineArgs();int flag=Array.IndexOf(args,"--regiment-upgrade-ui");
        if(flag<0||flag+1>=args.Length)throw new InvalidOperationException("Pass --regiment-upgrade-ui <task-local-output>.");
        string output=Path.GetFullPath(args[flag+1]);Directory.CreateDirectory(output);
        string app=Path.GetFullPath(Path.Combine(Directory.GetParent(output).FullName,"RegimentUpgradeFocusedCapture.app"));
        string scenePath="Assets/Scenes/__RegimentUpgradeFocusedCapture.unity";
        if(File.Exists(scenePath)||File.Exists(scenePath+".meta"))throw new IOException("The temporary review scene path is occupied; preserve it.");
        if(Directory.Exists(app)||File.Exists(app))throw new IOException("The task-local review app path is occupied; preserve it.");
        var setup=EditorSceneManager.GetSceneManagerSetup();
        try
        {
            RegimentUpgradeMenuVerification.Verify();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("Temporary regiment upgrade capture");
            var camera=root.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=EmpireTheme.Ink;camera.cullingMask=0;
            root.AddComponent<RegimentUpgradeReview>();
            if(!EditorSceneManager.SaveScene(scene,scenePath))throw new IOException("Could not save the temporary review scene.");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scenePath},locationPathName=app,target=BuildTarget.StandaloneOSX,options=BuildOptions.None});
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Focused regiment review app build failed.");
            Debug.Log("REGIMENT_UPGRADE_FOCUSED_REVIEW_BUILD_PASSED "+app);
        }
        finally
        {
            if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);
            AssetDatabase.DeleteAsset(scenePath);
        }
    }

    static void Hook(){EditorApplication.update-=Tick;EditorApplication.update+=Tick;}

    static void Tick()
    {
        if(!SessionState.GetBool(ActiveKey,false)){EditorApplication.update-=Tick;return;}
        try
        {
            if(SessionState.GetBool(StopKey,false))
            {
                if(!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    int code=SessionState.GetInt(ExitCodeKey,1);SessionState.SetBool(ActiveKey,false);
                    EditorApplication.update-=Tick;EditorApplication.Exit(code);
                }
                return;
            }

            string output=SessionState.GetString(OutputKey,"");
            string reportPath=Path.Combine(output,"result.json");
            if(File.Exists(reportPath))
            {
                var report=JsonUtility.FromJson<CaptureReport>(File.ReadAllText(reportPath));
                if(report==null||!report.passed||report.captureCount!=2||report.width!=1920||report.height!=1080||report.campaignEntered||report.persistentSavesModified)
                    throw new InvalidDataException("The focused capture report failed or did not match the requested two-shot, 1920×1080, no-save scope.");
                foreach(string image in Images)RequirePng(Path.Combine(output,image));
                SessionState.SetInt(ExitCodeKey,0);SessionState.SetBool(StopKey,true);
                if(EditorApplication.isPlaying)EditorApplication.isPlaying=false;
                return;
            }

            long started;
            if(!long.TryParse(SessionState.GetString(StartedKey,""),out started))started=DateTime.UtcNow.Ticks;
            if(DateTime.UtcNow.Ticks-started>TimeSpan.FromMinutes(3).Ticks)
                throw new TimeoutException("The two production-panel screenshots were not completed within three minutes.");
        }
        catch(Exception error)
        {
            Debug.LogException(error);SessionState.SetInt(ExitCodeKey,1);SessionState.SetBool(StopKey,true);
            if(EditorApplication.isPlaying)EditorApplication.isPlaying=false;
        }
    }

    static void RequirePng(string path)
    {
        if(!File.Exists(path))throw new FileNotFoundException("The requested production UI screenshot was not written.",path);
        using(var stream=File.OpenRead(path))
        {
            if(stream.Length<24)throw new InvalidDataException("Screenshot is shorter than a PNG header: "+path);
            byte[] header=new byte[24];stream.Read(header,0,header.Length);
            byte[] signature={137,80,78,71,13,10,26,10};
            for(int i=0;i<signature.Length;i++)if(header[i]!=signature[i])throw new InvalidDataException("Screenshot is not a PNG: "+path);
            int width=ReadBigEndian(header,16),height=ReadBigEndian(header,20);
            if(width!=1920||height!=1080)throw new InvalidDataException("Expected 1920×1080 screenshot, received "+width+"×"+height+": "+path);
        }
    }

    static int ReadBigEndian(byte[] bytes,int offset)=>(bytes[offset]<<24)|(bytes[offset+1]<<16)|(bytes[offset+2]<<8)|bytes[offset+3];
}
