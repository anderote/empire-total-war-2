using System;
using System.Collections.Generic;
using System.Linq;
using EmpireBattle;
using EmpireBattle.Art;
using EmpireBattle.SupportingArt;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Empire.UI
{
    // One offscreen actor, sharing production meshes. Unscaled time keeps it alive in paused menus.
    public sealed class RegimentPortrait : MonoBehaviour
    {
        const int PortraitLayer=31;
        const float Handoff=.25f;
        struct Step
        {
            public string Name;
            public Mesh[] Poses;
            public Mesh Morph;
            public float Duration,From,To;
            public bool Loop;
        }
        readonly List<Step> steps=new List<Step>();
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        MaterialPropertyBlock properties;
        Camera portraitCamera;
        RenderTexture texture;
        Material material,wood,iron,bronze;
        InfantryCrowdLibrary infantry;
        CavalryCrowdLibrary cavalry;
        ProceduralSupportingArt.FieldCannonMeshSet cannon;
        Mesh bridge;
        CampaignPortraitDiorama diorama;
        Material groundPaint,horizonPaint,landmarkPaint;
        string identity="",caption="";
        int stepIndex,lastDrawFrame=-10,dragControl,activeButton=-1;
        double stepStart,lastVisible;
        bool paused;
        float pauseElapsed,yaw=155,pitch=10,distance=4,framedDistance=4;
        Vector3 focus=new Vector3(0,1,0),framedFocus;
        Vector4 uniform;
        Bounds modelBounds;
        public string Caption=>caption;
        public RenderTexture Texture=>texture;

        public static RegimentPortrait Create()
        {
            var host=new GameObject("Regiment portrait (offscreen)"){hideFlags=HideFlags.HideAndDontSave};
            return host.AddComponent<RegimentPortrait>();
        }
        void Awake()
        {
            properties=new MaterialPropertyBlock();
            var cameraObject=new GameObject("Portrait camera"){hideFlags=HideFlags.HideAndDontSave};
            cameraObject.transform.SetParent(transform,false);
            portraitCamera=cameraObject.AddComponent<Camera>();
            portraitCamera.enabled=false;portraitCamera.cullingMask=1<<PortraitLayer;
            portraitCamera.clearFlags=CameraClearFlags.SolidColor;
            portraitCamera.backgroundColor=new Color(.065f,.09f,.115f);
            portraitCamera.nearClipPlane=.02f;portraitCamera.farClipPlane=60;portraitCamera.fieldOfView=34;
            portraitCamera.allowHDR=false;portraitCamera.allowMSAA=false;portraitCamera.depth=-100;
            var data=cameraObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing=false;data.renderShadows=false;
            data.requiresColorOption=CameraOverrideOption.Off;data.requiresDepthOption=CameraOverrideOption.Off;
        }
        Material PortraitMaterial(Material original,Color? color=null)
        {
            var shader=Resources.Load<Shader>("RegimentPortrait");
            if(!shader)return null;
            var result=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            if(original&&original.HasProperty("_BaseMap"))result.SetTexture("_BaseMap",original.GetTexture("_BaseMap"));
            result.SetColor("_BaseColor",color??(original&&original.HasProperty("_BaseColor")?original.GetColor("_BaseColor"):Color.white));
            owned.Add(result);return result;
        }
        public void Configure(RegimentRecord unit)
        {
            string weapon=unit.Loadout?.items.Select(UnitEquipment.Find).FirstOrDefault(item=>item!=null&&!string.IsNullOrEmpty(item.Weapon))?.Weapon??unit.Weapon;
            string key=unit.Id+"/"+unit.Faction+"/"+unit.BattleRole+"/"+unit.Users+"/"+unit.Tier+"/"+weapon+"/"+unit.Loadout.appearance+"/"+string.Join(",",unit.Loadout.items);
            if(key==identity)return;
            identity=key;steps.Clear();ClearOwned();cannon=null;bridge=null;material=null;
            diorama=null;groundPaint=horizonPaint=landmarkPaint=null;
            uniform=RegimentBaseStats.Uniform(unit.Tier);
            diorama=CampaignPortraitDiorama.Build(unit.Faction,unit.BattleRole);
            owned.Add(diorama.Ground);owned.Add(diorama.Horizon);owned.Add(diorama.Landmark);
            groundPaint=PortraitMaterial(null,diorama.GroundColor);
            horizonPaint=PortraitMaterial(null,diorama.HorizonColor);
            landmarkPaint=PortraitMaterial(null,diorama.LandmarkColor);
            portraitCamera.backgroundColor=diorama.SkyColor;
            var actor=new BattalionState {Name=unit.Name,Loadout=unit.Loadout,Faction=unit.Faction,Side=ArmySide.French,Role=unit.BattleRole,Kind=unit.BattleRole==UnitRole.Cavalry?UnitKind.Cavalry:UnitKind.Infantry,Weapon=MilitaryEquipment.Weapon(weapon),TroopTier=unit.Tier};
            if(unit.Users==EquipmentUsers.Artillery)
            {
                cannon=ProceduralSupportingArt.CreateFieldCannonMeshes();
                owned.Add(cannon.Wood);owned.Add(cannon.Iron);owned.Add(cannon.Bronze);
                wood=PortraitMaterial(null,new Color(.3f,.22f,.12f));iron=PortraitMaterial(null,new Color(.16f,.17f,.19f));bronze=PortraitMaterial(null,new Color(.55f,.4f,.18f));
                Frame(cannon.Wood.bounds);caption="Field artillery";
            }
            else if(unit.BattleRole==UnitRole.Cavalry)
            {
                if(!cavalry)cavalry=Resources.Load<CavalryCrowdLibrary>("BakedCavalry/CavalryCrowdLibrary");
                var set=cavalry?cavalry.Resolve(actor):null;
                string mountedWeapon=NapoleonicOutfits.MountedWeapon(unit.Loadout);
                var motion=mountedWeapon=="carbine"?MountedMotion.MountedAim:mountedWeapon=="pistol"?MountedMotion.PistolAim:mountedWeapon=="lance"?MountedMotion.LanceReady:MountedMotion.Idle;
                var idle=set?.Lods?.Length>0?set.Lods[0].Clip(motion)??set.Lods[0].Clip(MountedMotion.Idle):null;
                material=PortraitMaterial(set?.Material);
                if(idle?.Poses?.Length>0){Add("Mounted idle",idle.Poses,5,true);Frame(idle.Poses[0].bounds);}
            }
            else
            {
                if(!infantry)infantry=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
                var set=infantry?infantry.Resolve(actor):null;
                var lod=set?.Lods?.Length>0?set.Lods[0]:null;
                material=PortraitMaterial(set?.Material);
                if(lod!=null)
                {
                    Add("Standing idle",lod.Idle,infantry.IdleDuration,true);
                    if(UnitRoster.Shoots(unit.BattleRole))
                    {
                        Add("Standing aim",lod.Fire,2.6f,false,.45f,.45f);
                        Add("Firing",lod.Fire,.8f,false,.5f,1);
                        Add("Reloading",lod.Reload,infantry.ReloadDuration);
                        AddMorph("Kneeling down",lod.LowerToKneel,.7f,0,1);
                        AddMorph("Kneeling idle",lod.LowerToKneel,3,1,1);
                        Add("Kneeling aim",lod.KneelingFire,2.8f,false,.45f,.45f);
                        Add("Kneeling fire",lod.KneelingFire,.8f,false,.5f,1);
                        Add("Kneeling reload",lod.KneelingReload,infantry.ReloadDuration);
                        AddMorph("Standing up",lod.RiseToIdle,.8f,1,0);
                        var variant=lod.IdleVariants?.Length>1?lod.IdleVariants[1]?.Poses:lod.Idle;
                        Add("Standing idle",variant,infantry.IdleDuration,true);
                        Add("Standing aim",lod.Fire,2,false,.45f,.45f);
                        AddMorph("Kneeling to aim",lod.KneelingAimTransition,.8f,0,1);
                        Add("Kneeling aim",lod.KneelingFire,3,false,.45f,.45f);
                        AddMorph("Standing up",lod.RiseToAim,.8f,1,0);
                    }
                    if(lod.Idle?.Length>0)Frame(lod.Idle[0].bounds);
                }
            }
            stepIndex=0;stepStart=Time.realtimeSinceStartupAsDouble;paused=false;pauseElapsed=0;
            if(steps.Count==0&&cannon==null)caption="Unit preview unavailable";
        }
        void Add(string name,Mesh[] poses,float duration,bool loop=false,float from=0,float to=1)
        {if(poses?.Length>0)steps.Add(new Step {Name=name,Poses=poses,Duration=duration,Loop=loop,From=from,To=to});}
        void AddMorph(string name,Mesh mesh,float duration,float from,float to)
        {if(mesh)steps.Add(new Step {Name=name,Morph=mesh,Duration=duration,From=from,To=to});}
        void Frame(Bounds bounds)
        {
            modelBounds=bounds;framedFocus=bounds.center;focus=framedFocus;
            float tangent=Mathf.Tan(portraitCamera.fieldOfView*Mathf.Deg2Rad*.5f);
            framedDistance=Mathf.Max(1,Mathf.Max(bounds.size.y/(2*tangent),bounds.size.x/(2*tangent*Mathf.Max(.1f,portraitCamera.aspect)))*1.25f+bounds.size.z*.5f);
            ResetView();
        }
        void ResetView(){focus=framedFocus;distance=framedDistance;yaw=155;pitch=10;}
        public void Draw(Rect r,RegimentRecord unit)
        {
            Configure(unit);lastDrawFrame=Time.frameCount;lastVisible=Time.realtimeSinceStartupAsDouble;
            EnsureTexture(r);portraitCamera.enabled=true;
            var e=Event.current;int control=GUIUtility.GetControlID(FocusType.Passive);
            if(e.type==EventType.MouseDown&&r.Contains(e.mousePosition)&&e.button<=2)
            {
                if(e.button==0&&e.clickCount==2)ResetView();
                else {dragControl=control;activeButton=e.button;GUIUtility.hotControl=control;}
                e.Use();
            }
            if(activeButton>=0&&GUIUtility.hotControl==dragControl)
            {
                if(e.type==EventType.MouseDrag)
                {
                    if(activeButton==0&&!e.shift){yaw+=e.delta.x*.65f;pitch=Mathf.Clamp(pitch-e.delta.y*.45f,-30,75);}
                    else focus+=portraitCamera.transform.right*(-e.delta.x*distance*.0018f)+portraitCamera.transform.up*(e.delta.y*distance*.0018f);
                    e.Use();
                }
                if(e.rawType==EventType.MouseUp){GUIUtility.hotControl=0;activeButton=-1;e.Use();}
            }
            if(e.type==EventType.ScrollWheel&&r.Contains(e.mousePosition))
            {distance=Mathf.Clamp(distance*Mathf.Exp(e.delta.y*.07f),framedDistance*.35f,framedDistance*2.5f);e.Use();}
            EmpireTheme.Panel(r,EmpireTheme.Ink);
            if(texture)GUI.DrawTexture(r,texture,ScaleMode.StretchToFill,false);
            if(!material&&cannon==null)CommandUI.Label(CommandUI.Inset(r,12),"Unit preview unavailable",13,true,false,true);
        }
        public void DrawControls(Rect r)
        {
            bool compact=r.width<260;
            if(compact)
            {
                CommandUI.Label(new Rect(r.x,r.y,r.width,22),caption,10,true);
                if(CommandUI.Button(new Rect(r.x,r.y+25,r.width/2-3,23),paused?"Play":"Pause"))TogglePause();
                if(CommandUI.Button(new Rect(r.x+r.width/2+3,r.y+25,r.width/2-3,23),"Reset"))ResetView();
                return;
            }
            CommandUI.Label(new Rect(r.x,r.y,r.width-142,23),caption,11,true);
            if(CommandUI.Button(new Rect(r.xMax-136,r.y,66,23),paused?"Play":"Pause"))
            {
                TogglePause();
            }
            if(CommandUI.Button(new Rect(r.xMax-64,r.y,64,23),"Reset"))ResetView();
            CommandUI.Label(new Rect(r.x,r.y+27,r.width,32),"Drag to rotate · right drag to pan · wheel to zoom",10,true,false,true);
        }
        void TogglePause()
        {
            if(paused){stepStart=Time.realtimeSinceStartupAsDouble-pauseElapsed;paused=false;}
            else {pauseElapsed=(float)(Time.realtimeSinceStartupAsDouble-stepStart);paused=true;}
        }
        void EnsureTexture(Rect r)
        {
            // Bound GPU memory; resize only when the pane changes size.
            int width=Mathf.Clamp(Mathf.RoundToInt(r.width),128,768),height=Mathf.Clamp(Mathf.RoundToInt(r.height),128,768);
            if(texture&&texture.width==width&&texture.height==height)return;
            ReleaseTexture();texture=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32){name="Regiment portrait",hideFlags=HideFlags.HideAndDontSave,antiAliasing=1};
            texture.Create();portraitCamera.targetTexture=texture;portraitCamera.aspect=width/(float)height;
            float tangent=Mathf.Tan(portraitCamera.fieldOfView*Mathf.Deg2Rad*.5f);
            // Portrait framing also accounts for wide mounted units and field pieces.
            float zoom=distance/Mathf.Max(.1f,framedDistance);
            framedDistance=Mathf.Max(1,Mathf.Max(modelBounds.size.y/(2*tangent),modelBounds.size.x/(2*tangent*portraitCamera.aspect))*1.25f+modelBounds.size.z*.5f);
            distance=framedDistance*zoom;
        }
        static Mesh Sample(Step step,float time,out float blend)
        {
            float phase=Mathf.Lerp(step.From,step.To,Mathf.Clamp01(time/Mathf.Max(.01f,step.Duration)));
            if(step.Morph){blend=Mathf.SmoothStep(0,1,phase);return step.Morph;}
            int frame=AnimationPlayback.Frame(step.Poses.Length,phase,step.Loop,out blend);return step.Poses[frame];
        }
        static Vector3[] Evaluated(Mesh mesh,float blend,bool normals)
        {
            var values=normals?mesh.normals:mesh.vertices;
            var next=new List<Vector3>();mesh.GetUVs(normals?3:2,next);
            if(next.Count==values.Length)for(int i=0;i<values.Length;i++)values[i]=normals?Vector3.Lerp(values[i],next[i],blend).normalized:Vector3.Lerp(values[i],next[i],blend);
            return values;
        }
        void BuildBridge(Step previous,Step next)
        {
            if(bridge){DestroyOwned(bridge);owned.Remove(bridge);bridge=null;}
            var from=Sample(previous,previous.Duration,out float a);var to=Sample(next,0,out float b);
            if(!from||!to||from.vertexCount!=to.vertexCount||!from.isReadable||!to.isReadable)return;
            bridge=Instantiate(from);bridge.name="Portrait pose handoff";bridge.hideFlags=HideFlags.HideAndDontSave;
            bridge.vertices=Evaluated(from,a,false);bridge.normals=Evaluated(from,a,true);
            bridge.SetUVs(2,new List<Vector3>(Evaluated(to,b,false)));bridge.SetUVs(3,new List<Vector3>(Evaluated(to,b,true)));
            var bounds=from.bounds;bounds.Encapsulate(to.bounds);bridge.bounds=bounds;owned.Add(bridge);
        }
        void LateUpdate()
        {
            if(lastDrawFrame<Time.frameCount-1)
            {
                portraitCamera.enabled=false;
                if(Time.realtimeSinceStartupAsDouble-lastVisible>2)Destroy(gameObject);
                return;
            }
            portraitCamera.transform.rotation=Quaternion.Euler(pitch,yaw,0);
            portraitCamera.transform.position=focus-portraitCamera.transform.forward*distance;
            diorama?.Draw(portraitCamera,PortraitLayer,groundPaint,horizonPaint,landmarkPaint,properties);
            if(cannon!=null)
            {
                DrawMesh(cannon.Wood,wood,0,false);DrawMesh(cannon.Iron,iron,0,false);DrawMesh(cannon.Bronze,bronze,0,false);return;
            }
            if(steps.Count==0||!material)return;
            float elapsed=paused?pauseElapsed:(float)(Time.realtimeSinceStartupAsDouble-stepStart);
            if(elapsed>=steps[stepIndex].Duration+Handoff)
            {
                var previous=steps[stepIndex];stepIndex=(stepIndex+1)%steps.Count;BuildBridge(previous,steps[stepIndex]);
                stepStart=Time.realtimeSinceStartupAsDouble;elapsed=0;if(paused)pauseElapsed=0;
            }
            caption=steps[stepIndex].Name;
            var mesh=elapsed<Handoff&&bridge?bridge:Sample(steps[stepIndex],Mathf.Max(0,elapsed-Handoff),out _);
            float blend;
            if(mesh==bridge)blend=Mathf.SmoothStep(0,1,elapsed/Handoff);
            else Sample(steps[stepIndex],Mathf.Max(0,elapsed-Handoff),out blend);
            DrawMesh(mesh,material,blend,true);
        }
        void DrawMesh(Mesh mesh,Material paint,float blend,bool morph)
        {
            if(!mesh||!paint)return;
            properties.Clear();properties.SetFloat("_PoseBlend",blend);properties.SetFloat("_Morph",morph?1:0);
            properties.SetVector("_RegimentUniform",morph?uniform:Vector4.zero);
            Graphics.DrawMesh(mesh,Matrix4x4.identity,paint,PortraitLayer,portraitCamera,0,properties,ShadowCastingMode.Off,false,null,LightProbeUsage.Off);
        }
        void ReleaseTexture()
        {
            if(!texture)return;
            if(portraitCamera)portraitCamera.targetTexture=null;
            texture.Release();DestroyOwned(texture);texture=null;
        }
        static void DestroyOwned(UnityEngine.Object item){if(Application.isPlaying)Destroy(item);else DestroyImmediate(item);}
        void ClearOwned(){foreach(var item in owned)if(item)DestroyOwned(item);owned.Clear();}
        void OnDestroy()
        {
            if(activeButton>=0&&GUIUtility.hotControl==dragControl)GUIUtility.hotControl=0;
            ReleaseTexture();ClearOwned();
        }
    }
}
