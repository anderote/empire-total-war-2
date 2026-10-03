using System;
using System.Linq;
using EmpireBattle;
using UnityEngine;

namespace Empire.UI
{
    public sealed class RegimentLibraryPanel
    {
        readonly UITooltip tips=new UITooltip();
        Vector2 listScroll,previewScroll;
        string selected="",name="",search="",focus="",confirm="";
        Action pending;
        public void Draw(Rect r,RegimentRecord current,IRegimentSource source,ref string message)
        {
            tips.Begin();var library=RegimentLibrary.Shared;
            if(focus!=current.Id){focus=current.Id;name=current.Name.Substring(0,Math.Min(60,current.Name.Length));}
            if(pending!=null)
            {
                CommandUI.Label(new Rect(r.x,r.y,r.width,48),"Confirm library action",22,false,true);
                CommandUI.Label(new Rect(r.x,r.y+58,r.width,116),confirm,15,true,false,true);
                if(CommandUI.Button(new Rect(r.x,r.y+188,190,38),"Confirm")){pending();pending=null;message=result;}
                if(CommandUI.Button(new Rect(r.x+202,r.y+188,150,38),"Cancel"))pending=null;
                return;
            }
            CommandUI.Label(new Rect(r.x,r.y,r.width,32),"Save veteran cadres. Reuse their progression and kit across game modes.",13,true,false,true);
            GUI.SetNextControlName("library-name");name=GUI.TextField(new Rect(r.x,r.y+38,r.width-186,32),name,60);
            var capture=RegimentLibrary.Capture(current,name,source.Context);
            bool archive=source.CanArchive&&capture.Valid&&library.Writable;
            string saveHint=!source.CanArchive?"Save regiments between battles or while managing an idle campaign army.":!capture.Valid?"Enter a name for an infantry regiment, up to 60 characters.":"Save a separate copy of XP, stats, abilities, quality and equipment. Casualties remain with the original; imports use destination troop strength.";
            if(CommandUI.Button(new Rect(r.xMax-176,r.y+38,176,32),"Save new copy",archive,hint:saveHint,tips:tips)){library.Store(capture,"",out message);selected=library.Entries.LastOrDefault()?.id??selected;}
            var entries=library.Entries;var visible=entries.Where(s=>string.IsNullOrWhiteSpace(search)||(s.name+" "+s.source+" "+UnitRoster.Label(s.role)).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            if(library.Find(selected)==null)selected=visible.FirstOrDefault()?.id??"";
            float lw=Mathf.Clamp(r.width*.32f,220,310);var left=new Rect(r.x,r.y+91,lw,r.height-91);
            GUI.SetNextControlName("library-search");search=GUI.TextField(new Rect(left.x,left.y,left.width,28),search);
            tips.Add(new Rect(left.x,left.y,left.width,28),"Search saved regiments by name, role or source.");
            if(search==""&&GUI.GetNameOfFocusedControl()!="library-search")CommandUI.Label(new Rect(left.x+8,left.y,left.width-16,28),"Search saved regiments…",12,true);
            var list=new Rect(left.x,left.y+37,left.width,left.height-111);
            listScroll=GUI.BeginScrollView(list,listScroll,new Rect(0,0,lw-14,Math.Max(list.height-1,visible.Length*66)));
            for(int i=0;i<visible.Length;i++)
            {
                var s=visible[i];var row=new Rect(0,i*66,lw-16,59);
                if(CommandUI.Button(row,"",selected:selected==s.id)){selected=s.id;previewScroll=Vector2.zero;}
                MilitaryInsignia.Rank(new Rect(7,row.y+8,30,37),s.progression.Rank);
                CommandUI.Label(new Rect(45,row.y+4,row.width-53,24),s.name,13,false,true);
                CommandUI.Label(new Rect(45,row.y+29,row.width-53,22),UnitRoster.Label(s.role)+" · Lv "+s.progression.UnitLevel,11,true);
            }
            if(visible.Length==0)CommandUI.Label(new Rect(8,12,lw-30,130),entries.Count==0?"Your library is empty. Save a Glory regiment above, then open Library in battle preparation or campaign regiment management.":"No matching saved regiments.",13,true,false,true);
            GUI.EndScrollView();var saved=library.Find(selected);
            if(CommandUI.Button(new Rect(left.x,left.yMax-65,left.width/2-4,29),"Update copy",archive&&saved!=null,hint:"Replace the selected library entry with this regiment's current progression and equipment.",tips:tips))
            {
                var snapshot=capture.Copy();string id=saved.id;confirm="Replace saved copy ‘"+saved.name+"’ with ‘"+snapshot.name+"’? Its previous library version will be replaced. Active regiments remain unchanged.";
                pending=()=>library.Store(snapshot,id,out result);
            }
            if(CommandUI.Button(new Rect(left.x+left.width/2+4,left.yMax-65,left.width/2-4,29),"Delete copy",library.Writable&&saved!=null))
            {string id=saved.id;confirm="Delete ‘"+saved.name+"’ from the library? Regiments already active in Glory, battles and campaigns remain unchanged.";pending=()=>library.Delete(id,out result);}
            CommandUI.Label(new Rect(left.x,left.yMax-30,left.width,28),entries.Count+" / "+RegimentLibrary.Capacity+" saved copies",11,true);
            var detail=new Rect(left.xMax+18,left.y,r.width-lw-18,left.height);
            if(library.Error!=""){CommandUI.Label(detail,library.Error,13,true,false,true);tips.Draw(r);return;}
            if(saved!=null)
            {
                var offer=source.Import(current.Id,saved);float footer=118;
                var view=new Rect(detail.x,detail.y,detail.width,detail.height-footer);
                previewScroll=GUI.BeginScrollView(view,previewScroll,new Rect(0,0,detail.width-14,665));
                float dw=detail.width-22;
                MilitaryInsignia.Rank(new Rect(0,0,42,51),saved.progression.Rank);
                CommandUI.Label(new Rect(54,0,dw-54,33),saved.name,20,false,true);
                CommandUI.Label(new Rect(54,35,dw-54,24),UnitRoster.Label(saved.role)+" · Level "+saved.progression.UnitLevel+" / "+UnitProgression.MaxLevel+" · "+saved.progression.RankName,12,true);
                CommandUI.Label(new Rect(0,69,dw,46),saved.source+"\nSaved "+(DateTime.TryParse(saved.savedAt,out var date)?date.ToLocalTime().ToString("d MMM yyyy HH:mm"):saved.savedAt),11,true,false,true);
                CommandUI.Label(new Rect(0,119,dw,45),saved.progression.xp+" XP · "+saved.progression.AvailablePoints+" unspent stat points · "+saved.progression.AvailableSkillPoints+" ability points · equipment era "+saved.RequiredYear,12,true,false,true);
                var profile=new RegimentRecord {Id=saved.id,Name=saved.name,Role=UnitRoster.Label(saved.role),Users=saved.users,BattleRole=saved.role,Weapon=saved.weapon,Strength=current.Strength,Capacity=current.Capacity,Quality=saved.quality,Progression=saved.progression,Loadout=saved.loadout};
                var stats=RegimentPreview.Stats(profile);CommandUI.Eyebrow(new Rect(0,171,dw,23),"Saved regiment stats");
                for(int i=0;i<stats.Length;i++){var row=new Rect(0,199+i*24,dw,23);CommandUI.Label(new Rect(0,row.y,dw*.58f,23),stats[i].Name,12,true);CommandUI.Label(new Rect(dw*.58f,row.y,dw*.42f,23),stats[i].Display,12);tips.Add(row,stats[i].Help);}
                float y=207+stats.Length*24;
                CommandUI.Label(new Rect(0,y,dw,80),"Equipment: "+(saved.loadout.items.Count==0?"Standard issue":string.Join(", ",saved.loadout.items.Select(id=>UnitEquipment.Label(UnitEquipment.Find(id))))),12,true,false,true);
                CommandUI.Label(new Rect(0,y+89,dw,100),"Abilities: "+(saved.progression.abilities.Count==0?"None learned":string.Join(", ",saved.progression.abilities.Distinct().Select(a=>RegimentAbilities.Find(a).Name+" "+saved.progression.AbilityRank(a)+"/"+RegimentAbilities.MaxRank))),12,true,false,true);
                GUI.EndScrollView();
                CommandUI.Label(new Rect(detail.x,detail.yMax-footer+3,detail.width,65),offer.Enabled?offer.Description:offer.Reason,12,true,false,true);
                if(CommandUI.Button(new Rect(detail.x,detail.yMax-44,detail.width,40),offer.Title+(offer.Cost==""?"":" · "+offer.Cost),offer.Enabled,hint:offer.Enabled?offer.Description:offer.Reason,tips:tips))
                {confirm=offer.Title+" · "+saved.name+"\n"+offer.Cost+"\n\n"+offer.Description;pending=()=>result=source.Import(current.Id,saved).Execute();}
            }
            tips.Draw(r);
        }
        string result="";
    }
}
