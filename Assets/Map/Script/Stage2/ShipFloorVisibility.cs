using UnityEngine;

/// <summary>
/// 우주선 맵 최적화: 로컬 카메라가 있는 층과 위·아래 한 층만 그린다.
/// - 병합된 배경 메시(Visual), 비상등(Lights), 이펙트(FX)는 GameObject 단위로 끈다.
/// - 소품(Props)은 콜라이더를 유지해야 하므로 Renderer만 끈다 (멀티플레이에서 다른 층 플레이어 물리 보존).
/// 판정은 interval 초마다 한 번, 층이 바뀔 때만 적용하므로 매 프레임 비용과 GC 할당이 없다.
/// </summary>
[DisallowMultipleComponent]
public class ShipFloorVisibility : MonoBehaviour
{
    [System.Serializable]
    public class Floor
    {
        public string name;
        public float floorY;
        public GameObject[] toggles;     // Visual / Lights / FX
        public Renderer[] propRenderers; // Props (콜라이더 유지)
    }

    public Floor[] floors;
    [Tooltip("현재 층 기준 위·아래로 함께 보여 줄 층 수")]
    [Min(0)] public int range = 1;
    [Min(0.05f)] public float interval = 0.25f;
    [Tooltip("비워 두면 Camera.main 사용")]
    public Camera targetCamera;

    int _current = -1;
    float _timer;

    void OnEnable()
    {
        _current = -1;
        _timer = 0f;
    }

    void Update()
    {
        _timer -= Time.unscaledDeltaTime;
        if (_timer > 0f) return;
        _timer = interval;

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null || floors == null || floors.Length == 0) return;

        int idx = FloorIndex(cam.transform.position.y);
        if (idx == _current) return;
        _current = idx;
        Apply();
    }

    int FloorIndex(float y)
    {
        int best = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < floors.Length; i++)
        {
            float d = Mathf.Abs(y - (floors[i].floorY + 2f));
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    void Apply()
    {
        for (int i = 0; i < floors.Length; i++)
        {
            bool on = Mathf.Abs(i - _current) <= range;
            Floor f = floors[i];
            if (f.toggles != null)
                for (int k = 0; k < f.toggles.Length; k++)
                {
                    GameObject g = f.toggles[k];
                    if (g != null && g.activeSelf != on) g.SetActive(on);
                }
            if (f.propRenderers != null)
                for (int k = 0; k < f.propRenderers.Length; k++)
                {
                    Renderer r = f.propRenderers[k];
                    if (r != null && r.enabled != on) r.enabled = on;
                }
        }
    }

    /// <summary>에디터/디버그용: 모든 층을 다시 보이게 한다.</summary>
    public void ShowAll()
    {
        _current = -1;
        foreach (Floor f in floors)
        {
            if (f.toggles != null) foreach (GameObject g in f.toggles) if (g != null) g.SetActive(true);
            if (f.propRenderers != null) foreach (Renderer r in f.propRenderers) if (r != null) r.enabled = true;
        }
    }

    void OnDisable()
    {
        if (floors != null) ShowAll();
    }
}
