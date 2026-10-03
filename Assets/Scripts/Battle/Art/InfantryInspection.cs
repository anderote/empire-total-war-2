using System;
using UnityEngine;
namespace EmpireBattle.Art
{
    public sealed class InfantryInspection : MonoBehaviour
    {
        public GameObject[] Soldiers;
        public AnimationClip[] Clips;
        public InfantryCrowdLibrary Library;
        MaterialPropertyBlock properties;
        int clip, motion=-1, variant;
        float phase, captureAt=2;
        bool paused, captured, kneeling;
        string shot;
        static readonly string[] Motions={"Walk","March","Jog","Run","Charge","Death","Lower","Rise","Idle","UnderFire","Captain","Drummer","Colour","Sergeant","Colonel","PikeReady","PikeThrust","PikeKnockdown","PikeGetUp","PikeDeath","OfficerReady","OfficerSlash","OfficerFire","OfficerKnockdown","OfficerGetUp","OfficerDeath","Knockdown","GetUp"};
        float Duration => motion==26?1.25f:motion==27?InfantryRecoveryTiming.RiseSeconds: motion==6?RifleFireTiming.LowerDuration:motion==7?RifleFireTiming.RaiseDuration:
            motion>=8?motion==9?2.4f:Library?Library.IdleDuration:3.333f:motion==5?1.25f:motion>=0?(motion<2?.9f:motion==2?.7f:.62f):clip==1?.85f:clip==2?RifleFireTiming.ReloadActionDuration:Clips[clip].length;
        void Start()
        {
            Application.runInBackground=true;Application.targetFrameRate=60;
            var args=Environment.GetCommandLineArgs();kneeling=Array.IndexOf(args,"--kneeling")>=0;
            for(int i=0;i<args.Length-1;i++)
            {
                if(args[i]=="--screenshot")shot=args[i+1];
                if(args[i]=="--clip")int.TryParse(args[i+1],out clip);
                if(args[i]=="--motion")motion=Array.FindIndex(Motions,n=>string.Equals(n,args[i+1],StringComparison.OrdinalIgnoreCase));
                if(args[i]=="--variant")int.TryParse(args[i+1],out variant);
                if(args[i]=="--capture-at")float.TryParse(args[i+1],out captureAt);
            }
            if(Array.IndexOf(args,"--ground-close")>=0){var camera=FindFirstObjectByType<Camera>();camera.transform.position=new Vector3(0,.85f,-3.5f);camera.transform.LookAt(new Vector3(0,.12f,.1f));camera.fieldOfView=56;}
            clip=Mathf.Clamp(clip,0,Clips.Length-1);
            int fixedPhase=Array.IndexOf(args,"--pose-phase");
            if(fixedPhase>=0&&fixedPhase+1<args.Length&&float.TryParse(args[fixedPhase+1],out float normalized)){phase=Mathf.Clamp01(normalized)*Duration;paused=true;}
            if(Array.IndexOf(args,"--hands-close")>=0){var camera=FindFirstObjectByType<Camera>();camera.transform.position=new Vector3(-2.3f,1.75f,-1.5f);camera.transform.LookAt(new Vector3(-1,1.32f,0));camera.fieldOfView=32;}
        }
        void Update()
        {
            if(!paused)phase+=Time.deltaTime;
            if(Library){Draw(Library.French,-1);Draw(Library.British,1);}
            else foreach(var s in Soldiers)Clips[clip].SampleAnimation(s,phase%Clips[clip].length);
            if(!captured&&shot!=null&&Time.time>captureAt){captured=true;ScreenCapture.CaptureScreenshot(shot);}
            if(shot!=null&&Time.time>captureAt+2)Application.Quit();
        }
        void Draw(InfantryCrowdSet set,float x)
        {
            var lod=set.Lods[0];var meshes=clip==0?lod.Idle:clip==1?lod.Fire:kneeling?lod.KneelingReload:lod.Reload;
            if(motion>=0&&motion<5&&lod.Locomotion!=null&&lod.Locomotion.Length>0)
            {
                var v=lod.Locomotion[Mathf.Clamp(variant,0,lod.Locomotion.Length-1)];
                meshes=motion==0?v.Walk:motion==1?v.March:motion==2?v.Jog:motion==3?v.Run:v.Charge;
            }
            if(motion==26||motion==27){var v=lod.RecoveryVariants[Mathf.Clamp(variant,0,lod.RecoveryVariants.Length-1)];meshes=motion==26?v.Knockdown:v.GetUp;}
            if(motion==5)meshes=lod.DeathVariants!=null&&lod.DeathVariants.Length>0?lod.DeathVariants[Mathf.Clamp(variant,0,lod.DeathVariants.Length-1)].Poses:lod.Death;
            if(motion==8||motion==9){
                var variants=motion==8?lod.IdleVariants:lod.UnderFireVariants;
                if(variants!=null&&variants.Length>0)meshes=variants[Mathf.Clamp(variant,0,variants.Length-1)].Poses;
            }
            if(motion>=10&&lod.Command!=null&&lod.Command.Length>motion-10)meshes=lod.Command[motion-10].Idle;
            if(motion>=15&&motion<=19&&lod.Command!=null&&lod.Command.Length>3){
                var pike=lod.Command[3];
                meshes=motion==15?pike.PikeReady:motion==16?pike.PikeThrust:motion==17?pike.PikeKnockdown:motion==18?pike.PikeGetUp:pike.PikeDeath;
            }
            if(motion>=20&&motion<=25&&lod.Command!=null&&lod.Command.Length>0){
                var officer=lod.Command[0];
                meshes=motion==20?officer.OfficerReady:motion==21?officer.OfficerSlash:motion==22?officer.OfficerFire:motion==23?officer.OfficerKnockdown:motion==24?officer.OfficerGetUp:officer.OfficerDeath;
            }
            if(motion==6||motion==7)
            {
                var transition=motion==6?lod.LowerToKneel:lod.RiseToAim;
                if(!transition)return;
                if(properties==null)properties=new MaterialPropertyBlock();
                float t=Mathf.Clamp01(phase/Duration);properties.SetFloat("_PoseBlend",Mathf.SmoothStep(0,1,motion==6?t:1-t));
                Graphics.DrawMesh(transition,Matrix4x4.TRS(new Vector3(x,0,0),Quaternion.Euler(0,244,0),Vector3.one),set.Material,0,null,0,properties);return;
            }
            if(meshes==null||meshes.Length==0)return;
            bool loop=motion>=0?motion!=5&&(motion<16||motion==20):clip==0;
            float normalized=!loop?Mathf.Clamp01(phase/Duration):Mathf.Repeat(phase/Duration,1f);
            float sample=normalized*(loop?meshes.Length:meshes.Length-1);
            int index=Mathf.Clamp(Mathf.FloorToInt(sample),0,meshes.Length-1);
            if(properties==null)properties=new MaterialPropertyBlock();properties.SetFloat("_PoseBlend",sample-index);
            Graphics.DrawMesh(meshes[index],Matrix4x4.TRS(new Vector3(x,0,0),Quaternion.Euler(0,244,0),Vector3.one),set.Material,0,null,0,properties);
        }
        void OnGUI()
        {
            GUI.Box(new Rect(15,15,820,269),"Infantry inspection — French / British");
            for(int i=0;i<Clips.Length;i++)if(GUI.Button(new Rect(25+i*200,45,195,26),Clips[i].name)){clip=i;motion=-1;phase=0;}
            for(int i=0;i<Motions.Length;i++)if(GUI.Button(new Rect(25+(i%8)*100,78+(i/8)*32,95,26),Motions[i])){motion=i;phase=0;variant=0;}
            if(GUI.Button(new Rect(25,210,120,26),"Variant "+(variant+1))){variant=(variant+1)%(motion==5?10:motion>=26?4:3);phase=0;}
            paused=GUI.Toggle(new Rect(155,210,100,25),paused,"Pause pose");
            float next=GUI.HorizontalSlider(new Rect(270,219,345,20),Mathf.Clamp01(phase/Duration),0,1);
            if(paused)phase=next*Duration;
            if(GUI.Button(new Rect(25,244,120,25),"Replay"))phase=0;
            GUI.Label(new Rect(155,244,460,25),"3 idle/carry variants · command party · 10 deaths · drag the timeline while paused");
        }
    }
}
