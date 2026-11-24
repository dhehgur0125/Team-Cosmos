using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using VoidLast.Player;

/// <summary>
/// 층마다 계단(A·B) 입구의 격벽 문. 살아 있는 대원이 문 앞뒤 autoOpenDistance(2.5m) 안에 있으면 저절로 열리고,
/// 아무도 없으면 저절로 닫힌다 (문짝 두 장이 위로 올라가 천장 속으로 들어감).
///  - 문 사이에 대원이 서 있으면 닫히지 않는다. 몬스터와 죽은 대원(관전자)은 문을 열지 못한다.
///  - 임무와 상관없이 열린다. 층 이동은 계단 안 투명 벽 앞에서 F (FloorTransition).
///  - 멀티플레이: 방장(서버)이 열림 상태를 정하고 모든 대원에게 같은 문 상태가 보인다. 오프라인에서도 그대로 동작한다.
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
        [Tooltip("문 위치 (문 가운데를 정할 때 사용)")]
        public Collider interactTrigger;
        [Tooltip("열린 위치 = 닫힌 위치 + 이 값 (월드)")]
        public Vector3 openOffset = new Vector3(0, 4f, 0);
        [NonSerialized] public Vector3[] closedPos;
        [NonSerialized] public float t;
        [NonSerialized] public AudioSource audio;
    }

    public Door[] doors;
    [Min(0.2f)] public float openSeconds = 1.2f;
    [Tooltip("이 거리(m) 안에 살아 있는 대원이 있으면 열림 (문 앞뒤 모두)")]
    [Min(0.5f)] public float autoOpenDistance = 2.5f;
    public TMP_FontAsset font;

    public static StairDoorController Instance { get; private set; }

    readonly NetworkVariable<int> _mask = new NetworkVariable<int>();
    int _offlineMask, _shownMask, _localMask;
    AudioClip _openClip, _closeClip;
    float _nextCheck;
    readonly List<Transform> _people = new List<Transform>();
    float _nextPeopleScan;

    int Mask => IsSpawned ? _mask.Value : _offlineMask;
    /// <summary>화면에 보이는 열림 상태 = 네트워크 상태 + 연출용으로 내 화면에서만 연 문</summary>
    int DisplayMask => Mask | _localMask;
    bool HasAuthority => !IsSpawned || IsServer;
    public bool IsOpen(int index) => (DisplayMask & (1 << index)) != 0;
    /// <summary>방장이 자동 규칙으로 연 상태인지 (내 화면에서만 연 것은 빼고)</summary>
    public bool IsAutoOpen(int index) => (Mask & (1 << index)) != 0;
    /// <summary>도착 연출 중: 방장이 문을 자동으로 열지 않는다 (각자 화면에서 연출로 연다)</summary>
    public static bool SuppressAuto;

    /// <summary>도착 연출: 내 화면에서만 문을 열어 둔다 (끄면 다시 자동 규칙을 따름)</summary>
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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; SuppressAuto = false; }

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
            d.audio = d.root.gameObject.AddComponent<AudioSource>();
            d.audio.playOnAwake = false; d.audio.spatialBlend = 1f; d.audio.minDistance = 2f; d.audio.maxDistance = 25f; d.audio.volume = 0.8f;
            GameAudio.Route(d.audio);
        }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ------------------------------------------------------------------ 매 프레임: 자동 열림 판정(방장) + 애니메이션
    void Update()
    {
        if (HasAuthority && !SuppressAuto && Time.unscaledTime >= _nextCheck)
        {
            _nextCheck = Time.unscaledTime + 0.1f;
            int want = AutoMask();
            if (want != Mask) { if (IsSpawned) _mask.Value = want; else _offlineMask = want; }
        }

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
    }

    static void SetBlocker(Door d)
    {
        if (d.blocker == null) return;
        bool block = d.t < 0.6f;   // 반쯤 열리면 지나갈 수 있음
        if (d.blocker.enabled != block) d.blocker.enabled = block;
    }

    /// <summary>방장: 문마다 가까이 살아 있는 대원이 있는지로 열림 상태를 정한다</summary>
    int AutoMask()
    {
        var people = LivingPlayers();
        int current = Mask, mask = 0;
        for (int i = 0; i < doors.Length; i++)
        {
            var d = doors[i];
            if (d == null || d.root == null) continue;
            Vector3 c = Center(d);
            bool near = false;
            foreach (var p in people)
            {
                Vector3 v = p.position - c;
                if (Mathf.Abs(v.y + 1.5f) > 3f) continue;   // 다른 높이 (발 위치 기준)
                v.y = 0;
                if (v.sqrMagnitude <= autoOpenDistance * autoOpenDistance) { near = true; break; }
            }
            bool open = (current & (1 << i)) != 0;
            if (near || (open && Blocked(i))) mask |= 1 << i;   // 문 사이에 사람이 있으면 닫지 않음
        }
        return mask;
    }

    static Vector3 Center(Door d) => d.interactTrigger != null ? d.interactTrigger.bounds.center : d.root.position;

    /// <summary>살아 있는 대원들의 위치 (온라인: 네트워크 대원 / 오프라인: 씬의 대원)</summary>
    List<Transform> LivingPlayers()
    {
        _people.Clear();
        if (IsSpawned || Stage2NetworkBootstrap.IsOnline)
        {
            foreach (var p in FpsNetworkPlayer.All)
                if (p != null && p.Health != null && !p.Health.IsDead) _people.Add(p.transform);
            return _people;
        }
        if (Time.unscaledTime >= _nextPeopleScan) { _nextPeopleScan = Time.unscaledTime + 1f; _offline = FindObjectsByType<FpsHealth>(FindObjectsSortMode.None); }
        if (_offline != null)
            foreach (var h in _offline)
                if (h != null && h.isActiveAndEnabled && !h.IsDead) _people.Add(h.transform);
        return _people;
    }
    FpsHealth[] _offline;

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

    // ------------------------------------------------------------------ 소리
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
