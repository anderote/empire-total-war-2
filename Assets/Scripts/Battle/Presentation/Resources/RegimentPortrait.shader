Shader "EmpireBattle/RegimentPortrait"
{
    Properties
    {
        _BaseMap("Atlas",2D)="white"{}
        _BaseColor("Color",Color)=(1,1,1,1)
        _RegimentUniform("Uniform",Vector)=(0,0,0,0)
        _PoseBlend("Pose blend",Float)=0
        _Morph("Baked pose",Float)=1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _BaseColor, _RegimentUniform;
            float _PoseBlend, _Morph;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; float3 nextPosition:TEXCOORD2; float3 nextNormal:TEXCOORD3; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float2 uv:TEXCOORD1; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                float t=_PoseBlend*_Morph;
                float3 position=lerp(v.positionOS.xyz,v.nextPosition,t);
                o.positionCS=TransformObjectToHClip(position);
                o.normalWS=TransformObjectToWorldNormal(normalize(lerp(v.normalOS,v.nextNormal,t)));
                o.uv=TRANSFORM_TEX(v.uv,_BaseMap);
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                half3 albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb;
                half cloth=smoothstep(.015,.06,max(albedo.b,albedo.g)-albedo.r)*_RegimentUniform.a;
                half shade=clamp(max(albedo.b,albedo.g)/.3,.35,1.5);
                albedo=lerp(albedo,_RegimentUniform.rgb*shade,cloth);
                half3 n=normalize(i.normalWS);
                half key=saturate(dot(n,normalize(float3(-.4,.8,.6))));
                half rim=saturate(dot(n,normalize(float3(.7,.3,-.6))));
                half3 light=half3(.48,.5,.55)+key*half3(.65,.6,.5)+rim*half3(.12,.16,.22);
                return half4(albedo*_BaseColor.rgb*light,1);
            }
            ENDHLSL
        }
    }
}
