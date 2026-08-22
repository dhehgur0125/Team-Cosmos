using UnityEngine;

public class MinimapCameraFollow : MonoBehaviour
{
    public Transform player;

    // 플레이어와 미니맵 카메라의 높이 차이
    public float height = 5000f;

    void LateUpdate()
    {
        if (player == null)
            return;

        transform.position = new Vector3(
            player.position.x,
            height,
            player.position.z
        );

        // 카메라 회전은 고정
        transform.rotation = Quaternion.Euler(
            90f,
            0f,
            0f
        );
    }
}