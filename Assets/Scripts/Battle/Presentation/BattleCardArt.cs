using System.Collections.Generic;
using Empire.UI;
using UnityEngine;

namespace EmpireBattle
{
    /// <summary>Loads only commissioned base-line paintings; all card text remains live HUD data.</summary>
    public static class BattleCardArt
    {
        static readonly Dictionary<string,Texture2D> portraits=new Dictionary<string,Texture2D>();

        static Texture2D Resolve(BattalionState unit)
        {
            if(unit==null||unit.Kind!=UnitKind.Infantry||unit.Role!=UnitRole.Line||RegimentBaseStats.Uniform(unit.TroopTier)!=Vector4.zero)return null;
            string weapon=unit.Weapon?.Id;
            if(unit.Loadout?.items!=null)
                foreach(string id in unit.Loadout.items)
                {
                    var item=UnitEquipment.Find(id);
                    if(item!=null&&item.Slot==EquipmentSlot.Weapon){weapon=item.Weapon;break;}
                }
            if(weapon=="breechloader"||weapon=="magazine"||weapon=="smokeless")return null;
            string faction=UnitRoster.ArtFaction(unit.Faction);
            if(faction!="france"&&faction!="britain"&&faction!="ming"&&faction!="india")return null;
            string key=faction+"-line-base";
            if(!portraits.TryGetValue(key,out var texture))portraits[key]=texture=Resources.Load<Texture2D>("UI/CampaignArt/Portraits/"+key);
            return texture;
        }

        public static bool Draw(Rect rect,BattalionState unit)
        {
            var texture=Resolve(unit);
            if(!texture)return false;
            Color previous=GUI.color;GUI.color=Color.white;
            GUI.DrawTexture(rect,texture,ScaleMode.ScaleAndCrop,false);
            GUI.color=previous;
            EmpireTheme.Fill(new Rect(rect.x,rect.y+rect.height*.36f,rect.width,rect.height*.64f),new Color(.025f,.03f,.04f,.66f));
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()=>portraits.Clear();
    }
}
