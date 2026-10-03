using System;
using System.IO;
using System.Linq;
using UnityEngine;
namespace EmpireWorkbench
{
    public sealed partial class AssetWorkbench
    {
        [Serializable]sealed class NapoleonicQAReceipt {public int Libraries,Tracks,Meshes;public string[] Sources;}
        System.Collections.IEnumerator ReviewNapoleonicQA()
        {
            Directory.CreateDirectory(qaDirectory);yield return null;
            var entries=Catalog.Entries.Where(e=>e.Category=="Units"&&(e.SourcePath.Contains("/NapoleonicOutfits/")||e.SourcePath.Contains("/GrenadeMotions/")||e.SourcePath.Contains("/NapoleonicCommands/"))).ToArray();
            var receipt=new NapoleonicQAReceipt{Sources=entries.Select(e=>e.SourcePath).ToArray()};string failure=null;
            if(entries.Length<165)failure="Incomplete Napoleonic review catalog: "+entries.Length;
            foreach(var entry in entries)
            {
                if(failure!=null)break;
                try
                {
                    WorkbenchUnitLoader.Load(entry);
                    if(!entry.Material)throw new Exception("Missing material: "+entry.Name);
                    foreach(var motion in entry.Tracks)
                    {
                        if(motion.Poses==null||motion.Poses.Length==0||motion.Poses.Any(p=>!p||p.vertexCount==0))throw new Exception("Missing review poses: "+entry.Name+" / "+motion.Id);
                        receipt.Tracks++;receipt.Meshes+=motion.Poses.Length;
                    }
                    receipt.Libraries++;
                }
                catch(Exception error){failure=error.Message;}
                finally{WorkbenchUnitLoader.Release(entry);}
                yield return Resources.UnloadUnusedAssets();
            }
            if(failure!=null){File.WriteAllText(Path.Combine(qaDirectory,"failure.txt"),failure);Debug.LogError("NAPOLEONIC_WORKBENCH_QA_FAILED "+failure);Application.Quit(20);yield break;}
            File.WriteAllText(Path.Combine(qaDirectory,"receipt.json"),JsonUtility.ToJson(receipt,true));
            Debug.Log($"NAPOLEONIC_WORKBENCH_RUNTIME_VERIFIED libraries={receipt.Libraries} tracks={receipt.Tracks} meshes={receipt.Meshes}");Application.Quit(0);
        }
    }
}
