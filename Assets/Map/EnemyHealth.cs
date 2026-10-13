using UnityEngine;
using Unity.Netcode;

public class EnemyHealth : NetworkBehaviour
{
    [Header("Health Settings")]
    public int maxHp = 30;
    
    // 네트워크로 동기화되는 체력 변수
    private NetworkVariable<int> currentHp = new NetworkVariable<int>(
        30, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server // 오직 서버(호스트)만 체력을 변경할 수 있음
    );

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            currentHp.Value = maxHp;
        }
    }

    // 데미지 처리 함수 (로봇이 호출함)
    public void TakeDamage(int damage)
    {
        // 방어 코드: 서버가 아니거나 이미 죽었다면 무시
        if (!IsServer || currentHp.Value <= 0) return;

        // 체력 감소
        currentHp.Value -= damage;
        Debug.Log($"적 적중! 남은 체력: {currentHp.Value}");

        // 체력이 0 이하가 되면 파괴(Despawn)
        if (currentHp.Value <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        if (!IsServer) return;

        // NGO 환경에서 네트워크 오브젝트를 삭제하는 올바른 방법
        NetworkObject netObj = GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
        {
            netObj.Despawn(true); // true는 씬에서 GameObject 자체도 Destroy함을 의미
        }
        else
        {
            Destroy(gameObject);
        }
    }
}