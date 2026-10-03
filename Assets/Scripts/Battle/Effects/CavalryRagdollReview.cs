using System;
using System.Collections;
using System.IO;
using UnityEngine;
namespace EmpireBattle
{
    // Opt-in QA scene only; exercises the real casualty pool and cavalry renderer in a native player.
    public sealed class CavalryRagdollReview : MonoBehaviour
    {
        BattleWorld world;BattleCasualtyView casualties;CavalryView cavalry;Camera cameraView;
        readonly Plane[] planes=new Plane[6];
        void Require(bool value,string message){if(!value)throw new Exception("Mounted ragdoll review: "+message);}
        void LateUpdate(){if(cavalry!=null){GeometryUtility.CalculateFrustumPlanes(cameraView,planes);cavalry.Render(planes,world.Time);}}
        IEnumerator Start()
        {
            string output=Path.Combine(Application.dataPath,"../../../../../Artifacts/CavalryMotionReview/Native");
            var args=Environment.GetCommandLineArgs();int option=Array.IndexOf(args,"--cavalry-review-output");if(option>=0&&option+1<args.Length)output=args[option+1];
            Directory.CreateDirectory(output);
            world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=20,FrenchCavalry=1,BritishCavalry=1,FrenchArtillery=0,BritishArtillery=0,EnableLandscape=false,EnableAI=false,InitialSeparation=50,FieldHalfSize=128});
            cameraView=new GameObject("Ragdoll review camera").AddComponent<Camera>();cameraView.transform.position=new Vector3(14,10,-18);cameraView.transform.LookAt(new Vector3(0,1,4));cameraView.fieldOfView=52;cameraView.nearClipPlane=.1f;cameraView.farClipPlane=100;cameraView.backgroundColor=new Color(.35f,.43f,.53f);cameraView.clearFlags=CameraClearFlags.SolidColor;
            var sun=new GameObject("Ragdoll review sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=2;sun.transform.rotation=Quaternion.Euler(45,-30,0);sun.shadows=LightShadows.Soft;RenderSettings.ambientLight=Color.gray;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.transform.localScale=Vector3.one*8;var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.color=new Color(.29f,.34f,.23f);floor.GetComponent<Renderer>().sharedMaterial=material;
            casualties=gameObject.AddComponent<BattleCasualtyView>();casualties.Initialize(world,cameraView);cavalry=new CavalryView(world,cameraView){Casualties=casualties};
            yield return null;
            int start=world.Battalions[1].SoldierStart;float[] speeds={0,2.2f,4.2f,7.6f};
            for(int i=0;i<4;i++)
            {
                int soldier=start+i;world.Soldiers.Positions[soldier]=new Vector2(-9+i*6,0);world.Soldiers.Facings[soldier]=0;world.Soldiers.Velocities[soldier]=Vector2.up*speeds[i];world.Soldiers.MovementSpeeds[soldier]=speeds[i];
                world.ApplyWound(soldier,200,DeathCause.Musket,Vector3.zero);Require(casualties.IsPhysical(soldier),"Every close-up shot rider uses a physical ragdoll");
            }
            int british=world.Battalions[3].SoldierStart;
            for(int i=0;i<2;i++)
            {
                int soldier=british+i;world.Soldiers.Positions[soldier]=new Vector2(i==0?-9:9,8);world.Soldiers.Facings[soldier]=Mathf.PI;
                world.Soldiers.Velocities[soldier]=Vector2.down*7.6f;world.Soldiers.MovementSpeeds[soldier]=7.6f;
                world.ApplyArtilleryHit(soldier,200,i==0?DeathCause.Roundshot:DeathCause.Shell,Vector3.forward*200,world.Terrain.SurfacePoint(world.Soldiers.Positions[soldier]));
                Require(casualties.IsPhysical(soldier),"Mounted artillery deaths use bounded physical launches");
            }
            Require(casualties.ActiveRagdolls==6,"Six physical riders, no duplicate fallback bodies");
            int capture=0;float[] moments={0,.25f,.5f,1,2,5};
            for(int tick=0;tick<=1200;tick++)
            {
                if(capture<moments.Length&&world.Time>=moments[capture])
                {
                    yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,$"riders-{capture}-{moments[capture]:F2}.png"));capture++;
                    yield return null;
                }
                world.StepMovement(.025f);casualties.Step(.025f);
                if(tick%10==0)yield return null;
            }
            Require(casualties.FrozenRagdolls==6&&casualties.ActiveRagdolls==0,"All riders settle into persistent corpses");
            for(int i=0;i<4;i++){Require(casualties.HasPhysicalCorpse(start+i),"Frozen rider suppresses baked duplicate");Require(casualties.BloodAnchor(start+i).y<1,"Rider rests on the ground");}
            for(int i=0;i<2;i++)Require(Vector3.Distance(casualties.BloodAnchor(british+i),world.Deaths[british+i].Position)<12,"Artillery ragdoll settles near the hit instead of flying backward");
            Require(casualties.ValidateCorpses(out string issue),issue);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"riders-settled.png"));yield return null;
            // Opposing appearance swaps and a burst beyond the physics budget must keep all casualties.
            for(int i=0;i<40;i++)
            {
                int soldier=british+2+i;world.Soldiers.Positions[soldier]=new Vector2(-10+i*.5f,5);world.Soldiers.Facings[soldier]=Mathf.PI;world.Soldiers.Velocities[soldier]=Vector2.down*7.6f;
                world.ApplyWound(soldier,200,DeathCause.Musket,Vector3.zero);
            }
            Require(casualties.ActiveRagdolls==32&&world.DeathCount==46,"Physics pool remains bounded and all forty burst casualties are retained");
            int physical=0;for(int i=0;i<40;i++)if(casualties.HasPhysicalCorpse(british+2+i))physical++;
            Require(physical==32,"Eight overflow riders use animated fallback");
            File.WriteAllText(Path.Combine(output,"result.json"),"{\"passed\":true,\"shotRiders\":4,\"settledRiders\":6,\"burstDeaths\":40,\"physicsLimit\":32,\"fallbackDeaths\":8}");
            Debug.Log("MOUNTED_RAGDOLL_NATIVE_PASSED shot riders at idle/walk/trot/gallop, bounded roundshot/shell reactions, grounded persistent corpses, both factions, physics budget and fallback");Application.Quit();
        }
    }
}
