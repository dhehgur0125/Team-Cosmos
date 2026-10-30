/*
    [ 코드 설명 ]

    자동소총(AR)의 T자형 반동을 담당하는 스크립트입니다.

    연사 초반
    → 설정된 발 수 동안 위쪽으로 일정하게 상승
    → 마지막 수직 반동 발에서 최대 높이에 도달

    최대 높이 도달 이후
    → 높이가 완전히 고정되지 않고 조금씩 위아래로 흔들림
    → 동시에 좌우로 계속 이동하며 T자형 스프레이를 생성
    → 연사를 계속하는 동안 반동이 멈추지 않음

    발사를 멈추면
    → 짧은 딜레이 후 빠르게 원래 조준 위치로 복귀
    → 복귀가 끝나면 다음 연사의 반동 패턴을 처음부터 시작

    견착 / 정밀 조준
    → 일반 사격보다 반동 크기가 감소함

    이 스크립트는 총 발사 자체를 처리하지 않고
    자동소총의 반동 계산만 담당합니다.
*/

using UnityEngine;
using Unity.Netcode;

public class ARRecoil : NetworkBehaviour
{
    [Header("Vertical Recoil")]
    [SerializeField] private int verticalShotCount = 5;
    [SerializeField] private float maxVerticalRecoil = 15f;

    [Header("T Spray")]
    [SerializeField] private float horizontalStep = 2f;
    [SerializeField] private float maxHorizontalRecoil = 7f;

    [Tooltip("T자 상단에서 연사 중 발생하는 작은 수직 흔들림")]
    [SerializeField] private float verticalSprayJitter = 1.2f;

    [Tooltip("좌우 움직임이 너무 기계적으로 보이지 않도록 추가하는 작은 흔들림")]
    [SerializeField] private float horizontalJitter = 0.25f;

    [Header("Aim Recoil")]
    [SerializeField] private float shoulderMultiplier = 0.7f;
    [SerializeField] private float precisionMultiplier = 0.4f;

    [Header("Recoil Movement")]
    [SerializeField] private float recoilMoveSpeed = 60f;

    [Header("Recovery")]
    [SerializeField] private float returnDelay = 0.08f;
    [SerializeField] private float returnSpeed = 50f;

    private GunAim gunAim;

    private Vector2 targetRecoil;
    private Vector2 currentRecoil;

    private int shotCount;
    private int horizontalDirection = 1;

    private float lastShotTime = -10f;

    public float PitchOffset => currentRecoil.x;
    public float YawOffset => currentRecoil.y;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
            return;

        gunAim = GetComponent<GunAim>();
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        bool shouldReturn =
            Time.time - lastShotTime > returnDelay;

        if (shouldReturn)
        {
            ReturnRecoil();
            return;
        }

        // 발사 중에는 새로운 반동 위치를 빠르게 따라감
        currentRecoil = Vector2.MoveTowards(
            currentRecoil,
            targetRecoil,
            recoilMoveSpeed * Time.deltaTime
        );
    }

    public void ApplyShot()
    {
        if (!IsOwner)
            return;

        lastShotTime = Time.time;
        shotCount++;

        float multiplier = GetAimMultiplier();

        // -----------------------------
        // 1. 초반 수직 반동
        // -----------------------------
        if (shotCount <= verticalShotCount)
        {
            float progress =
                (float)shotCount /
                Mathf.Max(verticalShotCount, 1);

            targetRecoil.x =
                maxVerticalRecoil *
                progress *
                multiplier;

            // T자의 세로 부분에서는 좌우 반동 거의 없음
            targetRecoil.y = 0f;

            return;
        }

        // -----------------------------
        // 2. T자 상단 스프레이
        // -----------------------------

        // 최대 높이에 도달한 이후에도
        // 높이를 완전히 고정하지 않고 조금씩 흔듦
        float verticalJitter =
            Random.Range(
                -verticalSprayJitter,
                verticalSprayJitter
            );

        targetRecoil.x =
            (maxVerticalRecoil + verticalJitter) *
            multiplier;

        // 기본 좌우 이동
        float horizontalMovement =
            horizontalStep *
            horizontalDirection;

        // 약간의 랜덤 흔들림 추가
        horizontalMovement +=
            Random.Range(
                -horizontalJitter,
                horizontalJitter
            );

        targetRecoil.y +=
            horizontalMovement *
            multiplier;

        float horizontalLimit =
            maxHorizontalRecoil *
            multiplier;

        // 오른쪽 끝에 도달하면 왼쪽으로 전환
        if (targetRecoil.y >= horizontalLimit)
        {
            targetRecoil.y = horizontalLimit;
            horizontalDirection = -1;
        }

        // 왼쪽 끝에 도달하면 오른쪽으로 전환
        else if (targetRecoil.y <= -horizontalLimit)
        {
            targetRecoil.y = -horizontalLimit;
            horizontalDirection = 1;
        }
    }

    private void ReturnRecoil()
    {
        // 목표 반동을 빠르게 원점으로 이동
        targetRecoil = Vector2.MoveTowards(
            targetRecoil,
            Vector2.zero,
            returnSpeed * Time.deltaTime
        );

        // 실제 카메라 반동도 빠르게 복귀
        currentRecoil = Vector2.MoveTowards(
            currentRecoil,
            targetRecoil,
            returnSpeed * Time.deltaTime
        );

        // 완전히 돌아오면 다음 연사를 위해 초기화
        if (targetRecoil.sqrMagnitude < 0.0001f &&
            currentRecoil.sqrMagnitude < 0.0001f)
        {
            targetRecoil = Vector2.zero;
            currentRecoil = Vector2.zero;

            shotCount = 0;
            horizontalDirection = 1;
        }
    }

    private float GetAimMultiplier()
    {
        if (gunAim == null)
            return 1f;

        return gunAim.CurrentMode switch
        {
            GunAim.AimMode.Shoulder
                => shoulderMultiplier,

            GunAim.AimMode.Precision
                => precisionMultiplier,

            _ => 1f
        };
    }
}