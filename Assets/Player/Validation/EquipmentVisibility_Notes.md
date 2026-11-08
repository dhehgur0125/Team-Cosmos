# 장비 중복 표시 수정

## 원인 및 수정

의료 키트의 3인칭 모델 참조와 카메라별 숨김 값은 올바르게 연결되어 있었지만, GPU Resident Drawer가 이전 표시 상태를 사용해 자기 FPS 카메라에도 모델을 그렸다. 수정 전 Character_test에서 의료 키트와 주사기에서 재현했고, 실행 중 생성한 배터리에서도 화면 표시 이상을 확인했다.

게임 코드 변경은 `Assets/Player/Scripts/FpsLocalVisibility.cs` 한 파일이다. 등록된 장비 MeshRenderer를 준비할 때 작은 MaterialPropertyBlock을 부여해 일반 렌더링 경로를 사용하게 했다. 셰이더에서 사용하지 않는 값이므로 색상·텍스처를 바꾸지 않는다. 기존 발광·색상 블록은 덮어쓰지 않는다. 원본 머티리얼을 복제하거나 수정하지 않으며, 실행 중 AddRenderers로 추가되는 무전기·스캐너·파밍 모델도 같은 처리를 받는다.

Unity 문서의 [GPU Resident Drawer 호환 조건](https://docs.unity3d.com/cn/6000.0/Manual/urp/make-object-compatible-gpu-rendering.html)에 따라 장비 렌더러만 캐시 대상에서 제외한다. 프로젝트 전체의 GPU 렌더 설정은 바꾸지 않았다. 기존 1인칭/3인칭 카메라별 숨김과 복원 구조는 유지한다.

## 실제 검사

- Character_test Play Mode, 카메라를 아래로 65도 회전한 상태.
- 고유 장비: 의료 키트, 빛 방출기, 무전기, 스캐너.
- 4·5·6번 파밍 슬롯: 주사기, 무전기, 배터리, 격리 카드, 절단기, 전력 모듈, 연료 커플러, 스캐너.
- 권총도 같은 표시 처리의 영향을 받으므로 함께 확인.
- 총 13개 장착 경우에서 일반 화면과 3인칭 모델을 지속적으로 숨긴 기준 화면의 픽셀 차이 0. 관찰 카메라에서는 전신과 장비를 유지하고 FPS 모델은 숨김.
- 스캐너가 기본 Character_test의 오프라인 플레이어에는 없어, 수정 후 검사는 기존 VOID_LAST_FPS_Network 프리팹을 임시로 생성해 진행했다. 네트워크 연결은 시작하지 않았고 패킷 동기화를 재검사한 것은 아니다.
- 최종 Play Mode 검사 중 Console 오류 0. 컴파일 성공. 장비 사용·회복·스턴 등 게임플레이는 변경하지 않았다.

씬·프리팹·머티리얼·Stage2_Ship·Lobby·VoiceChat·몬스터·패키지 설정은 이번 작업에서 저장하거나 변경하지 않았다. Character_test는 편집 모드로 돌아왔다.

## 파일

- 수정: `Assets/Player/Scripts/FpsLocalVisibility.cs`
- 추가 검사 도구: `Assets/Player/Scripts/Editor/FpsEquipmentVisibilityValidation.cs` 및 meta
- 기록: `EquipmentVisibility_Before.txt`, `EquipmentVisibility_After.txt`, `Visibility_Before_*.png`, `Visibility_After_*.png`, 이 문서 및 meta

Before 기록은 원래 문제를 재현한 실패 결과이며, 최종 판정은 After 기록을 기준으로 한다. 임시 테스트 바닥은 카메라 전환에 따른 GPU 가림 이력의 영향을 피하도록 구성했다.
