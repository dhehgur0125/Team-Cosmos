# SciFiPistol02_2 권총 적용 기록

## 적용 결과

- 게임용 Variant: `Assets/Player/Prefabs/Weapons/VOID_LAST_SciFiPistol02_2.prefab`
- 원본: `Assets/TirgamesAssets/SciFiWorld/Shared/Weapons/Prefabs/SciFiPistol02_2.prefab`. 원본 FBX·프리팹·메시·머티리얼은 변경하지 않았다.
- Player 기본 프리팹과 Network Variant의 1인칭·전신 권총을 교체했다. 기존 손전등 자식과 참조를 보존했다.
- 원본 프리팹에서 빠져 있던 Clip 위치는 게임용 Variant에서만 FBX의 장착 위치로 복원했다. Scope/Dot, Slide, Trigger 구조는 유지했다.
- 물리적인 `Scope/Dot` 중심을 ADS 카메라 중앙에 정렬했다. 카메라 기준 Raycast·탄약·피해량·총소리·기존 사격 RPC는 유지했다.
- FPS 팔 누락 원인은 손목 부근까지만 추출한 기존 정적 장갑 메시와 낮은 장착 위치였다. 원래 우주복의 오른팔 메시를 권총 전용으로 추출하고, 손과 총을 같은 WeaponRoot에서 움직인다. 원본 Humanoid/애니메이션 및 다른 장비 장갑은 변경하지 않았다.
- 카메라별 숨김이 GPU Resident Drawer의 캐시에 영향을 받지 않도록 WeaponRoot와 WorldWeaponSocket에 Unity 기본 `DisallowGPUDrivenRendering` 컴포넌트를 연결했다. 렌더러 중복 등록에 의한 표시 복원 문제도 방지했다. ProjectSettings나 URP 설정은 변경하지 않았다.

## 탄창 낙하

- 재장전 진행률 22%에 실제 Clip 메시와 머티리얼을 복제한다. 기존 재장전 시간 1.5초 기준 약 0.33초이다.
- 원본 Clip은 잠시 숨기고 완료·취소·장비 전환 시 복원한다. 한 재장전에서 한 번만 생성한다.
- 낙하 탄창: Rigidbody 0.08kg, BoxCollider, 바닥 충돌, 5초 후 제거. 플레이어·NavMeshAgent 캐릭터·다른 탄창과의 충돌을 제외한다.
- 탄창은 시각용이며 기존 총알 Raycast에서도 제외한다. 탄약 계산은 바꾸지 않았다.
- 기존 NGO 플레이어에 재장전 시작/종료 시각만 전달하는 `FpsReloadVisualNetwork`를 추가했다. 각 피어가 자체 탄창을 재생하며 네트워크 탄창 오브젝트는 생성하지 않는다.

## Inspector 조정

기본 설정은 `Assets/Player/Prefabs/Player/VOID_LAST_FPS.prefab`에 적용되어 있다.

| 컴포넌트/위치 | 설정 |
|---|---|
| FpsWeaponView / Hip Position | (0.18, -0.10, 0.50) |
| FpsWeaponView / Aim Position | (0, -0.0805, 0.45) |
| FpsWeaponView / Hand Position | (0.015, -0.025, -0.045) |
| FpsWeaponView / Hand Euler | (90, 0, 0) |
| FpsWeaponView / Reload Offset | (0.04, -0.045, -0.02) |
| FpsWeaponView / Reload Euler Angles | (8, 0, 20) |
| FpsMagazineDrop / Drop At Progress | 0.22 |
| FpsMagazineDrop / Lifetime | 5초 |
| 두 권총 Mount의 MuzzleFlash / Local Position | (0, 0.032, 0.128) |

ADS X/Y를 바꾸면 실제 조준기 중심이 틀어질 수 있다. 가까움 정도를 바꿀 때는 Aim Position의 Z부터 조정한다. 장갑은 권총 전용 메시이며 새로운 팔 리그나 왼손 재장전 애니메이션을 만든 것은 아니다.

## 확인 방법과 범위

`Assets/Player/Character_test.unity`를 열고 Play: 1번 권총, 우클릭 ADS, 좌클릭 발사, R 재장전. 재장전 도중 2/3번 등으로 전환하면 원본 Clip이 복원된다. 별도 수동 프리팹 연결은 필요 없다.

