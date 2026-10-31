# VOID LAST — 권총 실탄 사격 연출

대상은 기존 `Prefabs/Player/VOID_LAST_FPS.prefab`과 `Character_test.unity`입니다. 원본 권총·우주비행사, 기존 손 애니메이션과 Animator, 이동·아이템 코드, ProjectSettings/Packages는 수정하지 않습니다.

## 적용 내용

- `FpsPistol`에 성공한 발사를 알리는 `Shot` 이벤트만 추가했습니다. 기존 Raycast, 탄약, 재장전, 명중 이벤트와 탄흔 처리를 재사용합니다. 발사 실패에는 화염·반동이 발생하지 않습니다.
- `FpsPistolEffects`는 카메라용/외부 권총용 `MuzzleFlash` 파티클을 한 번씩 재생합니다. 원본 권총 앞면의 중심을 메시에서 측정했습니다: 약 `(0, 0.0214, 0.1616)`. 화염 중심은 총구 앞 약 2.8cm인 `(0, 0.0214, 0.19)`입니다. 1인칭에서 총에 완전히 가려지지 않도록 실제 렌더를 비교했습니다.
- 기존 `Muzzle_Flash_B.png`의 4×4 화염 시트와 `White_DotGlow.png`를 재사용합니다. 위치는 `Assets/Creepy_Cat/3D Scifi Kit Vol 4/Textures/Particles/`이며 원본은 변경하지 않습니다. Player 안에 작은 주황색 화염·불꽃용 머티리얼만 생성했습니다. 에너지 무기 프리팹이나 맵의 동기화 스파크 스크립트는 가져오지 않았습니다.
- 총구 화염은 기본 0.045초입니다. 외부 총구에는 그림자 없는 약한 Point Light를 하나만 둡니다. 기존 `FpsLocalVisibility` 목록에 두 파티클 Renderer를 등록하여 로컬 카메라는 FPS 화염, 다른 카메라는 외부 권총 화염을 봅니다.
- `FpsWeaponView`가 기존 장착·조준·재장전 기준 자세에 반동 오프셋을 합성합니다. 매 발사마다 반동을 1로 재설정하고 지수적으로 0까지 복귀하므로 연속 사격해도 Transform 위치가 누적되지 않습니다. ADS 반동은 기본 65%입니다. 카메라 반동과 손 애니메이션 수정은 없습니다.
- 벽 명중 시 실제 법선 방향으로 작은 불꽃 7개를 흩뿌립니다. 수명은 0.08~0.18초이며 마지막 입자가 사라지면 ParticleSystem이 오브젝트를 제거합니다. Collider·파티클 충돌·트레일이 없어 Raycast를 막지 않습니다. `FpsHealth`나 Animator가 있는 캐릭터에는 환경 이펙트를 생성하지 않습니다.
- 재장전 시작·슬롯 전환·사망 시 총구 화염과 반동을 정리합니다. 벽 명중 효과는 자신의 짧은 수명을 마칩니다. 탄흔 관리 코드는 변경하지 않았으며 월드 전체 7개·30초 규칙을 유지합니다.
- Projectile, LineRenderer, 예광탄, 레이저를 만들지 않습니다. 기존 FPS 프리팹에 AudioSource가 없어서 새 총성은 추가하지 않았습니다.

## Inspector 조절값

FPS 프리팹 루트에서 조절합니다.

| 컴포넌트 / 항목 | 기본값 | 의미 |
| --- | --- | --- |
| FpsPistolEffects / Flash Size | 0.16 | 화염 입자 크기. 권총 Transform의 배율도 적용 |
| Flash Duration | 0.045초 | 화염과 섬광 시간 |
| Light Intensity / Range | 0.12 / 1m | 약한 조명. Intensity 0이면 끔 |
| Impact Scale | 1 | 환경 명중 효과 전체 배율 |
| FpsWeaponView / Recoil Position | `(0, 0.008, -0.025)` | 위쪽·뒤쪽 이동 |
| Recoil Euler | `(-3.5, 0, 0)`도 | 총구가 위로 들리는 회전 |
| Recoil Return Speed | 22 | 클수록 빠른 복귀 |
| Aimed Recoil Multiplier | 0.65 | 조준 시 반동 비율 |

