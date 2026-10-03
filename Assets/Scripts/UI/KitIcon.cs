using EmpireBattle;
using UnityEngine;

namespace Empire.UI
{
    public static class KitIcon
    {
        public static void Draw(Rect r,EquipmentSlot slot)
        {
            var c=EmpireTheme.Brass;
            void Bar(float x,float y,float w,float h)=>EmpireTheme.Fill(new Rect(r.x+x*r.width,r.y+y*r.height,w*r.width,h*r.height),c);
            switch(slot)
            {
                case EquipmentSlot.Weapon:Bar(.38f,.02f,.12f,.75f);Bar(.25f,.63f,.25f,.34f);Bar(.5f,.49f,.32f,.1f);break;
                case EquipmentSlot.Secondary:Bar(.44f,.03f,.13f,.77f);Bar(.17f,.61f,.66f,.1f);Bar(.38f,.80f,.25f,.17f);break;
                case EquipmentSlot.Throwables:Bar(.37f,.05f,.27f,.17f);Bar(.25f,.3f,.5f,.58f);Bar(.14f,.43f,.72f,.31f);break;
                case EquipmentSlot.Ammunition:for(int i=0;i<3;i++){Bar(.1f+i*.3f,.2f,.17f,.65f);Bar(.12f+i*.3f,.09f,.13f,.11f);}break;
                case EquipmentSlot.Uniform:Bar(.25f,.23f,.5f,.66f);Bar(.07f,.3f,.2f,.38f);Bar(.75f,.3f,.2f,.38f);break;
                case EquipmentSlot.Protection:Bar(.16f,.14f,.68f,.6f);Bar(.29f,.74f,.42f,.12f);Bar(.43f,.86f,.14f,.1f);break;
                case EquipmentSlot.Mobility:Bar(.18f,.1f,.29f,.72f);Bar(.18f,.72f,.69f,.19f);break;
                case EquipmentSlot.Standard:Bar(.14f,.02f,.1f,.96f);Bar(.26f,.06f,.66f,.41f);break;
                case EquipmentSlot.Support:Bar(.11f,.27f,.78f,.61f);Bar(.32f,.1f,.36f,.12f);break;
                default:Bar(.4f,.27f,.16f,.68f);Bar(.11f,.11f,.75f,.17f);break;
            }
        }
    }
    public static class StrategicAtlasArt
    {
        static Texture2D atlas;
        public static void Draw(Rect r,bool caption=true)
        {
            if(!atlas)atlas=Resources.Load<Texture2D>("WorldGeography/relief");
            EmpireTheme.Panel(r,EmpireTheme.Ink);
            if(atlas)
            {
                Color old=GUI.color;GUI.color=new Color(.88f,.79f,.60f,.88f);
                GUI.DrawTextureWithTexCoords(CommandUI.Inset(r,1),atlas,new Rect(.37f,.48f,.37f,.33f));GUI.color=old;
                EmpireTheme.Fill(new Rect(r.x+1,r.y+1,r.width-2,r.height-2),new Color(.035f,.075f,.085f,.26f));
            }
            for(int i=1;i<6;i++)EmpireTheme.Fill(new Rect(r.x+i*r.width/6,r.y,1,r.height),new Color(.8f,.76f,.57f,.16f));
            for(int i=1;i<4;i++)EmpireTheme.Fill(new Rect(r.x,r.y+i*r.height/4,r.width,1),new Color(.8f,.76f,.57f,.16f));
            if(caption){EmpireTheme.Fill(new Rect(r.x+18,r.y+18,254,30),EmpireTheme.Ink);CommandUI.Eyebrow(new Rect(r.x+29,r.y+21,235,24),"The world at your command");}
            float cx=r.xMax-56,cy=r.yMax-59;
            EmpireTheme.Fill(new Rect(cx-1,cy-27,2,54),EmpireTheme.Brass);EmpireTheme.Fill(new Rect(cx-27,cy-1,54,2),EmpireTheme.Brass);
            CommandUI.Label(new Rect(cx-5,cy-49,20,20),"N",12,false,true);
        }
    }
}
