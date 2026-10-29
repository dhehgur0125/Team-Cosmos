# VOID LAST — 체력·파밍·홀딩 사용

2026-10-30 / Unity 6000.4.9f1. 기존 `Character_test.unity`와 `Prefabs/Player/VOID_LAST_FPS.prefab`에 추가했습니다. 기존 이동·달리기·웅크리기·점프·Animator는 재사용합니다.

## 조작

| 입력 | 동작 |
| --- | --- |
| H | 현재 HP 절반 감소. Character_test 씬의 테스트 컴포넌트이며 Editor/Development Build에서만 작동 |
| F | 카메라 중앙 근처 주사기 획득. 이전 E 입력은 사용하지 않음 |
| 1 / 2 / 3 | 기존 권총 / 근접 / 고유 아이템 |
| 4 / 5 / 6 | 파밍 핫바 1 / 2 / 3칸 선택 |
| 좌클릭 홀딩 | 장착한 주사기를 자신에게 사용. 3초 유지 후 회복 |
| 우클릭 홀딩 | 주사기를 들고 가까운 다른 플레이어를 겨냥하여 사용. 3초 유지 후 대상 회복 |
| Q | 선택한 파밍 아이템 드랍. 사용 중과 1~3번 장비에서는 차단 |

1번 슬롯의 우클릭 조준과 2번 슬롯의 좌클릭 근접 동작은 유지합니다. 권총 선택 시 약 0.3초 동안 아래에서 올라오는 연출을 추가했습니다.

## 체력과 사용 규칙

- `FpsHealth`: 최대/시작 HP 기본 100. `TakeDamage`와 `Heal`을 분리하고 0~최대 HP로 제한합니다. 사망 시 기존 `FpsAnimation.Die()`로 이동 차단과 사망 애니메이션을 연결합니다. 회복으로 부활하지 않습니다.
- `FpsInventory`: 중첩 없는 3칸입니다. 획득 시 앞쪽 빈칸부터 채우며, 가득 차면 월드 아이템을 유지합니다.
- 주사기는 사용 완료 시 **받는 플레이어 최대 HP의 30%**를 한 번 회복하고, 사용한 플레이어의 선택 슬롯에서 1개 소비합니다. 도중에 다른 회복으로 HP가 가득 차면 소비하지 않습니다.
- 버튼 해제, 슬롯 변경, 사용자/대상 사망, 대상 비활성화·제거, 대상 거리 이탈 또는 시야 차단 시 소비 없이 취소합니다. 새 클릭 없이 사용이 자동으로 반복되지 않습니다.
- 사용 중 Q와 중복 사용을 차단합니다. 이동은 기본 속도의 **15%**, 달리기는 차단합니다. 취소/완료 후 정상 속도로 복귀합니다.
- 다른 플레이어 사용은 같은 씬의 `FpsHealth`와 활성 Collider를 가진 대상에 적용합니다. 기본 거리는 2.2m이며 최초 겨냥한 대상을 유지하고 이후 거리와 가림을 검사합니다. 네트워크 전송·소유권 코드는 없습니다.

## 획득과 드랍

원본 주사기는 실제 확인한 `Prefabs/Item/SciFiMedInjector01_2.prefab`입니다. 원본은 수정하지 않았습니다. 이를 중첩 인스턴스로 재사용한 `Prefabs/Item/VOID_LAST_SyringePickup.prefab`에 물리 Collider·Rigidbody·획득 Trigger를 구성했습니다. 월드 모델은 1.4배 크기입니다.

F 획득 거리는 기본 2.5m, 보조 SphereCast 반경은 **0.10m**입니다. 카메라 중심선 근처 후보를 찾은 뒤 해당 아이템까지 시야를 검사합니다. 자신의 캡슐은 감지에서 제외하고, 닫힌 면이나 벽 너머는 획득하지 않습니다. 서랍 열기/닫기 시스템은 현재 프로젝트에서 발견되지 않아 추가하지 않았습니다. 추후 서랍 입력을 합칠 때는 통합 상호작용 처리에서 `TryPickup()` 성공 시 서랍 처리를 실행하지 않으면 됩니다.

Q는 플레이어 전방 경로와 생성 공간을 검사합니다. 벽이나 캡슐과 겹치는 위치에는 생성하지 않고 슬롯을 유지합니다. 성공하면 실제 주사기가 Rigidbody로 바닥에 떨어집니다.

맵에 처음 놓인 주사기는 F로만 획득합니다. 버린 주사기는 반경 0.65m Trigger로 자동 획득하며, 1.2초 지연과 드랍한 플레이어의 범위 이탈 조건을 모두 적용합니다. 가득 찬 상태에서는 월드에 남으며 빈칸이 생기면 다시 획득할 수 있습니다.

## 화면과 연출

- 왼쪽 위 HP 숫자·게이지, 하단 중앙 4/5/6 핫바, 정중앙 작은 흰 점을 추가했습니다. CanvasScaler와 앵커로 위치를 유지합니다.
- 핫바는 획득·선택·사용·취소·드랍 시 나타나고, 3초 동안 조작이 없으면 0.5초에 걸쳐 투명해집니다. 내용과 선택 상태는 유지됩니다.
- 주사기는 카메라의 `ItemRoot`에 표시합니다. 자신에게 사용할 때 바늘을 몸 쪽으로 돌리고, 다른 플레이어에게 사용할 때 앞으로 내미는 Transform 보간 연출입니다. 별도 애니메이션 클립·손·IK는 만들지 않았습니다.
- `FpsLocalVisibility`의 기존 카메라별 표시 구조에 주사기 Renderer를 등록했습니다. 다른 카메라에는 자신의 FPS 주사기가 중복 표시되지 않습니다.
- 우주복의 손·팔은 몸통과 같은 `Astronaut_Suit_Body_Mesh`에 포함되어 있어 간단히 재사용하기 어렵습니다. 원본 메시를 분리하거나 복잡한 리깅을 추가하지 않고 손 표현은 보류했습니다.
- 임시 UI는 기본 내장 폰트를 사용하는 영어 라벨입니다. 아이콘/외부 폰트 에셋은 추가하지 않았습니다.

