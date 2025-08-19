// Assets/Shaders/TriplanarToonRock.shader
Shader "URP/TriplanarToonRock"
{
    Properties
    {
        _BaseColor      ("Lit Color", Color)         = (0.70, 0.72, 0.66, 1)
        _DarkColor      ("Shadow Color", Color)      = (0.35, 0.36, 0.34, 1)
        _HeightTint     ("Height Tint", Color)       = (0.80, 0.78, 0.72, 1)
        _HeightStrength ("Height Tint Strength", Range(0,1)) = 0.25

        _RampSteps      ("Light Steps", Range(1,6))  = 3
        _RampSharpness  ("Ramp Sharpness", Range(0.5,4)) = 1.5
        _MinLight       ("Ambient Floor", Range(0,1))= 0.15

        _NoiseScale     ("Noise Scale", Float)       = 2.0
        _NoiseAmount    ("Noise Amount", Range(0,1)) = 0.25

        // Render Face dropdown in the material inspector
        [Enum(Both (Off),0, Front,1, Back,2)] _Cull ("Render Face", Float) = 2
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
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON

            // GPU instancing
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _DarkColor, _HeightTint;
                float _HeightStrength, _RampSteps, _RampSharpness, _MinLight;
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
                float3 viewDirWS  : TEXCOORD2;
                float4 shadowCoord: TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ---- tiny procedural noise (no textures) ----
            float hash21(float2 p){ p=frac(p*float2(123.34,345.45)); p+=dot(p,p+34.345); return frac(p.x*p.y); }
            float valueNoise2(float2 p){
                float2 i=floor(p), f=frac(p);
                float a=hash21(i), b=hash21(i+float2(1,0));
                float c=hash21(i+float2(0,1)), d=hash21(i+float2(1,1));
                float2 u=f*f*(3-2*f);
                return lerp(lerp(a,b,u.x), lerp(c,d,u.x), u.y);
            }
            float triNoise(float3 posWS, float3 nWS, float s){
                float3 w = pow(abs(nWS), 4.0); w /= (w.x+w.y+w.z+1e-5);
                float nx=valueNoise2(posWS.yz*s), ny=valueNoise2(posWS.xz*s), nz=valueNoise2(posWS.xy*s);
                return nx*w.x + ny*w.y + nz*w.z;
            }

            Varyings vert(Attributes IN){
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS  = GetWorldSpaceViewDir(OUT.positionWS);
                OUT.shadowCoord= TransformWorldToShadowCoord(OUT.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN): SV_Target
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

                // Simple GI from SH
                color += _BaseColor.rgb * SampleSH(N);

                // Height tint + triplanar breakup
                half heightMask = saturate(IN.positionWS.y * 0.2h);
                color = lerp(color, lerp(color, _HeightTint.rgb, _HeightStrength), heightMask);

                float n = triNoise(IN.positionWS, N, _NoiseScale);
                color = lerp(color, color * (0.8 + 0.4 * n), _NoiseAmount);

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
                float3 nWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, 0));
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

        // ---------- Meta (lightmapping) ----------
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
