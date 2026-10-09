/*
    [ 코드 설명 ]

    자동소총의 발사를 담당하는 스크립트입니다.

    좌클릭 유지
    → 자동소총 연사

    1번 주무기를 들고 있을 때만 발사할 수 있습니다.

    좌클릭을 누르면 먼저 IsFiring 상태가 활성화되어
    TestPlayerController가 캐릭터 몸을
    카메라 / 크로스헤어 방향으로 회전시킵니다.

    캐릭터 몸 방향이 크로스헤어 방향과
    일정 각도 이내로 맞춰진 뒤에만 실제 총알을 발사합니다.

    따라서 캐릭터가 앞을 보고 있는데
    총알만 등 뒤로 발사되는 현상을 방지합니다.

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
    private TestPlayerController playerController;

    private Camera playerCamera;

    private float nextLocalFireTime;
    private float nextServerFireTime;

    public bool IsFiring { get; private set; }

    private float FireInterval =>
        60f / Mathf.Max(
            roundsPerMinute,
            1f
        );

    public override void OnNetworkSpawn()
    {
        weaponSlot =
            GetComponent<WeaponSlot>();

        gunAim =
            GetComponent<GunAim>();

        arRecoil =
            GetComponent<ARRecoil>();

        playerController =
            GetComponent<TestPlayerController>();

        if (IsOwner)
            playerCamera = Camera.main;
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        IsFiring = false;

        if (playerCamera == null)
            playerCamera = Camera.main;

        if (playerCamera == null ||
            weaponSlot == null ||
            gunAim == null ||
            muzzlePoint == null)
        {
            return;
        }

        // 현재 1번 슬롯만 자동소총
        if (weaponSlot.CurrentSlot != 1)
            return;

        // 게임 커서가 잠겨 있을 때만 발사
        if (Cursor.lockState !=
            CursorLockMode.Locked)
        {
            return;
        }

        // 좌클릭을 누르고 있는 동안
        // 캐릭터는 카메라 방향으로 회전
        IsFiring =
            Input.GetMouseButton(0);

        if (!IsFiring)
            return;

        // 캐릭터 몸이 아직 크로스헤어 방향을
        // 충분히 바라보고 있지 않으면
        // 몸만 회전시키고 총은 발사하지 않음
        if (playerController != null &&
            !playerController.CanFireTowardsCamera())
        {
            return;
        }

        // 연사 속도 제한
        if (Time.time <
            nextLocalFireTime)
        {
            return;
        }

        nextLocalFireTime =
            Time.time +
            FireInterval;

        Fire();
    }

    private void Fire()
    {
        // 자동소총 반동 적용
        arRecoil?.ApplyShot();

        float spread =
            gunAim.CurrentSpread;

        Quaternion spreadRotation =
            Quaternion.Euler(
                Random.Range(
                    -spread,
                    spread
                ),
                Random.Range(
                    -spread,
                    spread
                ),
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
        InvokePermission =
            RpcInvokePermission.Owner
    )]
    private void FireRequestRpc(
        Vector3 cameraOrigin,
        Vector3 cameraDirection)
    {
        if (weaponSlot == null)
        {
            weaponSlot =
                GetComponent<WeaponSlot>();
        }

        if (weaponSlot == null ||
            weaponSlot.CurrentSlot != 1 ||
            muzzlePoint == null)
        {
            return;
        }

        // 서버에서도 연사 속도 제한
        if (Time.time <
            nextServerFireTime)
        {
            return;
        }

        nextServerFireTime =
            Time.time +
            FireInterval;

        Vector3 muzzlePosition =
            muzzlePoint.position;

        Vector3 aimPoint =
            cameraOrigin +
            cameraDirection *
            maxDistance;

        // 카메라 기준 목표 지점 계산
        if (Physics.Raycast(
            cameraOrigin,
            cameraDirection,
            out RaycastHit cameraHit,
            maxDistance,
            hitMask,
            QueryTriggerInteraction.Ignore))
        {
            aimPoint =
                cameraHit.point;
        }

        Vector3 muzzleDirection =
            (aimPoint -
             muzzlePosition)
            .normalized;

        float muzzleDistance =
            Vector3.Distance(
                muzzlePosition,
                aimPoint
            );

        Vector3 finalPoint =
            aimPoint;

        // 총구 바로 앞에 벽 등이 있으면
        // 해당 위치에서 총알이 막히도록 처리
        if (Physics.Raycast(
            muzzlePosition,
            muzzleDirection,
            out RaycastHit muzzleHit,
            muzzleDistance,
            hitMask,
            QueryTriggerInteraction.Ignore))
        {
            finalPoint =
                muzzleHit.point;
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
            tracerObject
                .GetComponent<BulletTracer>();

        if (tracer != null)
        {
            tracer.Play(
                start,
                end
            );
        }
    }
}