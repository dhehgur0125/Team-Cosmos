/* [ 코드 설명 ]
1. 기존 Netcode 플레이어의 재장전 시작·종료 시각만 전달합니다.
2. 각 피어가 탄창 낙하를 자체 재생하며 탄약 계산·사격 RPC·NetworkObject 생성에는 관여하지 않습니다.
*/
using Unity.Netcode;
using UnityEngine;

namespace VoidLast.Player
{
    public sealed class FpsReloadVisualNetwork : NetworkBehaviour
    {
        readonly NetworkVariable<double> started = new NetworkVariable<double>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        FpsPistol pistol;
        FpsMagazineDrop visual;
        bool wasReloading;
        void Awake() { pistol = GetComponent<FpsPistol>(); visual = GetComponent<FpsMagazineDrop>(); }
        public override void OnNetworkDespawn() { wasReloading = false; if (visual != null) visual.ApplyNetworkReload(false, 0); }
        void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner)
            {
                if (wasReloading == pistol.IsReloading) return;
                wasReloading = pistol.IsReloading;
                started.Value = wasReloading ? NetworkManager.ServerTime.Time - pistol.ReloadProgress * pistol.ReloadDuration : -1;
            }
            else
            {
                float progress = started.Value < 0 ? 0 : (float)(NetworkManager.ServerTime.Time - started.Value) / pistol.ReloadDuration;
                visual.ApplyNetworkReload(started.Value >= 0 && progress < 1, Mathf.Clamp01(progress), started.Value);
            }
        }
    }
}
