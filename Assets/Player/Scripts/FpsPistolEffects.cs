/* [ 코드 설명 ] 성공한 권총 발사 이벤트에만 짧은 총구 화염·약한 섬광을 재생하고 환경 명중점에 작은 불꽃을 생성합니다. 사격 판정·탄흔·손 애니메이션·카메라 회전은 변경하지 않습니다. */
using UnityEngine;

namespace VoidLast.Player
{
    public sealed class FpsPistolEffects : MonoBehaviour
    {
        [SerializeField] ParticleSystem localFlash;
        [SerializeField] ParticleSystem worldFlash;
        [SerializeField] Light muzzleLight;
        [SerializeField] ParticleSystem impactPrefab;
        [SerializeField, Min(0.005f)] float flashSize = 0.16f;
        [SerializeField, Range(0.015f, 0.12f)] float flashDuration = 0.045f;
        [SerializeField, Min(0)] float lightIntensity = 0.12f;
        [SerializeField, Min(0.1f)] float lightRange = 1;
        [SerializeField, Min(0.1f)] float impactScale = 1;
        FpsPistol pistol;
        FpsEquipment equipment;
        FpsMotor motor;
        float remaining;
        public bool IsFlashing => remaining > 0;
        public ParticleSystem LocalFlash => localFlash;
        public ParticleSystem WorldFlash => worldFlash;
        public ParticleSystem LastImpact { get; private set; }

        void Awake() { pistol = GetComponent<FpsPistol>(); equipment = GetComponent<FpsEquipment>(); motor = GetComponent<FpsMotor>(); }
        void OnEnable()
        {
            pistol.Shot += Fire; pistol.Hit += Impact; pistol.Changed += StateChanged;
            equipment.SlotChanged += Clear; Clear();
        }
        void OnDisable()
        {
            pistol.Shot -= Fire; pistol.Hit -= Impact; pistol.Changed -= StateChanged;
            equipment.SlotChanged -= Clear; Clear();
        }
        void Update()
        {
            if (motor.IsDead) { Clear(); return; }
            remaining = Mathf.Max(0, remaining - Time.deltaTime);
            if (muzzleLight != null)
            {
                muzzleLight.enabled = remaining > 0 && lightIntensity > 0;
                muzzleLight.intensity = lightIntensity * Mathf.Clamp01(remaining / Mathf.Max(0.015f, flashDuration));
            }
        }
        void Fire()
        {
            remaining = Mathf.Max(0.015f, flashDuration);
            Emit(localFlash); Emit(worldFlash);
            if (muzzleLight != null) { muzzleLight.range = lightRange; muzzleLight.intensity = lightIntensity; muzzleLight.enabled = lightIntensity > 0; }
        }
        void Emit(ParticleSystem flash)
        {
            if (flash == null) return;
            flash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            flash.Play();
            flash.Emit(new ParticleSystem.EmitParams { startSize = flashSize, startLifetime = remaining, rotation = Random.Range(0, 360) }, 1);
        }
        void Impact(RaycastHit hit)
        {
            if (impactPrefab == null || hit.collider.GetComponentInParent<FpsHealth>() != null || hit.collider.GetComponentInParent<Animator>() != null) return;
            LastImpact = Instantiate(impactPrefab, hit.point + hit.normal * 0.003f, Quaternion.LookRotation(hit.normal));
            LastImpact.transform.localScale = Vector3.one * impactScale;
            LastImpact.Play(true); // Non-looping prefab destroys itself when its last particle expires.
        }
        void StateChanged() { if (pistol.IsReloading) Clear(); }
        void Clear()
        {
            remaining = 0;
            if (localFlash != null) localFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (worldFlash != null) worldFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (muzzleLight != null) muzzleLight.enabled = false;
        }
    }
}
