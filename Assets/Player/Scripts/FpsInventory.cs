/* [ 코드 설명 ] 3칸 파밍 인벤토리와 홀딩 사용을 관리합니다. 좌클릭은 자신, 우클릭은 가까운 플레이어를 3초 뒤 회복하며 버튼 해제·슬롯 변경·사망·대상 이탈은 소비 없이 취소합니다. */
using System;
using UnityEngine;

namespace VoidLast.Player
{
    // 프리팹·씬에 번호로 저장되므로 새 종류는 맨 뒤에만 추가한다
    // 씬·프리팹에 번호로 저장되므로 새 종류는 맨 뒤에만 추가한다
    public enum FarmingItemKind { None, Syringe, Radio, Battery, QuarantineCard, Cutter, PowerModule, FuelCoupler, Scanner, AdminCard, ToolKit, PowerCell, AmmoPack }

    [DefaultExecutionOrder(-50)]
    public sealed class FpsInventory : MonoBehaviour
    {
        public const int Capacity = 3;
        [SerializeField, Min(0.1f)] float useDuration = 3;
        [SerializeField, Range(0, 1)] float syringeHealFraction = 0.3f;
        [Header("탄약 팩 (좌클릭 길게: 몸에 가져다 대고 예비 탄약 추가)")]
        [SerializeField, Min(0.1f)] float ammoPackUseDuration = 2;
        [SerializeField, Min(1)] int ammoPackRounds = 10;
        readonly FarmingItemKind[] items = new FarmingItemKind[Capacity];
        FpsEquipment equipment;
        FpsHealth health;
        FpsMotor motor;
        FpsInput input;
        FpsItemInteractor interactor;
        FpsItemView view;
        FpsHealth recipient;
        FpsPistol pistol;
        int usingIndex = -1;
        float elapsed;
        public event Action Changed;
        public event Action Activity;
        public int SelectedIndex => equipment == null ? -1 : (int)equipment.SelectedSlot >= 4 ? (int)equipment.SelectedSlot - 4 : -1;
        public bool IsUsing => usingIndex >= 0;
        public bool IsUsingOnOther => IsUsing && (IsNetworkProxy ? networkUsingOnOther : recipient != health);
        /// <summary>멀티플레이 복제본이면 입력 대신 네트워크 상태를 받는다 (아이템 획득·사용·드랍 불가).</summary>
        public bool IsNetworkProxy { get; set; }
        bool networkUsingOnOther;
        public bool HasItem(FarmingItemKind kind) => Array.IndexOf(items, kind) >= 0;
        public bool IsAlive => health != null && !health.IsDead && !motor.IsDead;
        public float UseProgress => IsUsing ? Mathf.Clamp01(elapsed / DurationFor(ItemAt(usingIndex))) : 0;
        /// <summary>지금 사용 중인(없으면 선택된) 아이템의 사용 시간</summary>
        public float UseDuration => DurationFor(IsUsing ? ItemAt(usingIndex) : ItemAt(SelectedIndex));
        /// <summary>사용 중인 아이템 종류 (사용 중이 아니면 None)</summary>
        public FarmingItemKind UsingItem => IsUsing ? ItemAt(usingIndex) : FarmingItemKind.None;
        float DurationFor(FarmingItemKind kind) => kind == FarmingItemKind.AmmoPack ? ammoPackUseDuration : useDuration;
        static bool IsUsable(FarmingItemKind kind) => kind == FarmingItemKind.Syringe || kind == FarmingItemKind.AmmoPack;
        public bool IsFull => Array.IndexOf(items, FarmingItemKind.None) < 0;
        public FarmingItemKind ItemAt(int index) => index >= 0 && index < Capacity ? items[index] : FarmingItemKind.None;

