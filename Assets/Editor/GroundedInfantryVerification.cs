using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using EmpireBattle.Art;
using UnityEditor;
using UnityEngine;

public static class GroundedInfantryVerification
{
    public static void Verify()
    {
        var common=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        var sets=new List<InfantryCrowdSet>{common.French,common.British};
        foreach(string guid in AssetDatabase.FindAssets("t:UnitVisualLibrary"))
        {
            var asset=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>(AssetDatabase.GUIDToAssetPath(guid));
            if(asset.BakeVersion>0)sets.Add(asset.Set);
        }
        int endpoints=0;
        foreach(var set in sets)foreach(var lod in set.Lods)
        {
            if(lod.RecoveryVariants==null||lod.RecoveryVariants.Length!=4)throw new Exception("Missing four recovery variants");
            foreach(var recovery in lod.RecoveryVariants)
            {
                Grounded(recovery.Knockdown[recovery.Knockdown.Length-1]);endpoints++;
                Match(recovery.Knockdown[recovery.Knockdown.Length-1],recovery.GetUp[0]);
                Match(lod.Idle[0],recovery.GetUp[recovery.GetUp.Length-1]);
            }
            foreach(var death in lod.DeathVariants){Grounded(death.Poses[death.Poses.Length-1]);endpoints++;}
            if(lod.Command!=null)foreach(var role in lod.Command)
            {
                bool officer=role.OfficerKnockdown!=null&&role.OfficerKnockdown.Length>0;
                var down=officer?role.OfficerKnockdown:role.PikeKnockdown;var up=officer?role.OfficerGetUp:role.PikeGetUp;
                if(down!=null&&down.Length>0&&up!=null&&up.Length>0){Match(down[down.Length-1],up[0]);Match(role.Idle[0],up[up.Length-1]);}
                foreach(var poses in new[]{role.OfficerDeath,role.PikeDeath,role.OfficerKnockdown,role.PikeKnockdown})
                    if(poses!=null&&poses.Length>0){Grounded(poses[poses.Length-1]);endpoints++;}
            }
        }
        int limbContacts=0;
        var contacts=new Dictionary<string,float>();
        foreach(string path in new[]{"Logs/infantry-recovery-bake.txt","Logs/regiment-presentation-bake.txt","Logs/unit-roster-bake.txt"})
            foreach(string line in File.ReadLines(path))
            {
                var match=Regex.Match(line,@"^GROUND_CONTACT (.*) ((?:l|r)_(?:calf|foot|foretwist|foretwist1|palm)) low=(-?[0-9.]+)$");
                if(!match.Success)continue;
                string bone=match.Groups[2].Value;
                string region=bone.Contains("foretwist")?bone.Substring(0,2)+"forearm":bone;
                string key=match.Groups[1].Value+" "+region;
                float low=float.Parse(match.Groups[3].Value,System.Globalization.CultureInfo.InvariantCulture);
                if(!contacts.TryGetValue(key,out float previous)||low<previous)contacts[key]=low;
            }
        foreach(var contact in contacts)
        {
            float limit=contact.Key.EndsWith("palm")?.08f:contact.Key.EndsWith("forearm")?.10f:.13f;
            if(contact.Value<-.025f||contact.Value>limit)throw new Exception("Resting limb misses the floor: "+contact.Key+" low="+contact.Value);
            limbContacts++;
        }
        if(limbContacts==0)throw new Exception("Missing resting limb contact evidence");
        Directory.CreateDirectory("Artifacts/GroundContact");
        File.WriteAllText("Artifacts/GroundContact/endpoints.txt",$"GROUNDED_INFANTRY_PASSED {sets.Count} visual sets, {endpoints} endpoints, {limbContacts} limb contacts; four continuous recovery variants, all baked deaths per LOD.\n");
        Debug.Log($"GROUNDED_INFANTRY_PASSED {sets.Count} visual sets, {endpoints} endpoints, {limbContacts} limb contacts");
    }
    static void Grounded(Mesh mesh)
    {
        float min=float.PositiveInfinity,max=float.NegativeInfinity;
        foreach(var v in mesh.vertices){min=Mathf.Min(min,v.y);max=Mathf.Max(max,v.y);}
        if(min<-.025f||min>.035f||max>.65f)throw new Exception($"Floating/penetrating corpse {mesh.name}: min={min}, max={max}");
    }
    static void Match(Mesh a,Mesh b)
    {
        var av=a.vertices;var bv=b.vertices;
        if(av.Length!=bv.Length)throw new Exception("Recovery endpoint topology mismatch");
        for(int i=0;i<av.Length;i++)if((av[i]-bv[i]).sqrMagnitude>1e-8f)throw new Exception("Recovery endpoint pops: "+b.name);
    }
}
