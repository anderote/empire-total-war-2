using UnityEngine;

namespace EmpireCampaign.Geography
{
    public sealed partial class EuropeMapView
    {
        Texture2D worldBiomes,detailBiomes;
        GameObject scenery;
        float sceneryLon=999,sceneryLat=999,sceneryRange;
        bool miniatureDetail;
        static readonly Color Stone=new Color(.64f,.60f,.49f).linear;
        static readonly Color Plaster=new Color(.83f,.77f,.62f).linear;
        static readonly Color Timber=new Color(.32f,.23f,.15f).linear;
        static readonly Color Tile=new Color(.46f,.23f,.15f).linear;
        static readonly Color Slate=new Color(.24f,.29f,.30f).linear;

        [System.Serializable] public class ArtProfile { public string settlementId,artSize,fortification,resourceArt; }
        [System.Serializable] public class ArtCatalog { public ArtProfile[] settlements; }
        void LoadArtProfiles()
        {
            var resource=EmpireCampaign.Geography.CampaignResourceData.Load("CampaignArtProfiles");
            if(resource==null)return;
            var profiles=JsonUtility.FromJson<ArtCatalog>(resource.text);
            foreach(var town in Atlas.Data.settlements)
            {
                town.artSize="town";town.fortification="open";
                foreach(var p in profiles.settlements)if(p.settlementId==town.id)
                {town.artSize=p.artSize;town.fortification=p.fortification;town.resourceArt=p.resourceArt;break;}
            }
        }
        void BuildBiomes()
        {
            worldBiomes=BiomeTexture(Atlas,1025,513);
            detailBiomes=BiomeTexture(Atlas.Detail,769,769);
            reliefMaterial.SetTexture("_WorldBiomes",worldBiomes);
            reliefMaterial.SetTexture("_DetailBiomes",detailBiomes);
        }
        static Texture2D BiomeTexture(GeographyAtlas atlas,int width,int height)
        {
            var pixels=new Color32[width*height];var d=atlas.Data;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                pixels[y*width+x]=CampaignLandscape.Biome(atlas,Mathf.Lerp(d.west,d.east,x/(float)(width-1)),Mathf.Lerp(d.south,d.north,y/(float)(height-1)));
            var t=new Texture2D(width,height,TextureFormat.RGBA32,false,true){name="Campaign climate and coast",filterMode=FilterMode.Bilinear,wrapModeU=atlas.Spherical?TextureWrapMode.Repeat:TextureWrapMode.Clamp,wrapModeV=TextureWrapMode.Clamp};
            t.SetPixels32(pixels);t.Apply(false,true);return t;
        }
        void UpdateScenery()
        {
            if(distance>2800){if(scenery!=null)scenery.SetActive(false);return;}
            if(scenery!=null)scenery.SetActive(true);
            if(Mathf.Abs(Mathf.DeltaAngle(sceneryLon,focusLon))<.35f && Mathf.Abs(sceneryLat-focusLat)<.35f && Mathf.Abs(sceneryRange-distance)<Mathf.Max(80,distance*.25f))return;
            RemoveScenery();
            var b=new GeometryBuilder();
            // Fixed geographic lattice and jitter keep woodland stable while panning.
            const float step=.16f;
            float span=Mathf.Clamp(distance/180,1.4f,8);
            int south=Mathf.FloorToInt(Mathf.Max(-85,focusLat-span)/step),north=Mathf.CeilToInt(Mathf.Min(85,focusLat+span)/step);
            float stretch=1/Mathf.Max(.35f,Mathf.Cos(focusLat*Mathf.Deg2Rad));
            int west=Mathf.FloorToInt((focusLon-span*stretch)/step),east=Mathf.CeilToInt((focusLon+span*stretch)/step);
            int count=0;
            for(int y=south;y<=north;y++)for(int x=west;x<=east;x++)
            {
                if(count>=6500)break;
                float seed=Hash(x,y),lon=Mathf.DeltaAngle(0,(x+seed*.8f)*step),lat=(y+Hash(y,x)*.8f)*step;
                if(!Atlas.IsDry(lon,lat))continue;
                float h=Atlas.HeightMeters(lon,lat),forest=CampaignLandscape.Forest(lon,lat,h);
                if(forest<.35f||seed>forest*.82f||NearTown(lon,lat))continue;
                // Steep rock faces remain bare.
                if(Mathf.Abs(h-Atlas.HeightMeters(lon+.035f,lat))>450)continue;
                b.Frame(Atlas.World(lon,lat,.5f),Quaternion.FromToRotation(Vector3.up,GeographyAtlas.Normal(lon,lat)));
                bool pine=Mathf.Abs(lat)>48||h>1100;
                Color leaves=Color.Lerp(new Color(.17f,.29f,.15f).linear,new Color(.31f,.39f,.18f).linear,seed);
                for(int k=0;k<3;k++)
                {
                    var at=new Vector3((k-1)*3,0,Hash(x+k,y)*4-2);
                    float size=1.8f+Hash(x,y+k)*1.2f;
                    b.Box(at,new Vector3(.45f,size*.5f,.45f),Timber);
                    if(pine){b.Cone(at+Vector3.up*size*.3f,size*.65f,size*1.7f,leaves);b.Cone(at+Vector3.up*size,size*.5f,size,leaves*1.1f);}
                    else {b.Cone(at+Vector3.up*size*.4f,size,size*.8f,leaves);b.Cone(at+Vector3.up*size*1.2f,size*.85f,-size*.8f,leaves*.85f);}
                }
                count++;
            }
            scenery=MeshObject("Local clustered woodland",b.Mesh("Campaign forests"),colorMaterial);
            sceneryLon=focusLon;sceneryLat=focusLat;sceneryRange=distance;
        }
        bool NearTown(float lon,float lat)
        {
            foreach(int i in labelCandidates)
            {
                var t=Atlas.Data.settlements[i];
                float dx=Mathf.DeltaAngle(t.lon,lon)*Mathf.Cos(lat*Mathf.Deg2Rad),dy=t.lat-lat;
                if(dx*dx+dy*dy<.065f)return true;
            }
            return false;
        }
        static float Hash(int x,int y)
        {
            unchecked {uint n=(uint)(x*374761393+y*668265263);n=(n^(n>>13))*1274126177u;return (n^(n>>16))/4294967295f;}
        }
        void RemoveScenery()
        {
            if(scenery==null)return;
            var mesh=scenery.GetComponent<MeshFilter>().sharedMesh;meshes.Remove(mesh);Destroy(mesh);Destroy(scenery);
        }
        void DestroyScenery(){RemoveScenery();if(worldBiomes!=null)Destroy(worldBiomes);if(detailBiomes!=null)Destroy(detailBiomes);}

