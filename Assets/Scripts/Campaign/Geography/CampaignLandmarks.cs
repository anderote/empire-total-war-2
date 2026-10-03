using System.Collections.Generic;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    // Authored anchors and original strategic silhouettes, not archaeological reconstructions.
    [System.Serializable] public sealed class CampaignLandmark
    {
        public string id,name,artKind,description,source;
        public float lon,lat;
    }
    [System.Serializable] public sealed class LandmarkCatalog
    {
        public int schemaVersion;
        public CampaignLandmark[] landmarks;
    }

    public sealed partial class EuropeMapView
    {
        CampaignLandmark[] landmarks=System.Array.Empty<CampaignLandmark>();
        bool landmarkListOpen;
        void LoadLandmarks()
        {
            var resource=EmpireCampaign.Geography.CampaignResourceData.Load("CampaignLandmarks");
            if(resource==null)return;
            var data=JsonUtility.FromJson<LandmarkCatalog>(resource.text);
            if(data!=null && data.schemaVersion==1 && data.landmarks!=null)landmarks=data.landmarks;
        }
        bool LandmarkNearView(CampaignLandmark place)
        {
            float dx=Mathf.DeltaAngle(focusLon,place.lon)*Mathf.Cos(place.lat*Mathf.Deg2Rad);
            float dy=place.lat-focusLat;
            float radius=Mathf.Clamp(distance/80,3,65);
            return dx*dx+dy*dy<radius*radius;
        }
        void DrawLandmarks(GeometryBuilder b)
        {
            if(distance>=5000)return;
            foreach(var place in landmarks)
            {
                if(!LandmarkNearView(place) || !Atlas.IsDry(place.lon,place.lat))continue;
                b.Frame(Atlas.World(place.lon,place.lat,1.5f),Quaternion.LookRotation(
                    GeographyAtlas.North(place.lon,place.lat),GeographyAtlas.Normal(place.lon,place.lat)));
                switch(place.artKind)
                {
                    case "pyramids":
                        Pyramid(b,new Vector3(-8,0,-6),12,9,new Color(.79f,.66f,.43f).linear);
                        Pyramid(b,Vector3.zero,10.5f,8,new Color(.74f,.61f,.39f).linear);
                        Pyramid(b,new Vector3(7,0,6),6,4.5f,new Color(.70f,.57f,.38f).linear);
                        // Sphinx silhouette: low body, chest and head beside the complex.
                        b.Box(new Vector3(11,0,-7),new Vector3(2,1,5),Stone);
                        b.Box(new Vector3(11,1,-8),new Vector3(1.4f,1.4f,1.3f),Stone);
                        break;
                    case "standing_stones":
                        for(int i=0;i<12;i++)
                        {
                            float angle=i*Mathf.PI*2/12;
                            var p=new Vector3(Mathf.Cos(angle)*6,0,Mathf.Sin(angle)*6);
                            b.Box(p,new Vector3(1.1f,i%4==0?1.4f:3.2f,1.2f),Stone);
                            if(i%3==0)
                            {
                                float next=(i+1)*Mathf.PI*2/12;
                                b.Beam(p+Vector3.up*3.2f,new Vector3(Mathf.Cos(next)*6,3.2f,Mathf.Sin(next)*6),1.2f,.7f,Stone);
                            }
                        }
                        b.Box(new Vector3(-2,0,1),new Vector3(1.4f,4,1.5f),Stone);
                        b.Box(new Vector3(2,0,1),new Vector3(1.4f,4,1.5f),Stone);
                        b.Beam(new Vector3(-2,4,1),new Vector3(2,4,1),1.5f,.8f,Stone);
                        b.Box(new Vector3(0,0,-3),new Vector3(3,.6f,1),Stone);
                        break;
                    case "temple_complex":
                        b.Box(Vector3.zero,new Vector3(18,1.2f,16),Stone);
                        b.Box(Vector3.up*1.2f,new Vector3(11,1.5f,10),Stone);
                        TempleTower(b,new Vector3(0,2.7f,0),7);
                        foreach(float x in new[]{-5f,5f})foreach(float z in new[]{-4f,4f})
                            TempleTower(b,new Vector3(x,1.2f,z),4.5f);
                        b.Beam(new Vector3(0,0,-17),new Vector3(0,0,-8),2,.6f,Stone);
                        break;
                    case "stone_enclosure":
                        for(int i=0;i<24;i++)
                        {
                            if(i==17)continue; // entrance, with irregular surviving wall heights
                            float a=i*Mathf.PI*2/24,c=(i+1)*Mathf.PI*2/24;
                            b.Beam(new Vector3(Mathf.Cos(a)*9,0,Mathf.Sin(a)*6),
                                new Vector3(Mathf.Cos(c)*9,0,Mathf.Sin(c)*6),.9f,i%5==0?2:3,Stone);
                        }
                        b.Cone(new Vector3(3,0,1),2,6,Stone);
                        b.Beam(new Vector3(-4,0,-1),new Vector3(-4,0,3),.7f,1.7f,Stone);
                        break;
                }
            }
        }
        static void Pyramid(GeometryBuilder b,Vector3 at,float width,float height,Color color)
        {
            var top=at+Vector3.up*height;
            for(int i=0;i<4;i++)
            {
                var a=at+new Vector3(i<2?-width/2:width/2,0,i==0||i==3?-width/2:width/2);
                int j=(i+1)%4;
                var c=at+new Vector3(j<2?-width/2:width/2,0,j==0||j==3?-width/2:width/2);
                b.Quad(a,c,top,top,color*(.72f+i*.08f));
            }
        }
        static void TempleTower(GeometryBuilder b,Vector3 at,float height)
        {
            for(int level=0;level<4;level++)
                b.Box(at+Vector3.up*(height*level*.16f),new Vector3(3.2f-level*.55f,height*.18f,3.2f-level*.55f),Stone);
            b.Cone(at+Vector3.up*(height*.64f),.8f,height*.36f,Stone);
        }
        void DrawLandmarkLabels(float w,float h,List<Rect> used)
        {
            if(distance>=3300)return;
            foreach(var place in landmarks)
            {
                if(!LandmarkNearView(place))continue;
                var world=Atlas.World(place.lon,place.lat,12);
                if(Vector3.Dot(world.normalized,cam.transform.position-world)<0)continue;
                var p=cam.WorldToScreenPoint(world);
                var at=new Vector2(p.x/uiScale,(Screen.height-p.y)/uiScale);
                float width=small.CalcSize(new GUIContent(place.name)).x+14;
                var rect=new Rect(at.x-width/2,at.y+8,width,24);
                if(p.z<=0 || !CampaignLabelFits(rect,w,h))continue;
                bool overlap=false;foreach(var r in used)if(r.Overlaps(rect)){overlap=true;break;}
                if(overlap)continue;
                used.Add(rect);
                GUI.color=new Color(.04f,.08f,.08f,.8f);GUI.DrawTexture(rect,white);
                GUI.color=Empire.UI.EmpireTheme.Brass;GUI.Label(rect,place.name,small);GUI.color=Color.white;
            }
        }
        void DrawLandmarkControls()
        {
            if(GUILayout.Button(landmarkListOpen?"Hide historic places":"Historic places",button))landmarkListOpen=!landmarkListOpen;
            if(!landmarkListOpen)return;
            foreach(var place in landmarks)
            {
                if(GUILayout.Button(place.name,button))Focus(place.lon,place.lat,400);
                GUILayout.Label(place.description,small);
            }
        }
    }
}
