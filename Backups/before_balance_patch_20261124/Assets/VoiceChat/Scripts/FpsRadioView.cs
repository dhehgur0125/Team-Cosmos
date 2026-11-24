using UnityEngine;
using UnityEngine.Rendering;
using VoidLast.Player;

/// <summary>
/// 무전기 아이템을 손에 들었을 때의 모습.
///  - 1인칭: 카메라 앞에 장갑 낀 손과 무전기 (말하는 동안 입 쪽으로 들어 올림)
///  - 3인칭: 오른손 소켓에 무전기 (다른 대원 화면에서 보임)
/// 송신 중에는 LED가 깜빡인다. 실제 음성 송수신은 InGameVoiceChat이 담당한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class FpsRadioView : MonoBehaviour
{
    [Tooltip("무전기 모델 프리팹 (VOID_LAST_RadioModel)")]
    public GameObject radioModelPrefab;
    [Tooltip("1인칭 무전기를 붙일 곳 (FPS Camera)")]
    public Transform firstPersonParent;
    [Tooltip("1인칭 손 모양으로 복제할 장갑 (FPS Right Glove)")]
    public Transform gloveSource;
    [Tooltip("3인칭 무전기를 붙일 손 소켓 (WorldItemSocket)")]
    public Transform worldSocket;

    [Header("1인칭 자세")]
    public Vector3 heldPosition = new Vector3(0.2f, -0.2f, 0.4f);
    public Vector3 heldEuler = new Vector3(-12, -158, -6);
    public Vector3 talkPosition = new Vector3(0.1f, -0.13f, 0.3f);
    public Vector3 talkEuler = new Vector3(-28, -140, -12);
    [Min(0.1f)] public float firstPersonScale = 1.25f;
    [Range(0.2f, 0.6f)] public float equipDuration = 0.35f;
    public Vector3 equipOffset = new Vector3(0, -0.35f, -0.08f);

    [Header("3인칭 (손 소켓 기준)")]
    public Vector3 worldPosition = Vector3.zero;
    public Vector3 worldEuler = Vector3.zero;
    [Min(0.1f)] public float worldScale = 1f;

    [Header("LED")]
    [ColorUsage(false, true)] public Color ledIdle = new Color(0.1f, 0.9f, 0.3f) * 1.5f;
    [ColorUsage(false, true)] public Color ledTalk = new Color(1f, 0.15f, 0.1f) * 4f;

    /// <summary>무전기로 송신 중인지 (내 캐릭터는 InGameVoiceChat, 다른 대원은 FpsNetworkPlayer가 설정)</summary>
    public bool Talking { get; set; }
    public bool IsHeld { get; private set; }

    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    FpsInventory inventory;
    FpsUniqueItem unique;
    Transform fpRoot, worldRoot;
    Renderer[] ledRenderers;
    MaterialPropertyBlock block;
    float equip, talk;

    void Awake()
    {
        inventory = GetComponent<FpsInventory>();
        unique = FpsUniqueItem.For(gameObject);
        block = new MaterialPropertyBlock();
        // 배터리·임무 물건도 무전기와 같은 자리에 들고 있는 모습을 보여 준다
        var carried = GetComponent<FpsCarriedItemView>();
        if (carried == null) carried = gameObject.AddComponent<FpsCarriedItemView>();
        carried.Configure(firstPersonParent, gloveSource, worldSocket);
        if (radioModelPrefab == null) return;

        var fpRenderers = new System.Collections.Generic.List<Renderer>();
        var worldRenderers = new System.Collections.Generic.List<Renderer>();
        var leds = new System.Collections.Generic.List<Renderer>();

        if (firstPersonParent != null)
        {
            fpRoot = new GameObject("RadioRoot (1P)").transform;
            fpRoot.SetParent(firstPersonParent, false);
            int layer = gloveSource != null ? gloveSource.gameObject.layer : firstPersonParent.gameObject.layer;
            var model = Instantiate(radioModelPrefab, fpRoot, false);
            model.name = "Radio (1P)";
            if (gloveSource != null)
            {
                var glove = Instantiate(gloveSource.gameObject, fpRoot, false);
                glove.name = "Radio Glove (1P)";
                glove.transform.localPosition = gloveSource.localPosition;
                glove.transform.localRotation = gloveSource.localRotation;
                glove.transform.localScale = gloveSource.localScale;
                glove.SetActive(true);
            }
            foreach (var t in fpRoot.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            Collect(fpRoot, fpRenderers, leds);
            fpRoot.gameObject.SetActive(false);
        }

        if (worldSocket != null)
        {
            // WorldItemSocket은 주사기를 들 때만 켜지므로, 같은 자리에 무전기 전용 소켓을 따로 만든다
            var holder = new GameObject("RadioSocket (3P)").transform;
            holder.SetParent(worldSocket.parent != null ? worldSocket.parent : worldSocket, false);
            holder.localPosition = worldSocket.localPosition;
            holder.localRotation = worldSocket.localRotation;
            holder.localScale = worldSocket.localScale;
            worldRoot = Instantiate(radioModelPrefab, holder, false).transform;
            worldRoot.name = "Radio (3P)";
            worldRoot.localPosition = worldPosition;
            worldRoot.localRotation = Quaternion.Euler(worldEuler);
            worldRoot.localScale = Vector3.one * worldScale;
            foreach (var t in worldRoot.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = worldSocket.gameObject.layer;
            Collect(worldRoot, worldRenderers, leds);
            worldRoot.gameObject.SetActive(false);
        }

        ledRenderers = leds.ToArray();
        var visibility = GetComponent<FpsLocalVisibility>();
        if (visibility != null) visibility.AddRenderers(worldRenderers.ToArray(), fpRenderers.ToArray());
    }

    static void Collect(Transform root, System.Collections.Generic.List<Renderer> renderers, System.Collections.Generic.List<Renderer> leds)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            renderers.Add(r);
            if (r.name.Contains("LED")) leds.Add(r);
        }
        foreach (var c in root.GetComponentsInChildren<Collider>(true)) Destroy(c);
    }

    void LateUpdate()
    {
        // 4~6번 칸의 무전기 또는 3번 칸의 고유 아이템 무전
        bool held = inventory != null && inventory.IsAlive && (inventory.ItemAt(inventory.SelectedIndex) == FarmingItemKind.Radio || unique != null && unique.InHand(UniqueItemKind.Radio));
        if (held != IsHeld) { IsHeld = held; equip = 0; }
        if (fpRoot != null && fpRoot.gameObject.activeSelf != held) fpRoot.gameObject.SetActive(held);
        if (worldRoot != null && worldRoot.gameObject.activeSelf != held) worldRoot.gameObject.SetActive(held);
        if (!held) { talk = 0; return; }

        float dt = Time.deltaTime;
        equip = Mathf.Min(1, equip + dt / Mathf.Max(0.05f, equipDuration));
        talk = Mathf.MoveTowards(talk, Talking ? 1 : 0, dt * 5f);
        float e = Mathf.SmoothStep(0, 1, equip), k = Mathf.SmoothStep(0, 1, talk);

        if (fpRoot != null)
        {
            fpRoot.localPosition = Vector3.Lerp(heldPosition, talkPosition, k) + equipOffset * (1 - e);
            fpRoot.localRotation = Quaternion.Slerp(Quaternion.Euler(heldEuler), Quaternion.Euler(talkEuler), k);
            fpRoot.localScale = Vector3.one * firstPersonScale;
        }
        if (worldRoot != null)
        {
            worldRoot.localPosition = worldPosition;
            worldRoot.localRotation = Quaternion.Euler(worldEuler);
            worldRoot.localScale = Vector3.one * worldScale;
        }

        if (ledRenderers != null)
        {
            bool blink = Talking && Mathf.Repeat(Time.time * 6f, 1f) < 0.55f;
            Color c = Talking ? (blink ? ledTalk : ledTalk * 0.25f) : ledIdle;
            foreach (var r in ledRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(EmissionId, c);
                r.SetPropertyBlock(block);
            }
        }
    }
}
