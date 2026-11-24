/* [ 코드 설명 ] Monster_test 플레이어 인스턴스에서 기존 근접 공격 이벤트를 받아 짧은 준비 뒤 실제 전방 명중을 검사합니다. 피해는 밸런스 확정값이 아닌 테스트용이며 원본 FPS 프리팹은 변경하지 않습니다. */
using UnityEngine;
using VoidLast.Player;

namespace VoidLast.Monster
{
    public sealed class MonsterMeleeTestBridge : MonoBehaviour
    {
        [SerializeField, Tooltip("Prototype only; large-monster melee kill count is not decided.")] float prototypeMeleeDamage = 20;
        [SerializeField] float reach = 1.8f;
        [SerializeField] float hitDelay = 0.25f;
        [SerializeField] float interval = 1.2f;
        FpsEquipment equipment;
        FpsHealth health;
        Camera view;
        float nextAttack, strikeAt;
        bool pending;
        void Awake() { equipment = GetComponent<FpsEquipment>(); health = GetComponent<FpsHealth>(); view = GetComponentInChildren<Camera>(); }
        void OnEnable() => equipment.MeleeRequested += Begin;
        void OnDisable() { equipment.MeleeRequested -= Begin; pending = false; }
        void Begin() { if (health.IsDead || Time.time < nextAttack) return; pending = true; strikeAt = Time.time + hitDelay; nextAttack = Time.time + interval; }
        void Update()
        {
            if (!pending) return;
            if (health.IsDead || equipment.SelectedSlot != FpsEquipment.EquipmentSlot.Melee) { pending = false; return; }
            if (Time.time < strikeAt) return;
            pending = false; var ray = view.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            RaycastHit nearest = default; float distance = float.PositiveInfinity;
            foreach (var hit in Physics.SphereCastAll(ray, 0.15f, reach, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(transform) && hit.distance < distance) { nearest = hit; distance = hit.distance; }
            if (nearest.collider != null) nearest.collider.GetComponentInParent<MonsterHealth>()?.TakeHit(prototypeMeleeDamage, gameObject, nearest.point);
        }
    }
}
