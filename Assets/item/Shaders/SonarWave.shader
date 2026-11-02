Shader "Custom/SonarWave"
{
    // 플레이어에게서 퍼져 나가는 음파(소나) 이펙트 (URP, Depth Texture 필요).
    // 카메라 깊이 텍스처로 화면 속 표면의 월드 좌표를 복원해서,
    // 파동이 지나가는 바닥/벽/물체 표면에 밝은 파면과 동심원 잔물결을 그린다.
    Properties
    {
        [HDR] _Color ("Wave Color", Color) = (0.15, 0.85, 1.2, 1)
        _Origin ("Origin", Vector) = (0, 0, 0, 0)
        _Radius ("Radius", Float) = 0
        _MaxRadius ("Max Radius", Float) = 15
        _BandWidth ("Front Width", Float) = 0.18
        _TrailLength ("Trail Length", Float) = 5
        _RingSpacing ("Ring Spacing", Float) = 1.2
        _Intensity ("Intensity", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SonarWave"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            // 파동 범위를 덮는 큐브의 안쪽 면을 그리고, 실제 모양은 깊이로 계산한다.
            ZTest Always
            ZWrite Off
            Cull Front
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _Origin;
                float _Radius;
                float _MaxRadius;
                float _BandWidth;
                float _TrailLength;
                float _RingSpacing;
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(IN.positionCS);
                float rawDepth = SampleSceneDepth(uv);

                // 하늘(가장 먼 배경)에는 파동이 닿지 않는다
                #if UNITY_REVERSED_Z
                    if (rawDepth < 0.000001) return half4(0, 0, 0, 0);
                #else
                    if (rawDepth > 0.999999) return half4(0, 0, 0, 0);
                #endif

                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float dist = distance(worldPos, _Origin.xyz);
                if (dist > _MaxRadius + _BandWidth) return half4(0, 0, 0, 0);

                // d > 0 : 파면이 이미 지나간 지점 (뒤쪽 꼬리)
                float d = _Radius - dist;

                // 1) 선두: 가장 밝은 파면
                float front = saturate(1.0 - abs(d) / _BandWidth);
                front = front * front;

                // 2) 꼬리: 파면 뒤로 갈수록 옅어지는 동심원 잔물결
                float inTrail = step(0.0, d) * saturate(1.0 - d / _TrailLength);
                float rings = pow(saturate(0.5 + 0.5 * cos(d * 6.2831853 / _RingSpacing)), 6.0);
                float trail = inTrail * inTrail * rings * 0.55;

                // 3) 멀리 갈수록 약해지며 사라짐
                float fade = 1.0 - smoothstep(0.7 * _MaxRadius, _MaxRadius, _Radius);

                float v = (front * 1.8 + trail) * fade * _Intensity;
                return half4(_Color.rgb * v, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
