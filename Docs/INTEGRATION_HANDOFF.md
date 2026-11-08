# VOID LAST 통합 인계 문서

작성일: 2026-11-02  
기준 프로젝트: `D:/Cosmos/Team-Cosmos` · Unity 6000.4.9f1  
통합 씬: `Assets/Map/Scenes/Stage2_Ship.unity`

## 1. 완료 상태와 보존 범위

기존 FPS, 몬스터 3종, 주사기 3개, 스캔 및 기존 Vivox 구성요소를 F2 화물 구역에 통합했다. 정식 스테이지 배치가 아닌 임시 통합 테스트 배치다.

Player·Monster 원본 파일, 기존 테스트 씬, 기존 맵 NavMesh 7개, ProjectSettings, Packages를 보존했다. 원본 맵의 48,495개 직렬화 블록 중 삭제는 없으며, 기존 블록 변경은 F2 NavMesh 참조와 통합 루트 추가 두 곳뿐이다. 자동 추가된 조명 컴포넌트 12개와 조명 활성 상태 2개는 복원했다.

Git reset/clean/commit/push는 하지 않았다. 작업 시작 전부터 있던 `Team-Cosmos.slnx` 삭제 상태도 그대로 유지했다.

## 2. 최종 폴더 구조

```text
Assets/
  Player/                         기존 구조 유지
    Prefabs/Player/VOID_LAST_FPS.prefab
    Prefabs/Item/VOID_LAST_SyringePickup.prefab
    Scripts/
    Character_test.unity
  Monster/                        기존 구조 유지
    Scripts/
    Prefabs/
    Scenes/Monster_test.unity
  item/
    Scripts/                      탐지 및 기존 테스트 코드
    Shaders/                      기존 스캔 셰이더 3종
    Scenes/itemtest.unity
    Navigation/itemtest_NavMesh.asset
    Backups/                      아이템 관련 백업 5개
  VoiceChat/
    Scripts/StandaloneRadioTest.cs
    Scripts/Tests/MonsterAI.cs     기존 음성 테스트용 AI
    UI/RadioHUD.uxml
    UI/RadioHUD.uss
    UI/RadioPanelSettings.asset
    Backups/                      음성 관련 백업 2개
  Map/
    Scenes/Stage2_Ship.unity
    Script/Stage2/Editor/          통합 배치·검사 도구
    Stage2_Ship/Navigation/Stage2_Integration_F2_NavMesh.asset
Docs/
  INTEGRATION_HANDOFF.md
  IntegrationReports/
```

현재 item에는 별도의 프리팹·머티리얼·UI 에셋이 없어 빈 폴더를 만들지 않았다. 실제 주사기 프리팹은 기존 Player 경로에 유지했다.

## 3. 이동·생성·수정 파일

이동은 Unity AssetDatabase.MoveAsset을 사용했고 .meta GUID를 보존했다. 전체 24개 이동 기록은 `Docs/IntegrationReports/AssetMoves.tsv`에 있다.

- item 루트 → Scripts: DetectableItem.cs, ItemDetector.cs, Flashlight.cs, MouseLook.cs, PlayerMovement.cs.
- item 루트 → Shaders: EchoPing.shader, ItemXRayGlow.shader, SonarWave.shader.
- itemtest.unity → Scenes, itemtest_NavMesh.asset → Navigation.
- item/voicechat → Assets/VoiceChat. StandaloneRadioTest는 Scripts, 기존 MonsterAI는 Scripts/Tests, UXML·USS는 UI로 분리.
- a.asset은 실제 UI Toolkit PanelSettings여서 UI/RadioPanelSettings.asset으로 이름과 위치를 변경했다.
- 아이템 백업 5개는 item/Backups, 음성 백업 2개는 VoiceChat/Backups에 배치했다.

신규 파일:
- `Assets/item/Scripts/FpsPickupDetectionBridge.cs`
- `Assets/Map/Script/Stage2/Editor/Stage2IntegrationSetup.cs`
- `Assets/Map/Script/Stage2/Editor/Stage2IntegrationValidation.cs`
- `Assets/Map/Stage2_Ship/Navigation/Stage2_Integration_F2_NavMesh.asset`
- 위 에셋의 .meta, 인계 문서 및 IntegrationReports의 검사 기록·화면 캡처.

기존 내용 수정:
- Stage2_Ship.unity: 통합 루트 배치와 F2 NavMesh 연결.
- StandaloneRadioTest.cs: 승인된 무전 V 입력, 통합 씬용 우측 상단 HUD 옵션.
- RadioHUD.uxml: V 안내와 이동 후 상대 USS 경로.
- RadioPanelSettings.asset: 이름 변경에 따른 m_Name만 변경.

