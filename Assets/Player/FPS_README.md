# VOID LAST FPS — Jump / SF Pistol 업데이트

체력·파밍·F 상호작용·좌/우클릭 홀딩 사용의 최신 내용은 [HEALTH_FARMING_README.md](HEALTH_FARMING_README.md)를 참고합니다. 현재 FPS/원본 캐릭터 프리팹은 `Prefabs/Player/` 폴더에 있습니다. 아래 4번 슬롯 설명은 이전 단계의 기록이며 현재 파밍 슬롯은 4/5/6입니다.

2026-10-30, Unity 6000.4.9f1. 테스트 대상은 `Assets/Player/Character_test.unity`입니다.

## 실행 및 조작

`Character_test`를 열고 Play를 누릅니다. 기존 Input System 설정(Both)을 유지했습니다.

| 입력 | 기능 |
| --- | --- |
| WASD | 로컬 좌표 기준 이동, 대각선 속도 정규화 |
| Left Shift | 전진 달리기. 기존처럼 조준 입력이 우선하며 조준 중 달리기는 차단 |
| Left Ctrl | 누르는 동안 웅크리기. 낮은 천장 아래에서는 해제해도 일어서지 않음 |
| Space | 지상에서 점프. 공중/웅크림/아직 일어서는 중에는 거부 |
| 마우스 | yaw 및 ±80° pitch |
| 1 / 2 / 3 / 4 | 권총 / 근접 / 고유 아이템 / 파밍 아이템 |
| 우클릭 | 1번 슬롯에서 권총을 중앙 조준 위치로 보간 |
| 좌클릭 | 2번 슬롯에서 일반 근접 애니메이션 |
| Escape / Game 뷰 클릭 | 커서 해제 / 다시 잠금 |

## 기존 구성에서 변경한 부분

- 원본 `Prefabs/Astronaut_Pilot_Full.prefab`과 `Prefabs/Weapon/SciFiPistol01_2.prefab`은 수정하지 않습니다. 실제 권총 위치는 요청에 적힌 `Weapon/` 대신 **`Assets/Player/Prefabs/Weapon/`**입니다.
- `Prefabs/VOID_LAST_FPS.prefab`에 카메라 자식 `WeaponRoot`와 원본 권총의 중첩 인스턴스를 추가했습니다. Button / Clip / Trigger / UCX 구조를 유지하고, 인스턴스의 콜라이더만 끕니다.
- 오른손 Humanoid Bone에 `WorldWeaponSocket`과 별도 권총 인스턴스를 추가했습니다. 실제 Pistol Idle 포즈를 샘플링하여 손의 기준 축을 구했습니다. FPS 인스턴스는 로컬 카메라에만, 손의 인스턴스는 다른 카메라에만 표시합니다.
- `FpsLocalVisibility`는 로컬 카메라 렌더 동안 전신과 손의 권총을 숨기고, 다른 카메라 렌더 동안 FPS 권총을 숨깁니다. 렌더가 끝나면 원래 Renderer 상태를 복원합니다. 모델·Animator·Bone·CharacterController를 제거하지 않습니다.
- `FpsWeaponView`의 Hip/Aim Position, Euler Angles, Model Scale, Aim Transition Speed를 Inspector에서 조절합니다. 초기값은 기본 위치 `(0.2, -0.18, 0.42)`, 조준 위치 `(0, -0.075, 0.42)`, 회전 `(0, 0, 0)`, 크기 `1.25`입니다. 렌더로 실제 총구가 모델 +Z 방향임을 확인했습니다.
- 1번 슬롯에서만 두 권총 표현을 활성화합니다. 다른 슬롯과 사망 시 모두 숨깁니다. 3/4번은 슬롯 선택만 구현합니다.

## 이동 애니메이션 변경

기존 MoveX/MoveY에 속도까지 넣던 구성을 방향과 속도로 분리했습니다.

