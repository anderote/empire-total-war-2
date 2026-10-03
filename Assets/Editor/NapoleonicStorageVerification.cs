using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEditorInternal;
public static partial class InfantryAssetBuild
{
    public static void VerifyBinaryMeshStorage()
    {
        const string path="Assets/Resources/NapoleonicOutfits/BinaryStorageTest.asset";
        var source=AssetDatabase.LoadAssetAtPath<EmpireBattle.Art.UnitVisualLibrary>("Assets/Resources/NapoleonicOutfits/france-guard/Library.asset").Set.Lods[0].Idle[0];
        var original=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(original,path);AssetDatabase.SaveAssets();
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original,out string guid,out long id);
        var clone=UnityEngine.Object.Instantiate(original);
        try
        {
            InternalEditorUtility.SaveToSerializedFileAndForget(new UnityEngine.Object[]{clone},path,false);
            using(var stream=File.OpenRead(path))if(stream.ReadByte()=='%')throw new Exception("Mesh remains text");
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
            var loaded=AssetDatabase.LoadAssetAtPath<Mesh>(path);AssetDatabase.TryGetGUIDAndLocalFileIdentifier(loaded,out string newGuid,out long newId);
            if(!loaded||loaded.vertexCount!=source.vertexCount||guid!=newGuid||newId!=1)throw new Exception($"Unexpected binary mesh identifier {id}->{newId}");
            Debug.Log("NAPOLEONIC_BINARY_STORAGE_VERIFIED oldId="+id+" newId="+newId+" bytes="+new FileInfo(path).Length);
        }
        finally{UnityEngine.Object.DestroyImmediate(clone);AssetDatabase.DeleteAsset(path);}
    }
}
