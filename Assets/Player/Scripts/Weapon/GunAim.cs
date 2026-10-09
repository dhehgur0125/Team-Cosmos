/*
    [ 코드 설명 ]

    주무기의 조준 상태를 담당하는 스크립트입니다.

    기본 상태
    → Hip 상태
    → 집탄률이 가장 낮음

    우클릭 유지
    → Shoulder 상태
    → 3인칭 견착
    → 집탄률 상승

    우클릭 빠르게 두 번
    → Precision 상태 진입
    → 정밀 조준
    → 집탄률이 가장 높음

    정밀 조준 상태에서 우클릭 한 번
    → 정밀 조준 즉시 해제
    → 원래 시점으로 복귀

    1번 주무기를 들고 있을 때만 조준할 수 있습니다.

    이 스크립트는 카메라를 직접 움직이지 않고
    현재 조준 상태와 집탄률만 관리합니다.
*/

using UnityEngine;
using Unity.Netcode;

public class GunAim : NetworkBehaviour
{
    public enum AimMode
    {
        Hip,
        Shoulder,
        Precision
    }

    [Header("Precision Aim")]
    [SerializeField] private float doubleClickTime = 0.3f;

    [Header("Accuracy")]
    [SerializeField] private float hipSpread = 3f;
    [SerializeField] private float shoulderSpread = 1f;
    [SerializeField] private float precisionSpread = 0.2f;

    private WeaponSlot weaponSlot;

    private float lastRightClickTime = -10f;
    private bool precisionAim = false;

    // 정밀 조준을 해제한 클릭이
    // 바로 견착으로 이어지는 것을 방지
    private bool waitForRightClickRelease = false;

    public AimMode CurrentMode { get; private set; } = AimMode.Hip;

    public float CurrentSpread
    {
        get
        {
            return CurrentMode switch
            {
                AimMode.Shoulder => shoulderSpread,
                AimMode.Precision => precisionSpread,
                _ => hipSpread
            };
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
            return;

        weaponSlot = GetComponent<WeaponSlot>();
    }

    private void Update()
    {

        if (!IsOwner || weaponSlot == null)
            return;

        // 1번 주무기를 들고 있지 않으면 조준 해제
        if (weaponSlot.CurrentSlot != 1)
        {
            ResetAim();
            return;
        }

        HandlePrecisionAim();

        // 정밀 조준 상태
        if (precisionAim)
        {
            CurrentMode = AimMode.Precision;
            return;
        }

        // 정밀 조준 해제 직후에는
        // 우클릭을 완전히 놓을 때까지 견착하지 않음
        if (waitForRightClickRelease)
        {
            CurrentMode = AimMode.Hip;

            if (!Input.GetMouseButton(1))
                waitForRightClickRelease = false;

            return;
        }

        // 일반 견착
        CurrentMode = Input.GetMouseButton(1)
            ? AimMode.Shoulder
            : AimMode.Hip;
    }

    private void HandlePrecisionAim()
    {
        if (!Input.GetMouseButtonDown(1))
            return;

        // 정밀 조준 중에는 우클릭 한 번으로 해제
        if (precisionAim)
        {
            precisionAim = false;
            waitForRightClickRelease = true;
            lastRightClickTime = -10f;

            return;
        }

        float currentTime = Time.unscaledTime;

        // 빠르게 두 번 눌렀으면 정밀 조준 진입
        if (currentTime - lastRightClickTime <= doubleClickTime)
        {
            precisionAim = true;
            lastRightClickTime = -10f;

            return;
        }

        // 첫 번째 우클릭 시간 저장
        lastRightClickTime = currentTime;
    }

    private void ResetAim()
    {
        precisionAim = false;
        waitForRightClickRelease = false;
        lastRightClickTime = -10f;
        CurrentMode = AimMode.Hip;
    }
}