/*
    [ �ڵ� ���� ]

    �÷��̾��� ũ�ν���� ǥ�ø� ����ϴ� ��ũ��Ʈ�Դϴ�.

    �Ǽ�
    �� ũ�ν���� ����

    1�� �ֹ��� ����
    �� ũ�ν���� ǥ��

    �Ϲ� ���(Hip)
    �� ũ�ν��� �а� ������

    ����(Shoulder)
    �� ũ�ν��� ������

    ���� ����(Precision)
    �� ũ�ν��� ȭ�� �߾����� ���� ����

    �� ��ũ��Ʈ�� ���� �Է��̳� �� �߻縦 ó������ �ʰ�,
    ũ�ν������ ȭ�� ǥ�ø� ����մϴ�.
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

        // ����� 1�� �ֹ��⸦ ��� ���� ���� ũ�ν���� ǥ��
        bool hasWeapon = weaponSlot.CurrentSlot == 1 || weaponSlot.CurrentSlot == 4;

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