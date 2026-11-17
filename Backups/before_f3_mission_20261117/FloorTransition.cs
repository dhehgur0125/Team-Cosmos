using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using VoidLast.Player;

/// <summary>
/// 층(씬) 사이 이동.
///  - 내려가기: 이 층의 클리어 조건을 만족한 뒤 계단 문(A·B)을 F로 열면, 모든 대원의 화면에서
///    문이 열리고 계단으로 걸어 내려가는 연출 → 점점 어두워짐 → 완전히 검게 되면 방장이 아래층 씬을 불러온다 (로딩 화면).
///  - 도착: 로딩이 끝나면 화면이 밝아지며 위층에서 계단을 내려와 이 층의 문을 열고 복도로 나오는 연출 → 조종 가능.
///  - 연출 중에는 이동·시점·사격·상호작용 입력을 모두 막고, 피해도 받지 않는다.
///  - HP·핫바·탄약·손전등은 FloorCarryOver로 다음 층에 그대로 이어진다.
///  - 오프라인(씬 직접 실행)에서도 같은 연출로 다음 층 씬을 불러온다.
/// 계단 좌표: 계단 A는 x = -42(문)에서 -58 쪽, 계단 B는 +42에서 +58 쪽. 층 높이 = (층-1) × 8m.
/// </summary>
[DisallowMultipleComponent]
public sealed class FloorTransition : NetworkBehaviour
{
    [Tooltip("이 씬의 층 (1~5)")] [Range(1, 5)] public int floor = 2;
    [Tooltip("아래층 씬 이름 (1층이면 비움)")] public string nextSceneName;
    [Tooltip("내려갈 수 있는 계단 (예: A, AB)")] public string descentSides = "AB";
    public TMP_FontAsset font;
    [Tooltip("연출 중 걷는 속도 (m/s)")] [Min(0.5f)] public float walkSpeed = 2.4f;
    [Tooltip("내려갈 때 화면이 검게 되는 시간")] [Min(0.2f)] public float fadeOutSeconds = 1.6f;
    [Tooltip("도착할 때 화면이 밝아지는 시간")] [Min(0.2f)] public float fadeInSeconds = 1.5f;
    [Tooltip("연출 중 앞을 비추는 손전등 밝기")] [Min(0)] public float cutsceneLight = 9f;

    public static FloorTransition Instance { get; private set; }
    /// <summary>층 이동 연출 중 (입력 금지)</summary>
    public static bool InCutscene { get; private set; }

    F2MissionDirector _director;
    bool _descending, _arrivalPending, _arrivalRunning, _applied;
    float _arrivalWait;

