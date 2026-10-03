using UnityEngine;

namespace EmpireCampaign.Geography
{
    public sealed partial class EuropeMapView
    {
        // Presentation presets; campaign turns and economic weather remain independent.
        public enum MapWeather { Clear, Cloudy, Rain, Snow, Fog }
        public static Vector4 WeatherParameters(MapWeather weather)
        {
            switch(weather)
            {
                case MapWeather.Cloudy:return new Vector4(.62f,0,0,.03f);
                case MapWeather.Rain:return new Vector4(.80f,1,0,.08f);
                case MapWeather.Snow:return new Vector4(.68f,0,1,.10f);
                case MapWeather.Fog:return new Vector4(.46f,0,0,.34f);
                default:return new Vector4(.24f,0,0,0);
            }
        }
        MapWeather mapWeather;
        bool showClouds=true,showWaves=true;
        Material cloudMaterial,precipitationMaterial;
        GameObject cloudShell,precipitation;
        void BuildAtmosphere()
        {
            cloudMaterial=MakeMaterial("EmpireCampaign/Clouds");
            precipitationMaterial=MakeMaterial("EmpireCampaign/Precipitation");
            const int nx=96,ny=48;
            var v=new Vector3[(nx+1)*(ny+1)];var triangles=new int[nx*ny*6];int t=0;
            for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++)
            {
                int i=y*(nx+1)+x;
                v[i]=GeographyAtlas.Normal(-180+x*360f/nx,-90+y*180f/ny)*(GeographyAtlas.EarthRadiusKm+470);
                if(x<nx&&y<ny){triangles[t++]=i;triangles[t++]=i+nx+1;triangles[t++]=i+1;triangles[t++]=i+1;triangles[t++]=i+nx+1;triangles[t++]=i+nx+2;}
            }
            var shell=new Mesh {name="Drifting spherical cloud layer",vertices=v,triangles=triangles};shell.RecalculateBounds();
            cloudShell=MeshObject("Campaign clouds",shell,cloudMaterial);
            // A single camera-local curtain shades rain/snow; no per-droplet GameObjects.
            var quad=new Mesh {name="Campaign precipitation curtain",vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)},uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},triangles=new[]{0,1,2,0,2,3}};
            quad.RecalculateBounds();precipitation=MeshObject("Campaign rain and snow",quad,precipitationMaterial);
            precipitation.transform.SetParent(cam.transform,false);precipitation.transform.localPosition=new Vector3(0,0,1);
        }
        void UpdateAtmosphere()
        {
            Vector4 weather=WeatherParameters(mapWeather);
            float time=Time.unscaledTime;
            reliefMaterial.SetVector("_Weather",weather);
            reliefMaterial.SetFloat("_MapTime",time);
            reliefMaterial.SetFloat("_Waves",showWaves?Mathf.Clamp01((7000-distance)/3000):0);
            reliefMaterial.SetFloat("_CloudShadows",showClouds?1:0);
            cloudMaterial.SetVector("_Weather",weather);cloudMaterial.SetFloat("_MapTime",time);
            // Below the cloud deck, shadows carry cloud motion without obscuring towns.
            cloudMaterial.SetFloat("_Opacity",Mathf.Lerp(.10f,.34f,Mathf.InverseLerp(600,7000,distance)));
            cloudShell.SetActive(showClouds&&distance>600);
            precipitationMaterial.SetVector("_Weather",weather);precipitationMaterial.SetFloat("_MapTime",time);
            precipitation.SetActive((weather.y>0||weather.z>0)&&distance<7000);
            float height=Mathf.Tan(cam.fieldOfView*Mathf.Deg2Rad*.5f);
            precipitation.transform.localScale=new Vector3(height*cam.aspect,height,1);
        }
        void DrawAtmosphereControls()
        {
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            showWaves=Empire.UI.EmpireTheme.Toggle(showWaves,"Waves");
            showClouds=Empire.UI.EmpireTheme.Toggle(showClouds,"Clouds");
            GUILayout.EndHorizontal();
            if(GUILayout.Button("Weather: "+mapWeather+"  ›",button))mapWeather=(MapWeather)(((int)mapWeather+1)%5);
        }
    }
}
