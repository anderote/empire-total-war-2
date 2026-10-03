using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireBattle
{
    public enum WeatherKind { Clear, Cloudy, Fog, Rain }

    /// <summary>Fixed clear dawn presentation for every battle.</summary>
    public sealed class BattleAtmosphere : MonoBehaviour
    {
        BattleWorld world;
        Camera battleCamera;
        Light sun;
        Material sky;
        Mesh dome;
        const float hour = 7f;
        const WeatherKind weather = WeatherKind.Clear;
        public float Hour => hour;
        public string WeatherName => weather.ToString();
        public WeatherKind Weather => weather;
        public Rect ControlsRect => Rect.zero;

        public void Initialize(BattleWorld value, Camera camera, Light sunlight)
        {
            ReleaseResources();
            world = value; battleCamera = camera; sun = sunlight;
            Shader skyShader = Resources.Load<Shader>("BattleAtmosphericSky");
            if (!skyShader)
            { Debug.LogError("Atmosphere shaders missing from Resources."); enabled = false; return; }
            sky = new Material(skyShader) { name="Procedural battlefield sky" };
            dome = MakeDome();
            ApplyLighting();
            enabled = true;
        }

        // Retain presentation API compatibility while keeping the playtest fixed at clear dawn.
        public void SetHour(float value) => ApplyLighting();
        public void SetWeather(WeatherKind value) => ApplyLighting();

        void ApplyLighting()
        {
            if (!sky || !battleCamera || !sun) return;
            float elevation = Mathf.Sin((hour-6f)*Mathf.PI/12f)*70f;
            float daylight = Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(-8f,24f,elevation));
            float warmth = 1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(3f,42f,elevation));
            float overcast = weather==WeatherKind.Clear ? 0f : weather==WeatherKind.Cloudy ? .55f : weather==WeatherKind.Fog ? .70f : .9f;
            sun.transform.rotation = Quaternion.Euler(elevation,(hour-6f)*15f+85f,0f);
            Color daylightSun = Color.Lerp(new Color(1f,.96f,.86f),new Color(1f,.65f,.38f),warmth);
            sun.color = Color.Lerp(daylightSun,new Color(.76f,.83f,.9f),overcast*.8f);
            sun.intensity = daylight*Mathf.Lerp(1.35f,.35f,overcast);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = Mathf.Lerp(.7f,.35f,overcast);
            sun.shadowBias = .05f;
            sun.shadowNormalBias = .35f;
            QualitySettings.shadowDistance = 100f;
            Color zenith = Color.Lerp(new Color(.035f,.055f,.10f),new Color(.27f,.46f,.65f),daylight);
            zenith = Color.Lerp(zenith,new Color(.44f,.49f,.53f)*Mathf.Lerp(.15f,1f,daylight),overcast);
            Color horizon = Color.Lerp(new Color(.075f,.085f,.12f),new Color(.72f,.76f,.76f),daylight);
            horizon = Color.Lerp(horizon,new Color(.88f,.68f,.48f),warmth*daylight*(1f-overcast)*.60f);
            horizon = Color.Lerp(horizon,new Color(.58f,.63f,.66f)*Mathf.Lerp(.2f,1f,daylight),overcast);
            Color cloud = Color.Lerp(new Color(.91f,.90f,.85f),new Color(.52f,.58f,.63f),overcast);
            cloud *= Mathf.Lerp(.12f,1f,daylight);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(zenith,new Color(.59f,.65f,.72f)*daylight,.55f);
            RenderSettings.ambientEquatorColor = horizon*Mathf.Lerp(.72f,.78f,overcast);
            RenderSettings.ambientGroundColor = new Color(.36f,.38f,.34f)*Mathf.Lerp(.15f,1f,daylight);
            // Crowd draw calls use LightProbeUsage.Off. Supply the current ambient
            // hemisphere explicitly instead of relying on per-renderer SH data.
            Shader.SetGlobalVector("_BattleAmbientSky",RenderSettings.ambientSkyColor.linear);
            Shader.SetGlobalVector("_BattleAmbientEquator",RenderSettings.ambientEquatorColor.linear);
            Shader.SetGlobalVector("_BattleAmbientGround",RenderSettings.ambientGroundColor.linear);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = horizon;
            RenderSettings.fogDensity = weather==WeatherKind.Clear ? .00006f : weather==WeatherKind.Cloudy ? .00014f : weather==WeatherKind.Fog ? .00055f : .00028f;
            battleCamera.backgroundColor = horizon;
            sky.SetColor("_ZenithColor",zenith);
            sky.SetColor("_HorizonColor",horizon);
            sky.SetColor("_CloudColor",cloud);
            sky.SetColor("_SunColor",sun.color);
            sky.SetVector("_SunDirection",-sun.transform.forward);
            sky.SetFloat("_SunStrength",sun.intensity);
            sky.SetFloat("_Coverage",weather==WeatherKind.Clear ? .30f : weather==WeatherKind.Cloudy ? .50f : weather==WeatherKind.Fog ? .78f : .72f);
            sky.SetFloat("_Overcast",overcast);
        }

        void LateUpdate()
        {
            if (world==null || !battleCamera || !sky) return;
            sky.SetFloat("_WeatherTime",world.Time);
            float radius = Mathf.Max(10f,battleCamera.farClipPlane*.94f);
            Graphics.DrawMesh(dome,Matrix4x4.TRS(battleCamera.transform.position,Quaternion.identity,Vector3.one*radius),
                sky,gameObject.layer,battleCamera,0,null,ShadowCastingMode.Off,false);
        }

        static Mesh MakeDome()
        {
            const int columns=48, rows=24;
            var vertices=new Vector3[(columns+1)*(rows+1)];
            var triangles=new int[columns*rows*6];
            for(int row=0;row<=rows;row++)
            {
                float latitude=Mathf.PI*row/rows;
                for(int column=0;column<=columns;column++)
                {
                    float longitude=Mathf.PI*2f*column/columns;
                    vertices[row*(columns+1)+column]=new Vector3(Mathf.Sin(latitude)*Mathf.Cos(longitude),Mathf.Cos(latitude),Mathf.Sin(latitude)*Mathf.Sin(longitude));
                }
            }
            int index=0;
            for(int row=0;row<rows;row++) for(int column=0;column<columns;column++)
            {
                int a=row*(columns+1)+column,b=a+columns+1;
                triangles[index++]=a;triangles[index++]=b;triangles[index++]=a+1;
                triangles[index++]=a+1;triangles[index++]=b;triangles[index++]=b+1;
            }
            var mesh=new Mesh{name="Camera-centered atmosphere dome"};
            mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateBounds();return mesh;
        }
        void ReleaseResources()
        {
            if(sky) Destroy(sky);if(dome) Destroy(dome);
        }
        void OnDestroy(){
            ReleaseResources();
            Shader.SetGlobalVector("_BattleAmbientSky",Vector4.zero);
            Shader.SetGlobalVector("_BattleAmbientEquator",Vector4.zero);
            Shader.SetGlobalVector("_BattleAmbientGround",Vector4.zero);
        }
    }
}
