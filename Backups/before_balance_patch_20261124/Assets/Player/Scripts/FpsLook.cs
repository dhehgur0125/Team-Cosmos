/* [ 코드 설명 ] 독립된 FPS 카메라의 마우스 회전, 상하 제한, 웅크린 눈높이와 커서 잠금을 관리합니다. 뼈 애니메이션은 시점에 전달하지 않습니다. */
using UnityEngine;

namespace VoidLast.Player
{
    [DefaultExecutionOrder(-200)]
    public sealed class FpsLook : MonoBehaviour
    {
        [SerializeField] Transform cameraPivot;
        [SerializeField, Min(0)] float mouseSensitivity = 0.08f;
        [SerializeField] float minimumPitch = -80;
        [SerializeField] float maximumPitch = 80;
        [SerializeField] float standingEyeHeight = 1.67f;
        [SerializeField] float crouchingEyeHeight = 1.02f;
        [SerializeField, Min(0.01f)] float heightSmoothTime = 0.06f;
        FpsInput input;
        FpsMotor motor;
        float pitch, heightVelocity;

        void Awake() { input = GetComponent<FpsInput>(); motor = GetComponent<FpsMotor>(); }
        void OnEnable() => SetCursor(true);
        void OnDisable() => SetCursor(false);
        void OnApplicationFocus(bool focused) { if (!focused) SetCursor(false); }
        void Update()
        {
            if (input == null || cameraPivot == null) return;
            if (input.ReleaseCursor) SetCursor(false);
            else if (input.CaptureCursor && Cursor.lockState != CursorLockMode.Locked) SetCursor(true);
            if (Cursor.lockState == CursorLockMode.Locked && !motor.IsDead) Rotate(input.Look);
        }

        public void Rotate(Vector2 delta)
        {
            if (cameraPivot == null) return;
            transform.Rotate(0, delta.x * mouseSensitivity, 0, Space.Self);
            pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, minimumPitch, maximumPitch);
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0, 0);
        }

        void LateUpdate()
        {
            if (cameraPivot == null || motor == null) return;
            var position = cameraPivot.localPosition;
            float target = Mathf.Lerp(standingEyeHeight, crouchingEyeHeight, motor.CrouchFraction);
            position.y = Mathf.SmoothDamp(position.y, target, ref heightVelocity, heightSmoothTime);
            cameraPivot.localPosition = position;
        }

        static void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
