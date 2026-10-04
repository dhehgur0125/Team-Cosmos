/*
    [ 코드 설명 ]

    플레이어의 크로스헤어 표시를 담당하는 스크립트입니다.

    맨손
    → 크로스헤어 숨김

    1번 주무기 장착
    → 크로스헤어 표시

    일반 사격(Hip)
    → 크로스헤어가 넓게 벌어짐

    견착(Shoulder)
    → 크로스헤어가 좁아짐

    정밀 조준(Precision)
    → 크로스헤어가 화면 중앙으로 거의 모임

    이 스크립트는 조준 입력이나 총 발사를 처리하지 않고,
    크로스헤어의 화면 표시만 담당합니다.
*/

using UnityEngine;
using Unity.Netcode;

public class CrosshairUI : MonoBehaviour
{
    [Header("Crosshair Parts")]
    [SerializeField] private RectTransform top;
    [SerializeField] private RectTransform bottom;
    [SerializeField] private RectTransform left;
    [SerializeField] private RectTransform right;

    [Header("Crosshair Gap")]
    [SerializeField] private float hipGap = 24f;
    [SerializeField] private float shoulderGap = 10f;
    [SerializeField] private float precisionGap = 2f;

    [Header("Transition")]
    [SerializeField] private float moveSpeed = 15f;

    private WeaponSlot weaponSlot;
    private GunAim gunAim;

    private float currentGap;

    private void Start()
    {
        currentGap = hipGap;
        SetVisible(false);
    }

    private void Update()
    {
        FindLocalPlayer();

        if (weaponSlot == null || gunAim == null)
        {
            SetVisible(false);
            return;
        }

        // 현재는 1번 주무기를 들고 있을 때만 크로스헤어 표시
        bool hasWeapon = weaponSlot.CurrentSlot == 1;

        SetVisible(hasWeapon);

        if (!hasWeapon)
            return;

        float targetGap = GetTargetGap();

        currentGap = Mathf.Lerp(
            currentGap,
            targetGap,
            moveSpeed * Time.deltaTime
        );

        UpdateCrosshairPosition();
    }

    private void FindLocalPlayer()
    {
        if (weaponSlot != null && gunAim != null)
            return;

        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsConnectedClient)
        {
            return;
        }

        NetworkObject player =
            NetworkManager.Singleton.LocalClient.PlayerObject;

        if (player == null)
            return;

        weaponSlot = player.GetComponent<WeaponSlot>();
        gunAim = player.GetComponent<GunAim>();
    }

    private float GetTargetGap()
    {
        return gunAim.CurrentMode switch
        {
            GunAim.AimMode.Shoulder => shoulderGap,
            GunAim.AimMode.Precision => precisionGap,
            _ => hipGap
        };
    }

    private void UpdateCrosshairPosition()
    {
        top.anchoredPosition =
            new Vector2(0f, currentGap);

        bottom.anchoredPosition =
            new Vector2(0f, -currentGap);

        left.anchoredPosition =
            new Vector2(-currentGap, 0f);

        right.anchoredPosition =
            new Vector2(currentGap, 0f);
    }

    private void SetVisible(bool visible)
    {
        top.gameObject.SetActive(visible);
        bottom.gameObject.SetActive(visible);
        left.gameObject.SetActive(visible);
        right.gameObject.SetActive(visible);
    }
}