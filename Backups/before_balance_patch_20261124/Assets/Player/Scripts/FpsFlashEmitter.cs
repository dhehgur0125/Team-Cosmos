/*
[ 코드 설명 ]
1. 3번 슬롯 빛 방출기의 충전·해제·횟수·쿨타임과 서버 측 스턴 판정을 담당합니다.
2. 기존 FPS 입력과 슬롯을 사용하며 완충 후 버튼을 놓아야 방출합니다. 모델·게이지·섬광은 별도 View에 맡깁니다.
*/
using System.Collections.Generic;
using UnityEngine;
using VoidLast.Monster;

namespace VoidLast.Player
{
    [DisallowMultipleComponent]
    public sealed class FpsFlashEmitter : MonoBehaviour
    {
        [Min(0.01f)] public float chargeDuration = 1.5f;
        [Min(0)] public float stunDuration = 5f;
        [Min(0)] public float effectRadius = 5f;
        [Min(0)] public float cooldownDuration = 5f;
        [Min(0)] public int maxUses = 3;
        [SerializeField] FpsFlashEmitterView view;
        [SerializeField] Camera viewCamera;
        FpsEquipment equipment;
        FpsHealth health;
        FpsInput input;
        FpsMedicalKit medicalKit;
        FpsFlashEmitterNetwork network;
        bool charging;
        float started, readyAt;
        int spent;
        readonly HashSet<MonsterHealth> affected = new HashSet<MonsterHealth>();
        bool Online => network != null && network.IsSpawned;
        public bool IsLocal => Online ? network.IsOwner : equipment != null && !equipment.IsNetworkProxy;
        FpsUniqueItem unique;
        FpsUniqueItem Unique => unique != null ? unique : unique = FpsUniqueItem.For(gameObject);
        // 고유 아이템으로 스턴을 고른 대원만 3번 칸에서 꺼낸다 (혼자 시험: 구급상자가 꺼져 있으면 스턴)
        public bool IsSelected => isActiveAndEnabled && health != null && !health.IsDead && equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Item && Unique.Has(UniqueItemKind.Stun);
        public bool CanOperate => IsSelected && !FpsInput.Blocked && view != null && view.isActiveAndEnabled && view.IsEquipped;
        public bool IsCharging => Online ? network.IsCharging : charging;
        public float ChargeProgress => Online ? network.ChargeProgress : charging ? Mathf.Clamp01((Time.time - started) / Mathf.Max(0.01f, chargeDuration)) : 0;
        public int RemainingUses => Mathf.Max(0, maxUses - (Online ? network.Spent : spent));
        public float CooldownRemaining => Online ? network.CooldownRemaining : Mathf.Max(0, readyAt - Time.time);
        public int LastStunnedCount { get; private set; }
        public FpsFlashEmitterView View => view;

        void Awake()
        {
            equipment = GetComponent<FpsEquipment>(); health = GetComponent<FpsHealth>(); input = GetComponent<FpsInput>();
            medicalKit = GetComponent<FpsMedicalKit>(); network = GetComponent<FpsFlashEmitterNetwork>();
        }
        void OnEnable() { equipment.SlotChanged += CancelCharge; health.Died += CancelCharge; }
        void OnDisable() { equipment.SlotChanged -= CancelCharge; health.Died -= CancelCharge; CancelCharge(); }
        void Update()
        {
            if (!IsLocal) return;
            if (!CanOperate) { CancelCharge(); return; }
            if (input == null || !input.isActiveAndEnabled) return;
            if (Cursor.lockState != CursorLockMode.Locked || !Application.isFocused)
            { CancelCharge(); return; }
            ProcessInput(input.Attack, input.AttackHeld);
        }
        public void ProcessInput(bool pressed, bool held)
        {
            if (!CanOperate) { CancelCharge(); return; }
            if (pressed) TryBeginCharge();
            if (IsCharging && !held) ReleaseCharge();
        }
        public bool TryBeginCharge()
        {
            if (!IsLocal || !CanOperate || IsCharging || RemainingUses <= 0 || CooldownRemaining > 0) return false;
            if (Online) return network.BeginCharge();
            charging = true; started = Time.time; return true;
        }
        public bool ReleaseCharge()
        {
            if (!IsLocal || !IsCharging) return false;
            if (Online) return network.ReleaseCharge();
            bool complete = ChargeProgress >= 1; charging = false; view.HideGauge();
            if (!complete || !CanOperate || RemainingUses <= 0 || CooldownRemaining > 0) return false;
            spent++; readyAt = Time.time + Mathf.Max(0, cooldownDuration);
            ApplyStun(); view.PlayFlash(); return true;
        }
        public void CancelCharge()
        {
            charging = false;
            if (Online && network.IsOwner) network.CancelCharge();
            if (view != null) view.HideGauge();
        }
        public Vector3 EmissionOrigin()
        {
            var origin = viewCamera.transform.position;
            var desired = view.EmissionPoint;
            var delta = desired - origin; float distance = delta.magnitude;
            // A view model can extend into a nearby wall; keep gameplay emission on the player's side.
            foreach (var hit in Physics.RaycastAll(origin, delta.normalized, distance, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(transform)) distance = Mathf.Min(distance, Mathf.Max(0, hit.distance - 0.02f));
            return origin + delta.normalized * distance;
        }
        public int ApplyStun()
        {
            LastStunnedCount = 0;
            if (Online && !network.IsServer || viewCamera == null || view == null || effectRadius <= 0 || stunDuration <= 0) return 0;
            var origin = EmissionOrigin(); affected.Clear();
            foreach (var collider in Physics.OverlapSphere(origin, effectRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                var monster = collider.GetComponentInParent<MonsterHealth>();
                if (monster == null || monster.IsDead || monster.NetworkHitRedirect != null || !affected.Add(monster)) continue;
                var body = monster.GetComponent<Collider>(); if (body == null || !body.enabled) continue;
                Vector3 delta = body.bounds.center - origin;
                if (delta.magnitude > effectRadius || !Visible(monster, origin, delta)) continue;
                var stun = monster.GetComponent<MonsterStun>(); if (stun == null) stun = monster.gameObject.AddComponent<MonsterStun>();
                if (stun.Apply(stunDuration)) LastStunnedCount++;
            }
            return LastStunnedCount;
        }
        bool Visible(MonsterHealth monster, Vector3 origin, Vector3 delta)
        {
            float nearest = float.PositiveInfinity; Collider first = null;
            foreach (var hit in Physics.RaycastAll(origin, delta.normalized, delta.magnitude + 0.02f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
                first = hit.collider; nearest = hit.distance;
            }
            return first != null && first.GetComponentInParent<MonsterHealth>() == monster;
        }
    }
}
