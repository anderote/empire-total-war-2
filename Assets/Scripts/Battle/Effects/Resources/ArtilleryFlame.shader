Shader "EmpireBattle/ArtilleryFlame"
{
    Properties { _FlameData("Time, seed, muzzle, strength", Vector) = (0,0,0,1) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+5" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _FlameData;
            float4 _FlameRoots[28];
            struct A { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float heat:TEXCOORD1; float opacity:TEXCOORD2; float kind:TEXCOORD3; float seed:TEXCOORD4; };
            float Hash(float n) { return frac(sin(n*127.1)*43758.5453); }
            float Noise(float2 p)
            {
                float2 a=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(dot(a,float2(1,57))),Hash(dot(a+float2(1,0),float2(1,57))),f.x),
                    lerp(Hash(dot(a+float2(0,1),float2(1,57))),Hash(dot(a+1,float2(1,57))),f.x),f.y);
            }
            // Each ribbon follows a continuously changing three-dimensional arc.
            float3 Arc(float u,float strand,float time,float seed,float muzzle)
            {
                float h=Hash(strand+seed*17), a=strand*2.399963+seed;
                float body=muzzle>.5 ? step(strand,11.5) : 1;
                float radius=lerp(.12,.65,h)*(1-muzzle*.7)*lerp(1,.78,body);
                float life=frac(time*lerp(.65,1.3,h)+h*7);
                float height=lerp(.55,1.35,Hash(strand*3+seed))*lerp(.6,1,sin(life*3.14159));
                float bend=u*u;
                float3 start=muzzle>.5 ? float3(sin(a)*radius,0,cos(a)*radius) : _FlameRoots[(int)strand].xyz;
                float3 p=start+float3(0,u*height,0);
                float curl=sin(u*4.7-time*3.6+a)*bend;
                float wind=sin(time*1.7+seed)*.22;
                p.x+=bend*(wind+.42*sin(a+time*1.1))+.24*curl;
                p.z+=bend*(.15+.32*cos(a-time*.8))+.18*cos(u*5-time*3+a)*bend;
                p.xz+=float2(sin(a*2+u*8-time*6),cos(a+u*7-time*5))*.025*u;
                return p;
            }
            V Vert(A i)
            {
                V o;
                float strand=i.vertex.z, u=i.vertex.y, time=_FlameData.x, seed=_FlameData.y, muzzle=_FlameData.z;
                float h=Hash(strand+seed*17), phase=frac(time*lerp(.65,1.3,h)+h*7);
                float3 center=TransformObjectToWorld(Arc(u,strand,time,seed,muzzle));
                float3 tangent=normalize(TransformObjectToWorldDir(Arc(min(1,u+.015),strand,time,seed,muzzle)-Arc(max(0,u-.015),strand,time,seed,muzzle)));
                float3 side=normalize(cross(tangent,normalize(_WorldSpaceCameraPos-center))+float3(.0001,0,0));
                float body=muzzle>.5 ? step(strand,11.5) : 1;
                float width=lerp(lerp(.022,.065,Hash(strand*7+seed)),lerp(.24,.46,Hash(strand*7+seed)),body);
                if(muzzle<.5)width=lerp(.18,.36,Hash(strand*7+seed));
                float scale=length(TransformObjectToWorldDir(float3(1,0,0),false));
                width*=scale*pow(max(0,1-u),lerp(1,.65,body))*lerp(.55,1.2,sin(phase*3.14159));
                center+=side*i.vertex.x*width;
                o.vertex=TransformWorldToHClip(center); o.uv=i.uv;
                o.heat=lerp(.35,1,h); o.opacity=sin(phase*3.14159)*saturate((1-u)*5);
                if(muzzle<.5)o.opacity=(.35+.65*sin(phase*3.14159))*saturate((1-u)*5);
                o.kind=body; o.seed=strand*3.13+seed;
                return o;
            }
            half4 Frag(V i):SV_Target
            {
                float time=_FlameData.x, u=i.uv.y;
                float2 flow=float2(i.uv.x*4+i.seed,u*7-time*2.8+i.seed);
                float turbulence=Noise(flow)+.45*Noise(flow*2.1+time*.2);
                float warp=(Noise(float2(u*8-time*3,i.seed))-.5)*.5;
                float across=abs((i.uv.x*2-1)+warp*i.kind);
                float core=exp(-across*across*lerp(22,6,i.kind));
                float glow=pow(saturate(1-across),lerp(1.5,1.8,i.kind));
                float travel=.65+.35*sin(u*20-time*12+i.heat*8);
                float bodyMask=smoothstep(.2,.72,turbulence-u*.45);
                float3 ember=float3(1.1,.08,.008), hot=lerp(float3(2.6,1.05,.12),float3(5.2,3.0,.75),_FlameData.z);
                float heat=core*saturate(1-u*.9)*lerp(.7,1,bodyMask);
                float3 color=lerp(ember,hot,heat);
                float alpha=glow*i.opacity*travel*lerp(.4,.8,i.heat);
                alpha*=lerp(.65,bodyMask*.85,i.kind);
                if(_FlameData.z<.5)alpha=glow*i.opacity*travel*(.35+bodyMask*.65)*.85;
                return half4(color,alpha*_FlameData.w);
            }
            ENDHLSL
        }
    }
}
