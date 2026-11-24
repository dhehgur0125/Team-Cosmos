using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 전체를 검게 덮는 페이드 (씬이 바뀌어도 유지).
/// 로딩 화면(1000)과 게임 오버 화면(1100)보다 아래, 게임 HUD보다 위(990)에 그린다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ScreenFader : MonoBehaviour
{
    static ScreenFader s_instance;
    CanvasGroup _group;
    float _target, _speed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { s_instance = null; }

    public static ScreenFader Ensure()
    {
        if (s_instance != null) return s_instance;
        var go = new GameObject("Screen Fader", typeof(RectTransform));
        DontDestroyOnLoad(go);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 990;
        s_instance = go.AddComponent<ScreenFader>();
        s_instance._group = go.AddComponent<CanvasGroup>();
        s_instance._group.alpha = 0f;
        s_instance._group.blocksRaycasts = false;
        s_instance._group.interactable = false;
        var img = new GameObject("Black", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        img.transform.SetParent(go.transform, false);
        var r = (RectTransform)img.transform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        var i = img.GetComponent<Image>(); i.color = Color.black; i.raycastTarget = false;
        return s_instance;
    }

    public static float Alpha => s_instance != null ? s_instance._group.alpha : 0f;
    public static bool IsBlack => Alpha >= 0.999f;

    /// <summary>있으면 바로 투명하게 (로비로 돌아왔을 때)</summary>
    public static void Clear()
    {
        if (s_instance == null) return;
        s_instance._group.alpha = s_instance._target = 0f;
    }

    /// <summary>바로 그 밝기로</summary>
    public static void Set(float alpha)
    {
        var f = Ensure();
        f._group.alpha = f._target = Mathf.Clamp01(alpha);
    }

    /// <summary>seconds 동안 alpha(0=안 보임, 1=검정)로 바뀐다</summary>
    public static void FadeTo(float alpha, float seconds)
    {
        var f = Ensure();
        f._target = Mathf.Clamp01(alpha);
        f._speed = 1f / Mathf.Max(0.01f, seconds);
    }

    void Update()
    {
        _group.alpha = Mathf.MoveTowards(_group.alpha, _target, _speed * Mathf.Min(Time.unscaledDeltaTime, 0.1f));
    }
}
