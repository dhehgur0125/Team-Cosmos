# VOID LAST — 권총 사격·재장전·장착 연출

실제 대상: `Assets/Player/Character_test.unity`, `Assets/Player/Prefabs/Player/VOID_LAST_FPS.prefab`. 원본 권총은 `Prefabs/Weapon/SciFiPistol01_2.prefab`의 중첩 인스턴스로 유지합니다. 패키지·프로젝트 설정·다른 맵은 변경하지 않습니다.

## 조작과 동작

| 입력 | 동작 |
| --- | --- |
| 1 / 좌클릭 | 권총 장착 / 클릭당 한 발. 홀딩 자동 연사 없음 |
| 우클릭 | 기존 권총 조준. 재장전 중 차단 |
| R | 기본 1.5초 재장전. 다른 슬롯 선택 시 취소 |
| 4 / 5 / 6 | 주사기 장착. 0.4초 올라오는 연출 후 사용 가능 |
| F / Q | 기존 아이템 획득 / 드랍 유지 |

`FpsPistol`이 카메라 정중앙에서 최대 100m 히트스캔을 검사합니다. 자신의 캐릭터·장비 자식 Collider와 Trigger를 제외한 가장 가까운 충돌을 선택합니다. 기본 발사 간격은 0.18초이며 Inspector에서 변경할 수 있습니다. 장착 중·재장전 중·탄창 소진·다른 슬롯·사망 상태에서는 발사하지 않습니다. `HasLastHit`, `LastHit`, `Hit` 이벤트로 추후 피해 처리에 필요한 정보를 제공합니다. 현재 피해·총알 오브젝트·발사음·반동·네트워크 동기화는 추가하지 않았습니다.

탄창 용량/초기 탄창/초기 예비 탄약은 각각 10/10/30입니다. 발사는 탄창만 1 감소시키며, 재장전 완료 시 `min(부족한 탄창 수, 예비 탄약)`을 이동합니다. 0/30→10/20, 6/30→10/26, 0/5→5/0입니다. 가득 찬 탄창이나 예비 탄약 0에서는 재장전을 거절합니다. 취소 시 양쪽 탄약은 변하지 않습니다.

기존 `FpsWeaponView`가 장착·조준·재장전 위치를 함께 계산합니다. 재장전은 0.3초 하강, 0.9초 유지, 0.3초 상승이며 전체 시간은 `FpsPistol.Reload Duration`, 내려가는 위치는 `FpsWeaponView.Reload Offset`으로 조절합니다. 복잡한 탄창 분리나 새 애니메이션은 없습니다. 탄약 이동은 전체 연출 완료 시점입니다. 우측 하단의 흰색 탄약 UI는 1번 슬롯에서만 표시되며 기존 HP·핫바는 유지합니다.

## 탄흔

기존 리소스 조사에서 적합한 단순 탄흔을 찾지 못해 Player 내부에 작은 어두운 메시·머티리얼·프리팹을 만들었습니다. 충돌면 법선을 따라 회전하고 0.002m 띄워 붙입니다. Collider가 없으므로 다음 사격을 막지 않습니다. 플레이어 명중에는 탄흔을 붙이지 않습니다.

`FpsBulletMark`의 간단한 정적 FIFO 목록을 모든 사격자가 공유합니다. 화면 표시 여부와 무관하게 월드 전체 최대 7개이며, 8번째는 가장 오래된 것을 즉시 비활성화 후 제거합니다. 각 탄흔은 생성 30초 후 사라집니다. 현재 싱글 플레이 월드 기준이며 네트워크 간 동기화는 없습니다.

## 아이템 장착과 기존 손 표시

권총과 아이템의 공통 시간/보간 계산은 작은 `FpsEquipMotion` 클래스로 재사용합니다. 주사기를 선택하면 0.4초간 아래에서 올라오며, 이 동안 클릭은 사용을 시작하지 않습니다. 장착 뒤 새 클릭으로 기존 3초 홀딩을 시작합니다. 슬롯 전환·소비·빈 슬롯은 모델과 연출 상태를 정리합니다. 장착 시간은 사용 시간에 포함되지 않습니다.

3번 고유 아이템은 현재 모델·효과가 없습니다. 임의 모델을 만들지 않았으며 `FpsItemView.Unique Item Root`에 향후 카메라 자식 모델을 연결하면 같은 장착 연출을 사용할 수 있습니다. 현재 빈 3번 슬롯에는 아무것도 표시하지 않습니다.

