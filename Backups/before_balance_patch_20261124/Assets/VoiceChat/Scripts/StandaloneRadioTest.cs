/* [ 코드 설명 ] 기존 Vivox 근접 음성·무전 채널과 HUD를 연결합니다. F 획득/R 재장전과 충돌하지 않도록 무전 송신은 V 홀딩을 사용하며, 소리 어그로 테스트는 기존 MonsterAI에만 전달합니다. */
using System.Collections.Generic;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.UIElements;
using VoidLast.Player;

[RequireComponent(typeof(UIDocument))]
public class StandaloneRadioTest : MonoBehaviour
{
    [Header("테스트 채널 이름")]
    public string proximityChannel = "Test_3D_Room";
    public string radioChannel = "Test_2D_Radio";

    [Header("3D 사운드용 가짜 플레이어 위치")]
    public Transform dummyPlayerTransform;
    public float normalVoiceRadius = 10f;
    public float radioVoiceRadius = 30f;
    public LayerMask monsterLayer;

    [Header("통합 씬 HUD 배치")]
    [Tooltip("FPS의 좌측 하단 체력 UI와 겹치지 않게 우측 상단에 배치합니다. 기존 테스트 씬은 기본 위치를 유지합니다.")]
    public bool placeHudTopRight;

    private enum HudState { Connecting, Proximity, Radio, Error }

    private const int MeterSegments = 16;
    private static readonly string[] ModeClasses = { "mode-connecting", "mode-proximity", "mode-radio", "mode-error" };
    private static readonly string[] DotClasses = { "dot-connecting", "dot-ok", "dot-radio", "dot-error" };

    private Label statusLabel;
    private Label subLabel;
    private Label memberLabel;
    private VisualElement statusDot;
    private readonly List<VisualElement> meterSegments = new List<VisualElement>();

    private bool isRadioTransmitting;
    private FpsInventory playerInventory;
    private FpsInput playerInput;
    private FpsRadioView playerRadioView;
    private bool isChannelJoined;
    private string currentChannel;
    private float displayedLevel;
    private float nextMemberRefreshTime;

    private async void Start()
    {
        BindUI();
        SetHudState(HudState.Connecting);

        try
        {
            // ParrelSync 클론끼리 충돌하지 않도록 랜덤 프로필 강제 할당
            InitializationOptions initOptions = new InitializationOptions();
            initOptions.SetProfile("TesterProfile_" + Random.Range(1000, 9999));
            await UnityServices.InitializeAsync(initOptions);

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            // 오프라인으로 층(씬)을 옮기면 이미 로그인·입장된 상태일 수 있다 → 중복 입장 오류 방지
            if (VivoxService.Instance.InitializationState == VivoxInitializationState.Uninitialized)
                await VivoxService.Instance.InitializeAsync();
            if (!VivoxService.Instance.IsLoggedIn)
            {
                LoginOptions options = new LoginOptions { DisplayName = "Tester_" + Random.Range(100, 999) };
                await VivoxService.Instance.LoginAsync(options);
                Debug.Log($"[Vivox] {options.DisplayName} 서버 로그인 성공!");
            }

            if (!VivoxService.Instance.ActiveChannels.ContainsKey(radioChannel))
                await VivoxService.Instance.JoinGroupChannelAsync(radioChannel, ChatCapability.AudioOnly);
            Channel3DProperties props = new Channel3DProperties(30, 1, 1.0f, AudioFadeModel.InverseByDistance);
            if (!VivoxService.Instance.ActiveChannels.ContainsKey(proximityChannel))
                await VivoxService.Instance.JoinPositionalChannelAsync(proximityChannel, ChatCapability.AudioOnly, props);

            isChannelJoined = true;
            SwitchTransmissionMode(proximityChannel);
            SetHudState(HudState.Proximity);

            Debug.Log("[Vivox] 채널 접속 및 마이크 세팅 완료!");
        }
        catch (System.Exception e)
        {
            // 에러가 나도 화면이 '연결 중'에서 멈추지 않도록 UI에 표시
            Debug.LogError($"[Vivox] 초기화 실패: {e}");
            string message = e.Message;
            if (message.Length > 90) message = message.Substring(0, 90) + "...";
            SetHudState(HudState.Error, message);
        }
    }

