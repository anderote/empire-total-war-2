using System;
using System.Collections.Generic;
using Empire.UI;
using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class BattleView
    {
        ArtillerySystem commandArtillery;
        GUIStyle gunButtonStyle, ammoButtonStyle, gunStatsStyle;
        bool gunPanelPointer, gunBombardOrder;
        bool GunBombardModifier => gunBombardOrder||Input.GetKey(KeyCode.LeftAlt)||Input.GetKey(KeyCode.RightAlt);
        const float BombardDragThresholdPixels=18f;
        sealed class BombardQueueEntry
        {
            public readonly Vector2 Start,End;
            public readonly bool IsLine;
            public readonly Vector2[] Targets;
            public readonly bool[] Included;
            public BombardQueueEntry(Vector2 start,Vector2 end,bool isLine,Vector2[] targets,bool[] included)
            {Start=start;End=end;IsLine=isLine;Targets=targets;Included=included;}
        }
        readonly List<BombardQueueEntry> bombardQueue=new List<BombardQueueEntry>();
        readonly HashSet<int> bombardQueueGunIds=new HashSet<int>();
        bool bombardQueueActive;
        int bombardQueueIndex;
        bool[] bombardStepParticipants;
        int[] bombardStepShots;
        bool[] selectedGuns=Array.Empty<bool>();
        readonly bool[][] gunGroups=new bool[9][];
        float GunUIScale => BattleUILayout.Scale(Screen.width,Screen.height);
        Rect GunLogicalRect => BattleUILayout.Battery(Screen.width/GunUIScale,Screen.height/GunUIScale,SelectedRegimentCount>0);
        Rect GunPanelRect => BattleUILayout.Physical(GunLogicalRect,GunUIScale);
        public int SelectedGunCount { get {int count=0;foreach(bool picked in selectedGuns)if(picked)count++;return count;} }
        public int SelectedArtilleryUnitCount
        {
            get
            {
                int count=0,lastUnit=-1;
                ForSelectedGun(g=>{if(g.UnitId!=lastUnit){count++;lastUnit=g.UnitId;}});
                return count;
            }
        }
        public void InitializeArtilleryCommands(ArtillerySystem system)
        {
            commandArtillery=system;selectedGuns=new bool[system.Guns.Length];
            for(int i=0;i<gunGroups.Length;i++)gunGroups[i]=new bool[selectedGuns.Length];
        }
        void ClearSelection()
        {Array.Clear(selected,0,selected.Length);Array.Clear(selectedGuns,0,selectedGuns.Length);}
        public void SelectGun(int id,bool additive)
        {
            if(commandArtillery==null||id<0||id>=selectedGuns.Length||commandArtillery.Guns[id].Side!=ArmySide.French)return;
            bool pick=!additive||!selectedGuns[id];
            if(!additive)ClearSelection();
            SelectArtilleryUnit(commandArtillery.Guns[id].UnitId,pick);
        }
        void SelectArtilleryUnit(int unitId,bool pick)
        {
            foreach(var gun in commandArtillery.Guns)
                if(gun.Side==ArmySide.French&&gun.UnitId==unitId)selectedGuns[gun.Id]=pick;
        }
        void ForSelectedGun(Action<ArtilleryGun> action)
        {if(commandArtillery!=null)foreach(var gun in commandArtillery.Guns)if(selectedGuns[gun.Id])action(gun);}
        List<ArtilleryGun> SelectedOperationalGunsStable()
        {
            var result=new List<ArtilleryGun>();
            ForSelectedGun(g=>{if(g.Operational&&g.Ammunition>0)result.Add(g);});
            result.Sort((a,b)=>{int unit=a.UnitId.CompareTo(b.UnitId);if(unit!=0)return unit;int x=a.Position.x.CompareTo(b.Position.x);if(x!=0)return x;int y=a.Position.y.CompareTo(b.Position.y);return y!=0?y:a.Id.CompareTo(b.Id);});
            return result;
        }
        internal static float BombardLineFraction(int index,int count)
            =>count<=1 ? .5f : (Mathf.Clamp(index,0,count-1)+.5f)/count;
        internal static Vector2 BombardLineAimPoint(Vector2 start,Vector2 end,int index,int count)
            =>Vector2.Lerp(start,end,BombardLineFraction(index,count));
        void SelectAllGuns()
        {if(commandArtillery!=null)foreach(var gun in commandArtillery.Guns)selectedGuns[gun.Id]=gun.Side==ArmySide.French;}
        void SaveGunGroup(int group)
        {if(commandArtillery!=null)Array.Copy(selectedGuns,gunGroups[group],selectedGuns.Length);}
        void RecallGunGroup(int group)
        {if(commandArtillery!=null)Array.Copy(gunGroups[group],selectedGuns,selectedGuns.Length);}
        Vector3 GunScreenPoint(ArtilleryGun gun) => battleCamera.WorldToScreenPoint(world.Terrain.SurfacePoint(gun.Position,2.5f));
        int PickGun(Vector2 mouse)
        {
            if(commandArtillery==null)return -1;
            int closest=-1;float best=22f;
            foreach(var gun in commandArtillery.Guns)
            {
                if(gun.Side!=ArmySide.French)continue;
                Vector3 point=GunScreenPoint(gun);float delta=Vector2.Distance(mouse,point);
                if(point.z>0&&delta<best){closest=gun.Id;best=delta;}
            }
            if(closest<0&&GroundPoint(mouse,out var ground))
                foreach(var gun in commandArtillery.Guns)
                    if(gun.Side==ArmySide.French&&Vector2.Distance(ground,gun.Position)<3f)return gun.Id;
            return closest;
        }
        void SelectGunsInRect(Rect rect)
        {
            if(commandArtillery==null)return;
            foreach(var gun in commandArtillery.Guns)
            {var point=GunScreenPoint(gun);if(gun.Side==ArmySide.French&&point.z>0&&new Rect(point.x-10,point.y-10,20,20).Overlaps(rect))SelectArtilleryUnit(gun.UnitId,true);}
        }
        public void OrderSelectedGuns(int target,Vector2 ground,bool bombard=false)
        {
            if(SelectedGunCount==0)return;
            if(target<0&&!bombard&&commandArtillery.BatteryAt(ArmySide.French,ground)<0)
            { MoveSelectedGuns(ground);return; }
            int count=0,waiting=0,batteries=0;
            ForSelectedGun(g=>
            {
                bool accepted=target>=0?commandArtillery.OrderTarget(g.Id,target):commandArtillery.OrderGround(g.Id,ground);
                if(!accepted)return;count++;if(g.TargetBattery>=0)batteries++;if(!commandArtillery.TryAimPoint(g,out _))waiting++;
            });
            if(count>0)status=$"{count} guns: "+(batteries>0?"targeting individual enemy cannons.":target>=0?"tracking enemy formation.":"bombarding ground position.")
                +(waiting>0?$" {waiting} waiting for a target inside ammunition range.":"");
        }
        public void OrderSelectedGunsAlongLine(int target,Vector2 start,Vector2 end,bool isLine,bool append)
        {
            if(SelectedGunCount==0)return;
            if(target>=0){CancelBombardSequence(true);OrderSelectedGuns(target,end,false);return;}
            var guns=SelectedOperationalGunsStable();if(guns.Count==0){status="No operational guns with ammunition can bombard.";return;}
            if(!append)CancelBombardSequence(true);
            var targets=new Vector2[commandArtillery.Guns.Length];var included=new bool[targets.Length];
            for(int i=0;i<guns.Count;i++)
            {
                var point=BombardLineAimPoint(start,isLine?end:start,i,guns.Count);
                targets[guns[i].Id]=point;included[guns[i].Id]=true;
            }
            bombardQueue.Add(new BombardQueueEntry(start,isLine?end:start,isLine,targets,included));
            foreach(var gun in guns)bombardQueueGunIds.Add(gun.Id);
            if(!bombardQueueActive){bombardQueueIndex=0;BeginBombardQueueEntry();}
            if(!bombardQueueActive&&bombardQueue.Count==0){status="No selected guns can reach that target.";return;}
            status=append?$"Queued bombardment location {bombardQueue.Count}. Each salvo waits for all eligible guns.":
                bombardQueue.Count>1?$"Bombardment sequence started · {bombardQueue.Count} locations.":"Bombardment ordered.";
        }
        void CancelBombardSequence(bool holdGuns)
        {
            if(commandArtillery!=null)
                foreach(int id in bombardQueueGunIds)commandArtillery.ClearTarget(id,!holdGuns);
            bombardQueue.Clear();bombardQueueGunIds.Clear();bombardQueueActive=false;bombardQueueIndex=0;
            bombardStepParticipants=null;bombardStepShots=null;
        }
        void BeginBombardQueueEntry()
        {
            while(true)
            {
                if(bombardQueueIndex>=bombardQueue.Count)
                {
                    foreach(int id in bombardQueueGunIds)commandArtillery.ClearTarget(id,false);
                    bombardQueue.Clear();bombardQueueGunIds.Clear();bombardQueueActive=false;
                    bombardStepParticipants=null;bombardStepShots=null;status="Bombardment sequence complete.";return;
                }
                var entry=bombardQueue[bombardQueueIndex];bombardStepParticipants=new bool[selectedGuns.Length];bombardStepShots=new int[selectedGuns.Length];
                bool any=false;
                for(int id=0;id<entry.Included.Length;id++)if(entry.Included[id])
                {
                    var gun=commandArtillery.Guns[id];if(!gun.Operational||gun.Ammunition<=0||gun.Morale<25f)continue;
                    if(!commandArtillery.OrderGround(id,entry.Targets[id]))continue;
                    bombardStepShots[id]=gun.ShotsFired;
                    bombardStepParticipants[id]=commandArtillery.TryAimPoint(gun,out _);
                    any|=bombardStepParticipants[id];
                }
                bombardQueueActive=any;
                if(any)return;
                bombardQueueIndex++;
            }
        }
        void UpdateBombardSequence()
        {
            if(!bombardQueueActive||commandArtillery==null)return;
            bool waiting=false;
            for(int id=0;id<bombardStepParticipants.Length;id++)if(bombardStepParticipants[id])
            {
                var gun=commandArtillery.Guns[id];
                if(gun.ShotsFired>bombardStepShots[id])continue;
                if(!gun.Operational||gun.Ammunition<=0)continue;
                waiting=true;
            }
            if(waiting)return;
            bombardQueueActive=false;bombardQueueIndex++;BeginBombardQueueEntry();
        }
        void RenderBombardQueuePreview()
        {
            if(commandArtillery==null||AtlasBlend>=.5f)return;
            for(int n=0;n<bombardQueue.Count;n++)
            {
                var entry=bombardQueue[n];var color=n%2==0?attackMarker:marker;
                if(entry.IsLine)GroundOrderLine(entry.Start,entry.End,color);
                foreach(var gun in commandArtillery.Guns)if(entry.Included[gun.Id])
                    DrawBombardAimPoint(entry.Targets[gun.Id],WithinBombardRange(gun,entry.Targets[gun.Id])?color:attackMarker);
            }
            if(ordering&&GunBombardModifier&&SelectedGunCount>0&&GroundPoint(Input.mousePosition,out var cursor))
            {
                Vector2 from=moveDestination;bool line=Vector2.Distance(Input.mousePosition,rightStart)>=BombardDragThresholdPixels;
                if(line)GroundOrderLine(from,cursor,marker);
                var guns=SelectedOperationalGunsStable();
                for(int i=0;i<guns.Count;i++)
                {
                    Vector2 point=line?BombardLineAimPoint(from,cursor,i,guns.Count):cursor;
                    DrawBombardAimPoint(point,WithinBombardRange(guns[i],point)?marker:attackMarker);
                }
            }
        }
        static bool WithinBombardRange(ArtilleryGun gun,Vector2 point)
        {float distance=Vector2.Distance(gun.Position,point);return distance>=8f&&distance<=ArtillerySystem.EffectiveRange(gun);}
        void DrawBombardAimPoint(Vector2 point,Material color)
        {
            GroundOrderLine(point-new Vector2(2.4f,0),point+new Vector2(2.4f,0),color);
            GroundOrderLine(point-new Vector2(0,2.4f),point+new Vector2(0,2.4f),color);
        }
        public void MoveSelectedGuns(Vector2 destination)
        {
            Vector2 center=Vector2.zero;int living=0;
            ForSelectedGun(g=>{if(g.Operational){center+=g.Position;living++;}});
            if(living==0){status="No crewed guns can move.";return;}
            center/=living;int moved=0;
            ForSelectedGun(g=>{if(commandArtillery.OrderMove(g.Id,destination+g.Position-center,g.Facing))moved++;});
            status=moved>0?$"{moved} guns: "+(world.DeploymentActive?"redeployed.":"relocating with their crews; setting up on arrival."):
                "No clear artillery route. Choose open ground inside the field or deployment area.";
        }
        public void SetSelectedAmmunition(ArtilleryAmmunition type)
        {
            bool allowed=SelectedGunCount>0;ForSelectedGun(g=>{if(!commandArtillery.CanUseAmmunition(g,type))allowed=false;});
            if(!allowed){status="Learn Artillery munitions rank "+(int)type+" on every selected battery to use "+type+".";return;}
            ForSelectedGun(g=>commandArtillery.SetAmmunition(g.Id,type));status=type switch {
                ArtilleryAmmunition.Roundshot => "Roundshot: solid shot pierces ranks and skips across ground.",
                ArtilleryAmmunition.Canister => "Canister: close-range spread; distant hits can be survived.",
                ArtilleryAmmunition.Explosive => "Shell: local blast; wounded survivors recover and reform.",
                ArtilleryAmmunition.Carcass => "Carcass: blast followed by 90 seconds of fire across an 18 m radius.",
                ArtilleryAmmunition.PoisonGas => "Poison gas: lethal cloud; surviving exposed units move slower for the rest of the battle.",
                _ => "Lime: damaging cloud; exposed units lose accuracy for the rest of the battle." };}
        void ToggleGunFire()
        {
            bool enable=false;ForSelectedGun(g=>{if(!g.FireAtWill)enable=true;});
            ForSelectedGun(g=>g.FireAtWill=enable&&g.Operational);
        }
        void DrawGunControls()
        {
            if(commandArtillery==null)return;
            gunPanelPointer=SelectedGunCount>0&&GunPanelRect.Contains(Event.current.mousePosition);
            int count=SelectedGunCount;if(count==0)return;
            if(gunButtonStyle==null)
            {
                gunButtonStyle=new GUIStyle(EmpireTheme.ButtonStyle(13)){padding=new RectOffset(4,4,3,3)};
                ammoButtonStyle=new GUIStyle(gunButtonStyle);
                gunStatsStyle=EmpireTheme.Text(12,EmpireTheme.Muted);
            }
            Matrix4x4 previousMatrix=GUI.matrix;
            bool enabled=GUI.enabled;
            try
            {
                GUI.matrix=previousMatrix*Matrix4x4.Scale(new Vector3(GunUIScale,GunUIScale,1));
                var panel=GunLogicalRect;EmpireTheme.Panel(panel,EmpireTheme.Ink);
                EmpireTheme.Fill(new Rect(panel.x+1,panel.y+1,panel.width-2,2),EmpireTheme.Brass);
                int ammo=0,ready=0,waiting=0,held=0,tracking=0,ground=0,disabled=0,suppressed=0,crew=0,moving=0;
                ForSelectedGun(g=>{ammo+=g.Ammunition;crew+=g.LivingCrew;if(g.Relocating)moving++;if(!g.Operational)disabled++;if(g.Suppressed)suppressed++;if(g.Operational&&!g.Suppressed&&!g.Relocating&&g.Phase==ArtilleryPhase.Ready)ready++;if(!g.FireAtWill)held++;
                    if(g.TargetBattalion>=0||g.TargetBattery>=0)tracking++;if(g.HasGroundTarget)ground++;
                    if((g.TargetBattalion>=0||g.TargetBattery>=0||g.HasGroundTarget)&&!commandArtillery.TryAimPoint(g,out _))waiting++;});
                float x=panel.x+12,y=panel.y+50;
                float minimumRange=float.MaxValue,maximumRange=0;
                ForSelectedGun(g=>{float range=ArtillerySystem.EffectiveRange(g);minimumRange=Mathf.Min(minimumRange,range);maximumRange=Mathf.Max(maximumRange,range);});
                GUI.Label(new Rect(x,panel.y+6,820,20),$"{SelectedArtilleryUnitCount} BATTERIES · {count-disabled}/{count} GUNS · {crew} crew · {ammo:N0} rounds",textStyle);
                string rangeLabel=Mathf.Approximately(minimumRange,maximumRange)?$"{minimumRange:0} m":$"{minimumRange:0}–{maximumRange:0} m";
                GUI.Label(new Rect(x,panel.y+27,820,18),$"{ready} ready · {moving} moving/setup · {disabled} disabled · {suppressed} suppressed · {waiting} out of range · Range {rangeLabel}",gunStatsStyle);
                GUI.enabled=enabled&&EnableInput;
                if(EmpireTheme.Button(new Rect(x,y,204,24),"FIRE / HOLD",gunButtonStyle,held==0))ToggleGunFire();
                if(GUI.Button(new Rect(x+210,y,204,24),"HALT",gunButtonStyle)){CancelBombardSequence(true);gunBombardOrder=false;ForSelectedGun(g=>commandArtillery.ClearTarget(g.Id,false));}
                if(GUI.Button(new Rect(x+420,y,204,24),"AUTO TARGET",gunButtonStyle)){CancelBombardSequence(true);gunBombardOrder=false;ForSelectedGun(g=>commandArtillery.ClearTarget(g.Id,true));}
                if(EmpireTheme.Button(new Rect(x+630,y,204,24),"BOMBARD GROUND",gunButtonStyle,gunBombardOrder))
                {gunBombardOrder=!gunBombardOrder;status=gunBombardOrder?"Right-click for a point; right-drag for a line. Shift appends locations; Escape cancels.":"Right-click open ground to move; an enemy to attack. Alt-right-click bombards ground.";}
                string[] names={"ROUND SHOT","CANISTER","SHELL","CARCASS","LIME","POISON GAS"};
                for(int i=0;i<names.Length;i++)
                {
                    var type=(ArtilleryAmmunition)i;bool all=true,unlocked=true;ForSelectedGun(g=>{if(g.AmmunitionType!=type)all=false;if(!commandArtillery.CanUseAmmunition(g,type))unlocked=false;});
                    GUI.enabled=enabled&&EnableInput&&unlocked;
                    if(EmpireTheme.Button(new Rect(x+i*(840f/names.Length),y+28,840f/names.Length-6,24),names[i]+(unlocked?"":" / rank "+i),ammoButtonStyle,all))SetSelectedAmmunition(type);
                }
            }
            finally{GUI.matrix=previousMatrix;GUI.enabled=enabled;}
        }
        void RenderGunOrders(bool showAll=false)
        {
            if(commandArtillery==null)return;
            foreach(var g in commandArtillery.Guns)
            {
                bool ordered=g.Moving||g.TargetBattalion>=0||g.TargetBattery>=0||g.HasGroundTarget;
                if(g.Side!=ArmySide.French||(!selectedGuns[g.Id]&&!(showAll&&ordered)))continue;
                if(selectedGuns[g.Id])tacticalMarkers.DrawArtilleryRange(g);
                Vector2 p=g.Position;
                GroundOrderLine(p+new Vector2(-3,-3),p+new Vector2(3,-3));
                GroundOrderLine(p+new Vector2(3,-3),p+new Vector2(3,3));
                GroundOrderLine(p+new Vector2(3,3),p+new Vector2(-3,3));
                GroundOrderLine(p+new Vector2(-3,3),p+new Vector2(-3,-3));
                if(!ordered)continue;
                Vector2 aim;
                if(g.Moving)aim=g.Destination;
                else if(g.TargetBattery>=0){if(!commandArtillery.TryAimPoint(g,out aim))continue;}
                else aim=g.HasGroundTarget?g.GroundTarget:world.Battalions[g.TargetBattalion].Position;
                for(int segment=0;segment<20;segment++)
                    GroundOrderLine(Vector2.Lerp(p,aim,segment/20f),Vector2.Lerp(p,aim,(segment+.55f)/20f),g.Moving?marker:attackMarker);
                GroundOrderLine(aim-new Vector2(3,0),aim+new Vector2(3,0),g.Moving?marker:attackMarker);
                GroundOrderLine(aim-new Vector2(0,3),aim+new Vector2(0,3),g.Moving?marker:attackMarker);
            }
        }
        void GroundOrderLine(Vector2 from,Vector2 to,Material color=null)
        {
            Vector3 a=world.Terrain.SurfacePoint(from,.2f),b=world.Terrain.SurfacePoint(to,.2f),delta=b-a;
            if(delta.sqrMagnitude<.0001f)return;
            float width=ProjectileVisibility.Width(battleCamera,(a+b)*.5f,1.2f,.12f,1f);
            Graphics.DrawMesh(cubeMesh,Matrix4x4.TRS((a+b)*.5f,Quaternion.LookRotation(delta),new Vector3(width,.08f,delta.magnitude)),color?color:marker,0,battleCamera);
        }
    }
}