- Play Mode 자동 검사: Idle/Walk/Run/Aim/Shoot/Reload 화면, 조준기 중앙 오차 0.1% 미만, 카메라 중앙 명중, 기존 피해 30과 탄약 계산, 손·총 동시 반동과 복귀.
- 탄창 검사: 한 번 생성, 원본 숨김/복원, 바닥 충돌·정지, 플레이어 충돌 제외, 5초 제거, 발사선에 탄창이 있어도 기존 타깃 명중, 분리 전/후 장비 전환 취소.
- 관찰 카메라에서 전신과 권총 표시, 로컬 카메라에서 전신 권총 숨김을 확인했다.
- 같은 Editor 프로세스의 실제 로컬 UDP NGO 호스트/클라이언트 두 개에서 양쪽 소유자의 재장전·취소·단일 탄창 생성 및 기존 Shot RPC 전달을 확인했다.
- 기존 최신 GunNoise의 정적 이벤트가 한 Editor 내 두 피어에 공유되므로, 네트워크 사격 검사는 기존 시각 효과 이벤트/RPC 전달을 사용했다. 실제 사격/명중/탄약은 오프라인 Play Mode에서 검사했다.
- 서로 다른 PC의 Relay/Lobby 접속, 지연·패킷 손실, 보이스챗, 실제 키보드/마우스 조작감은 미검증이다. 기존 몬스터·의료 키트·빛 방출기 전체 게임플레이를 재검사한 것은 아니다.
- 변경한 프리팹 5개와 Character_test의 Missing Script/Reference/Material 검사 결과 모두 0. 컴파일 성공.
- 테스트 중 기존 Vivox Editor 인증 요청에서 HTTP 403이 한 번 발생했다. 권총 코드 오류와 별개이며 음성 서비스 설정은 수정하지 않았다.

최종 자동 검사 결과: `Assets/Player/Validation/Pistol02Results.txt`. 같은 폴더의 `Pistol02_*.png`는 실제 Unity 카메라 캡처이다. 자동 재검사는 `VOID LAST > Player > Validate Pistol 02` 메뉴를 사용하며 씬은 저장하지 않는다.

## 생성·수정 파일

생성 (각 Unity 에셋의 `.meta` 포함):

- `Assets/Player/Prefabs/Weapons/VOID_LAST_SciFiPistol02_2.prefab`
- `Assets/Player/Prefabs/Player/Visuals/FPS_PistolRightArm.asset`
- `Assets/Player/Scripts/FpsDroppedMagazine.cs`
- `Assets/Player/Scripts/FpsMagazineDrop.cs`
- `Assets/Player/Scripts/FpsReloadVisualNetwork.cs`
- `Assets/Player/Scripts/Editor/FpsPistol02Setup.cs`
- `Assets/Player/Scripts/Editor/FpsPistol02Validation.cs`
- 이 문서와 `Assets/Player/Validation/Pistol02Results.txt`, `Pistol02_*.png`

수정:

- `Assets/Player/Prefabs/Player/VOID_LAST_FPS.prefab`
- `Assets/Player/Prefabs/Player/VOID_LAST_FPS_Network.prefab`
- `Assets/Player/Prefabs/Player/VOID_LAST_FPS_FlashEmitter_Test.prefab`
- `Assets/Player/Prefabs/Player/VOID_LAST_FPS_FlashEmitter_Network_Test.prefab`
- `Assets/Player/Scripts/FpsPistol.cs`: 시각용 탄창만 Raycast 대상에서 제외하는 한 줄.
- `Assets/Player/Scripts/FpsWeaponView.cs`: 기존 재장전 위치 연출에 회전 오프셋 추가.
- `Assets/Player/Scripts/FpsLocalVisibility.cs`: 렌더러 중복 등록 방지.
- `Assets/Player/Scripts/Editor/FpsPresentationSetup.cs`: 권총 전용 팔 추출 옵션 추가.

FlashEmitter 테스트 Variant 두 개는 삭제된 이전 권총의 렌더러 참조만 수리했다. 장비 기능/선택은 변경하지 않았다. 모든 이번 변경은 Assets/Player 내부이며 Stage2_Ship, Lobby, Monster, VoiceChat, Packages, ProjectSettings 및 원본 권총은 수정하지 않았다.
