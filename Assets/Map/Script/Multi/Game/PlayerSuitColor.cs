using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 대원 번호(로비 1~4P)에 따라 우주복 색을 바꾼다. 1P 주황(원래), 2P 파랑, 3P 초록, 4P 보라.
///  - 바뀌는 재질: 몸통·신발·1인칭 장갑(Astroanut_Suit_Body), 멜빵·가방 등 부속(Astroanut_Suit_Details), 1인칭 팔(gloved arms mat URP).
///    헬멧 유리·얼굴은 그대로.
///  - 색 바꾼 이미지 파일은 저장소에 두지 않는다(LFS 용량). 처음 대원이 생길 때(맵 로딩 중) 원본 텍스처의 주황색 부분만
///    셰이더(Resources/SuitColors/SuitHueShift)로 색을 돌려 2~4P 텍스처를 한 번에 만들고, 게임이 끝날 때까지 다시 쓴다.
///  - 로비(LobbyManager)가 내 칸 번호를 LocalSlot에 적고, FpsNetworkPlayer가 그 번호를 다른 대원에게 동기화한다.
/// </summary>
public static class PlayerSuitColor
{
    public const int SlotCount = 4;
    // 몸통·신발·1인칭 오른손 장갑 / 멜빵·가방 / 1인칭 팔(총 든 장갑)
    static readonly string[] BaseNames = { "Astroanut_Suit_Body", "Astroanut_Suit_Details", "gloved arms mat URP" };
    // 2P 파랑, 3P 초록, 4P 보라 (원래 주황은 색상 약 15°)
    static readonly float[] TargetHue = { 0f, 213f, 128f, 278f };
    static readonly float[] SatMul = { 1f, 0.95f, 0.85f, 0.85f };
    static readonly float[] ValMul = { 1f, 0.92f, 0.85f, 0.9f };
    const string VariantTag = " (P";   // 만든 재질 이름: "원래 이름 (P2)"

    static readonly Dictionary<(Material, int), Material> Variants = new Dictionary<(Material, int), Material>();
    static readonly List<Object> Created = new List<Object>();
    static Material _shiftMaterial;

    /// <summary>로비에서 내 칸 번호 (0 = 1P). 로비를 거치지 않았으면 -1</summary>
    public static int LocalSlot = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        LocalSlot = -1;
        Created.Clear();   // 이전 플레이에서 만든 것은 플레이가 끝날 때 함께 정리된다
        Variants.Clear();
        _shiftMaterial = null;
    }

    /// <summary>root 아래 우주복 재질을 slot 번호 색으로 바꾼다. 원래(1P) 재질은 기억해 두었다가 되돌린다.</summary>
    public static void Apply(GameObject root, int slot)
    {
        if (root == null) return;
        slot = Mathf.Clamp(slot, 0, SlotCount - 1);
        var keeper = root.GetComponent<SuitColorOriginal>() ?? root.AddComponent<SuitColorOriginal>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var m = materials[i];
                if (m == null) continue;
                string baseName = BaseOf(m.name);
                if (baseName == null) continue;
                if (m.name == baseName)
                {
                    keeper.Remember(baseName, m);
                    Prewarm(m);   // 처음 보는 원래 재질이면 2~4P를 미리 다 만든다 (로딩 중 한 번)
                }
                var original = keeper.Original(baseName);
                var target = slot == 0 ? original : Variant(original, slot);
                if (target == null || target == m) continue;
                materials[i] = target;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = materials;
        }
    }

    static string BaseOf(string materialName)
    {
        foreach (var b in BaseNames)
            if (materialName == b || materialName.StartsWith(b + VariantTag)) return b;
        return null;
    }

    static void Prewarm(Material original)
    {
        for (int s = 1; s < SlotCount; s++) Variant(original, s);
    }

    /// <summary>원래 재질의 slot 색 버전 (없으면 지금 만든다)</summary>
    static Material Variant(Material original, int slot)
    {
        if (original == null) return null;
        if (Variants.TryGetValue((original, slot), out var cached) && cached != null) return cached;
        var source = original.HasProperty("_BaseMap") ? original.GetTexture("_BaseMap") : original.mainTexture;
        var recolored = Recolor(source, slot);
        if (recolored == null) return null;
        var m = new Material(original) { name = original.name + VariantTag + (slot + 1) + ")" };
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", recolored);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", recolored);
        Created.Add(m);
        Variants[(original, slot)] = m;
        return m;
    }

    /// <summary>원본 텍스처의 주황 부분만 색을 돌린 압축 텍스처를 만든다 (GPU에서 변환 → 한 번 읽어서 압축)</summary>
    static Texture2D Recolor(Texture source, int slot)
    {
        if (source == null) return null;
        if (_shiftMaterial == null)
        {
            var shader = Resources.Load<Shader>("SuitColors/SuitHueShift");
            if (shader == null || !shader.isSupported) { Debug.LogWarning("[우주복 색] 색 변환 셰이더를 찾지 못해 원래 색으로 둡니다"); return null; }
            _shiftMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            Created.Add(_shiftMaterial);
        }
        _shiftMaterial.SetFloat("_Shift", (TargetHue[slot] - 15f) / 360f);
        _shiftMaterial.SetFloat("_SatMul", SatMul[slot]);
        _shiftMaterial.SetFloat("_ValMul", ValMul[slot]);

        int w = source.width, h = source.height;
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = RenderTexture.active;
        Graphics.Blit(source, rt, _shiftMaterial);
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, false) { name = source.name + " (P" + (slot + 1) + ")", wrapMode = source.wrapMode, filterMode = source.filterMode, anisoLevel = Mathf.Max(1, source.anisoLevel) };
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        tex.Apply(true, false);
        tex.Compress(false);              // 원본처럼 압축해 메모리를 줄인다
        tex.Apply(false, true);           // CPU 쪽 사본은 버린다
        Created.Add(tex);
        return tex;
    }
}

/// <summary>PlayerSuitColor가 원래(1P 주황) 재질을 기억해 두는 곳</summary>
[DisallowMultipleComponent]
public sealed class SuitColorOriginal : MonoBehaviour
{
    readonly Dictionary<string, Material> _originals = new Dictionary<string, Material>();
    public void Remember(string baseName, Material m) { if (!_originals.ContainsKey(baseName)) _originals[baseName] = m; }
    public Material Original(string baseName) => _originals.TryGetValue(baseName, out var m) ? m : null;
}
