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
Shader "Vuplex/Android Web Shader" {
    Properties {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        [Toggle(FLIP_X)] _FlipX ("Flip X", Float) = 0
        [Toggle(FLIP_Y)] _FlipY ("Flip Y", Float) = 0

        [Header(Properties set programmatically)]
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

            GLSLPROGRAM
                #pragma multi_compile ___ FLIP_X
                #pragma multi_compile ___ FLIP_Y
                #if defined(STEREO_MULTIVIEW_ON) || defined(STEREO_INSTANCING_ON)
                    layout(std140) uniform UnityStereoGlobals {
                        mat4 unity_StereoMatrixP[2];
                        mat4 unity_StereoMatrixV[2];
                        mat4 unity_StereoMatrixInvV[2];
                        mat4 unity_StereoMatrixVP[2];
                        mat4 unity_StereoCameraProjection[2];
                        mat4 unity_StereoCameraInvProjection[2];
                        mat4 unity_StereoWorldToCamera[2];
                        mat4 unity_StereoCameraToWorld[2];
                        vec3 unity_StereoWorldSpaceCameraPos[2];
                        vec4 unity_StereoScaleOffset[2];
                    };
                #endif

                #ifdef VERTEX
                    #version 300 es
                    #ifdef STEREO_MULTIVIEW_ON
                        // Requiring GL_OVR_multiview2 is supposed to implicitly enable GL_OVR_multiview, but there are
                        // some old Android devices (e.g. Galaxy Tab A7 Lite (SM-T220)) where shader compilation fails unless
                        // GL_OVR_multiview is explicitly required.
                        #extension GL_OVR_multiview : require
                        #extension GL_OVR_multiview2 : require
                    #endif
                        #extension GL_OES_EGL_image_external : require
                        #extension GL_OES_EGL_image_external_essl3 : enable

                        uniform mat4 video_matrix;
                        uniform vec4 _MainTex_ST;
                    #ifdef STEREO_MULTIVIEW_ON
                        layout(num_views = 2) in;
                    #endif

                    uniform int unity_StereoEyeIndex;
                    in highp vec2 in_TEXCOORD0;
                    out highp vec2 uv;
                    // Pass the vertex color to the fragment shader
                    // so that it can be used for calculating alpha.
                    // This is needed, for example, to allow CanvasGroup.alpha
                    // to control the alpha.
                    varying vec4 vertexColor;

                    int setupStereoEyeIndex() {
                        int eyeIndex = unity_StereoEyeIndex;
                        #if defined(STEREO_MULTIVIEW_ON)
                            eyeIndex = int(gl_ViewID_OVR);
                        #elif defined(STEREO_INSTANCING_ON)
                            eyeIndex = int(gl_InstanceID & 1);
                            gl_Layer = eyeIndex;
                        #endif
                        return eyeIndex;
                    }

                    mat4 getStereoMatrixVP(int eyeIndex) {
                        mat4 stereoVP = unity_MatrixVP;
                        #if defined(STEREO_MULTIVIEW_ON) || defined(STEREO_INSTANCING_ON)
                            stereoVP = unity_StereoMatrixVP[eyeIndex];
                        #endif
                        return stereoVP;
                    }

                    void main() {

                        int eye = setupStereoEyeIndex();
                        gl_Position = getStereoMatrixVP(eye) * unity_ObjectToWorld * gl_Vertex;
                        vec2 untransformedUV = in_TEXCOORD0;
                        vertexColor = gl_Color;
                        #ifdef FLIP_X
                            untransformedUV.x = 1.0 - untransformedUV.x;
                        #endif
                        #ifdef FLIP_Y
                            untransformedUV.y = 1.0 - untransformedUV.y;
                        #endif
                        uv.xy = untransformedUV.xy * _MainTex_ST.xy + _MainTex_ST.zw;
                    }
                #endif

                #ifdef FRAGMENT
                    // A port of GammaToLinearSpace from UnityCG.cginc
                    vec3 GammaToLinearSpace (vec3 sRGB) {
                        return sRGB * (sRGB * (sRGB * 0.305306011 + 0.682171111) + 0.012522878);
                    }

                    uniform samplerExternalOES _MainTex;
                    uniform float _RenderBlackAsTransparent;
                    varying vec4 vertexColor;
                    varying vec2 uv;

                    void main() {

                        vec4 color = texture2D(_MainTex, uv);
                        // See the comments in DefaultWebShader.shader about pre-multiplied alpha.
                        if (color.w != 0.0) {
                            color = vec4(color.xyz / color.w, color.w);
                        }
                        // Use a threshold of 0.15 to consider a pixel as black.
                        if (_RenderBlackAsTransparent != 0.0 && all(lessThan(color.xyz, vec3(0.15, 0.15, 0.15)))) {
                            color = vec4(0.0, 0.0, 0.0, 0.0);
                        }
                        // Place color correction last so it doesn't affect _RenderBlackAsTransparent functionality.
                        #ifndef UNITY_COLORSPACE_GAMMA
                            color = vec4(GammaToLinearSpace(color.xyz), color.w);
                        #endif
                        // Multiply the alpha by the vertex color's alpha to support CanvasGroup.alpha.
                        gl_FragColor = vec4(color.xyz, color.w * vertexColor.w);
                    }
                #endif
            ENDGLSL
        }
    }
    Fallback "Unlit/Texture"
}
