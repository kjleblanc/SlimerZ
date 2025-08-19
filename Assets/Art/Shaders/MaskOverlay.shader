Shader "URP/MaskOverlay"
{
  Properties { _MainTex ("Mask", 2D) = "white" {}  _Color ("Tint", Color) = (1,1,1,0.6) }
  SubShader {
    Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
    Pass {
      Blend SrcAlpha OneMinusSrcAlpha
      ZWrite Off
      Cull Off
      HLSLPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
      CBUFFER_START(UnityPerMaterial) float4 _Color; CBUFFER_END
      TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
      struct A { float4 posOS:POSITION; float2 uv:TEXCOORD0; };
      struct V { float4 posCS:SV_POSITION; float2 uv:TEXCOORD0; };
      V vert(A i){ V o; o.posCS=TransformObjectToHClip(i.posOS.xyz); o.uv=i.uv; return o; }
      half4 frag(V i):SV_Target { return SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv) * _Color; }
      ENDHLSL
    }
  }
}
