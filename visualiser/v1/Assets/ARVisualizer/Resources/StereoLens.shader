Shader "ARVisualizer/Stereo Lens"
{
    Properties { [PerRendererData] _MainTex ("Eye view", 2D) = "black" {} }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float2 _LensCentre, _Distortion;
            float _Border;
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            Output vert(Input v) { Output o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            half4 frag(Output i) : SV_Target
            {
                if (any(i.uv < _Border) || any(i.uv > 1 - _Border)) return half4(0,0,0,1);
                float2 p = (i.uv - _LensCentre) * 2;
                float r2 = dot(p, p);
                // Both optical axes sample the centres of their parallel eye projections.
                float2 uv = 0.5 + p * (1 + _Distortion.x * r2 + _Distortion.y * r2 * r2) * 0.5;
                if (any(uv < 0) || any(uv > 1)) return half4(0,0,0,1);
                return half4(tex2D(_MainTex, uv).rgb, 1);
            }
            ENDHLSL
        }
    }
}
