Shader "ARVisualizer/Cube"
{
    Properties { _BaseColor ("Color", Color) = (0.18, 0.88, 0.72, 1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            // Compare against real-world depth written by the AR camera background.
            ZTest LEqual
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float2 uv : TEXCOORD1; };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half shade = 0.5h + 0.5h * saturate(dot(normalize(input.normalWS), normalize(float3(-0.4, 0.8, -0.3))));
                float2 edge = min(input.uv, 1 - input.uv);
                half rim = 1 - smoothstep(0.012, 0.025, min(edge.x, edge.y));
                return half4(lerp(_BaseColor.rgb * shade, half3(0.68, 1, 0.9), rim * 0.7), 1);
            }
            ENDHLSL
        }
    }
}
