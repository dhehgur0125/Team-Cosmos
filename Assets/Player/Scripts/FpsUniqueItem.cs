/* [ 코드 설명 ] 대원마다 하나씩 고르는 고유 아이템(스캔·무전·힐러·스턴)을 기억하고, 3번 키(3번 칸)에서 무엇을 꺼낼지 알려 줍니다.
   - 멀티플레이: 대기실에서 고른 값(UniqueItems.LocalChoice)을 내 대원이 네트워크로 알리고(FpsNetworkPlayer), 다른 대원 화면의 복제본도 같은 값을 받습니다.
   - 혼자 시험(층 씬 바로 Play): 고른 값이 없으면 예전처럼 구급상자가 켜져 있으면 힐러, 꺼져 있으면 스턴. Inspector의 kind로 바꿔 시험할 수 있습니다.
   - 아이템 효과는 각 컴포넌트(FpsMedicalKit, FpsFlashEmitter, FpsScannerItem, FpsRadioView, InGameVoiceChat)가 이 값을 보고 켜고 끕니다. */
using System;
using UnityEngine;

namespace VoidLast.Player
{
    // 네트워크로 번호가 오가므로 새 종류는 맨 뒤에만 추가한다
    public enum UniqueItemKind : byte { None, Scanner, Radio, Healer, Stun }

    /// <summary>고유 아이템 이름·대기실 선택값</summary>
    public static class UniqueItems
    {
        public static readonly UniqueItemKind[] Selectable = { UniqueItemKind.Scanner, UniqueItemKind.Radio, UniqueItemKind.Healer, UniqueItemKind.Stun };

        /// <summary>대기실에서 내가 고른 고유 아이템 (씬이 바뀌어도 남음)</summary>
        public static UniqueItemKind LocalChoice;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => LocalChoice = UniqueItemKind.None;

        public static string DisplayName(UniqueItemKind kind)
        {
            switch (kind)
            {
                case UniqueItemKind.Scanner: return "스캔";
                case UniqueItemKind.Radio: return "무전";
                case UniqueItemKind.Healer: return "힐러";
                case UniqueItemKind.Stun: return "스턴";
                default: return "없음";
            }
        }

        public static string Description(UniqueItemKind kind)
        {
            switch (kind)
            {
                case UniqueItemKind.Scanner: return "임무 물건·목표·고치 위치를 알려 줌 (눈 없는 몬스터가 몰려옴)";
                case UniqueItemKind.Radio: return "무전기 2개로 시작 · 하나는 동료에게 줄 수 있음";
                case UniqueItemKind.Healer: return "나와 동료의 체력 회복 · 3회";
                case UniqueItemKind.Stun: return "몬스터를 5초간 멈춤 · 3회";
                default: return "";
            }
        }

        /// <summary>대기실 세션에 저장하는 글자</summary>
        public static string Code(UniqueItemKind kind)
        {
            switch (kind)
            {
                case UniqueItemKind.Scanner: return "scan";
                case UniqueItemKind.Radio: return "radio";
                case UniqueItemKind.Healer: return "heal";
                case UniqueItemKind.Stun: return "stun";
                default: return "";
            }
        }

        public static UniqueItemKind Parse(string code)
        {
            foreach (var kind in Selectable) if (Code(kind) == code) return kind;
            return UniqueItemKind.None;
        }
    }

    [DisallowMultipleComponent]
    public sealed class FpsUniqueItem : MonoBehaviour
    {
        [Tooltip("혼자 시험할 때 쓸 고유 아이템. None이면 자동 (구급상자가 켜져 있으면 힐러, 아니면 스턴). 멀티플레이에서는 대기실에서 고른 값으로 바뀝니다.")]
        [SerializeField] UniqueItemKind kind = UniqueItemKind.None;
        bool assigned;
        FpsEquipment equipment;
        FpsInventory inventory;
        FpsMedicalKit medicalKit;
        FpsFlashEmitter flashEmitter;
        bool cached;

        public event Action Changed;

        /// <summary>이 대원의 고유 아이템</summary>
        public UniqueItemKind Kind
        {
            get
            {
                if (assigned || kind != UniqueItemKind.None) return kind;
                Cache();
                if (medicalKit != null && medicalKit.enabled) return UniqueItemKind.Healer;
                if (flashEmitter != null && flashEmitter.enabled) return UniqueItemKind.Stun;
                return UniqueItemKind.None;
            }
        }

        /// <summary>대기실 선택값 또는 네트워크로 받은 값을 적용한다 (None이면 3번 칸이 비어 있음)</summary>
        public void Assign(UniqueItemKind value)
        {
            bool changed = !assigned || kind != value;
            assigned = true; kind = value;
            if (changed) Changed?.Invoke();
        }

        /// <summary>3번 칸을 들고 있고 살아 있는지</summary>
        public bool SlotSelected
        {
            get
            {
                Cache();
                return equipment != null && equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Item && (inventory == null || inventory.IsAlive);
            }
        }

        public bool Has(UniqueItemKind value) => value != UniqueItemKind.None && Kind == value;
        /// <summary>3번 칸에서 이 고유 아이템을 손에 들고 있는지</summary>
        public bool InHand(UniqueItemKind value) => Has(value) && SlotSelected;

        void Cache()
        {
            if (cached) return;
            cached = true;
            equipment = GetComponent<FpsEquipment>(); inventory = GetComponent<FpsInventory>();
            medicalKit = GetComponent<FpsMedicalKit>(); flashEmitter = GetComponent<FpsFlashEmitter>();
        }

        /// <summary>3인칭 팔 자세용: 3번 칸에서 무전기·스캐너처럼 오른손에 드는 고유 아이템을 들고 있는지</summary>
        public static bool HoldsHandItem(FpsInventory inventory)
        {
            if (inventory == null) return false;
            var unique = inventory.GetComponent<FpsUniqueItem>();
            return unique != null && (unique.InHand(UniqueItemKind.Radio) || unique.InHand(UniqueItemKind.Scanner));
        }

        /// <summary>대원에게서 찾고, 없으면 붙인다 (프리팹을 고치지 않아도 되도록)</summary>
        public static FpsUniqueItem For(GameObject owner)
        {
            if (owner == null) return null;
            var unique = owner.GetComponent<FpsUniqueItem>();
            return unique != null ? unique : owner.AddComponent<FpsUniqueItem>();
        }
    }
}
