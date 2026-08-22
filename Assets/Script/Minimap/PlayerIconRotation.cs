using UnityEngine;

public class PlayerIconRotation : MonoBehaviour
{
    public Transform player;

    void LateUpdate()
    {
        if (player == null)
            return;

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