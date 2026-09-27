using UnityEngine;
using Unity.Netcode; // 🌟 [멀티플레이 변경] Netcode 네임스페이스 추가

public class PlayerIconRotation : MonoBehaviour // MonoBehaviour 유지
{
    [Tooltip("멀티플레이 환경에서는 비워두면 자동으로 로컬 플레이어를 찾습니다.")]
    public Transform player;

    void LateUpdate()
    {
        // 🌟 [멀티플레이 변경] 플레이어가 할당되지 않았다면 로컬 플레이어를 자동 검색
        if (player == null)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
            {
                var localPlayerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
                if (localPlayerObj != null)
                {
                    player = localPlayerObj.transform;
                }
            }

            // 아직 로컬 플레이어가 스폰되지 않았다면 리턴
            if (player == null) return;
        }

        // 플레이어의 Y축 회전값 가져오기
        float playerRotation = player.eulerAngles.y;

        // UI 화살표 회전
        transform.rotation = Quaternion.Euler(
            0f,
            0f,
            -playerRotation
        );
    }
}