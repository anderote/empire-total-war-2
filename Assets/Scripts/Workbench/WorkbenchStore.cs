using System;
using System.Collections.Generic;
using System.IO;
using EmpireBattle;
using UnityEngine;

namespace EmpireWorkbench
{
    [Serializable] public sealed class ReviewNote
    {
        public string Id,AssetId,AssetName,SourcePath,TrackId,TrackName,Text,Status="todo",UpdatedUtc;
        public float FrameTime,NormalizedTime,Yaw,Pitch,Distance;
        public int Lod;
        public Vector3 Focus;
        public BattleEffectProfile Effects;
    }
    [Serializable] public sealed class ReviewNotebook {public int Version=1;public List<ReviewNote> Notes=new List<ReviewNote>();}
    [Serializable] public sealed class ReviewPreset
    {
        public int Version=1;
        public string Name="Default";
        public BattleEffectProfile Effects=new BattleEffectProfile();
        public int Soldiers=100,Files=10,Ranks=10;
        public float FileSpacing=1.2f,RankSpacing=1.2f,ReplenishDelay=6;
        public bool AutoReplenish=true;
        public float Spacing=1.2f;
        public string AssetId,TrackId;
        public int Lod,Formation=1,Ammunition;
        public float Yaw=155,Pitch=12,Distance=4,NormalizedTime,Speed=1;
        public Vector3 Focus=new Vector3(0,1,0);
        public bool Loop=true;
    }
    public sealed class WorkbenchStore
    {
        public string Root {get;}
        public ReviewNotebook Notebook {get;private set;}
        public WorkbenchStore(string root)
        {
            Root=root;Directory.CreateDirectory(root);
            Notebook=File.Exists(NotesPath)?JsonUtility.FromJson<ReviewNotebook>(File.ReadAllText(NotesPath)):new ReviewNotebook();
            if(Notebook==null||Notebook.Notes==null||Notebook.Version!=1)throw new InvalidDataException("Invalid review notebook; original file preserved.");
        }
        public string NotesPath=>Path.Combine(Root,"notes.json");
        public ReviewNote Find(string asset,string track)=>Notebook.Notes.Find(n=>n.AssetId==asset&&n.TrackId==track);
        public void SaveNote(ReviewNote note)
        {
            using var writeLock=new FileStream(Path.Combine(Root,"notes.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            if(File.Exists(NotesPath))
            {
                var latest=JsonUtility.FromJson<ReviewNotebook>(File.ReadAllText(NotesPath));
                if(latest?.Notes==null)throw new InvalidDataException("Invalid review notebook; save cancelled.");
                Notebook=latest;
            }
            note.UpdatedUtc=DateTime.UtcNow.ToString("o");
            var existing=Find(note.AssetId,note.TrackId);
            if(existing!=null)Notebook.Notes.Remove(existing);
            Notebook.Notes.Add(note);WriteAtomic(NotesPath,JsonUtility.ToJson(Notebook,true));
        }
        public static string SafeName(string name)
        {
            var result=System.Text.RegularExpressions.Regex.Replace(name??"","[^A-Za-z0-9_-]","_");
            if(string.IsNullOrWhiteSpace(result))throw new ArgumentException("Enter a preset name.");
            return result;
        }
        public void SavePreset(ReviewPreset preset)
        {preset.Effects.Validate();WriteAtomic(Path.Combine(Root,"Presets",SafeName(preset.Name)+".json"),JsonUtility.ToJson(preset,true));}
        public ReviewPreset LoadPreset(string name)
        {
            var value=JsonUtility.FromJson<ReviewPreset>(File.ReadAllText(Path.Combine(Root,"Presets",SafeName(name)+".json")));
            if(value?.Effects==null||value.Version!=1)throw new InvalidDataException("Invalid preset.");
            value.Effects.Validate();value.Soldiers=Mathf.Clamp(value.Soldiers,1,500);value.Spacing=Mathf.Clamp(value.Spacing,.5f,3);
            value.Files=Mathf.Clamp(value.Files,1,40);value.Ranks=Mathf.Clamp(value.Ranks,1,Mathf.Min(40,500/value.Files));
            value.FileSpacing=Mathf.Clamp(value.FileSpacing,.5f,3);value.RankSpacing=Mathf.Clamp(value.RankSpacing,.5f,4);value.ReplenishDelay=Mathf.Clamp(value.ReplenishDelay,2,15);
            return value;
        }
        public string[] PresetNames()
        {
            var path=Path.Combine(Root,"Presets");if(!Directory.Exists(path))return Array.Empty<string>();
            return Array.ConvertAll(Directory.GetFiles(path,"*.json"),Path.GetFileNameWithoutExtension);
        }
        public void ExportQueue()
        {
            var queue=new ReviewNotebook();queue.Notes.AddRange(Notebook.Notes.FindAll(n=>n.Status=="todo"&&!string.IsNullOrWhiteSpace(n.Text)));
            WriteAtomic(Path.Combine(Root,"codex-queue.json"),JsonUtility.ToJson(queue,true));
        }
        public static void WriteAtomic(string path,string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            File.WriteAllText(temporary,text);
            if(File.Exists(path))File.Replace(temporary,path,path+".bak");else File.Move(temporary,path);
        }
    }
}