총구 부착점은 `CameraPivot/FPS Camera/WeaponRoot/MuzzleFlash`와 오른손 `WorldWeaponSocket/MuzzleFlash`의 로컬 Transform입니다. 원본 권총의 Button·Clip·Trigger는 유지했습니다. 환경 불꽃의 개수·속도·수명은 `Prefabs/Effects/VOID_LAST_PistolImpact.prefab`의 ParticleSystem에서 조절합니다. 탄흔 프리팹과는 별개입니다.

## 생성·수정 파일

모두 `Assets/Player/` 기준이며 새 에셋의 `.meta`를 포함합니다.

| 구분 | 파일 |
| --- | --- |
| 신규 런타임 | `Scripts/FpsPistolEffects.cs` |
| 수정 런타임 | `Scripts/FpsPistol.cs`, `Scripts/FpsWeaponView.cs` |
| 신규 설정 도구 | `Scripts/Editor/FpsShootingEffectsSetup.cs` |
| 수정 검증·연결 도구 | `Scripts/Editor/FpsCombatValidation.cs`, `Scripts/Editor/Invoke-UnityMcp.ps1` |
| 수정 FPS 프리팹 | `Prefabs/Player/VOID_LAST_FPS.prefab` |
| 신규 효과 | `Prefabs/Effects/VOID_LAST_PistolImpact.prefab` |
| 신규 머티리얼 | `Materials/Effects/PistolMuzzleFlash.mat`, `Materials/Effects/PistolImpact.mat` |
| 문서·검증 | 이 문서, `COMBAT_README.md`, `Validation/PlayModeResults.txt`, `Validation/ShotEffectsAudit.txt`, `Validation/Shot_*.png` 및 기존 회귀 검증 캡처 갱신 |

기존 씬의 FPS 프리팹 인스턴스가 변경을 상속하므로 `Character_test.unity` 자체와 맵을 다시 저장하지 않습니다. Unity 메뉴 `VOID LAST > Player > 9 - Add Pistol Shot Effects`가 기존 프리팹에 효과 참조만 연결합니다. 기존 손 설정 도구는 실행하지 않습니다.

## 검증 방법과 범위

`VOID LAST > Player > 3 - Run Play Mode Validation`은 실제 Character_test의 물리·Animator·렌더링에서 검사를 수행합니다. 동작 API로 입력을 전달하며 물리 마우스를 직접 누른 검사는 아닙니다. 결과는 최신 `Validation/PlayModeResults.txt`에서 확인합니다.

추가 검사: 성공/실패 발사 효과, 일반·ADS 반동과 기준 자세 복귀, 연속 발사 위치 비누적, 총구 부착점, 약한 조명, 환경 명중 위치·방향·짧은 수명, 캐릭터 명중 제외, Collider/LineRenderer/TrailRenderer 부재, 재장전/슬롯 전환 정리입니다. 기존 탄약 수량·탄흔 전역 7개/실제 30초 수명과 이동·점프·웅크리기·아이템 회귀 검사도 함께 실행합니다.

실제 마우스 클릭 체감, 어두운 맵의 조명 강도, 여러 FOV·해상도에서의 화염 크기는 직접 플레이로 최종 조절할 수 있습니다. 네트워크와 몬스터 피격 연출은 이번 범위가 아닙니다.

2026-10-31 16:19~16:20 KST 최종 실제 Play Mode 실행은 **전체 자동 검사 통과**입니다. 총구 입자와 약한 조명, 일반/ADS 반동 복귀, 반복 발사의 비누적, 빗나감/캐릭터 명중의 환경 효과 제외, 재장전/슬롯 전환 정리, 환경 불꽃 자동 삭제, 탄흔의 전역 7개 및 실제 Play 시간 30초 만료를 확인했습니다. 기존 이동·하체 점프·웅크리기·권총/아이템 슬롯·홀딩 회복·획득/드랍·HUD 검사도 통과했습니다.

최종 Console Error/Exception 조회는 0개입니다. `Shot_Hip_FPS.png`, `Shot_ADS_FPS.png`, `Shot_Observer.png`, `Shot_Impact_Closeup.png`를 직접 확인했습니다. 1인칭·외부 화염과 기존 탄흔 주변의 작은 불꽃이 보이며 발광 궤적은 없습니다. 원본 모델·손 코드·Animator·씬·설정·패키지 보호 파일 78개의 해시는 작업 시작 시점과 일치합니다. 누락 참조와 머티리얼 검사는 `Validation/ShotEffectsAudit.txt`에 기록했습니다.
