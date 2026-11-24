using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VoidLast.Monster;
using VoidLast.Player;

/// <summary>
/// 3층 중앙 관제실의 감시 화면: 층 지도 위에 몬스터와 대원 위치를 점으로 보여 준다.
/// 콘솔을 보고 F를 누르고 있는 대원의 화면에만 뜬다 (F3MissionDirector가 켜고 끈다).
/// 지도는 위쪽이 북쪽(-z). 방·복도 모양은 맵 배치 파일에서 가져온 값을 쓴다.
/// </summary>
public sealed class F3MonitorMap
{
    const float PanelWidth = 960f, PanelHeight = 840f, MapWidth = 900f, MapHeight = 690f;
    static readonly Color RoomColor = new Color(0.16f, 0.27f, 0.33f, 0.95f), CorridorColor = new Color(0.11f, 0.17f, 0.21f, 0.95f),
        StairColor = new Color(0.3f, 0.27f, 0.14f, 0.95f), MonsterColor = new Color(1f, 0.25f, 0.2f), DownColor = new Color(0.45f, 0.25f, 0.25f),
        CrewColor = new Color(0.3f, 1f, 0.5f), SelfColor = Color.white;

    readonly GameObject _panel;
    readonly RectTransform _mapRoot;
    readonly MissionHud _hud;
    readonly Sprite _dotSprite;
    readonly List<Image> _dots = new List<Image>();
    readonly List<MonsterHealth> _monsters = new List<MonsterHealth>();
    readonly float _floorY;
    Vector2 _center;
    float _scale = 6f, _nextScan;

    public bool Visible => _panel.activeSelf;

    public F3MonitorMap(MissionHud hud, F3MissionDirector.MapRoom[] rooms, Rect[] corridors, float floorY)
    {
        _hud = hud;
        _floorY = floorY;
        _dotSprite = MakeDot();

        var panel = hud.Img(hud.Root, "Monster Monitor", new Color(0.02f, 0.05f, 0.07f, 0.98f));
        MissionHud.Anchor(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(PanelWidth, PanelHeight));
        _panel = panel.gameObject;
        var title = hud.Txt(panel.transform, "Title", "3층 감시 화면 · 몬스터 위치", 26, MissionHud.Cyan, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        MissionHud.Anchor(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(PanelWidth, 36));
        var legend = hud.Txt(panel.transform, "Legend",
            "<color=#FF4033>●</color> 몬스터   <color=#734040>●</color> 쓰러진 몬스터   <color=#4DFF80>●</color> 대원   <color=#FFFFFF>●</color> 나      위쪽이 북쪽",
            18, new Color(1, 1, 1, 0.75f), TextAlignmentOptions.Center);
        MissionHud.Anchor(legend.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(PanelWidth, 28));

        var mapGo = new GameObject("Map", typeof(RectTransform));
        mapGo.transform.SetParent(panel.transform, false);
        _mapRoot = (RectTransform)mapGo.transform;
        MissionHud.Anchor(_mapRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -6), new Vector2(MapWidth, MapHeight));

        // 계단 (A 서쪽, B 동쪽)
        var stairA = Rect.MinMaxRect(-58f, -8f, -42f, 8f);
        var stairB = Rect.MinMaxRect(42f, -8f, 58f, 8f);
        Fit(rooms, corridors, stairA, stairB);

        if (corridors != null) foreach (var c in corridors) Area(c, CorridorColor, null);
        Area(stairA, StairColor, "A 계단");
        Area(stairB, StairColor, "B 계단");
        if (rooms != null) foreach (var r in rooms) Area(r.area, RoomColor, r.name);

        _panel.SetActive(false);
    }

    /// <summary>점으로 쓸 둥근 그림 (코드로 만든다)</summary>
    static Sprite MakeDot()
    {
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(size * 0.5f - d) * 255));
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    /// <summary>모든 구역이 지도 영역에 들어가도록 중심과 배율을 정한다</summary>
    void Fit(F3MissionDirector.MapRoom[] rooms, Rect[] corridors, Rect stairA, Rect stairB)
    {
        float x0 = stairA.xMin, x1 = stairB.xMax, z0 = stairA.yMin, z1 = stairA.yMax;
        void Grow(Rect r) { x0 = Mathf.Min(x0, r.xMin); x1 = Mathf.Max(x1, r.xMax); z0 = Mathf.Min(z0, r.yMin); z1 = Mathf.Max(z1, r.yMax); }
        if (rooms != null) foreach (var r in rooms) Grow(r.area);
        if (corridors != null) foreach (var c in corridors) Grow(c);
        _center = new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
        _scale = Mathf.Min(MapWidth / Mathf.Max(1f, x1 - x0), MapHeight / Mathf.Max(1f, z1 - z0));
    }

    /// <summary>월드 (x, z) → 지도 위 좌표. 북쪽(-z)이 위</summary>
    Vector2 ToMap(float x, float z) => new Vector2((x - _center.x) * _scale, -(z - _center.y) * _scale);

    void Area(Rect world, Color color, string label)
    {
        var img = _hud.Img(_mapRoot, label ?? "Corridor", color);
        MissionHud.Anchor(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), ToMap(world.center.x, world.center.y), world.size * _scale);
        if (string.IsNullOrEmpty(label)) return;
        var text = _hud.Txt(img.transform, "Name", label, 15, new Color(1, 1, 1, 0.8f), TextAlignmentOptions.Center);
        MissionHud.Stretch(text.rectTransform);
    }

    public void SetVisible(bool on)
    {
        if (_panel.activeSelf == on) return;
        _panel.SetActive(on);
        if (on) _nextScan = 0f;
    }

    /// <summary>보이는 동안 매 프레임: 점 위치 갱신</summary>
    public void Tick(FpsHealth me)
    {
        if (!_panel.activeSelf) return;
        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 1f;
            _monsters.Clear();
            _monsters.AddRange(Object.FindObjectsByType<MonsterHealth>(FindObjectsSortMode.None));
        }

        int used = 0;
        float blink = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));
        foreach (var m in _monsters)
        {
            if (m == null || !m.isActiveAndEnabled) continue;
            var p = m.transform.position;
            if (Mathf.Abs(p.y - _floorY) > 6f) continue;
            var body = m.GetComponent<CapsuleCollider>();
            float size = body != null && body.radius > 0.6f ? 22f : 14f;   // 큰 몬스터는 큰 점
            var color = m.IsDead ? DownColor : MonsterColor;
            color.a = m.IsDead ? 0.9f : blink;
            Dot(used++, p, size, color);
        }

        if (Stage2NetworkBootstrap.IsOnline)
        {
            foreach (var player in FpsNetworkPlayer.All)
            {
                if (player == null || player.Health == null || player.Health.IsDead) continue;
                bool self = me != null && player.Health == me;
                Dot(used++, player.transform.position, self ? 16f : 13f, self ? SelfColor : CrewColor);
            }
        }
        else if (me != null && !me.IsDead) Dot(used++, me.transform.position, 16f, SelfColor);

        for (int i = used; i < _dots.Count; i++) if (_dots[i].gameObject.activeSelf) _dots[i].gameObject.SetActive(false);
    }

    void Dot(int index, Vector3 world, float size, Color color)
    {
        while (_dots.Count <= index)
        {
            var img = _hud.Img(_mapRoot, "Dot", Color.white);
            img.sprite = _dotSprite;
            _dots.Add(img);
        }
        var dot = _dots[index];
        if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
        dot.color = color;
        MissionHud.Anchor(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), ToMap(world.x, world.z), new Vector2(size, size));
        dot.transform.SetAsLastSibling();
    }
}