- `MoveX/MoveY`: 실제 CharacterController 이동량을 캐릭터 로컬 좌표로 바꾼 단위 방향. 기본 보간 0.12초.
- `Gait`: 정지 0 / 보행 1 / 달리기 2. 바깥 1D Tree에서 속도를 혼합합니다.
- 내부 `Walk Directions`, `Run Directions`: 2D Simple Directional Tree. 대각선에서는 이웃한 두 방향을 혼합하고 Idle/Run이 잘못 섞이지 않도록 구성했습니다.
- 보행 클립의 주기를 약 0.85초로 맞췄습니다. 발 위치 샘플을 바탕으로 후진 cycleOffset -0.08, 왼쪽 +0.08을 적용했습니다. Run의 전진 클립은 원래 속도를 유지합니다.
- 좌우 이동과 웅크린 전후 이동의 Root Rotation 기준을 Body Orientation으로 바꾸어, 소스에 포함된 약 30~60도 기준 방향 차이를 줄였습니다.
- 웅크림은 `CrouchAmount`(이동 크기)와 `CrouchDirection`(전후)을 분리했습니다. W+D/S+A처럼 대각선으로 이동해도 전후축 값 감소 때문에 Idle이 섞이지 않습니다.

제약: 진짜 대각선·측면 달리기·웅크린 좌우 클립은 없습니다. 달리기 대각선은 Run과 빠르게 재생한 측면 보행의 조합입니다. 순수 웅크린 측면 이동에는 전진 웅크리기를 사용합니다. 소스의 보폭·앉는 깊이가 달라 발 미끄러짐과 자세 차이를 완전히 제거하지는 못합니다. 전용 클립이나 추후 발 IK가 필요한 부분이며 이번에는 새 애니메이션을 생성하지 않았습니다.

## 점프

- 실제 `Animator/Jump.fbx`의 내부 클립 `mixamo.com`(0.600초)을 검사했습니다. Humanoid Avatar가 유효하며 Loop Time/Pose는 껐습니다.
- `FpsMotor`의 Jump Height 기본값은 1.1m입니다. `sqrt(-2 * gravity * jumpHeight)`로 수직 속도를 만들고 CharacterController.Move로 이동합니다. 지상 검사, 이중 점프 차단, 웅크림 차단 및 머리 위 충돌을 처리합니다.
- `Lower Body Jump` 레이어와 `VOID_LAST_LowerBody.mask`는 좌우 다리만 포함합니다. Root, Body, 머리, 팔, 손, IK는 제외합니다. 상체 Pistol Idle/장비 자세를 유지합니다.
- `JumpPhase`를 수직 속도에 맞춰 상승/정점/하강으로 진행시킵니다. 공중 시간이 길어지면 하강 포즈를 유지하고 실제 착지 시 레이어를 부드럽게 끕니다. 짧은 클립 길이에 맞춰 강제로 착지하지 않습니다.
- Apply Root Motion은 계속 꺼져 있습니다. Jump의 Root Rotation/Y/XZ는 Bake Into Pose로 설정했으며 하체 Mask에서 Root 이동을 제외해 물리 점프와 중복되지 않도록 했습니다.

## 애니메이션 목록

모든 클립의 내부 이름은 `mixamo.com`입니다. 파일과 Avatar 및 실제 포즈/발 접지 데이터를 검사했습니다.

- 반복 10개: Idle, Walking, Walking Backwards, Left Strafe Walk, Right Strafe Walk, Standard Run, Crouching Idle, Crouched Walking, Walk Crouching Backward, Pistol Idle.
- 일회성 6개: Standing To Crouched, Crouched To Standing, Unarmed Equip Underarm, Standing Melee Punch, Dying, Jump.
- 별도 Pistol Aim Idle은 여전히 없습니다. 외부 전신의 상체에는 Pistol Idle을 유지하고, 1인칭 조준은 권총 Transform 보간으로 구현했습니다.

## 이번 생성/수정 파일

경로는 `Assets/Player/` 기준이며 생성된 파일의 `.meta`도 포함합니다.

| 구분 | 파일 |
| --- | --- |
| 신규 런타임 | `Scripts/FpsWeaponView.cs` |
| 수정 런타임 | `Scripts/FpsInput.cs`, `FpsMotor.cs`, `FpsEquipment.cs`, `FpsAnimation.cs`, `FpsLocalVisibility.cs` |
| 유지 런타임 | `Scripts/FpsLook.cs` |
| 신규 에디터 | `Scripts/Editor/FpsAssetUpgrade.cs`, `FpsFeatureValidation.cs` |
| 수정 에디터 | `Scripts/Editor/FpsAssetSetup.cs`, `FpsValidation.cs` |
| 수정 에셋 | `Prefabs/VOID_LAST_FPS.prefab`, `Animator/VOID_LAST_FPS.controller`, `Character_test.unity` |
| 신규 Mask | `Animator/VOID_LAST_LowerBody.mask` |
| 수정 Import 설정 | `Animator/Jump.fbx.meta`, `Left Strafe Walk.fbx.meta`, `Right Strafe Walk.fbx.meta`, `Crouched Walking.fbx.meta`, `Walk Crouching Backward.fbx.meta` |
| 검사 자료 | `Validation/LocomotionJumpInspection.csv`, `PlayModeResults.txt` |
| 렌더 자료 | `Validation/Pistol_Hip_FPS.png`, `Pistol_ADS_FPS.png`, `Pistol_Observer.png`, `Jump_Observer.png`, `Unarmed_FPS.png`, `Diagonal_WD/WA/SD/SA.png` 및 기존 렌더 갱신 |
| 문서 | `FPS_README.md` |

