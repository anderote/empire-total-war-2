Shader "EmpireBattle/MusketFlash"
{
    Properties { _FlashColor("Flash color",Color)=(1,.5,.08,1) }
    SubShader
    {
        Tags { "Queue"="Transparent+19" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma instancing_options forcemaxcount:256
            #include "UnityCG.cginc"
            UNITY_INSTANCING_BUFFER_START(Flash)
                UNITY_DEFINE_INSTANCED_PROP(float4,_FlashColor)
            UNITY_INSTANCING_BUFFER_END(Flash)
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v,o);
                o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o; }
            half4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 color=UNITY_ACCESS_INSTANCED_PROP(Flash,_FlashColor);
                float2 p=i.uv*2-1;
                float radius=dot(p,p);
                float core=exp(-radius*14);
                float flare=exp(-radius*4)*(1-smoothstep(.55,1,radius));
                float rays=exp(-abs(p.y)*22)*exp(-abs(p.x)*4);
                return half4(lerp(color.rgb,float3(1.8,1.5,.85),core),color.a*(flare*.6+core+rays*.2));
            }
            ENDCG
        }
    }
}
