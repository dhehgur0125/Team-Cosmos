/*
[ 코드 설명 ]
1. 빛 방출기의 기존 FPS 손·임시 모델 장착과 로컬 충전 막대만 표시합니다.
2. 충전 발광·짧은 입자·한 개의 Point Light를 재생합니다. 스턴·횟수 판정은 하지 않으며 카메라별로 FPS/전신 모델을 구분합니다.
*/
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace VoidLast.Player
{
    public sealed class FpsFlashEmitterView : MonoBehaviour
    {
        [SerializeField] FpsFlashEmitter emitter;
        [SerializeField] Camera localCamera;
        [SerializeField] Transform localRoot, worldRoot;
        [SerializeField] Transform localMuzzle, worldMuzzle;
        [SerializeField] Renderer localLens, worldLens;
        [SerializeField] ParticleSystem localFlash, worldFlash;
        [SerializeField] Light flashLight;
        [SerializeField] Image chargeGauge;
        [SerializeField] Vector3 heldPosition = new Vector3(0.23f, -0.2f, 0.4f);
        [SerializeField] Vector3 heldEuler = new Vector3(-5, -8, 0);
        [SerializeField, Min(0.01f)] float equipDuration = 0.35f;
        [SerializeField, Min(1)] float gaugeWidth = 140;
        [SerializeField, Min(0.01f)] float flashDuration = 0.16f;
        [SerializeField, Min(0)] float flashIntensity = 6;
        [SerializeField, Min(0.1f)] float lightRange = 4;
        readonly FpsEquipMotion equip = new FpsEquipMotion();
        MaterialPropertyBlock lensColor;
        FpsEquipment equipment;
        float flashEnds;
        int flashFrame = -1;
        Vector3 previousLightPosition;
        bool lightMoved;
        public bool IsEquipped => localRoot != null && emitter != null && emitter.IsSelected && equip.IsReady;
        public Vector3 EmissionPoint => localMuzzle != null ? localMuzzle.position : transform.position;
        public bool GaugeVisible => chargeGauge != null && chargeGauge.enabled;
        public int FlashesPlayed { get; private set; }
        public Transform LocalRoot => localRoot;
        public Transform WorldRoot => worldRoot;
        void Awake() { equipment = GetComponent<FpsEquipment>(); lensColor = new MaterialPropertyBlock(); }
        void OnEnable()
        {
            equipment.SlotChanged += SelectionChanged;
            RenderPipelineManager.beginCameraRendering += BeginSrp; RenderPipelineManager.endCameraRendering += EndSrp;
            Camera.onPreCull += BeginCamera; Camera.onPostRender += EndCamera;
            Refresh(0);
        }
        void OnDisable()
        {
            equipment.SlotChanged -= SelectionChanged;
            RenderPipelineManager.beginCameraRendering -= BeginSrp; RenderPipelineManager.endCameraRendering -= EndSrp;
            Camera.onPreCull -= BeginCamera; Camera.onPostRender -= EndCamera;
            RestoreLight(); HideGauge(); equip.Reset(); flashEnds = 0;
            if (flashLight != null) flashLight.enabled = false;
            if (localRoot != null) localRoot.gameObject.SetActive(false);
            if (worldRoot != null) worldRoot.gameObject.SetActive(false);
        }
        void SelectionChanged() { equip.Reset(); Refresh(0); }
        void LateUpdate() => Refresh(Time.deltaTime);
        public void HideGauge() { if (chargeGauge != null) chargeGauge.enabled = false; }
        public void Refresh(float deltaTime)
        {
            bool visible = emitter != null && emitter.IsSelected;
            equip.Tick(visible, 3, deltaTime, equipDuration);
            if (localRoot != null)
            {
                localRoot.gameObject.SetActive(visible);
                localRoot.localPosition = heldPosition + new Vector3(0, -0.35f, -0.05f) * (1 - equip.Blend);
                localRoot.localRotation = Quaternion.Euler(heldEuler);
            }
            if (worldRoot != null) worldRoot.gameObject.SetActive(visible);
            bool charging = visible && emitter.IsCharging;
            float progress = charging ? emitter.ChargeProgress : 0;
            if (chargeGauge != null)
            {
                chargeGauge.enabled = charging && emitter.IsLocal;
                var position = chargeGauge.rectTransform.anchoredPosition;
                position.x = -gaugeWidth * 0.5f; chargeGauge.rectTransform.anchoredPosition = position;
                chargeGauge.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, gaugeWidth * progress);
                chargeGauge.color = progress >= 1 ? new Color(0.85f, 1, 1) : new Color(0.3f, 0.75f, 0.9f);
            }
            float flash = flashFrame == Time.frameCount ? 1 : Mathf.Clamp01((flashEnds - Time.time) / Mathf.Max(0.01f, flashDuration));
            float glow = 0.08f + (charging ? Mathf.Lerp(0.2f, 3, progress) : 0) + 8 * flash;
            SetLens(localLens, glow); SetLens(worldLens, glow);
            if (flashLight != null)
            {
                flashLight.enabled = visible && (charging || flash > 0);
                flashLight.intensity = flash > 0 ? flashIntensity * flash * flash : progress * 0.5f;
                flashLight.range = flash > 0 ? lightRange : 1.2f;
            }
        }
        void SetLens(Renderer lens, float glow)
        {
            if (lens == null) return;
            lens.GetPropertyBlock(lensColor); lensColor.SetColor("_EmissionColor", new Color(0.65f, 0.9f, 1) * glow); lens.SetPropertyBlock(lensColor);
        }
        public void PlayFlash()
        {
            FlashesPlayed++; flashFrame = Time.frameCount; flashEnds = Time.time + flashDuration;
            foreach (var particles in new[] { localFlash, worldFlash })
                if (particles != null && particles.gameObject.activeInHierarchy)
                {
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); particles.Play(); particles.Emit(10);
                    // One short, soft burst makes the release readable even against a bright background.
                    particles.Emit(new ParticleSystem.EmitParams { startSize = 0.35f, startLifetime = 0.1f, velocity = Vector3.zero, startColor = new Color(0.8f, 0.95f, 1, 0.85f) }, 1);
                }
            Refresh(0);
        }
        void BeginSrp(ScriptableRenderContext context, Camera camera) => BeginCamera(camera);
        void EndSrp(ScriptableRenderContext context, Camera camera) => RestoreLight();
        void EndCamera(Camera camera) => RestoreLight();
        void BeginCamera(Camera camera)
        {
            RestoreLight();
            if (flashLight == null || !flashLight.enabled || localMuzzle == null || worldMuzzle == null) return;
            previousLightPosition = flashLight.transform.position;
            flashLight.transform.position = camera == localCamera ? localMuzzle.position : worldMuzzle.position; lightMoved = true;
        }
        void RestoreLight() { if (lightMoved && flashLight != null) flashLight.transform.position = previousLightPosition; lightMoved = false; }
    }
}
