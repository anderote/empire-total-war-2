Shader "EmpireCampaign/Precipitation"
{
    Properties { _Weather("Cloud rain snow fog",Vector)=(.24,0,0,0) _MapTime("Map time",Float)=0 }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Weather;float _MapTime;
            CBUFFER_END
            struct A { float4 positionOS:POSITION;float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0; };
            V vert(A a){V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.uv=a.uv;return o;}
            float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            half4 frag(V i):SV_Target
            {
                float2 rain=i.uv*float2(150,28)+float2(i.uv.y*18,_MapTime*18);
                float2 cell=floor(rain),f=frac(rain);
                float drop=step(.86,hash(cell))*(1-smoothstep(0,.065,abs(f.x-.5)))*(1-smoothstep(.15,.46,abs(f.y-.5)));
                float2 snow=i.uv*float2(95,60)+float2(sin(_MapTime*.25)*1.8,_MapTime*2.1);
                float2 s=frac(snow)-.5;
                float flake=step(.75,hash(floor(snow)))*(1-smoothstep(.035,.10,length(s)));
                return half4(lerp(half3(.60,.73,.80),half3(.91,.94,.95),_Weather.z),drop*_Weather.y*.22+flake*_Weather.z*.48);
            }
            ENDHLSL
        }
    }
}
