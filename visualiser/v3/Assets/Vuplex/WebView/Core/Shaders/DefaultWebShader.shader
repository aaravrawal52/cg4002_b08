// Copyright (c) 2026 Vuplex Inc. All rights reserved.
//
// Licensed under the Vuplex Commercial Software Library License, you may
// not use this file except in compliance with the License. You may obtain
// a copy of the License at
//
//     https://vuplex.com/commercial-library-license
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
Shader "Vuplex/Default Web Shader" {
    Properties {
        // Properties that get treated as multi-compile constants for performance.
        [Toggle(FLIP_X)] _FlipX ("Flip X", Float) = 0
        [Toggle(FLIP_Y)] _FlipY ("Flip Y", Float) = 0
        [Toggle(FALLBACK_VIDEO_ENABLED)] _FallbackVideoEnabled ("Fallback Video Enabled", Float) = 0

        [Header(Properties set programmatically)]
        _FallbackVideoRect ("Video Cutout Rect", Vector) = (0, 0, 0, 0)
        _FallbackVideoTexture ("Fallback Video Texture", 2D) = "white" {}
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _RenderBlackAsTransparent ("Render Black as Transparent", Float) = 0

        // Include these UI properties from UI-Default.shader
        // in order to support UI Scroll Views.
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader {
        Pass {
            Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }

            // Include these UI properties from UI-Default.shader
            // in order to support UI Scroll Views.
            Stencil {
                Ref [_Stencil]
                Comp [_StencilComp]
                Pass [_StencilOp]
                ReadMask [_StencilReadMask]
                WriteMask [_StencilWriteMask]
            }

            Lighting Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask [_ColorMask]

            CGPROGRAM
                #pragma multi_compile ___ FALLBACK_VIDEO_ENABLED
                #pragma multi_compile ___ FLIP_X
                #pragma multi_compile ___ FLIP_Y
                #pragma vertex vert
                #pragma fragment frag
                #include "UnityCG.cginc"

                struct appdata {
                    float4 vertex : POSITION;
                    float2 uv : TEXCOORD0;
                    // Pass the vertex color to the fragment shader
                    // so that it can be used for calculating alpha.
                    // This is needed, for example, to allow CanvasGroup.alpha
                    // to control the alpha.
                    float4 vertexColor : COLOR;
                    // For Single Pass Instanced stereo rendering
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                };

                struct v2f {
                    float2 uv : TEXCOORD0;
                    float4 vertex : SV_POSITION;
                    float4 vertexColor : COLOR0;
                    UNITY_VERTEX_OUTPUT_STEREO
                };

                Texture2D _MainTex;
                // Specify linear filtering by using a SamplerState
                // and specifying "linear" in its name.
                // https://docs.unity3d.com/Manual/SL-SamplerStates.html
                SamplerState linear_clamp_sampler;

                float4 _MainTex_ST;

                v2f vert(appdata v) {

                    v2f o;
                    // For Single Pass Instanced stereo rendering
                    UNITY_SETUP_INSTANCE_ID(v);
                    UNITY_INITIALIZE_OUTPUT(v2f, o);
                    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                    o.vertex = UnityObjectToClipPos(v.vertex);
                    o.vertexColor =  v.vertexColor;
                    float2 untransformedUV = v.uv;
                    #ifdef FLIP_X
                        untransformedUV.x = 1.0 - untransformedUV.x;
                    #endif
                    #ifdef FLIP_Y
                        untransformedUV.y = 1.0 - untransformedUV.y;
                    #endif
                    o.uv = TRANSFORM_TEX(untransformedUV, _MainTex);
                    return o;
                }

                float4 _FallbackVideoRect;
                Texture2D _FallbackVideoTexture;
                float _RenderBlackAsTransparent;

                bool _isBlack(fixed4 color) {

                    // Use a threshold of 0.15 to consider a pixel as black.
                    return all(color.xyz < float3(0.15, 0.15, 0.15));
                }

                bool _pointIsInRect(float2 pnt, float4 rect) {

                    #ifdef FLIP_X
                        float nonflippedX = 1.0 - pnt.x;
                    #else
                        float nonflippedX = pnt.x;
                    #endif
                    #ifdef FLIP_Y
                        float nonflippedY = pnt.y;
                    #else
                        float nonflippedY = 1.0 - pnt.y;
                    #endif
                    float width = rect.z;
                    float height = rect.w;
                    bool pointIsInRect = width != 0.0 &&
                                         height != 0.0 &&
                                         nonflippedX >= rect.x &&
                                         nonflippedX <= rect.x + width &&
                                         nonflippedY >= rect.y &&
                                         nonflippedY <= rect.y + height;
                    return pointIsInRect;
                }

                fixed4 frag(v2f i) : SV_Target {

                    // Sample the main view texture.
                    fixed4 color = _MainTex.Sample(linear_clamp_sampler, i.uv);
                    // On all platforms, alpha is pre-multiplied in the texture data's colors. To render the colors accurately in Unity,
                    // we need to get rid of the pre-multiplied alpha by dividing the colors by the alpha.
                    // Otherwise, when displaying the following HTML test page, the semi-transparent
                    // section would appear darker than it should:
                    // <html>
                    //   <head><meta name="transparent" content="true"></head>
                    //   <body><style>html {background: linear-gradient(90deg, white 34%, transparent 100%);}</style></body>
                    // </html>
                    if (color.w != 0.0) {
                        color = float4(color.xyz / color.w, color.w);
                    }

                    #ifdef FALLBACK_VIDEO_ENABLED
                        if (_pointIsInRect(i.uv, _FallbackVideoRect)) {
                            // In order to allow a web page to display content on top of a video, only
                            // render video on black pixels.
                            if (_isBlack(color)) {
                                // Sample the fallback video texture.
                                // Convert from normalized coordinates within the view to normalized coordinates within the fallback video texture.
                                float2 fallbackVideoTextureCoordinates = (float2(i.uv.x, 1.0 - i.uv.y) - _FallbackVideoRect.xy) / _FallbackVideoRect.zw;
                                fixed4 videoColor = _FallbackVideoTexture.Sample(linear_clamp_sampler, fallbackVideoTextureCoordinates);
                                // Don't render the video if the video texture is transparent.
                                if (videoColor[3] != 0) {
                                    color = videoColor;
                                }
                            }
                        }
                    #endif

                    if (_RenderBlackAsTransparent && _isBlack(color)) {
                        color = float4(0.0, 0.0, 0.0, 0.0);
                    }

                    // Color correction to convert gamma to linear color space.
                    // This is performed last so that it doesn't affect fallback video.
                    // Skip for visionOS, where color correction is implemented by using a different
                    // native texture color format because Unity only supports linear space for visionOS
                    // and custom shaders like this aren't supported for RealityKit.
                    #if !defined(UNITY_COLORSPACE_GAMMA) && !defined(UNITY_PLATFORM_VISIONOS)
                        color = float4(GammaToLinearSpace(color.xyz), color.w);
                    #endif

                    // Multiply the alpha by the vertex color's alpha to support CanvasGroup.alpha.
                    color = float4(color.xyz, color.w * i.vertexColor.w);
                    return color;
                }
            ENDCG
        }
    }
    Fallback "Unlit/Texture"
}
