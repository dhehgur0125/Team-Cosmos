/* [ 코드 설명 ] 길어진 HP 막대와 정사각형 아이콘 핫바를 갱신합니다. 4~6번 재선택에도 핫바를 표시하고, 장착 중 키 안내 없이 사용 진행과 획득 안내만 표시합니다. */
using UnityEngine;
using UnityEngine.UI;

namespace VoidLast.Player
{
    public sealed class FpsHud : MonoBehaviour
    {
        [SerializeField] Text healthLabel;
        [SerializeField] Image healthFill;
        [SerializeField] CanvasGroup hotbar;
        [SerializeField] Text[] slotLabels;
        [SerializeField] Image[] slotBorders;
        [SerializeField] Image[] slotIcons;
        [SerializeField] Sprite syringeIcon;
        [SerializeField, Min(1)] float healthBarWidth = 400;
        [SerializeField] Text contextLabel;
        [SerializeField, Min(0)] float visibleSeconds = 3;
        [SerializeField, Min(0.01f)] float fadeSeconds = 0.5f;
        FpsHealth health;
        FpsInventory inventory;
        FpsItemInteractor interactor;
        FpsEquipment equipment;
        float lastActivity;
        public float HotbarAlpha => hotbar.alpha;
        public string HealthText => healthLabel.text;
        void Awake() { health = GetComponent<FpsHealth>(); inventory = GetComponent<FpsInventory>(); interactor = GetComponent<FpsItemInteractor>(); equipment = GetComponent<FpsEquipment>(); }
        void OnEnable()
        {
            health.Changed += Refresh; inventory.Changed += Refresh; inventory.Activity += ShowHotbar;
            equipment.SlotSelected += OnSlotSelected;
            Refresh(); ShowHotbar();
        }
        void OnDisable() { health.Changed -= Refresh; inventory.Changed -= Refresh; inventory.Activity -= ShowHotbar; equipment.SlotSelected -= OnSlotSelected; }
        void Update()
        {
            float target = Time.unscaledTime - lastActivity <= visibleSeconds ? 1 : 0;
            hotbar.alpha = Mathf.MoveTowards(hotbar.alpha, target, Time.unscaledDeltaTime / fadeSeconds);
            contextLabel.text = !inventory.IsAlive ? "" : inventory.IsUsing ? (inventory.IsUsingOnOther ? "HEALING PLAYER  " : "HEALING SELF  ") + (inventory.UseProgress * inventory.UseDuration).ToString("F1") + " / " + inventory.UseDuration.ToString("F1") + " s"
                : interactor.Target != null ? inventory.IsFull ? "HOTBAR FULL" : "F  PICK UP SYRINGE"
                : "";
        }
        void Refresh()
        {
            healthLabel.text = "HP " + health.Current.ToString("0.#") + " / " + health.Maximum.ToString("0.#");
            healthFill.fillAmount = health.Current / health.Maximum;
            var size = healthFill.rectTransform.sizeDelta; size.x = healthBarWidth * health.Current / health.Maximum; healthFill.rectTransform.sizeDelta = size;
            for (int i = 0; i < FpsInventory.Capacity; i++)
            {
                slotLabels[i].text = (i + 4).ToString();
                if (slotIcons != null && i < slotIcons.Length)
                {
                    slotIcons[i].sprite = syringeIcon;
                    slotIcons[i].enabled = inventory.ItemAt(i) == FarmingItemKind.Syringe && syringeIcon != null;
                }
                slotBorders[i].color = inventory.SelectedIndex == i ? new Color(0.35f, 0.9f, 1) : new Color(0.35f, 0.4f, 0.45f);
            }
        }
        public void ShowHotbar() { lastActivity = Time.unscaledTime; hotbar.alpha = 1; }
        void OnSlotSelected(int slot) { Refresh(); if (slot >= 4 && slot <= 6) ShowHotbar(); }
    }
}
