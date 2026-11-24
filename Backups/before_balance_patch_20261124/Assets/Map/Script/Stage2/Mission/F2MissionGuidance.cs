using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VoidLast.Player;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Reference: VOID LAST deck plan, 2026-11-14. Dialogue is newly authored.
// Presentation only: consumes the director's authoritative state and never advances missions.
public sealed partial class F2MissionDirector
{
    [Header("시나리오 안내")]
    public bool showScenarioGuide = true;
    [Range(5f, 20f)] public float dialogueSeconds = 10f;
    [System.Serializable]
    public sealed class ScenarioLine
    {
        public string objective;
        [TextArea(2, 5)] public string dialogue;
        public ScenarioLine(string objective, string dialogue) { this.objective = objective; this.dialogue = dialogue; }
    }
    public ScenarioLine[] scenarioLines = {
        new ScenarioLine("화물 분류실에서 검역 카드 찾기", "격납고로 내려가려면 전력과 연료부터 확보해야 해. 화물 분류실에 남은 검역 카드를 찾아보자. 감지기가 있다면 30m 안의 임무 물건을 확인할 수 있어."),
        new ScenarioLine("검역 관제실 콘솔에서 봉쇄 해제", "검역 카드를 확보했어. 검역 관제실 콘솔에 사용하면 장비 작업실의 봉쇄를 풀 수 있어."),
        new ScenarioLine("장비 작업실에서 절단기 확보", "봉쇄가 풀렸어. 장비 작업실에서 절단기를 챙기자. 전력 모듈을 꺼낼 때 필요해."),
        new ScenarioLine("정비 보관실에서 전력 모듈 꺼내기", "절단기를 확보했어. 정비 보관실의 랙을 열자. 금속 소리가 나면 근처 녀석들이 몰려올 수 있으니 주변을 살펴."),
        new ScenarioLine("전력 모듈을 주워 중앙 화물창 리프트에 싣기", "모듈이 분리됐어. 바닥에 나온 모듈을 챙겨 중앙 화물창 리프트로 운반하자. 1층에 내려보내야 해."),
        new ScenarioLine("연료 커플러 찾기", "전력 모듈을 내려보냈어. 이제 연료 배관을 연결할 커플러가 필요해. 이번에 놓인 위치를 수색하자. 감지기 소리에도 몬스터가 반응하니 조심해."),
        new ScenarioLine("연료 공급실 펌프에 커플러 연결", "커플러 확보. 연료 공급실 펌프에 연결하고 충전을 시작하자. 충전 중에는 대원이 이 방을 지켜야 해."),
        new ScenarioLine("연료 공급실을 지키며 충전 완료 기다리기", "연료가 들어가고 있어. 펌프 소리가 크니 입구를 경계해. 살아 있는 대원이 공급실을 떠나면 충전이 멈춰."),
        new ScenarioLine("컨테이너 보관실에서 고장 난 밸브 수리", "배관 압력이 끊겼어! 컨테이너 보관실의 밸브를 수리해야 해. 수리가 끝날 때까지 충전은 멈춰 있어."),
        new ScenarioLine("연료 공급실로 돌아가 충전 재개", "연료 공급실이 비어서 충전이 멈췄어. 살아 있는 대원 한 명이 공급실 안으로 돌아가야 해."),
        new ScenarioLine("A 계단으로 모여 1층으로 내려가기", "탈출 준비가 끝났어. 비상등이 켜진 A 계단으로 이동하자. B 계단은 막혀 있어. 문 앞에서 상호작용해 1층으로 내려가."),
        new ScenarioLine("2층 완료 · 1층 격납고로 이동", "이 층에서 할 일은 끝났어. 다음은 보조 우주선 격납고야. 함께 이동하자."),
        new ScenarioLine("임무 실패", "대원들의 응답이 끊겼다. 이번 작전은 여기까지다.")
    };

    Image dialogueCard;
    TMP_Text dialogueText;
    int currentGuide = -1;
    float dialogueRemaining;
    float nextGuidePoll;

    // Pure state selection also supports late join: show the current task, not the whole past story.
    public static int SelectScenarioStep(MissionState s, bool card, bool cutter, bool coupler)
    {
        if (s.phase == (byte)Phase.Failed) return 12;
        if (s.phase == (byte)Phase.Cleared) return 11;
        if (s.phase == (byte)Phase.Rally || s.fuel == 2) return 10;
        if (!s.lockdownLifted) return card ? 1 : 0;
        if (s.module == 0) return cutter ? 3 : 2;
        if (s.module == 1) return 4;
        if (s.fuel == 0) return coupler ? 6 : 5;
        if (s.valveBroken) return 8;
        return s.fuelPaused ? 9 : 7;
    }

    void BuildScenarioGuide(Transform root)
    {
        dialogueCard = Img(root, "Scenario Radio", new Color(0.03f,0.045f,0.06f,0.92f));
        Anchor(dialogueCard.rectTransform,new Vector2(1,1),new Vector2(1,1),new Vector2(-24,-300),new Vector2(490,220));
        dialogueText = Txt(dialogueCard.transform,"Dialogue","",20,new Color(0.84f,0.93f,1f),TextAlignmentOptions.TopLeft);
        Stretch(dialogueText.rectTransform);
        dialogueText.rectTransform.offsetMin=new Vector2(20,16); dialogueText.rectTransform.offsetMax=new Vector2(-20,-16);
        dialogueText.textWrappingMode=TextWrappingModes.Normal;
        dialogueText.overflowMode=TextOverflowModes.Ellipsis;
        dialogueCard.gameObject.SetActive(false);
    }

    void UpdateScenarioGuide(MissionState s)
    {
        if (dialogueCard == null) return;
        bool visible = showScenarioGuide && LocalPlayer() != null && !LoadingScreen.IsShowing && !FpsInput.Blocked;
        dialogueCard.gameObject.SetActive(visible && dialogueRemaining > 0f);
        if (!visible) return;
        if (Time.unscaledTime >= nextGuidePoll)
        {
            nextGuidePoll = Time.unscaledTime + 0.2f;
            int step = SelectScenarioStep(s, TeamHas(FarmingItemKind.QuarantineCard), TeamHas(FarmingItemKind.Cutter), TeamHas(FarmingItemKind.FuelCoupler));
            if (scenarioLines != null && step < scenarioLines.Length && scenarioLines[step] != null && currentGuide != step)
            {
                currentGuide = step;
                dialogueText.text = "<color=#75E3DB><b>대원 · 무전</b></color>  <size=16>J · 다시 보기</size>\n" + scenarioLines[step].dialogue;
                dialogueRemaining = dialogueSeconds;
            }
        }
        bool replay = false;
#if ENABLE_INPUT_SYSTEM
        replay = Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        replay = Input.GetKeyDown(KeyCode.J);
#endif
        if (replay && currentGuide >= 0) dialogueRemaining = dialogueSeconds;
        dialogueCard.gameObject.SetActive(dialogueRemaining > 0);
        dialogueRemaining = Mathf.Max(0, dialogueRemaining - Time.unscaledDeltaTime);
    }
}
