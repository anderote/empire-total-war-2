using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireBattle;
using UnityEngine;

namespace Empire.UI
{
    [Serializable] public sealed class RegimentLibrarySave
    {
        public int version=1;
        public List<SavedRegiment> regiments=new List<SavedRegiment>();
        public bool Valid=>version==1&&regiments!=null&&regiments.Count<=RegimentLibrary.Capacity&&regiments.All(r=>r!=null&&r.Valid&&!string.IsNullOrEmpty(r.id))&&regiments.Select(r=>r.id).Distinct().Count()==regiments.Count;
    }
    public sealed class RegimentLibrary
    {
        public const int Capacity=100;
        static RegimentLibrary shared;
        public static RegimentLibrary Shared=>shared??(shared=new RegimentLibrary((Array.IndexOf(Environment.GetCommandLineArgs(),"--ui-workbench-review")>=0||Array.IndexOf(Environment.GetCommandLineArgs(),"--regiment-upgrade-ui")>=0)?null:Path.Combine(Application.persistentDataPath,"regiment-library.json")));
        readonly string path;
        RegimentLibrarySave save=new RegimentLibrarySave();
        bool blocked;
        public string Error {get;private set;}="";
        public bool Writable=>!blocked;
        public IReadOnlyList<SavedRegiment> Entries=>save.regiments.Select(r=>r.Copy()).ToArray();
        public RegimentLibrary(string file)
        {
            path=file;if(path==null||!File.Exists(path))return;
            try {var loaded=JsonUtility.FromJson<RegimentLibrarySave>(File.ReadAllText(path));if(loaded==null||!loaded.Valid)throw new InvalidDataException("Invalid regiment library.");save=loaded;}
            catch(Exception e){blocked=true;Error="Could not read the regiment library. Existing file preserved. "+e.Message;}
        }
        public SavedRegiment Find(string id)=>save.regiments.FirstOrDefault(r=>r.id==id)?.Copy();
        public bool Store(SavedRegiment snapshot,string replaceId,out string reason)
        {
            reason=Error;if(blocked)return false;
            if(snapshot==null||!snapshot.Valid){reason="Choose a name up to 60 characters and a supported regiment.";return false;}
            var next=CopySave();var old=next.regiments.Find(r=>r.id==replaceId);
            if(replaceId!=""&&old==null){reason="That saved regiment no longer exists.";return false;}
            if(old==null&&next.regiments.Count>=Capacity){reason="Library is full. Delete a saved copy before adding another.";return false;}
            var entry=snapshot.Copy();entry.id=old?.id??Guid.NewGuid().ToString("N");entry.savedAt=DateTime.UtcNow.ToString("o");
            if(old!=null)next.regiments[next.regiments.IndexOf(old)]=entry;else next.regiments.Add(entry);
            if(!Commit(next,out reason))return false;reason="Saved "+entry.name+" to the regiment library.";return true;
        }
        public bool Delete(string id,out string reason)
        {
            reason=Error;if(blocked)return false;var next=CopySave();if(next.regiments.RemoveAll(r=>r.id==id)==0){reason="Saved copy no longer exists.";return false;}
            if(!Commit(next,out reason))return false;reason="Saved copy deleted. Active regiments are unaffected.";return true;
        }
        RegimentLibrarySave CopySave()=>new RegimentLibrarySave {regiments=save.regiments.Select(r=>r.Copy()).ToList()};
        bool Commit(RegimentLibrarySave next,out string reason)
        {
            try
            {
                if(!next.Valid)throw new InvalidDataException("Invalid saved regiment.");
                if(path!=null){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));File.WriteAllText(path+".tmp",JsonUtility.ToJson(next,true));if(File.Exists(path))File.Replace(path+".tmp",path,path+".bak");else File.Move(path+".tmp",path);}
                save=next;Error=reason="";return true;
            }
            catch(Exception e){Error=reason="Could not save the regiment library: "+e.Message;return false;}
        }
        public static SavedRegiment Capture(RegimentRecord unit,string name,string context)=>new SavedRegiment {
            name=name.Trim(),source=context,role=unit.BattleRole,users=unit.Users,weapon=unit.Weapon,quality=unit.Quality,tier=unit.Tier,
            sourceStrength=unit.Strength,sourceCapacity=unit.Capacity,progression=unit.Progression.Copy(),loadout=unit.Loadout.Copy()};
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void Reset(){shared=null;}
    }
}
