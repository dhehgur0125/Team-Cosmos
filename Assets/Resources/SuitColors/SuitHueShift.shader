// 우주복 색 바꾸기 (PlayerSuitColor 전용, 화면에 직접 그리지 않음).
// 원본 텍스처의 주황색 부분(색상 -5~40°, 바깥 15°는 부드럽게)만 색상을 돌리고 나머지(패치·회색·검정)는 그대로 둔다.
// 게임이 처음 대원을 만들 때(로딩 중) 한 번만 실행해 2~4P 텍스처를 만든다. 색 바꾼 이미지 파일을 저장소에 올리지 않기 위함.
Shader "Hidden/VoidLast/SuitHueShift"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Shift ("Hue Shift (0~1)", Float) = 0
        _SatMul ("Saturation Multiplier", Float) = 1
        _ValMul ("Value Multiplier", Float) = 1
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Shift, _SatMul, _ValMul;

            float3 RgbToHsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 HsvToRgb(float3 c)
            {
                float3 p = abs(frac(c.xxx + float3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
                return c.z * lerp(float3(1, 1, 1), saturate(p - 1.0), c.y);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                // 프로젝트는 Linear 색 공간: 원본(sRGB) 텍스처 값을 눈에 보이는 값(감마)으로 바꿔서 계산한다
                float3 c = LinearToGammaSpace(src.rgb);
                float3 hsv = RgbToHsv(c);
                float deg = hsv.x * 360.0;
                if (deg > 300.0) deg -= 360.0;
                float hueW = saturate(1.0 - max(0.0, max(-5.0 - deg, deg - 40.0)) / 15.0);
                float satW = saturate((hsv.y - 0.12) / 0.18);
                float w = hueW * satW;
                float3 shifted = HsvToRgb(float3(frac(hsv.x + _Shift), saturate(hsv.y * _SatMul), saturate(hsv.z * _ValMul)));
                float3 outGamma = lerp(c, shifted, w);
                return float4(GammaToLinearSpace(outGamma), src.a);
            }
            ENDCG
        }
    }
}
