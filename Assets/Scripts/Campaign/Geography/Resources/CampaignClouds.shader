Shader "EmpireCampaign/Clouds"
{
    Properties { _Weather("Cloud rain snow fog",Vector)=(.24,0,0,0) _MapTime("Map time",Float)=0 _Opacity("Cloud opacity",Float)=.2 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CampaignWeather.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Weather;float _MapTime,_Opacity;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; };
            struct V { float4 positionCS:SV_POSITION; float3 normal:TEXCOORD0; };
            V vert(A a){V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.normal=normalize(a.positionOS.xyz);return o;}
            half4 frag(V i):SV_Target
            {
                float cloud=MapCloud(normalize(i.normal),_MapTime,_Weather.x);
                half3 color=lerp(half3(.83,.88,.86),half3(.40,.47,.49),_Weather.y*.6);
                return half4(color,cloud*_Opacity);
            }
            ENDHLSL
        }
    }
}