        void DrawSettlement(GeometryBuilder b,Settlement town)
        {
            bool big=town.artSize=="city" || (town.artSize!="town" && town.displayRank<=3);
            bool walled=town.fortification=="walls" || (town.fortification!="open" && big);
            var frame=Quaternion.LookRotation(GeographyAtlas.North(town.lon,town.lat),GeographyAtlas.Normal(town.lon,town.lat));
            var center=Atlas.World(town.lon,town.lat,1);
            b.Frame(center,frame,big?1.9f:1.65f);
            Color roof=town.lat>47?Slate:Tile;
            float radius=big?10:5.5f;
            int count=big?32:12;
            for(int i=0;i<count;i++)
            {
                float angle=i*2.39996f,rad=i==0?0:Mathf.Sqrt(i/(float)count)*(radius-1.8f);
                Vector3 at=new Vector3(Mathf.Cos(angle)*rad,0,Mathf.Sin(angle)*rad);
                float h=i==0?(big?4:2.5f):1.1f+Hash(i,count)*1.8f;
                b.Box(at,new Vector3(1.5f,h,1.3f),Color.Lerp(Stone,Plaster,Hash(i,17)));
                b.Roof(at+Vector3.up*h,new Vector3(1.8f,.8f,1.6f),roof);
            }
            // Civic spire and market square make capitals legible beyond individual roofs.
            b.Box(new Vector3(0,0,-2),new Vector3(1.5f,big?6:4,1.5f),Plaster);
            b.Cone(new Vector3(0,big?6:4,-2),1.2f,2,roof);
            if(walled)DrawWalls(b,radius);
            else DrawFields(b,new Vector3(radius+3,0,0));
            var riverAccess=riverways.Nearby(town.lon,town.lat,15);
            if(riverAccess!=null)DrawRiverPort(b,town,riverAccess);
            else if(town.port)DrawCoastalPort(b,town.lon,town.lat);
            if(town.resourceArt=="mine")DrawNearbyMine(b,town.lon,town.lat);
        }
        void DrawWalls(GeometryBuilder b,float radius)
        {
            const int segments=16;
            for(int i=0;i<segments;i++)
            {
                float a=i*Mathf.PI*2/segments,c=(i+1)*Mathf.PI*2/segments;
                Vector3 p=new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius),q=new Vector3(Mathf.Cos(c)*radius,0,Mathf.Sin(c)*radius);
                if(i!=3) // True gap beneath the gatehouse.
                {
                    b.Beam(p,q,.8f,2.5f,Stone);
                    for(int k=0;k<4;k++)b.Box(Vector3.Lerp(p,q,(k+.5f)/4)+Vector3.up*2.5f,new Vector3(.55f,.55f,.7f),Plaster);
                }
                if(i%2==0){b.Box(p,new Vector3(1.7f,3.5f,1.7f),Stone);b.Box(p+Vector3.up*3.5f,new Vector3(2,.5f,2),Plaster);}
                if(i==3){b.Box(p,new Vector3(1.5f,3.8f,1.5f),Stone);b.Box(q,new Vector3(1.5f,3.8f,1.5f),Stone);b.Beam(p+Vector3.up*3,q+Vector3.up*3,1.2f,1,Plaster);}
            }
        }
        void DrawFields(GeometryBuilder b,Vector3 at)
        {
            for(int i=0;i<4;i++)
            {
                Vector3 p=at+new Vector3(i%2*3,0,i/2*3);
                b.Box(p,new Vector3(2.7f,.12f,2.7f),Color.Lerp(new Color(.47f,.43f,.20f).linear,new Color(.37f,.43f,.23f).linear,i/3f));
                for(int k=0;k<5;k++)b.Box(p+new Vector3(k*.48f-1,.12f,0),new Vector3(.12f,.08f,2.5f),Timber);
            }
        }
        void DrawLocalTrack(GeometryBuilder b,GeoPoint from,GeoPoint to)
        {
            float km=RiverwayNavigation.Distance(from,to);
            int steps=Mathf.Max(1,Mathf.CeilToInt(km/1.5f));
            var points=new Vector3[steps+1];
            for(int i=0;i<=steps;i++)
            {
                float t=i/(float)steps,lon=from.lon+Mathf.DeltaAngle(from.lon,to.lon)*t,lat=Mathf.Lerp(from.lat,to.lat,t);
                if(!Atlas.IsDry(lon,lat))return; // No invented causeways across lakes or sea.
                points[i]=Atlas.World(lon,lat,.35f);
            }
            b.Frame(Vector3.zero,Quaternion.identity);
            for(int i=1;i<points.Length;i++)
            {
                Vector3 p=points[i-1],q=points[i],side=Vector3.Cross((p+q).normalized,q-p).normalized*.18f;
                b.Quad(p-side,p+side,q+side,q-side,new Color(.40f,.34f,.23f).linear);
            }
        }
        void DrawSupportSite(GeometryBuilder b,RegionSite site)
        {
            b.Frame(Atlas.World(site.lon,site.lat,1),Quaternion.LookRotation(GeographyAtlas.North(site.lon,site.lat),GeographyAtlas.Normal(site.lon,site.lat)),1.5f);
            if(site.kind=="Mine"){DrawMine(b);return;}
            if(site.kind=="Port"){DrawCoastalPort(b,site.lon,site.lat);return;}
            if(site.kind=="Fortress"){DrawWalls(b,4);b.Box(Vector3.zero,new Vector3(2,4,2),Stone);return;}
            for(int i=0;i<6;i++)
            {
                var at=new Vector3(i%3*2.2f,0,i/3*2.5f);
                b.Box(at,new Vector3(1.6f,1.6f,1.6f),Plaster);b.Roof(at+Vector3.up*1.6f,new Vector3(1.9f,.7f,1.9f),Tile);
            }
            if(site.kind=="Workshop")b.Box(new Vector3(-1,0,0),new Vector3(.7f,4,.7f),Stone);
            else if(site.kind=="University"){b.Box(new Vector3(2,0,-2),new Vector3(5,2.5f,2),Plaster);b.Roof(new Vector3(2,2.5f,-2),new Vector3(5.4f,1,2.4f),Slate);}
            else DrawFields(b,new Vector3(6,0,0));
        }
        void DrawRiverPort(GeometryBuilder b,GeoPoint town,NavigableRiver river)
        {
            RiverwayNavigation.Project(river,new Vector2(town.lon,town.lat),out float cursor,out GeoPoint point,out _);
            int i=Mathf.Clamp(Mathf.FloorToInt(cursor),0,river.points.Length-2);
            var a=river.points[i];var c=river.points[i+1];
            Vector3 normal=GeographyAtlas.Normal(point.lon,point.lat);
            Vector3 direction=GeographyAtlas.Normal(c.lon,c.lat)-GeographyAtlas.Normal(a.lon,a.lat);
            b.Frame(Atlas.World(point.lon,point.lat,1.4f),Quaternion.LookRotation(Vector3.ProjectOnPlane(direction,normal),normal),1.4f);
            b.Box(new Vector3(1.8f,0,0),new Vector3(1.2f,.35f,5),Timber);
            b.Box(new Vector3(0,0,1),new Vector3(1.2f,.6f,3),Timber);
            b.Box(new Vector3(0,.6f,1),new Vector3(.15f,2.8f,.15f),Timber);
            b.Quad(new Vector3(0,1,1),new Vector3(0,3.3f,1),new Vector3(0,2.9f,2.4f),new Vector3(0,1,2.4f),Plaster);
        }
        void DrawCoastalPort(GeometryBuilder b,float lon,float lat)
        {
            // Search the real land mask; inland/river ports without nearby sea get no invented harbor.
            for(int ring=1;ring<=12;ring++)for(int i=0;i<24;i++)
            {
                float a=i*Mathf.PI/12,dy=Mathf.Sin(a)*ring*.025f,dx=Mathf.Cos(a)*ring*.025f/Mathf.Max(.25f,Mathf.Cos(lat*Mathf.Deg2Rad));
                float x=Mathf.DeltaAngle(0,lon+dx),y=lat+dy;
                if(Atlas.IsDry(x,y))continue;
                Vector3 up=GeographyAtlas.Normal(x,y),toward=(GeographyAtlas.Normal(x,y)-GeographyAtlas.Normal(lon,lat)).normalized;
                b.Frame(up*(GeographyAtlas.EarthRadiusKm+.6f),Quaternion.LookRotation(Vector3.ProjectOnPlane(toward,up),up),1.6f);
                for(int k=-1;k<=1;k++)b.Box(new Vector3(k*4,0,0),new Vector3(1.8f,.5f,8),Timber);
                b.Box(new Vector3(0,0,-4),new Vector3(12,.5f,1.5f),Stone);
                b.Box(new Vector3(-5,0,-4),new Vector3(1.4f,4,1.4f),Plaster);b.Cone(new Vector3(-5,4,-4),1.1f,1.4f,Tile);
                for(int k=-1;k<=1;k+=2)
                {
                    var p=new Vector3(k*4+1.6f,0,1);
                    b.Box(p,new Vector3(1.3f,.6f,4),Timber);b.Box(p,new Vector3(.15f,4,.15f),Timber);
                    b.Quad(p+new Vector3(0,1,0),p+new Vector3(0,4,0),p+new Vector3(0,3.5f,2),p+new Vector3(0,1,2),Plaster);
                }
                return;
            }
        }
        void DrawNearbyMine(GeometryBuilder b,float lon,float lat)
        {
            // A resource-art override places a vignette, never a new economic resource.
            float best=-1,bx=lon,by=lat;
            for(int i=0;i<16;i++)
            {
                float a=i*Mathf.PI/8,x=lon+Mathf.Cos(a)*.18f,y=lat+Mathf.Sin(a)*.18f;
                if(!Atlas.IsDry(x,y))continue;
                float h=Atlas.HeightMeters(x,y);if(h>best){best=h;bx=x;by=y;}
            }
            if(best<0)return;
            b.Frame(Atlas.World(bx,by,1),Quaternion.LookRotation(GeographyAtlas.North(bx,by),GeographyAtlas.Normal(bx,by)),1.5f);DrawMine(b);
        }
        void DrawMine(GeometryBuilder b)
        {
            b.Cone(Vector3.zero,4,2.8f,Stone*.6f);
            b.Box(new Vector3(0,0,2.8f),new Vector3(2.6f,2.3f,.3f),Timber);
            b.Box(new Vector3(0,0,3),new Vector3(1.7f,1.8f,.35f),new Color(.035f,.03f,.025f));
            for(int i=-1;i<=1;i+=2){b.Box(new Vector3(i*2,0,-2),new Vector3(.45f,6,.45f),Timber);b.Box(new Vector3(i*.6f,0,6),new Vector3(.12f,.15f,6),Slate);}
            b.Box(new Vector3(0,5.5f,-2),new Vector3(5,.5f,1),Timber);
            b.Box(new Vector3(0,.2f,5),new Vector3(1.2f,.8f,1.5f),Timber);
        }
    }
}
