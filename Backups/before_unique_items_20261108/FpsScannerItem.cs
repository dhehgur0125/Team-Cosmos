/* [ 코드 설명 ] 파밍 슬롯(4·5·6)의 스캐너 아이템을 손에 든 모습과 사용을 담당합니다. 스캐너를 들고 좌클릭하면 같은 대원의 ItemDetector가 스캔하고 15초 쿨타임이 돌며, 1인칭 반동과 쿨타임 표시를 보여 줍니다. 획득·드랍·핫바는 기존 FpsWorldPickup/FpsInventory가 그대로 맡고, 모델은 Resources/FarmingItems/Scanner 프리팹의 ScannerModel을 복제합니다. ItemDetector가 Awake에서 자동으로 붙이므로 플레이어 프리팹은 수정하지 않습니다. */
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using VoidLast.Player;

[DisallowMultipleComponent]
public sealed class FpsScannerItem : MonoBehaviour
{
    // 바닥 프리팹의 모델 자식 이름. FpsCarriedItemView는 "Model"만 복제하므로 이름을 다르게 두어 같은 모델이 두 번 보이지 않게 한다
    public const string ModelChildName = "ScannerModel";

    [Header("1인칭 자세 (FPS Camera 기준)")]
    public Vector3 heldPosition = new Vector3(0.2f, -0.2f, 0.42f);
    public Vector3 heldEuler = new Vector3(0f, 180f, 0f);
    [Min(0.1f)] public float firstPersonScale = 1f;
    [Range(0.2f, 0.6f)] public float equipDuration = 0.35f;
    public Vector3 equipOffset = new Vector3(0, -0.35f, -0.08f);
    [Tooltip("스캔할 때 뒤로 밀리고 들리는 정도")]
    public Vector3 recoilOffset = new Vector3(0f, 0.02f, -0.07f);
    public float recoilPitch = -14f;

    [Header("1인칭 장갑 (스캐너 모델 기준)")]
    // 권총(WeaponRoot)에서 장갑이 손잡이를 쥐는 위치 관계를 레이저건 손잡이(모델 기준 y≈0.05, z≈-0.08, 총구 -Z)에 옮긴 값
    public bool showGlove = true;
    public Vector3 glovePosition = new Vector3(0f, 0.065f, -0.12f);
    public Vector3 gloveEuler = new Vector3(90f, 180f, 0f);

    [Header("3인칭 (손 소켓 기준)")]
    public Vector3 worldPosition = Vector3.zero;
    public Vector3 worldEuler = Vector3.zero;
    [Min(0.1f)] public float worldScale = 0.8f;

    [Header("쿨타임 표시")]
    public bool showCooldownHud = true;

    FpsInventory inventory;
    FpsEquipment equipment;
    FpsInput input;
    ItemDetector detector;
    Transform fpRoot, worldRoot;
    float equip, recoil;
    bool wasHeld;

    GameObject hudRoot;
    Text hudText;
    Image hudFill;

    public bool IsHeld { get; private set; }
    public bool IsEquipped => IsHeld && equip >= 1f;
    public ItemDetector Detector => detector;
    bool IsLocal => inventory != null && !inventory.IsNetworkProxy && input != null && input.isActiveAndEnabled;

    void Awake()
    {
        inventory = GetComponent<FpsInventory>();
        equipment = GetComponent<FpsEquipment>();
        input = GetComponent<FpsInput>();
        detector = GetComponent<ItemDetector>();
        BuildViews();
    }

    void OnDestroy()
    {
        if (hudRoot != null) Destroy(hudRoot);
    }

    // ------------------------------------------------------------------ 모델

