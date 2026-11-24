using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using VoidLast.Player;

/// <summary>
/// 멀티플레이 관전 모드.
///  - 내 대원이 죽으면 잠시(deathDelay) 뒤 살아있는 다른 대원을 3인칭으로 따라가며 본다.
///  - 마우스 이동: 대상 주위를 둘러보기 / 좌클릭: 다음 대원 / 우클릭: 이전 대원
///  - 보고 있는 대원이 죽으면 살아있는 다른 대원으로 자동으로 넘어간다.
///  - 지금 보고 있는 대원의 닉네임을 인벤토리(핫바) 바로 위에 표시한다.
/// 내 1인칭 카메라는 끄고 따로 만든 관전 카메라를 쓴다 (내 1인칭 손·총이 따라다니지 않도록).
/// </summary>
[DisallowMultipleComponent]
public sealed class SpectatorSystem : MonoBehaviour
{
    public TMP_FontAsset font;
    [Tooltip("죽은 뒤 관전으로 넘어가기까지 (내 죽음 연출을 잠깐 보여줌)")]
    [Min(0)] public float deathDelay = 2.5f;
    [Tooltip("대상 뒤 카메라 거리(m)")]
    [Min(0.5f)] public float distance = 2.8f;
    [Min(0)] public float mouseSensitivity = 0.1f;
    public float minPitch = -35f, maxPitch = 65f;

    public static bool IsSpectating { get; private set; }
    public static FpsNetworkPlayer Target { get; private set; }

    readonly List<FpsNetworkPlayer> _alive = new List<FpsNetworkPlayer>();
    FpsNetworkPlayer _me, _target;
    float _diedAt = -1f, _yaw, _pitch = 14f, _dist;
    Camera _cam, _ownCam;
    AudioListener _ownListener;
    bool _spectating;

    GameObject _panel;
    TMP_Text _name, _hint;
    RectTransform _panelRect;
    FpsHud _hud;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { IsSpectating = false; Target = null; }

    void OnDestroy()
    {
        IsSpectating = false; Target = null;
        if (_cam != null) Destroy(_cam.gameObject);
    }

    void Update()
    {
        if (!Stage2NetworkBootstrap.IsOnline) return;
        // 층 이동 연출 중에는 관전 화면을 잠시 숨긴다
        if (FloorTransition.InCutscene)
        {
            if (_cam != null && _cam.gameObject.activeSelf) _cam.gameObject.SetActive(false);
            if (_panel != null && _panel.activeSelf) _panel.SetActive(false);
            return;
        }
        if (_spectating)
        {
            if (_cam != null && !_cam.gameObject.activeSelf) _cam.gameObject.SetActive(true);
            if (_panel != null && !_panel.activeSelf) _panel.SetActive(true);
        }
        if (_me == null) { _me = FpsNetworkPlayer.Local; _diedAt = -1f; if (_me == null) return; }
        var myHealth = _me.Health;
        if (myHealth == null || !myHealth.IsDead) { if (_spectating) Stop(); _diedAt = -1f; return; }

        if (_diedAt < 0f) _diedAt = Time.time;
        if (Time.time - _diedAt < (FloorCarryOver.ArrivedDead ? 0f : deathDelay)) return;   // 죽은 채로 다음 층에 왔으면 바로 관전

        CollectAlive();
        if (_alive.Count == 0)
        {
            // 살아있는 대원이 없으면 마지막으로 보던 대원(쓰러진 모습)을 계속 비춘다 → 게임 오버 화면이 뜬다
            if (_spectating && _target == null) Stop();
            if (_spectating) UpdateLabel(true);
            return;
        }

        if (!_spectating) Begin();
        if (_target == null || _target.Health == null || _target.Health.IsDead || !_alive.Contains(_target)) Switch(+1);

        var input = _me.PlayerInput;
        if (input != null && input.isActiveAndEnabled)
        {
            if (input.Attack) Switch(+1);
            else if (input.AimPressed) Switch(-1);
            Vector2 look = input.Look;
            _yaw += look.x * mouseSensitivity;
            _pitch = Mathf.Clamp(_pitch - look.y * mouseSensitivity, minPitch, maxPitch);
        }
        UpdateLabel(false);
    }

