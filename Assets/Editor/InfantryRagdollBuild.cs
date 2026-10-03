using System;
using System.Collections.Generic;
using System.IO;
using EmpireBattle.Art;
using EmpireBattle.AnimationAuthoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class InfantryRagdollBuild
{
    const string Output = "Assets/Resources/BakedInfantry";
    const string Source = "Assets/Art/NapoleonicInfantry";
    static readonly Quaternion Turn = Quaternion.Euler(0,180,0);
    [MenuItem("Empire Battle/Bake cannon ragdoll assets")]
    public static void Bake()
    {
        Directory.CreateDirectory(Output);
        var library = AssetDatabase.LoadAssetAtPath<InfantryRagdollLibrary>(Output+"/InfantryRagdollLibrary.asset");
        if (!library) { library=ScriptableObject.CreateInstance<InfantryRagdollLibrary>(); AssetDatabase.CreateAsset(library,Output+"/InfantryRagdollLibrary.asset"); }
        library.French=Build("French"); library.British=Build("British");
        EditorUtility.SetDirty(library);
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Scripts/Battle/Effects/Resources/BattleBlood.shader");
        if (shader && !AssetDatabase.LoadAssetAtPath<Material>("Assets/Scripts/Battle/Effects/Resources/BloodVariant.mat"))
            AssetDatabase.CreateAsset(new Material(shader){enableInstancing=true},"Assets/Scripts/Battle/Effects/Resources/BloodVariant.mat");
        AssetDatabase.SaveAssets();
        Debug.Log("RAGDOLL_ASSETS_PASSED");
    }
    static AnimationClip Clip(string name)
    {
        foreach(var a in AssetDatabase.LoadAllAssetsAtPath(Source+"/Animations/"+name+".FBX"))
            if(a is AnimationClip clip && !clip.name.StartsWith("__preview"))return clip;
        throw new Exception("Missing infantry clip: "+name);
    }
    public static void BakeMounted()
    {
        const string path="Assets/Resources/BakedCavalry/CavalryRagdollLibrary.asset";
        var library=AssetDatabase.LoadAssetAtPath<InfantryRagdollLibrary>(path);
        if(!library){library=ScriptableObject.CreateInstance<InfantryRagdollLibrary>();AssetDatabase.CreateAsset(library,path);}
        library.French=Build("French",true);library.British=Build("British",true);
        EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();Debug.Log("MOUNTED_RAGDOLL_ASSETS_BAKED saddle poses and sabre-equipped riders for both factions");
    }
    static InfantryRagdollSet Build(string side,bool mounted=false)
    {
        var source=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Source+"/Prefabs/"+side+"Inspection.prefab"));
        try
        {
            source.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            var idle=Clip("musket_idle"); idle.SampleAnimation(source,0);
            string assetSide=side+(mounted?"Mounted":"");
            if(mounted)
            {
                var gun=Array.Find(source.GetComponentsInChildren<MeshRenderer>(),r=>r.enabled&&r.name.StartsWith("newmusket.lod"));
                var skin=Array.Find(source.GetComponentsInChildren<SkinnedMeshRenderer>(),r=>r.enabled&&r.name.Contains(side=="French"?"french_infantry":"british_infantry"));
                var equipment=InfantryCommandAuthoring.Create(source,InfantryMotionKind.Officer,skin.sharedMaterial,gun.sharedMaterial,gun.sharedMaterial,side=="French"?0:1);
                foreach(var renderer in equipment.GetComponentsInChildren<Renderer>())renderer.enabled=false;
                gun.enabled=false;
                foreach(var renderer in source.GetComponentsInChildren<Renderer>())if(renderer.name=="Authored ramrod")renderer.enabled=false;
            }
            var bones=new List<Transform>();
            var needed=new HashSet<Transform>();
            foreach(var skin in source.GetComponentsInChildren<SkinnedMeshRenderer>())if(skin.enabled)
                foreach(var bone in skin.bones)for(var t=bone;t && t!=source.transform;t=t.parent)needed.Add(t);
            foreach(var t in source.GetComponentsInChildren<Transform>())if(needed.Contains(t))bones.Add(t);
            var names=new string[bones.Count];var parents=new int[bones.Count];
            for(int i=0;i<bones.Count;i++){names[i]=bones[i].name;parents[i]=bones.IndexOf(bones[i].parent);}
            var rest=Capture(source,bones);
            var set=new InfantryRagdollSet{Names=names,Parents=parents,Positions=rest.Positions,Rotations=rest.Rotations};
            var materials=new List<Material>();
            foreach(var renderer in source.GetComponentsInChildren<Renderer>())if(renderer.enabled)
                foreach(var mat in renderer.sharedMaterials)if(!materials.Contains(mat))materials.Add(mat);
            var textures=new List<Texture2D>();
            foreach(var material in materials)
            {
                var tex=new Texture2D(2,2,TextureFormat.RGBA32,false);var map=material.GetTexture("_BaseMap");
                if(map)tex.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(map)));
                else {tex.SetPixels(new[]{material.color,material.color,material.color,material.color});tex.Apply();}
                textures.Add(tex);
            }
            var atlas=new Texture2D(4096,4096);var rects=atlas.PackTextures(textures.ToArray(),8,4096);
            string atlasPath=Output+"/"+assetSide+"RagdollAtlas.png";File.WriteAllBytes(atlasPath,atlas.EncodeToPNG());
            foreach(var tex in textures)UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(atlasPath);
            var importer=(TextureImporter)AssetImporter.GetAtPath(atlasPath);importer.maxTextureSize=4096;importer.mipmapEnabled=true;importer.SaveAndReimport();
            string matPath=Output+"/"+assetSide+"Ragdoll.mat";var matAsset=AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if(!matAsset){matAsset=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(matAsset,matPath);}
            matAsset.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath));matAsset.SetFloat("_Smoothness",.12f);matAsset.enableInstancing=true;EditorUtility.SetDirty(matAsset);set.Material=matAsset;
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var weights=new List<BoneWeight>();var triangles=new List<int>();
            foreach(var renderer in source.GetComponentsInChildren<Renderer>())if(renderer.enabled)
            {
                var skin=renderer as SkinnedMeshRenderer;
                Mesh mesh=skin?new Mesh():renderer.GetComponent<MeshFilter>().sharedMesh;
                if(skin)skin.BakeMesh(mesh,true);
                var matrix=Matrix4x4.Rotate(Turn)*source.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
                var nm=matrix.inverse.transpose;var points=mesh.vertices;var ns=mesh.normals;var uvs=mesh.uv;
                var bw=skin?skin.sharedMesh.boneWeights:null;
                int rigid=-1;for(var t=renderer.transform.parent;t && rigid<0;t=t.parent)rigid=bones.IndexOf(t);
                for(int sub=0;sub<mesh.subMeshCount;sub++)
                {
                    var rect=rects[materials.IndexOf(renderer.sharedMaterials[Mathf.Min(sub,renderer.sharedMaterials.Length-1)])];
                    var remap=new Dictionary<int,int>();
                    foreach(int index in mesh.GetTriangles(sub))
                    {
                        if(remap.TryGetValue(index,out int existing)){triangles.Add(existing);continue;}
                        remap[index]=vertices.Count;triangles.Add(vertices.Count);
                        vertices.Add(matrix.MultiplyPoint3x4(points[index]));normals.Add(nm.MultiplyVector(ns[index]).normalized);
                        Vector2 tex=uvs.Length>index?uvs[index]:Vector2.one*.5f;uv.Add(new Vector2(rect.x+Mathf.Clamp01(tex.x)*rect.width,rect.y+Mathf.Clamp01(tex.y)*rect.height));
                        BoneWeight w=skin?bw[index]:new BoneWeight{boneIndex0=Mathf.Max(0,rigid),weight0=1};
                        if(skin){w.boneIndex0=bones.IndexOf(skin.bones[w.boneIndex0]);w.boneIndex1=bones.IndexOf(skin.bones[w.boneIndex1]);w.boneIndex2=bones.IndexOf(skin.bones[w.boneIndex2]);w.boneIndex3=bones.IndexOf(skin.bones[w.boneIndex3]);}
                        weights.Add(w);
                    }
                }
                if(skin)UnityEngine.Object.DestroyImmediate(mesh);
            }
            var combined=new Mesh{name=assetSide+"NormalizedRagdoll",indexFormat=IndexFormat.UInt32};
            combined.SetVertices(vertices);combined.SetNormals(normals);combined.SetUVs(0,uv);combined.SetTriangles(triangles,0);combined.boneWeights=weights.ToArray();
            var binds=new Matrix4x4[bones.Count];for(int i=0;i<binds.Length;i++)binds[i]=Matrix4x4.TRS(rest.Positions[i],rest.Rotations[i],Vector3.one).inverse;
            combined.bindposes=binds;combined.RecalculateBounds();
            string meshPath=Output+"/"+assetSide+"RagdollMesh.asset";
            var existingMesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);if(existingMesh){EditorUtility.CopySerialized(combined,existingMesh);UnityEngine.Object.DestroyImmediate(combined);set.Mesh=existingMesh;}else{AssetDatabase.CreateAsset(combined,meshPath);set.Mesh=combined;}
            if(mounted)
            {
                idle.SampleAnimation(source,0);var motion=new ProceduralInfantryRig(source);
                var gun=Array.Find(source.GetComponentsInChildren<MeshRenderer>(),r=>r.name.StartsWith("newmusket.lod"));
                var rod=Array.Find(source.GetComponentsInChildren<Transform>(),t=>t.name=="Authored ramrod");
                var grip=new ClosedGripAuthoring(source,gun.transform,rod,-.65f);
                set.Mounted=new InfantryBonePose[16];
                for(int frame=0;frame<16;frame++){float phase=frame/16f;motion.SampleMounted(EmpireBattle.MeleeAction.None,phase,Mathf.Sin(phase*Mathf.PI*4)*.027f);grip.ApplyMountedCombat(EmpireBattle.MeleeAction.None,phase);set.Mounted[frame]=Capture(source,bones);}
                return set;
            }
            set.Idle=Poses(source,bones,idle,4,null);set.Fire=Poses(source,bones,Clip("gp_musket_aimfire"),8,null);
            set.Reload=Poses(source,bones,Clip("gp_reload_musket_reworked"),24,null);
            set.March=Poses(source,bones,idle,8,InfantryMotionKind.March);set.Run=Poses(source,bones,idle,8,InfantryMotionKind.Run);set.Charge=Poses(source,bones,idle,8,InfantryMotionKind.Charge);
            return set;
        }
        finally {UnityEngine.Object.DestroyImmediate(source);}
    }
    static InfantryBonePose Capture(GameObject root,List<Transform> bones)
    {
        var pose=new InfantryBonePose{Positions=new Vector3[bones.Count],Rotations=new Quaternion[bones.Count]};
        for(int i=0;i<bones.Count;i++){pose.Positions[i]=Turn*root.transform.InverseTransformPoint(bones[i].position);pose.Rotations[i]=Turn*Quaternion.Inverse(root.transform.rotation)*bones[i].rotation;}
        return pose;
    }
    static InfantryBonePose[] Poses(GameObject source,List<Transform> bones,AnimationClip clip,int count,InfantryMotionKind? kind)
    {
        Clip("musket_idle").SampleAnimation(source,0);
        var motion=kind.HasValue?new ProceduralInfantryRig(source):null;
        var result=new InfantryBonePose[count];
        for(int i=0;i<count;i++){if(motion!=null)motion.Sample(kind.Value,i/(float)count);else clip.SampleAnimation(source,clip.length*i/count);result[i]=Capture(source,bones);}
        return result;
    }
}
