Shader "ARVisualizer/Screen"
{
    Properties
    {
        _BaseColor ("Screen color", Color) = (0, 0, 0, 1)
        _SurfaceDepthTolerance ("Mounting surface depth tolerance (metres)", Range(0, 0.05)) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZTest LEqual
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MountedScreenDepth.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _SurfaceDepthTolerance;
            CBUFFER_END
            float4 Vert(float4 positionOS : POSITION) : SV_POSITION
            {
                float3 viewPosition = TransformWorldToView(TransformObjectToWorld(positionOS.xyz));
                return MountedScreenClipPosition(TransformObjectToHClip(positionOS.xyz), viewPosition, _SurfaceDepthTolerance);
            }
            half4 Frag() : SV_Target { return _BaseColor; }
            ENDHLSL
        }
    }
}
