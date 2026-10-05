/*
    [ 코드 설명 ]

    자동소총의 발사를 담당하는 스크립트입니다.

    좌클릭 유지
    → 자동소총 연사

    1번 주무기를 들고 있을 때만 발사할 수 있습니다.

    현재 GunAim의 집탄률을 가져와
    Hip / Shoulder / Precision 상태에 따라
    총알이 퍼지는 정도를 다르게 적용합니다.

    실제 명중 판정은 서버에서 Raycast로 처리합니다.

    카메라 중앙을 기준으로 조준하지만,
    총알 궤적은 실제 MuzzlePoint에서 시작합니다.

    이 스크립트는 데미지나 피격 이펙트를 처리하지 않고
    발사와 명중 위치 계산만 담당합니다.
*/

using UnityEngine;
using Unity.Netcode;

public class GunFire : NetworkBehaviour
{
    [Header("Weapon")]
    [SerializeField] private Transform muzzlePoint;

    [Header("Fire Settings")]
    [SerializeField] private float roundsPerMinute = 600f;
    [SerializeField] private float maxDistance = 200f;
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("Tracer")]
    [SerializeField] private GameObject tracerPrefab;

    private WeaponSlot weaponSlot;
    private GunAim gunAim;
    private Camera playerCamera;

    private float nextLocalFireTime;
    private float nextServerFireTime;

    private float FireInterval =>
        60f / Mathf.Max(roundsPerMinute, 1f);

    public override void OnNetworkSpawn()
    {
        weaponSlot = GetComponent<WeaponSlot>();
        gunAim = GetComponent<GunAim>();

        if (IsOwner)
            playerCamera = Camera.main;
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        if (playerCamera == null)
            playerCamera = Camera.main;

        if (playerCamera == null ||
            weaponSlot == null ||
            gunAim == null ||
            muzzlePoint == null)
        {
            return;
        }

        // 현재는 1번 주무기 = 자동소총
        if (weaponSlot.CurrentSlot != 1)
            return;

        // 마우스가 게임 화면에 잡혀 있을 때만 발사
        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        if (Input.GetMouseButton(0) &&
            Time.time >= nextLocalFireTime)
        {
            nextLocalFireTime =
                Time.time + FireInterval;

            Fire();
        }
    }

    private void Fire()
    {
        float spread = gunAim.CurrentSpread;

        // 카메라 기준으로 상하좌우 오차를 적용
        Quaternion spreadRotation =
            Quaternion.Euler(
                Random.Range(-spread, spread),
                Random.Range(-spread, spread),
                0f
            );

        Vector3 fireDirection =
            playerCamera.transform.rotation *
            spreadRotation *
            Vector3.forward;

        FireRequestRpc(
            playerCamera.transform.position,
            fireDirection.normalized,
            muzzlePoint.position
        );
    }

    [Rpc(SendTo.Server, RequireOwnership = true)]
    private void FireRequestRpc(
        Vector3 cameraOrigin,
        Vector3 cameraDirection,
        Vector3 muzzlePosition)
    {
        // 서버에서도 현재 자동소총을 들고 있는지 확인
        if (weaponSlot == null)
            weaponSlot = GetComponent<WeaponSlot>();

        if (weaponSlot == null ||
            weaponSlot.CurrentSlot != 1)
        {
            return;
        }

        // 서버에서도 연사속도 제한
        if (Time.time < nextServerFireTime)
            return;

        nextServerFireTime =
            Time.time + FireInterval;

        Vector3 aimPoint =
            cameraOrigin +
            cameraDirection * maxDistance;

        // 1차 Raycast:
        // 카메라가 실제로 바라보는 지점 계산
        if (Physics.Raycast(
            cameraOrigin,
            cameraDirection,
            out RaycastHit cameraHit,
            maxDistance,
            hitMask,
            QueryTriggerInteraction.Ignore))
        {
            aimPoint = cameraHit.point;
        }

        Vector3 muzzleDirection =
            (aimPoint - muzzlePosition).normalized;

        float muzzleDistance =
            Vector3.Distance(
                muzzlePosition,
                aimPoint
            );

        Vector3 finalPoint = aimPoint;

        // 2차 Raycast:
        // 총구 앞에 벽이 있으면 벽을 뚫고 쏘지 못하게 함
        if (Physics.Raycast(
            muzzlePosition,
            muzzleDirection,
            out RaycastHit muzzleHit,
            muzzleDistance,
            hitMask,
            QueryTriggerInteraction.Ignore))
        {
            finalPoint = muzzleHit.point;
        }

        PlayShotRpc(
            muzzlePosition,
            finalPoint
        );
    }

    [Rpc(SendTo.Everyone)]
    private void PlayShotRpc(
        Vector3 start,
        Vector3 end)
    {
        if (tracerPrefab == null)
            return;

        GameObject tracerObject =
            Instantiate(
                tracerPrefab,
                Vector3.zero,
                Quaternion.identity
            );

        BulletTracer tracer =
            tracerObject.GetComponent<BulletTracer>();

        if (tracer != null)
            tracer.Play(start, end);
    }
}