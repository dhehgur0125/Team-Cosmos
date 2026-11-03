/* [ 코드 설명 ] 기존 Input System에서 FPS 조작을 읽습니다. 입력은 다른 역할과 분리하며 커서 해제 시 게임 입력을 차단합니다. */
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VoidLast.Player
{
    [DefaultExecutionOrder(-300)]
    public sealed class FpsInput : MonoBehaviour
    {
        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool Sprint { get; private set; }
        public bool Crouch { get; private set; }
        public bool Aim { get; private set; }
        public bool Attack { get; private set; }
        public bool AttackHeld { get; private set; }
        public bool AimPressed { get; private set; }
        public bool Jump { get; private set; }
        public bool Interact { get; private set; }
        public bool Drop { get; private set; }
        public bool Reload { get; private set; }
        public bool HalveHealth { get; private set; }
        public int Slot { get; private set; }
        public bool ReleaseCursor { get; private set; }
        public bool CaptureCursor { get; private set; }

        void Update()
        {
            Clear();
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            ReleaseCursor = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            CaptureCursor = mouse != null && mouse.leftButton.wasPressedThisFrame;
            if (Cursor.lockState != CursorLockMode.Locked || !Application.isFocused) return;
            if (keyboard != null)
            {
                Move = Vector2.ClampMagnitude(new Vector2(
                    (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0)), 1);
                Sprint = keyboard.leftShiftKey.isPressed;
                Crouch = keyboard.leftCtrlKey.isPressed;
                Jump = keyboard.spaceKey.wasPressedThisFrame;
                Interact = keyboard.fKey.wasPressedThisFrame;
                Drop = keyboard.qKey.wasPressedThisFrame;
                Reload = keyboard.rKey.wasPressedThisFrame;
                HalveHealth = keyboard.hKey.wasPressedThisFrame;
                Slot = (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) ? 1 : (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) ? 2 : (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) ? 3 : (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) ? 4 : (keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame) ? 5 : (keyboard.digit6Key.wasPressedThisFrame || keyboard.numpad6Key.wasPressedThisFrame) ? 6 : 0;
            }
            if (mouse != null)
            {
                Look = mouse.delta.ReadValue();
                Aim = mouse.rightButton.isPressed;
                Attack = mouse.leftButton.wasPressedThisFrame;
                AttackHeld = mouse.leftButton.isPressed;
                AimPressed = mouse.rightButton.wasPressedThisFrame;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            ReleaseCursor = Input.GetKeyDown(KeyCode.Escape);
            CaptureCursor = Input.GetMouseButtonDown(0);
            if (Cursor.lockState != CursorLockMode.Locked || !Application.isFocused) return;
            Move = Vector2.ClampMagnitude(new Vector2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0), (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0)), 1);
            Look = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
            Sprint = Input.GetKey(KeyCode.LeftShift);
            Crouch = Input.GetKey(KeyCode.LeftControl);
            Aim = Input.GetMouseButton(1);
            Attack = Input.GetMouseButtonDown(0);
            AttackHeld = Input.GetMouseButton(0);
            AimPressed = Input.GetMouseButtonDown(1);
            Jump = Input.GetKeyDown(KeyCode.Space);
            Interact = Input.GetKeyDown(KeyCode.F);
            Drop = Input.GetKeyDown(KeyCode.Q);
            Reload = Input.GetKeyDown(KeyCode.R);
            HalveHealth = Input.GetKeyDown(KeyCode.H);
            Slot = (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) ? 1 : (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) ? 2 : (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) ? 3 : (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) ? 4 : (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) ? 5 : (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6)) ? 6 : 0;
#endif
        }

        void OnDisable() => Clear();
        void Clear()
        {
            Move = Look = Vector2.zero;
            Sprint = Crouch = Aim = Attack = Jump = ReleaseCursor = CaptureCursor = false;
            Interact = Drop = Reload = HalveHealth = false;
            AttackHeld = AimPressed = false;
            Slot = 0;
        }
    }
}
