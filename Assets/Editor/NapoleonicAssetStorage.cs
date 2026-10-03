using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
public static partial class InfantryAssetBuild
{
    // Only this task's generated meshes are converted. Untracked binary objects receive
    // new local IDs, so update their library references explicitly before saving.
    static void CompactOutfitData(string output)
    {
        var refs=new Dictionary<string,List<Action<Mesh>>>();
        var libraries=Directory.GetFiles(output,"*.asset",SearchOption.AllDirectories)
            .Where(p=>p.EndsWith("/Library.asset")||p.EndsWith("/CavalryCrowdLibrary.asset"))
            .Select(p=>AssetDatabase.LoadAssetAtPath<ScriptableObject>(p)).Where(o=>o).ToArray();
        foreach(var library in libraries)CollectMeshReferences(library,refs,new HashSet<object>());
        var orphaned=Directory.GetFiles(output,"*.asset").Where(p=>!refs.ContainsKey(p)&&AssetDatabase.LoadAssetAtPath<Mesh>(p)).ToArray();
        Directory.CreateDirectory("Artifacts/NapoleonicOutfits");
        foreach(var path in orphaned){long size=new FileInfo(path).Length;AssetDatabase.DeleteAsset(path);File.AppendAllText("Artifacts/NapoleonicOutfits/storage-cleanup.txt",$"{DateTime.UtcNow:O} unreferenced task-generated mesh {path}: {size} bytes\n");}
        var paths=refs.Keys.Where(p=>p.StartsWith(output+"/",StringComparison.Ordinal)&&IsTextMesh(p)).ToArray();
        long before=paths.Sum(p=>new FileInfo(p).Length);
        foreach(var path in paths)
        {
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);int count=mesh.vertexCount;
            var clone=UnityEngine.Object.Instantiate(mesh);
            try{UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(new UnityEngine.Object[]{clone},path,false);}
            finally{UnityEngine.Object.DestroyImmediate(clone);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
            var loaded=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!loaded||loaded.vertexCount!=count||IsTextMesh(path))throw new Exception("Binary conversion failed: "+path);
            foreach(var assign in refs[path])assign(loaded);
        }
        foreach(var library in libraries){EditorUtility.SetDirty(library);AssetDatabase.SaveAssetIfDirty(library);}
        long after=paths.Sum(p=>new FileInfo(p).Length);
        File.AppendAllText("Artifacts/NapoleonicOutfits/storage.txt",$"{DateTime.UtcNow:O} {output}: {paths.Length} generated meshes; {before} -> {after} bytes; removed {orphaned.Length} unreferenced task meshes; project serialization setting unchanged\n");
        Debug.Log($"NAPOLEONIC_MESH_STORAGE {output}: {before} -> {after} bytes; orphan meshes={orphaned.Length}");
    }
    static void CollectMeshReferences(object value,Dictionary<string,List<Action<Mesh>>> refs,HashSet<object> visited)
    {
        if(value==null||value is string||value.GetType().IsPrimitive||value.GetType().IsEnum||!visited.Add(value))return;
        void Add(Mesh mesh,Action<Mesh> setter)
        {
            if(!mesh)return;string path=AssetDatabase.GetAssetPath(mesh);if(string.IsNullOrEmpty(path))return;
            if(!refs.TryGetValue(path,out var setters))refs[path]=setters=new List<Action<Mesh>>();setters.Add(setter);
        }
        if(value is Array array)
        {
            for(int i=0;i<array.Length;i++){int index=i;var element=array.GetValue(i);if(element is Mesh mesh)Add(mesh,m=>array.SetValue(m,index));else CollectMeshReferences(element,refs,visited);}
            return;
        }
        if(value is UnityEngine.Object&&!(value is ScriptableObject))return;
        foreach(var field in value.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))
        {var element=field.GetValue(value);if(element is Mesh mesh)Add(mesh,m=>field.SetValue(value,m));else CollectMeshReferences(element,refs,visited);}
    }
    static bool IsTextMesh(string path){using(var stream=File.OpenRead(path))return stream.ReadByte()=='%';}
    public static void VerifyNapoleonicPreparation()
    {
        BakeNapoleonicOutfits();
        var library=AssetDatabase.LoadAssetAtPath<EmpireBattle.Art.UnitVisualLibrary>("Assets/Resources/NapoleonicOutfits/france-guard/Library.asset");
        if(!library||!library.Set.Lods[0].Idle[0]||library.Set.Lods[0].Idle[0].vertexCount==0)throw new Exception("Binary mesh serialization failed");
        NapoleonicOutfitVerification.VerifySimulation();VerifySupplementalGrips();
    }
    public static void VerifyNapoleonicWorkbench()
    {
        NapoleonicOutfitVerification.Verify();VerifySupplementalGrips();NapoleonicPosePreview.ExportAll();
        AssetWorkbenchBuild.Prepare();AssetWorkbenchBuild.Verify();
    }
    public static void BakeNapoleonicExpansion()
    {
        BakeNapoleonicOutfits();BakeGrenadeMotions();BakeNapoleonicCommands();BakeCartridgeOutfits();
        NapoleonicOutfitVerification.Verify();NapoleonicPosePreview.ExportAll();
    }
}
