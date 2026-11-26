/* [ 코드 설명 ] 기존 Netcode 플레이어의 의료 키트 요청만 연결합니다. 서버가 대상·거리·가림·1.5초·횟수를 검증하고 HP 소유자가 한 번 회복한 성공 응답에만 횟수를 차감합니다. 기존 HP 소유권·사격 RPC·로비는 변경하지 않습니다. */
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace VoidLast.Player
{
    [DisallowMultipleComponent]
    public sealed class FpsMedicalKitNetwork : NetworkBehaviour
    {
        readonly NetworkVariable<int> uses = new NetworkVariable<int>();
        readonly NetworkVariable<double> started = new NetworkVariable<double>(-1);
        readonly NetworkVariable<bool> onOther = new NetworkVariable<bool>();
        readonly Dictionary<ulong, uint> lastApplied = new Dictionary<ulong, uint>();
        FpsMedicalKit kit;
        FpsHealth health;
        FpsMedicalKitNetwork recipient;
        uint nextRequest, lastRequest, activeRequest;
        bool pending, awaitingResult;
        public int RemainingUses => uses.Value;
        public bool IsUsing => started.Value >= 0 || IsOwner && pending;
        public bool UsingOnOther => onOther.Value;
        public float UseProgress => kit != null && started.Value >= 0 ? Mathf.Clamp01((float)(NetworkManager.ServerTime.Time - started.Value) / kit.UseDuration) : 0;
        void Awake() { kit = GetComponent<FpsMedicalKit>(); health = GetComponent<FpsHealth>(); }
        public override void OnNetworkSpawn() { if (IsServer) { uses.Value = kit != null ? kit.StartingUses : 0; started.Value = -1; } }
        public override void OnNetworkDespawn() { pending = awaitingResult = false; recipient = null; lastApplied.Clear(); }
        public bool RequestUse(FpsHealth target)
        {
            if (!IsSpawned || !IsOwner || IsUsing || kit == null || !kit.CanTreat(target)) return false;
            var bridge = target.GetComponent<FpsMedicalKitNetwork>();
            if (bridge == null || !bridge.IsSpawned || !bridge.NetworkObject.IsPlayerObject) return false;
            pending = true; StartUseRpc(++nextRequest, new NetworkObjectReference(bridge.NetworkObject)); return true;
        }
        public void CancelUse()
        {
            if (!IsSpawned || !IsOwner || !IsUsing) return;
            pending = false; CancelUseRpc(nextRequest);
        }
        [Rpc(SendTo.Server, RequireOwnership = true)]
        void StartUseRpc(uint request, NetworkObjectReference target, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != OwnerClientId) return;
            bool fresh = request > lastRequest; if (fresh) lastRequest = request;
            FpsMedicalKitNetwork candidate = null;
            if (target.TryGet(out var obj, NetworkManager) && obj.IsPlayerObject) candidate = obj.GetComponent<FpsMedicalKitNetwork>();
            bool accepted = fresh && started.Value < 0 && !awaitingResult && uses.Value > 0 && candidate != null && candidate.IsSpawned && kit != null && kit.CanTreat(candidate.health);
            if (accepted)
            {
                activeRequest = request; recipient = candidate; onOther.Value = candidate != this;
                started.Value = NetworkManager.ServerTime.Time;
            }
            StartReplyRpc(request);
        }
        [Rpc(SendTo.Owner)]
        void StartReplyRpc(uint request, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId == Unity.Netcode.NetworkManager.ServerClientId && request == nextRequest) pending = false;
        }
        [Rpc(SendTo.Server, RequireOwnership = true)]
        void CancelUseRpc(uint request, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId == OwnerClientId && request == activeRequest && !awaitingResult) EndUse();
        }
        void Update()
        {
            if (!IsSpawned || !IsServer || started.Value < 0) return;
            if (recipient == null || !recipient.IsSpawned) { EndUse(); return; }
            if (awaitingResult) return; // A completed request remains locked until its HP owner acknowledges it.
            if (kit == null || uses.Value <= 0 || !kit.CanTreat(recipient.health)) { EndUse(); return; }
            if (NetworkManager.ServerTime.Time - started.Value < kit.UseDuration) return;
            awaitingResult = true;
            recipient.CommitHealRpc(new NetworkObjectReference(NetworkObject), activeRequest);
        }
        [Rpc(SendTo.Owner)]
        void CommitHealRpc(NetworkObjectReference healerReference, uint request, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != Unity.Netcode.NetworkManager.ServerClientId || !healerReference.TryGet(out var healer, NetworkManager)) return;
            var source = healer.GetComponent<FpsMedicalKitNetwork>();
            if (source == null) return;
            // Reliable RPCs are still treated as untrusted duplicates: never apply the same request twice.
            if (lastApplied.TryGetValue(healer.NetworkObjectId, out uint previous) && request <= previous) return;
            lastApplied[healer.NetworkObjectId] = request;
            bool healed = source.kit != null && source.kit.CanTreat(health) && health.NetworkHealRedirect == null && health.Heal(source.kit.HealAmount);
            HealResultRpc(healerReference, request, healed);
        }
        [Rpc(SendTo.Server, RequireOwnership = true)]
        void HealResultRpc(NetworkObjectReference healerReference, uint request, bool healed, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != OwnerClientId || !healerReference.TryGet(out var healer, NetworkManager)) return;
            var source = healer.GetComponent<FpsMedicalKitNetwork>();
            if (source == null || !source.awaitingResult || source.recipient != this || source.activeRequest != request) return;
            if (healed) source.uses.Value = Mathf.Max(0, source.uses.Value - 1);
            source.EndUse();
        }
        void EndUse() { started.Value = -1; onOther.Value = false; awaitingResult = false; recipient = null; }
    }
}