    void LateUpdate()
    {
        if (!_spectating || _cam == null || _target == null) return;
        var pivot = _target.cameraPivot != null ? _target.cameraPivot : _target.transform;
        Vector3 focus = pivot.position + Vector3.up * 0.15f;
        var rot = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 back = rot * Vector3.back;

        float want = distance;
        if (Physics.SphereCast(focus, 0.22f, back, out var hit, distance, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(_target.transform))
            want = Mathf.Max(0.35f, hit.distance - 0.05f);
        // 벽에 붙을 때는 바로, 멀어질 때는 부드럽게
        _dist = want < _dist ? want : Mathf.MoveTowards(_dist, want, Time.deltaTime * 4f);

        _cam.transform.SetPositionAndRotation(focus + back * _dist + Vector3.up * 0.25f, rot);
    }

    void CollectAlive()
    {
        _alive.Clear();
        foreach (var p in FpsNetworkPlayer.All)
            if (p != null && p != _me && p.Health != null && !p.Health.IsDead) _alive.Add(p);
        _alive.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
    }

    void Switch(int dir)
    {
        if (_alive.Count == 0) return;
        int i = _target != null ? _alive.IndexOf(_target) : -1;
        int next = i < 0 ? (dir > 0 ? 0 : _alive.Count - 1) : (i + dir + _alive.Count) % _alive.Count;
        var t = _alive[next];
        if (t == _target && i >= 0) return;
        _target = t;
        Target = t;
        _yaw = t.transform.eulerAngles.y;
        _pitch = 14f;
        _dist = distance;
        UpdateLabel(false);
    }

    // ------------------------------------------------------------------ 카메라 전환
    void Begin()
    {
        _spectating = true;
        IsSpectating = true;
        _ownCam = _me.ViewCamera;
        if (_cam == null)
        {
            var go = new GameObject("Spectator Camera");
            go.tag = "MainCamera";
            _cam = go.AddComponent<Camera>();
            if (_ownCam != null)
            {
                _cam.CopyFrom(_ownCam);
                CopyUrp(_ownCam, _cam);
            }
            _cam.nearClipPlane = 0.05f;
            go.AddComponent<AudioListener>();
        }
        _cam.gameObject.SetActive(true);
        if (_ownCam != null)
        {
            _ownCam.enabled = false;
            _ownListener = _ownCam.GetComponent<AudioListener>();
            if (_ownListener != null) _ownListener.enabled = false;
        }
        BuildLabel();
        if (_panel != null) _panel.SetActive(true);
    }

    void Stop()
    {
        _spectating = false;
        IsSpectating = false;
        _target = null; Target = null;
        if (_cam != null) _cam.gameObject.SetActive(false);
        if (_ownCam != null) _ownCam.enabled = true;
        if (_ownListener != null) _ownListener.enabled = true;
        if (_panel != null) _panel.SetActive(false);
    }

    static void CopyUrp(Camera from, Camera to)
    {
        var a = from.GetUniversalAdditionalCameraData();
        var b = to.GetUniversalAdditionalCameraData();
        if (a == null || b == null) return;
        b.renderPostProcessing = a.renderPostProcessing;
        b.antialiasing = a.antialiasing;
        b.antialiasingQuality = a.antialiasingQuality;
        b.renderShadows = a.renderShadows;
        b.volumeLayerMask = a.volumeLayerMask;
        b.volumeTrigger = null;   // 관전 카메라 위치 기준으로 후처리 볼륨 적용
        b.dithering = a.dithering;
        b.stopNaN = a.stopNaN;
    }

    // ------------------------------------------------------------------ 닉네임 표시 (핫바 바로 위)
    void BuildLabel()
    {
        if (_panel != null) return;
        _hud = _me.GetComponent<FpsHud>();
        var hotbar = _hud != null ? _hud.HotbarRect : null;
        Transform parent;
        if (hotbar != null) parent = hotbar.parent;
        else
        {
            // HUD를 못 찾으면 화면 아래 가운데에 따로 그린다
            var cgo = new GameObject("Spectator HUD", typeof(RectTransform));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30;
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            parent = cgo.transform;
        }

        _panel = new GameObject("Spectating", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _panel.transform.SetParent(parent, false);
        var bg = _panel.GetComponent<Image>();
        bg.color = new Color(0.07f, 0.085f, 0.105f, 0.82f);   // 로비 패널 색
        bg.raycastTarget = false;
        _panelRect = (RectTransform)_panel.transform;
        _panelRect.sizeDelta = new Vector2(460, 86);
        if (hotbar != null)
        {
            // 핫바의 위쪽 가운데에 붙인다
            _panelRect.anchorMin = hotbar.anchorMin; _panelRect.anchorMax = hotbar.anchorMax;
            _panelRect.pivot = new Vector2(0.5f, 0f);
            var size = hotbar.rect.size;
            _panelRect.anchoredPosition = hotbar.anchoredPosition + new Vector2((0.5f - hotbar.pivot.x) * size.x, (1f - hotbar.pivot.y) * size.y + 14f);
        }
        else
        {
            _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(0.5f, 0f);
            _panelRect.pivot = new Vector2(0.5f, 0f);
            _panelRect.anchoredPosition = new Vector2(0, 150);
        }

        var stripe = new GameObject("Stripe", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        stripe.transform.SetParent(_panel.transform, false);
        var si = stripe.GetComponent<Image>(); si.color = LobbyStyle.Accent; si.raycastTarget = false;
        var sr = (RectTransform)stripe.transform;
        sr.anchorMin = new Vector2(0, 0); sr.anchorMax = new Vector2(0, 1); sr.pivot = new Vector2(0, 0.5f); sr.sizeDelta = new Vector2(5, 0); sr.anchoredPosition = Vector2.zero;

        _name = Text(_panel.transform, "Name", 28, Color.white, FontStyles.Normal);
        var nr = _name.rectTransform; nr.anchorMin = new Vector2(0, 1); nr.anchorMax = new Vector2(1, 1); nr.pivot = new Vector2(0.5f, 1);
        nr.offsetMin = new Vector2(22, -52); nr.offsetMax = new Vector2(-16, -6);
        _hint = Text(_panel.transform, "Hint", 16, LobbyStyle.TextDim, FontStyles.Normal);
        var hr = _hint.rectTransform; hr.anchorMin = new Vector2(0, 0); hr.anchorMax = new Vector2(1, 0); hr.pivot = new Vector2(0.5f, 0);
        hr.offsetMin = new Vector2(22, 6); hr.offsetMax = new Vector2(-16, 32);
    }

    TMP_Text Text(Transform parent, string name, float size, Color c, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size; t.color = c; t.fontStyle = style; t.alignment = TextAlignmentOptions.Left;
        t.raycastTarget = false; t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    void UpdateLabel(bool nobodyAlive)
    {
        if (_name == null) return;
        string nick = _target != null && !string.IsNullOrEmpty(_target.Nickname) ? _target.Nickname : "대원";
        _name.text = "<size=70%><color=#E0443A>관전 중</color></size>   <b>" + Escape(nick) + "</b>";
        _hint.text = nobodyAlive ? "살아있는 대원이 없어요"
            : _alive.Count > 1 ? "좌클릭 다음 대원 · 우클릭 이전 대원 · " + (_alive.IndexOf(_target) + 1) + " / " + _alive.Count
            : "살아있는 대원 1명 · 마우스로 둘러보기";
    }

    static string Escape(string s) => s.Replace("<", "‹").Replace(">", "›");
}
