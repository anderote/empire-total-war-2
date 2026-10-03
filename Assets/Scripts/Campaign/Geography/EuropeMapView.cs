using System;
using Empire.UI;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireCampaign.Geography
{
    /// <summary>Campaign-owned atlas presentation. Army/province state can bind to the same coordinates.</summary>
    public sealed partial class EuropeMapView : MonoBehaviour
    {
        public GeographyAtlas Atlas { get; private set; }
        Camera cam;
        Material colorMaterial, reliefMaterial;
        Texture2D regionIds,regionOwners,worldDryLand,detailDryLand;
        string[] paintedOwners;
        bool showFactionColors=true,showFactionKey,qaRunning;
        Vector2 factionScroll;
        RegionCatalog catalog;
        RegionCampaign regionCampaign;
        CampaignRegion selectedRegion;
        GameObject regionBorders,provinceBorders,selectedRegionOverlay;
        string constructionMessage="Select a region to manage its shared building slots.";
        string remoteName;
        GameObject remotePatch;
        float patchLon=999,patchLat=999;
        GameObject roads, trails, seaRoutes, riverLayer, navigableRivers, highlights;
        RiverwayCatalog riverways;
        bool showTrails=true,showRiverways=true;
        Vector3 focus;
        float focusLon=-1,focusLat=52;
        float distance = 2350, pitch = 64, yaw;
        int selected = -1, hovered = -1, origin = -1;
        bool showRoads = true, showSea = false, showRivers = true, showMountains = true, credits;
        string search = "", plannerMessage = "Select a town to inspect its connections.";
        Vector2 listScroll, detailScroll;
        GUIStyle heading, text, small, townText, mountainText, button;
        Texture2D panel, header, gold, white;
        readonly List<GameObject> owned = new List<GameObject>();
        readonly List<Material> materials = new List<Material>();
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<int> visibleTowns = new List<int>();
        readonly Dictionary<int,Rect> townRects = new Dictionary<int,Rect>();
        Vector2 lastMouse;
        Vector2 pressMouse;
        Vector3[] townWorld;
        readonly List<int> labelCandidates=new List<int>();
        readonly List<int> searchResults=new List<int>();
        string lastSearch=null;
        GameObject townMiniatures;
        float miniatureLon=999,miniatureLat=999,nextMiniatures,nextPolitics;
        float uiScale = 1;

        void Start()
        {
            Atlas = new GeographyAtlas();
            catalog=RegionCatalog.Load();regionCampaign=new RegionCampaign(catalog);
            Atlas.Data.settlements=new Settlement[catalog.regions.Length];
            for(int i=0;i<catalog.regions.Length;i++)Atlas.Data.settlements[i]=catalog.regions[i].capital;
            cam = new GameObject("Campaign map camera").AddComponent<Camera>();
            owned.Add(cam.gameObject);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.065f,.11f,.13f);
            cam.nearClipPlane = .3f; cam.farClipPlane = 70000; cam.fieldOfView = 42;
            colorMaterial = MakeMaterial("EmpireCampaign/MapColor");
            reliefMaterial = MakeMaterial("EmpireCampaign/Relief");
            reliefMaterial.mainTexture = Resources.Load<Texture2D>("WorldGeography/relief");
            reliefMaterial.SetTexture("_DetailTex",Resources.Load<Texture2D>("EuropeGeography/relief"));
            var detail=Atlas.Detail.Data;
            reliefMaterial.SetVector("_DetailBounds",new Vector4((detail.west+180)/360,(detail.south+90)/180,(detail.east+180)/360,(detail.north+90)/180));
            BuildPolitics();
            BuildTerrain();
            BuildBiomes();
            LoadArtProfiles();
            LoadLandmarks();
            BuildAtmosphere();
            regionBorders=BuildPaths("Campaign region boundaries",catalog.regionBorders,new Color(.10f,.12f,.12f).linear,.55f,.7f);
            provinceBorders=BuildPaths("Campaign province boundaries",catalog.provinceBorders,new Color(.035f,.045f,.045f).linear,1.3f,.9f);
            riverLayer = BuildPaths("Rivers",Atlas.Data.rivers,new Color(.28f,.52f,.61f),1.1f,.6f);
            riverways=RiverwayCatalog.Load();
            navigableRivers=BuildPaths("Navigable river reaches",riverways.reaches,new Color(.16f,.39f,.49f).linear,3.2f,1.1f);
            var landPaths = new List<GeoPath>(); var trailPaths=new List<GeoPath>(); var seaPaths = new List<GeoPath>();
            foreach(var r in Atlas.Data.routes) (r.mode=="land"?(r.surface=="trail"?trailPaths:landPaths):seaPaths).Add(r);
            roads = BuildPaths("Strategic land corridors",landPaths.ToArray(),new Color(.48f,.37f,.23f).linear,.7f,.5f);
            trails=BuildPaths("Campaign trails",trailPaths.ToArray(),new Color(.36f,.32f,.21f).linear,.45f,.5f,true);
            seaRoutes = BuildPaths("Provisional maritime connections",seaPaths.ToArray(),new Color(.37f,.6f,.65f),2.8f,7,true);
            seaRoutes.SetActive(showSea);
            townWorld=new Vector3[Atlas.Data.settlements.Length];
            for(int i=0;i<townWorld.Length;i++)townWorld[i]=Atlas.World(Atlas.Data.settlements[i].lon,Atlas.Data.settlements[i].lat,5);
            Focus(-1,52,2350);
            Debug.Log("EUROPE_MAP_READY towns="+Atlas.Data.settlements.Length+" routes="+Atlas.Data.routes.Length);
            string[] args = Environment.GetCommandLineArgs();
            for(int i=0;i<args.Length;i++)
                if(args[i]=="-country" && i+1<args.Length)StartCountry(args[i+1]);
                else if(args[i]=="-mapScreenshot" && i+1<args.Length) StartCoroutine(Capture(args[i+1]));
                else if(args[i]=="-mapQA" && i+1<args.Length)StartCoroutine(QA(args[i+1]));
                else if(args[i]=="-franceCampaignQA" && i+1<args.Length)StartCoroutine(FranceCampaignQA(args[i+1]));
                else if(args[i]=="-campaignUIQA" && i+1<args.Length)StartCoroutine(CampaignUIQA(args[i+1]));
        }

        Material MakeMaterial(string shader)
        {
            var found = Shader.Find(shader);
            if(found==null) throw new InvalidOperationException("Map shader missing: "+shader);
            var m = new Material(found); materials.Add(m); return m;
        }
        GameObject MeshObject(string name,Mesh mesh,Material material)
        {
            var go = new GameObject(name); go.transform.SetParent(transform);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial=material;
            meshes.Add(mesh); return go;
        }
        void BuildPolitics()
        {
            var bytes=EmpireCampaign.Geography.CampaignResourceData.Load("WorldGeography/region-ids").bytes;
            if(bytes.Length!=catalog.politicalRasterWidth*catalog.politicalRasterHeight)
                throw new InvalidOperationException("Region ownership raster has incorrect dimensions.");
            regionIds=new Texture2D(catalog.politicalRasterWidth,catalog.politicalRasterHeight,TextureFormat.R8,false,true);
            regionIds.filterMode=FilterMode.Point;regionIds.wrapModeU=TextureWrapMode.Repeat;regionIds.wrapModeV=TextureWrapMode.Clamp;
            regionIds.LoadRawTextureData(bytes);regionIds.Apply(false,true);
            regionOwners=new Texture2D(256,1,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            reliefMaterial.SetTexture("_RegionIds",regionIds);reliefMaterial.SetTexture("_RegionOwners",regionOwners);
            worldDryLand=DryLandTexture(Atlas);detailDryLand=DryLandTexture(Atlas.Detail);
            reliefMaterial.SetTexture("_WorldDryLand",worldDryLand);reliefMaterial.SetTexture("_DetailDryLand",detailDryLand);
            paintedOwners=new string[catalog.regions.Length];RefreshOwnershipColors();
            reliefMaterial.SetFloat("_PoliticalOpacity",.35f);reliefMaterial.SetFloat("_SelectedRegion",0);
        }
        Texture2D DryLandTexture(GeographyAtlas atlas)
        {
            var data=new byte[atlas.DryLand.Length];
            for(int i=0;i<data.Length;i++)data[i]=atlas.DryLand[i]>0?(byte)255:(byte)0;
            var texture=new Texture2D(atlas.Data.width,atlas.Data.height,TextureFormat.R8,false,true){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            texture.LoadRawTextureData(data);texture.Apply(false,true);return texture;
        }
        void RefreshOwnershipColors()
        {
            bool changed=false;
            for(int i=0;i<catalog.regions.Length;i++)
            {
                string owner=regionCampaign.State(catalog.regions[i].id).owner;
                if(paintedOwners[i]==owner)continue;
                paintedOwners[i]=owner;changed=true;
                regionOwners.SetPixel(i+1,0,catalog.Faction(owner).MapColor.linear);
            }
            if(changed)regionOwners.Apply(false,false);
        }
        void BuildTerrain()
        {
            var d=Atlas.Detail.Data;
            var lons=new List<float>();var lats=new List<float>();
            for(int i=0;i<=512;i++)lons.Add(-180+i*360f/512);
            for(int i=0;i<=256;i++)lats.Add(-90+i*180f/256);
            lons.Add(d.west);lons.Add(d.east);lats.Add(d.south);lats.Add(d.north);
            lons.Sort();lats.Sort();
            BuildSurface("World relief",lons,lats,true);
            lons.Clear();lats.Clear();
            for(int i=0;i<=512;i++)lons.Add(Mathf.Lerp(d.west,d.east,i/512f));
            for(int i=0;i<=512;i++)lats.Add(Mathf.Lerp(d.south,d.north,i/512f));
            BuildSurface("Europe & North Africa detail",lons,lats,false);
        }
        void BuildSurface(string name,List<float> lons,List<float> lats,bool omitDetail)
        {
            const int cells=128;
            var d=Atlas.Detail.Data;
            for(int cy=0;cy<lats.Count-1;cy+=cells)for(int cx=0;cx<lons.Count-1;cx+=cells)
            {
                int nx=Mathf.Min(cells,lons.Count-1-cx),ny=Mathf.Min(cells,lats.Count-1-cy);
                var v = new Vector3[(nx+1)*(ny+1)]; var uv = new Vector2[v.Length];
                var tri=new List<int>(nx*ny*6);
                for(int y=0;y<=ny;y++) for(int x=0;x<=nx;x++)
                {
                    float lon=lons[cx+x],lat=lats[cy+y];
                    int i=y*(nx+1)+x;uv[i]=new Vector2((lon+180)/360,(lat+90)/180);
                    v[i]=Atlas.World(lon,lat);
                    if(omitDetail)v[i]=GeographyAtlas.Normal(lon,lat)*GeographyAtlas.EarthRadiusKm;
                    if(x<nx && y<ny)
                    {
                        float midlon=(lon+lons[cx+x+1])/2,midlat=(lat+lats[cy+y+1])/2;
                        if(omitDetail && Atlas.Detail.Contains(midlon,midlat))continue;
                        // Spherical winding faces outward; parallels at the poles collapse naturally.
                        tri.Add(i);tri.Add(i+nx+1);tri.Add(i+1);
                        tri.Add(i+1);tri.Add(i+nx+1);tri.Add(i+nx+2);
                    }
                }
                if(tri.Count==0)continue;
                var mesh=new Mesh {name=name,vertices=v,uv=uv,triangles=tri.ToArray()};
                mesh.RecalculateNormals();mesh.RecalculateBounds();
                MeshObject(name+" "+cx+","+cy,mesh,reliefMaterial);
            }
        }

        GameObject BuildPaths(string name,GeoPath[] paths,Color color,float width,float lift,bool dashed=false)
        {
            var b = new GeometryBuilder();
            foreach(var path in paths)
            {
                for(int i=1;i<path.points.Length;i++)
                {
                    var a=path.points[i-1];var c=path.points[i];
                    float lonDelta=Mathf.DeltaAngle(a.lon,c.lon);
                    float length=Vector3.Distance(GeographyAtlas.Normal(a.lon,a.lat),GeographyAtlas.Normal(c.lon,c.lat))*GeographyAtlas.EarthRadiusKm;
                    int steps=Mathf.Max(1,Mathf.CeilToInt(length/8));
                    for(int j=0;j<steps;j++)
                    {
                        if(dashed && j%4>=2) continue;
                        float f=j/(float)steps,g=(j+1)/(float)steps;
                        var p=Atlas.World(a.lon+lonDelta*f,Mathf.Lerp(a.lat,c.lat,f),lift);
                        var q=Atlas.World(a.lon+lonDelta*g,Mathf.Lerp(a.lat,c.lat,g),lift);
                        var side=Vector3.Cross((p+q).normalized,q-p).normalized*width/2;
                        b.Quad(p-side,p+side,q+side,q-side,color);
                    }
                }
            }
            return MeshObject(name,b.Mesh(name),colorMaterial);
        }

        void BuildTowns()
        {
            if(townMiniatures!=null)
            {
                var old=townMiniatures.GetComponent<MeshFilter>().sharedMesh;meshes.Remove(old);Destroy(old);Destroy(townMiniatures);
            }
            var b=new GeometryBuilder();
            foreach(int index in labelCandidates)
            {
                var town=Atlas.Data.settlements[index];
                DrawSettlement(b,town);
                var region=catalog.ForCity(town.id);
                if(region!=null && distance<1600)
                    foreach(var site in region.sites)
                        if(Atlas.IsDry(site.lon,site.lat)){DrawLocalTrack(b,town,site);DrawSupportSite(b,site);}
            }
            DrawLandmarks(b);
            townMiniatures=MeshObject("Nearby settlement miniatures",b.Mesh("Towns"),colorMaterial);
        }

        public void Focus(float lon,float lat,float range)
        {
            focusLon=Mathf.DeltaAngle(0,lon);focusLat=Mathf.Clamp(lat,-88,88);distance=range;
        }
        void Update()
        {
            if(Atlas==null) return;
            if(Time.unscaledTime>=nextPolitics){RefreshOwnershipColors();nextPolitics=Time.unscaledTime+.5f;}
            uiScale=CampaignUILayout.Scale(Screen.width,Screen.height);
            Vector2 mouse=Input.mousePosition;
            Vector2 ui=new Vector2(mouse.x/uiScale,(Screen.height-mouse.y)/uiScale);
            float screenW=Screen.width/uiScale,screenH=Screen.height/uiScale;
            bool overUI=CampaignBlocksPoint(ui,screenW,screenH) || (showFactionKey && ui.x<340 && ui.y<600);
            if(Input.GetMouseButtonDown(0)||Input.GetMouseButtonDown(1))campaignPressOnMap=!overUI;
            if(!qaRunning && Input.GetKeyDown(KeyCode.Escape))
            {
                if(campaignOptions)campaignOptions=false;
                else if(campaignPopup!="")CloseCampaignDialog();
                else if(campaignNavigation)campaignNavigation=false;
                else if(choosingCampaignDestination){choosingCampaignDestination=false;countryMessage="Destination selection cancelled.";}
                else if(selectedRegimentId!="")selectedRegimentId="";
                else if(countryPage!="Map"||credits){countryPage="Map";campaignPopup="";credits=false;}
                else campaignMenu=!campaignMenu;
                ResetCampaignPointer();ApplyCampaignCursor();
                return;
            }
            ResetCampaignPointer();
            hovered=-1;
            foreach(var pair in townRects) if(pair.Value.Contains(ui)) {hovered=pair.Key;break;}
            if(!qaRunning && !overUI)
            {
                UpdateCampaignPointer(mouse,ui,screenW,screenH);
                distance=Mathf.Clamp(distance*Mathf.Exp(-Input.mouseScrollDelta.y*.14f),120,28000);
                if(Input.GetMouseButtonDown(0)||Input.GetMouseButtonDown(1))pressMouse=mouse;
                if(Input.GetMouseButton(2) || Input.GetMouseButton(0))
                {
                    Vector2 delta=mouse-lastMouse;
                    Pan(-delta.x*distance/Screen.height, -delta.y*distance/Screen.height);
                }
                if(Input.GetMouseButton(1))
                {
                    Vector2 delta=mouse-lastMouse;
                    yaw+=delta.x*.2f;pitch=Mathf.Clamp(pitch-delta.y*.13f,32,88);
                }
                if(campaignPressOnMap && Input.GetMouseButtonUp(0) && Vector2.Distance(pressMouse,mouse)<8)
                {
                    HandleCampaignPointerClick(false);
                }
                if(campaignPressOnMap&&Input.GetMouseButtonUp(1)&&Vector2.Distance(pressMouse,mouse)<8)
                    HandleCampaignPointerClick(true);
            }
            if(!qaRunning && !GUIUtilityHasTextFocus() && (countryCampaign==null || countryPage=="Map") && !campaignMenu && !credits && !campaignOptions && campaignPopup=="" && selectedRegimentId=="")
            {
                float dx=(Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0);
                float dz=(Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0);
                float panSpeed=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift)?2.5f:1f;
                Pan(dx*distance*.55f*Time.unscaledDeltaTime*panSpeed,dz*distance*.55f*Time.unscaledDeltaTime*panSpeed);
                if(Input.GetKey(KeyCode.Q))yaw-=45*Time.unscaledDeltaTime;
                if(Input.GetKey(KeyCode.E))yaw+=45*Time.unscaledDeltaTime;
                if(Input.GetKeyDown(KeyCode.Home))Focus(-1,52,2350);
                if(Input.GetKeyDown(KeyCode.Escape)){campaignPopup="";selectedArmyId="";selectedRegimentId="";origin=-1;ClearHighlight();plannerMessage="Route selection cleared.";}
            }
            focusLon=Mathf.DeltaAngle(0,focusLon);focusLat=Mathf.Clamp(focusLat,-88,88);
            Vector3 normal=GeographyAtlas.Normal(focusLon,focusLat),north=GeographyAtlas.North(focusLon,focusLat);
            north=Quaternion.AngleAxis(yaw,normal)*north;
            focus=normal*GeographyAtlas.EarthRadiusKm;
            float tilt=(90-pitch)*Mathf.Deg2Rad*Mathf.Clamp01(1-distance/12000);
            cam.transform.position=focus+(normal*Mathf.Cos(tilt)-north*Mathf.Sin(tilt))*distance;
            cam.transform.rotation=Quaternion.LookRotation(focus-cam.transform.position,north);
            LoadRemoteDetail();
            PrepareLabels();
            if(Time.unscaledTime>=nextMiniatures && (Mathf.Abs(Mathf.DeltaAngle(miniatureLon,focusLon))>.8f || Mathf.Abs(miniatureLat-focusLat)>.8f || miniatureDetail!=(distance<1600)))
            {
                BuildTowns();miniatureDetail=distance<1600;miniatureLon=focusLon;miniatureLat=focusLat;nextMiniatures=Time.unscaledTime+.5f;
            }
            if(townMiniatures!=null)townMiniatures.SetActive(distance<5000);
            trails.SetActive(showTrails && distance<8500);navigableRivers.SetActive(showRiverways && distance<8500);
            roads.SetActive(showRoads && distance<8500);riverLayer.SetActive(showRivers && distance<8500);
            regionBorders.SetActive(distance<8500);
            provinceBorders.SetActive(distance<8500);
            UpdateCampaignArmyTokens();
            ApplyCampaignCursor();
            UpdateScenery();
            UpdateAtmosphere();
            lastMouse=mouse;
        }
        void Pan(float eastKm,float northKm)
        {
            float angle=yaw*Mathf.Deg2Rad;
            float east=eastKm*Mathf.Cos(angle)-northKm*Mathf.Sin(angle);
            float north=eastKm*Mathf.Sin(angle)+northKm*Mathf.Cos(angle);
            focusLon+=east/(111.32f*Mathf.Max(.15f,Mathf.Cos(focusLat*Mathf.Deg2Rad)));
            focusLat+=north/111.32f;
        }
        void PrepareLabels()
        {
            labelCandidates.Clear();
            int rank=distance>10000?2:distance>5000?3:distance>2500?5:distance>1600?7:20;
            float angular=Mathf.Min(85,distance/150+1)*Mathf.Deg2Rad;
            float threshold=Mathf.Cos(angular);
            Vector3 normal=GeographyAtlas.Normal(focusLon,focusLat);
            for(int i=0;i<townWorld.Length;i++)
            {
                var town=Atlas.Data.settlements[i];
                if(i!=selected && town.displayRank>rank)continue;
                Vector3 p=townWorld[i];
                if(Vector3.Dot(p.normalized,normal)<threshold || Vector3.Dot(p.normalized,cam.transform.position-p)<=0)continue;
                if(labelCandidates.Count<700 || i==selected)labelCandidates.Add(i);
            }
        }
        bool GUIUtilityHasTextFocus()=>GUI.GetNameOfFocusedControl()=="townSearch";
        bool GlobePoint(Vector2 mouse,out Vector2 point)
        {
            Ray ray=cam.ScreenPointToRay(mouse);float radius=GeographyAtlas.EarthRadiusKm;Vector3 hit=Vector3.zero;
            for(int i=0;i<3;i++)
            {
                float b=Vector3.Dot(ray.origin,ray.direction),c=ray.origin.sqrMagnitude-radius*radius;
                float disc=b*b-c;if(disc<0){point=Vector2.zero;return false;}
                float t=-b-Mathf.Sqrt(disc);if(t<0){point=Vector2.zero;return false;}
                hit=ray.origin+ray.direction*t;Vector3 n=hit.normalized;
                float lon=Mathf.Atan2(-n.x,n.z)*Mathf.Rad2Deg,lat=Mathf.Asin(n.y)*Mathf.Rad2Deg;
                radius=GeographyAtlas.EarthRadiusKm+Mathf.Max(0,Atlas.HeightMeters(lon,lat))*.035f;
            }
            hit.Normalize();point=new Vector2(Mathf.Atan2(-hit.x,hit.z)*Mathf.Rad2Deg,Mathf.Asin(hit.y)*Mathf.Rad2Deg);return true;
        }
        void DrawSelectedRegion()
        {
            reliefMaterial.SetFloat("_SelectedRegion",selectedRegion==null?0:Array.IndexOf(catalog.regions,selectedRegion)+1);
            if(selectedRegionOverlay!=null)
            {
                var mesh=selectedRegionOverlay.GetComponent<MeshFilter>().sharedMesh;meshes.Remove(mesh);Destroy(mesh);Destroy(selectedRegionOverlay);
            }
            if(selectedRegion==null)return;
            var paths=new List<GeoPath>();foreach(var p in selectedRegion.polygons)foreach(var ring in p.rings)paths.Add(ring);
            selectedRegionOverlay=BuildPaths("Selected region",paths.ToArray(),EmpireTheme.Brass.linear,.9f,.8f);
        }
        void LoadRemoteDetail()
        {
            if(distance>8500 || Atlas.Detail.Contains(focusLon,focusLat))
            {if(remotePatch!=null)remotePatch.SetActive(false);return;}
            string name=focusLon<-30?(focusLat>8?"NorthAmerica":"SouthAmerica"):
                focusLon>=95&&focusLat<6?"Australasia":focusLon>=45?"Asia":
                focusLon>=-20&&focusLon<=55&&focusLat<38?"Africa":null;
            if(name==null){if(remotePatch!=null)remotePatch.SetActive(false);return;}
            bool changed=name!=remoteName;
            if(changed)
            {
                Atlas.RemoteDetail=null;remoteName=name;
                Atlas.RemoteDetail=new GeographyAtlas(name+"/atlas",false);
                for(int i=0;i<townWorld.Length;i++)townWorld[i]=Atlas.World(Atlas.Data.settlements[i].lon,Atlas.Data.settlements[i].lat,5);
                DrawSelectedRegion();
                miniatureLon=999;sceneryLon=999;
                Resources.UnloadUnusedAssets();
            }
            if(!Atlas.RemoteDetail.Contains(focusLon,focusLat)){if(remotePatch!=null)remotePatch.SetActive(false);return;}
            if(!changed && Mathf.Abs(Mathf.DeltaAngle(patchLon,focusLon))<2 && Mathf.Abs(patchLat-focusLat)<2)
            {if(remotePatch!=null)remotePatch.SetActive(true);return;}
            if(remotePatch!=null)
            {
                var old=remotePatch.GetComponent<MeshFilter>().sharedMesh;meshes.Remove(old);Destroy(old);Destroy(remotePatch);
            }
            var d=Atlas.RemoteDetail.Data;float span=Mathf.Clamp(distance/100,5,35);
            const int cells=128;var vertices=new Vector3[(cells+1)*(cells+1)];var uv=new Vector2[vertices.Length];var tri=new int[cells*cells*6];int t=0;
            float w=Mathf.Max(d.west,focusLon-span),e=Mathf.Min(d.east,focusLon+span),s=Mathf.Max(d.south,focusLat-span),n=Mathf.Min(d.north,focusLat+span);
            for(int y=0;y<=cells;y++)for(int x=0;x<=cells;x++)
            {
                float lon=Mathf.Lerp(w,e,x/(float)cells),lat=Mathf.Lerp(s,n,y/(float)cells);int i=y*(cells+1)+x;
                vertices[i]=Atlas.World(lon,lat,1);uv[i]=new Vector2((lon+180)/360,(lat+90)/180);
                if(x<cells&&y<cells){tri[t++]=i;tri[t++]=i+cells+1;tri[t++]=i+1;tri[t++]=i+1;tri[t++]=i+cells+1;tri[t++]=i+cells+2;}
            }
            var mesh=new Mesh {name=name+" local terrain",vertices=vertices,uv=uv,triangles=tri};mesh.RecalculateNormals();mesh.RecalculateBounds();
            remotePatch=MeshObject(name+" local terrain",mesh,reliefMaterial);patchLon=focusLon;patchLat=focusLat;
        }

        void Select(int index)
        {
            if(index<0||index>=Atlas.Data.settlements.Length)return;
            selected=index;detailScroll=Vector2.zero;selectedArmyId="";selectedRegimentId="";campaignPopup="";choosingCampaignDestination=false;
            selectedRegion=catalog.ForCity(Atlas.Data.settlements[index].id);
            DrawSelectedRegion();
            if(origin>=0 && origin!=index) PlanRoute(origin,index);
            else ClearHighlight();
        }
        void ClearHighlight()
        {
            if(highlights!=null)
            {
                var mesh=highlights.GetComponent<MeshFilter>().sharedMesh;
                meshes.Remove(mesh);Destroy(mesh);Destroy(highlights);
            }
        }
        void HighlightConnections(string id)
        {
            ClearHighlight();
            var paths=Atlas.Connected(id);
            paths.RemoveAll(r=>r.mode!="land");
            highlights=BuildPaths("Selected connections",paths.ToArray(),new Color(.94f,.73f,.34f),5,9);
        }
        void PlanRoute(int start,int target)
        {
            var a=Atlas.Data.settlements[start];var b=Atlas.Data.settlements[target];
            var journey=LandRoutePlanner.Find(Atlas,a.id,b.id);
            ClearHighlight();
            if(journey==null)
            {
                plannerMessage="No connected land corridor. Overseas journeys require naval transport.";return;
            }
            highlights=BuildPaths("Planned land journey",journey.Routes.ToArray(),new Color(1,.78f,.33f),6,11);
            plannerMessage=a.name+" → "+b.name+"\n"+journey.DistanceKm.ToString("N0")+" km • "+journey.Routes.Count+" corridors\nChooses lower terrain cost; provisional network.";
        }

        void EnsureStyles()
        {
            if(text!=null)return;
            panel=Solid(EmpireTheme.Surface);header=Solid(EmpireTheme.Ink);
            gold=Solid(EmpireTheme.Brass);white=Solid(Color.white);
            text=PaperUI.Text();text.wordWrap=true;
            small=PaperUI.Text(12,true);small.wordWrap=true;
            heading=PaperUI.Title();
            townText=EmpireTheme.Text(13,EmpireTheme.Ivory,FontStyle.Bold);
            mountainText=new GUIStyle(small){fontSize=12,fontStyle=FontStyle.Italic,alignment=TextAnchor.MiddleCenter};
            button=PaperUI.Button(13,TextAnchor.MiddleLeft);

        }
        Texture2D Solid(Color c){var t=new Texture2D(1,1);t.SetPixel(0,0,EmpireTheme.RenderColor(c));t.Apply();return t;}
        void OnGUI()
        {
            if(Atlas==null)return;
            if(qaRunning && (Event.current.isMouse || Event.current.isKey || Event.current.type==EventType.ScrollWheel))Event.current.Use();
            using var theme = new EmpireTheme.Scope(true);
            using var paperTheme=new PaperUI.Scope(true);
            EnsureStyles();GUI.matrix=Matrix4x4.Scale(new Vector3(uiScale,uiScale,1));
            float w=Screen.width/uiScale,h=Screen.height/uiScale;
            campaignTips.Begin();
            bool mapEnabled=GUI.enabled;GUI.enabled=mapEnabled&&campaignPopup=="";
            EnsureCampaignStyles();
            DrawMapLabels(w,h);DrawCountryArmies(w,h);GUI.enabled=mapEnabled;
            DrawCampaignInterface(w,h);
            DrawCampaignPointerHint(w,h);
            if(showFactionKey)DrawFactionKey(w,h);
            GUI.matrix=Matrix4x4.identity;
        }

        void DrawMapLabels(float w,float h)
        {
            if(Event.current.type==EventType.Repaint){townRects.Clear();cityTaskRects.Clear();visibleTowns.Clear();}
            var used=new List<Rect>();
            used.AddRange(CampaignArmyLabelRects(w,h).Values);
            // Capitals and the selected settlement receive priority when labels overlap.
            for(int pass=0;pass<3;pass++)foreach(int i in labelCandidates)
            {
                var town=Atlas.Data.settlements[i];int priority=i==selected?0:town.kind=="capital"?1:2;
                if(priority!=pass)continue;
                Vector3 p=cam.WorldToScreenPoint(townWorld[i]);
                if(p.z<=0)continue;
                Vector2 at=new Vector2(p.x/uiScale,(Screen.height-p.y)/uiScale);
                if(at.x<12 || at.x>w-12 || at.y<82 || at.y>h-104)continue;
                var cityRegion=catalog.ForCity(town.id);
                var cityState=cityRegion?.capital.id==town.id?regionCampaign?.State(cityRegion.id):null;
                string cityLabel=town.name+(town.port?"  [P]":""),tasks=CampaignCityTasks(cityState);
                string label=cityLabel+tasks;
                townText.fontSize=distance<1600?16:14;
                float width=townText.CalcSize(new GUIContent(label)).x+25;
                Rect rect=new Rect(at.x-6,at.y-10,width,25);
                if(!CampaignLabelFits(rect,w,h))continue;
                bool overlap=false;foreach(var r in used)if(r.Overlaps(rect)){overlap=true;break;}
                if(overlap&&i!=selected&&distance<1600)
                {
                    foreach(var offset in new[]{new Vector2(0,-29),new Vector2(-width,0),new Vector2(0,29)})
                    {
                        var candidate=new Rect(rect.position+offset,rect.size);
                        if(CampaignLabelFits(candidate,w,h)&&!used.Any(r=>r.Overlaps(candidate)))
                        {rect=candidate;overlap=false;break;}
                    }
                }
                if(overlap && i!=selected)continue;
                used.Add(rect);
                Color c=i==selected?EmpireTheme.Brass:EmpireTheme.Ivory;
                GUI.color=new Color(.025f,.04f,.04f,distance<1600?.90f:.72f);GUI.DrawTexture(rect,white);GUI.color=Color.white;
                GUI.color=c;GUI.DrawTexture(new Rect(at.x-3,at.y-3,6,6),white);GUI.color=Color.white;
                townText.normal.textColor=c;
                GUI.Label(new Rect(rect.x+14,rect.y,width-12,25),label,townText);
                if(showCityTasks)campaignTips.Add(rect,CampaignCityTaskDetails(cityState));
                if(Event.current.type==EventType.Repaint)
                {
                    townRects[i]=rect;visibleTowns.Add(i);
                    if(tasks!="")
                    {float start=rect.x+14+townText.CalcSize(new GUIContent(cityLabel)).x;cityTaskRects[i]=new Rect(start,rect.y,rect.xMax-start,rect.height);}
                }
            }
            DrawLandmarkLabels(w,h,used);
            if(showMountains && distance<3300)foreach(var mountain in Atlas.Data.mountains)
            {
                Vector3 world=Atlas.World(mountain.lon,mountain.lat,14);
                if(Vector3.Dot(world.normalized,cam.transform.position-world)<0)continue;
                var p=cam.WorldToScreenPoint(world);
                var at=new Vector2(p.x/uiScale,(Screen.height-p.y)/uiScale);
                if(p.z>0 && CampaignLabelFits(new Rect(at.x-130,at.y,260,25),w,h))
                    GUI.Label(new Rect(at.x-130,at.y,260,25),mountain.name,mountainText);
            }
            if(distance<1200)foreach(var site in catalog.regions.SelectMany(r=>r.sites))
            {
                var world=Atlas.World(site.lon,site.lat,5);
                if(Vector3.Dot(world.normalized,cam.transform.position-world)<=0)continue;
                var p=cam.WorldToScreenPoint(world);
                var at=new Vector2(p.x/uiScale,(Screen.height-p.y)/uiScale);
                if(p.z<=0 || at.x<15 || at.x>w-15 || at.y<82 || at.y>h-104)continue;
                GUI.color=new Color(.83f,.72f,.48f);GUI.DrawTexture(new Rect(at.x-2,at.y-2,4,4),white);GUI.color=Color.white;
                var label=site.name+" / "+site.kind;
                var rect=new Rect(at.x+8,at.y-9,townText.CalcSize(new GUIContent(label)).x+12,25);
                if(!CampaignLabelFits(rect,w,h))continue;
                bool overlap=false;foreach(var r in used)if(r.Overlaps(rect)){overlap=true;break;}
                if(overlap)continue;
                used.Add(rect);GUI.color=new Color(.025f,.04f,.04f,.88f);GUI.DrawTexture(rect,white);GUI.color=Color.white;
                townText.normal.textColor=EmpireTheme.Ivory;GUI.Label(new Rect(rect.x+5,rect.y,rect.width-5,rect.height),label,townText);
            }
        }

        void DrawSidebar(float w,float h)
        {
            float x=w-304;
            EmpireTheme.Panel(new Rect(x,84,304,h-146),EmpireTheme.Surface);
            GUILayout.BeginArea(new Rect(x+18,100,268,h-180));
            detailScroll=GUILayout.BeginScrollView(detailScroll);
            DrawCountryControls();
            if(countryCampaign!=null&&GUILayout.Button(atlasLayersOpen?"Hide map settings":"Map settings",button))atlasLayersOpen=!atlasLayersOpen;
            if(countryCampaign==null||atlasLayersOpen){
            GUILayout.Label("WORLD ATLAS",heading);
            GUILayout.Label(catalog.provinces.Length+" provinces / "+catalog.regions.Length+" regional cities",small);
            GUILayout.Space(8);
            bool political=EmpireTheme.Toggle(showFactionColors,"Faction territory colors");
            if(political!=showFactionColors){showFactionColors=political;reliefMaterial.SetFloat("_PoliticalOpacity",political?.35f:0);}
            if(GUILayout.Button(showFactionKey?"Hide faction key":"Faction key / 1800 setup",button))showFactionKey=!showFactionKey;
            GUILayout.Space(6);
            bool a=EmpireTheme.Toggle(showRoads,"Roads"),b=EmpireTheme.Toggle(showRivers,"Rivers"),c=EmpireTheme.Toggle(showSea,"Maritime connections");
            if(a!=showRoads){showRoads=a;roads.SetActive(a);}
            if(b!=showRivers){showRivers=b;riverLayer.SetActive(b);}
            if(c!=showSea){showSea=c;seaRoutes.SetActive(c);}
            showTrails=EmpireTheme.Toggle(showTrails,"Trails");
            showRiverways=EmpireTheme.Toggle(showRiverways,"Navigable rivers");
            showMountains=EmpireTheme.Toggle(showMountains,"Mountain names");
            DrawAtmosphereControls();
            DrawLandmarkControls();}
            GUILayout.Space(12);
            if(selected>=0)
            {
                var town=Atlas.Data.settlements[selected];
                GUILayout.Label(town.name,heading);
                var province=catalog.Province(selectedRegion.provinceId);
                GUILayout.Label(province.name+" / "+province.regionIds.Length+" regions",text);
                GUILayout.Label("Main city controls this region and its sites.",small);
                GUILayout.Label(Mathf.Abs(town.lat).ToString("F2")+"° "+(town.lat>=0?"N":"S")+"  /  "+Mathf.Abs(town.lon).ToString("F2")+"° "+(town.lon>=0?"E":"W")+"  /  "+Atlas.HeightMeters(town.lon,town.lat).ToString("N0")+" m",small);
                var riverAccess=riverways.Nearby(town.lon,town.lat);
                if(riverAccess!=null)GUILayout.Label("River access: "+riverAccess.name+" / shallow-draft ships",small);
                GUILayout.Space(8);
                if(GUILayout.Button("Focus settlement",button))Focus(town.lon,town.lat,420);
                GUILayout.Label("SUPPORTING SITES",small);
                foreach(var site in selectedRegion.sites)GUILayout.Label(site.name+" / "+site.kind,text);
                var state=regionCampaign.State(selectedRegion.id);
                var faction=catalog.Faction(state.owner);
                var ownerRect=GUILayoutUtility.GetRect(1,24,GUILayout.ExpandWidth(true));
                GUI.color=faction.MapColor;GUI.DrawTexture(new Rect(ownerRect.x,ownerRect.y+5,12,12),white);GUI.color=Color.white;
                GUI.Label(new Rect(ownerRect.x+20,ownerRect.y,ownerRect.width-20,24),faction.name,small);
                if(countryCampaign!=null&&!countryCampaign.Active(state.regionId))GUILayout.Label("Outside the European scenario; inspection only.",small);
                else if(countryCampaign==null||state.owner==countryCampaign.Save.playerFaction)DrawEconomyBalance(state);
                else GUILayout.Label("Foreign economic and construction details are concealed.",small);
                if(CanManageRegion(state))
                {
                GUILayout.Space(8);GUILayout.Label("SHARED REGION BUILD SLOTS",small);
                foreach(var slot in state.buildings)GUILayout.Label(slot.kind+(slot.turnsRemaining>0?" / "+slot.turnsRemaining+" turns":""),text);
                GUILayout.BeginHorizontal();BuildButton("Farm");BuildButton("Fortress");GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();BuildButton("Workshop");BuildButton("University");GUILayout.EndHorizontal();
                GUILayout.Label(constructionMessage,small);
                GUILayout.Label("Turn "+regionCampaign.Save.turn+" • "+faction.name+" treasury "+regionCampaign.FactionState(state.owner).treasury,small);
                if(countryCampaign!=null)
                {
                    GUILayout.BeginHorizontal();BuildButton("Barracks");BuildButton("Granary");GUILayout.EndHorizontal();GUILayout.BeginHorizontal();BuildButton("Market");BuildButton("Arsenal");GUILayout.EndHorizontal();
                    if(GUILayout.Button("Recruit regiment / "+countryCampaign.Balance.recruitCost,button))countryCampaign.Recruit(countryCampaign.Save.playerFaction,state.regionId,out constructionMessage);
                    GUILayout.BeginHorizontal();
                    if(GUILayout.Button("Militia / 120",button))countryCampaign.Recruit(countryCampaign.Save.playerFaction,state.regionId,"Militia",out constructionMessage);
                    if(GUILayout.Button("Riflemen / 270",button))countryCampaign.Recruit(countryCampaign.Save.playerFaction,state.regionId,"Rifle",out constructionMessage);
                    GUILayout.EndHorizontal();
                    for(int slot=0;slot<state.buildings.Length;slot++)if(state.buildings[slot].kind!="Empty"&&state.buildings[slot].turnsRemaining==0&&state.buildings[slot].level<CampaignBuildingTiers.MaxLevel(state.buildings[slot].kind))
                        if(GUILayout.Button("Upgrade "+state.buildings[slot].kind+" / "+countryCampaign.UpgradeCost(state.buildings[slot].kind,state.buildings[slot].level+1),button))countryCampaign.Upgrade(countryCampaign.Save.playerFaction,state.regionId,slot,out constructionMessage);
                }
                if(countryCampaign!=null)for(int slot=0;slot<state.buildings.Length;slot++)if(state.buildings[slot].turnsRemaining>0&&GUILayout.Button("Cancel "+state.buildings[slot].kind,button))countryCampaign.CancelConstruction(countryCampaign.Save.playerFaction,state.regionId,slot,out constructionMessage);
                if(countryCampaign==null){
                GUILayout.BeginHorizontal();
                if(countryCampaign==null&&GUILayout.Button("Next turn",button))regionCampaign.EndTurn();
                if(GUILayout.Button("Save",button))
                {
                    File.WriteAllText(Path.Combine(Application.persistentDataPath,"region-atlas.json"),JsonUtility.ToJson(regionCampaign.Save,true));
                    constructionMessage="Regional construction saved.";
                }
                if(GUILayout.Button("Load",button))
                {
                    string savePath=Path.Combine(Application.persistentDataPath,"region-atlas.json");
                    constructionMessage=File.Exists(savePath)&&regionCampaign.Restore(File.ReadAllText(savePath))?"Regional construction loaded.":"No compatible save found.";
                }
                GUILayout.EndHorizontal();}
                }
                GUILayout.Space(8);
            }
            GUILayout.Label("FIND A REGION CITY",small);
            GUI.SetNextControlName("townSearch");search=GUILayout.TextField(search);
            if(search!=lastSearch)
            {
                searchResults.Clear();lastSearch=search;listScroll=Vector2.zero;
                if(search.Length>0)for(int i=0;i<Atlas.Data.settlements.Length && searchResults.Count<150;i++)
                    if(Atlas.Data.settlements[i].name.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0)searchResults.Add(i);
            }
            if(searchResults.Count==150)GUILayout.Label("Showing 150 matches; refine the name.",small);
            listScroll=GUILayout.BeginScrollView(listScroll,GUILayout.Height(selected>=0?140:Mathf.Max(180,h-410)));
            var listing=search.Length>0?searchResults:labelCandidates;
            int shown=0;
            foreach(int i in listing)
            {
                if(shown++>=100)break;
                var town=Atlas.Data.settlements[i];
                if(GUILayout.Button(town.name+(town.port?"  / port":""),button)){Select(i);Focus(town.lon,town.lat,650);GUI.FocusControl(null);}
            }
            GUILayout.EndScrollView();GUILayout.EndScrollView();GUILayout.EndArea();
        }
        void BuildButton(string kind)
        {
            if(GUILayout.Button(kind+" "+RegionCampaign.Cost(kind),button))
            {
                if(countryCampaign!=null)countryCampaign.Build(countryCampaign.Save.playerFaction,selectedRegion.id,kind,out constructionMessage);
                else if(atlasSandbox)regionCampaign.Build(selectedRegion.id,kind,out constructionMessage);
            }
        }

        void DrawScale(float w,float h)
        {
            float km=distance<500?50:distance<1600?100:distance<8000?300:1000;
            Vector3 a=cam.WorldToScreenPoint(focus),b=cam.WorldToScreenPoint(focus+cam.transform.right*km);
            float pixels=Vector3.Distance(a,b)/uiScale;
            GUI.DrawTexture(new Rect(w-280,h-30,pixels,2),gold);
            GUI.Label(new Rect(w-280,h-52,240,22),"~"+km+" km at focus  •  north up at reset",small);
        }
        void DrawFactionKey(float w,float h)
        {
            float height=Mathf.Min(480,h-200);
            EmpireTheme.Panel(new Rect(20,108,320,height),EmpireTheme.Surface);
            GUILayout.BeginArea(new Rect(36,122,288,height-28));
            GUILayout.Label("FACTIONS / 1800",heading);
            GUILayout.Label("Tint = owner • brass outline = selection",small);
            GUILayout.Label("Opening scenario; borders are approximate.",small);
            if(GUILayout.Button("Reset 1800 scenario",button))
            {
                regionCampaign=new RegionCampaign(catalog);RefreshOwnershipColors();
                constructionMessage="Restored the 1800 opening scenario.";
            }
            GUILayout.Label("Reset restores territories, builds and budget.",small);
            factionScroll=GUILayout.BeginScrollView(factionScroll);
            foreach(var faction in catalog.factions)
            {
                int count=0;foreach(var state in regionCampaign.Save.regions)if(state.owner==faction.id)count++;
                if(count==0)continue;
                var row=GUILayoutUtility.GetRect(260,27);
                GUI.color=faction.MapColor;GUI.DrawTexture(new Rect(row.x,row.y+5,13,13),white);GUI.color=Color.white;
                GUI.Label(new Rect(row.x+22,row.y,235,27),faction.name+"  ("+count+")",small);
            }
            GUILayout.EndScrollView();GUILayout.EndArea();
        }
        void DrawCredits(float w,float h)
        {
            var rect=new Rect((w-680)/2,130,680,Mathf.Min(600,h-240));EmpireTheme.Panel(rect,EmpireTheme.Surface);
            GUILayout.BeginArea(new Rect(rect.x+25,rect.y+22,630,rect.height-44));
            GUILayout.Label("GEOGRAPHY & SOURCES",heading);GUILayout.Space(16);
            GUILayout.Label("Whole Earth measured heights and seabed: NOAA ETOPO 2022, accessed through NOAA ERDDAP. Regional detail across six continental atlases: Mapzen / Tilezen Terrain Tiles on AWS. Elevation is preserved in metres; the scene exaggerates relief for campaign readability.",text);
            GUILayout.Space(12);
            GUILayout.Label("Made with Natural Earth: 1:10 million coastlines, land, lakes, rivers and settlement coordinates. Public domain. Secondary British settlement coordinates are authored supplements.",text);
            GUILayout.Label("World reference-place catalogue: GeoNames, CC BY 4.0 (geonames.org). Coordinates and names are modern geographic references; campaign suitability in 1800 is unreviewed. Natural Earth II supplies idealized land-cover colors, not surveyed historical vegetation.",text);
            GUILayout.Space(12);
            GUILayout.Label("Europe terrain data produced using Copernicus data and information funded by the European Union — EU-DEM layers. United Kingdom terrain data © Environment Agency copyright and/or database right 2015. All rights reserved. Norway terrain data © Kartverket. Austria terrain data © offene Daten Österreichs. Global ETOPO1 terrain data: NOAA. United States 3DEP, GMTED2010 and SRTM: U.S. Geological Survey. Canada: Open Government Licence – Canada. Australia: © Commonwealth of Australia (Geoscience Australia) 2017. Mexico: INEGI, Continental relief, 2016. New Zealand: Copyright 2011 Crown copyright, Land Information New Zealand and the New Zealand Government (All rights reserved). ArcticDEM: DigitalGlobe imagery, funded by NSF awards 1043681, 1559691 and 1542736.",small);
            GUILayout.Space(12);
            GUILayout.Label("Land corridors use terrain-aware routing. They are provisional gameplay connections, not a surveyed 1800 road network. Maritime lines are schematic. Town roles and decorative miniatures are authored. Modern reservoirs/shoreline alterations and settlement names still need a period review. Opening faction controllers are authored for an approximate 1800 scenario. Region shapes are gameplay geography rather than reconstructed political borders.",text);
            GUILayout.Space(14);
            if(GUILayout.Button("Close",button))credits=false;
            GUILayout.EndArea();
        }
        IEnumerator Capture(string path)
        {
            yield return new WaitForSecondsRealtime(4);
            yield return new WaitForEndOfFrame();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("EUROPE_MAP_SCREENSHOT "+path);
        }
        IEnumerator QA(string directory)
        {
            qaRunning=true;
            Directory.CreateDirectory(directory);
            yield return new WaitForSecondsRealtime(3);
            var shots=new[]{new Vector3(20,25,22000),new Vector3(4,48,2000),new Vector3(14,37.6f,550),new Vector3(11,33,3000),
                new Vector3(-110,35,6000),new Vector3(-46.63f,-23.55f,650),new Vector3(105,28,5000),new Vector3(179,-20,9000),new Vector3(7.8f,46.5f,550),new Vector3(-.12f,51.5f,160),new Vector3(13.36f,38.12f,180),new Vector3(10,28,700),new Vector3(14,37.6f,550),new Vector3(-80,46,2500),new Vector3(100,20,1800),new Vector3(80,24,2200),new Vector3(111,30,2400),new Vector3(-150,60,3200),new Vector3(12,13,2800),new Vector3(13.36f,38.12f,600),new Vector3(13.36f,38.12f,600),new Vector3(129,62,700)};
            var names=new[]{"globe","europe-factions","sicily","north-africa","americas","sao-paulo","asia","date-line","alpine-snow","london-walls","palermo-port","sahara-biomes","sicily-natural","north-american-provinces","southeast-asia","india-roads","china-cities","alaska","african-interior","rain-coast","clouds-coast","snow-siberia"};
            var report=new System.Text.StringBuilder();
            for(int i=0;i<shots.Length;i++)
            {
                mapWeather=names[i]=="rain-coast"?MapWeather.Rain:names[i]=="snow-siberia"?MapWeather.Snow:names[i]=="clouds-coast"?MapWeather.Cloudy:MapWeather.Clear;
                showFactionColors=names[i]!="sicily-natural";reliefMaterial.SetFloat("_PoliticalOpacity",showFactionColors?.35f:0);
                Focus(shots[i].x,shots[i].y,shots[i].z);
                if(names[i]=="europe-factions")Select(Array.FindIndex(Atlas.Data.settlements,town=>town.id=="paris"));
                else if(names[i]=="sicily")Select(Array.FindIndex(Atlas.Data.settlements,town=>town.id=="palermo"));
                else if(names[i]=="sao-paulo")Select(Array.FindIndex(Atlas.Data.settlements,town=>town.name=="São Paulo"));
                else {selected=-1;selectedRegion=null;DrawSelectedRegion();}
                yield return new WaitForSecondsRealtime(1);
                var frameTimes=new List<float>();float until=Time.realtimeSinceStartup+2;
                while(Time.realtimeSinceStartup<until){frameTimes.Add(Time.unscaledDeltaTime*1000);yield return null;}
                frameTimes.Sort();float total=0;foreach(float ms in frameTimes)total+=ms;
                string line=names[i]+": mean "+(total/frameTimes.Count).ToString("F2")+" ms; p95 "+frameTimes[(int)(frameTimes.Count*.95f)].ToString("F2")+" ms; Unity allocated "+(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()/1048576)+" MiB; visible candidates "+labelCandidates.Count;
                report.AppendLine(line);Debug.Log("MAP_QA "+line);
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,names[i]+".png"));
                // Capture is deferred; keep this view stable until its image has been rendered.
                yield return new WaitForSecondsRealtime(.2f);
            }
            showFactionColors=true;reliefMaterial.SetFloat("_PoliticalOpacity",.35f);
            Focus(-1,52,2350);
            File.WriteAllText(Path.Combine(directory,"performance.txt"),report.ToString());Debug.Log("MAP_QA_COMPLETE");qaRunning=false;
        }
        void OnDestroy()
        {
            RestoreCampaignCursor();
            foreach(var cursor in campaignCursors.Values)if(cursor!=null)Destroy(cursor);
            DestroyScenery();
            foreach(var go in owned)if(go!=null)Destroy(go);
            foreach(var mesh in meshes)if(mesh!=null)Destroy(mesh);
            foreach(var material in materials)if(material!=null)Destroy(material);
            foreach(var texture in new[]{panel,header,gold,white,regionIds,regionOwners,worldDryLand,detailDryLand,campaignSealDisk,campaignOverviewTexture})if(texture!=null)Destroy(texture);
        }

        sealed class GeometryBuilder
        {
            readonly List<Vector3> vertices=new List<Vector3>();readonly List<int> triangles=new List<int>();readonly List<Color> colors=new List<Color>();
            Vector3 framePosition;
            Quaternion frameRotation=Quaternion.identity;
            float frameScale=1;
            public void Frame(Vector3 position,Quaternion rotation,float scale=1)
            {
                framePosition=position;frameRotation=rotation;frameScale=scale;
            }
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color)
            {
                int i=vertices.Count;
                vertices.Add(framePosition+frameRotation*(a*frameScale));vertices.Add(framePosition+frameRotation*(b*frameScale));
                vertices.Add(framePosition+frameRotation*(c*frameScale));vertices.Add(framePosition+frameRotation*(d*frameScale));
                for(int j=0;j<4;j++)colors.Add(color);
                triangles.Add(i);triangles.Add(i+1);triangles.Add(i+2);triangles.Add(i);triangles.Add(i+2);triangles.Add(i+3);
            }
            public void Box(Vector3 at,Vector3 size,Color c)
            {
                Vector3 a=at+new Vector3(-size.x/2,0,-size.z/2),b=at+new Vector3(size.x/2,0,-size.z/2),
                    d=at+new Vector3(-size.x/2,0,size.z/2),e=at+new Vector3(size.x/2,0,size.z/2),u=Vector3.up*size.y;
                Quad(a,b,b+u,a+u,c*.8f);Quad(b,e,e+u,b+u,c*.7f);Quad(e,d,d+u,e+u,c);Quad(d,a,a+u,d+u,c*.9f);Quad(a+u,b+u,e+u,d+u,c);
            }
            public void Roof(Vector3 at,Vector3 size,Color c)
            {
                Vector3 a=at+new Vector3(-size.x/2,0,-size.z/2),b=at+new Vector3(size.x/2,0,-size.z/2),
                    d=at+new Vector3(-size.x/2,0,size.z/2),e=at+new Vector3(size.x/2,0,size.z/2),
                    p=at+new Vector3(0,size.y,-size.z/2),q=at+new Vector3(0,size.y,size.z/2);
                Quad(a,d,q,p,c);Quad(b,p,q,e,c*.8f);Quad(a,p,b,b,c*.9f);Quad(d,e,q,q,c*.9f);
            }
            public void Cone(Vector3 at,float radius,float height,Color color)
            {
                for(int k=0;k<7;k++)
                {
                    float a=k*Mathf.PI*2/7,c=(k+1)*Mathf.PI*2/7;
                    var p=at+new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius);
                    var q=at+new Vector3(Mathf.Cos(c)*radius,0,Mathf.Sin(c)*radius);
                    Quad(p,q,at+Vector3.up*height,at+Vector3.up*height,color*(.72f+.28f*(k/6f)));
                }
            }
            public void Beam(Vector3 from,Vector3 to,float width,float height,Color color)
            {
                Vector3 side=Vector3.Cross(Vector3.up,to-from).normalized*width*.5f,u=Vector3.up*height;
                Quad(from-side,to-side,to-side+u,from-side+u,color*.8f);
                Quad(to+side,from+side,from+side+u,to+side+u,color);
                Quad(from-side+u,to-side+u,to+side+u,from+side+u,color);
                Quad(from+side,from-side,from-side+u,from+side+u,color*.7f);
                Quad(to-side,to+side,to+side+u,to-side+u,color*.7f);
            }
            public Mesh Mesh(string name)
            {
                var mesh=new Mesh {name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();return mesh;
            }
        }
    }
}
