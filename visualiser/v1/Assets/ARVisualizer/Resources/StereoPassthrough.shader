Shader "ARVisualizer/Stereo Passthrough"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_SourceDepth);
        TEXTURE2D(_SourceColour);
        float4x4 _SourceProjection, _EyeInverseProjection, _EyeToSource, _EyeGPUProjection;
        float _FallbackDistance;
        struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
        Varyings Vert(uint id : SV_VertexID)
        {
            Varyings o; o.position = GetFullScreenTriangleVertexPosition(id);
            o.uv = GetFullScreenTriangleTexCoord(id); return o;
        }
        // Camera projection matrices use Y-up; texture memory follows the graphics API.
        float2 TextureToClip(float2 uv)
        {
            #if UNITY_UV_STARTS_AT_TOP
            uv.y = 1 - uv.y;
            #endif
            return uv * 2 - 1;
        }
        float2 SourceUV(float3 position)
        {
            float4 clip = mul(_SourceProjection, float4(position, 1));
            float2 uv = clip.xy / clip.w * 0.5 + 0.5;
            #if UNITY_UV_STARTS_AT_TOP
            uv.y = 1 - uv.y;
            #endif
            return uv;
        }
        bool Inside(float2 uv) { return all(uv >= 0) && all(uv <= 1); }
        float SampleDepth(float2 uv) { return Inside(uv) ? SAMPLE_TEXTURE2D_LOD(_SourceDepth, sampler_PointClamp, uv, 0).r : 0; }
        ENDHLSL
        Pass
        {
            Name "CaptureDepthMetres"
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CaptureDepth
            float CaptureDepth(Varyings input) : SV_Target
            {
                float raw = SAMPLE_TEXTURE2D_LOD(_SourceDepth, sampler_PointClamp, input.uv, 0).r;
                #if UNITY_REVERSED_Z
                if (raw <= 0.000001) return 0;
                #else
                if (raw >= 0.999999) return 0;
                #endif
                return LinearEyeDepth(raw, _ZBufferParams);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ReprojectVideoAndDepth"
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Reproject
            struct Output { half4 colour : SV_Target; float depth : SV_Depth; };
            Output Reproject(Varyings input)
            {
                float4 ray4 = mul(_EyeInverseProjection, float4(TextureToClip(input.uv), 1, 1));
                float3 ray = ray4.xyz / max(0.00001, -ray4.z);
                float3 origin = mul(_EyeToSource, float4(0,0,0,1)).xyz;
                float3 direction = mul((float3x3)_EyeToSource, ray);
                float distance = _FallbackDistance;
                float2 uv = 0;
                // Small-baseline inverse reprojection. Missing/discontinuous depth falls back to video only.
                [unroll] for (int step = 0; step < 4; ++step)
                {
                    uv = SourceUV(origin + direction * distance);
                    float z = SampleDepth(uv);
                    if (z > 0) distance = max(0.01, (z + origin.z) / -direction.z);
                }
                float3 sourcePosition = origin + direction * distance;
                uv = SourceUV(sourcePosition);
                float measured = SampleDepth(uv);
                bool valid = measured > 0 && abs(measured + sourcePosition.z) < max(0.025, measured * 0.025);
                if (!valid) uv = SourceUV(origin + direction * _FallbackDistance);
                Output o;
                o.colour = Inside(uv) ? SAMPLE_TEXTURE2D_LOD(_SourceColour, sampler_LinearClamp, uv, 0) : half4(0,0,0,1);
                float4 eyeClip = mul(_EyeGPUProjection, float4(ray * distance, 1));
                #if UNITY_REVERSED_Z
                o.depth = valid ? saturate(eyeClip.z / eyeClip.w) : 0;
                #else
                o.depth = valid ? saturate(eyeClip.z / eyeClip.w) : 1;
                #endif
                return o;
            }
            ENDHLSL
        }
    }
}
