# VOID LAST 빛 방출기

## 바로 테스트

`Assets/Player/FlashEmitter_test.unity`를 열고 Play를 누른다. 기존 Monster_test의 복도·방·NavMesh를 복사한 별도 씬이다. 기본 플레이어와 의료 키트, 원본 Monster_test, Stage2_Ship 및 Lobby는 변경하지 않았다.

이 씬의 플레이어는 **3번 슬롯에 빛 방출기**가 연결된 테스트 변형 프리팹이다. 좌클릭을 누르고 1.5초 이상 충전한 뒤 놓으면 방출한다. 충전 중에만 중앙 점 아래 막대가 표시된다. 일찍 놓거나 슬롯을 변경하면 취소된다. 총 3회 사용하며 방출 후 5초간 충전할 수 없다. 다시 Play하면 새 플레이어로 시작한다. 충전 중 사망해도 취소된다.

## 에셋과 Inspector

- 모델: `Assets/Player/Prefabs/Item/VOID_LAST_FlashEmitter.prefab`
- 오프라인 플레이어: `Assets/Player/Prefabs/Player/VOID_LAST_FPS_FlashEmitter_Test.prefab`
- 네트워크 플레이어: `Assets/Player/Prefabs/Player/VOID_LAST_FPS_FlashEmitter_Network_Test.prefab`
- 머티리얼: `Assets/Player/Materials/FlashEmitter_Body.mat`, `FlashEmitter_Lens.mat`

플레이어 루트의 **FpsFlashEmitter**에서 다음 public 필드를 조절한다. 네트워크 게임에서는 서버 프리팹 설정이 판정 기준이므로 모든 빌드에 같은 프리팹을 사용한다.

| 필드 | 기본값 | 의미 |
| --- | --- | --- |
| chargeDuration | 1.5 | 완충까지 걸리는 초 |
| stunDuration | 5 | 몬스터 스턴 초 |
| effectRadius | 5 | 장비 발광부에서 판정하는 반경(m) |
| cooldownDuration | 5 | 방출 후 대기 초. **0이면 대기 없음** |
| maxUses | 3 | 플레이어 생성 시 사용할 수 있는 총 횟수 |

**FpsFlashEmitterView**의 Held Position / Held Euler는 FPS 손과 장비가 함께 움직이는 위치·회전이다. Equip Duration, Gauge Width, Flash Duration, Flash Intensity, Light Range도 조절할 수 있다. 빛의 시각적 범위와 스턴 반경은 별개다. 기본 섬광은 0.16초, Point Light 범위는 4m이며 화면 전체를 하얗게 덮지 않는다.

FPS 장착 위치는 `CameraPivot/FPS Camera/FlashEmitterRoot`다. 전신용 모델은 오른손 Bone 아래 `WorldFlashEmitterSocket`에 있다. 기존 FpsLocalVisibility가 카메라별로 두 표시를 구분한다. 기존 장갑 메시를 재사용하며 새로운 손 애니메이션이나 IK는 없다.

실제 모델로 교체할 때는 각 루트 아래 모델 인스턴스를 교체하고 View의 Local/World Muzzle, Lens, Flash 참조와 기존 FpsLocalVisibility의 Renderer 목록을 갱신한다. 기능은 FpsFlashEmitter, 표현은 FpsFlashEmitterView, 통신은 FpsFlashEmitterNetwork에 분리되어 있다.

## 슬롯과 기존 의료 키트

이번에는 직업·장비 교체 시스템을 추가하지 않았다. **새 테스트 변형에서만 FpsMedicalKit을 비활성화**하고 FpsFlashEmitter를 활성화했다. 기본 `VOID_LAST_FPS` 및 `VOID_LAST_FPS_Network`는 기존 의료 키트를 그대로 사용한다. 두 기능을 동시에 켜면 의료 키트가 우선하며 방출기는 작동하지 않는다.

## 몬스터 / 멀티플레이

대형 시각형, 소형 시각형, 눈 없는 촉수형 AI에 Stunned 상태를 마지막 enum 항목으로 추가했다. 기존 상태 번호와 기존 MonsterNetworkSync는 유지한다. MonsterStun은 적중 시 해당 몬스터에 붙어 종료 시간만 관리한다. 공격을 취소하고 NavMesh 이동을 멈추며, 끝나면 기존 감지·수색·배회 로직으로 복귀한다. 짧은 중복 스턴은 남은 시간을 줄이지 않는다. HP 피해는 없다.

