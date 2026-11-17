using System;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using VoidLast.Player;

/// <summary>
/// 죽은 대원의 고치. 층 씬마다 하나씩 있다.
///  - 대원이 죽으면 그 층의 랜덤한 위치에 고치(Cube)가 생기고, 그 대원은 구출될 때까지 관전한다.
///  - 고치에 총을 5발 쏘면 그 자리에서 최대 체력의 절반으로 되살아난다. 여러 대원이 나눠 쏴도 된다.
///  - 스캔에 표시된다 (DetectableItem). 사격 소리는 FpsPistol이 몬스터에게 알린다.
///  - 그 층에서 못 살리고 내려가면, 죽은 채로 도착한 다음 층에서 다시 고치가 생긴다 (따로 저장하지 않고 죽어 있는 대원을 보고 만든다).
///  - 위치 후보(spawnSpots)는 CocoonSetup이 층마다 문에서 걸어갈 수 있는 자리만 골라 저장한다. 잠긴 구역은 실행 중에 뺀다.
/// 판정은 방장(서버)만 하고 목록은 모든 대원에게 동기화된다. 혼자(오프라인)일 때는 죽으면 바로 게임 오버이므로 동작하지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class CocoonSystem : NetworkBehaviour
{
    public struct Entry : INetworkSerializable, IEquatable<Entry>
    {
        public ulong playerId;       // 대원의 NetworkObjectId
        public Vector3 position;     // 바닥 위치
        public byte hits;
        public FixedString64Bytes nickname;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref playerId);
            s.SerializeValue(ref position);
            s.SerializeValue(ref hits);
            s.SerializeValue(ref nickname);
        }

        public bool Equals(Entry o) => playerId == o.playerId && position == o.position && hits == o.hits && nickname.Equals(o.nickname);
    }

    [Header("위치 후보 (메뉴 6b로 생성)")]
    public Vector3[] spawnSpots;

    [Header("규칙")]
    [Min(1)] public int shotsToRescue = 5;
    [Range(0.05f, 1f)] public float reviveHealthFraction = 0.5f;
    [Tooltip("고치끼리 이만큼은 떨어져서 생긴다 (m)")]
    [Min(0f)] public float minSpacing = 3f;

    [Header("모습")]
    [Tooltip("대원 크기의 1.2배")]
    public Vector3 size = new Vector3(0.72f, 2.22f, 0.72f);
    public Material material;
    public TMP_FontAsset font;

    public static CocoonSystem Instance { get; private set; }

    readonly NetworkList<Entry> _entries = new NetworkList<Entry>();
    readonly HashSet<ulong> _reviving = new HashSet<ulong>();   // 서버 전용: 살려 달라고 보냈고 HP가 돌아오기를 기다리는 대원
    readonly Dictionary<ulong, CocoonTarget> _cubes = new Dictionary<ulong, CocoonTarget>();
    readonly HashSet<ulong> _seen = new HashSet<ulong>();
    MaterialPropertyBlock _block;
    TMP_Text _prompt, _toast;
    CanvasGroup _toastGroup;
    float _toastUntil;

    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    void Awake() { Instance = this; }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    void Update()
    {
        if (!IsSpawned) return;
        if (IsServer) ServerUpdate();
        SyncCubes();
        UpdateUi();
    }

    // ================================================================== 서버: 고치 만들기·없애기
    void ServerUpdate()
    {
        if (FloorTransition.InCutscene) return;   // 층 이동 연출이 끝난 뒤에 만든다
        foreach (var player in FpsNetworkPlayer.All)
        {
            if (player == null || player.Health == null) continue;
            ulong id = player.NetworkObjectId;
            int index = IndexOf(id);
            if (player.Health.IsDead)
            {
                if (index < 0 && !_reviving.Contains(id)) Create(player);
            }
            else
            {
                _reviving.Remove(id);
                if (index >= 0) _entries.RemoveAt(index);
            }
        }
        // 게임을 나간 대원의 고치는 없앤다
        for (int i = _entries.Count - 1; i >= 0; i--)
            if (FindPlayer(_entries[i].playerId) == null) _entries.RemoveAt(i);
    }

    void Create(FpsNetworkPlayer player)
    {
        string nick = string.IsNullOrEmpty(player.Nickname) ? "대원" : player.Nickname;
        _entries.Add(new Entry
        {
            playerId = player.NetworkObjectId,
            position = PickSpot(player.transform.position),
            nickname = new FixedString64Bytes(nick.Length > 20 ? nick.Substring(0, 20) : nick),
        });
    }

    Vector3 PickSpot(Vector3 fallback)
    {
        var usable = new List<Vector3>();
        var director = F2MissionDirector.Instance;
        if (spawnSpots != null)
            foreach (var spot in spawnSpots)
            {
                if (director != null && director.IsInLockedArea(spot + Vector3.up)) continue;
                bool taken = false;
                for (int i = 0; i < _entries.Count; i++)
                    if (Vector3.Distance(_entries[i].position, spot) < minSpacing) { taken = true; break; }
                if (!taken) usable.Add(spot);
            }
        return usable.Count > 0 ? usable[UnityEngine.Random.Range(0, usable.Count)] : fallback;
    }

    int IndexOf(ulong playerId)
    {
        for (int i = 0; i < _entries.Count; i++) if (_entries[i].playerId == playerId) return i;
        return -1;
    }

    static FpsNetworkPlayer FindPlayer(ulong playerId)
    {
        foreach (var player in FpsNetworkPlayer.All)
            if (player != null && player.IsSpawned && player.NetworkObjectId == playerId) return player;
        return null;
    }

    // ================================================================== 사격
    /// <summary>내 총이 고치를 맞혔을 때 (쏜 대원 화면에서 호출)</summary>
    public void ReportHit(ulong playerId)
    {
        if (IsSpawned) HitRpc(playerId);
    }

    [Rpc(SendTo.Server)]
    void HitRpc(ulong playerId, RpcParams rpcParams = default)
    {
        var shooter = FpsNetworkPlayer.FindByClient(rpcParams.Receive.SenderClientId);
        if (shooter == null || shooter.Health == null || shooter.Health.IsDead) return;
        int index = IndexOf(playerId);
        if (index < 0) return;
        var entry = _entries[index];
        entry.hits++;
        if (entry.hits < shotsToRescue) { _entries[index] = entry; return; }

        _entries.RemoveAt(index);
        _reviving.Add(playerId);
        ReviveRpc(playerId, entry.position, entry.nickname);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void ReviveRpc(ulong playerId, Vector3 position, FixedString64Bytes nickname)
    {
        var player = FindPlayer(playerId);
        if (player != null && player.IsOwner) player.ReviveFromCocoon(position + Vector3.up * 0.03f, reviveHealthFraction);
        Toast("<color=#6EE7B7>" + nickname + "</color> 대원을 고치에서 구출했습니다");
    }

    // ================================================================== 모든 화면: Cube 맞추기
    void SyncCubes()
    {
        var current = new HashSet<ulong>();
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            current.Add(entry.playerId);
            if (!_cubes.TryGetValue(entry.playerId, out var cube) || cube == null)
            {
                cube = BuildCube(entry);
                _cubes[entry.playerId] = cube;
                Announce(entry);
            }
            if (cube.Hits != entry.hits)
            {
                cube.Hits = entry.hits;
                Paint(cube);
            }
        }
        if (_cubes.Count == current.Count) return;
        var gone = new List<ulong>();
        foreach (var pair in _cubes) if (!current.Contains(pair.Key)) gone.Add(pair.Key);
        foreach (var id in gone)
        {
            if (_cubes[id] != null) Destroy(_cubes[id].gameObject);
            _cubes.Remove(id);
            _seen.Remove(id);
        }
    }

    CocoonTarget BuildCube(Entry entry)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Cocoon " + entry.nickname;
        go.transform.SetParent(transform, false);
        // 모든 화면에서 같은 방향이 되도록 위치로 정한다
        go.transform.SetPositionAndRotation(entry.position + Vector3.up * (size.y * 0.5f), Quaternion.Euler(0f, Mathf.Repeat(entry.position.x * 37f + entry.position.z * 53f, 90f), 0f));
        go.transform.localScale = size;
        var renderer = go.GetComponent<MeshRenderer>();
        if (material != null) renderer.sharedMaterial = material;
        var target = go.AddComponent<CocoonTarget>();
        target.System = this;
        target.PlayerId = entry.playerId;
        target.Nickname = entry.nickname.ToString();
        var detectable = go.AddComponent<DetectableItem>();
        detectable.glowColor = new Color(1f, 0.25f, 0.15f) * 2f;
        Paint(target);
        return target;
    }

    /// <summary>맞을수록 밝아진다</summary>
    void Paint(CocoonTarget cube)
    {
        var renderer = cube.GetComponent<MeshRenderer>();
        if (_block == null) _block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(_block);
        float t = shotsToRescue > 0 ? (float)cube.Hits / shotsToRescue : 0f;
        _block.SetColor(EmissionId, Color.Lerp(new Color(0.25f, 0.02f, 0.02f), new Color(2.2f, 0.45f, 0.1f), t));
        renderer.SetPropertyBlock(_block);
    }

    void Announce(Entry entry)
    {
        if (!_seen.Add(entry.playerId)) return;
        var local = FpsNetworkPlayer.Local;
        if (local != null && local.NetworkObjectId == entry.playerId) Toast("고치에 갇혔습니다 · 동료가 구출할 때까지 관전합니다");
        else Toast("<color=#FCA5A5>" + entry.nickname + "</color> 대원이 고치에 갇혔습니다 · 스캔으로 찾아 총 " + shotsToRescue + "발로 구출하세요");
    }

    // ================================================================== 안내
    void UpdateUi()
    {
        if (_prompt == null) BuildUi();
        _toastGroup.alpha = Mathf.MoveTowards(_toastGroup.alpha, Time.unscaledTime < _toastUntil ? 1f : 0f, Time.unscaledDeltaTime * 3f);

        string text = "";
        var local = FpsNetworkPlayer.Local;
        var cam = local != null ? local.ViewCamera : null;
        if (cam != null && cam.isActiveAndEnabled && local.Health != null && !local.Health.IsDead && !FloorTransition.InCutscene &&
            Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f)), out var hit, 40f, ~0, QueryTriggerInteraction.Ignore))
        {
            var target = hit.collider.GetComponent<CocoonTarget>();
            if (target != null)
                text = "<b>" + target.Nickname + "</b> 대원의 고치 · 총 <color=#FCD34D><b>" + Mathf.Max(0, shotsToRescue - target.Hits) + "</b>발</color> 더 쏘면 구출";
        }
        if (_prompt.text != text) _prompt.text = text;
    }

    void Toast(string message)
    {
        if (_toast == null) BuildUi();
        _toast.text = message;
        _toastUntil = Time.unscaledTime + 6f;
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("Cocoon HUD", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 34;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _prompt = Text(canvasGo.transform, "Prompt", 22);
        Place(_prompt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(900, 34));

        var bg = new GameObject("Toast", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bg.transform.SetParent(canvasGo.transform, false);
        var image = bg.GetComponent<Image>();
        image.color = new Color(0.05f, 0.06f, 0.08f, 0.82f);
        image.raycastTarget = false;
        Place((RectTransform)bg.transform, new Vector2(0.5f, 1f), new Vector2(0, -196), new Vector2(1000, 52));
        _toastGroup = bg.AddComponent<CanvasGroup>();
        _toastGroup.alpha = 0f;
        _toast = Text(bg.transform, "Text", 21);
        Place(_toast.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 52));
    }

    TMP_Text Text(Transform parent, string name, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = fontSize; t.color = Color.white; t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false; t.richText = true; t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    static void Place(RectTransform r, Vector2 anchor, Vector2 position, Vector2 sizeDelta)
    {
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = position;
        r.sizeDelta = sizeDelta;
    }

    void OnDrawGizmosSelected()
    {
        if (spawnSpots == null) return;
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.7f);
        foreach (var spot in spawnSpots) Gizmos.DrawWireCube(spot + Vector3.up * (size.y * 0.5f), size);
    }
}
