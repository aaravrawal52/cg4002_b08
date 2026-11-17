// AR Foundation supplies the standard ARKit texture/property names and keyword state.
// Camera UV mapping and YCbCr conversion follow the installed ARKit provider contract.
Shader "ARVisualizer/ARKit Tight Occlusion"
{
    Properties
    {
        _textureY ("Camera luma", 2D) = "white" {}
        _textureCbCr ("Camera chroma", 2D) = "black" {}
        _EnvironmentDepth ("Environment depth", 2D) = "black" {}
        _HumanDepth ("Human depth", 2D) = "black" {}
        _HumanStencil ("Human stencil", 2D) = "black" {}
        _EdgeInsetPixels ("Edge inset in depth pixels", Range(0, 3)) = 1
        _DepthBiasMetres ("Depth tolerance in metres", Range(0, 0.03)) = 0.005
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Background" "ForceNoShadowCasting"="True" }
        Pass
        {
            Cull Off ZTest Always ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local __ ARKIT_BACKGROUND_URP
            #pragma multi_compile_local __ ARKIT_HUMAN_SEGMENTATION_ENABLED ARKIT_ENVIRONMENT_DEPTH_ENABLED
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_textureY); TEXTURE2D(_textureCbCr);
            TEXTURE2D(_EnvironmentDepth); TEXTURE2D(_HumanDepth); TEXTURE2D(_HumanStencil);
            float4 _EnvironmentDepth_TexelSize, _HumanDepth_TexelSize;
            float4x4 _UnityDisplayTransform;
            float _UnityCameraForwardScale, _EdgeInsetPixels, _DepthBiasMetres;

            struct Input { float3 position : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            struct Output { half4 colour : SV_Target; float depth : SV_Depth; };
            Varyings Vert(Input input)
            {
                Varyings output;
                output.position = TransformObjectToHClip(input.position);
                output.uv = mul(float4(input.uv, 1, 1), _UnityDisplayTransform).xy;
                return output;
            }

            float ReadDepth(float2 uv)
            {
                #if defined(ARKIT_ENVIRONMENT_DEPTH_ENABLED)
                return SAMPLE_TEXTURE2D_LOD(_EnvironmentDepth, sampler_PointClamp, uv, 0).r;
                #elif defined(ARKIT_HUMAN_SEGMENTATION_ENABLED)
                float stencil = SAMPLE_TEXTURE2D_LOD(_HumanStencil, sampler_PointClamp, uv, 0).r;
                return stencil > 0.5 ? SAMPLE_TEXTURE2D_LOD(_HumanDepth, sampler_PointClamp, uv, 0).r : 0;
                #else
                return 0;
                #endif
            }

            float OccluderDistance(float2 uv)
            {
                float centre = ReadDepth(uv);
                if (centre <= 0) return 0; // Never fill missing depth with a nearby obstacle.
                float farthest = centre;
                #if defined(ARKIT_ENVIRONMENT_DEPTH_ENABLED)
                float2 pixel = abs(_EnvironmentDepth_TexelSize.xy) * _EdgeInsetPixels;
                #else
                float2 pixel = abs(_HumanDepth_TexelSize.xy) * _EdgeInsetPixels;
                #endif
                if (_EdgeInsetPixels > 0)
                {
                    // Erode only depth discontinuities, not smooth surfaces. Valid farther
                    // neighbours pull back the foreground silhouette by the chosen radius.
                    [unroll] for (int y = -1; y <= 1; y++)
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        if (x == 0 && y == 0) continue;
                        float neighbour = ReadDepth(uv + float2(x, y) * pixel);
                        #if defined(ARKIT_HUMAN_SEGMENTATION_ENABLED)
                        if (neighbour <= 0) return 0;
                        #endif
                        farthest = max(farthest, neighbour);
                    }
                }
                float distance = farthest - centre > max(0.03, centre * 0.03) ? farthest : centre;
                return distance + _DepthBiasMetres;
            }

            Output Frag(Varyings input)
            {
                half y = SAMPLE_TEXTURE2D(_textureY, sampler_LinearClamp, input.uv).r;
                half2 chroma = SAMPLE_TEXTURE2D(_textureCbCr, sampler_LinearClamp, input.uv).rg - 0.5h;
                half3 rgb = half3(y + 1.402h * chroma.y, y - 0.3441h * chroma.x - 0.7141h * chroma.y, y + 1.772h * chroma.x);
                #if !UNITY_COLORSPACE_GAMMA
                rgb = FastSRGBToLinear(rgb);
                #endif
                Output output;
                output.colour = half4(rgb, 1);
                #if UNITY_REVERSED_Z
                output.depth = 0;
                #else
                output.depth = 1;
                #endif
                float distance = OccluderDistance(input.uv);
                if (distance > 0)
                {
                    distance *= _UnityCameraForwardScale > 0 ? _UnityCameraForwardScale : 1;
                    distance = max(distance, _ProjectionParams.y);
                    output.depth = saturate((rcp(distance) - _ZBufferParams.w) / _ZBufferParams.z);
                }
                return output;
            }
            ENDHLSL
        }
    }
}