## Inspector에서 조절

`FpsHealth`: Maximum/Starting Health. `FpsInventory`: Use Duration, Syringe Heal Fraction. `FpsItemInteractor`: Pickup Distance, Pickup Assist Radius, Player Use Distance, Drop Distance/Clearance. `FpsMotor`: Item Use Speed Multiplier. `FpsWeaponView`: Equip Duration/Offset. `FpsItemView`: 대기/자기 사용/타인 사용 위치·회전·크기. `FpsHud`: Visible/Fade Seconds. `FpsWorldPickup`: Pickup Delay, 자식 Trigger 반경.

## 생성·수정 파일

모든 경로는 `Assets/Player/` 기준이며 신규 파일의 `.meta`를 포함합니다.

| 구분 | 파일 |
| --- | --- |
| 새 런타임 | `Scripts/FpsHealth.cs`, `FpsHealthDebugInput.cs`, `FpsInventory.cs`, `FpsWorldPickup.cs`, `FpsItemInteractor.cs`, `FpsItemView.cs`, `FpsHud.cs` |
| 수정 런타임 | `Scripts/FpsInput.cs`, `FpsEquipment.cs`, `FpsMotor.cs`, `FpsWeaponView.cs` |
| 새 에디터 | `Scripts/Editor/FpsFarmingSetup.cs`, `FpsFarmingValidation.cs` |
| 수정 에디터 | `Scripts/Editor/FpsAssetSetup.cs`의 이동된 프리팹 경로, `FpsValidation.cs`, `FpsFeatureValidation.cs`의 회귀 검증 |
| 새 프리팹 | `Prefabs/Item/VOID_LAST_SyringePickup.prefab` |
| 수정 프리팹·씬 | `Prefabs/Player/VOID_LAST_FPS.prefab`, `Character_test.unity` |
| 검증 자료 | `Validation/PlayModeResults.txt`, `Farming_Full_HUD.png`, `Syringe_Using_HUD.png`, `Syringe_OtherPlayer_HUD.png`, `Syringe_Held_FPS.png`, `Syringe_Observer.png` 및 기존 FPS 검사 이미지 갱신 |
| 문서 | 이 문서와 `FPS_README.md` |

## 검증 및 수동 확인

Unity 메뉴 `VOID LAST > Player > 3 - Run Play Mode Validation`으로 기존 FPS와 새 기능을 함께 검증합니다. 테스트는 실제 Character_test Play Mode의 물리·Trigger·Animator·렌더·HUD를 사용합니다. 홀딩 입력은 동작 API에 명시적으로 전달하므로 물리 마우스를 직접 누른 검사는 아닙니다. 테스트용 다른 플레이어와 벽은 Play 중에만 생성하며 씬에 저장하지 않습니다.

확인 항목: HP 100→50→25, 회복 상한/사망, F 획득·중복 방지·3칸 제한, 작은 조준 오차와 가림, 4~6 선택, 좌/우클릭 홀딩 완료·해제·슬롯 변경·거리 이탈·가림 취소, 사용 중 감속, 다른 플레이어만 회복, Q 벽 검사·물리 낙하·자동 재획득, 즉시 재획득 방지, 핫바 페이드, UI 앵커, 카메라별 주사기 표시, 기존 이동·점프·권총 기능입니다.

최종 실행은 **전체 자동 검증 통과**입니다. `Validation/PlayModeResults.txt`에 기록했습니다. Unity C# 컴파일이 성공했고, 최종 Console Error/Exception 조회는 0개, 테스트 씬의 Missing Script/Reference 검사도 0개입니다. `Character_test`는 저장된 편집 모드로 남겼습니다.

실제 Game 뷰에서 자기/타인 사용 화면을 캡처해 주사기 방향과 HP/핫바 표시를 확인했습니다. 직접 확인할 사항은 F 조준 허용 폭의 체감, 3초 홀딩과 감속 체감, 여러 해상도의 UI/주사기 크기, 향후 실제 서랍 상호작용 통합입니다. 네트워크 협동 플레이와 실제 손 주사 애니메이션은 검증 대상이 아닙니다.

Unity 서비스(Vivox/Unity Connect)의 HTTP 403 인증 오류가 별도로 관측됐습니다. 플레이어 컴파일/동작 문제와 구분했으며 서비스 설정이나 Packages는 변경하지 않았습니다.

## 원본 및 범위 보존

원본 우주비행사·권총·주사기 프리팹과 메타, 외부 Demo 씬, Packages, 프로젝트 설정, 관련 외부 머티리얼을 시작 시점의 해시와 대조했습니다. 원본 에셋·외부 머티리얼은 유지했습니다. ProBuilder가 이전과 같은 `ShapeComponent.ResetSettings=false` 항목을 자동 저장하여, 앞서 받은 복원 승인에 따라 이번 시작 시점의 바이트로 복원했습니다. 해당 패키지는 이후 Unity 재로드/종료 시 이 항목을 다시 생성할 수 있으며 패키지 자체 코드는 수정하지 않았습니다.