    void BuildViews()
    {
        var pickup = FarmingItems.PickupPrefab(FarmingItemKind.Scanner);
        var model = pickup != null ? pickup.transform.Find(ModelChildName) : null;
        if (model == null)
        {
            Debug.LogWarning("[스캐너] Resources/FarmingItems/Scanner 프리팹의 " + ModelChildName + "를 찾지 못해 손에 든 모습을 만들지 않았습니다.", this);
            return;
        }

        // 무전기와 같은 자리(1인칭 카메라 앞, 3인칭 손 소켓)를 쓴다
        var radio = GetComponent<FpsRadioView>();
        Transform firstPersonParent = radio != null ? radio.firstPersonParent : null;
        Transform gloveSource = radio != null ? radio.gloveSource : null;
        Transform worldSocket = radio != null ? radio.worldSocket : null;

        var fpRenderers = new List<Renderer>();
        var worldRenderers = new List<Renderer>();

        if (firstPersonParent != null)
        {
            fpRoot = new GameObject("ScannerRoot (1P)").transform;
            fpRoot.SetParent(firstPersonParent, false);
            int layer = gloveSource != null ? gloveSource.gameObject.layer : firstPersonParent.gameObject.layer;
            var gun = Instantiate(model.gameObject, fpRoot, false);
            gun.name = "Scanner (1P)";
            gun.transform.localPosition = Vector3.zero;
            gun.transform.localRotation = Quaternion.identity;
            gun.transform.localScale = Vector3.one;
            if (showGlove && gloveSource != null)
            {
                var glove = Instantiate(gloveSource.gameObject, fpRoot, false);
                glove.name = "Scanner Glove (1P)";
                glove.transform.localPosition = glovePosition;
                glove.transform.localRotation = Quaternion.Euler(gloveEuler);
                glove.transform.localScale = gloveSource.localScale;
                glove.SetActive(true);
            }
            Prepare(fpRoot, layer, fpRenderers);
            fpRoot.gameObject.SetActive(false);
        }

        if (worldSocket != null)
        {
            // WorldItemSocket은 주사기를 들 때만 켜지므로 같은 자리에 스캐너 전용 소켓을 따로 만든다 (무전기와 같은 방식)
            var holder = new GameObject("ScannerSocket (3P)").transform;
            holder.SetParent(worldSocket.parent != null ? worldSocket.parent : worldSocket, false);
            holder.localPosition = worldSocket.localPosition;
            holder.localRotation = worldSocket.localRotation;
            holder.localScale = worldSocket.localScale;
            worldRoot = Instantiate(model.gameObject, holder, false).transform;
            worldRoot.name = "Scanner (3P)";
            Prepare(worldRoot, worldSocket.gameObject.layer, worldRenderers);
            worldRoot.gameObject.SetActive(false);
        }

        var visibility = GetComponent<FpsLocalVisibility>();
        if (visibility != null) visibility.AddRenderers(worldRenderers.ToArray(), fpRenderers.ToArray());
    }

