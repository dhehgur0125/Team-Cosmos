using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using VoidLast.Player;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 층마다 계단(A·B) 입구를 막고 있던 격벽 문을 F로 열고 닫는다.
///  - 문 앞에서 문을 보고 F → 문짝 두 장이 위로 올라가 천장 속(벽 두께 안)으로 들어가고 통과할 수 있게 된다. 다시 F → 내려와 닫힌다.
///  - 문 사이에 대원이 서 있으면 닫히지 않는다.
///  - 멀티플레이: 방장(서버)이 열림 상태를 정하고 모든 대원에게 같은 문 상태가 보인다. 오프라인에서도 그대로 동작한다.
///  - 층별 씬(FloorTransition이 있는 씬): 문은 잠겨 있고, 이 층의 클리어 조건을 만족하면 F로 문을 열고 아래층으로 내려간다
///    (층 이동 연출은 FloorTransition). 1층의 계단 문은 위층으로 돌아갈 수 없으므로 열리지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class StairDoorController : NetworkBehaviour
{
    [Serializable]
    public class Door
    {
        public string id;
        public Transform root;
        [Tooltip("움직이는 문짝 (닫힌 위치에서 시작)")]
        public Transform[] leaves;
        [Tooltip("닫혀 있을 때 길을 막는 콜라이더")]
        public Collider blocker;
        [Tooltip("문을 바라볼 때 맞는 트리거")]
        public Collider interactTrigger;
        [Tooltip("열린 위치 = 닫힌 위치 + 이 값 (월드)")]
        public Vector3 openOffset = new Vector3(0, 4f, 0);
        [NonSerialized] public Vector3[] closedPos;
        [NonSerialized] public float t;
        [NonSerialized] public AudioSource audio;
    }

    public Door[] doors;
    [Min(0.2f)] public float openSeconds = 1.2f;
    [Min(1f)] public float interactDistance = 3.2f;
    public TMP_FontAsset font;

    public static StairDoorController Instance { get; private set; }

    readonly NetworkVariable<int> _mask = new NetworkVariable<int>();
    int _offlineMask, _shownMask, _localMask;
    readonly Dictionary<Collider, int> _byCollider = new Dictionary<Collider, int>();
    TMP_Text _prompt;
    AudioClip _openClip, _closeClip;
    float _denyUntil;

    int Mask => IsSpawned ? _mask.Value : _offlineMask;
    /// <summary>화면에 보이는 열림 상태 = 네트워크 상태 + 연출용으로 내 화면에서만 연 문</summary>
    int DisplayMask => Mask | _localMask;
    bool HasAuthority => !IsSpawned || IsServer;
    public bool IsOpen(int index) => (DisplayMask & (1 << index)) != 0;

    /// <summary>층 이동 연출: 내 화면에서만 문을 열고 닫는다</summary>
    public void SetLocalOpen(int index, bool open)
    {
        if (index < 0 || index >= doors.Length) return;
        if (open) _localMask |= 1 << index; else _localMask &= ~(1 << index);
    }

    /// <summary>계단 'A' 또는 'B'의 문 번호 (없으면 -1)</summary>
    public int FindDoor(char side)
    {
        string key = "Stair" + side;
        for (int i = 0; i < doors.Length; i++)
            if (doors[i] != null && !string.IsNullOrEmpty(doors[i].id) && doors[i].id.EndsWith(key)) return i;
        return -1;
    }

    static char SideOf(Door d) => d != null && d.id != null && d.id.EndsWith("StairB") ? 'B' : 'A';

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    void Awake()
    {
        Instance = this;
        _openClip = DoorSound("StairDoorOpen", true);
        _closeClip = DoorSound("StairDoorClose", false);
        for (int i = 0; i < doors.Length; i++)
        {
            var d = doors[i];
            if (d == null || d.root == null) continue;
            d.closedPos = new Vector3[d.leaves.Length];
            for (int k = 0; k < d.leaves.Length; k++) d.closedPos[k] = d.leaves[k].position;
            if (d.interactTrigger != null) _byCollider[d.interactTrigger] = i;
            if (d.blocker != null) _byCollider[d.blocker] = i;
            d.audio = d.root.gameObject.AddComponent<AudioSource>();
            d.audio.playOnAwake = false; d.audio.spatialBlend = 1f; d.audio.minDistance = 2f; d.audio.maxDistance = 25f; d.audio.volume = 0.8f;
            GameAudio.Route(d.audio);
        }
        BuildPrompt();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ------------------------------------------------------------------ 매 프레임: 애니메이션 + 내 입력
    void Update()
    {
        int mask = DisplayMask;
        if (mask != _shownMask)
        {
            int changed = mask ^ _shownMask;
            for (int i = 0; i < doors.Length; i++)
                if ((changed & (1 << i)) != 0 && doors[i]?.audio != null) doors[i].audio.PlayOneShot((mask & (1 << i)) != 0 ? _openClip : _closeClip);
            _shownMask = mask;
        }

        float step = Time.deltaTime / openSeconds;
        for (int i = 0; i < doors.Length; i++)
        {
            var d = doors[i];
            if (d == null || d.closedPos == null) continue;
            float target = (mask & (1 << i)) != 0 ? 1f : 0f;
            if (d.t == target) { SetBlocker(d); continue; }
            d.t = Mathf.MoveTowards(d.t, target, step);
            float e = Mathf.SmoothStep(0, 1, d.t);
            for (int k = 0; k < d.leaves.Length; k++) d.leaves[k].position = d.closedPos[k] + d.openOffset * e;
            SetBlocker(d);
        }

        LocalInput();
    }

    static void SetBlocker(Door d)
    {
        if (d.blocker == null) return;
        bool block = d.t < 0.6f;   // 반쯤 열리면 지나갈 수 있음
        if (d.blocker.enabled != block) d.blocker.enabled = block;
    }

    static FpsHealth LocalPlayer()
    {
        if (Stage2NetworkBootstrap.IsOnline) return FpsNetworkPlayer.Local != null ? FpsNetworkPlayer.Local.Health : null;
        var cam = Camera.main;
        return cam != null ? cam.GetComponentInParent<FpsHealth>() : null;
    }

    static bool InteractPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
#else
        return GameKeys.Down(GameAction.Interact);
#endif
    }

    void LocalInput()
    {
        int target = -1;
        var me = LocalPlayer();
        var cam = Camera.main;
        if (me != null && !me.IsDead && cam != null && Cursor.lockState == CursorLockMode.Locked && !FloorTransition.InCutscene)
        {
            var hits = Physics.SphereCastAll(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f)), 0.2f, interactDistance, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(me.transform)) continue;
                if (_byCollider.TryGetValue(h.collider, out int idx)) { target = idx; break; }
                if (!h.collider.isTrigger) break;   // 다른 물체에 가려짐
            }
        }

        if (_prompt == null) return;
        if (target < 0) { _prompt.text = ""; return; }

        // 층별 씬: 클리어해야 열리는 출구 문
        var floor = FloorTransition.Instance;
        if (floor != null)
        {
            bool allowed = floor.AllowsSide(SideOf(doors[target]));
            if (!floor.HasNextFloor) _prompt.text = "<color=#94A3B8>위층으로는 다시 올라갈 수 없어요</color>";
            else if (floor.IsDescending) _prompt.text = "";
            else if (!allowed) _prompt.text = "<color=#FCA5A5>잠김</color> · 이 계단은 막혀 있어요. 다른 계단으로 가세요";
            else if (floor.CanDescend) _prompt.text = "[" + GameKeys.Label(GameAction.Interact) + "] 문 열고 " + FloorTransition.FloorName(floor.floor - 1).Split(' ')[0] + "으로 내려가기";
            else _prompt.text = "<color=#FCA5A5>잠김</color> · 이 층의 임무를 먼저 완료하세요";
            if (InteractPressed() && floor.CanDescend && allowed) Request(target, me);
            return;
        }

        bool open = IsOpen(target);
        if (Time.unscaledTime < _denyUntil) _prompt.text = "<color=#FCA5A5>문 사이에 누가 있어서 닫을 수 없어요</color>";
        else _prompt.text = "[" + GameKeys.Label(GameAction.Interact) + (open ? "] 문 닫기" : "] 문 열기");

        if (InteractPressed())
        {
            if (open && Blocked(target)) { _denyUntil = Time.unscaledTime + 1.5f; return; }
            Request(target, me);
        }
    }

    // ------------------------------------------------------------------ 열기/닫기 요청
    void Request(int index, FpsHealth who)
    {
        if (HasAuthority) Toggle(index, who != null ? who.transform.position : Vector3.zero, who != null);
        else ToggleRpc(index);
    }

    [Rpc(SendTo.Server)]
    void ToggleRpc(int index, RpcParams rpcParams = default)
    {
        var player = FpsNetworkPlayer.FindByClient(rpcParams.Receive.SenderClientId);
        if (player == null || player.Health == null || player.Health.IsDead) return;
        Toggle(index, player.transform.position, true);
    }

    void Toggle(int index, Vector3 from, bool checkDistance)
    {
        if (index < 0 || index >= doors.Length || doors[index] == null || doors[index].root == null) return;
        var d = doors[index];
        if (checkDistance && Vector3.Distance(from, ClosestPoint(d, from)) > interactDistance + 2f) return;
        var floor = FloorTransition.Instance;
        if (floor != null) { floor.TryBeginDescent(SideOf(d)); return; }   // 층별 씬: 문 열기 = 아래층으로 내려가기
        bool open = (Mask & (1 << index)) != 0;
        if (open && Blocked(index)) return;   // 문 사이에 사람이 있으면 닫지 않음
        int mask = Mask ^ (1 << index);
        if (IsSpawned) _mask.Value = mask; else _offlineMask = mask;
    }

    static Vector3 ClosestPoint(Door d, Vector3 p)
    {
        if (d.interactTrigger != null) return d.interactTrigger.ClosestPoint(p);
        return d.root.position;
    }

    /// <summary>닫히는 자리에 대원(캐릭터 컨트롤러)이 있는지</summary>
    bool Blocked(int index)
    {
        var d = doors[index];
        var box = d.blocker as BoxCollider;
        if (box == null) return false;
        bool was = box.enabled; box.enabled = true;
        var b = box.bounds;
        box.enabled = was;
        foreach (var c in Physics.OverlapBox(b.center, b.extents + new Vector3(0.15f, 0, 0.15f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            if (c is CharacterController && c.GetComponentInParent<FpsHealth>() != null) return true;
        return false;
    }

    // ------------------------------------------------------------------ UI / 소리
    void BuildPrompt()
    {
        var canvasGo = new GameObject("Stair Door Prompt", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 36;
        var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(canvasGo.transform, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = 22; t.color = Color.white; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.richText = true;
        var r = t.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(0, -90); r.sizeDelta = new Vector2(900, 34);
        _prompt = t;
    }

    /// <summary>문 소리: 공압 '치익' + 끝에 '쿵'</summary>
    static AudioClip DoorSound(string name, bool opening)
    {
        int rate = 44100; float seconds = 1.1f; int n = (int)(rate * seconds);
        var data = new float[n];
        var rng = new System.Random(opening ? 11 : 17);
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float hiss = (float)(rng.NextDouble() * 2 - 1);
            lp = Mathf.Lerp(lp, hiss, 0.12f);
            float hissEnv = Mathf.Clamp01(t * 12f) * Mathf.Clamp01((0.75f - t) * 3f);
            float motor = Mathf.Sin(2 * Mathf.PI * (opening ? 70f + t * 40f : 110f - t * 40f) * t) * 0.35f * Mathf.Clamp01((0.9f - t) * 4f) * Mathf.Clamp01(t * 8f);
            float thudT = t - 0.92f;
            float thud = thudT > 0 ? Mathf.Sin(2 * Mathf.PI * 55f * thudT) * Mathf.Exp(-thudT * 18f) * 0.9f : 0;
            data[i] = (lp * 0.5f * hissEnv + motor + thud) * 0.6f;
        }
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
