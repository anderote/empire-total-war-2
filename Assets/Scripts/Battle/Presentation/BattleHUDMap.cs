using UnityEngine;
using Empire.UI;

namespace EmpireBattle
{
    public sealed partial class BattleHUD
    {
        Texture2D mapTexture;
        Rect MapRect => BattleUILayout.Map(Width);
        void DrawBattleMap()
        {
            if(menuOpen)return;
            Rect panel=MapRect;FramedPanel(panel,Navy);
            GUI.Label(new Rect(panel.x+12,panel.y+5,panel.width-24,20),"BATTLEFIELD  ·  N ↑",cardTitle);
            Rect map=BattleUILayout.TerrainMap(Width);
            if(!mapTexture)CreateBattleMap();
            GUI.DrawTexture(map,mapTexture);
            foreach(var unit in world.Battalions)
            {
                if(unit.AliveCount==0)continue;
                Vector2 point=BattleUILayout.MapPoint(map,unit.Position,world.Terrain.HalfSize);
                if(!map.Contains(point))continue;
                if(selected[unit.Id])Dot(new Rect(point.x-4,point.y-4,8,8),Cream);
                Dot(new Rect(point.x-2,point.y-2,4,4),unit.Routing?EmpireTheme.Danger:unit.Side==ArmySide.French?world.Config.PlayerCountryColor:world.Config.EnemyCountryColor);
            }
            foreach(var gun in view.CommandGuns)
            {
                Vector2 point=BattleUILayout.MapPoint(map,gun.Position,world.Terrain.HalfSize);
                if(!map.Contains(point))continue;
                Fill(new Rect(point.x-2,point.y-2,4,4),view.IsGunSelected(gun.Id)?Cream:gun.Side==ArmySide.French?world.Config.PlayerCountryColor:world.Config.EnemyCountryColor);
            }
            Ray ray=battleCamera.ViewportPointToRay(new Vector3(.5f,.5f,0));
            if(new Plane(Vector3.up,Vector3.zero).Raycast(ray,out float distance))
            {
                Vector3 center=ray.GetPoint(distance);
                Vector2 point=BattleUILayout.MapPoint(map,new Vector2(center.x,center.z),world.Terrain.HalfSize);
                if(map.Contains(point))
                {
                    Fill(new Rect(point.x-5,point.y,10,1),Brass);
                    Fill(new Rect(point.x,point.y-5,1,10),Brass);
                }
            }
            bool old=GUI.enabled;GUI.enabled=old&&view.EnableInput;
            if(GUI.Button(map,"",GUIStyle.none))
                view.SetInspectionView(BattleUILayout.WorldPoint(map,Event.current.mousePosition,world.Terrain.HalfSize),500,60);
            if(ThemedButton(new Rect(panel.x+14,panel.y+165,160,21),"ARMY OVERVIEW",button))view.SetOverview();
            GUI.enabled=old;
        }
        void CreateBattleMap()
        {
            const int size=128;
            mapTexture=new Texture2D(size,size,TextureFormat.RGBA32,false){name="Battle terrain overview",hideFlags=HideFlags.HideAndDontSave};
            var pixels=new Color[size*size];
            float half=world.Terrain.HalfSize;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                Vector2 point=new Vector2((x/(float)(size-1)*2-1)*half,(y/(float)(size-1)*2-1)*half);
                float height=world.Terrain.BaseHeightAt(point);
                Color color=Color.Lerp(new Color(.4f,.44f,.31f),new Color(.76f,.7f,.52f),Mathf.Clamp01((height+10)/80));
                for(int wood=0;wood<world.Terrain.WoodsCenters.Length;wood++)
                    if((point-world.Terrain.WoodsCenters[wood]).sqrMagnitude<world.Terrain.WoodsRadii[wood]*world.Terrain.WoodsRadii[wood])
                    {color=new Color(.24f,.3f,.22f);break;}
                pixels[y*size+x]=EmpireTheme.RenderColor(color);
            }
            mapTexture.SetPixels(pixels);mapTexture.Apply(false,true);
        }
    }
}