    static void Prepare(Transform root, int layer, List<Renderer> renderers)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.Off; renderers.Add(r); }
        foreach (var c in root.GetComponentsInChildren<Collider>(true)) Destroy(c);
    }

    // ------------------------------------------------------------------ 사용

    void Update()
    {
        if (!IsLocal || detector == null || !IsEquipped) return;
        if (FpsInput.Blocked || Cursor.lockState != CursorLockMode.Locked || !Application.isFocused) return;
        if (input.Attack && detector.TryUseScanner()) recoil = 1f;
    }

    /// <summary>검사·디버그용: 키 입력 없이 좌클릭 사용과 같은 조건으로 스캔한다</summary>
    public bool TryUse()
    {
        if (!IsLocal || detector == null || !IsEquipped) return false;
        if (!detector.TryUseScanner()) return false;
        recoil = 1f;
        return true;
    }

    void LateUpdate()
    {
        bool held = inventory != null && inventory.IsAlive && inventory.ItemAt(inventory.SelectedIndex) == FarmingItemKind.Scanner;
        if (held != wasHeld) { wasHeld = held; equip = 0f; recoil = 0f; }
        IsHeld = held;
        if (fpRoot != null && fpRoot.gameObject.activeSelf != held) fpRoot.gameObject.SetActive(held);
        if (worldRoot != null && worldRoot.gameObject.activeSelf != held) worldRoot.gameObject.SetActive(held);
        UpdateHud(held);
        if (!held) return;

        float dt = Time.deltaTime;
        equip = Mathf.Min(1f, equip + dt / Mathf.Max(0.05f, equipDuration));
        recoil = Mathf.MoveTowards(recoil, 0f, dt * 4f);
        float e = Mathf.SmoothStep(0, 1, equip), k = Mathf.SmoothStep(0, 1, recoil);

        if (fpRoot != null)
        {
            fpRoot.localPosition = heldPosition + recoilOffset * k + equipOffset * (1 - e);
            fpRoot.localRotation = Quaternion.Euler(heldEuler) * Quaternion.Euler(recoilPitch * k, 0, 0);
            fpRoot.localScale = Vector3.one * firstPersonScale;
        }
        if (worldRoot != null)
        {
            worldRoot.localPosition = worldPosition;
            worldRoot.localRotation = Quaternion.Euler(worldEuler);
            worldRoot.localScale = Vector3.one * worldScale;
        }
    }

    // ------------------------------------------------------------------ 쿨타임 표시 (내 화면에만)

    void UpdateHud(bool held)
    {
        bool show = showCooldownHud && held && IsLocal && detector != null;
        if (!show) { if (hudRoot != null && hudRoot.activeSelf) hudRoot.SetActive(false); return; }
        if (hudRoot == null) BuildHud();
        if (!hudRoot.activeSelf) hudRoot.SetActive(true);

        float remaining = detector.CooldownRemaining;
        if (remaining > 0f)
        {
            hudText.text = "SCAN  " + Mathf.CeilToInt(remaining) + "s";
            hudText.color = new Color(1f, 1f, 1f, 0.7f);
            hudFill.fillAmount = 1f - Mathf.Clamp01(remaining / Mathf.Max(0.01f, detector.LastCooldownDuration));
            hudFill.color = new Color(1f, 1f, 1f, 0.35f);
        }
        else
        {
            hudText.text = "SCAN READY  [LMB]";
            hudText.color = new Color(0.55f, 0.95f, 1f, 1f);
            hudFill.fillAmount = 1f;
            hudFill.color = new Color(0.3f, 0.85f, 1f, 0.9f);
        }
    }

    void BuildHud()
    {
        hudRoot = new GameObject("ScannerHUD", typeof(RectTransform));
        var canvas = hudRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        var scaler = hudRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var panel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
        panel.SetParent(hudRoot.transform, false);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, 190f);
        panel.sizeDelta = new Vector2(260f, 34f);

        var back = NewImage(panel, "Back", new Color(0.04f, 0.05f, 0.08f, 0.7f));
        Stretch(back.rectTransform, 0, 0, 0, 0);
        hudFill = NewImage(panel, "Fill", Color.white);
        Stretch(hudFill.rectTransform, 0, 0, 0, 30);
        hudFill.type = Image.Type.Filled;
        hudFill.fillMethod = Image.FillMethod.Horizontal;
        hudFill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer));
        textGo.transform.SetParent(panel, false);
        hudText = textGo.AddComponent<Text>();
        hudText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hudText.fontSize = 18;
        hudText.fontStyle = FontStyle.Bold;
        hudText.alignment = TextAnchor.MiddleCenter;
        hudText.raycastTarget = false;
        Stretch(hudText.rectTransform, 0, 0, 0, 4);
        hudRoot.SetActive(false);
    }

    static Image NewImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    // left, right, top, bottom 여백(px)
    static void Stretch(RectTransform r, float left, float right, float top, float bottom)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(left, bottom);
        r.offsetMax = new Vector2(-right, -top);
    }
}
