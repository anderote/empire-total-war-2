Shader "EmpireBattle/AtmosphericSky"
{
    Properties
    {
        _ZenithColor("Zenith",Color)=(.27,.46,.65,1)
        _HorizonColor("Horizon",Color)=(.72,.76,.76,1)
        _CloudColor("Cloud",Color)=(.91,.90,.85,1)
        _SunColor("Sun",Color)=(1,.9,.7,1)
        _SunDirection("Sun direction",Vector)=(0,1,0,0)
        _SunStrength("Sun strength",Float)=1
        _Coverage("Cloud cover",Range(0,1))=.3
        _Overcast("Overcast",Range(0,1))=0
        _WeatherTime("Simulation time",Float)=0
    }
    SubShader
    {
        Tags {"Queue"="Background" "RenderType"="Background" "RenderPipeline"="UniversalPipeline"}
        Pass
        {
            Tags {"LightMode"="SRPDefaultUnlit"}
            Cull Off ZWrite Off ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float4 _ZenithColor,_HorizonColor,_CloudColor,_SunColor,_SunDirection;
            float _SunStrength,_Coverage,_Overcast,_WeatherTime;
            struct appdata {float4 vertex:POSITION;};
            struct v2f {float4 position:SV_POSITION;float3 direction:TEXCOORD0;};
            v2f vert(appdata input)
            {v2f output;output.position=UnityObjectToClipPos(input.vertex);output.direction=input.vertex.xyz;return output;}
            float hash(float2 p){p=frac(p*float2(123.34,456.21));p+=dot(p,p+45.32);return frac(p.x*p.y);}
            float noise(float2 p)
            {
                float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);
            }
            float fbm(float2 p)
            {
                float sum=0,amplitude=.53;
                for(int i=0;i<5;i++){sum+=noise(p)*amplitude;p=mul(float2x2(.8,-.6,.6,.8),p)*2.02+11.2;amplitude*=.48;}
                return sum;
            }
            half4 frag(v2f input):SV_Target
            {
                float3 direction=normalize(input.direction);
                float altitude=saturate(direction.y);
                float3 color=lerp(_HorizonColor.rgb,_ZenithColor.rgb,pow(altitude,.55));
                float2 plane=direction.xz/(max(direction.y,.025)+.32);
                float2 wind=float2(.00085,.00035)*_WeatherTime;
                float broad=fbm(plane*2.25+wind);
                float detail=fbm(plane*8.0+wind*1.5+16.3);
                float threshold=lerp(.78,.22,_Coverage);
                float cloud=smoothstep(threshold-.09,threshold+.13,broad*.8+detail*.2);
                cloud*=smoothstep(-.01,.09,direction.y);
                float billow=saturate(broad*1.15+detail*.3);
                float3 cloudShade=_CloudColor.rgb*lerp(.70,1.04,billow);
                float sunDot=saturate(dot(direction,normalize(_SunDirection.xyz)));
                float glow=pow(sunDot,24)*.19*_SunStrength*(1-_Overcast*.9);
                color+=_SunColor.rgb*glow;
                color=lerp(color,cloudShade,cloud*lerp(.88,.99,_Overcast));
                float disc=smoothstep(.99990,.999965,sunDot)*_SunStrength*(1-cloud*.95)*(1-_Overcast*.8);
                color+=_SunColor.rgb*disc;
                return half4(color,1);
            }
            ENDCG
        }
    }
    FallBack Off
}
