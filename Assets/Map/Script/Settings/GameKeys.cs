using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>키 설정에서 바꿀 수 있는 기능</summary>
public enum GameAction
{
    MoveForward, MoveBack, MoveLeft, MoveRight,
    Sprint, Crouch, Jump,
    Interact, Drop, Reload, Flashlight,
    Radio, MicMute,
}

/// <summary>
/// 키 설정. 기능마다 키 하나를 PlayerPrefs("VL_Key_기능")에 저장한다.
/// 게임 코드는 Keyboard.current의 특정 키 대신 GameKeys.Held/Down(기능)으로 읽는다.
/// 바꿀 수 없는 키: ESC(메뉴), 1~6(칸 번호), H(시험용), 마우스 버튼(사격·조준).
/// </summary>
public static class GameKeys
{
    public static readonly GameAction[] All = (GameAction[])Enum.GetValues(typeof(GameAction));

    static readonly Key[] Defaults =
    {
        Key.W, Key.S, Key.A, Key.D,
        Key.LeftShift, Key.LeftCtrl, Key.Space,
        Key.F, Key.Q, Key.R, Key.E,
        Key.V, Key.M,
    };

    static Key[] _keys;
    public static event Action Changed;

    /// <summary>키 설정 창이 새 키를 기다리는 중 (이때 ESC는 '취소'로만 쓰인다)</summary>
    public static bool Listening;

    static Key[] Keys
    {
        get
        {
            if (_keys != null) return _keys;
            _keys = new Key[All.Length];
            for (int i = 0; i < All.Length; i++)
            {
                string saved = PlayerPrefs.GetString(PrefKey(All[i]), "");
                _keys[i] = !string.IsNullOrEmpty(saved) && Enum.TryParse(saved, out Key k) && IsAllowed(k) ? k : Defaults[i];
            }
            return _keys;
        }
    }

    static string PrefKey(GameAction a) => "VL_Key_" + a;

    public static Key Get(GameAction a) => Keys[(int)a];
    public static Key Default(GameAction a) => Defaults[(int)a];

    public static bool Held(GameAction a)
    {
        var kb = Keyboard.current;
        return kb != null && kb[Get(a)].isPressed;
    }

    public static bool Down(GameAction a)
    {
        var kb = Keyboard.current;
        return kb != null && kb[Get(a)].wasPressedThisFrame;
    }

    /// <summary>키를 바꾼다. 다른 기능이 이미 쓰던 키면 두 기능의 키를 서로 바꾼다. 바뀐 상대 기능을 돌려준다(없으면 null).</summary>
    public static GameAction? Set(GameAction a, Key key)
    {
        if (!IsAllowed(key)) return null;
        var keys = Keys;
        GameAction? swapped = null;
        Key old = keys[(int)a];
        for (int i = 0; i < keys.Length; i++)
            if (i != (int)a && keys[i] == key) { keys[i] = old; swapped = All[i]; PlayerPrefs.SetString(PrefKey(All[i]), old.ToString()); }
        keys[(int)a] = key;
        PlayerPrefs.SetString(PrefKey(a), key.ToString());
        Changed?.Invoke();
        return swapped;
    }

    public static void ResetAll()
    {
        foreach (var a in All) PlayerPrefs.DeleteKey(PrefKey(a));
        _keys = null;
        Changed?.Invoke();
    }

    /// <summary>바꿀 수 없는 키 (메뉴·칸 번호·시험용)</summary>
    public static bool IsAllowed(Key k)
    {
        if (k == Key.None || k == Key.Escape || k == Key.H) return false;
        if (k >= Key.Digit1 && k <= Key.Digit6) return false;
        if (k >= Key.Numpad1 && k <= Key.Numpad6) return false;
        return k != Key.LeftWindows && k != Key.RightWindows && k != Key.ContextMenu && k != Key.PrintScreen;
    }

    /// <summary>이번 프레임에 눌린 키 (키 설정 창에서 새 키를 받을 때)</summary>
    public static Key PressedThisFrame()
    {
        var kb = Keyboard.current;
        if (kb == null) return Key.None;
        foreach (KeyControl k in kb.allKeys)
            if (k != null && k.wasPressedThisFrame) return k.keyCode;
        return Key.None;
    }

    /// <summary>화면에 보일 키 이름 ("F", "Shift", "Space" …)</summary>
    public static string Label(GameAction a) => KeyName(Get(a));

    public static string KeyName(Key k)
    {
        switch (k)
        {
            case Key.LeftShift: return "Shift";
            case Key.RightShift: return "오른쪽 Shift";
            case Key.LeftCtrl: return "Ctrl";
            case Key.RightCtrl: return "오른쪽 Ctrl";
            case Key.LeftAlt: return "Alt";
            case Key.RightAlt: return "오른쪽 Alt";
            case Key.Space: return "Space";
            case Key.Enter: return "Enter";
            case Key.Tab: return "Tab";
            case Key.Backspace: return "Backspace";
            case Key.CapsLock: return "CapsLock";
            case Key.UpArrow: return "↑";
            case Key.DownArrow: return "↓";
            case Key.LeftArrow: return "←";
            case Key.RightArrow: return "→";
            case Key.Backquote: return "`";
            case Key.Minus: return "-";
            case Key.Equals: return "=";
            case Key.LeftBracket: return "[";
            case Key.RightBracket: return "]";
            case Key.Semicolon: return ";";
            case Key.Quote: return "'";
            case Key.Comma: return ",";
            case Key.Period: return ".";
            case Key.Slash: return "/";
            case Key.Backslash: return "\\";
        }
        string s = k.ToString();
        if (s.StartsWith("Digit")) return s.Substring(5);
        if (s.StartsWith("Numpad")) return "숫자판 " + s.Substring(6);
        return s;
    }

    public static string ActionName(GameAction a)
    {
        switch (a)
        {
            case GameAction.MoveForward: return "앞으로";
            case GameAction.MoveBack: return "뒤로";
            case GameAction.MoveLeft: return "왼쪽";
            case GameAction.MoveRight: return "오른쪽";
            case GameAction.Sprint: return "달리기";
            case GameAction.Crouch: return "앉기";
            case GameAction.Jump: return "점프";
            case GameAction.Interact: return "상호작용 (줍기·장치·문)";
            case GameAction.Drop: return "버리기";
            case GameAction.Reload: return "장전";
            case GameAction.Flashlight: return "손전등";
            case GameAction.Radio: return "무전 (누르는 동안)";
            default: return "마이크 끄기·켜기";
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { _keys = null; Listening = false; }
}