에디터 업데이트 메뉴는 기존 에셋을 제자리에서 갱신합니다. 작업 중 `AssetModificationProcessor` 저장 필터로 Player 밖의 dirty 에셋 및 두 원본 프리팹 저장을 제외합니다. 다른 일반 Unity 저장 작업에는 이 필터가 적용되지 않습니다.

## 검증

Unity 메뉴 `VOID LAST > Player > 3 - Run Play Mode Validation`에서 재실행합니다. 실제 `Character_test`만 Play 시작 씬으로 지정해 실행하고 종료 후 원래 설정을 복원합니다. 검증 동안의 관찰 카메라와 상태 변경은 저장하지 않습니다.

자동 검증: 기존 이동·달리기·웅크리기·천장/벽 충돌·카메라 높이, 네 방향 대각선 Blend 가중치, 캐릭터 로컬 좌표 이동, 권총 자식/머티리얼/콜라이더, 1~4 슬롯 표시, 조준 보간/복귀, 카메라 회전 추종, 실제 렌더 콜백에서 전신과 두 권총의 상호 배제, 점프/이중 점프 차단/웅크림 차단/착지, 공중 상체 유지 및 사망 상태를 확인합니다. 결과는 `Validation/PlayModeResults.txt`입니다.

렌더를 검토해 권총의 앞뒤 방향·크기·머티리얼, FPS 전신 숨김, 외부 전신/오른손 권총과 점프 상체 유지도 확인했습니다. 화면의 기존 바닥 소품은 사용자의 테스트 씬 배치를 유지한 것입니다.

자동 검증은 공개 동작 메서드와 Animator를 구동하는 Play Mode 테스트입니다. 물리 키보드/마우스 입력을 누르는 테스트는 아닙니다. 직접 확인할 사항은 WASD/Space/Shift/Ctrl/1~4/우클릭 입력 체감, 여러 화면비의 권총 위치, 움직이는 발의 미끄러짐, 웅크리기 클립 간 깊이 차이, 외부 손가락과 권총의 세밀한 그립입니다.

마지막 Play Mode 실행은 모든 자동 검사를 통과했습니다. 점프 정점은 설정 1.1m에 대해 약 1.05m였으며, Unity Console의 Error/Exception은 0개이고 직렬화된 Missing Reference는 발견되지 않았습니다.

사격·피해·재장전·아이템 효과·네트워크·새 손 모델은 추가하지 않았습니다. 패키지 설치 및 프로젝트 설정 변경은 없습니다.

## 작업 범위 확인

Unity 저장 중 외부 머티리얼 12개에 호환 속성이 변경되고 ProBuilder 설정에 `ShapeComponent.ResetSettings=false` 항목 하나가 자동 추가되었습니다. 사용자가 승인한 이 13개 파일의 자동 변경만 복원했습니다. 머티리얼 12개는 Git 차이가 없으며, ProBuilder 파일은 작업 시작 시점의 SHA-256 해시와 일치합니다. 원본 우주비행사·권총 프리팹 및 메타, 외부 Demo 씬, Packages와 ProjectSettings.asset도 시작 시점의 해시를 유지합니다. 다른 기존 사용자 변경은 복원하지 않았습니다.

ProBuilder 패키지는 초기화 시 위 항목을 메모리에 추가하고 어셈블리 재로드·Unity 종료 시 저장합니다. 따라서 이후 Unity 사용 중 같은 항목이 다시 생길 수 있습니다. 패키지 자체의 코드는 수정하지 않았으며, 마지막 에셋 정리 후 디스크 복원을 확인했습니다.