## 4. 시스템별 주요 파일과 연결

**Player**
- 실제 FPS 프리팹 경로는 `Assets/Player/Prefabs/Player/VOID_LAST_FPS.prefab`이다.
- Scripts의 FpsInput/Motor/Look, FpsPistol/Effects, FpsHealth/Hud, FpsInventory/ItemInteractor/ItemView가 기존 기능을 담당한다.
- 원본 프리팹과 코드는 변경하지 않았다. Stage2 인스턴스에 ItemDetector, FpsPickupDetectionBridge, 기존 MonsterMeleeTestBridge를 추가했다.
- MonsterMeleeTestBridge의 근접 피해 20은 기존 테스트용 수치다.

**Monster**
- `Assets/Monster/Prefabs/VOID_LAST_EyedLargeMonster.prefab`: EyedMonsterAI, HP 300, 공격 피해 30. 기존 시야·추격·돌진 근접 공격 유지.
- `Assets/Monster/Prefabs/VOID_LAST_BlindMonster.prefab`: BlindMonsterAI, HP 120, 촉수 피해 18. 기존 배회·스캔 반응·근접 감지·더듬기 유지.
- `Assets/Monster/Prefabs/VOID_LAST_SmallMonster.prefab`: SmallMonsterAI, HP 40, 공격 피해 8. 기존 수색·비명 상태·추격 유지. screamClip은 미지정이다.
- 공통 연결: MonsterHealth/MonsterVision/MonsterMelee 및 Player의 IFpsDamageTarget. AI 행동 코드와 원본 프리팹은 보존했다.

**item / 스캔**
- `Assets/item/Scripts/ItemDetector.cs`의 Z 스캔 → `Assets/Monster/Scripts/ScanNoise.cs` → BlindMonsterAI의 기존 이벤트 구독.
- 주사기 인스턴스에 DetectableItem을 추가했다. 드롭된 주사기에는 FpsPickupDetectionBridge가 같은 탐지 표시만 연결한다.
- 획득·사용·드롭은 기존 FPS 코드가 담당한다. 별도 인벤토리를 만들지 않았다.
- MouseLook/PlayerMovement/Flashlight는 기존 테스트 코드이며 통합 FPS에 중복 부착하지 않았다.

**VoiceChat**
- StandaloneRadioTest는 기존 Unity Services 인증과 Vivox를 사용한다. Test_3D_Room은 근접 채널, Test_2D_Radio는 무전 채널이다.
- 통합 씬의 UIDocument에 RadioPanelSettings와 RadioHUD를 연결하고 플레이어 Transform을 위치 갱신 대상으로 지정했다.
- 일반 Play에서는 기존 Start가 로그인과 채널 참가를 시도한다. 자동 검사에서는 Start 전에 음성 컴포넌트를 꺼서 로그인·마이크 송신을 하지 않는다.
- 실제 무전기·스캐너 획득/장착 프리팹은 찾지 못했다. 현재 스캔은 플레이어 컴포넌트이고 무전은 인벤토리와 독립된 기존 프로토타입이다. 임의 모델이나 장착 시스템을 추가하지 않았다.
- VoiceChat/Scripts/Tests/MonsterAI와 T 소리 테스트는 기존 음성 실험용이다. 실제 몬스터 3종에 연결하지 않았다.

## 5. Stage2 배치와 NavMesh

통합 오브젝트는 `VOID_LAST_Integration_Test` 루트 아래에 있다.

| 대상 | 월드 위치(근사) | Y 회전 |
|---|---|---|
| FPS | (-18, 8.07, -28.56) | 90° |
| 눈 있는 대형 몬스터 | (-20, 8.07, 10) | 180° |
| 눈 없는 몬스터 | (-20, 8.07, -15) | 90° |
| 쪼꼬미 | (10, 8.07, -10) | 270° |

플레이어는 복도를 바라보며, 앞쪽 바닥에 주사기 3개를 배치했다. Integration Waypoints에는 대형 순찰 3개·수색 23개, 눈 없는 몬스터 배회 3개, 쪼꼬미 수색 3개가 있다. 별도 자식 오브젝트에 기존 VoiceChat과 HUD를 연결했다.

