Shader "EmpireBattle/CarcassFire"
{
    Properties { _MainTex("Fire flipbook 15 x 4",2D)="black"{} _FireData("Frame, strength, mirror",Vector)=(0,1,0,0) }
    SubShader
    {
        Tags {"RenderType"="Transparent" "Queue"="Transparent+6" "RenderPipeline"="UniversalPipeline"}
        Pass
        {
            Tags {"LightMode"="SRPDefaultUnlit"}
            Blend One One ZWrite Off Cull Off
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma instancing_options forcemaxcount:256
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            UNITY_INSTANCING_BUFFER_START(FireInstances)
                UNITY_DEFINE_INSTANCED_PROP(float4,_FireData)
            UNITY_INSTANCING_BUFFER_END(FireInstances)
            struct A {float4 vertex:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct V {float4 position:SV_POSITION;float2 uv:TEXCOORD0;UNITY_FOG_COORDS(1) UNITY_VERTEX_INPUT_INSTANCE_ID};
            V vert(A i) {V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_TRANSFER_INSTANCE_ID(i,o);o.position=UnityObjectToClipPos(i.vertex);o.uv=i.uv;UNITY_TRANSFER_FOG(o,o.position);return o;}
            float2 FrameUV(float2 uv,float frame)
            {
                float2 tile=float2(fmod(frame,15),3-floor(frame/15));
                float2 inset=_MainTex_TexelSize.xy*float2(15,4)*.5;
                return (tile+lerp(inset,1-inset,uv))/float2(15,4);
            }
            half4 frag(V i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 data=UNITY_ACCESS_INSTANCED_PROP(FireInstances,_FireData);
                float frame=fmod(data.x,60),first=floor(frame);
                float2 uv=i.uv;uv.x=lerp(uv.x,1-uv.x,data.z);
                half4 fire=lerp(tex2D(_MainTex,FrameUV(uv,first)),tex2D(_MainTex,FrameUV(uv,fmod(first+1,60))),frac(frame));
                half4 result=half4(fire.rgb*data.y*1.7,1);
                UNITY_APPLY_FOG_COLOR(i.fogCoord,result,half4(0,0,0,0));
                return result;
            }
            ENDCG
        }
    }
}
