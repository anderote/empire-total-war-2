using System;
using System.IO;
using EmpireCampaign.Geography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CampaignBuild
{
    [MenuItem("Empire Campaign/Verify geography")]
    public static void Verify()
    {
        var atlas=new GeographyAtlas();var d=atlas.Data;
        Require(d.schemaVersion==1,"supported geography version");
        Require(d.west==-180 && d.east==180 && d.south==-90 && d.north==90,"complete world extent including poles");
        Require(d.settlements.Length<250,"deliberately sparse world campaign roster");
        Require(Vector3.Distance(GeographyAtlas.Normal(-180,20),GeographyAtlas.Normal(180,20))<.00001f,"seam wraps at date line");
        Require(Vector3.Distance(GeographyAtlas.Normal(10,90),Vector3.up)<.00001f,"north pole projection");
        Vector3 east=(GeographyAtlas.Normal(.1f,0)-GeographyAtlas.Normal(0,0)).normalized;
        Require(Vector3.Dot(Vector3.Cross(GeographyAtlas.North(0,0),-GeographyAtlas.Normal(0,0)),east)>.99f,"east appears on screen right with north up");
        Require(Mathf.Abs(atlas.World(-140,0).magnitude-GeographyAtlas.EarthRadiusKm)<1,"ocean surface uses spherical radius");
        Require(d.settlements.Length>=100,"substantial settlement coverage");
        Require(d.routes.Length>=100,"substantial connection coverage");
        foreach(var town in d.settlements)
        {
            var p=atlas.Unproject(atlas.Project(town.lon,town.lat));
            Require(Mathf.Abs(p.x-town.lon)<.0001f && Mathf.Abs(p.y-town.lat)<.0001f,"coordinate roundtrip: "+town.name);
        }
        Require(atlas.IsDry(-.12f,51.5f),"London is land");
        Require(!atlas.IsDry(-6f,47f),"Atlantic is water");
        Require(atlas.HeightMeters(7.8f,46.5f)>1500,"measured Alps elevation");
        Require(atlas.HeightMeters(.1f,42.7f)>1000,"measured Pyrenees elevation");
        int landCount=0;
        var routeAtlases=new System.Collections.Generic.Dictionary<string,GeographyAtlas>();
        foreach(var route in d.routes)
        {
            Require(atlas.Towns.ContainsKey(route.fromId)&&atlas.Towns.ContainsKey(route.toId),"valid route endpoints");
            Require(route.points.Length>=2,"route has geometry");
            if(route.mode!="land")continue;
            landCount++;
            Require(route.distanceKm>0 && route.terrainCost>0,"positive travel costs");
            var routeAtlas=atlas;
            if(!string.IsNullOrEmpty(route.terrainAtlas))
            {
                if(!routeAtlases.TryGetValue(route.terrainAtlas,out routeAtlas))
                {
                    string manifest=route.terrainAtlas=="EuropeGeography"?"europe":route.terrainAtlas=="WorldGeography"?"world":"atlas";
                    routeAtlas=new GeographyAtlas(route.terrainAtlas+"/"+manifest,false);routeAtlases.Add(route.terrainAtlas,routeAtlas);
                }
            }
            foreach(var p in route.points)Require(routeAtlas.IsDry(p.lon,p.lat),"land route stays dry: "+route.name);
            Require(!(route.fromId=="dover"&&route.toId=="calais"),"Channel requires ships");
        }
        Require(landCount>100,"land route coverage");
        var britain=LandRoutePlanner.Find(atlas,"london","inverness");
        Require(britain!=null && britain.DistanceKm>800,"Britain has a connected north/south journey");
        Require(LandRoutePlanner.Find(atlas,"london","paris")==null,"planner never treats Channel as land");
        var alps=LandRoutePlanner.Find(atlas,"paris","vienna");
        Require(alps!=null && alps.Routes.Count>3,"connected continental journey");
        Require(LandRoutePlanner.Find(atlas,"palermo","siracusa")!=null,"Sicily has multiple connected towns");
        Require(LandRoutePlanner.Find(atlas,"tunis","cairo")!=null,"North Africa has a connected authored network");
        Require(LandRoutePlanner.Find(atlas,"palermo","tunis")==null,"Sicily to Africa requires naval transport");
        var catalog=RegionCatalog.Load();var campaign=new RegionCampaign(catalog);
        var ids=Resources.Load<TextAsset>("WorldGeography/region-ids").bytes;
        Require(ids.Length==catalog.politicalRasterWidth*catalog.politicalRasterHeight,"complete region ownership raster");
        var factionIds=new System.Collections.Generic.HashSet<string>();
        foreach(var f in catalog.factions)
        {
            Color parsed;
            Require(factionIds.Add(f.id)&&ColorUtility.TryParseHtmlString(f.color,out parsed),"unique faction with valid map color: "+f.id);
        }
        var rasterRegions=new System.Collections.Generic.HashSet<byte>(ids);
        for(int i=0;i<catalog.regions.Length;i++)
        {
            Require(catalog.Faction(catalog.regions[i].owner)!=null,"valid starting faction: "+catalog.regions[i].name);
            Require(rasterRegions.Contains((byte)(i+1)),"region represented in political raster: "+catalog.regions[i].name);
        }
        foreach(var pair in new[]{"London|britain","Paris|france","Palermo|naples","Venice|austria","Milan|cisalpine","Rome|papal","Rio de Janeiro|portugal","New Orleans|spain"})
        {
            var parts=pair.Split('|');var start=Array.Find(catalog.regions,r=>r.capital.name==parts[0]);
            Require(start!=null&&start.owner==parts[1],"period starting controller: "+parts[0]);
        }
        Require(!campaign.CaptureCity("palermo","unknown-faction"),"unknown factions cannot acquire territory");
        Require(catalog.Province("sicily").regionIds.Length==4,"Sicily has exactly four regions");
        int italianProvinces=0,italianCities=0,sicilySites=0,saoPaulo=0;
        foreach(var p in catalog.provinces)if(p.id.EndsWith("italy")||p.id=="sicily"){italianProvinces++;italianCities+=p.regionIds.Length;}
        foreach(var r in catalog.regions)
        {
            if(r.provinceId=="sicily")sicilySites+=r.sites.Length;
            if(r.capital.name=="São Paulo"||r.capital.name=="Sao Paulo")saoPaulo++;
        }
        Require(italianProvinces==4 && italianCities==16,"Italy has four provinces and sixteen main cities");
        Require(sicilySites==12,"Sicily has three support sites per region");
        Require(saoPaulo==1,"Sao Paulo is one regional city, not a suburb cluster");
        var region=catalog.ForCity("palermo");string reason;
        Require(campaign.Build(region.id,"University",out reason),"shared region construction starts");
        Require(campaign.State(region.id).buildings[0].turnsRemaining==3,"construction queue persists");
        string saved=JsonUtility.ToJson(campaign.Save);campaign.EndTurn();
        Require(campaign.State(region.id).buildings[0].turnsRemaining==2,"turn advances construction");
        Require(campaign.Restore(saved)&&campaign.Save.turn==1,"regional save restores construction/turn");
        Require(!campaign.CaptureCity(region.sites[0].id,"france"),"satellites cannot capture regional ownership");
        Require(campaign.CaptureCity("palermo","france")&&campaign.State(region.id).owner=="france","main city conquest transfers whole region");
        string conquestSave=JsonUtility.ToJson(campaign.Save);
        Require(new RegionCampaign(catalog).Restore(conquestSave),"valid conquered faction ownership can be loaded");
        var legacy=JsonUtility.FromJson<RegionSave>(conquestSave);legacy.schemaVersion=1;legacy.treasury=1234;
        legacy.factions=null;foreach(var s in legacy.regions)s.economy=null;
        var migrated=new RegionCampaign(catalog);
        Require(migrated.Restore(JsonUtility.ToJson(legacy))&&migrated.Save.schemaVersion==2,"schema-1 economy migration");
        Require(migrated.State(region.id).owner=="france"&&migrated.State(region.id).buildings[0].kind=="University"&&migrated.FactionState("france").treasury==1234,"migration preserves ownership/buildings and old purchasing power");
        var invalid=JsonUtility.FromJson<RegionSave>(conquestSave);invalid.factions=null;
        Require(!campaign.Restore(JsonUtility.ToJson(invalid)),"schema-2 requires faction accounts");
        invalid=JsonUtility.FromJson<RegionSave>(conquestSave);invalid.regions[0].economy.food=-1;
        Require(!campaign.Restore(JsonUtility.ToJson(invalid)),"invalid food save rejected");
        Require(!campaign.Restore(conquestSave.Replace("\"owner\":\"france\"","\"owner\":\"unknown-faction\"")),"invalid save faction rejected");
        foreach(string theater in new[]{"NorthAmerica","SouthAmerica","Africa","Asia","Australasia"})
        {
            var terrain=new GeographyAtlas(theater+"/atlas",false);
            Require(terrain.Data.width>=1537&&terrain.Data.height>=1537,"detailed continental terrain: "+theater);
            Require(terrain.Elevation.Length==terrain.Data.width*terrain.Data.height,"complete continental raster: "+theater);
        }
        VerifyLandscapeArt(atlas,catalog);
        VerifyExpandedCampaign(catalog);
        Debug.Log("CAMPAIGN_GEOGRAPHY_VERIFIED towns="+d.settlements.Length+" routes="+d.routes.Length);
    }
    static void VerifyLandscapeArt(GeographyAtlas atlas,RegionCatalog catalog)
    {
        Color ocean=CampaignLandscape.Biome(atlas,-30,0);
        Require(ocean.maxColorComponent==0,"open ocean has no forest, snow, sand or desert art");
        Require(CampaignLandscape.Aridity(15,24)>.95f,"Sahara remains arid");
        Require(CampaignLandscape.Forest(15,24,200)<.01f,"no procedural forests in the Sahara");
        Require(CampaignLandscape.Snow(46,3800)>.9f,"high Alps receive snow");
        Require(CampaignLandscape.Snow(37,500)<.01f,"low Mediterranean terrain remains snow free");
        Require(CampaignLandscape.Forest(8,46,4000)<.01f,"terrain above the tree line remains bare");
        Require(CampaignLandscape.Snow(82,100)>.95f,"polar land receives snow");
        var json=Resources.Load<TextAsset>("CampaignArtProfiles");
        Require(json!=null,"authored campaign miniature profiles are packaged");
        var profiles=JsonUtility.FromJson<EuropeMapView.ArtCatalog>(json.text);
        var seen=new System.Collections.Generic.HashSet<string>();
        int walls=0,mines=0;
        foreach(var p in profiles.settlements)
        {
            Require(catalog.ForCity(p.settlementId)!=null && seen.Add(p.settlementId),"unique art profile refers to a real capital: "+p.settlementId);
            Require(p.artSize=="city"||p.artSize=="town","supported miniature size");
            Require(p.fortification=="walls"||p.fortification=="open","supported fortification art");
            Require(string.IsNullOrEmpty(p.resourceArt)||p.resourceArt=="mine","supported resource art");
            if(p.fortification=="walls")walls++;
            if(p.resourceArt=="mine")mines++;
        }
        Require(walls>=20 && mines>=4,"walled cities and resource vignettes have authored coverage");
        Debug.Log("CAMPAIGN_LANDSCAPE_ART_VERIFIED walls="+walls+" mines="+mines);
    }
    static void VerifyExpandedCampaign(RegionCatalog catalog)
    {
        Require(catalog.regions.Length>=230 && catalog.regions.Length<=255,"expanded roster fits categorical ownership texture");
        Require(catalog.provinceBorders.Length>100 && catalog.regionBorders.Length>50,"separate shared province and regional boundaries");
        foreach(string name in new[]{"Timbuktu","Kano","Agra","Wuhan","Yakutsk","Sitka","Amarapura","Chiang Mai"})
            Require(Array.Exists(catalog.regions,r=>r.capital.name==name),"expanded regional capital: "+name);
        Require(catalog.Province("upper-canada")!=null && catalog.Province("lower-canada")!=null,"Canada has separate authored provinces");
        var waterways=RiverwayCatalog.Load();Require(waterways.reaches.Length>=15,"navigable river coverage");
        foreach(var reach in waterways.reaches)
        {
            Require(reach.points.Length>=2 && reach.maxDraftMeters>0,"river has geometry and a draft limit");
            var a=reach.points[0];var b=reach.points[reach.points.Length-1];
            Vector2 from=new Vector2(a.lon,a.lat),to=new Vector2(b.lon,b.lat);
            var journey=RiverwayNavigation.Find(waterways,from,to,.5f,.01f);
            Require(journey!=null && journey.DistanceKm>0,"shallow-draft ship follows continuous river: "+reach.id);
            Require(RiverwayNavigation.Find(waterways,from,to,20,.01f)==null,"deep-draft ship cannot use inland river: "+reach.id);
        }
        Require(RiverwayNavigation.Find(waterways,new Vector2(-30,0),new Vector2(-31,0),.5f)==null,"open sea is not mistaken for a river link");
        var nile=Array.Find(waterways.reaches,r=>r.name=="Nile");var rhine=Array.Find(waterways.reaches,r=>r.name=="Rhine");
        Require(RiverwayNavigation.Find(waterways,new Vector2(nile.points[0].lon,nile.points[0].lat),new Vector2(rhine.points[0].lon,rhine.points[0].lat),.5f)==null,"unrelated river networks cannot teleport ships");
        Require(EuropeMapView.WeatherParameters(EuropeMapView.MapWeather.Rain).y==1,"rain preset enables rainfall");
        Require(EuropeMapView.WeatherParameters(EuropeMapView.MapWeather.Snow).z==1,"snow preset enables snowfall");
        Debug.Log("CAMPAIGN_EXPANSION_VERIFIED cities="+catalog.regions.Length+" provinces="+catalog.provinces.Length+" riverReaches="+waterways.reaches.Length);
    }
    static void Require(bool yes,string message){if(!yes)throw new Exception("Campaign geography check failed: "+message);}

    [MenuItem("Empire Campaign/Create campaign scene")]
    public static void CreateScene()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("Europe campaign atlas").AddComponent<EuropeMapView>();
        Directory.CreateDirectory("Assets/Scenes");
        foreach(string folder in new[]{"EuropeGeography","WorldGeography"})
        {
        string texture="Assets/Scripts/Campaign/Geography/Resources/"+folder+"/relief.png";
        var importer=AssetImporter.GetAtPath(texture) as TextureImporter;
        if(importer!=null)
        {
            importer.maxTextureSize=4096;importer.mipmapEnabled=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
        }
        }
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/Campaign.unity");
        AssetDatabase.SaveAssets();Debug.Log("CAMPAIGN_SCENE_CREATED");
    }

    [MenuItem("Empire Campaign/Build Mac atlas")]
    public static void BuildMac()
    {
        Verify();CampaignCountryBuild.Verify();CreateScene();Directory.CreateDirectory("Builds");
        string oldProduct=PlayerSettings.productName;
        var target=UnityEditor.Build.NamedBuildTarget.Standalone;
        string oldIdentifier=PlayerSettings.GetApplicationIdentifier(target);
        try
        {
        PlayerSettings.productName="ETW2 Campaign";
        PlayerSettings.SetApplicationIdentifier(target,"com.etw2.campaign.prototype");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
            scenes=new[]{"Assets/Scenes/Campaign.unity"},locationPathName="Builds/ETW2 Campaign.app",
            target=BuildTarget.StandaloneOSX,options=BuildOptions.CleanBuildCache});
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Campaign build failed: "+report.summary.result);
        Debug.Log("CAMPAIGN_MAC_BUILD_PASSED "+report.summary.totalSize);
        }
        finally
        {
            PlayerSettings.productName=oldProduct;
            PlayerSettings.SetApplicationIdentifier(target,oldIdentifier);
        }
    }
}
