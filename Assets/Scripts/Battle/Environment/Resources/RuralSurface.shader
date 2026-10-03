Shader "EmpireBattle/RuralSurface"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _DetailTex ("Ground detail", 2D) = "white" {}
        _GroundCoverTex ("Static turf color and relief", 2D) = "gray" {}
        _GroundCoverStrength ("Static turf strength", Range(0,1)) = 0
        _DetailStrength ("Ground texture strength", Range(0,1)) = 0
        _ReliefStrength ("Ground relief strength", Range(0,2)) = 1
        _BarkTex ("Bark detail", 2D) = "white" {}
        _SceneryDetail ("Branch and leaf detail", Float) = 0
        _FoliageWind ("Canopy wind", Float) = 0
        _HeightMap ("Grass root heights", 2D) = "black" {}
        _HeightResolution ("Height grid resolution", Float) = 257
        _SurfaceMap ("Field surface map", 2D) = "white" {}
        _UseSurfaceMap ("Use field map", Float) = 0
        _MapSize ("Map size", Float) = 3000
        _WindStrength ("Grass wind", Float) = 0
        _GrassFadeRange ("Grass fade range", Float) = 40
        _GrassLodStart ("Grass density fade start", Float) = 35
        _GrassLodEnd ("Grass density fade end", Float) = 65
        _GrassDistance ("Grass draw distance", Float) = 0
        _Cull ("Cull mode", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _DetailStrength, _ReliefStrength, _GroundCoverStrength;
            float _UseSurfaceMap, _MapSize, _WindStrength, _GrassDistance, _Cull, _HeightResolution, _SceneryDetail, _FoliageWind, _GrassFadeRange, _GrassLodStart, _GrassLodEnd;
        CBUFFER_END
        TEXTURE2D(_SurfaceMap); SAMPLER(sampler_SurfaceMap);
        TEXTURE2D(_BarkTex); SAMPLER(sampler_BarkTex);
        TEXTURE2D(_HeightMap); SAMPLER(sampler_HeightMap);
        float3 RuralPosition(float3 positionOS)
        {
            if(_WindStrength<=0 && _FoliageWind<=0)return positionOS;
            float3 root=TransformObjectToWorld(float3(positionOS.x,0,positionOS.z));
            float wave=sin(root.x*.13+root.z*.17+_Time.y*1.6)+.4*sin(root.z*.37-_Time.y*2.3);
            positionOS.xz+=float2(wave,wave*.45)*_WindStrength*positionOS.y*positionOS.y;
            // A separate canopy wind keeps tree/hedge vertices out of the grass-height path.
            float branchWind=sin(root.x*.09+root.z*.11+_Time.y*.85);
            positionOS.xz+=float2(branchWind,branchWind*.4)*_FoliageWind*saturate(positionOS.y*.12)*positionOS.y*.12;
            return positionOS;
        }
        float RuralHash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
        float Noise(float2 p)
        {
            float2 cell=floor(p),f=frac(p);f=f*f*(3-2*f);
            return lerp(lerp(RuralHash(cell),RuralHash(cell+float2(1,0)),f.x),
                lerp(RuralHash(cell+float2(0,1)),RuralHash(cell+1),f.x),f.y);
        }
        float _BattleAtlasBlend, _BattleAtlasWoodsCount;
        float4 _BattleAtlasWoods[16], _BattleAtlasFields[16];
        float _BattleAtlasFieldCount, _BattleAtlasMapScale;
        float AtlasRoadDistance(float2 q)
        {
            float d=abs(q.x-(410+sin(q.y*.0021)*110));
            for(int i=0;i<2;i++)
            {
                float2 farm=_BattleAtlasFields[i].xy;
                float2 end=float2(410+sin(farm.y*.0021)*110,farm.y+45);
                float2 roadSegment=end-farm;
                float t=saturate(dot(q-farm,roadSegment)/dot(roadSegment,roadSegment));
                d=min(d,length(q-farm-roadSegment*t));
            }
            return d;
        }
        float _EarthworkCount;
        float4 _EarthworkShape[32], _EarthworkAxes[32];
        float IntactEarth(float2 root)
        {
            for (int i=0; i<(int)_EarthworkCount; i++)
            {
                float4 shape=_EarthworkShape[i], axes=_EarthworkAxes[i];
                float2 d=root-shape.xy;
                float u=dot(d,float2(axes.y,-axes.x))/shape.z, v=dot(d,axes.xy)/shape.w;
                if(axes.w<.5) u-=(.28*sin(v*3.5+axes.z)+.12*sin(v*9.3-axes.z))*saturate(1-v*v);
                float a=atan2(v,u);
                float irregular=axes.w>.5 ? 1+.09*sin(a*3+axes.z)+.055*sin(a*7-axes.z*2) : 1+.24*sin(v*4.5+axes.z)+.16*sin(v*13-axes.z);
                float r=lerp(abs(u),length(float2(u,v)),axes.w)/irregular;
                if(r<1.15 && (axes.w>.5 || abs(v)<.98)) return 0;
            }
            return 1;
        }
        float3 GrassPosition(float3 positionOS,float2 rootOS,half retained)
        {
            if(_WindStrength<=0)return RuralPosition(positionOS);
            float3 root=TransformObjectToWorld(float3(rootOS.x,0,rootOS.y));
            float2 mapUV=root.xz/_MapSize+.5;
            float2 heightUV=(mapUV*(_HeightResolution-1)+.5)/_HeightResolution;
            float groundHeight=SAMPLE_TEXTURE2D_LOD(_HeightMap,sampler_HeightMap,heightUV,0).r-.018;
            float cameraDistance=distance(float3(root.x,groundHeight,root.z),_WorldSpaceCameraPos);
            // Non-silhouette blades shrink to zero before the CPU selects the subset mesh.
            float lod=lerp(1-smoothstep(_GrassLodStart,_GrassLodEnd,cameraDistance),1,retained);
            float soil=SAMPLE_TEXTURE2D_LOD(_SurfaceMap,sampler_SurfaceMap,mapUV,0).a;
            float density=lerp(.38,.96,smoothstep(.2,.8,Noise(root.xz*.045+float2(3,41))));
            density*=1-smoothstep(.48,.72,soil);
            float alive=step(RuralHash(root.xz*13.71),density) * IntactEarth(root.xz);
            alive*=step(0,mapUV.x)*step(mapUV.x,1)*step(0,mapUV.y)*step(mapUV.y,1);
            // Stubble fades into meadow at field edges rather than switching entire patches.
            float cut=smoothstep(.14,.42,soil);
            float height=lerp(.65,1.15,Noise(root.xz*.19))*lerp(1,.28,cut)*alive;
            positionOS.xz=lerp(rootOS,positionOS.xz,alive*lod);
            positionOS.y*=height*lod;
            // Only silhouette blades sway; smaller cover stays still.
            float3 positionWS=TransformObjectToWorld(retained>.5?RuralPosition(positionOS):positionOS);
            positionWS.y+=groundHeight;
            return TransformWorldToObject(positionWS);
        }
        void RuralFade(float3 positionWS,float2 screenPosition)
        {
            if(_GrassDistance>0)
            {
                float fade=saturate((_GrassDistance-distance(positionWS,_WorldSpaceCameraPos))/max(.1,_GrassFadeRange));
                clip(fade-RuralHash(floor(screenPosition))*.999-.001);
            }
        }
        ENDHLSL
        Pass
        {
            Cull [_Cull]
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float2 root : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float4 screenShadow : TEXCOORD2; half fog : TEXCOORD3; float2 uv : TEXCOORD4; half4 color : COLOR; };
            TEXTURE2D(_DetailTex); SAMPLER(sampler_DetailTex);
            TEXTURE2D(_GroundCoverTex); SAMPLER(sampler_GroundCoverTex);
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                o.positionWS=TransformObjectToWorld(GrassPosition(input.positionOS.xyz,input.root,input.color.a));
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(input.normalOS);
                o.screenShadow=ComputeScreenPos(o.positionCS);
                o.fog=ComputeFogFactor(o.positionCS.z);
                o.uv=input.root;
                o.color=input.color*_BaseColor;
                if(_WindStrength>0)
                {
                    float3 root=TransformObjectToWorld(float3(input.root.x,0,input.root.y));
                    float soil=SAMPLE_TEXTURE2D_LOD(_SurfaceMap,sampler_SurfaceMap,root.xz/_MapSize+.5,0).a;
                    o.color.rgb=lerp(o.color.rgb,half3(.48,.39,.19),smoothstep(.14,.42,soil)*.75);
                    o.color.rgb*=lerp(.83,1.14,Noise(root.xz*.11));
                }
                return o;
            }
            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                RuralFade(input.positionWS,input.positionCS.xy);
                half3 normal=normalize(input.normalWS)*(frontFace?1:-1);
                if(_WindStrength>0)normal=normalize(float3(normal.x,max(normal.y,.55),normal.z));
                half4 surface=half4(1,1,1,0);
                if(_UseSurfaceMap>0)surface=SAMPLE_TEXTURE2D(_SurfaceMap,sampler_SurfaceMap,input.positionWS.xz/_MapSize+.5);
                half soil=surface.a*_UseSurfaceMap;
                half ploughed=smoothstep(.94,.99,soil);
                float2 p=input.positionWS.xz;
                float distanceToCamera=distance(input.positionWS,_WorldSpaceCameraPos);
                float microFade=1-smoothstep(35,160,distanceToCamera);
                // World-space variation stays continuous across terrain cells and avoids a tiled carpet.
                // Fade frequencies by their pixel footprint, including grazing camera angles.
                float footprint=max(length(ddx(p)),length(ddy(p)));
                float grainFade=1-smoothstep(.045,.18,footprint);
                float mesoFade=1-smoothstep(.35,1.2,footprint);
                float furrowFade=1-smoothstep(.08,.4,footprint);
                float grain=.5,meso=.5,furrow=0;
                if(_UseSurfaceMap>0)
                {
                    grain=Noise(p*8.5);
                    meso=Noise(p*1.6+float2(37,19));
                    furrow=sin(p.x*7.5+Noise(p*.035)*.5);
                }
                half3 detailValue=1;
                if(_DetailStrength>0)
                {
                    // The same source covers ~4m and ~0.8m; rotated sampling breaks repetition.
                    float2 rotated=float2(p.x*.8-p.y*.6,p.x*.6+p.y*.8);
                    half3 coarse=SAMPLE_TEXTURE2D(_DetailTex,sampler_DetailTex,p*.24).rgb;
                    half3 fine=SAMPLE_TEXTURE2D(_DetailTex,sampler_DetailTex,rotated*1.25+float2(.37,.73)).rgb;
                    float coarseLuma=dot(coarse,half3(.2126,.7152,.0722));
                    float fineLuma=dot(fine,half3(.2126,.7152,.0722));
                    float grassDetail=lerp(clamp(coarseLuma*2.3,.55,1.5),clamp(fineLuma*2.3,.5,1.55),microFade*.7);
                    // Fine blades are static texture relief, faded by footprint to prevent shimmer.
                    half4 turf=SAMPLE_TEXTURE2D(_GroundCoverTex,sampler_GroundCoverTex,rotated*.75);
                    float turfWeight=_GroundCoverStrength*(1-smoothstep(.18,.48,soil))*microFade*grainFade;
                    grassDetail*=lerp(1,lerp(.65,1.35,turf.r),turfWeight);
                    float soilDetail=1+(meso-.5)*.36*mesoFade+(grain-.5)*.25*grainFade;
                    detailValue=lerp(1,lerp(grassDetail,soilDetail,smoothstep(.15,.75,soil)),_DetailStrength);
                    // Centimeter-scale sod/soil relief changes lighting without moving the shared simulation ground.
                    float relief=(fineLuma-.45)*.022*(1-soil)*microFade;
                    relief+=(turf.a-.4)*.014*turfWeight;
                    relief+=(meso-.5)*.065*mesoFade+(grain-.5)*.012*grainFade;
                    relief+=furrow*ploughed*.032*furrowFade;
                    float3 dpdx=ddx(input.positionWS),dpdy=ddy(input.positionWS);
                    float3 r1=cross(dpdy,normal),r2=cross(normal,dpdx);
                    float determinant=dot(dpdx,r1);
                    float3 gradient=(r1*ddx(relief)+r2*ddy(relief))*sign(determinant)/max(abs(determinant),.000001);
                    normal=normalize(normal-gradient*_ReliefStrength);
                }
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord=input.screenShadow;
                #else
                    float4 shadowCoord=TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light main=GetMainLight(shadowCoord,input.positionWS,half4(1,1,1,1));
                half3 illumination=max(SampleSH(normal),half3(.04,.04,.045))*.8+main.color*main.distanceAttenuation*main.shadowAttenuation*saturate(dot(normal,main.direction));
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX) || USE_CLUSTER_LIGHT_LOOP
                InputData inputData=(InputData)0;
                inputData.positionWS=input.positionWS;
                inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(input.positionCS);
                uint count=GetAdditionalLightsCount(),processed=0u;
                LIGHT_LOOP_BEGIN(count)
                    if(processed++>=4u)break;
                    Light light=GetAdditionalLight(lightIndex,input.positionWS);
                    illumination+=light.color*light.distanceAttenuation*saturate(dot(normal,light.direction));
                LIGHT_LOOP_END
                #endif
                half3 albedo=lerp(input.color.rgb,surface.rgb,_UseSurfaceMap)*detailValue;
                if(_UseSurfaceMap>0)
                {
                    albedo*=lerp(1,lerp(.86,1.14,Noise(p*.065)),_UseSurfaceMap);
                    albedo*=1+_UseSurfaceMap*microFade*((grain-.5)*.12*grainFade+furrow*ploughed*.10*furrowFade);
                }
                if(_SceneryDetail>0)
                {
                    half leaf=input.color.a;
                    half3 bark=SAMPLE_TEXTURE2D(_BarkTex,sampler_BarkTex,input.uv).rgb;
                    half barkLuma=dot(bark,half3(.2126,.7152,.0722));
                    albedo*=lerp(clamp(barkLuma*2.5,.45,1.45),1,leaf);
                    float vein=1-smoothstep(.012,.035,abs(input.uv.x-.5));
                    float leafShape=1-.13*abs(input.uv.x-.5)*2+.05*vein;
                    albedo*=lerp(1,leafShape,leaf);
                    illumination+=main.color*main.shadowAttenuation*saturate(dot(-normal,main.direction))*.32*leaf;
                }
                // Soft back lighting through thin grass blades.
                illumination+=main.color*saturate(dot(-normal,main.direction))*.24*saturate(_WindStrength*8);
                half3 realisticColor=MixFog(albedo*illumination,input.fog);
                if(_BattleAtlasBlend<.001) return half4(realisticColor,1);
                // Printed terrain uses the same roads, field extents and woods as the scenery.
                half3 mapGround=lerp(half3(.78,.76,.59), half3(.71,.68,.49),smoothstep(.25,.95,soil));
                // Dark woodland surface patches remain legible beneath the canopy symbols.
                half woodland=0, woodBoundary=0;
                for(int w=0;w<(int)_BattleAtlasWoodsCount;w++)
                {
                    float4 wood=_BattleAtlasWoods[w];
                    float edge=length(p-wood.xy)/wood.z+(Noise(p*.045)-.5)*.09;
                    float aa=max(footprint/wood.z,.005);
                    woodland=max(woodland,1-smoothstep(.96-aa,.96+aa,edge));
                    woodBoundary=max(woodBoundary,1-smoothstep(aa,aa*2,abs(edge-.96)));
                }
                mapGround=lerp(mapGround,half3(.40,.53,.32),woodland);
                mapGround=lerp(mapGround,half3(.29,.39,.22),woodBoundary*.42);
                // Small engraved pine symbols remain readable as foliage becomes a painted area.
                float treeSpacing=24*_BattleAtlasMapScale;
                float2 treeCell=floor(p/treeSpacing);
                float2 treeUV=frac(p/treeSpacing)-.5;
                treeUV-=float2(RuralHash(treeCell),RuralHash(treeCell+71))*.2-.1;
                float treeAA=max(footprint/treeSpacing,.015);
                float canopy=1-smoothstep(treeAA,treeAA*2,max(abs(treeUV.x)-(.22-treeUV.y*.48),abs(treeUV.y+.02)-.24));
                float trunk=(1-smoothstep(treeAA,treeAA*2,abs(treeUV.x)-.025))*(1-smoothstep(treeAA,treeAA*2,abs(treeUV.y-.26)-.075));
                float treeFade=1-smoothstep(.18,.4,footprint/treeSpacing);
                mapGround=lerp(mapGround,half3(.27,.38,.20),max(canopy,trunk)*woodland*.55*treeFade);
                if(_BattleAtlasFieldCount>0)
                {
                    float2 q=p/_BattleAtlasMapScale;
                    float px=max(footprint/_BattleAtlasMapScale,.5);
                    float roadDistance=AtlasRoadDistance(q);
                    float roadOuter=1-smoothstep(4+px*.8,4+px*1.8,roadDistance);
                    float roadInner=1-smoothstep(2+px*.35,2+px*.9,roadDistance);
                    mapGround=lerp(mapGround,half3(.49,.40,.28),roadOuter*.72);
                    mapGround=lerp(mapGround,half3(.88,.83,.65),roadInner);
                    for(int f=0;f<(int)_BattleAtlasFieldCount;f++)
                    {
                        float4 field=_BattleAtlasFields[f];
                        float2 local=q-field.xy;
                        float2 edges=abs(abs(local)-field.zw);
                        bool horizontal=edges.y<edges.x;
                        float along=horizontal?local.x:local.y;
                        float span=horizontal?field.z:field.w;
                        float lineDistance=horizontal?edges.y:edges.x;
                        float boundary=(1-smoothstep(px*.55,px*1.3,lineDistance))*step(abs(along),span);
                        boundary*=step(10,abs(along))*step(10,roadDistance);
                        // Farm fences have short posts; hedgerows print as dotted green boundaries.
                        float dash=1-smoothstep(.42,.6,frac((along+span)/18));
                        half3 edgeInk=f<2?half3(.43,.34,.24):half3(.35,.43,.25);
                        mapGround=lerp(mapGround,edgeInk,boundary*lerp(.45,1,dash)*.72);
                        float inside=step(abs(local.x),field.z-6)*step(abs(local.y),field.w-6);
                        float fieldHatch=1-smoothstep(px*.3,px*.7,abs(frac((q.x+q.y*.25)/32)-.5)*32);
                        mapGround*=1-fieldHatch*inside*.035;
                    }
                }
                half3 mapScenery=lerp(half3(.52,.47,.37),half3(.42,.56,.37),step(.1,input.color.g-input.color.r));
                if(_SceneryDetail>0) mapScenery=lerp(half3(.48,.41,.30),half3(.40,.53,.32),input.color.a);
                half3 mapColor=lerp(mapScenery,mapGround,_UseSurfaceMap);
                mapColor*=.98+.02*Noise(p*.012);
                // Subtle engraved contours give the flat palette a sense of relief.
                float contourPhase=input.positionWS.y/10;
                float contourDistance=abs(frac(contourPhase+.5)-.5);
                float contourWidth=max(fwidth(contourPhase)*.7,.008);
                half contour=1-smoothstep(contourWidth,contourWidth*2,contourDistance);
                mapColor*=1-contour*.10*_UseSurfaceMap;
                // Broken fall-line strokes suggest engraved hill shading rather than 3D shadows.
                float2 slopeDirection=normalize(input.normalWS.xz+float2(.00001,0));
                float2 tangent=float2(-slopeDirection.y,slopeDirection.x);
                float slope=length(input.normalWS.xz);
                float hatchPeriod=18*_BattleAtlasMapScale;
                float hatchDistance=abs(frac(dot(p,tangent)/hatchPeriod)-.5)*hatchPeriod;
                float hatch=1-smoothstep(max(.5,footprint*.3),max(1,footprint*.8),hatchDistance);
                hatch*=1-smoothstep(.25,.6,frac(contourPhase));
                hatch*=smoothstep(.035,.12,slope)*(1-woodland*.7);
                mapColor*=1-hatch*.13*_UseSurfaceMap;
                mapColor*=.99+.01*Noise(p*2.7);
                return half4(lerp(MixFog(albedo*illumination,input.fog),mapColor,_BattleAtlasBlend),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RuralShadowVertex
            #pragma fragment RuralShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            struct ShadowInput { half4 color:COLOR; float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 root:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 RuralShadowVertex(ShadowInput input):SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 position=TransformObjectToWorld(GrassPosition(input.positionOS.xyz,input.root,input.color.a));
                float3 normal=TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 direction=normalize(_LightPosition-position);
                #else
                    float3 direction=_LightDirection;
                #endif
                float4 clipPosition=TransformWorldToHClip(ApplyShadowBias(position,normal,direction));
                #if UNITY_REVERSED_Z
                    clipPosition.z=min(clipPosition.z,UNITY_NEAR_CLIP_VALUE*clipPosition.w);
                #else
                    clipPosition.z=max(clipPosition.z,UNITY_NEAR_CLIP_VALUE*clipPosition.w);
                #endif
                return clipPosition;
            }
            half4 RuralShadowFragment():SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct DepthInput { half4 color:COLOR; float4 positionOS:POSITION; float2 root:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DepthOutput { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; };
            DepthOutput DepthVert(DepthInput input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                DepthOutput output;output.positionWS=TransformObjectToWorld(GrassPosition(input.positionOS.xyz,input.root,input.color.a));
                output.positionCS=TransformWorldToHClip(output.positionWS);return output;
            }
            half4 DepthFrag(DepthOutput input):SV_Target { RuralFade(input.positionWS,input.positionCS.xy);return 0; }
            ENDHLSL
        }
    }
}
