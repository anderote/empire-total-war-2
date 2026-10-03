using System;
using UnityEngine;

namespace EmpireWorkbench
{
    [Serializable] public sealed class ReviewTrack
    {
        public string Id,Name;
        public int Lod;
        public float Duration=1;
        public bool Loop=true;
        public bool Morph;
        public bool ReverseMorph;
        public Mesh[] Poses;
        public AnimationClip Animation;
    }
    [Serializable] public sealed class ReviewAsset
    {
        public string Id,Name,Category,SourcePath,Faction,Family;
        public string ResourcePath,BundlePath,SetField,Weapon="musket";
        [NonSerialized]public EmpireBattle.Art.InfantryCrowdSet PreviewSet;
        public Material Material;
        public GameObject Model;
        public ReviewTrack[] Tracks;
    }
    public sealed class WorkbenchCatalog:ScriptableObject
    {
        public ReviewAsset[] Entries;
    }
    public static class WorkbenchUnitLoader
    {
        public static void Load(ReviewAsset entry)
        {
            if(string.IsNullOrEmpty(entry.ResourcePath)&&string.IsNullOrEmpty(entry.BundlePath))return;
            ScriptableObject library;
            #if UNITY_EDITOR
            library=UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(entry.SourcePath);
            #else
            if(!string.IsNullOrEmpty(entry.ResourcePath))library=Resources.Load<ScriptableObject>(entry.ResourcePath);
            else
            {
                var bundle=AssetBundle.LoadFromFile(System.IO.Path.Combine(Application.streamingAssetsPath,entry.BundlePath));
                if(!bundle)throw new InvalidOperationException("Review bundle missing: "+entry.BundlePath);
                library=bundle.LoadAsset<ScriptableObject>(entry.SourcePath.ToLowerInvariant());bundle.Unload(false);
            }
            #endif
            if(!library)throw new InvalidOperationException("Unit library missing: "+entry.SourcePath);
            object set=library.GetType().GetField(entry.SetField).GetValue(library);
            if(set is EmpireBattle.Art.InfantryCrowdSet infantry)
            {
                entry.Material=infantry.Material;
                entry.PreviewSet=infantry;
                foreach(var track in entry.Tracks)
                {
                    object value=infantry.Lods[track.Lod];
                    foreach(string token in track.Id.Split('/'))
                    {
                        if(value is Array array)
                        {
                            int index;
                            if(array is EmpireBattle.Art.InfantryMeleeClip[] melee)index=Array.FindIndex(melee,c=>c.Action.ToString()==token);
                            else if(array is EmpireBattle.Art.InfantryCommandVariant[]&&!int.TryParse(token,out index))index=Array.IndexOf(new[]{"Captain","Drummer","Standard","Pike sergeant","Colonel"},token);
                            else index=int.Parse(token);
                            if(index<0||index>=array.Length)throw new InvalidOperationException("Invalid review track: "+entry.Name+" / "+track.Id+" token "+token);
                            value=array.GetValue(index);
                        }
                        else value=value.GetType().GetField(token).GetValue(value);
                    }
                    track.Poses=value is Mesh mesh?new[]{mesh}:value as Mesh[];
                }
            }
            else if(set is EmpireBattle.Art.CavalryCrowdSet cavalry)
            {
                entry.Material=cavalry.Material;
                foreach(var track in entry.Tracks)
                    track.Poses=Array.Find(cavalry.Lods[track.Lod].Clips,c=>c.Motion.ToString()==track.Id)?.Poses;
            }
            else throw new InvalidOperationException("Unsupported unit library: "+entry.Name);
        }
        public static void Release(ReviewAsset entry)
        {
            if(entry==null||(string.IsNullOrEmpty(entry.ResourcePath)&&string.IsNullOrEmpty(entry.BundlePath)))return;
            entry.Material=null;entry.PreviewSet=null;foreach(var track in entry.Tracks)track.Poses=null;
        }
    }
}
