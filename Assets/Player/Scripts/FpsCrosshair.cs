/* [ 코드 설명 ] 기존 FPS HUD의 중앙 점을 권총 장착 시 네 방향 크로스헤어로 표시합니다. FpsMotor의 실제 이동·공중 상태로 간격만 보간하고 조준·원격 플레이어에서는 숨깁니다. 입력과 사격 판정은 변경하지 않습니다. */
using UnityEngine;
using UnityEngine.UI;

namespace VoidLast.Player
{
    [DisallowMultipleComponent, RequireComponent(typeof(Image))]
    public sealed class FpsCrosshair : MonoBehaviour
    {
        [Header("상태별 간격 (Canvas 기준, 중앙에서 선 안쪽까지)")]
        [SerializeField, Min(0)] float idleGap = 6;
        [SerializeField, Min(0)] float walkGap = 10;
        [SerializeField, Min(0)] float runGap = 16;
        [SerializeField, Min(0)] float crouchGap = 3;
        [SerializeField, Min(0)] float airborneGap = 22;
        [Header("선 크기와 전환")]
        [SerializeField, Min(1)] float lineLength = 8;
        [SerializeField, Min(1)] float lineThickness = 2;
        [SerializeField, Min(0.1f)] float transitionSpeed = 12;

        readonly Image[] lines = new Image[4];
        static readonly Vector2[] Directions = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        Image dot;
        FpsHud hud;
        FpsMotor motor;
        FpsEquipment equipment;
        Camera viewCamera;
        float gap;

        void Awake()
        {
            dot = GetComponent<Image>();
            hud = GetComponentInParent<FpsHud>();
            motor = GetComponentInParent<FpsMotor>();
            equipment = GetComponentInParent<FpsEquipment>();
            if (motor != null) viewCamera = motor.GetComponentInChildren<Camera>(true);
            // Use the existing centered Canvas and dot; no extra Canvas or input handler.
            var rect = dot.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = Vector2.zero;
            dot.raycastTarget = false;
            for (int i = 0; i < lines.Length; i++)
            {
                var go = new GameObject("Crosshair " + new[] { "Up", "Down", "Left", "Right" }[i], typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(transform, false);
                lines[i] = go.GetComponent<Image>();
                lines[i].raycastTarget = false;
                lines[i].rectTransform.anchorMin = lines[i].rectTransform.anchorMax = Vector2.one * 0.5f;
            }
        }

        void OnEnable() { gap = idleGap; SetVisible(false, false); }
        void OnDisable() => SetVisible(false, false);

        void LateUpdate()
        {
            bool local = hud != null && hud.isActiveAndEnabled && motor != null && !motor.IsNetworkProxy && !motor.IsDead
                && equipment != null && !equipment.IsNetworkProxy && viewCamera != null && viewCamera.isActiveAndEnabled;
            bool pistol = equipment != null && equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Pistol;
            bool visible = local && !equipment.IsAiming;
            float target = motor == null ? idleGap : motor.IsAirborne ? airborneGap : motor.IsCrouching ? crouchGap
                : motor.LocalVelocity.sqrMagnitude < 0.01f ? idleGap : motor.IsRunning ? runGap : walkGap;
            gap = Mathf.Lerp(gap, target, 1 - Mathf.Exp(-transitionSpeed * Time.unscaledDeltaTime));
            for (int i = 0; i < lines.Length; i++)
            {
                var rect = lines[i].rectTransform;
                rect.sizeDelta = i < 2 ? new Vector2(lineThickness, lineLength) : new Vector2(lineLength, lineThickness);
                rect.anchoredPosition = Directions[i] * (gap + lineLength * 0.5f);
                lines[i].color = dot.color;
            }
            SetVisible(visible && pistol, visible && !pistol);
        }

        void SetVisible(bool cross, bool center)
        {
            if (dot != null) dot.enabled = center;
            foreach (var line in lines) if (line != null) line.enabled = cross;
        }
    }
}
