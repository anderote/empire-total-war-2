Shader "EmpireBattle/RegimentalSilk"
{
 Properties { _BaseMap("Heraldry",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1) }
 SubShader {
 Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
 Cull Off
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseMap_ST; half4 _BaseColor;
 CBUFFER_END
 float _BattleAtlasBlend;
 float3 _BattleAmbientSky, _BattleAmbientEquator, _BattleAmbientGround;
 struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
 ENDHLSL
 Pass {
 Tags { "LightMode"="UniversalForward" }
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile_fog
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; half fog:TEXCOORD3; };
 Varyings Vert(Attributes v){
 Varyings o; o.positionWS=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);
 o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.uv=v.uv;o.fog=ComputeFogFactor(o.positionCS.z);return o;
 }
 half4 Frag(Varyings i, FRONT_FACE_TYPE frontFace:FRONT_FACE_SEMANTIC):SV_Target {
 float noise=frac(sin(dot(floor(i.positionCS.xy),float2(12.9898,78.233)))*43758.5453);
 clip(1-_BattleAtlasBlend-noise*.999-.0001);
 half3 n=normalize(i.normalWS)*IS_FRONT_VFACE(frontFace,1,-1);
 Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
 half diffuse=saturate(dot(n,light.direction));
 // Thin silk transmits a little sunlight through the back of a fold.
 half transmission=.22*saturate(dot(-n,light.direction));
 half3 skyFill=n.y>=0?lerp(_BattleAmbientEquator,_BattleAmbientSky,n.y):lerp(_BattleAmbientEquator,_BattleAmbientGround,-n.y);
 half3 lighting=max(max(SampleSH(n),skyFill),half3(.1,.1,.12))+light.color*(diffuse*light.shadowAttenuation+transmission);
 half3 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;
 return half4(MixFog(color*lighting,i.fog),1);
 }
 ENDHLSL
 }
 Pass {
 Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" }
 ZWrite On ZTest LEqual ColorMask 0
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
 float3 _LightDirection;float3 _LightPosition;
 float4 Vert(Attributes v):SV_POSITION {
 float3 p=TransformObjectToWorld(v.positionOS.xyz),n=TransformObjectToWorldNormal(v.normalOS);
 #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
 float3 direction=normalize(_LightPosition-p);
 #else
 float3 direction=_LightDirection;
 #endif
 float4 clip=TransformWorldToHClip(ApplyShadowBias(p,n,direction));
 #if UNITY_REVERSED_Z
 clip.z=min(clip.z,UNITY_NEAR_CLIP_VALUE*clip.w);
 #else
 clip.z=max(clip.z,UNITY_NEAR_CLIP_VALUE*clip.w);
 #endif
 return clip;
 }
 half4 Frag():SV_Target{return 0;}
 ENDHLSL
 }
 Pass {
 Name "DepthOnly" Tags { "LightMode"="DepthOnly" } ZWrite On ColorMask R
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 float4 Vert(Attributes v):SV_POSITION{return TransformObjectToHClip(v.positionOS.xyz);}
 half4 Frag():SV_Target{return 0;}
 ENDHLSL
 }
 }
}