F2_Cargo Surface만 별도 통합 NavMesh를 참조한다. 실제 맵 콜라이더 983개를 사용했으며 베이크 반경 0.85m, 높이 2.8m, 단차 0.3m, voxel 0.12m다. 범위 중심은 (-1.75, 10, 3), 크기는 (145, 5, 115)다.

기존 7개 맵 NavMesh 에셋은 남아 있고 나머지 6개 Surface 참조는 유지했다. itemtest NavMesh를 재사용하지 않았다. 프로젝트의 전역 Agent 설정은 변경하지 않았다. F2 맵 구조가 바뀌면 이 별도 데이터도 같은 체격 조건으로 다시 베이크해야 한다.

## 6. 조작 키

| 입력 | 동작 |
|---|---|
| WASD / 마우스 | 이동 / 시점 |
| Left Shift / Left Ctrl / Space | 달리기 / 웅크리기 / 점프 |
| 1 / 2 / 3 | 권총 / 근접 / 고유 아이템 슬롯 |
| 4·5·6 | 파밍 아이템 슬롯 |
| F / Q | 아이템 획득·상호작용 / 드롭 |
| R | 권총 재장전 |
| 좌클릭 | 권총 사격·근접 공격, 주사기는 3초 홀딩 자기 사용 |
| 우클릭 | 권총 조준, 주사기는 가까운 다른 플레이어에게 홀딩 사용 |
| Z | 기존 탐지 스캔 |
| V 홀딩 | 기존 무전 송신 |
| Esc | 커서 잠금 해제 |
| T | 기존 VoiceChat의 소리 어그로 테스트 전용 |

F 획득 유지·V 무전 변경은 사용자 승인 사항이다. 기존 Flashlight의 F는 Stage2에 부착하지 않아 충돌하지 않는다. H 체력 감소는 별도 FpsHealthDebugInput이 부착된 테스트 오브젝트에서만 사용한다.

## 7. 확인한 결과와 한계

실제 Stage2 Play Mode에서 기존 런타임 API로 49개 검사를 통과했다:
- 플레이어 1명, 활성 카메라·AudioListener 각 1개, 중복 이동 코드 및 EventSystem 없음.
- 바닥 충돌, 이동, 점프·착지, 웅크리기·일어서기, 시점 회전.
- 권총 Raycast 명중·탄약 감소, 시간에 따른 재장전과 잔탄 이동.
- 체력 HUD 초기화, 4·5·6 핫바 반응.
- 주사기 획득, 홀딩 취소, 3초 사용 후 HP 30 회복과 소비, 드롭·재탐지.
- 세 몬스터 NavMesh 이동과 관찰 구간의 벽 충돌 여유, 대형 몬스터 시야 추격·기존 공격 피해, 쪼꼬미 비명 상태 후 추격.
- Z 스캔의 아이템 표시 및 눈 없는 몬스터 반응.
- VoiceChat UI 로딩, V 안내, 연결된 UI·플레이어 참조.

Stage2의 Missing Script/Prefab과 통합 오브젝트의 Missing Reference는 0이었다. 맵의 외부 GUID 3,076개와 주요 프리팹 참조가 해석됐다. 기존 Character_test, Monster_test, 이동한 itemtest는 Edit Mode로 열어 참조를 확인한 뒤 저장 없이 닫았다. 기존 테스트 씬 전체를 다시 플레이한 것은 아니다.

프로젝트 스크립트는 컴파일되어 Play Mode를 실행했다. Console을 무조건 오류 0으로 보고하지 않는다. 검사 중 플레이어를 잠시 비활성화한 구간에서 기존 CrateOpen.cs:101의 Camera.main 참조 예외가 기록됐다. 검사 도구가 카메라를 유지하도록 보완했으며, 이 보완 후 전체 Play Mode 검사는 반복하지 않았다. 기존 맵의 일부 Convex Mesh Collider 256면 제한 경고도 남아 있다. 원본 상자 코드와 메시를 수정하지 않았다.

수동 확인이 필요한 항목:
- 실제 키보드·마우스로 플레이하는 조작감, 어두운 맵에서 HUD·아이템의 가독성.
- 두 클라이언트의 Vivox 인증·마이크 권한·근접 감쇠·V 송수신. 실제 음성 통신은 미검증이다.
- 전체 층·계단·문 통과, 장시간 AI 이동, 여러 플레이어 간 아이템 사용.
- 무전기·스캐너의 실제 아이템화, 쪼꼬미 비명 사운드 지정.

검사 기록은 Docs/IntegrationReports에 있다. Stage2_FPS.png와 Stage2_FPS_Scan.png는 실제 카메라 렌더이며 Screen Space Overlay HUD는 포함하지 않는다.

