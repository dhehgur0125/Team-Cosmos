/* [ 코드 설명 ] Fps Weapons Pack의 팔(FirstPersonArms)과 권총(Pistol) 리그를 1인칭 카메라 아래에 두고, 기존 권총 이벤트(장착·발사·재장전·조준)에 맞춰 팩의 Hand/Gun Animator 상태를 함께 재생합니다. 사격 판정·탄약·네트워크는 기존 FpsPistol/FpsEquipment를 그대로 쓰며 이 컴포넌트는 표시와 애니메이션만 담당합니다. 총구 화염과 손전등은 애니메이션되는 총 본(bone)을 따라가도록 위치를 맞춥니다. */
using System;
using UnityEngine;

namespace VoidLast.Player
{
    [DefaultExecutionOrder(200)] // After FpsWeaponView moves WeaponRoot, so followers keep the animated bone position.
    public sealed class FpsPackPistolRig : MonoBehaviour
    {
        [Serializable]
        public sealed class Follower
        {
            public Transform target;
            public Transform bone;
            public Vector3 localPosition;
            public Vector3 localEuler;
        }

        [SerializeField] GameObject rigRoot;
        [SerializeField] Animator handAnimator;
        [SerializeField] Animator gunAnimator;
        [SerializeField] Follower[] followers = new Follower[0];
        [Header("팩 클립 길이(초). 재생 속도 계산에 사용")]
        [SerializeField, Min(0.1f)] float drawClipLength = 0.667f;
        [SerializeField, Min(0.1f)] float reloadClipLength = 2.333f;
        [SerializeField, Min(0.05f)] float holsterClipLength = 0.333f;
        [Tooltip("권총 해제 재생 속도. 해제가 끝나야 다음 장비가 올라온다.")]
        [SerializeField, Range(0.5f, 3f)] float holsterSpeed = 1.2f;
        [SerializeField, Min(0)] float blendTime = 0.04f;
        FpsPistol pistol;
        FpsEquipment equipment;
        FpsMotor motor;
        FpsWeaponView view;
        bool shown, playedAim, wasReloading, holstering;
        float busyUntil;
                public bool IsShown => shown;
        public bool IsHolstering => holstering;
        public string CurrentState { get; private set; } = "";
        public Animator HandAnimator => handAnimator;
        public Animator GunAnimator => gunAnimator;

        void Awake()
        {
            pistol = GetComponent<FpsPistol>(); equipment = GetComponent<FpsEquipment>(); motor = GetComponent<FpsMotor>(); view = GetComponent<FpsWeaponView>();
            foreach (var animator in new[] { handAnimator, gunAnimator })
            {
                if (animator == null) continue;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.applyRootMotion = false;
                foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;
            }
            if (rigRoot != null) rigRoot.SetActive(false);
        }
        void OnEnable() { if (pistol != null) pistol.Shot += Fired; }
        void OnDisable()
        {
            if (pistol != null) pistol.Shot -= Fired;
            shown = false; wasReloading = false; holstering = false; busyUntil = 0;
            if (rigRoot != null) rigRoot.SetActive(false);
        }
        void Update()
        {
            if (rigRoot == null) return;
            bool selected = equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Pistol && !motor.IsDead;
            if (selected && !shown)
            {
                shown = true; rigRoot.SetActive(true);
                PlayDraw(false);
            }
            else if (!selected && shown) { shown = false; holstering = false; rigRoot.SetActive(false); return; }
            if (!shown || holstering) return;
            if (pistol.IsReloading && !wasReloading)
            {
                float speed = reloadClipLength / pistol.ReloadDuration;
                Play("PistolReload", speed); busyUntil = Time.time + pistol.ReloadDuration; playedAim = false;
            }
            wasReloading = pistol.IsReloading;
            if (Time.time >= busyUntil && !pistol.IsReloading && equipment.IsAiming != playedAim)
            {
                playedAim = equipment.IsAiming;
                Play(playedAim ? "PistolAdsUp" : "PistolAdsDown", 1);
            }
        }
        void PlayDraw(bool fade)
        {
            playedAim = false; wasReloading = false; holstering = false;
            float duration = view != null ? view.EquipDuration : drawClipLength;
            Play("PistolDraw", drawClipLength / Mathf.Max(0.1f, duration), fade);
            busyUntil = Time.time + duration;
        }
        /// <summary>권총 해제 애니메이션을 시작하고 걸리는 시간(초)을 돌려준다. 표시 중이 아니면 0.</summary>
        public float BeginHolster()
        {
            if (!shown || motor.IsDead || rigRoot == null) return 0;
            holstering = true;
            Play("PistolHolster", holsterSpeed);
            return holsterClipLength / holsterSpeed;
        }
        /// <summary>해제 도중 다시 권총을 고르면 그 자리에서 다시 꺼낸다.</summary>
        public void CancelHolster() { if (holstering && shown) PlayDraw(true); }
        void Fired()
        {
            if (!shown || holstering || pistol.IsReloading) return;
            Play(equipment.IsAiming ? "PistolAdsFire" : "PistolHipFire", 1);
            playedAim = equipment.IsAiming; busyUntil = Time.time;
        }
        void Play(string state, float speed, bool fade = true)
        {
            CurrentState = state;
            foreach (var animator in new[] { handAnimator, gunAnimator })
            {
                if (animator == null || !animator.gameObject.activeInHierarchy) continue;
                animator.speed = speed;
                if (fade && blendTime > 0 && animator.isInitialized) animator.CrossFadeInFixedTime(state, blendTime, 0, 0);
                else animator.Play(state, 0, 0);
            }
        }
        void LateUpdate()
        {
            if (!shown) return;
            foreach (var follower in followers)
            {
                if (follower == null || follower.target == null || follower.bone == null) continue;
                follower.target.SetPositionAndRotation(follower.bone.TransformPoint(follower.localPosition), follower.bone.rotation * Quaternion.Euler(follower.localEuler));
            }
        }
    }
}