        void Awake()
        {
            equipment = GetComponent<FpsEquipment>(); health = GetComponent<FpsHealth>();
            input = GetComponent<FpsInput>(); motor = GetComponent<FpsMotor>();
            interactor = GetComponent<FpsItemInteractor>();
            view = GetComponent<FpsItemView>();
            pistol = GetComponent<FpsPistol>();
        }
        void OnEnable() { equipment.SlotChanged += SelectionChanged; health.Died += CancelUse; }
        void OnDisable() { equipment.SlotChanged -= SelectionChanged; health.Died -= CancelUse; CancelUse(); }
        void Update()
        {
            if (IsNetworkProxy) return;
            if (input.Slot >= 4 && input.Slot <= 6) ShowHotbar();
            if (input.Attack) TryStartUse();
            else if (input.AimPressed)
            {
                var target = interactor.FindUseTarget();
                if (target != null) TryStartUse(target);
            }
            TickUse(Time.deltaTime, input.AttackHeld, input.Aim);
        }
        /// <summary>복제본 전용: 주인의 핫바 내용과 사용 진행 상태를 반영한다.</summary>
        public void ApplyNetworkState(FarmingItemKind a, FarmingItemKind b, FarmingItemKind c, int usingSlot, float progress, bool onOther)
        {
            if (!IsNetworkProxy) return;
            bool changed = items[0] != a || items[1] != b || items[2] != c || usingIndex != usingSlot || networkUsingOnOther != onOther;
            items[0] = a; items[1] = b; items[2] = c;
            usingIndex = usingSlot >= 0 && usingSlot < Capacity ? usingSlot : -1;
            networkUsingOnOther = onOther;
            elapsed = usingIndex >= 0 ? Mathf.Clamp01(progress) * DurationFor(items[usingIndex]) : 0;
            if (changed) Changed?.Invoke();
        }
        /// <summary>층 이동 후: 이전 층의 핫바 내용을 그대로 이어받는다</summary>
        public void RestoreItems(FarmingItemKind[] list)
        {
            if (IsNetworkProxy || list == null) return;
            CancelUse();
            for (int i = 0; i < Capacity; i++) items[i] = i < list.Length ? list[i] : FarmingItemKind.None;
            Changed?.Invoke();
        }
        /// <summary>게임 시작 때 받는 물건을 정해진 칸에 넣는다 (무전 고유 아이템: 4번 칸에 동료에게 줄 무전기). 칸이 차 있으면 빈 칸에 넣는다</summary>
        public bool GiveStartingItem(int index, FarmingItemKind item)
        {
            if (IsNetworkProxy || item == FarmingItemKind.None) return false;
            if (index < 0 || index >= Capacity || items[index] != FarmingItemKind.None) index = Array.IndexOf(items, FarmingItemKind.None);
            if (index < 0) return false;
            items[index] = item;
            Changed?.Invoke(); ShowHotbar();
            return true;
        }
        public bool TryAdd(FarmingItemKind item)
        {
            if (IsNetworkProxy) return false;
            if (!IsAlive || item == FarmingItemKind.None) return false;
            if (FarmingItems.IsHeavy(item) && HasHeavyItem) return false;   // 무거운 물건은 한 번에 하나만
            int empty = Array.IndexOf(items, FarmingItemKind.None);
            if (empty < 0) return false;
            items[empty] = item;
            Changed?.Invoke(); ShowHotbar();
            return true;
        }
        /// <summary>무거운 물건(전력 셀)을 들고 있는지. 들고 있는 동안 달리기·사격을 할 수 없다 (FpsInput)</summary>
        public bool HasHeavyItem
        {
            get { foreach (var item in items) if (FarmingItems.IsHeavy(item)) return true; return false; }
        }
        /// <summary>멀티플레이: 같은 아이템을 다른 대원이 먼저 주웠다고 서버가 알려 오면 하나를 다시 뺀다.</summary>
        public bool RemoveOneByNetwork(FarmingItemKind kind)
        {
            int index = Array.LastIndexOf(items, kind);
            if (kind == FarmingItemKind.None || index < 0) return false;
            if (usingIndex == index) CancelUse();
            items[index] = FarmingItemKind.None;
            Changed?.Invoke(); ShowHotbar();
            return true;
        }
        public bool TryRemoveSelected(out FarmingItemKind item)
        {
            item = ItemAt(SelectedIndex);
            if (!IsAlive || IsUsing || item == FarmingItemKind.None) return false;
            items[SelectedIndex] = FarmingItemKind.None;
            Changed?.Invoke(); ShowHotbar();
            return true;
        }
        /// <summary>죽을 때 물건을 떨어뜨리려고 칸을 비운다 (살아 있는지 검사하지 않음)</summary>
        public void ClearSlot(int index)
        {
            if (index < 0 || index >= Capacity || items[index] == FarmingItemKind.None) return;
            if (usingIndex == index) CancelUse();
            items[index] = FarmingItemKind.None;
            Changed?.Invoke();
        }
        public bool TryStartUse(FpsHealth target = null)
        {
            target = target == null ? health : target;
            var kind = ItemAt(SelectedIndex);
            if (kind == FarmingItemKind.AmmoPack)
            {
                // Ammo goes into the user's own pistol only; it is held up and pressed to the body like the syringe.
                if (!IsAlive || IsUsing || target != health || pistol == null || equipment.IsSwitching) return false;
                recipient = health; usingIndex = SelectedIndex; elapsed = 0;
                Changed?.Invoke(); ShowHotbar();
                return true;
            }
            if (!IsAlive || IsUsing || kind != FarmingItemKind.Syringe || !target.CanHeal || view == null || !view.CanUse) return false;
            if (target != health && !interactor.CanReachPlayer(target)) return false;
            recipient = target;
            usingIndex = SelectedIndex; elapsed = 0;
            Changed?.Invoke(); ShowHotbar();
            return true;
        }
        public void TickUse(float deltaTime, bool selfHeld, bool otherHeld)
        {
            if (!IsUsing) return;
            bool onOther = recipient != health;
            if (!IsAlive || recipient == null || recipient.IsDead || usingIndex != SelectedIndex || !IsUsable(ItemAt(usingIndex)) ||
                (onOther ? !otherHeld || !interactor.CanReachPlayer(recipient) : !selfHeld)) { CancelUse(); return; }
            elapsed += Mathf.Max(0, deltaTime);
            if (elapsed < DurationFor(ItemAt(usingIndex))) return;
            int completedIndex = usingIndex;
            usingIndex = -1; elapsed = 0;
            if (items[completedIndex] == FarmingItemKind.AmmoPack)
            {
                pistol.AddReserve(ammoPackRounds);
                items[completedIndex] = FarmingItemKind.None;
                recipient = null;
                Changed?.Invoke(); ShowHotbar();
                return;
            }
            // A different healing source may fill HP during use; in that case keep the syringe.
            if (recipient.Heal(recipient.Maximum * syringeHealFraction)) items[completedIndex] = FarmingItemKind.None;
            recipient = null;
            Changed?.Invoke(); ShowHotbar();
        }
        public void CancelUse()
        {
            if (!IsUsing) return;
            usingIndex = -1; elapsed = 0;
            recipient = null;
            Changed?.Invoke(); ShowHotbar();
        }
        void SelectionChanged() { CancelUse(); Changed?.Invoke(); if (SelectedIndex >= 0) ShowHotbar(); }
        public void ShowHotbar() => Activity?.Invoke();
    }
}
