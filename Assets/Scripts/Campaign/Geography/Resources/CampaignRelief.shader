Shader "EmpireCampaign/Relief"
{
    Properties { _Weather("Cloud rain snow fog",Vector)=(.24,0,0,0) _MapTime("Map time",Float)=0 _Waves("Wave visibility",Float)=1 _CloudShadows("Cloud shadows",Float)=1 _WorldBiomes ("Global forest snow coast aridity", 2D) = "black" {} _DetailBiomes ("Regional forest snow coast aridity", 2D) = "black" {} _WorldDryLand ("World land coverage", 2D) = "white" {} _DetailDryLand ("Detailed land coverage", 2D) = "white" {} _RegionIds ("Region IDs", 2D) = "black" {} _RegionOwners ("Region faction colors", 2D) = "white" {} _PoliticalOpacity ("Territory tint", Float) = 0 _SelectedRegion ("Selected region", Float) = 0 _MainTex ("Measured relief", 2D) = "white" {} _DetailTex ("Regional relief", 2D) = "white" {} _DetailBounds ("Detail UV bounds",Vector)=(0,0,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CampaignWeather.hlsl"
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD1; float2 uv:TEXCOORD0; float3 pos:TEXCOORD2; };
            TEXTURE2D(_WorldBiomes); SAMPLER(sampler_WorldBiomes);
            TEXTURE2D(_DetailBiomes); SAMPLER(sampler_DetailBiomes);
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_DetailTex); SAMPLER(sampler_DetailTex);
            TEXTURE2D(_RegionIds); SAMPLER(sampler_RegionIds);
            TEXTURE2D(_RegionOwners); SAMPLER(sampler_RegionOwners);
            TEXTURE2D(_WorldDryLand); SAMPLER(sampler_WorldDryLand);
            TEXTURE2D(_DetailDryLand); SAMPLER(sampler_DetailDryLand);
            CBUFFER_START(UnityPerMaterial)
            float4 _DetailBounds, _RegionIds_TexelSize;
            float _PoliticalOpacity, _SelectedRegion, _MapTime, _Waves, _CloudShadows;
            float4 _Weather;
            CBUFFER_END
            V vert(A a) { V o; o.positionCS=TransformObjectToHClip(a.positionOS.xyz); o.normalWS=TransformObjectToWorldNormal(a.normalOS); o.uv=a.uv; o.pos=a.positionOS.xyz; return o; }
            // Fetch the color through discrete IDs first; interpolating the ID values
            // themselves would produce unrelated factions along political boundaries.
            void Politics(float2 uv, out half3 color, out float coverage, out float selected)
            {
                float id=floor(SAMPLE_TEXTURE2D(_RegionIds,sampler_RegionIds,uv).r*255+.5);
                coverage=step(.5,id);
                color=SAMPLE_TEXTURE2D(_RegionOwners,sampler_RegionOwners,float2((id+.5)/256,.5)).rgb*coverage;
                selected=(abs(id-_SelectedRegion)<.1?1:0)*coverage;
            }
            half4 frag(V i):SV_Target
            {
                half3 c=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).rgb;
                half4 biome=SAMPLE_TEXTURE2D(_WorldBiomes,sampler_WorldBiomes,i.uv);
                float dry=SAMPLE_TEXTURE2D(_WorldDryLand,sampler_WorldDryLand,i.uv).r;
                if(i.uv.x>=_DetailBounds.x && i.uv.x<=_DetailBounds.z && i.uv.y>=_DetailBounds.y && i.uv.y<=_DetailBounds.w)
                {
                    float2 duv=(i.uv-_DetailBounds.xy)/(_DetailBounds.zw-_DetailBounds.xy);
                    float edge=min(min(duv.x,1-duv.x),min(duv.y,1-duv.y));
                    float blend=smoothstep(0,.025,edge);
                    biome=lerp(biome,SAMPLE_TEXTURE2D(_DetailBiomes,sampler_DetailBiomes,duv),blend);
                    c=lerp(c,SAMPLE_TEXTURE2D(_DetailTex,sampler_DetailTex,duv).rgb,blend);
                    dry=lerp(dry,SAMPLE_TEXTURE2D(_DetailDryLand,sampler_DetailDryLand,duv).r,blend);
                }
                // Smooth colors/coverage at vector raster edges, while preserving categorical IDs.
                float2 grid=i.uv*_RegionIds_TexelSize.zw-.5;
                float2 baseUV=(floor(grid)+.5)*_RegionIds_TexelSize.xy;
                float2 f=frac(grid);half3 a,b,c0,d;float ma,mb,mc,md,sa,sb,sc,sd;
                Politics(baseUV,a,ma,sa);
                Politics(baseUV+float2(_RegionIds_TexelSize.x,0),b,mb,sb);
                Politics(baseUV+float2(0,_RegionIds_TexelSize.y),c0,mc,sc);
                Politics(baseUV+_RegionIds_TexelSize.xy,d,md,sd);
                float coverage=lerp(lerp(ma,mb,f.x),lerp(mc,md,f.x),f.y);
                half3 faction=lerp(lerp(a,b,f.x),lerp(c0,d,f.x),f.y)/max(.001,coverage);
                float selection=lerp(lerp(sa,sb,f.x),lerp(sc,sd,f.x),f.y);
                // Use the exact masks behind the relief palette. A conservative coast cutoff
                // keeps the wash on solid land and excludes seas/lakes even inside ID edge pixels.
                float water=1-smoothstep(.05,.65,dry);
                float coastline=4*dry*(1-dry);
                dry=smoothstep(.65,.95,dry);
                c=lerp(c,half3(.42,.33,.17),biome.a*.30*dry);
                c=lerp(c,half3(.085,.16,.055),biome.r*.48*dry);
                c=lerp(c,half3(.68,.54,.31),biome.b*.8*dry);
                // Terrain features retain their colors beneath the faction wash.
                c=lerp(c,faction,_PoliticalOpacity*coverage*dry*(1-biome.g*.8));
                c=lerp(c,half3(.83,.89,.90),biome.g*dry);
                c=lerp(c,half3(.16,.83,.92),.48*selection*dry);
                // Two directional swells in spherical world coordinates, with moving glints.
                // Water coverage gates every effect, so land and snow cannot shimmer.
                float3 normal=normalize(i.pos);
                float phase=dot(i.pos,float3(.83,.19,.53))*.58-_MapTime*(1.15+_Weather.y*.8);
                float crossPhase=dot(i.pos,float3(-.36,.21,.91))*.91-_MapTime*.81;
                float swell=sin(phase)*.6+sin(crossPhase)*.4;
                float ripple=pow(saturate(sin(phase+sin(crossPhase)*.7)),14);
                float3 view=normalize(_WorldSpaceCameraPos-i.pos);
                float glint=pow(saturate(dot(reflect(-normalize(float3(-.6,1,-.35)),normal),view)),12);
                c+=water*_Waves*(swell*.007+ripple*.021+glint*ripple*.13);
                // Restrained foam and shallow turquoise only at the land-mask edge.
                c=lerp(c,half3(.20,.39,.38),coastline*water*.3);
                c+=coastline*water*_Waves*ripple*.055;
                float cloud=MapCloud(normal,_MapTime,_Weather.x);
                c*=1-cloud*_CloudShadows*lerp(.09,.20,_Weather.y);
                c*=1-_Weather.y*.10*dry;
                c=lerp(c,half3(.43,.49,.50),_Weather.w*.55);
                half light=.77+.28*saturate(dot(normalize(i.normalWS),normalize(float3(-.6,1,-.35))));
                float grain=frac(sin(dot(floor(i.pos.xz*1.8),float2(12.9898,78.233)))*43758.5453);
                return half4(c*light*(.97+grain*.06),1);
            }
            ENDHLSL
        }
    }
}
