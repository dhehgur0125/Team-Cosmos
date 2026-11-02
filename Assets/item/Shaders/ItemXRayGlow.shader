Shader "Custom/ItemXRayGlow"
{
    // 탐지된 아이템을 벽 너머에서도 발광하는 실루엣으로 보여주는 URP용 언릿 셰이더.
    //  - _ZTest = Greater(5)    : 벽/지형에 가려진 부분에만 그려서 '투시' 효과 (Dst = OneMinusSrcAlpha)
    //  - _ZTest = LessEqual(4)  : 보이는 부분에 은은한 림라이트를 더함      (Dst = One, 가산)
    Properties
    {
        [HDR] _GlowColor ("Glow Color", Color) = (1.0, 0.8, 0.2, 1.0)
        _Intensity ("Intensity", Range(0, 4)) = 1
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 2.5
        _FillAlpha ("Fill Alpha", Range(0, 1)) = 0.4
        _RimBoost ("Rim Boost", Range(0, 4)) = 1.5
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "XRayGlow"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZTest [_ZTest]
            ZWrite Off
            Cull Back
            Offset -1, -1
            Blend SrcAlpha [_DstBlend]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half _Intensity;
                half _FresnelPower;
                half _FillAlpha;
                half _RimBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceViewDir(pos.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 n = normalize(IN.normalWS);
                half3 v = normalize(IN.viewDirWS);

                // 가장자리로 갈수록 강해지는 프레넬 림 (마인크래프트 발광 외곽선 느낌)
                half fresnel = pow(saturate(1.0h - saturate(dot(n, v))), _FresnelPower);

                half alpha = saturate((_FillAlpha + fresnel * _RimBoost) * _Intensity);
                half3 color = _GlowColor.rgb * (1.0h + fresnel * _RimBoost) * _Intensity;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
