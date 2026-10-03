Shader "EmpireBattle/CrowdInfantry"
{
 Properties { _BaseMap("Atlas",2D)="white"{} _BaseColor("Color",Color)=(1,1,1,1) }
 SubShader {
 Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseMap_ST; half4 _BaseColor;
 CBUFFER_END
 float _BattleAtlasBlend;
 float3 _BattleAmbientSky, _BattleAmbientEquator, _BattleAmbientGround;
 TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
 UNITY_INSTANCING_BUFFER_START(Poses)
 UNITY_DEFINE_INSTANCED_PROP(float,_PoseBlend)
 UNITY_DEFINE_INSTANCED_PROP(float4,_RegimentUniform)
 UNITY_INSTANCING_BUFFER_END(Poses)
 struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; float3 nextPosition:TEXCOORD2; float3 nextNormal:TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
 void Morph(inout Attributes v) { UNITY_SETUP_INSTANCE_ID(v); float t=UNITY_ACCESS_INSTANCED_PROP(Poses,_PoseBlend); v.positionOS.xyz=lerp(v.positionOS.xyz,v.nextPosition,t); v.normalOS=normalize(lerp(v.normalOS,v.nextNormal,t)); }
 ENDHLSL
 Pass {
 Tags { "LightMode"="UniversalForward" }
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile_instancing
 #pragma instancing_options assumeuniformscaling forcemaxcount:511
 #pragma multi_compile_fog
 #pragma multi_compile _ _ADDITIONAL_LIGHTS
 #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
 struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; float4 screenShadow:TEXCOORD3; half fog:TEXCOORD4; half4 coat:TEXCOORD5; };
 Varyings Vert(Attributes v) { Morph(v); Varyings o; o.positionWS=TransformObjectToWorld(v.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.positionWS); o.normalWS=TransformObjectToWorldNormal(v.normalOS); o.uv=TRANSFORM_TEX(v.uv,_BaseMap); o.screenShadow=ComputeScreenPos(o.positionCS); o.fog=ComputeFogFactor(o.positionCS.z); o.coat=UNITY_ACCESS_INSTANCED_PROP(Poses,_RegimentUniform); return o; }
 half4 Frag(Varyings i):SV_Target {
 // Screen-door dissolve avoids switching tiny troop silhouettes in a single frame.
 float atlasNoise=frac(sin(dot(floor(i.positionCS.xy),float2(12.9898,78.233)))*43758.5453);
 clip(1-_BattleAtlasBlend-atlasNoise*.999-.0001);
 half3 n=normalize(i.normalWS);
 #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
 float4 shadow=i.screenShadow;
 #else
 float4 shadow=TransformWorldToShadowCoord(i.positionWS);
 #endif
 Light main=GetMainLight(shadow,i.positionWS,half4(1,1,1,1));
 half3 skyFill=n.y>=0?lerp(_BattleAmbientEquator,_BattleAmbientSky,n.y):lerp(_BattleAmbientEquator,_BattleAmbientGround,-n.y);
 half3 lighting=max(max(SampleSH(n),skyFill),half3(.04,.04,.045))*.8+main.color*main.distanceAttenuation*main.shadowAttenuation*saturate(dot(n,main.direction));
 #if defined(_ADDITIONAL_LIGHTS)
 InputData inputData=(InputData)0;
 inputData.positionWS=i.positionWS;
 inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
 uint count=GetAdditionalLightsCount();
 LIGHT_LOOP_BEGIN(count)
 Light extra=GetAdditionalLight(lightIndex,i.positionWS);
 lighting+=extra.color*extra.distanceAttenuation*extra.shadowAttenuation*saturate(dot(n,extra.direction));
 LIGHT_LOOP_END
 #endif
 half3 albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb;
 // Recolour the company's blue/green coat fabric, preserving skin, leather,
 // weapons, white straps and the source texture's shading.
 half cloth=smoothstep(.015,.06,max(albedo.b,albedo.g)-albedo.r)*i.coat.a;
 half shade=clamp(max(albedo.b,albedo.g)/.3,.35,1.5);
 albedo=lerp(albedo,i.coat.rgb*shade,cloth);
 return half4(MixFog(albedo*_BaseColor.rgb*lighting,i.fog),1);
 }
 ENDHLSL
 }
 Pass {
 Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" }
 ZWrite On ZTest LEqual ColorMask 0
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex ShadowVert
 #pragma fragment EmptyFrag
 #pragma multi_compile_instancing
 #pragma instancing_options assumeuniformscaling forcemaxcount:511
 #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
 float3 _LightDirection; float3 _LightPosition;
 float4 ShadowVert(Attributes v):SV_POSITION { Morph(v); float3 p=TransformObjectToWorld(v.positionOS.xyz); float3 n=TransformObjectToWorldNormal(v.normalOS);
 #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
 float3 dir=normalize(_LightPosition-p);
 #else
 float3 dir=_LightDirection;
 #endif
 float4 clip=TransformWorldToHClip(ApplyShadowBias(p,n,dir));
 #if UNITY_REVERSED_Z
 clip.z=min(clip.z,UNITY_NEAR_CLIP_VALUE*clip.w);
 #else
 clip.z=max(clip.z,UNITY_NEAR_CLIP_VALUE*clip.w);
 #endif
 return clip; }
 half4 EmptyFrag():SV_Target { return 0; }
 ENDHLSL
 }
 Pass {
 Name "DepthNormals" Tags { "LightMode"="DepthNormals" } ZWrite On
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex NormalVert
 #pragma fragment NormalFrag
 #pragma multi_compile_instancing
 #pragma instancing_options assumeuniformscaling forcemaxcount:511
 #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
 struct NormalVaryings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; };
 NormalVaryings NormalVert(Attributes v) { Morph(v); NormalVaryings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.normalWS=TransformObjectToWorldNormal(v.normalOS); return o; }
 half4 NormalFrag(NormalVaryings i):SV_Target {
 float3 n=normalize(i.normalWS);
 #if defined(_GBUFFER_NORMALS_OCT)
 float2 oct=PackNormalOctQuadEncode(n);
 return half4(PackFloat2To888(saturate(oct*.5+.5)),0);
 #else
 return half4(n,0);
 #endif
 }
 ENDHLSL
 }
 Pass {
 Name "DepthOnly" Tags { "LightMode"="DepthOnly" } ZWrite On ColorMask R
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex DepthVert
 #pragma fragment DepthFrag
 #pragma multi_compile_instancing
 #pragma instancing_options assumeuniformscaling forcemaxcount:511
 float4 DepthVert(Attributes v):SV_POSITION { Morph(v); return TransformObjectToHClip(v.positionOS.xyz); }
 half4 DepthFrag():SV_Target { return 0; }
 ENDHLSL
 }
 }
}
