/*
    [ 코드 설명 ]

    플레이어의 무기 슬롯 선택을 담당하는 스크립트입니다.

    1 → 1번 슬롯 장착
    2 → 2번 슬롯 장착
    3 → 3번 슬롯 장착
    X → 맨손

    같은 번호를 다시 누름
    → 현재 무기를 넣고 맨손으로 변경

    다른 번호를 누름
    → 기존 무기를 넣고 선택한 슬롯의 무기로 변경

    멀티플레이에서는 현재 슬롯 번호를 동기화하여
    다른 플레이어 화면에서도 같은 무기가 보이게 합니다.

    CurrentSlot
    → 다른 스크립트가 현재 장착 슬롯을 확인할 때 사용합니다.
*/

using UnityEngine;
using Unity.Netcode;

public class WeaponSlot : NetworkBehaviour
{
    [Header("Weapon Slots")]
    [SerializeField] private GameObject[] slots = new GameObject[4];

    // 0 = 맨손 / 1~3 = 무기 슬롯
    private NetworkVariable<int> currentSlot = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    public int CurrentSlot => currentSlot.Value;

    public override void OnNetworkSpawn()
    {
        currentSlot.OnValueChanged += OnSlotChanged;
        ApplySlot(currentSlot.Value);
    }

    public override void OnNetworkDespawn()
    {
        currentSlot.OnValueChanged -= OnSlotChanged;
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (Input.GetKeyDown(KeyCode.Alpha1))
            ToggleSlot(1);

        else if (Input.GetKeyDown(KeyCode.Alpha2))
            ToggleSlot(2);

        else if (Input.GetKeyDown(KeyCode.Alpha3))
            ToggleSlot(3);
            
        else if (Input.GetKeyDown(KeyCode.C))
            ToggleSlot(4);

        else if (Input.GetKeyDown(KeyCode.X))
            currentSlot.Value = 0;
    }

    private void ToggleSlot(int slot)
    {
        if (slots[slot - 1] == null)
            return;

        currentSlot.Value =
            currentSlot.Value == slot ? 0 : slot;
    }

    private void OnSlotChanged(int previousSlot, int newSlot)
    {
        ApplySlot(newSlot);
    }

    private void ApplySlot(int slot)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
                slots[i].SetActive(slot == i + 1);
        }
    }
}