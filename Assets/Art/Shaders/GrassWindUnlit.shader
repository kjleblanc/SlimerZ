Shader "URP/Unlit/GrassWind_v3"
{
    Properties
    {
        _BaseMap ("Base (RGBA)", 2D) = "white" {}
        _BaseColor ("Color Tint", Color) = (0.85,1,0.85,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.1

        _WindDir ("Wind Dir (XZ)", Vector) = (1,0,0,0)
        _WindStrength ("Wind Strength", Range(0,1)) = 0.25
        _WindSpeed ("Wind Speed", Range(0,5)) = 1.5
        _Bend ("Top Bends More", Range(0,2)) = 1.0
        _NoiseScale ("Noise Scale", Range(0,5)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite On

        Pass
        {
            Name "ForwardUnlit"
            Tags{ "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Cutoff;
                float4 _BaseMap_ST;
                float4 _WindDir;
                float  _WindStrength, _WindSpeed, _Bend, _NoiseScale;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Per-instance random phase using world position of object
            float hash12(float2 p){ return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posOS = IN.positionOS.xyz;

                // Bend more at the top (assumes your blades go from y=0 base to y=1 tip)
                float height01 = saturate(posOS.y);

                // Unique phase per *instance* so clumps aren’t in sync
                float3 objPosWS = unity_ObjectToWorld._m03_m13_m23;
                float phase = hash12(objPosWS.xz * 17.0);

                float t = _Time.y * _WindSpeed;
                float wiggle = sin(t + phase * 6.28318 + posOS.x * _NoiseScale) * _WindStrength;

                float2 dir = normalize((_WindDir.xz == 0) ? float2(1,0) : _WindDir.xz);
                float bendAmt = wiggle * _Bend * height01;

                posOS.xz += dir * bendAmt;

                float3 posWS = TransformObjectToWorld(posOS);
                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                clip(col.a - _Cutoff);
                return col;
            }
            ENDHLSL
        }
    }
}