## 8. 담당 폴더와 협업 주의사항

| 담당 | 권장 수정 범위 |
|---|---|
| 플레이어·총기·인벤토리 | Assets/Player |
| 몬스터 AI·전투·스캔 소리 수신 | Assets/Monster |
| 탐지·스캔 표시 | Assets/item |
| 인증·음성·통신 HUD | Assets/VoiceChat |
| 맵·배치·F2 NavMesh·통합 도구 | Assets/Map |

에셋 이동 시 .meta/GUID를 함께 보존하고 가능하면 AssetDatabase.MoveAsset을 사용한다. VoiceChat의 테스트용 MonsterAI를 실제 몬스터 AI로 대체 부착하지 않는다. FPS에는 MouseLook/PlayerMovement를 추가하지 않는다. 드롭 탐지 연결은 인벤토리 로직과 분리되어 있다.

Editor 메뉴의 `VOID LAST/Integration/1 - Add Stage2 Test Setup If Missing`은 기존 통합 루트가 있으면 보존하고 종료한다. 기존 배치를 덮어쓰거나 기존 NavMesh를 자동 재베이크하는 메뉴가 아니다. `2 - Validate Stage2 Play Mode (No Voice Transmission)`는 음성 송신 없이 통합 검사를 실행하고 Edit Mode로 돌아온다. 검사 중에는 게임 입력을 피한다.

## 9. 팀원 Codex 공통 작업 안내문

> 먼저 Docs/INTEGRATION_HANDOFF.md와 현재 Git 변경 사항을 읽어라. 실제 Unity 씬·프리팹·참조를 확인하고 Stage2_Ship을 통합 기준으로 사용하라. 기존 팀원 변경, Player/Monster 원본, .meta/GUID를 보존하라. 맡은 기능에 필요한 최소 범위만 수정하고 Packages/ProjectSettings는 임의 변경하지 마라. F 획득·R 재장전·V 무전·Z 스캔을 유지하라. 존재하지 않는 무전기/스캐너 프리팹을 가정하지 마라. 변경한 기능에 필요한 검사만 실행하고 반복적인 전체 검사는 피하라. 실제 확인한 결과와 미검증 항목을 구분해 인계 문서를 갱신하라.

## 10. 스캐너 아이템 · 무전 음성 효과 (2026-11-08)

기준 `main` a16c7de. Packages·ProjectSettings는 변경하지 않았다. 새 파일은 Unity가 만든 .meta와 함께 추가됐다.

### 10.1 변경 내용

- 스캔은 이제 **스캐너 아이템**으로 한다. 4·5·6번 슬롯에 넣고 손에 든 상태에서 좌클릭하면 스캔하며, 쿨타임은 15초, 탐지 반경은 20m(`scannerRadius`, 파동도 20m까지)다. 대원(FPS)은 **Z 키 스캔을 쓰지 않는다**(사용자 결정). 인벤토리가 없는 itemtest에서만 Z가 남는다.
- F 획득, Q 드랍, 죽을 때 떨어뜨리기, 멀티 동기화(NetworkPickupRegistry), 핫바 아이콘은 기존 카탈로그 흐름(`Resources/FarmingItems`)을 그대로 탄다.
- 무전 채널로 들리는 다른 대원 목소리에만 무전기 음질(좁은 대역·찌그러짐·말하는 동안 치지직)을 입힌다. 근거리 육성은 그대로다.

수정한 기존 파일 (Player 원본 2곳은 최소 수정):
- `Player/Scripts/FpsInventory.cs`: `FarmingItemKind` 맨 끝에 `Scanner`(=8) 추가. 기존 번호는 그대로다.
- `Player/Scripts/FarmingItems.cs`: Scanner를 카탈로그 아이템에 포함, HUD 이름 `SCANNER`.
- `item/Scripts/ItemDetector.cs`: `TryUseScanner()`(스캐너 보유 확인, 쿨타임 `scannerCooldown`=15) 추가. 대원이면 Z 무시. 검사 도구가 쓰는 `ActivateDetector()`는 동작·쿨타임(1.5초)·반경(`detectRadius` 15m)을 그대로 뒀다. Awake에서 FpsScannerItem을 자동으로 붙이므로 플레이어 프리팹은 수정하지 않았다.
- `VoiceChat/Scripts/InGameVoiceChat.cs`: Start에 RadioVoiceEffect를 붙이는 한 줄.
- `VoiceChat/Scripts/StandaloneRadioTest.cs`: 오프라인 테스트(층 씬 바로 Play)에서도 멀티플레이와 같게 **무전기를 손에 들고 좌클릭하는 동안** 송신하도록 추가(V도 유지). 송신 중 손에 든 무전기 LED가 깜빡인다. 멀티플레이 경로(InGameVoiceChat)의 좌클릭 송신은 원래부터 있었고 바꾸지 않았다.
- `Map/Scenes/Floors/Stage2_Ship_2f.unity`: `Integration Pickups` 아래에 `VOID_LAST_ScannerPickup` 1개 배치 (-15.2, 8.0, -28.7).

