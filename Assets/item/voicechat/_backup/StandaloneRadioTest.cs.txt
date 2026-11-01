using UnityEngine;
using UnityEngine.UIElements;
using Unity.Services.Core;
using Unity.Services.Vivox;
using Unity.Services.Authentication;
using System.Threading.Tasks;

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

    private Label statusLabel;
    private ProgressBar micVolumeBar;
    private bool isRadioTransmitting = false;
    private bool isChannelJoined = false; 

    async void Start()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        statusLabel = root.Q<Label>("StatusLabel");
        micVolumeBar = root.Q<ProgressBar>("MicVolumeBar");
        
        UpdateUIState(false);
        if (statusLabel != null) statusLabel.text = "[ 서버 연결 중... ]";

        // 🌟 에러 해결: ParrelSync 클론끼리 충돌하지 않도록 랜덤 프로필 강제 할당
        InitializationOptions initOptions = new InitializationOptions();
        initOptions.SetProfile("TesterProfile_" + Random.Range(1000, 9999));
        await UnityServices.InitializeAsync(initOptions);

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();

        await VivoxService.Instance.InitializeAsync();
        LoginOptions options = new LoginOptions { DisplayName = "Tester_" + Random.Range(100, 999) };
        await VivoxService.Instance.LoginAsync(options);
        
        Debug.Log($"[Vivox] {options.DisplayName} 서버 로그인 성공!");

        await VivoxService.Instance.JoinGroupChannelAsync(radioChannel, ChatCapability.AudioOnly);
        Channel3DProperties props = new Channel3DProperties(30, 1, 1.0f, AudioFadeModel.InverseByDistance);
        await VivoxService.Instance.JoinPositionalChannelAsync(proximityChannel, ChatCapability.AudioOnly, props);

        isChannelJoined = true;
        UpdateUIState(false); 
        SwitchTransmissionMode(proximityChannel);
        
        Debug.Log("[Vivox] 채널 접속 및 마이크 세팅 완료!");
    }

    private void Update()
    {
        if (!isChannelJoined) return;

        // 🌟 수정됨: 복잡한 내부 접근 권한 대신 try-catch로 위치 전송 에러를 부드럽게 넘김
        if (dummyPlayerTransform != null && VivoxService.Instance.ActiveChannels.ContainsKey(proximityChannel))
        {
            try
            {
                VivoxService.Instance.Set3DPosition(dummyPlayerTransform.gameObject, proximityChannel);
            }
            catch (System.Exception)
            {
                // 통신이 아주 잠깐 끊겼을 때 발생하는 1001 에러 도배를 조용히 무시합니다.
            }
        }

        // T 키: 일반 말소리 어그로 발생 (테스트용)
        if (Input.GetKeyDown(KeyCode.T))
        {
            EmitSound(false);
            Debug.Log("일반 말소리를 냅니다. (반경 10m)");
        }

        // R 키: 무전기 사용
        if (Input.GetKeyDown(KeyCode.R))
        {
            isRadioTransmitting = true;
            SwitchTransmissionMode(radioChannel);
            UpdateUIState(true);
            EmitSound(true); 
            Debug.Log("<color=yellow>[무전기 송출 중] 2D 채널로 말합니다. (반경 30m 어그로)</color>");
        }
        
        if (Input.GetKeyUp(KeyCode.R))
        {
            isRadioTransmitting = false;
            SwitchTransmissionMode(proximityChannel);
            UpdateUIState(false);
            Debug.Log("<color=white>[무전기 종료] 3D 대화 모드로 복귀했습니다.</color>");
        }

        if (isRadioTransmitting && micVolumeBar != null)
        {
            micVolumeBar.value = Mathf.Lerp(micVolumeBar.value, Random.Range(20f, 100f), Time.deltaTime * 15f);
        }
    }

    private void EmitSound(bool isRadio)
    {
        float currentRadius = isRadio ? radioVoiceRadius : normalVoiceRadius;
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, currentRadius, monsterLayer);
        
        foreach (var hit in hitColliders)
        {
            MonsterAI monster = hit.GetComponent<MonsterAI>();
            if (monster != null)
            {
                monster.OnHearSound(this.transform, isRadio);
            }
        }
    }

    private void UpdateUIState(bool isRadio)
    {
        if (statusLabel == null || micVolumeBar == null) return;

        if (isRadio)
        {
            statusLabel.text = "[ 무전기 송출 중... ]";
            statusLabel.style.color = new StyleColor(Color.red);
            micVolumeBar.style.display = DisplayStyle.Flex; 
        }
        else
        {
            statusLabel.text = "3D 근접 대화 모드";
            statusLabel.style.color = new StyleColor(Color.white);
            micVolumeBar.value = 0;
            micVolumeBar.style.display = DisplayStyle.None; 
        }
    }

    private async void SwitchTransmissionMode(string targetChannel)
    {
        try
        {
            await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.Single, targetChannel);
        }
        catch (System.Exception e) { Debug.LogWarning(e.Message); }
    }
}