    public float FloorY => (floor - 1) * 8f;
    public bool HasNextFloor => floor > 1 && !string.IsNullOrEmpty(nextSceneName);
    /// <summary>이 층의 클리어 조건을 만족했는지 (임무가 없는 층은 바로 내려갈 수 있음)</summary>
    public bool CanDescend
    {
        get
        {
            if (!HasNextFloor || _descending) return false;
            if (_director == null) return true;
            var p = (F2MissionDirector.Phase)_director.State.phase;
            return p == F2MissionDirector.Phase.Rally || p == F2MissionDirector.Phase.Cleared;
        }
    }
    public bool IsDescending => _descending;
    /// <summary>이 계단(A/B)으로 내려갈 수 있는지</summary>
    public bool AllowsSide(char side) => string.IsNullOrEmpty(descentSides) || descentSides.IndexOf(side) >= 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; InCutscene = false; }

    public static string FloorName(int f)
    {
        switch (f)
        {
            case 1: return "1층 · 격납고";
            case 2: return "2층 · 화물 및 검역 구역";
            case 3: return "3층 · 관제 구역";
            case 4: return "4층 · 동력 구역";
            case 5: return "5층 · 승무원 구역";
            default: return f + "층";
        }
    }

    readonly List<GameObject> _stairwell = new List<GameObject>();

    /// <summary>계단 연출용 계단실 조각(이웃 층에서 잘라 온 것)은 연출할 때만 켠다 (평소에는 그리지 않음)</summary>
    void SetStairwell(bool on)
    {
        foreach (var g in _stairwell) if (g != null && g.activeSelf != on) g.SetActive(on);
    }

    void Awake()
    {
        Instance = this;
        _director = FindFirstObjectByType<F2MissionDirector>();
        var ship = GameObject.Find("VOID_LAST_Ship");
        if (ship != null)
            foreach (Transform c in ship.transform)
                if (c.name.Contains("_Stairwell_")) _stairwell.Add(c.gameObject);
        SetStairwell(FloorCarryOver.Pending);
        if (FloorCarryOver.Pending)
        {
            // 위층에서 내려온 경우: 도착 연출이 끝날 때까지 조작 금지, 화면은 검은 상태로 시작
            _arrivalPending = true;
            SetCutscene(true);
            ScreenFader.Set(1f);
        }
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    // ------------------------------------------------------------------ 내려가기 시작
    /// <summary>방장(또는 오프라인): 계단 문을 연 대원이 있을 때. 클리어 조건을 만족했으면 모든 대원 연출 시작</summary>
    public bool TryBeginDescent(char side)
    {
        bool authority = !IsSpawned || IsServer;
        if (!authority || !CanDescend || !AllowsSide(side)) return false;
        _descending = true;
        if (IsSpawned) DescendRpc((byte)side);
        else StartCoroutine(Descent(side));
        return true;
    }

    [Rpc(SendTo.ClientsAndHost)]
    void DescendRpc(byte side)
    {
        _descending = true;
        StartCoroutine(Descent((char)side));
    }

    IEnumerator Descent(char side)
    {
        SetCutscene(true);
        SetStairwell(true);
        var player = LocalPlayer(out var ownCam);
        FloorCarryOver.SavePlayer(player);
        FloorCarryOver.Pending = true;
        FloorCarryOver.Transitioning = true;
        FloorCarryOver.ArrivalSide = side;
        FloorCarryOver.FromFloor = floor;
        LoadingScreen.Subtitle = "STAGE 2 · " + FloorName(floor - 1);
        LoadingScreen.Ensure(font);
        ScreenFader.Ensure();

        var cam = CreateCutsceneCamera(ownCam);
        HideBody(player, true);
        float s = side == 'A' ? -1f : 1f, Y = FloorY;
        Func<float, float, float, Vector3> W = (u, y, z) => new Vector3(s * u, y, z);

        var doors = StairDoorController.Instance;
        int door = doors != null ? doors.FindDoor(side) : -1;
        if (door >= 0) doors.SetLocalOpen(door, true);
        float doorStart = Time.time;

        // 문 앞까지 (가까이 있으면 내 시점에서 이어서, 멀면 문 앞으로 바로)
        Vector3 doorFront = W(40.4f, Y + 1.65f, 0f);
        Quaternion faceShaft = Quaternion.LookRotation(new Vector3(s, 0, 0));
        if (ownCam != null && Vector3.Distance(ownCam.transform.position, doorFront) < 9f)
        {
            cam.transform.SetPositionAndRotation(ownCam.transform.position, ownCam.transform.rotation);
            yield return Walk(cam.transform, new[] { cam.transform.position, doorFront }, walkSpeed, 0f, null);
        }
        else cam.transform.SetPositionAndRotation(W(38.6f, Y + 1.65f, 0f), faceShaft);
        yield return Turn(cam.transform, faceShaft, 0.35f);
        while (Time.time - doorStart < 1.15f) yield return null;   // 문이 다 열릴 때까지

        // 계단참 → 오른쪽 계단으로 내려감 → 화면이 점점 검게
        var path = new[]
        {
            cam.transform.position, W(42.8f, Y + 1.65f, 0.5f), W(44.7f, Y + 1.62f, 3.2f), W(46.3f, Y + 1.6f, 4.1f),
            W(52.0f, Y - 2.4f, 4.1f), W(55.0f, Y - 2.4f, 3.0f), W(56.2f, Y - 2.4f, 0.5f),
        };
        float fadeAt = PathLength(path, 3);
        bool fading = false;
        yield return Walk(cam.transform, path, walkSpeed, 1f, d =>
        {
            if (!fading && d >= fadeAt) { fading = true; ScreenFader.FadeTo(1f, fadeOutSeconds); }
            return fading && ScreenFader.IsBlack;   // 완전히 검게 되면 멈춤
        });
        if (!fading) ScreenFader.FadeTo(1f, 0.4f);
        while (!ScreenFader.IsBlack) yield return null;

        // 다음 층 불러오기: 방장(또는 오프라인)만. 참가자는 방장이 불러올 때까지 검은 화면으로 기다린다
        if (!IsSpawned)
        {
            var op = SceneManager.LoadSceneAsync(nextSceneName);
            LoadingScreen.BeginOffline(op, nextSceneName);
        }
        else if (IsServer)
        {
            yield return new WaitForSecondsRealtime(0.5f);   // 다른 대원 화면도 다 어두워지도록
            LobbyPlayerSpawner.KeywordOverride = new[] { "Arrival_" + side };
            var status = NetworkManager.SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);
            Debug.Log($"[Floor] {floor}층 → {nextSceneName} (계단 {side}) : {status}");
        }
        else
        {
            // 참가자: 네트워크 씬 불러오기 신호는 방장이 다음 층을 다 불러온 뒤에 오므로, 그동안 검은 화면 대신 로딩 화면을 보여 준다
            LoadingScreen.Show();
        }
    }

    // ------------------------------------------------------------------ 도착
    void Update()
    {
        if (!_arrivalPending || _arrivalRunning) return;
        _arrivalWait += Time.unscaledDeltaTime;
        var player = LocalPlayer(out var ownCam);
        if (player != null && !_applied)
        {
            _applied = true;
            if (!Stage2NetworkBootstrap.IsOnline) PlaceOfflinePlayer(player);
            FloorCarryOver.ApplyPlayer(player);
        }
        if (_arrivalWait > 40f && player == null) { FinishArrival(null, null); return; }   // 대원을 끝내 못 찾음
        if (player == null || LoadingScreen.IsShowing) return;
        _arrivalRunning = true;
        StartCoroutine(Arrival(player, ownCam));
    }

    void PlaceOfflinePlayer(GameObject player)
    {
        var point = GameObject.Find("Arrival_" + FloorCarryOver.ArrivalSide + "_1");
        if (point == null) return;
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.transform.SetPositionAndRotation(point.transform.position, point.transform.rotation);
        if (cc != null) cc.enabled = true;
    }

    IEnumerator Arrival(GameObject player, Camera ownCam)
    {
        char side = FloorCarryOver.ArrivalSide == 'B' ? 'B' : 'A';
        float s = side == 'A' ? -1f : 1f, Y = FloorY;
        Func<float, float, float, Vector3> W = (u, y, z) => new Vector3(s * u, y, z);
        var cam = CreateCutsceneCamera(ownCam);
        HideBody(player, true);   // 연출 카메라에 내 몸이 비치지 않게 (마지막에 내 시점으로 들어갈 때 머리를 통과함)

        // 위층 계단 중간참에서 시작해 계단을 내려온다 (화면이 점점 밝아짐)
        var down = new[] { W(52.6f, Y + 5.62f, -4.1f), W(46.2f, Y + 1.62f, -4.1f), W(44.4f, Y + 1.65f, -2.2f), W(43.5f, Y + 1.65f, -0.2f) };
        cam.transform.SetPositionAndRotation(down[0], Quaternion.LookRotation(down[1] - down[0]));
        ScreenFader.FadeTo(0f, fadeInSeconds);
        yield return Walk(cam.transform, down, walkSpeed * 0.9f, 1f, null);

        // 문 쪽(복도 쪽)으로 돌아서 문을 연다
        yield return Turn(cam.transform, Quaternion.LookRotation(new Vector3(-s, 0, 0)), 0.45f);
        var doors = StairDoorController.Instance;
        int door = doors != null ? doors.FindDoor(side) : -1;
        if (door >= 0) doors.SetLocalOpen(door, true);
        yield return new WaitForSeconds(door >= 0 ? 1.15f : 0.2f);

        // 복도로 나와서 내 대원 시점으로 이어진다
        yield return Walk(cam.transform, new[] { cam.transform.position, W(40.8f, Y + 1.65f, 0f) }, walkSpeed * 0.9f, 1f, null);
        if (ownCam != null)
        {
            Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation;
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.7f)
            {
                float e = Mathf.SmoothStep(0, 1, t);
                cam.transform.SetPositionAndRotation(Vector3.Lerp(p0, ownCam.transform.position, e), Quaternion.Slerp(r0, ownCam.transform.rotation, e));
                yield return null;
            }
        }
        FinishArrival(cam, ownCam);
        yield return new WaitForSeconds(1.2f);
        if (door >= 0 && doors != null) doors.SetLocalOpen(door, false);
    }

    void FinishArrival(Camera cam, Camera ownCam)
    {
        _arrivalPending = false;
        FloorCarryOver.Pending = false;
        FloorCarryOver.Transitioning = false;
        if (cam != null) Destroy(cam.gameObject);
        HideBody(null, false);
        SetStairwell(false);
        RestoreOwnCamera(ownCam);
        ScreenFader.FadeTo(0f, 0.3f);
        SetCutscene(false);
        StartCoroutine(DamageGrace());
    }

    IEnumerator DamageGrace()
    {
        FpsHealth.DamageBlocked = true;
        yield return new WaitForSeconds(1.5f);   // 도착 직후 잠깐은 피해 없음
        if (!InCutscene) FpsHealth.DamageBlocked = false;
    }

    // ------------------------------------------------------------------ 공용
    static void SetCutscene(bool on)
    {
        InCutscene = on;
        FpsInput.Blocked = on;
        FpsHealth.DamageBlocked = on;
    }

    /// <summary>내 대원 (온라인: 내 네트워크 대원 / 오프라인: 씬에 놓인 대원)</summary>
    static GameObject LocalPlayer(out Camera cam)
    {
        cam = null;
        if (Stage2NetworkBootstrap.IsOnline)
        {
            var p = FpsNetworkPlayer.Local;
            if (p == null) return null;
            cam = p.ViewCamera;
            return p.gameObject;
        }
        foreach (var h in FindObjectsByType<FpsHealth>(FindObjectsSortMode.None))
        {
            if (h.GetComponent<FpsNetworkPlayer>() != null || !h.isActiveAndEnabled) continue;
            cam = h.GetComponentInChildren<Camera>(true);
            return h.gameObject;
        }
        return null;
    }

    Camera _hiddenOwn;
    AudioListener _hiddenListener;
    readonly List<KeyValuePair<Renderer, bool>> _hiddenBody = new List<KeyValuePair<Renderer, bool>>();

    /// <summary>내 대원 모델을 잠시 숨기거나 원래대로 (연출 카메라용)</summary>
    void HideBody(GameObject player, bool hide)
    {
        if (!hide)
        {
            foreach (var kv in _hiddenBody) if (kv.Key != null) kv.Key.forceRenderingOff = kv.Value;
            _hiddenBody.Clear();
            return;
        }
        if (player == null) return;
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            _hiddenBody.Add(new KeyValuePair<Renderer, bool>(r, r.forceRenderingOff));
            r.forceRenderingOff = true;
        }
    }

    Camera CreateCutsceneCamera(Camera own)
    {
        var go = new GameObject("Floor Cutscene Camera");
        var cam = go.AddComponent<Camera>();
        if (own != null)
        {
            cam.CopyFrom(own);
            var a = own.GetUniversalAdditionalCameraData();
            var b = cam.GetUniversalAdditionalCameraData();
            if (a != null && b != null)
            {
                b.renderPostProcessing = a.renderPostProcessing;
                b.antialiasing = a.antialiasing;
                b.antialiasingQuality = a.antialiasingQuality;
                b.renderShadows = a.renderShadows;
                b.volumeLayerMask = a.volumeLayerMask;
                b.volumeTrigger = null;
                b.dithering = a.dithering;
            }
            cam.transform.SetPositionAndRotation(own.transform.position, own.transform.rotation);
            own.enabled = false;
            _hiddenOwn = own;
            _hiddenListener = own.GetComponent<AudioListener>();
            if (_hiddenListener != null) _hiddenListener.enabled = false;
        }
        cam.depth = 50f;   // 관전 카메라 등 다른 카메라보다 위
        cam.nearClipPlane = 0.05f;
        go.tag = "MainCamera";
        go.AddComponent<AudioListener>();
        // 계단실은 어두우므로 대원이 손전등을 비추며 걷는 것처럼 앞을 비춘다
        var beam = go.AddComponent<Light>();
        beam.type = LightType.Spot; beam.range = 13f; beam.spotAngle = 85f; beam.innerSpotAngle = 55f;
        beam.intensity = cutsceneLight; beam.color = new Color(1f, 0.95f, 0.86f); beam.shadows = LightShadows.None;
        var fillGo = new GameObject("Fill Light");
        fillGo.transform.SetParent(go.transform, false);
        var fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Point; fill.range = 4.5f; fill.intensity = 0.6f; fill.color = new Color(0.8f, 0.86f, 1f); fill.shadows = LightShadows.None;
        return cam;
    }

    void RestoreOwnCamera(Camera own)
    {
        var c = own != null ? own : _hiddenOwn;
        // 관전 중(죽은 대원)이면 관전 시스템이 자기 카메라를 쓰므로 1인칭 카메라는 다시 켜지 않는다
        if (c != null && !SpectatorSystem.IsSpectating) c.enabled = true;
        if (_hiddenListener != null && !SpectatorSystem.IsSpectating) _hiddenListener.enabled = true;
    }

    /// <summary>점들을 부드럽게 이은 길을 따라 카메라를 움직인다. stop이 true를 돌려주면 그 자리에서 멈춘다.</summary>
    static IEnumerator Walk(Transform cam, Vector3[] pts, float speed, float bob, Func<float, bool> stop)
    {
        var samples = Smooth(pts);
        var lengths = new float[samples.Count];
        for (int i = 1; i < samples.Count; i++) lengths[i] = lengths[i - 1] + Vector3.Distance(samples[i - 1], samples[i]);
        float total = lengths[lengths.Length - 1];
        Quaternion rot = cam.rotation;
        float d = 0f;
        Vector3 lastDir = rot * Vector3.forward;
        while (d < total)
        {
            d = Mathf.Min(total, d + speed * Time.deltaTime);
            Vector3 pos = Sample(samples, lengths, d);
            Vector3 ahead = Sample(samples, lengths, Mathf.Min(total, d + 1.4f));
            Vector3 dir = ahead - pos;
            dir.y *= 0.55f;   // 계단에서 너무 아래를 보지 않도록
            if (dir.sqrMagnitude > 0.0004f) lastDir = dir.normalized;
            rot = Quaternion.Slerp(rot, Quaternion.LookRotation(lastDir), 1f - Mathf.Exp(-7f * Time.deltaTime));
            float step = d / 0.75f * Mathf.PI;   // 걸음 흔들림
            Vector3 sway = (rot * Vector3.right) * Mathf.Sin(step) * 0.018f * bob + Vector3.up * Mathf.Abs(Mathf.Sin(step)) * 0.035f * bob;
            cam.SetPositionAndRotation(pos + sway, rot);
            if (stop != null && stop(d)) yield break;
            yield return null;
        }
    }

    static IEnumerator Turn(Transform cam, Quaternion target, float seconds)
    {
        Quaternion r0 = cam.rotation;
        for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.05f, seconds))
        {
            cam.rotation = Quaternion.Slerp(r0, target, Mathf.SmoothStep(0, 1, t));
            yield return null;
        }
        cam.rotation = target;
    }

    static float PathLength(Vector3[] pts, int upToIndex)
    {
        float l = 0;
        for (int i = 1; i <= upToIndex && i < pts.Length; i++) l += Vector3.Distance(pts[i - 1], pts[i]);
        return l;
    }

    static List<Vector3> Smooth(Vector3[] p)
    {
        var list = new List<Vector3>();
        if (p.Length < 3) { list.AddRange(p); return list; }
        for (int i = 0; i < p.Length - 1; i++)
        {
            Vector3 p0 = p[Mathf.Max(0, i - 1)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(p.Length - 1, i + 2)];
            for (int k = 0; k < 12; k++)
            {
                float t = k / 12f, t2 = t * t, t3 = t2 * t;
                list.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
            }
        }
        list.Add(p[p.Length - 1]);
        return list;
    }

    static Vector3 Sample(List<Vector3> s, float[] len, float d)
    {
        for (int i = 1; i < s.Count; i++)
            if (len[i] >= d)
            {
                float seg = len[i] - len[i - 1];
                return Vector3.Lerp(s[i - 1], s[i], seg > 1e-5f ? (d - len[i - 1]) / seg : 1f);
            }
        return s[s.Count - 1];
    }
}
