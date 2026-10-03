Shader "EmpireBattle/ArtilleryEmberBed"
{
    Properties { _FuelData("Time, seed, strength", Vector) = (0,0,1,0) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+3" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _FuelData;
            float4 _FuelPatches[7];
            struct A { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; };
            V Vert(A i) { V o; o.vertex=TransformObjectToHClip(i.vertex.xyz); o.uv=i.uv; return o; }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p) { float2 a=floor(p),f=frac(p);f=f*f*(3-2*f); return lerp(lerp(Hash(a),Hash(a+float2(1,0)),f.x),lerp(Hash(a+float2(0,1)),Hash(a+1),f.x),f.y); }
            half4 Frag(V i):SV_Target
            {
                float seed=_FuelData.y, time=_FuelData.x;
                float2 p=i.uv;
                float edge=0;
                for(int patch=0;patch<7;patch++)
                {
                    float radius=length((p-_FuelPatches[patch].xy)*float2(1,.85)/max(.05,_FuelPatches[patch].z));
                    edge=max(edge,1-smoothstep(.35,1,radius+(Noise(p*4+seed)-.5)*.4));
                }
                float bank=Noise(p*3+seed);
                // Oily, broken burning streaks along the splash, rather than lava-like cracks.
                float flow=Noise(p*float2(6,2)+float2(seed,seed-time*.35));
                float pulse=.6+.4*sin(time*4+bank*14+seed);
                float heat=pow(saturate((flow-.35)*1.7),3)*pulse;
                float3 color=float3(.025,.019,.012)+float3(1.9,.38,.025)*heat;
                return half4(color,(.68+heat*.3)*edge*_FuelData.z);
            }
            ENDHLSL
        }
    }
}
