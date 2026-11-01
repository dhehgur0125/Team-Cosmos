Shader "Custom/EchoPing"
{
    // 파동이 아이템에 닿았을 때 아이템 위치에서 번져 나가는 메아리 링 (URP).
    // 카메라를 향하는 쿼드에 그리고 깊이 검사를 하지 않아서, 벽 너머에서도 보인다.
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 0.85, 0.2, 1)
        _Progress ("Progress", Range(0, 1.25)) = 0
        _Intensity ("Intensity", Float) = 1.5
        _RingWidth ("Ring Width", Float) = 0.09
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+150"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "EchoPing"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZTest Always
            ZWrite Off
            Cull Off
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Progress;
                float _Intensity;
                float _RingWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            // p(0~1)에 따라 바깥으로 퍼지며 얇아지고 사라지는 링 하나
            float RingAt(float r, float p)
            {
                if (p <= 0.0 || p >= 1.0) return 0.0;

                float inv = saturate(1.0 - p);
                float radius = 1.0 - inv * inv;                // 처음에 빠르게, 끝에 느리게
                float width = _RingWidth * (1.0 - 0.5 * p);
                float ring = saturate(1.0 - abs(r - radius) / width);
                return ring * ring * pow(inv, 1.5);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = (IN.uv - 0.5) * 2.0;
                float r = length(p);

                // 메아리처럼 시간차를 두고 두 번째 링이 뒤따른다
                float ring1 = RingAt(r, _Progress);
                float ring2 = RingAt(r, _Progress - 0.25) * 0.6;

                // 닿는 순간 중심에서 번쩍
                float flash = exp(-r * r * 30.0) * saturate(1.0 - _Progress * 3.0);

                float v = (ring1 + ring2 + flash) * _Intensity;
                return half4(_Color.rgb * v, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
