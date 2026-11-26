/*
[ 코드 설명 ]
1. 기존 Netcode 플레이어에 빛 방출기의 충전 요청과 결과만 연결합니다.
2. 서버 시계로 충전·쿨타임·횟수를 검증하고 서버에서만 스턴을 적용합니다. 요청 번호로 중복 방출을 막고 모든 피어에 섬광을 한 번 전달합니다.
*/
using Unity.Netcode;
using UnityEngine;

namespace VoidLast.Player
{
    [DisallowMultipleComponent]
    public sealed class FpsFlashEmitterNetwork : NetworkBehaviour
    {
        readonly NetworkVariable<int> spent = new NetworkVariable<int>();
        readonly NetworkVariable<uint> token = new NetworkVariable<uint>();
        readonly NetworkVariable<double> started = new NetworkVariable<double>(-1);
        readonly NetworkVariable<double> readyAt = new NetworkVariable<double>();
        FpsFlashEmitter emitter;
        uint localRequest, lastRequest, lastFlash;
        bool holding;
        public int Spent => spent.Value;
        public bool IsCharging => IsOwner ? holding : token.Value != 0;
        public float ChargeProgress => emitter != null && token.Value != 0 && (!IsOwner || holding && token.Value == localRequest)
            ? Mathf.Clamp01((float)(NetworkManager.ServerTime.Time - started.Value) / Mathf.Max(0.01f, emitter.chargeDuration)) : 0;
        public float CooldownRemaining => IsSpawned ? Mathf.Max(0, (float)(readyAt.Value - NetworkManager.ServerTime.Time)) : 0;
        void Awake() => emitter = GetComponent<FpsFlashEmitter>();
        public override void OnNetworkDespawn() => holding = false;
        public bool BeginCharge()
        {
            if (!IsSpawned || !IsOwner || holding || emitter == null || !emitter.CanOperate || emitter.RemainingUses <= 0 || CooldownRemaining > 0) return false;
            holding = true; BeginRpc(++localRequest); return true;
        }
        public bool ReleaseCharge()
        {
            if (!IsSpawned || !IsOwner || !holding) return false;
            bool full = ChargeProgress >= 1; holding = false;
            if (full) ReleaseRpc(localRequest); else CancelRpc(localRequest);
            emitter.View.HideGauge(); return full; // Visual flash/consumption still wait for server approval.
        }
        public void CancelCharge()
        {
            if (!IsSpawned || !IsOwner || !holding) return;
            holding = false; CancelRpc(localRequest);
        }
        bool Valid => emitter != null && emitter.CanOperate && spent.Value < Mathf.Max(0, emitter.maxUses);
        [Rpc(SendTo.Server, RequireOwnership = true)]
        void BeginRpc(uint request, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != OwnerClientId) return;
            bool fresh = request > lastRequest; if (fresh) lastRequest = request;
            if (!fresh || !Valid || token.Value != 0 || NetworkManager.ServerTime.Time < readyAt.Value) { RejectRpc(request); return; }
            token.Value = request; started.Value = NetworkManager.ServerTime.Time;
        }
        [Rpc(SendTo.Server, RequireOwnership = true)]
        void ReleaseRpc(uint request, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != OwnerClientId || request == 0 || request != token.Value) return;
            bool valid = Valid && NetworkManager.ServerTime.Time >= readyAt.Value && NetworkManager.ServerTime.Time - started.Value >= Mathf.Max(0.01f, emitter.chargeDuration);
            EndCharge();
            if (!valid) { RejectRpc(request); return; }
            spent.Value++; readyAt.Value = NetworkManager.ServerTime.Time + Mathf.Max(0, emitter.cooldownDuration);
            emitter.ApplyStun(); FlashRpc(request);
        }
        [Rpc(SendTo.Server, RequireOwnership = true)]
        void CancelRpc(uint request, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId == OwnerClientId && request == token.Value) EndCharge();
        }
        [Rpc(SendTo.Owner)]
        void RejectRpc(uint request, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId == Unity.Netcode.NetworkManager.ServerClientId && request == localRequest) { holding = false; emitter.View.HideGauge(); }
        }
        [Rpc(SendTo.Everyone)]
        void FlashRpc(uint request, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != Unity.Netcode.NetworkManager.ServerClientId || request <= lastFlash) return;
            lastFlash = request; emitter.View.PlayFlash();
        }
        void Update()
        {
            if (!IsSpawned || !IsServer || token.Value == 0 || Valid) return;
            uint request = token.Value; EndCharge(); RejectRpc(request);
        }
        void EndCharge() { token.Value = 0; started.Value = -1; }
    }
}
