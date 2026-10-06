/*
    [ 코드 설명 ]

    자동소총의 발사를 담당하는 스크립트입니다.

    좌클릭 유지
    → 자동소총 연사

    1번 주무기를 들고 있을 때만 발사할 수 있습니다.

    GunAim의 현재 집탄률을 가져와
    일반 / 견착 / 정밀 조준에 따라
    탄 퍼짐 정도를 다르게 적용합니다.

    한 발 발사할 때마다 ARRecoil에 알려
    자동소총의 T자형 반동이 발생하도록 합니다.

    실제 명중 판정은 서버에서 Raycast로 처리하고,
    MuzzlePoint에서 명중 지점까지 Tracer를 표시합니다.

    이 스크립트는 데미지나 피격 파편은 처리하지 않습니다.
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
    private ARRecoil arRecoil;

    private Camera playerCamera;

    private float nextLocalFireTime;
    private float nextServerFireTime;

    private float FireInterval =>
        60f / Mathf.Max(roundsPerMinute, 1f);

    public override void OnNetworkSpawn()
    {
        weaponSlot = GetComponent<WeaponSlot>();
        gunAim = GetComponent<GunAim>();
        arRecoil = GetComponent<ARRecoil>();

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

        // 현재 1번 슬롯만 자동소총으로 사용
        if (weaponSlot.CurrentSlot != 1)
            return;

        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        // 좌클릭을 누르고 있으면 자동 연사
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
        // 자동소총 반동 적용
        arRecoil?.ApplyShot();

        float spread = gunAim.CurrentSpread;

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
            fireDirection.normalized
        );
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Owner
    )]
    private void FireRequestRpc(
        Vector3 cameraOrigin,
        Vector3 cameraDirection)
    {
        if (weaponSlot == null)
            weaponSlot = GetComponent<WeaponSlot>();

        if (weaponSlot == null ||
            weaponSlot.CurrentSlot != 1 ||
            muzzlePoint == null)
        {
            return;
        }

        // 서버에서도 연사속도 제한
        if (Time.time < nextServerFireTime)
            return;

        nextServerFireTime =
            Time.time + FireInterval;

        Vector3 muzzlePosition =
            muzzlePoint.position;

        Vector3 aimPoint =
            cameraOrigin +
            cameraDirection * maxDistance;

        // 카메라 기준 목표 지점 계산
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

        // 총구 앞에 장애물이 있으면 그곳에 명중
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

        PlayTracerRpc(
            muzzlePosition,
            finalPoint
        );
    }

    [Rpc(SendTo.Everyone)]
    private void PlayTracerRpc(
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