Shader "URP/GrassWindUnlit"
{
    Properties
    {
        _Color ("Color", Color) = (0.35,0.7,0.35,1)
        _WindDir ("Wind Dir (x,z)", Vector) = (1,0,1,0)
        _WindStrength ("Wind Strength", Range(0,0.4)) = 0.15
        _WindSpeed ("Wind Speed", Range(0,4)) = 1.5
        [Enum(Both (Off),0, Front,1, Back,2)] _Cull ("Render Face", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="Opaque" }
        Pass
        {
            Name "Forward"
            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _WindDir;
                float _WindStrength, _WindSpeed;
            CBUFFER_END
            struct A{ float4 pos:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V{ float4 pos:SV_POSITION; half4 col:COLOR; };
            V vert(A i){
                UNITY_SETUP_INSTANCE_ID(i);
                float3 p = TransformObjectToWorld(i.pos.xyz);
                float2 wd = normalize(max(abs(_WindDir.xz), 1e-4));
                float sway = _WindStrength * (0.3 + 0.7 * sin(_Time.y*_WindSpeed + p.x*0.2 + p.z*0.21));
                // pivot at base: vertices have y in [0..1], bend top more
                float bend = sway * saturate(i.pos.y);
                p.xz += float2(wd.x, wd.y) * bend;
                V o; o.pos = TransformWorldToHClip(p); o.col = _Color; return o;
            }
            half4 frag(V i):SV_Target { return i.col; }
            ENDHLSL
        }
    }
}
