using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EmpireBattle;
using EmpireBattle.Art;
using EmpireBattle.AnimationAuthoring;
using UnityEditor;
using UnityEngine;
using HorseArt=EmpireBattle.SupportingArt.ProceduralSupportingArt;

public static partial class InfantryAssetBuild
{
    public static void BakeMeleeAndCavalry()
    {
        prepared=true;const string output="Assets/Resources/BakedInfantry";
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");library.hideFlags=HideFlags.DontUnloadUnusedAsset;
        var report=new StringBuilder();
        var kinds=new[]{InfantryMotionKind.MeleeReady,InfantryMotionKind.Thrust,InfantryMotionKind.Parry,InfantryMotionKind.ButtStrike,InfantryMotionKind.Stagger,InfantryMotionKind.Retreat};
        var actions=new[]{MeleeAction.Ready,MeleeAction.Thrust,MeleeAction.Parry,MeleeAction.ButtStrike,MeleeAction.Stagger,MeleeAction.Retreat};
        for(int level=0;level<2;level++)
        {
            BuildScene(false,level==0?3:5);var inspect=UnityEngine.Object.FindFirstObjectByType<InfantryInspection>();
            for(int side=0;side<2;side++)
            {
                var rig=inspect.Soldiers[side];var lod=(side==0?library.French:library.British).Lods[level];var rects=new Dictionary<string,Rect>();
                foreach(var r in rig.GetComponentsInChildren<Renderer>())if(r.enabled)foreach(var m in r.sharedMaterials)rects[m.name]=new Rect(0,0,1,1);
                lod.Melee=new InfantryMeleeClip[kinds.Length];
                for(int action=0;action<kinds.Length;action++)
                {
                    var clip=new InfantryMeleeClip{Action=actions[action]};lod.Melee[action]=clip;
                    for(int knee=0;knee<2;knee++)
                    {
                        var poses=BakePoses(rig,inspect.Clips[0],16,rects,output,level,report,kinds[action],knee==1);
                        foreach(var mesh in poses){if(mesh.vertexCount!=lod.Idle[0].vertexCount)throw new Exception("Melee topology changed");mesh.uv=lod.Idle[0].uv;EditorUtility.SetDirty(mesh);}
                        if(knee==0)clip.Poses=poses;else clip.Kneeling=poses;
                    }
                }
            }
        }
        BuildScene(false,3);library.hideFlags=HideFlags.None;EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
        File.WriteAllText("Logs/infantry-melee-bake.txt",report.ToString());InfantryPoseBlendBuild.Prepare();
        BakeMountedLibrary();
    }
    static bool mountedTurnsOnly,mountedCasualtiesOnly;
    public static void BakeMountedCasualties(){mountedCasualtiesOnly=true;try{BakeMountedLibrary();}finally{mountedCasualtiesOnly=false;}}
    public static void BakeMountedTurns(){mountedTurnsOnly=true;try{BakeMountedLibrary();}finally{mountedTurnsOnly=false;}}
    public static void BakeMountedLibrary()
    {
        prepared=true;const string output="Assets/Resources/BakedCavalry";Directory.CreateDirectory(output);
        var library=AssetDatabase.LoadAssetAtPath<CavalryCrowdLibrary>(output+"/CavalryCrowdLibrary.asset");
        if(!library){library=ScriptableObject.CreateInstance<CavalryCrowdLibrary>();AssetDatabase.CreateAsset(library,output+"/CavalryCrowdLibrary.asset");}
        library.hideFlags=HideFlags.DontUnloadUnusedAsset;if(!mountedTurnsOnly&&!mountedCasualtiesOnly){library.French=new CavalryCrowdSet{Lods=new CavalryCrowdLod[2]};library.British=new CavalryCrowdSet{Lods=new CavalryCrowdLod[2]};}
        var report=new StringBuilder();var factionMaps=new Dictionary<string,Rect>[2];
        for(int level=0;level<2;level++)
        {
            BuildScene(false,level==0?3:5);var inspect=UnityEngine.Object.FindFirstObjectByType<InfantryInspection>();
            for(int side=0;side<2;side++)
            {
                var rig=inspect.Soldiers[side];inspect.Clips[0].SampleAnimation(rig,0);rig.transform.position=Vector3.zero;
                var gun=Array.Find(rig.GetComponentsInChildren<MeshRenderer>(),r=>r.enabled&&r.name.StartsWith("newmusket.lod"));var rod=Array.Find(rig.GetComponentsInChildren<Transform>(),t=>t.name=="Authored ramrod");
                var all=rig.GetComponentsInChildren<Renderer>();var cloth=Array.Find(rig.GetComponentsInChildren<SkinnedMeshRenderer>(),r=>r.enabled&&r.name.Contains(side==0?"french_infantry":"british_infantry")).sharedMaterial;
                var equipment=InfantryCommandAuthoring.Create(rig,InfantryMotionKind.Colonel,cloth,rod.GetComponent<Renderer>().sharedMaterial,gun.sharedMaterial,side);
                foreach(var r in equipment.GetComponentsInChildren<Renderer>())if(r.name.StartsWith("Revolver")||r.name.Contains("epaulette")||r.name.Contains("Colonel"))r.enabled=false;
                gun.enabled=false;rod.GetComponent<Renderer>().enabled=false;
                var horse=equipment.GetComponent<CommandHorseMarker>().Rig;
                foreach(var part in equipment.GetComponentsInChildren<Renderer>())
                    if(part.name.StartsWith("Saddle")||part.name.StartsWith("Stirrup")||part.name.StartsWith("Bridle")||part.name.StartsWith("Rein"))part.transform.SetParent(horse.Root.transform,true);
                var motion=new ProceduralInfantryRig(rig);var grip=new ClosedGripAuthoring(rig,gun.transform,rod,-.65f);
                var set=side==0?library.French:library.British;
                var maps=new Dictionary<string,Rect>();var textures=new List<Texture2D>();var names=new List<string>();
                foreach(var r in rig.GetComponentsInChildren<Renderer>())if(r.enabled)foreach(var m in r.sharedMaterials)if(!names.Contains(m.name))
                {
                    names.Add(m.name);var t=new Texture2D(2,2,TextureFormat.RGBA32,false);var source=m.GetTexture("_BaseMap");
                    if(source)t.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(source)));else{t.SetPixels(new[]{m.color,m.color,m.color,m.color});t.Apply();}textures.Add(t);
                }
                if(level==0&&!names.Contains("CoarseWeapon")){names.Add("CoarseWeapon");var t=new Texture2D(2,2,TextureFormat.RGBA32,false);var c=new Color(.2f,.12f,.07f);t.SetPixels(new[]{c,c,c,c});t.Apply();textures.Add(t);}
                var atlas=new Texture2D(2048,2048,TextureFormat.RGBA32,false);var packed=atlas.PackTextures(textures.ToArray(),8,2048);for(int i=0;i<names.Count;i++)maps[names[i]]=packed[i];
                string atlasPath=output+"/"+rig.name+"Atlas.png";
                if(level==0){File.WriteAllBytes(atlasPath,atlas.EncodeToPNG());AssetDatabase.ImportAsset(atlasPath);var importer=(TextureImporter)AssetImporter.GetAtPath(atlasPath);importer.mipmapEnabled=true;importer.maxTextureSize=2048;importer.SaveAndReimport();}
                if(level==0)factionMaps[side]=maps;else maps=factionMaps[side];
                foreach(var t in textures)UnityEngine.Object.DestroyImmediate(t);UnityEngine.Object.DestroyImmediate(atlas);
                if(level==0)
                {
                    string path=output+"/"+rig.name+".mat";set.Material=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(!set.Material){set.Material=new Material(Shader.Find("EmpireBattle/CrowdInfantry"));AssetDatabase.CreateAsset(set.Material,path);}
                    set.Material.shader=Shader.Find("EmpireBattle/CrowdInfantry");set.Material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath));set.Material.enableInstancing=true;EditorUtility.SetDirty(set.Material);
                }
                var lod=mountedTurnsOnly||mountedCasualtiesOnly?set.Lods[level]:new CavalryCrowdLod{MaxDistance=level==0?50:100000};set.Lods[level]=lod;var clips=mountedTurnsOnly||mountedCasualtiesOnly?new List<MountedClip>(lod.Clips):new List<MountedClip>();
                var baseRoot=horse.Root.transform.localPosition;var baseRotation=horse.Root.transform.localRotation;
                foreach(MountedMotion kind in Enum.GetValues(typeof(MountedMotion)))
                {
                    if(kind>MountedMotion.HorseIdle)continue;
                    bool turn=kind==MountedMotion.TurnLeft||kind==MountedMotion.TurnRight;
                    if(mountedTurnsOnly&&!turn)continue;
                    if(mountedCasualtiesOnly&&!IsRiderFall(kind)&&kind!=MountedMotion.HorseFall&&kind<MountedMotion.HorseWalk)continue;
                    clips.RemoveAll(c=>c.Motion==kind);
                    bool loop=kind<=MountedMotion.Charge||turn||kind>=MountedMotion.HorseWalk;int count=loop?16:20;var poses=new Mesh[count];
                    for(int frame=0;frame<count;frame++)
                    {
                        float phase=frame/(float)(loop?count:count-1);
                        var gait=kind==MountedMotion.Walk||kind==MountedMotion.HorseWalk||turn?HorseArt.HorseGait.Walk:kind==MountedMotion.Trot||kind==MountedMotion.HorseTrot?HorseArt.HorseGait.Trot:kind==MountedMotion.Gallop||kind==MountedMotion.HorseGallop||kind==MountedMotion.Charge?HorseArt.HorseGait.Gallop:HorseArt.HorseGait.Idle;
                        horse.Root.transform.localPosition=baseRoot;horse.Root.transform.localRotation=baseRotation;HorseArt.SampleGait(horse,gait,phase);
                        MeleeAction action=kind==MountedMotion.SlashLeft?MeleeAction.SlashLeft:kind==MountedMotion.SlashRight?MeleeAction.SlashRight:kind==MountedMotion.Brake?MeleeAction.Brake:kind==MountedMotion.Stumble?MeleeAction.Stumble:MeleeAction.None;
                        float bob=gait==HorseArt.HorseGait.Gallop?.027f:gait==HorseArt.HorseGait.Trot?.018f:gait==HorseArt.HorseGait.Walk?.008f:0;
                        motion.SampleMounted(action,phase,Mathf.Sin(phase*Mathf.PI*4)*bob);grip.ApplyMountedCombat(action,phase);
                        if(kind==MountedMotion.Charge){var spine=Array.Find(motion.Bones,t=>t.name=="spine");spine.localRotation=Quaternion.AngleAxis(-10,Vector3.right)*spine.localRotation;}
                        if(kind==MountedMotion.Brake||kind==MountedMotion.Stumble){horse.Neck.localRotation*=Quaternion.Euler(-15*Mathf.Sin(phase*Mathf.PI),0,0);horse.Torso.localRotation*=Quaternion.Euler(12*Mathf.Sin(phase*Mathf.PI),0,0);}
                        if(kind==MountedMotion.TurnLeft||kind==MountedMotion.TurnRight){float sign=kind==MountedMotion.TurnLeft?-1:1;horse.Neck.localRotation*=Quaternion.Euler(0,sign*25,0);horse.Torso.localRotation*=Quaternion.Euler(0,0,sign*5);}
                        if(kind==MountedMotion.HorseFall)horse.Root.transform.localRotation=baseRotation*Quaternion.Euler(0,0,88*Mathf.SmoothStep(0,1,phase));
                        if(IsRiderFall(kind))motion.SampleMountedFall(phase,kind==MountedMotion.RiderFallBack?0:kind==MountedMotion.RiderFallSide?2:kind==MountedMotion.RiderTumble?3:1);
                        poses[frame]=CombineMounted(rig,horse,kind,phase,maps,rig.name+"_L"+level+"_"+kind+"_"+frame);
                        string path=output+"/"+poses[frame].name+".asset";AssetDatabase.DeleteAsset(path);AssetDatabase.CreateAsset(poses[frame],path);
                    }
                    for(int i=0;i<poses.Length;i++){var mesh=poses[i];var next=poses[i+1<poses.Length?i+1:loop?0:i];if(mesh.vertexCount!=next.vertexCount)throw new Exception("Mounted pose topology differs");mesh.SetUVs(2,new List<Vector3>(next.vertices));mesh.SetUVs(3,new List<Vector3>(next.normals));var bounds=mesh.bounds;bounds.Encapsulate(next.bounds);mesh.bounds=bounds;EditorUtility.SetDirty(mesh);}
                    if(mountedTurnsOnly)for(int old=count;old<20;old++)AssetDatabase.DeleteAsset(output+"/"+rig.name+"_L"+level+"_"+kind+"_"+old+".asset");
                    clips.Add(new MountedClip{Motion=kind,Loop=loop,Poses=poses});report.AppendLine($"{rig.name} L{level} {kind} poses={poses.Length} vertices={poses[0].vertexCount}");
                }
                lod.Clips=clips.ToArray();InfantryCommandAuthoring.Destroy(equipment,rig);
            }
        }
        BuildScene(false,3);library.hideFlags=HideFlags.None;EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();File.WriteAllText("Logs/cavalry-pose-bake.txt",report.ToString());
        Debug.Log("MELEE_CAVALRY_POSES_BAKED infantry standing/kneeling and mounted gait and varied casualty sequences at both LODs/factions");
    }
    static bool IsRiderFall(MountedMotion kind)=>kind==MountedMotion.RiderFall||kind==MountedMotion.RiderFallBack||kind==MountedMotion.RiderFallSide||kind==MountedMotion.RiderTumble;
    static Mesh CombineMounted(GameObject rig,HorseArt.HorseRig horse,MountedMotion kind,float phase,Dictionary<string,Rect> maps,string name)
    {
        var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
        foreach(var renderer in rig.GetComponentsInChildren<Renderer>())if(renderer.enabled)
        {
            bool horsePart=renderer==horse.Renderer||renderer.name.StartsWith("Saddle")||renderer.name.StartsWith("Stirrup")||renderer.name.StartsWith("Bridle")||renderer.name.StartsWith("Rein");
            if((kind==MountedMotion.HorseFall||kind>=MountedMotion.HorseWalk&&kind<=MountedMotion.HorseIdle)&&!horsePart||IsRiderFall(kind)&&horsePart)continue;
            var skin=renderer as SkinnedMeshRenderer;var mesh=skin?new Mesh():renderer.GetComponent<MeshFilter>().sharedMesh;if(skin)skin.BakeMesh(mesh,true);
            var matrix=Matrix4x4.Rotate(Quaternion.Euler(0,180,0))*rig.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;var normalMatrix=matrix.inverse.transpose;
            var points=mesh.vertices;var sourceNormals=mesh.normals;var texcoords=mesh.uv;
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                var mat=renderer.sharedMaterials[Mathf.Min(sub,renderer.sharedMaterials.Length-1)];var rect=maps[mat.name];var remap=new Dictionary<int,int>();
                foreach(int index in mesh.GetTriangles(sub))
                {
                    if(remap.TryGetValue(index,out int mapped)){triangles.Add(mapped);continue;}
                    mapped=vertices.Count;remap[index]=mapped;triangles.Add(mapped);vertices.Add(matrix.MultiplyPoint3x4(points[index]));normals.Add(normalMatrix.MultiplyVector(sourceNormals[index]).normalized);
                    var t=texcoords.Length==points.Length?texcoords[index]:Vector2.one*.5f;uv.Add(new Vector2(rect.x+Mathf.Clamp01(t.x)*rect.width,rect.y+Mathf.Clamp01(t.y)*rect.height));
                }
            }
            if(skin)UnityEngine.Object.DestroyImmediate(mesh);
        }
        if(kind==MountedMotion.HorseFall||IsRiderFall(kind))
        {
            float low=float.PositiveInfinity;foreach(var p in vertices)low=Mathf.Min(low,p.y);
            // Endpoints rest on the floor; rider's separated ballistic travel is supplied by the casualty renderer.
            float lift=Mathf.Max(.015f-low,(.015f-low)*Mathf.SmoothStep(0,1,phase));
            for(int i=0;i<vertices.Count;i++)vertices[i]+=Vector3.up*lift;
        }
        var combined=new Mesh{name=name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};combined.SetVertices(vertices);combined.SetNormals(normals);combined.SetUVs(0,uv);combined.SetTriangles(triangles,0);combined.RecalculateBounds();
        if(combined.bounds.size.magnitude>9||combined.vertexCount==0)throw new Exception("Invalid mounted mesh: "+name);return combined;
    }
}