새 파일:
- `item/Scripts/FpsScannerItem.cs`: 손에 든 모습(1인칭 레이저건+장갑, 3인칭 손 소켓), 좌클릭 사용, 반동, 화면 아래 쿨타임 표시. 무전기와 같은 위치(FpsRadioView의 참조)를 쓴다.
- `Resources/FarmingItems/Scanner.prefab`: 바닥 프리팹. FpsWorldPickup(Scanner)과 DetectableItem이 붙어 있고, 모델 자식은 `ScannerModel`(Sci-Fi Laser Gun Type B `LaserGun_B_Right` 중첩 프리팹, MeshCollider 제거)이다. FpsCarriedItemView가 `Model`이라는 자식만 복제하므로 이름을 달리해 손에 두 번 보이지 않게 했다.
- `Resources/FarmingItems/Icons/Scanner.png`: F2MissionItemAssets와 같은 조명·방식으로 렌더링한 아이콘.
- `VoiceChat/Scripts/RadioVoiceEffect.cs`, `RadioVoiceFilter.cs`: Vivox 16.12.1 참가자 오디오 탭(`CreateVivoxParticipantTap`, silenceInChannelAudioMix=true)으로 무전 채널의 다른 대원 음성을 받아 필터를 적용한다. 컴포넌트를 끄면 탭을 없애 원래 소리로 돌아간다.

### 10.2 실제 확인함

- 전체 컴파일 성공, Console 오류 0.
- 프리팹·아이콘을 Resources에서 읽을 수 있음. 2f 씬 저장 파일에 스캐너가 들어 있고 Missing Script 0.
- 2f 오프라인 테스트 Play에서 반경 20m 스캔 후 쿨타임 15초 적용, 파동 maxRadius 20 확인.
- 2f 오프라인 테스트 Play(사용자가 직접 플레이): 스캐너를 F로 주워 0번 슬롯에 들어감, 손에 들림(IsHeld/IsEquipped), 좌클릭 스캔 후 쿨타임 15초가 돌고 `SCAN 12s` 표시가 보임, 주변 아이템 발광 화면 확인.
- 스캐너가 없을 때 `TryUseScanner()`는 false를 반환한다.

### 10.3 미검증

- 무전 치지직 효과: 두 클라이언트로 Vivox 무전을 실제로 주고받는 테스트를 하지 않았다. 참가자 탭이 만들어지는지, 소리가 끊기지 않는지, 음질이 적당한지 모두 미확인. 문제가 있으면 RadioVoiceEffect를 끄면 원래 소리로 돌아간다. 음질은 RadioVoiceFilter의 값(대역·drive·hiss·crackle)으로 조절한다.
- 멀티플레이에서 스캐너 줍기·드랍 동기화, 다른 대원 화면의 3인칭 모습, 층 이동 시 스캐너 유지(쿨타임은 층마다 초기화된다).
- 장갑 위치: 권총(WeaponRoot)에서 장갑이 손잡이를 쥐는 관계를 레이저건 손잡이(메시 정점으로 측정, 모델 기준 y≈0.05·z≈-0.08)에 옮겨 `glovePosition (0, 0.065, -0.12)`, `gloveEuler (90, 180, 0)`으로 정했다. 프리뷰 씬 옆모습 렌더에서 권총과 같은 방식으로 손잡이를 감싸는 것을 확인했다. 실제 게임 화면(1인칭)에서의 모습과 레이저건 화면 위치(heldPosition)는 사용자가 직접 보고 조정해야 한다.
- 다른 층(1·3·4·5f)에는 스캐너를 배치하지 않았다.

### 10.4 주의

- `Stage2IntegrationValidation`은 `ActivateDetector()`를 직접 호출하므로 영향이 없다. 단, 실제 게임에서는 스캐너 없이 스캔할 수 없다.
- 프로젝트에서 F, R, V는 그대로다. Z는 대원에게서 제거됐다.
