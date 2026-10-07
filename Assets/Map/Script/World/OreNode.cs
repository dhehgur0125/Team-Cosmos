using UnityEngine;
using Unity.Netcode;

public class OreNode : NetworkBehaviour
{
    [Header("광물 설정")]
    public string oreId; 
    public float maxHp = 100f;
    
    public NetworkVariable<float> currentHp = new NetworkVariable<float>(100f);

    public Vector2Int parentChunkCoord { get; private set; }
    public bool isDestroyed { get; private set; } = false;

    public override void OnNetworkSpawn()
    {
        if (IsServer) currentHp.Value = maxHp;
    }

    public void Initialize(Vector2Int coord, string id)
    {
        parentChunkCoord = coord;
        oreId = id;
    }

    // 로봇이나 플레이어가 채굴할 때 호출 (서버에서 실행)
    public void TakeDamage(float amount)
    {
        if (!IsServer || isDestroyed) return;

        currentHp.Value -= amount;
        PlayHitEffectClientRpc(); // 타격 효과 동기화

        if (currentHp.Value <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        isDestroyed = true;
        PlayDestroyEffectClientRpc(); // 파괴 효과 동기화

        // 🌟 매니저에게 파괴 보고 (예약 해제 및 리젠 시작)
        if (WorldManager.Instance != null)
        {
            WorldManager.Instance.OnOreDestroyed(this);
        }

        // 네트워크 상에서 소멸
        GetComponent<NetworkObject>().Despawn();
    }

    [ClientRpc]
    private void PlayHitEffectClientRpc()
    {
        // TODO: 광물이 흔들리거나 파편이 튀는 이펙트 재생
    }

    [ClientRpc]
    private void PlayDestroyEffectClientRpc()
    {
        // TODO: 펑 터지는 이펙트 및 아이템 드롭 로직
    }

}