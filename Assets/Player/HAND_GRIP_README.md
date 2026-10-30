# VOID LAST — 장비별 손 그립과 외부 표시

현재 작업의 대상은 `Prefabs/Player/VOID_LAST_FPS.prefab`과 기존 `Character_test.unity`입니다. 원본 캐릭터·권총·주사기 프리팹 및 맵은 수정하지 않습니다.

## 아이템마다 잡는 위치가 다른 경우

화면 배치와 손 그립을 따로 조정합니다.

- **화면상의 위치**: `FpsItemView`의 Held/Use/Other Use Position·Euler로 주사기와 손 전체를 함께 이동합니다. 권총은 기존 `FpsWeaponView`를 사용합니다.
- **주사기를 잡는 손의 위치**: `FpsItemView`의 Hand Position·Euler·Scale로 주사기에 대한 장갑의 상대 위치만 조정합니다. 권총의 손에는 영향을 주지 않습니다.
- **외부에서 보이는 아이템의 부착 위치**: Humanoid 오른손의 `WorldItemSocket`은 주사기, `WorldWeaponSocket`은 권총 전용입니다. 원본 아이템 대신 해당 소켓의 위치·회전을 조정합니다.
- **외부 팔 자세**: Animator 오브젝트의 `FpsHeldItemPose`에서 권총 기본/조준 손 위치, 보조 손 오프셋, 주사기 손 위치를 별도로 조정합니다. 위치는 움직이는 가슴을 기준으로 합니다.

새 아이템을 추가할 때 공용 손이나 원본 모델의 좌표를 변경하지 말고 그 아이템에 맞는 소켓과 그립 설정을 추가합니다. 현재 아이템 종류는 주사기뿐이며, 범용 장비 데이터 시스템이나 네트워크 코드는 추가하지 않았습니다.

## 표시와 애니메이션

`FpsItemView`가 슬롯·소비·사망 상태에 맞춰 1인칭/외부 주사기를 함께 표시합니다. 외부 주사기는 원본 프리팹의 중첩 인스턴스이며 기존 Pump 구조를 그대로 사용합니다. 양쪽 Pump에 같은 사용 진행률을 적용합니다.

`FpsLocalVisibility`는 로컬 카메라에서 전신과 외부 아이템을 숨기고, 외부 카메라에서 1인칭 장갑·장비를 숨깁니다. 렌더 후 원래 플래그를 복원합니다.

기존 Pistol Idle·장착 애니메이션은 유지하며, `FpsHeldItemPose`가 상체 레이어의 손 IK를 사용해 권총 양손 그립과 주사기 오른손 그립을 보정합니다. 손가락은 기존 우주복 본에서 얻은 굽힌 자세를 사용합니다. 이동과 점프 하체 레이어는 유지합니다. 손가락이 펌프를 실제로 누르는 별도 애니메이션은 없습니다.

Unity 기본 [OnAnimatorIK](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/MonoBehaviour.OnAnimatorIK.html)와 [SetBoneLocalRotation](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Animator.SetBoneLocalRotation.html)을 사용하며 추가 패키지가 필요하지 않습니다.

## 적용 및 검증

Unity 메뉴 `VOID LAST > Player > 7 - Update Hand Grips and World Item`은 현재 FPS 프리팹과 기존 Animator의 상체 IK 설정만 갱신합니다. 전체 캐릭터/맵을 재생성하지 않습니다.

검증 메뉴는 `VOID LAST > Player > 3 - Run Play Mode Validation`입니다. 양손 목표 도달, 손목 좌우 순서, 주사기 오른손 부착, 카메라별 중복 숨김, 외부 펌프 진행률을 검사하고 관찰 화면을 저장합니다.

새 그립 작업은 Unity MCP 연결 복구 후 실제 적용·Play Mode·육안 검증이 필요합니다. 이전 UI/체력/파밍 검증 통과 기록과 이번 추가 작업의 완료 상태를 혼동하지 않습니다.
