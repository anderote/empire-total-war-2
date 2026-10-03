using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEditor;
using UnityEngine;

public static partial class InfantryAssetBuild
{
    static bool bayonetOrderBake;
    static bool bayonetUnfixBake;
    static readonly List<Mesh> bayonetTransferPoses=new List<Mesh>();
    static float bayonetMaxContactError;
    static float BayonetOrderMotionPhase(EmpireBattle.AnimationAuthoring.InfantryMotionKind kind,float phase)=>phase*(kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Walk?2:kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Run?4:1);
    static bool bayonetOrderKneeling;
    static void BayonetOrderPose(GameObject rig,Transform gun,Transform rod,float phase)
    {
        var author=new BayonetOrderAuthoring(rig,gun,bayonetOrderKneeling);author.Sample(bayonetUnfixBake?1-phase:phase);
        var turn=Quaternion.Euler(0,180,0);
        Vector3 Local(Vector3 p)=>turn*rig.transform.InverseTransformPoint(p);
        Quaternion LocalRotation(Quaternion r)=>turn*Quaternion.Inverse(rig.transform.rotation)*r;
        bayonetTransferPoses.Add(BayonetPresentationLibrary.Equipment(Local(author.BladeSocket),LocalRotation(author.BladeRotation),Local(author.BeltSocket),LocalRotation(author.BeltRotation)));
        bayonetMaxContactError=Mathf.Max(bayonetMaxContactError,author.ContactError);
        if(author.ContactError>.025f)throw new Exception("Bayonet socket hand disconnected: "+rig.name+" phase="+phase+" error="+author.ContactError);
        MusketReloadAuthoring.StowRod(gun,rod);
    }
    public static void BakeBayonetOrders()
    {
        const string output="Assets/Resources/Bayonets";
        Directory.CreateDirectory(output);Directory.CreateDirectory("Logs");prepared=true;bayonetMaxContactError=0;
        var path=output+"/Library.asset";var library=AssetDatabase.LoadAssetAtPath<BayonetPresentationLibrary>(path);
        if(!library){library=ScriptableObject.CreateInstance<BayonetPresentationLibrary>();AssetDatabase.CreateAsset(library,path);}
        library.hideFlags=HideFlags.DontUnloadUnusedAsset;
        var weapons=new List<BayonetWeaponTemplate>();var orders=new List<BayonetOrderClip>();var report=new StringBuilder();
        try
        {
            var infantry=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
            var cartridges=Resources.Load<CartridgeRifleLibrary>("CartridgeRifles/Library");
            if(cartridges)cartridges.hideFlags=HideFlags.DontUnloadUnusedAsset;
            if(infantry)infantry.hideFlags=HideFlags.DontUnloadUnusedAsset;
            for(int family=0;family<3;family++)for(int level=0;level<(family==0?2:3);level++)
            {
                if(family>0&&!cartridges)continue;
                cartridgeBake=family>0;
                BuildScene(false,level==0?3:5);var inspect=UnityEngine.Object.FindAnyObjectByType<InfantryInspection>();
                for(int side=0;side<2;side++)
                {
                    var set=family==0?(side==0?infantry.French:infantry.British):family==1?(side==0?cartridges.French:cartridges.British):(side==0?cartridges.SingleFrench:cartridges.SingleBritish);
                    var rig=inspect.Soldiers[side];rig.transform.position=Vector3.zero;
                    var gun=Array.Find(rig.GetComponentsInChildren<MeshRenderer>(),r=>r.enabled&&r.name.StartsWith("newmusket.lod"));
                    if(family>0){gun.GetComponent<MeshFilter>().sharedMesh=CartridgeWeaponAuthoring.Prepare(family==2,level);gun.sharedMaterial=CartridgeWeaponAuthoring.PrepareMaterial();}
                    if(side==0)weapons.Add(Template(gun.GetComponent<MeshFilter>().sharedMesh,rig.transform.InverseTransformVector(gun.transform.TransformVector(Vector3.right)).magnitude));
                    var maps=new Dictionary<string,Rect>();foreach(var renderer in rig.GetComponentsInChildren<Renderer>())if(renderer.enabled)foreach(var material in renderer.sharedMaterials)maps[material.name]=new Rect(0,0,1,1);
                    string target=output+"/Family"+family;Directory.CreateDirectory(target);
                    var order=new BayonetOrderClip{Name=(family==0?"Musket":family==1?"Magazine":"SingleShot")+"-"+rig.name+"-L"+level,Uniform=set.Material,Lod=level};orders.Add(order);
                    Directory.CreateDirectory(target+"/Unfix");
                    for(int action=0;action<6;action++)
                    {
                        bayonetOrderBake=true;bayonetUnfixBake=action>=4;bayonetOrderKneeling=action==1;bayonetTransferPoses.Clear();
                        var kind=(action==2||action==4)?(EmpireBattle.AnimationAuthoring.InfantryMotionKind?)EmpireBattle.AnimationAuthoring.InfantryMotionKind.Walk:(action==3||action==5)?EmpireBattle.AnimationAuthoring.InfantryMotionKind.Run:null;
                        var poses=BakePoses(rig,inspect.Clips[0],action>=2?32:20,maps,action>=4?target+"/Unfix":target,level,report,kind,action==1);
                        // BakePoses' generic idle name is relocated to prevent replacing
                        // shared idle files; only these dedicated output folders are used.
                        var reference=set.Lods[level].Idle[0];
                        foreach(var pose in poses){if(pose.vertexCount!=reference.vertexCount)throw new Exception("Bayonet order topology mismatch: "+pose.name);pose.uv=reference.uv;EditorUtility.SetDirty(pose);}
                        PrepareBayonetMorph(poses);
                        if(action==0)order.Standing=poses;else if(action==1)order.Kneeling=poses;else if(action==2)order.March=poses;else if(action==3)order.Run=poses;else if(action==4)order.UnfixMarch=poses;else order.UnfixRun=poses;
                        SaveTransfer(order,action,poses,target);
                    }
                    bayonetOrderBake=false;
                }
            }
            BakeBayonetRosterOrders(weapons,orders,report);
            library.Weapons=weapons.ToArray();library.Orders=orders.ToArray();
            string steelPath=output+"/Steel.mat";library.Steel=AssetDatabase.LoadAssetAtPath<Material>(steelPath);
            if(!library.Steel){library.Steel=new Material(Shader.Find("EmpireBattle/CrowdInfantry"));AssetDatabase.CreateAsset(library.Steel,steelPath);}
            var atlas=new Texture2D(2,1,TextureFormat.RGBA32,false){filterMode=FilterMode.Point};atlas.SetPixels(new[]{Color.white,new Color(.15f,.10f,.07f)});atlas.Apply();
            string atlasPath=output+"/EquipmentAtlas.asset";var oldAtlas=AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);if(oldAtlas){EditorUtility.CopySerialized(atlas,oldAtlas);UnityEngine.Object.DestroyImmediate(atlas);atlas=oldAtlas;EditorUtility.SetDirty(atlas);}else AssetDatabase.CreateAsset(atlas,atlasPath);
            library.Steel.SetTexture("_BaseMap",atlas);
            library.Steel.SetColor("_BaseColor",new Color(.72f,.76f,.8f));library.Steel.enableInstancing=true;EditorUtility.SetDirty(library.Steel);
            library.hideFlags=HideFlags.None;
            if(cartridges)cartridges.hideFlags=HideFlags.None;
            if(infantry)infantry.hideFlags=HideFlags.None;
            EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
            report.AppendLine("BAYONET_SOCKET_HAND_MAX_ERROR "+bayonetMaxContactError.ToString("F5")+"m");
            File.WriteAllText("Logs/bayonet-order-bake.txt",report.ToString());
            Debug.Log("BAYONET_ORDERS_BAKED "+orders.Count+" clips, standing/kneeling, muskets/single-shot/magazine rifles, both factions and all LODs");
        }
        finally{bayonetOrderBake=false;cartridgeBake=false;library.hideFlags=HideFlags.None;
            var cartridges=Resources.Load<CartridgeRifleLibrary>("CartridgeRifles/Library");if(cartridges)cartridges.hideFlags=HideFlags.None;
            var infantry=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");if(infantry)infantry.hideFlags=HideFlags.None;}
    }
    static void SaveTransfer(BayonetOrderClip order,int action,Mesh[] bodies,string target)
    {
        if(order.Transfer==null)order.Transfer=new BayonetTransferTrack[6];
        Directory.CreateDirectory(target+"/Transfer");Directory.CreateDirectory(target+"/Unfix");
        var props=bayonetTransferPoses.ToArray();if(props.Length!=bodies.Length)throw new Exception("Transfer frame count mismatch");
        for(int i=0;i<props.Length;i++){
            string path=target+"/Transfer/"+order.Name+"-"+action+"-"+i+".asset";props[i].name=order.Name+" transfer "+action+" frame "+i;
            var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old){EditorUtility.CopySerialized(props[i],old);UnityEngine.Object.DestroyImmediate(props[i]);props[i]=old;EditorUtility.SetDirty(old);}else AssetDatabase.CreateAsset(props[i],path);
        }
        PrepareBayonetMorph(props);order.Transfer[action]=new BayonetTransferTrack{Bodies=bodies,Props=props};
    }
    static BayonetWeaponTemplate Template(Mesh mesh,float scale)
    {
        var vertices=mesh.vertices;var ordered=new List<Vector3>();var seen=new HashSet<int>();
        for(int sub=0;sub<mesh.subMeshCount;sub++){seen.Clear();foreach(int index in mesh.GetTriangles(sub))if(seen.Add(index))ordered.Add(vertices[index]*scale);}
        int b=1,c=2;for(int i=1;i<ordered.Count;i++)if((ordered[i]-ordered[0]).sqrMagnitude>(ordered[b]-ordered[0]).sqrMagnitude)b=i;
        for(int i=1;i<ordered.Count;i++)if(Vector3.Cross(ordered[b]-ordered[0],ordered[i]-ordered[0]).sqrMagnitude>Vector3.Cross(ordered[b]-ordered[0],ordered[c]-ordered[0]).sqrMagnitude)c=i;
        var sum=Vector3.zero;int count=0;foreach(var vertex in vertices)if(vertex.z>mesh.bounds.max.z-.35f){sum+=vertex;count++;}
        var muzzle=count>0?new Vector3(sum.x/count,sum.y/count,mesh.bounds.max.z):new Vector3(0,0,mesh.bounds.max.z);
        return new BayonetWeaponTemplate{Points=ordered.ToArray(),B=b,C=c,Socket=muzzle*scale+Vector3.right*.03f};
    }
    static void PrepareBayonetMorph(Mesh[] poses)
    {
        for(int i=0;i<poses.Length;i++){var mesh=poses[i];var next=poses[Mathf.Min(i+1,poses.Length-1)];mesh.SetUVs(2,new List<Vector3>(next.vertices));mesh.SetUVs(3,new List<Vector3>(next.normals));var bounds=mesh.bounds;bounds.Encapsulate(next.bounds);mesh.bounds=bounds;EditorUtility.SetDirty(mesh);}
    }
}
