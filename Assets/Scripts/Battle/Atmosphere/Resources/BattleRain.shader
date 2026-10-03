Shader "EmpireBattle/AtmosphericRain"
{
    SubShader
    {
        Tags {"Queue"="Transparent+30" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"}
        Pass
        {
            Tags {"LightMode"="SRPDefaultUnlit"}
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma instancing_options forcemaxcount:256
            #include "UnityCG.cginc"
            struct appdata{float4 vertex:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f{float4 position:SV_POSITION;float2 uv:TEXCOORD0;};
            v2f vert(appdata input)
            {UNITY_SETUP_INSTANCE_ID(input);v2f output;output.position=UnityObjectToClipPos(input.vertex);output.uv=input.uv;return output;}
            half4 frag(v2f input):SV_Target
            {
                float edge=1-abs(input.uv.x*2-1);
                float tip=saturate(input.uv.y*8)*saturate((1-input.uv.y)*4);
                return half4(.71,.78,.83,edge*tip*.23);
            }
            ENDCG
        }
    }
    FallBack Off
}
