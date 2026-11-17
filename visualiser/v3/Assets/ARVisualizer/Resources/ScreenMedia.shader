Shader "ARVisualizer/Screen Media"
{
    Properties
    {
        _MainTex ("Photo or video", 2D) = "black" {}
        // x: width / height of the face. y: corner radius as a fraction of the shorter side (0 = rectangle).
        _Shape ("Face shape", Vector) = (1.7778, 0, 0, 0)
        _SurfaceDepthTolerance ("Mounting surface depth tolerance (metres)", Range(0, 0.05)) = 0.02
    }
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
            #include "MountedScreenDepth.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Shape;
            float _SurfaceDepthTolerance;
            struct Input { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Output Vert(Input v)
            {
                Output o;
                float3 viewPosition = TransformWorldToView(TransformObjectToWorld(v.position.xyz));
                o.position = MountedScreenClipPosition(TransformObjectToHClip(v.position.xyz), viewPosition, _SurfaceDepthTolerance);
                o.uv = v.uv;
                return o;
            }
            half4 Frag(Output i) : SV_Target
            {
                // App faces can be rounded, capsule-shaped or circular. Distances are in face heights;
                // the small inset keeps this hard edge under the soft edge of the app's own backdrop.
                float2 halfSize = float2(max(_Shape.x, 0.01), 1) * 0.5;
                float radius = saturate(_Shape.y * 2) * min(halfSize.x, halfSize.y);
                float2 corner = abs((i.uv - 0.5) * halfSize * 2) - (halfSize - radius);
                float outside = length(max(corner, 0)) + min(max(corner.x, corner.y), 0) - radius;
                clip(_Shape.y > 0 ? -(outside + 0.004) : 1);
                return half4(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb, 1);
            }
            ENDHLSL
        }
    }
}