    private void Update()
    {
        UpdateMicMeter();
        if (!isChannelJoined) return;

        UpdateMemberCount();
        Send3DPosition();

        // T 키: 일반 말소리 어그로 발생 (테스트용)
        if (Input.GetKeyDown(KeyCode.T))
        {
            EmitSound(false);
            Debug.Log($"일반 말소리를 냅니다. (반경 {normalVoiceRadius}m)");
        }

        // V 키 또는 무전기를 손에 들고 좌클릭: 누르는 동안만 송출 (멀티플레이 InGameVoiceChat과 같은 규칙). R은 FPS 재장전으로 유지한다.
        bool pushToTalk = Input.GetKey(KeyCode.V) || (RadioInHand() && playerInput != null && playerInput.AttackHeld && UnityEngine.Cursor.lockState == CursorLockMode.Locked);
        if (pushToTalk && !isRadioTransmitting) StartRadio();
        else if (!pushToTalk && isRadioTransmitting) StopRadio();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        // 송신 키를 누른 채 창 포커스를 잃으면 KeyUp을 놓쳐 송출이 계속되는 문제 방지
        if (!hasFocus && isRadioTransmitting) StopRadio();
    }

    private void OnDestroy()
    {
        if (isChannelJoined) SafeLogout();
    }

    private static async void SafeLogout()
    {
        try
        {
            if (VivoxService.Instance.IsLoggedIn)
                await VivoxService.Instance.LogoutAsync();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Vivox] 로그아웃 중 경고: {e.Message}");
        }
    }

    // ---------------------------------------------------------------- 무전기

    private void StartRadio()
    {
        isRadioTransmitting = true;
        if (playerRadioView != null) playerRadioView.Talking = true;   // 손에 든 무전기 LED
        SwitchTransmissionMode(radioChannel);
        SetHudState(HudState.Radio);
        EmitSound(true);
        Debug.Log($"<color=yellow>[무전기 송출 중] 2D 채널로 말합니다. (반경 {radioVoiceRadius}m 어그로)</color>");
    }

    private void StopRadio()
    {
        isRadioTransmitting = false;
        if (playerRadioView != null) playerRadioView.Talking = false;
        SwitchTransmissionMode(proximityChannel);
        SetHudState(HudState.Proximity);
        Debug.Log("<color=white>[무전기 종료] 3D 대화 모드로 복귀했습니다.</color>");
    }

    private async void SwitchTransmissionMode(string targetChannel)
    {
        currentChannel = targetChannel;
        try
        {
            await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.Single, targetChannel);
        }
        catch (System.Exception e) { Debug.LogWarning(e.Message); }
    }

    // ---------------------------------------------------------------- 3D 위치 / 몬스터 어그로

