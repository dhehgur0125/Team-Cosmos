using UnityEngine;
using Unity.Netcode;

public class FlyingRobotCombat : NetworkBehaviour
{
    [Header("Weapon")]
    [SerializeField] private Transform muzzlePoint;

    [Header("Fire Settings")]
    [SerializeField] private float roundsPerMinute = 300f; 
    [SerializeField] private float attackRange = 15f;      
    [SerializeField] private int attackDamage = 10;        
    [SerializeField] private LayerMask hitMask = ~0;       

    [Header("Aiming Settings")]
    [SerializeField] private float aimHeightOffset = 0.5f; // [핵심] 조준점 높이 조절 (머리 위로 쏘면 이 값을 줄이거나 0으로 만드세요)

    [Header("Tracer")]
    [SerializeField] private GameObject tracerPrefab;

    private float nextServerFireTime;

    private float FireInterval => 
        60f / Mathf.Max(roundsPerMinute, 1f);

    private void Update()
    {
        // 오직 서버에서만 탐지 및 공격 실행
        if (!IsServer || muzzlePoint == null) return;

        // 쿨다운 체크
        if (Time.time < nextServerFireTime) return;

        // 사거리 내의 가장 가까운 적 탐색
        Transform targetEnemy = FindClosestEnemy();

        if (targetEnemy != null)
        {
            Fire(targetEnemy);
            nextServerFireTime = Time.time + FireInterval;
        }
    }

    private Transform FindClosestEnemy()
    {
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, attackRange);
        Transform closest = null;
        float minDistance = Mathf.Infinity;

        foreach (var hit in hitColliders)
        {
            if (hit.CompareTag("Enemy"))
            {
                float distance = Vector3.Distance(transform.position, hit.transform.position);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closest = hit.transform;
                }
            }
        }
        return closest;
    }

    private void Fire(Transform target)
    {
        Vector3 muzzlePosition = muzzlePoint.position;
        
        // 조준점 보정 (aimHeightOffset 값으로 인스펙터에서 높이 조절 가능)
        Vector3 aimPoint = target.position + (Vector3.up * aimHeightOffset); 
        
        Vector3 fireDirection = (aimPoint - muzzlePosition).normalized;
        Vector3 finalPoint = aimPoint;

        // 총구에서 조준점을 향해 레이캐스트 발사
        if (Physics.Raycast(muzzlePosition, fireDirection, out RaycastHit hit, attackRange, hitMask, QueryTriggerInteraction.Ignore))
        {
            finalPoint = hit.point;

            // 맞은 대상이 "Enemy" 태그를 가졌다면
            if (hit.collider.CompareTag("Enemy"))
            {
                // 콜라이더가 자식 오브젝트에 있을 경우를 대비해 부모까지 검색하여 EnemyHealth 찾기
                EnemyHealth enemyHealth = hit.collider.GetComponentInParent<EnemyHealth>();
                if (enemyHealth != null)
                {
                    enemyHealth.TakeDamage(attackDamage);
                }
            }
        }
        else
        {
            finalPoint = muzzlePosition + fireDirection * attackRange;
        }

        // 트레이서 이펙트 실행
        PlayTracerRpc(muzzlePosition, finalPoint);
    }

    [Rpc(SendTo.Everyone)]
    private void PlayTracerRpc(Vector3 start, Vector3 end)
    {
        if (tracerPrefab == null) return;

        GameObject tracerObject = Instantiate(tracerPrefab, Vector3.zero, Quaternion.identity);
        BulletTracer tracer = tracerObject.GetComponent<BulletTracer>();

        if (tracer != null)
        {
            tracer.Play(start, end);
        }
    }
}