Shader "EmpireBattle/ArtilleryScar"
{
    Properties { _ScarData("Shell, seed, age", Vector)=(1,0,0,0) }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite On Offset -1,-1
            // Reserve one stencil bit for visible excavated soil, including furrows.
            Stencil { Ref 1 ReadMask 1 WriteMask 1 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float4 _ScarData;
            struct A { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 position:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; float2 uv:TEXCOORD2; float fog:TEXCOORD3; };
            V Vert(A v) { V o; o.world=TransformObjectToWorld(v.vertex.xyz); o.position=TransformWorldToHClip(o.world); o.normal=TransformObjectToWorldNormal(v.normal); o.uv=v.uv; o.fog=ComputeFogFactor(o.position.z); return o; }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p) { float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(a),Hash(a+float2(1,0)),f.x),lerp(Hash(a+float2(0,1)),Hash(a+1),f.x),f.y); }
            half4 Frag(V i):SV_Target
            {
                float shell=_ScarData.x, seed=_ScarData.y;
                float2 scarUV=i.uv;
                if(shell<.5) scarUV.x-=(.224*sin(scarUV.y*4.375+seed)+.096*sin(scarUV.y*11.625-seed))*saturate(1-scarUV.y*scarUV.y*1.5625);
                float r=lerp(abs(scarUV.x),length(scarUV),shell);
                float noise=Noise(i.uv*9+seed), fine=Noise(i.uv*43+seed);
                float edge=.9+(noise-.5)*.28;
                clip(edge-r);
                clip(1-abs(i.uv.y)+(noise-.5)*.08);
                float rim=exp(-pow((r-.72)/.16,2));
                float3 basin=lerp(float3(.045,.027,.015),float3(.07,.046,.025),saturate(_ScarData.z/160));
                float rubble=Noise(i.world.xz*5+seed);
                float3 spoil=float3(.105,.066,.034)*(.65+rubble*.65);
                float3 color=lerp(basin,spoil,rim*(.35+.5*rubble));
                float gouge=Noise(float2(i.uv.x*52,i.uv.y*6)+seed);
                color*=lerp(.7,1.15,gouge);
                color*=.8+noise*.3+fine*.15;
                // Fine soil roughness breaks up the newly exposed basin and clumped spoil.
                float grainX=Noise(i.world.xz*18+float2(.06,0))-Noise(i.world.xz*18-float2(.06,0));
                float grainZ=Noise(i.world.xz*18+float2(0,.06))-Noise(i.world.xz*18-float2(0,.06));
                Light sun=GetMainLight();float3 normal=normalize(i.normal+float3(grainX*.6,0,grainZ*.6));
                float3 light=max(SampleSH(normal),float3(.12,.12,.12))+sun.color*saturate(dot(normal,sun.direction))*.8;
                return half4(MixFog(color*light,i.fog),1);
            }
            ENDHLSL
        }
    }
}
