Shader "EmpireBattle/BattleBlood"
{
    Properties { _TerrainHeights("Terrain",2D)="black"{} _FieldHalf("Field half size",Float)=1500 _BloodTime("Time",Float)=0 _BloodData("Born, seed, pool, surface",Vector)=(0,0,1,1) [HideInInspector] _CraterStencilComp("Crater stencil comparison",Float)=6 }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest+20" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite On Offset -1,-1
            // Scars render first (AlphaTest+10); ground blood excludes their pixels.
            Stencil { Ref 1 ReadMask 1 WriteMask 0 Comp [_CraterStencilComp] Pass Keep }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_TerrainHeights);SAMPLER(sampler_TerrainHeights);
            float _FieldHalf,_BloodTime;
            UNITY_INSTANCING_BUFFER_START(Blood)
            UNITY_DEFINE_INSTANCED_PROP(float4,_BloodData)
            UNITY_INSTANCING_BUFFER_END(Blood)
            struct Attributes {float4 vertex:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct Varyings {float4 position:SV_POSITION;float3 world:TEXCOORD0;float2 uv:TEXCOORD1;float3 normal:TEXCOORD2;float fog:TEXCOORD3;UNITY_VERTEX_INPUT_INSTANCE_ID};
            float Height(float2 xz){float2 uv=(xz/(_FieldHalf*2)+.5)*(256.0/257.0)+.5/257.0;return SAMPLE_TEXTURE2D_LOD(_TerrainHeights,sampler_TerrainHeights,uv,0).r;}
            Varyings Vert(Attributes v)
            {
                Varyings o;UNITY_SETUP_INSTANCE_ID(v);UNITY_TRANSFER_INSTANCE_ID(v,o);
                float4 d=UNITY_ACCESS_INSTANCED_PROP(Blood,_BloodData);float age=max(0,_BloodTime-d.x);
                float growth=lerp(1,.13+.87*(1-exp(-age/14)),saturate(d.z));
                float3 local=v.vertex.xyz;if(d.w>.5)local.xz*=growth;
                o.world=TransformObjectToWorld(local);
                o.normal=TransformObjectToWorldNormal(v.normal);
                if(d.w>.5){o.world.y=Height(o.world.xz)+.018;float2 e=float2(.25,0);o.normal=normalize(float3(Height(o.world.xz-e)-Height(o.world.xz+e),.5,Height(o.world.xz-e.yx)-Height(o.world.xz+e.yx)));}
                o.position=TransformWorldToHClip(o.world);o.uv=v.uv*2-1;o.fog=ComputeFogFactor(o.position.z);return o;
            }
            float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float Noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);}
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);float4 d=UNITY_ACCESS_INSTANCED_PROP(Blood,_BloodData);
                float age=max(0,_BloodTime-d.x),dry=saturate(age/180),wet=1-dry;
                float noise=Noise(i.uv*5+d.y),fine=Noise(i.uv*19+d.y);
                float radius=length(i.uv);float edge=.74+(noise-.5)*.26+(fine-.5)*.055;
                if(d.z<-.5){float angle=atan2(i.uv.y,i.uv.x);edge=.48+.36*pow(abs(sin(angle*7+d.y)),10)+(noise-.5)*.18;}
                if(d.w>.5)clip(edge-radius);
                float3 normal=normalize(i.normal);
                if(d.w>.5)normal=normalize(normal+float3(sin(i.world.x*23+age*1.4),0,cos(i.world.z*19-age))*.012*wet);
                Light light=GetMainLight();float3 view=normalize(GetWorldSpaceViewDir(i.world));float3 halfway=normalize(view+light.direction);
                float spec=pow(saturate(dot(normal,halfway)),90)*wet*.65;
                float rim=smoothstep(edge-.045,edge,radius)*d.w;
                half3 color=lerp(half3(.52,.012,.018),half3(.065,.012,.009),dry)*(1+(noise-.5)*.12);
                color*=1-rim*.3;
                half3 illumination=max(SampleSH(normal),half3(.10,.10,.10))+light.color*saturate(dot(normal,light.direction))*.75;
                color=color*illumination+light.color*spec;
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
    }
}
