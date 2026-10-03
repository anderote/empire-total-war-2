using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    public sealed partial class EuropeMapView
    {
        readonly Dictionary<string,GameObject> armyTokens=new Dictionary<string,GameObject>();
        Vector3 CampaignArmyPosition(CampaignArmy army,float height)
        {
            var from=countryCampaign.Position(army);
            Vector3 normal=GeographyAtlas.Normal(from.lon,from.lat);
            if(CountryCampaign.AtSea(army))
            {
                var route=countryCampaign.SeaRoute(army.seaFrom,army.seaTarget);
                float t=1-army.seaTurns/(float)Mathf.Max(1,army.seaDuration);
                float part=t*(route.regions.Count-1);int leg=Mathf.Min(route.regions.Count-2,Mathf.FloorToInt(part));
                var a=catalog.regions.First(r=>r.id==route.regions[leg]).capital;var b=catalog.regions.First(r=>r.id==route.regions[leg+1]).capital;
                normal=Vector3.Slerp(GeographyAtlas.Normal(a.lon,a.lat),GeographyAtlas.Normal(b.lon,b.lat),part-leg).normalized;
            }
            var at=new Vector2(Mathf.Atan2(-normal.x,normal.z)*Mathf.Rad2Deg,Mathf.Asin(normal.y)*Mathf.Rad2Deg);
            return Atlas.World(at.x,at.y,height);
        }
        string ArmyStackKey(CampaignArmy a)=>Mathf.RoundToInt(a.longitude*100)+"/"+Mathf.RoundToInt(a.latitude*100);
        void UpdateCampaignArmyTokens()
        {
            var active=countryCampaign?.Save.armies.Where(a=>countryCampaign.CanObserve(a)).ToArray()??new CampaignArmy[0];
            foreach(var id in armyTokens.Keys.Where(id=>!active.Any(a=>a.id==id)).ToArray())
            {Destroy(armyTokens[id]);armyTokens.Remove(id);}
            var rows=new Dictionary<string,int>();
            foreach(var army in active)
            {
                if(armyTokens.TryGetValue(army.id,out var previous)&&previous.name!=army.owner+" / "+army.generalName)
                {Destroy(previous);armyTokens.Remove(army.id);}
                if(!armyTokens.TryGetValue(army.id,out var token))
                {
                    token=new GameObject(army.owner+" / "+army.generalName);owned.Add(token);armyTokens.Add(army.id,token);
                    AddArmyPiece(token,PrimitiveType.Cylinder,new Vector3(0,.08f,0),new Vector3(1.35f,.12f,1.35f),catalog.Faction(army.owner).MapColor);
                    AddArmyPiece(token,PrimitiveType.Capsule,new Vector3(0,.65f,0),new Vector3(.35f,.6f,.35f),CampaignPaper);
                    AddArmyPiece(token,PrimitiveType.Cube,new Vector3(.32f,1.2f,0),new Vector3(.65f,.42f,.09f),catalog.Faction(army.owner).MapColor);
                    if(army.generalName!="")AddArmyPiece(token,PrimitiveType.Sphere,new Vector3(0,1.4f,0),new Vector3(.28f,.28f,.28f),CampaignBronze);
                }
                token.SetActive(distance<10000);if(!token.activeSelf)continue;
                Vector3 position=CampaignArmyPosition(army,7);int row=rows.TryGetValue(ArmyStackKey(army),out int count)?count:0;rows[ArmyStackKey(army)]=row+1;
                float size=Mathf.Clamp(distance*.023f,4,100);
                token.transform.rotation=Quaternion.FromToRotation(Vector3.up,position.normalized);
                position+=token.transform.right*(row*1.2f+.85f)*size;
                token.transform.position=Vector3.Lerp(token.transform.position==Vector3.zero?position:token.transform.position,position,Mathf.Clamp01(Time.unscaledDeltaTime*5));
                token.transform.localScale=Vector3.one*size*(selectedArmyId==army.id||campaignHoveredArmy==army?1.15f:1f);
            }
        }
        void AddArmyPiece(GameObject parent,PrimitiveType shape,Vector3 at,Vector3 scale,Color color)
        {
            var piece=GameObject.CreatePrimitive(shape);piece.transform.SetParent(parent.transform,false);piece.transform.localPosition=at;piece.transform.localScale=scale;
            Destroy(piece.GetComponent<Collider>());
            var source=piece.GetComponent<MeshFilter>().sharedMesh;var mesh=Instantiate(source);meshes.Add(mesh);
            mesh.colors=Enumerable.Repeat(color.linear,mesh.vertexCount).ToArray();piece.GetComponent<MeshFilter>().sharedMesh=mesh;
            piece.GetComponent<MeshRenderer>().sharedMaterial=colorMaterial;
        }
    }
}
