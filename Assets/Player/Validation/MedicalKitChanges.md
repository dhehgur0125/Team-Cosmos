# 의료 키트 구현

현재 3번 슬롯에 의료 키트가 기본 연결되어 있다. 직업 선택, 실행 중 장비 교체, 범용 장비 프레임워크는 추가하지 않았다.

- 3번 장착 후 약 0.4초 상승 연출이 끝나면 사용 가능.
- 좌클릭으로 자신 치료, 우클릭으로 화면 중앙의 동료 치료를 시작한다. 클릭 후 1.5초 진행하며 성공한 경우에만 HP 50 회복 및 1회 소비.
- 시작/최대 3회. 자기 치료와 동료 치료가 공유한다. 최대 HP, 사망, 범위 이탈, 벽에 가림, 장비 변경, 장비 비활성화는 회복과 소비 없이 거부/취소한다.
- 치료 거리 기본 2m. 기존 FpsItemInteractor의 중앙 감지와 가림 검사를 재사용한다.
- 기존 주사기의 3초 홀딩·최대 HP의 30%(기본 HP에서 30) 회복은 그대로 유지한다.

## 파일

기존 파일 수정:

- `Assets/Player/Prefabs/Player/VOID_LAST_FPS.prefab`
- `Assets/Player/Prefabs/Player/VOID_LAST_FPS_Network.prefab`

추가:

- `Assets/Player/Scripts/FpsMedicalKit.cs`: 의료 키트의 회복 수치, 대상, 시간, 취소와 오프라인 사용 횟수.
- `Assets/Player/Scripts/FpsMedicalKitView.cs`: 공통 기준점의 모델/장갑, 장착·치료·복귀 연출과 의료 키트 UI.
- `Assets/Player/Scripts/FpsMedicalKitNetwork.cs`: 기존 NGO에 연결하는 의료 키트 전용 요청/완료 응답. 네트워크 프리팹에만 존재.
- `Assets/Player/Scripts/Editor/FpsMedicalKitSetup.cs`: Player 프리팹만 갱신하는 설정 도구.
- `Assets/Player/Scripts/Editor/FpsMedicalKitValidation.cs`: Character_test의 기능 및 로컬 UDP 호스트/클라이언트 검사.
- `Assets/Player/Prefabs/Player/Visuals/FPS_MedicalGlove.asset`: 기존 FPS_RightGlove의 손 부분을 그대로 복사하고, 분리되어 떠 보이던 소매 조각만 제외한 의료 키트 표시용 메시. 원본 장갑/FBX는 변경하지 않음.
- 이 폴더의 `MedicalKit*.txt`, `MedicalKit*.png` 및 해당 Unity 메타 파일: 검사 결과와 화면.

구급상자 원본은 사용자가 옮겨 둔 `Assets/Player/Prefabs/Item/SciFiMedKit01.prefab`을 중첩 인스턴스로 사용한다. 원본 자체는 수정하지 않았다.

## Inspector 연결과 추후 변경 지점

기본 프리팹 루트의 `FpsMedicalKit`에서 View, Maximum Uses, Starting Uses, Heal Amount, Use Duration, Treatment Distance를 조절한다.

`FpsMedicalKitView`는 다음 참조를 가진다.

- Medical Kit: 같은 루트의 `FpsMedicalKit`.
- Local Root: `CameraPivot/FPS Camera/MedicalKitRoot`. 이 아래에 구급상자 인스턴스와 기존 장갑 복사본이 함께 있어 연출 중 상대 위치가 유지된다.
- World Root: 우주비행사 RightHand 본의 `WorldMedicalKitSocket`. 원격 카메라에서는 이 모델을 표시한다.
- Uses Label: `FPS HUD/Medical Kit Uses`. 의료 키트가 사라지거나 3번에서 벗어나면 숨긴다.
- Held Position/Euler, Self/Other Offset, Equip Offset/Duration, Transition Speed: 표시 위치와 연출 조절값.

나중에 다른 직업 장비를 구현할 때에는 기본 프리팹의 `FpsMedicalKit`·`FpsMedicalKitView`를 해당 장비의 기능/표시 컴포넌트로 대체하고, 위 두 모델 기준점과 UI 참조를 해당 장비용으로 바꾸면 된다. 기존 `FpsEquipment`, 이동, 권총, 파밍 인벤토리에는 의료 키트 분기나 의존성을 넣지 않았다. 지금은 다른 장비를 Inspector에서 꽂아 즉시 작동시키는 교체 시스템을 구현한 상태가 아니다.

네트워크 프리팹의 `FpsMedicalKitNetwork`는 다른 직업도 의료 키트 치료를 **받기 위한 수신 경로**로 남길 수 있다. 자신의 FpsMedicalKit이 없어도 수신은 가능하며 자신의 의료 키트 사용은 막힌다. 다른 고유 장비의 네트워크 사용 기능은 그 장비를 개발할 때 별도로 연결한다.

## 네트워크 권한

기존 플레이어 HP는 소유 클라이언트 권한이다. 이 구조를 바꾸지 않았다. 의료 키트 사용 시간·횟수·현재 장비·대상·거리·가림은 서버에서 검사한다. 1.5초가 끝나면 대상 HP 소유자에게 서버가 완료 요청을 보낸다. 대상도 유효성을 다시 확인하며 요청 번호당 한 번만 Heal을 호출한다. 성공 응답이 확인된 경우에만 서버가 횟수를 줄여 모든 피어에 복제한다. 처리 중 추가 요청과 중복 완료 응답은 소비나 중복 회복을 일으키지 않는다.

## 실제 확인 범위

`MedicalKitResults.txt`: 실제 Character_test Play Mode에서 장착 대기, 1.5초·50 회복, 3회 공유, 최대 HP, 거리/벽/장비 변경 취소, 장비 제거, 기존 주사기·권총·재장전·손전등을 확인했다.

같은 Editor의 두 NGO 인스턴스를 실제 로컬 UDP로 연결해 호스트/클라이언트 각각의 자기 치료, 양방향 동료 치료, HP·횟수 동기화, 중복 시작 거부, 클라이언트 취소를 확인했다. 단일 Editor의 물리 월드에서 복제본끼리 가리지 않도록 테스트 중에만 서로 다른 좌표에 복제본을 배치했다.

`MedicalKitVisualResults.txt`: 의료 키트 위치, 한글 횟수 표시·갱신·숨김을 확인했다.

별도 PC 간 Relay 접속, 패킷 지연/손실, 로비 보이스, 몬스터 AI 및 층 이동은 이번 검사 대상이 아니었다. Stage2_Ship/Lobby 씬, 기존 멀티플레이 스크립트, 프로젝트/패키지 설정을 저장하거나 변경하지 않았다.
