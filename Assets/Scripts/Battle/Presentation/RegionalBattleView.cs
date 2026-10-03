using Empire.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireBattle
{
    public sealed class RegionalBattleView : MonoBehaviour
    {
        BattleWorld world;
        Camera cameraView;
        Mesh cube;
        Material stone,wood,water;
        StandardCloth objectiveFlag;
        LineRenderer deployment,objective;
        public void Initialize(BattleWorld world,Camera camera,SceneryImpactMap surfaces)
        {
            this.world=world;cameraView=camera;
            if(world.Terrain.Profile==null&&!world.DeploymentActive)return;
            var temporary=GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube=temporary.GetComponent<MeshFilter>().sharedMesh;temporary.SetActive(false);Destroy(temporary);
            var shader=Resources.Load<Shader>("RuralSurface");
            stone=new Material(shader);stone.SetColor("_BaseColor",new Color(.51f,.49f,.43f));
            wood=new Material(shader);wood.SetColor("_BaseColor",new Color(.34f,.21f,.12f));
            water=new Material(shader);water.SetColor("_BaseColor",new Color(.14f,.31f,.39f));
            if(world.Terrain.Profile?.Settlement==true)objectiveFlag=new StandardCloth(4.8f,2.8f,Color.white,false,false,93,11.6f);
            deployment=Outline("Deployment area",129,world.Config.PlayerCountryColor);
            Bounds area=world.DeploymentArea(ArmySide.French);
            Vector2[] corners={new Vector2(area.min.x,area.min.z),new Vector2(area.max.x,area.min.z),new Vector2(area.max.x,area.max.z),new Vector2(area.min.x,area.max.z)};
            for(int i=0;i<129;i++)
            {
                int edge=(i/32)%4;float phase=(i%32)/32f;
                deployment.SetPosition(i,world.Terrain.SurfacePoint(Vector2.Lerp(corners[edge],corners[(edge+1)%4],phase),.6f));
            }
            var map=world.Terrain.Profile;if(map==null)return;
            foreach(var wall in world.Terrain.Walls)
            {
                var root=world.Terrain.SurfacePoint(wall.Center);
                float height=5+map.FortLevel*1.5f;
                surfaces.Register(new Bounds(root+Vector3.up*(height*.5f),new Vector3(wall.Half.x*2,height,wall.Half.y*2)),SmallArmsSurface.Masonry);
            }
            if(map.Settlement)
            {
                objective=Outline("Town square objective",49,new Color(.94f,.76f,.30f));
                for(int i=0;i<49;i++)
                {float angle=i/48f*Mathf.PI*2;objective.SetPosition(i,world.Terrain.SurfacePoint(map.SettlementCenter+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*BattleSiege.CaptureRadius,.35f));}
            }
        }
        LineRenderer Outline(string name,int count,Color color)
        {
            var go=new GameObject(name);go.transform.SetParent(transform);
            var line=go.AddComponent<LineRenderer>();line.positionCount=count;line.useWorldSpace=true;
            line.material=new Material(Resources.Load<Shader>("RuralSurface"));line.material.SetColor("_BaseColor",color);
            line.startWidth=line.endWidth=1.3f;line.shadowCastingMode=ShadowCastingMode.Off;return line;
        }
        void Part(Material material,Vector3 p,Vector3 size)
        {Graphics.DrawMesh(cube,Matrix4x4.TRS(p,Quaternion.identity,size),material,0,cameraView,0,null,ShadowCastingMode.On,true);}
        void LateUpdate()
        {
            if(world==null||!cube)return;
            if(deployment)deployment.enabled=world.DeploymentActive;
            var map=world.Terrain.Profile;if(map==null)return;
            if(map.Coast)
            {
                float half=world.Config.FieldHalfSize;
                Part(water,new Vector3(-half*.91f,-.65f,0),new Vector3(half*.18f,.1f,half*2));
            }
            float height=5+map.FortLevel*1.5f;
            foreach(var wall in world.Terrain.Walls)
                Part(stone,world.Terrain.SurfacePoint(wall.Center,height*.5f),new Vector3(wall.Half.x*2,height,wall.Half.y*2));
            if(map.FortLevel>0&&!world.Terrain.GateOpen)
                Part(wood,world.Terrain.SurfacePoint(map.Gate,height*.5f),new Vector3(140,height,4));
            if(map.Settlement)
            {
                Vector3 root=world.Terrain.SurfacePoint(map.SettlementCenter);
                Part(wood,root+Vector3.up*6,new Vector3(.4f,12,.4f));
                Color defender=map.DefenderSide==ArmySide.French?world.Config.PlayerCountryColor:world.Config.EnemyCountryColor;
                Color attacker=map.DefenderSide==ArmySide.French?world.Config.EnemyCountryColor:world.Config.PlayerCountryColor;
                objectiveFlag.Tint(Color.Lerp(defender,attacker,(world.Siege?.CaptureProgress??0)/BattleSiege.CaptureDuration));
                objectiveFlag.Advance(world.Time,Quaternion.identity,false);
                objectiveFlag.Draw(Matrix4x4.TRS(root+new Vector3(.08f,0,-.37f),Quaternion.identity,Vector3.one),cameraView);
            }
        }
        void OnGUI()
        {
            if(world==null||GetComponent<BattleView>()?.EnableInput==false||!world.DeploymentActive&&world.Siege==null)return;
            using var scope=new EmpireTheme.Scope(true);
            float x=(Screen.width-560)*.5f;
            EmpireTheme.Panel(new Rect(x,124,560,world.DeploymentActive?105:64),EmpireTheme.Ink);
            var map=world.Terrain.Profile;
            GUI.Label(new Rect(x+12,130,530,24),world.DeploymentActive?"ARMY DEPLOYMENT":"TOWN OBJECTIVE");
            if(world.DeploymentActive)
            {
                if(GUI.Button(new Rect(x+398,157,150,30),"BEGIN BATTLE",EmpireTheme.ButtonStyle()))world.BeginBattle(out _);
                GUI.Label(new Rect(x+12,192,530,25),world.DeploymentMessage);
            }
            else if(world.Siege!=null)
            {
                string status=world.Siege.Contested?"Town square contested":$"Town capture: {world.Siege.CaptureProgress:0} / {BattleSiege.CaptureDuration:0} seconds";
                if(map.FortLevel>0)status+=" · "+(world.Terrain.GateOpen?"Gate breached":$"Gate strength: {world.Terrain.GateHealth:0}");
                GUI.Label(new Rect(x+12,157,530,24),status);
            }
        }
        void OnDestroy()
        {
            objectiveFlag?.Dispose();
            foreach(var material in new[]{stone,wood,water,deployment?deployment.sharedMaterial:null,objective?objective.sharedMaterial:null})if(material)Destroy(material);
        }
    }
}