private void Send3DPosition()
    {
        if (dummyPlayerTransform == null) return;

        try
        {
            // Vivox 상태가 초기화되면 ActiveChannels가 null일 수 있어 try 안에서 확인
            var channels = VivoxService.Instance.ActiveChannels;
            if (channels == null || !channels.ContainsKey(proximityChannel)) return;

            VivoxService.Instance.Set3DPosition(dummyPlayerTransform.gameObject, proximityChannel);
        }
        catch (System.Exception)
        {
            // 통신이 아주 잠깐 끊겼을 때 발생하는 에러 도배를 조용히 무시
        }
    }

    private void EmitSound(bool isRadio)
    {
        float currentRadius = isRadio ? radioVoiceRadius : normalVoiceRadius;

        // 소리의 기준점은 이 오브젝트(voice chat)가 아니라 플레이어 위치
        Transform source = dummyPlayerTransform != null ? dummyPlayerTransform : transform;

        Collider[] hitColliders = Physics.OverlapSphere(source.position, currentRadius, monsterLayer);
        foreach (var hit in hitColliders)
        {
            MonsterAI monster = hit.GetComponent<MonsterAI>();
            if (monster != null)
            {
                monster.OnHearSound(source, isRadio);
            }
        }
    }

    // ---------------------------------------------------------------- HUD

    private void BindUI()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        var panel = root.Q<VisualElement>("Root");
        if (placeHudTopRight && panel != null)
        {
            panel.style.justifyContent = Justify.FlexStart;
            panel.style.alignItems = Align.FlexEnd;
        }
        statusLabel = root.Q<Label>("StatusLabel");
        subLabel = root.Q<Label>("SubLabel");
        memberLabel = root.Q<Label>("MemberLabel");
        statusDot = root.Q<VisualElement>("StatusDot");

        var meter = root.Q<VisualElement>("MicMeter");
        meterSegments.Clear();
        if (meter == null) return;

        meter.Clear();
        for (int i = 0; i < MeterSegments; i++)
        {
            var segment = new VisualElement();
            segment.AddToClassList("meter-seg");
            meter.Add(segment);
            meterSegments.Add(segment);
        }
    }

    private void SetHudState(HudState state, string detail = null)
    {
        string title;
        string sub;
        switch (state)
        {
            case HudState.Proximity:
                title = "3D 근접 대화";
                sub = "가까이 있는 플레이어에게 들려요";
                break;
            case HudState.Radio:
                title = "무전기 송출 중";
                sub = "무전기 채널 전체에 전달돼요";
                break;
            case HudState.Error:
                title = "연결 실패";
                sub = string.IsNullOrEmpty(detail) ? "콘솔 로그를 확인해 주세요" : detail;
                break;
            default:
                title = "서버 연결 중...";
                sub = "Unity Services · Vivox 로그인 중";
                break;
        }

        if (statusLabel != null) statusLabel.text = title;
        if (subLabel != null) subLabel.text = sub;
        SetExclusiveClass(statusLabel, ModeClasses, ModeClasses[(int)state]);
        SetExclusiveClass(statusDot, DotClasses, DotClasses[(int)state]);

        if (memberLabel != null)
        {
            bool connected = state == HudState.Proximity || state == HudState.Radio;
            memberLabel.style.display = connected ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private static void SetExclusiveClass(VisualElement element, string[] allClasses, string activeClass)
    {
        if (element == null) return;
        foreach (var c in allClasses)
            element.EnableInClassList(c, c == activeClass);
    }

    private void UpdateMicMeter()
    {
        if (meterSegments.Count == 0) return;

        float target = 0f;
        if (isChannelJoined && !string.IsNullOrEmpty(currentChannel))
            target = ReadMicEnergy();

        // 올라갈 때는 빠르게, 내려갈 때는 부드럽게
        float speed = target > displayedLevel ? 30f : 8f;
        displayedLevel = Mathf.Lerp(displayedLevel, target, 1f - Mathf.Exp(-speed * Time.deltaTime));

        int lit = Mathf.RoundToInt(displayedLevel * meterSegments.Count);
        for (int i = 0; i < meterSegments.Count; i++)
        {
            bool on = i < lit;
            var segment = meterSegments[i];
            segment.EnableInClassList("meter-seg-low", on && i < 10);
            segment.EnableInClassList("meter-seg-mid", on && i >= 10 && i < 13);
            segment.EnableInClassList("meter-seg-high", on && i >= 13);
        }
    }

    // Vivox가 알려주는 내 마이크의 실제 음성 에너지(0~1)
    private float ReadMicEnergy()
    {
        try
        {
            if (!VivoxService.Instance.ActiveChannels.TryGetValue(currentChannel, out var participants))
                return 0f;

            foreach (var participant in participants)
            {
                if (participant.IsSelf)
                    return Mathf.Clamp01(Mathf.Sqrt((float)participant.AudioEnergy) * 1.2f);
            }
        }
        catch (System.Exception)
        {
            // 채널 정보가 바뀌는 순간의 일시적 오류는 무시
        }
        return 0f;
    }

private void UpdateMemberCount()
    {
        if (memberLabel == null || Time.unscaledTime < nextMemberRefreshTime) return;
        nextMemberRefreshTime = Time.unscaledTime + 0.5f;

        try
        {
            var channels = VivoxService.Instance.ActiveChannels;
            if (channels != null && channels.TryGetValue(radioChannel, out var participants))
                memberLabel.text = $"접속 {participants.Count}명";
        }
        catch (System.Exception)
        {
            // 채널 정보가 바뀌는 순간의 일시적 오류는 무시
        }
    }


// 오프라인 테스트용: 씬에 놓인 플레이어가 무전기를 손에 들고 있는지
    private bool RadioInHand()
    {
        if (playerInventory == null && dummyPlayerTransform != null)
        {
            playerInventory = dummyPlayerTransform.GetComponentInParent<FpsInventory>();
            playerInput = dummyPlayerTransform.GetComponentInParent<FpsInput>();
            playerRadioView = dummyPlayerTransform.GetComponentInParent<FpsRadioView>();
        }
        return playerInventory != null && playerInventory.IsAlive && playerInventory.ItemAt(playerInventory.SelectedIndex) == FarmingItemKind.Radio;
    }
}