서버가 요청자의 소유권, 장착 상태, 충전 시간, 횟수, 쿨타임을 검증한다. 승인된 방출만 한 번 차감하고 스턴을 적용한다. 같은 방출 요청을 반복해도 중복 방출하지 않는다. 섬광은 RPC로 각 피어에 전달하고 스턴 상태는 기존 MonsterNetworkSync로 전달한다. 충전 막대는 소유자에게만 표시한다. 벽 안으로 들어간 FPS 모델 때문에 벽 너머가 적중하지 않도록 방출 위치도 보정한다.

Stage2/Lobby의 스폰 설정은 변경하지 않았다. 실제 세션에 넣으려면 별도 테스트 세션의 NetworkManager Player Prefab에 위 네트워크 테스트 프리팹을 지정하고 양쪽에 같은 프리팹을 등록한다. 몬스터에는 기존 NetworkObject·서버 권한 NetworkTransform·MonsterNetworkSync가 필요하다. 직업 선택 결과 연결은 후속 작업이다.

## 확인 방법과 결과

`VOID LAST > Player > Validate Flash Emitter`는 전용 씬을 Play하고, 로컬 UDP 포트 18779로 임시 호스트/클라이언트 두 개를 연결해 검사한 뒤 종료한다. 원래 열었던 씬과 Play 시작 씬 설정은 복원하며 프로젝트 네트워크 설정을 저장하지 않는다.

- `Assets/Player/Validation/FlashEmitterResults.txt`: 실제 Play Mode 충전·취소·쿨타임 5/0초·3회 제한, 벽/거리 차단, 공격 취소, 세 몬스터의 스턴/복귀, 사망 중단, 두 피어의 방출·횟수·쿨타임·스턴 상태 및 로컬 UI 확인 기록.
- `FlashEmitterVisualResults.txt`와 `FlashEmitter_*.png`: 최종 모델·충전 막대·섬광 및 조명 종료 확인.
- `FlashEmitter_NetworkMonster.prefab`: 자동 검사에서만 사용하는 기존 대형 몬스터의 네트워크 변형. 원본 몬스터 프리팹은 변경하지 않는다.

로컬 UDP의 두 피어는 실제 연결해 확인한다. 별도 PC, Relay, 지연·패킷 손실 환경, Stage2/Lobby 세션 통합 및 실제 키보드·마우스 조작감은 이번 자동 검사 범위에 포함하지 않는다. 테스트 씬에서 직접 조작하며 최종 위치·섬광 강도를 조절할 수 있다.

## 이번 작업 파일

신규 런타임 코드:
- `Assets/Player/Scripts/FpsFlashEmitter.cs`
- `Assets/Player/Scripts/FpsFlashEmitterView.cs`
- `Assets/Player/Scripts/FpsFlashEmitterNetwork.cs`
- `Assets/Monster/Scripts/MonsterStun.cs`

수정 코드(스턴 진입·복귀·공격 차단만 추가):
- `Assets/Monster/Scripts/EyedMonsterAI.cs`
- `Assets/Monster/Scripts/SmallMonsterAI.cs`
- `Assets/Monster/Scripts/BlindMonsterAI.cs`
- `Assets/Monster/Scripts/MonsterMelee.cs`

신규 구성·검사 코드:
- `Assets/Player/Scripts/Editor/FpsFlashEmitterSetup.cs`
- `Assets/Player/Scripts/Editor/FpsFlashEmitterValidation.cs`

그 외 신규 파일은 위에 기재한 모델·플레이어 변형 2개·머티리얼 2개·테스트 씬, 이 문서, `Validation/FlashEmitter*` 검사 기록·캡처·네트워크 검사 몬스터와 각 Unity `.meta` 파일이다. 기존 플레이어 스크립트, 원본 플레이어/장비/몬스터 프리팹, 팀원 맵·음성 코드, Packages, ProjectSettings는 수정하지 않았다.

2026-11-06 최종 확인: 실제 Play Mode 기능 검사와 로컬 UDP 두 피어 검사 통과. 최종 섬광 표현 별도 검사 통과. 새 프리팹·씬 Missing Script/Reference 0, Console Error/Exception 0, 컴파일 완료. 원래 열린 Character_test 씬은 수정 없이 복원했다.
