Shader "ARVisualizer/Screen Edit Aura"
{
    Properties
    {
        _Color ("Aura colour", Color) = (0.15, 0.55, 1, 0.9)
        _Shape ("Width, height, corner radius, glow width (metres)", Vector) = (0.16, 0.09, 0, 0.009)
        _SurfaceDepthTolerance ("Mounting surface depth tolerance (metres)", Range(0, 0.05)) = 0.02
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MountedScreenDepth.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _Shape;
                float _SurfaceDepthTolerance;
            CBUFFER_END
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
                // Distances use the same physical units on both axes, even for very tall/wide media.
                float2 halfSize = _Shape.xy * 0.5;
                float radius = clamp(_Shape.z, 0, min(halfSize.x, halfSize.y));
                float glow = max(_Shape.w, 0.00001);
                float2 facePosition = (i.uv - 0.5) * (_Shape.xy + 2 * glow);
                float2 corner = abs(facePosition) - (halfSize - radius);
                float outside = length(max(corner, 0)) + min(max(corner.x, corner.y), 0) - radius;
                float aa = max(fwidth(outside), 0.000001);
                clip(outside + aa);
                float fade = saturate(1 - max(outside, 0) / glow);
                float alpha = smoothstep(-aa, aa, outside) * fade * fade * _Color.a;
                return half4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
