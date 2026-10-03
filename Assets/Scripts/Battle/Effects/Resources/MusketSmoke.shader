Shader "EmpireBattle/MusketSmoke"
{
    Properties
    {
        _SmokeColor ("Smoke tint and opacity", Color) = (.82,.825,.79,.5)
        _PuffData ("Seed, age fraction, haze, age seconds", Vector) = (0,0,0,0)
        _SmokeTime ("Simulation time", Float) = 0
        _SoftParticles ("Use camera depth", Float) = 0
        _FireSmokeTex ("Fire smoke flipbook 8 x 8",2D)="white"{}
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
    float _SmokeTime, _SoftParticles;
    float4 _SunDirection, _SunColor;
    sampler2D _FireSmokeTex;
    float2 SmokeUV(float2 uv,float frame)
    {return (float2(fmod(frame,8),7-floor(frame/8))+lerp(.004,.996,uv))/8;}
    UNITY_INSTANCING_BUFFER_START(SmokeInstances)
        UNITY_DEFINE_INSTANCED_PROP(float4, _SmokeColor)
        UNITY_DEFINE_INSTANCED_PROP(float4, _PuffData)
    UNITY_INSTANCING_BUFFER_END(SmokeInstances)
    struct Attributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
    struct Varyings
    {
        float4 position : SV_POSITION;
        float2 uv : TEXCOORD0;
        float4 screen : TEXCOORD1;
        float eyeDepth : TEXCOORD2;
        UNITY_FOG_COORDS(3)
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };
    Varyings vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.position = UnityObjectToClipPos(input.vertex);
        output.uv = input.uv;
        output.screen = ComputeScreenPos(output.position);
        output.eyeDepth = -UnityObjectToViewPos(input.vertex).z;
        UNITY_TRANSFER_FOG(output, output.position);
        return output;
    }
    float hash21(float2 p)
    {
        p = frac(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return frac(p.x * p.y);
    }
    float noise(float2 p)
    {
        float2 i = floor(p), f = frac(p);
        f = f*f*(3.0-2.0*f);
        return lerp(lerp(hash21(i),hash21(i+float2(1,0)),f.x),
                    lerp(hash21(i+float2(0,1)),hash21(i+1),f.x),f.y);
    }
    float fbm(float2 p)
    {
        float sum=0, amplitude=.53;
        for(int octave=0; octave<4; octave++)
        {
            sum += amplitude * noise(p);
            p = mul(float2x2(.8,-.6,.6,.8),p)*2.07+float2(13.1,7.3);
            amplitude *= .48;
        }
        return sum;
    }
    half4 frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        float4 color = UNITY_ACCESS_INSTANCED_PROP(SmokeInstances, _SmokeColor);
        float4 puff = UNITY_ACCESS_INSTANCED_PROP(SmokeInstances, _PuffData);
        if(puff.z>2.5 && puff.z<3.5)
        {
            float frame=min(62.99,puff.y*63),first=floor(frame);
            float2 uv=input.uv;if(frac(puff.x)>.5)uv.x=1-uv.x;
            half4 tex=lerp(tex2D(_FireSmokeTex,SmokeUV(uv,first)),tex2D(_FireSmokeTex,SmokeUV(uv,first+1)),frac(frame));
            float alpha=tex.a*color.a*saturate(input.eyeDepth/.8);
            float3 lit=color.rgb*(.55+tex.r*.9)+_SunColor.rgb*.055*tex.r;
            if(_SoftParticles>.5)
            {
                float depth=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture,UNITY_PROJ_COORD(input.screen)));
                if(depth>_ProjectionParams.y*1.01)alpha*=saturate((depth-input.eyeDepth)/.6);
            }
            half4 result=half4(lit,alpha);UNITY_APPLY_FOG(input.fogCoord,result);return result;
        }
        float2 uv = input.uv*2-1;
        float2 phase = float2(puff.x*.71,puff.x*.37) + float2(-.016,.009)*_SmokeTime;
        float2 warp = float2(fbm(uv*2.1+phase),fbm(uv*2.1+phase+11.7))-.48;
        float curlRate = .3 + .22*saturate(puff.w);
        float2 curled = uv + warp*curlRate;
        float coarse = fbm(curled*3.1+phase);
        float fine = noise(curled*17+phase*1.4);
        // Irregular lobes and filaments soften into a broad thin haze as the powder disperses.
        float body = exp(-dot(curled*float2(1.05,1.12),curled*float2(1.05,1.12))*2.8);
        float edge = 1-smoothstep(.60,1.05,max(abs(uv.x),abs(uv.y)));
        float wisps = smoothstep(.20,.74,coarse + (fine-.5)*.11);
        float density = body*edge*wisps;
        // Dense, softly rolling ground fog; retain a body between the fine wisps.
        if(puff.z>3.5)density=body*edge*(.42+.58*wisps);
        if(puff.z > 1.5 && puff.z<2.5)
            density *= lerp(saturate((uv.x+1.2)*1.4),1,saturate(puff.w*4));
        float alpha = saturate(density*color.a*1.9);
        if (_SoftParticles > .5)
        {
            float rawDepth = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture,UNITY_PROJ_COORD(input.screen));
            float sceneDepth = LinearEyeDepth(rawDepth);
            // An unavailable/empty depth texture should not erase every puff.
            if (sceneDepth > _ProjectionParams.y*1.01)
                alpha *= saturate((sceneDepth-input.eyeDepth)/1.8);
        }
        alpha *= saturate(input.eyeDepth/.8);
        float shading = .80 + coarse*.26;
        float3 ambient = max(ShadeSH9(float4(0,1,0,1)),float3(.06,.06,.07));
        float3 viewForward = normalize(UNITY_MATRIX_V[2].xyz);
        float backlight = pow(saturate(dot(-viewForward,normalize(_SunDirection.xyz))),3);
        float rim = pow(saturate(1-body),2)*edge;
        float3 scatter = _SunColor.rgb*(.09 + .30*backlight + .18*rim);
        half4 result = half4(color.rgb*(shading*saturate(ambient*1.25+.20)+scatter),alpha);
        UNITY_APPLY_FOG(input.fogCoord,result);
        return result;
    }
    ENDCG
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma instancing_options forcemaxcount:256
            ENDCG
        }
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma instancing_options forcemaxcount:256
            ENDCG
        }
    }
    FallBack Off
}
