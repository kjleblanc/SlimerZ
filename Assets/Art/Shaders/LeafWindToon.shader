// Assets/Shaders/LeafWindToon.shader
Shader "URP/LeafWindToon"
{
    Properties
    {
        _BaseColor      ("Lit Color", Color)         = (0.40, 0.75, 0.38, 1)
        _DarkColor      ("Shadow Color", Color)      = (0.20, 0.40, 0.20, 1)

        _RampSteps      ("Light Steps", Range(1,6))  = 3
        _RampSharpness  ("Ramp Sharpness", Range(0.5,4)) = 1.5
        _MinLight       ("Ambient Floor", Range(0,1))= 0.18

        _WindDir        ("Wind Dir (x,z)", Vector)   = (1,0,1,0)
        _WindStrength   ("Wind Strength", Range(0,0.5)) = 0.15
        _WindSpeed      ("Wind Speed", Range(0,5))   = 1.3
        _GustStrength   ("Gust Strength", Range(0,0.5)) = 0.12
        _GustFreq       ("Gust Freq", Range(0,10))   = 2.5

        _NoiseScale     ("Shade Noise Scale", Float) = 2.2
        _NoiseAmount    ("Shade Noise Amount", Range(0,1)) = 0.15

        // Render Face dropdown
        [Enum(Both (Off),0, Front,1, Back,2)] _Cull ("Render Face", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        // ---------- Forward ----------
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            // URP lighting variants
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _DarkColor;
                float _RampSteps, _RampSharpness, _MinLight;
                float4 _WindDir;
                float _WindStrength, _WindSpeed, _GustStrength, _GustFreq;
                float _NoiseScale, _NoiseAmount;
            CBUFFER_END

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 shadowCoord: TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // --- tiny 2D value noise (for variation) ---
            float hash21(float2 p){ p=frac(p*float2(127.1,311.7)); p+=dot(p,p+34.5); return frac(p.x*p.y); }
            float valueNoise2(float2 p){
                float2 i=floor(p), f=frac(p);
                float a=hash21(i), b=hash21(i+float2(1,0));
                float c=hash21(i+float2(0,1)), d=hash21(i+float2(1,1));
                float2 u=f*f*(3-2*f);
                return lerp(lerp(a,b,u.x), lerp(c,d,u.x), u.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Object->World
                VertexPositionInputs P = GetVertexPositionInputs(IN.positionOS.xyz);
                float3 posWS = P.positionWS;
                float3 nWS   = TransformObjectToWorldNormal(IN.normalOS);

                // Horizontal wind direction (normalize xz)
                float2 wd = normalize(max(abs(_WindDir.xz), 1e-4));
                float3 windDir = normalize(float3(wd.x, 0, wd.y));

                // Base sway (height helps exaggerate upper leaves a bit)
                float t = _Time.y * _WindSpeed;
                float base = sin(dot(posWS.xz, wd)*0.35 + t) * 0.5 + 0.5;
                float heightMask = saturate((posWS.y) * 0.15); // more sway higher up
                float sway = _WindStrength * (0.6*base + 0.4) * (0.6 + 0.4*heightMask);

                // Gusts with spatial noise
                float g = sin(t * _GustFreq + valueNoise2(posWS.xz*0.5)*6.2831) * 0.5 + 0.5;
                sway += _GustStrength * g;

                posWS += windDir * sway;

                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS   = nWS; // small displacement — keep normal
                OUT.shadowCoord= TransformWorldToShadowCoord(posWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 N = normalize(IN.normalWS);

                // Main light (use shadow-coord overload when available)
                Light mainLight;
                #if defined(_MAIN_LIGHT_SHADOWS)
                    mainLight = GetMainLight(IN.shadowCoord);
                    half shadowAtten = mainLight.shadowAttenuation;
                #else
                    mainLight = GetMainLight();
                    half shadowAtten = 1.0h;
                #endif

                float3 L = normalize(mainLight.direction);
                half ndl = saturate(dot(N, L));

                // Toon ramp
                half lam = pow(ndl * 0.5h + 0.5h, _RampSharpness);
                half stepped = floor(lam * _RampSteps) / _RampSteps;
                half lightTerm = lerp(_MinLight, 1.0h, stepped) * shadowAtten;

                float3 color = lerp(_DarkColor.rgb, _BaseColor.rgb, lightTerm) * mainLight.color.rgb;

                // SH ambient
                color += _BaseColor.rgb * SampleSH(N);

                // Subtle tonal breakup
                float n = valueNoise2(IN.positionWS.xz * _NoiseScale);
                color = lerp(color, color * (0.85 + 0.3 * n), _NoiseAmount);

                return half4(color, 1);
            }
            ENDHLSL
        }

        // ---------- ShadowCaster ----------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes { float4 positionOS: POSITION; float3 normalOS: NORMAL; };
            struct Varyings   { float4 positionCS: SV_POSITION; };

            Varyings vert(Attributes IN){
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(posWS);
                return OUT;
            }
            half4 frag(Varyings IN): SV_Target { return 0; }
            ENDHLSL
        }

        // ---------- DepthOnly ----------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS: POSITION; };
            struct Varyings   { float4 positionCS: SV_POSITION; };
            Varyings vert(Attributes IN){
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(posWS);
                return OUT;
            }
            half4 frag(Varyings IN): SV_Target { return 0; }
            ENDHLSL
        }

        // ---------- Meta ----------
        Pass
        {
            Name "Meta"
            Tags { "LightMode"="Meta" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            struct Attributes { float4 positionOS: POSITION; float2 uv0: TEXCOORD0; float2 uv1: TEXCOORD1; };
            struct Varyings   { float4 positionCS: SV_POSITION; };

            Varyings vert(Attributes v){
                Varyings o;
                o.positionCS = UnityMetaVertexPosition(v.positionOS, v.uv0, v.uv1, unity_LightmapST, unity_DynamicLightmapST);
                return o;
            }

            half4 _BaseColor;
            half4 frag(Varyings i): SV_Target
            {
                MetaInput meta;
                meta.Albedo   = _BaseColor.rgb;
                meta.Emission = 0;
                return UnityMetaFragment(meta);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