주사기 자신/다른 플레이어 홀딩, 취소 조건, 15% 이동 속도, Pump 진행, F 획득·Q 드랍·버린 아이템 자동 획득은 유지합니다. 첨부 요구의 E 표기는 앞서 확정한 **F 상호작용**을 유지하도록 적용했습니다. 장비별 손 조정과 외부 아이템 표시는 `HAND_GRIP_README.md`를 참고하세요.

## 이번 생성·수정 파일

경로는 `Assets/Player/` 기준이며 새 Unity 에셋의 `.meta`도 포함합니다.

| 구분 | 파일 |
| --- | --- |
| 새 런타임 | `Scripts/FpsPistol.cs`, `FpsBulletMark.cs`, `FpsEquipMotion.cs` |
| 수정 런타임 | `Scripts/FpsInput.cs`, `FpsEquipment.cs`, `FpsWeaponView.cs`, `FpsItemView.cs`, `FpsInventory.cs`, `FpsHud.cs` |
| 새 에디터 | `Scripts/Editor/FpsCombatSetup.cs`, `FpsCombatValidation.cs` |
| 수정 에디터 | `Scripts/Editor/FpsHeldItemSetup.cs`, `FpsValidation.cs`, `FpsFarmingValidation.cs`, MCP 연결 대상 갱신 `Invoke-UnityMcp.ps1` |
| 수정 프리팹·상체 설정 | `Prefabs/Player/VOID_LAST_FPS.prefab`, `Animator/VOID_LAST_FPS.controller`의 Unarmed에 기존 Idle 연결. 상체 Avatar Mask는 기존 IK 설정 유지 |
| 새 탄흔 에셋 | `Prefabs/Effects/VOID_LAST_BulletMark.prefab`, `Materials/Effects/BulletMark.mat`, `BulletMarkMesh.asset` |
| 문서·검증 | 이 문서, 손/파밍 문서, `Validation/PlayModeResults.txt`, `PresentationAudit.txt`, Play Mode 렌더 이미지 |

씬 오브젝트를 재생성하지 않고 기존 FPS 프리팹 인스턴스가 변경 내용을 상속합니다. `Character_test.unity` 파일 자체는 이번 사격 작업에서 저장할 필요가 없습니다.

## 검증

Unity 메뉴 `VOID LAST > Player > 8 - Add Pistol Shooting and Reload`로 기존 FPS 프리팹에 사격 구성만 연결할 수 있습니다. `3 - Run Play Mode Validation`은 실제 Character_test에서 이동·점프·손·아이템 회귀 검증과 사격·재장전·탄흔 검사를 실행합니다. 임시 벽/보조 플레이어는 Play Mode에서만 생성하며 저장하지 않습니다.

검증 결과는 `Validation/PlayModeResults.txt`의 최신 시간과 마지막 상태를 확인하세요. 동작 API를 호출하는 실제 Unity Play Mode 검사이며 물리 키보드·마우스 입력 체감 검사는 아닙니다. 클릭/홀딩 체감, 재장전 속도·화면 배치, 여러 해상도, 다양한 벽 재질과 거리에서의 탄흔 가독성은 직접 플레이로 확인해야 합니다.

2026-10-30 21:19 KST 최종 실행은 **전체 자동 검사 통과**입니다. 이동·달리기·웅크리기·하체 점프·양손 권총·외부 주사기·장착 차단·홀딩 취소/회복·F/Q·HUD를 함께 검사했습니다. 사격은 10→9, 빈 탄창, 발사 간격, 자기 Collider 제외, 장착/재장전/사망 차단을 확인했습니다. 재장전 세 가지 수량 예시, 슬롯 전환 취소, 조준 복귀, 두 사격자의 전역 7개 탄흔 공유, 8번째 즉시 제거, 실제 Play 시간 30초 만료도 통과했습니다.

21:37 KST 최종 감사에서 C# 명령 컴파일/실행 성공, Console Error/Exception 0개, 씬 및 두 프리팹의 컴포넌트 571개 검사에서 Missing Script/Reference 0개였습니다. `Character_test`는 변경 표시 없는 편집 모드입니다. 권총 양손 자세, 주사기와 빈 왼팔, FPS 주사기, 탄흔 및 탄약 UI를 캡처로 확인했습니다. 정교한 손가락 주사 애니메이션, 실제 몬스터 피해, 탄약 파밍, 네트워크 및 모델이 없는 3번 아이템의 실제 사용은 구현 범위에 포함하지 않습니다.
