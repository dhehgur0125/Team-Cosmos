/* [ 코드 설명 ] 불빛이 없는 곳에서도 몬스터가 어렴풋이 보이게 합니다. 두 가지를 함께 씁니다.
   1. 몬스터에만 닿는 약한 조명: 조명(selfLight)과 몬스터의 렌더러를 같은 "렌더링 레이어"(Monster)에 묶어서, 이 조명이 몬스터 몸만 비추고 벽·바닥은 밝히지 않게 합니다.
      몬스터 렌더러는 기본 레이어도 그대로 가지므로 손전등 같은 다른 조명은 평소처럼 받습니다.
   2. 발광 무늬: 모델 재질에 들어 있는 발광 무늬(눈·무늬)의 밝기를 emissionBoost배로 키웁니다. 재질 파일은 바꾸지 않고 이 몬스터에만 적용합니다.
   쓰러져 있는 동안(그로기)에는 조명과 발광을 downFactor배로 줄입니다. 밝기는 프리팹의 조명(Intensity·Range)과 이 컴포넌트의 수치로 조절합니다. */
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace VoidLast.Monster
{
    [DisallowMultipleComponent]
    public sealed class MonsterGlow : MonoBehaviour
    {
        /// <summary>몬스터 전용 렌더링 레이어 (Project Settings > Tags and Layers > Rendering Layers의 7번 "Monster")</summary>
        public const int MonsterLayerIndex = 7;
        public const uint MonsterLayerBit = 1u << MonsterLayerIndex;

        [Tooltip("몬스터 몸만 비추는 조명 (그림자 없음). 밝기와 범위는 이 조명에서 조절합니다.")]
        [SerializeField] Light selfLight;
        [Tooltip("발광 무늬(눈·무늬) 밝기 배율. 1 = 재질 원래 밝기.")]
        [SerializeField, Min(0)] float emissionBoost = 1.5f;
        [Tooltip("쓰러져 있는 동안의 밝기 배율.")]
        [SerializeField, Range(0, 1)] float downFactor = 0.35f;

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        MonsterHealth health;
        Renderer[] renderers;
        Color[][] baseEmission;   // 렌더러·재질별 원래 발광 색 (발광이 없는 재질은 검정)
        MaterialPropertyBlock block;
        float lightIntensity, level = 1;

        void Awake()
        {
            health = GetComponent<MonsterHealth>();
            block = new MaterialPropertyBlock();
            renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            baseEmission = new Color[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].renderingLayerMask |= MonsterLayerBit;   // 전용 조명을 받도록 (기본 레이어는 유지)
                var materials = renderers[i].sharedMaterials;
                baseEmission[i] = new Color[materials.Length];
                for (int m = 0; m < materials.Length; m++)
                {
                    var material = materials[m];
                    bool glows = material != null && material.HasProperty(EmissionColor) && material.IsKeywordEnabled("_EMISSION");
                    baseEmission[i][m] = glows ? material.GetColor(EmissionColor) : Color.black;
                }
            }
            if (selfLight != null)
            {
                lightIntensity = selfLight.intensity;
                selfLight.shadows = LightShadows.None;
                var data = selfLight.GetComponent<UniversalAdditionalLightData>();
                if (data == null) data = selfLight.gameObject.AddComponent<UniversalAdditionalLightData>();
                data.renderingLayers = MonsterLayerBit;   // 몬스터 렌더러만 비춘다
            }
            Apply(1);
        }

        void Update()
        {
            float wanted = health != null && health.IsDead ? downFactor : 1;
            if (Mathf.Approximately(level, wanted)) return;
            Apply(Mathf.MoveTowards(level, wanted, Time.deltaTime * 1.5f));
        }

        void Apply(float value)
        {
            level = value;
            if (selfLight != null) selfLight.intensity = lightIntensity * value;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                for (int m = 0; m < baseEmission[i].Length; m++)
                {
                    if (baseEmission[i][m].maxColorComponent <= 0) continue;
                    renderers[i].GetPropertyBlock(block, m);
                    block.SetColor(EmissionColor, baseEmission[i][m] * (emissionBoost * value));
                    renderers[i].SetPropertyBlock(block, m);
                }
            }
        }
    }
}
