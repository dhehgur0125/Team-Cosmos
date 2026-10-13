using UnityEngine;
using Unity.Netcode;

public class FlyingRobotMovement : NetworkBehaviour
{
    [Header("Target Settings")]
    public Transform target;          
    public float followSpeed = 5.0f;  

    [Header("Orbit & Hover Settings")]
    public float orbitDistance = 3.0f;
    public float orbitSpeed = 45.0f;  
    public float hoverHeight = 2.0f;  

    [Header("Visual Effects")]
    public float bobSpeed = 2.0f;     
    public float bobAmount = 0.3f;    

    private float currentAngle = 0f;

    void Update()
    {
        // 이동 연산은 오직 서버(호스트)에서만 전담
        if (!IsServer || target == null) return;

        Vector3 offset;

        // 1. 타겟이 플레이어인지 확인
        if (target.CompareTag("Player"))
        {
            // 플레이어 우측 뒤편에 고정
            offset = (target.right * orbitDistance) - (target.forward * (orbitDistance * 0.5f));
        }
        else
        {
            // 플레이어가 아니면 빙빙 도는 원운동
            currentAngle += orbitSpeed * Time.deltaTime;
            float rad = currentAngle * Mathf.Deg2Rad;
            offset = new Vector3(Mathf.Sin(rad), 0, Mathf.Cos(rad)) * orbitDistance;
        }

        // 2. 위아래 둥둥 떠다니는(Bobbing) 효과
        float bobbingOffset = Mathf.Sin(Time.time * bobSpeed) * bobAmount;

        // 3. 목표 위치 계산
        Vector3 desiredPosition = target.position + offset + Vector3.up * (hoverHeight + bobbingOffset);

        // 4. 이동 및 회전
        transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * followSpeed);
        transform.LookAt(target.position + Vector3.up * (hoverHeight * 0.5f));
    }
    
    // 타겟 지정 메서드
    public void SetTarget(Transform newTarget)
    {
        if(IsServer) target = newTarget;
    }
}