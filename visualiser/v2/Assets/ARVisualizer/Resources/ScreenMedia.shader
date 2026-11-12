Shader "ARVisualizer/Screen Media"
{
    Properties { _MainTex ("Photo or video", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite On ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct Input { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Output Vert(Input v) { Output o; o.position = TransformObjectToHClip(v.position.xyz); o.uv = v.uv; return o; }
            half4 Frag(Output i) : SV_Target
            {
                return half4(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb, 1);
            }
            ENDHLSL
        }
    }
}
