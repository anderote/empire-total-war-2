Shader "EmpireBattle/ArtilleryBurst"
{
    Properties { _BurstData("Age, seed, strength, fuel",Vector)=(0,0,1,0) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+4" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _BurstData;
            struct A { float4 position:POSITION;float2 uv:TEXCOORD0; };
            struct V { float4 position:SV_POSITION;float2 uv:TEXCOORD0; };
            V Vert(A i) { V o;o.position=TransformObjectToHClip(i.position.xyz);o.uv=i.uv;return o; }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p) { float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(a),Hash(a+float2(1,0)),f.x),lerp(Hash(a+float2(0,1)),Hash(a+1),f.x),f.y); }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.uv*2-1;
                float age=_BurstData.x,seed=_BurstData.y;
                float n=Noise(p*6+seed+float2(0,-age*9));
                float angle=atan2(p.y,p.x);
                float lobes=.57+.13*sin(angle*5+seed)+.09*sin(angle*9-seed);
                lobes=lerp(lobes,.68+.045*sin(angle*3+seed),_BurstData.w);
                float edge=1-smoothstep(lobes*.45,lobes,length(p)+(n-.5)*.24);
                float torn=smoothstep(.15,.62,n+(.7-length(p))*.5);
                float core=exp(-dot(p,p)*17);
                float3 color=lerp(float3(1.5,.23,.015),float3(3.8,2.6,1.2),core*(1-_BurstData.w*.65));
                return half4(color,edge*torn*_BurstData.z);
            }
            ENDHLSL
        }
    }
}
