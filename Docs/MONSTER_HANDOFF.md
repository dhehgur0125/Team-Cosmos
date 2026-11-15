# 몬스터 담당자 인수인계
작성일: 2026-11-15  
기준: Team-Cosmos / Unity URP / NavMesh / Netcode for GameObjects  
읽는 순서: 이 문서 → `WORK_SUMMARY_2026-11-15.md` → `INTEGRATION_HANDOFF.md`

이 문서의 “최종 크기”가 누적 문서의 초기 1m·1.6m 설정을 대체합니다. 에디터에서 다른 사람이 추가로 수정한 값은 실제 프리팹/씬을 우선 확인하세요.

## 바로 테스트하기
1. `Assets/Map/Scenes/Floors/Stage2_Ship_2f.unity`를 엽니다. 오프라인으로 Play하거나 기존 Start → 로비 → 게임 흐름을 사용합니다.
2. 첫 시작 위치에서 약 6m 앞의 `TEST_SmallMonster_NearStart`를 찾습니다. Scene 검색창에 이름을 입력하고 F로 프레임할 수도 있습니다.
3. 총을 장착하고 몬스터를 맞혀 혈흔과 HP 감소를 확인합니다. R 재장전 등 기존 입력은 유지합니다.
4. 가까이 접근해 비명 → 추격 → 공격을 관찰하고, 플레이어 피해 시 화면 가장자리 혈흔을 확인합니다.
5. 눈없는애는 기존 배치 개체로 검사합니다. 스캐너를 손에 들고 좌클릭해 소음 접근 → 더듬기 → 공격/배회 복귀를 확인합니다.
6. 테스트 쪼꼬미는 체력이 40인 현재 프리팹을 사용합니다. 반복 실험은 Play를 다시 시작하세요. 이번에 리스폰 UI는 추가하지 않았습니다.

테스트 개체 좌표는 `(-12, 8.067, -28.5)`, Y 회전 270도입니다. 시작점과 NavMesh 경로 연결 및 배치 공간 충돌 검사는 통과했습니다. 실제 첫 조우 전투를 새 배치 상태로 끝까지 검증한 것은 아닙니다. 비명으로 다른 쪼꼬미를 부르는 기존 AI가 유지되므로 테스트가 주변 몬스터에 영향을 줄 수 있습니다.

## 파일과 책임
| 경로 | 책임 |
|---|---|
| Assets/Monster/Prefabs/VOID_LAST_SmallMonster.prefab | 쪼꼬미 공용 프리팹 |
| Assets/Monster/Prefabs/VOID_LAST_BlindMonster.prefab | 눈없는애 공용 프리팹 |
| Assets/Monster/Prefabs/VOID_LAST_EyedLargeMonster.prefab | 기존 대형 몬스터; 이번에는 피격/머티리얼만 연결 |
| Assets/Monster/Scripts/SmallMonsterAI.cs | 기존 수색·비명·추격·공격 판단 |
| Assets/Monster/Scripts/BlindMonsterAI.cs | 기존 소음 반응·근접 감지·더듬기·촉수 공격 |
| Assets/Monster/Scripts/MonsterAssetAnimation.cs | 기존 AI 상태를 읽는 애니메이션 전용 연결 |
| Assets/Monster/Scripts/MonsterHealth.cs | HP, AI 피격 이벤트, 실제 피격 지점 이벤트 |
| Assets/Monster/Scripts/MonsterHitFeedback.cs | 혈흔 풀 생성·재생·정리 |
| Assets/Monster/Animations/ | 컨트롤러 2개 및 원본에서 복사한 클립 |
| Assets/Monster/Effects/ | URP 혈흔 프리팹 및 머티리얼 |
| Assets/Monster/Materials/*_Readable.mat | 몬스터 가시성 보정 머티리얼 |
| Assets/Map/Script/Multi/Game/MonsterNetworkSync.cs | 기존 상태/HP 동기화 + 서버 확정 피격 RPC |
| Assets/Player/Scripts/FpsHealth.cs | 실제 피해 시 DamageTaken 추가 |
| Assets/Player/Scripts/FpsDamageFeedback.cs | 본인 화면의 피격 표시 |
| Assets/Player/Prefabs/Effects/VOID_LAST_DamageOverlay.prefab | 구매 UI에서 Animator를 제거한 복사본 |
| Assets/Player/Prefabs/Effects/VOID_LAST_DamageScreen.mat | 화면 가장자리 혈흔 색상 설정 |
| Assets/Map/Script/Multi/Lobby/LobbyManager.cs | 별도 작업: 고유 아이템 선택의 비동기 저장 개선 |
| Assets/Map/Scenes/Floors/Stage2_Ship_2f.unity | 시작점 테스트 몬스터 인스턴스 추가 |

## 모델과 크기
원본 폴더: `Assets/Monsters Full Pack Vol 1/Monsters Pack Vol 2/`

| 구분 | 원본 프리팹 | 최종 Model scale | 대기 높이 | 기존 판정 |
|---|---|---:|---:|---|
| 쪼꼬미 | Muscomorph/Prefabs/Muscomorph.prefab | 0.526141 | 약 0.85m | 반경 0.3m, 높이 1m |
| 눈없는애 | Skorpmare/Prefabs/Skorpmare.prefab | 0.398831 | 약 2m | 반경 0.5m, 높이 1.6m |

두 모델은 Generic 리그입니다. 높이는 대기 클립을 샘플링한 실제 메시 정점을 기준으로 하며, 촉수/날개가 움직일 때 경계는 달라집니다.
루트 스케일은 유지하고 `Model` 자식만 조절했습니다. 바닥 여유는 약 1.5cm입니다.
기존 `Visual` 자식은 비활성화했습니다. 일부 기존 AI의 임시 색상/촉수 연출 참조는 여전히 그 숨겨진 자식을 가리킵니다. 새 모델의 뼈를 여기에 연결하면 Animator와 충돌할 수 있습니다.

프리팹 GUID:
- Small: `9818156bb889bbb40af4f401070e72fb`
- Blind: `4354e8be4055a794f9c8c9fa5bf91155`

공용 프리팹 변경은 다른 층/테스트 씬에도 반영됩니다. 특정 시험값은 씬 인스턴스 override로 제한하세요.

## 상태 → 애니메이션
| 몬스터 | AI 상태 | 재생 |
|---|---|---|
| Small | Search, Chase, Investigate, Hunt | Speed로 IdleBreathe ↔ CrawlForward, 추격은 빠른 CrawlForward |
| Small | Scream | Roar, 기존 비명 시간 1.1초에 맞춤 |
| Small | Attack | BiteAttack, 기존 공격 주기 1.3초에 맞춤 |
| Small | Dead / Stunned | Death / GetHit1 |
| Blind | Wander, Approach | Speed로 IdleBreathe ↔ Walk ↔ Run |
| Blind | Grope | TentaclesComboAttack, 0.65배속 반복 — 전용 더듬기 대체 |
| Blind | Strike | SimpleTentaclesAttack, 기존 공격 주기 1.45초에 맞춤 |
| Blind | Dead / Stunned | Death / GetHitFront |

- 컨트롤러: `VOID_LAST_SmallMonster.controller`, `VOID_LAST_BlindMonster.controller`
- 파라미터: `Speed`(float), `State`(int)
- 상태 이름: `Locomotion`, `Scream`, `Attack`, `Grope`, `Dead`, `Stunned`
- MonsterAssetAnimation이 StateChanged를 구독하고 CrossFade를 호출합니다. 컨트롤러의 트리거 전환 방식이 아닙니다.
- Root Motion은 꺼져 있습니다. 서버/오프라인 이동 속도는 Agent에서, 클라이언트 복제본은 Transform 이동량에서 읽습니다.
- 구매 클립은 수정하지 않고 복사본의 루프를 조정했습니다. 액션/사망은 비반복, 이동과 더듬기는 반복입니다.
- 총 공격 주기를 맞춘 것과 실제 타격 프레임 정렬은 다릅니다. 후자는 후속 검증이 필요합니다.
- 새 AI 종류에는 현재 연결기를 그대로 붙이는 것만으로 충분하지 않습니다. 새 enum → 상태 매핑과 서버/복제본 처리도 추가해야 합니다.

## 피격 이벤트와 네트워크
몬스터:
`FpsPistol → IFpsDamageTarget.TakeHit → MonsterHealth → HitConfirmed → MonsterHitFeedback`

기존 `Damaged(attacker, position)`의 position은 AI에 알리는 공격자 위치입니다. 혈흔 위치로 사용하지 마세요.
추가한 `HitConfirmed(hitPoint, outward)`가 실제 피격 지점이며, 치명타에서도 Died보다 먼저 발생합니다.

멀티플레이:
1. 복제본 피격은 기존 NetworkHitRedirect를 통해 서버로 전달됩니다.
2. 서버가 HP를 깎고 로컬 혈흔을 재생합니다.
3. 서버의 HitConfirmed 구독자가 BloodHitRpc를 비서버 클라이언트에 보냅니다.
4. 클라이언트는 시각 효과만 재생합니다. RPC에서 다시 TakeHit를 호출하면 안 됩니다.

혈흔은 각 몬스터당 3개를 미리 생성합니다. 1.5초 후 숨기고, 비활성화/파괴 시 정리합니다.
혈흔 프리팹에는 Collider, NetworkObject 또는 구매 에셋 데모 스크립트를 추가하지 마세요.
서버 확정 방식이라 클라이언트에서는 네트워크 지연 후 혈흔이 표시될 수 있습니다. 예측 연출은 아직 없습니다.

플레이어:
`FpsHealth.TakeDamage → DamageTaken → FpsDamageFeedback`
회복·체력 복원·단순 복제 HP 반영은 DamageTaken을 호출하지 않습니다.
네트워크 소유자 여부와 활성 카메라를 확인하고, 로딩 중에는 UI를 숨깁니다.
최대 피격 강도 0.8, fadeSeconds 기본 1.25, 체력 25% 미만 잔여 효과 최대 0.2입니다. 연속 피격은 합쳐지며 별도 화면 흔들림은 없습니다.
Canvas 정렬 순서는 90, raycastTarget은 false입니다.

## 어둠 속 가시성
- 공용 구매 머티리얼 대신 `*_Readable.mat` 복사본을 사용합니다.
- URP Lit의 기본 텍스처를 EmissionMap에도 사용하고 색상을 (0.12, 0.13, 0.15)로 설정했습니다.
- 기술적으로 낮은 emission을 사용해 표면 최소 밝기를 보장합니다. 주변을 비추는 실시간 Light나 발광 테두리 효과는 없습니다.
- URP가 emission 키워드를 유지하려면 현재 BakedEmissive 플래그가 필요합니다. None으로 바꾸면 에디터 검증이 키워드를 꺼 버립니다.
- 현재 움직이는 비정적 모델이며 라이트 베이크를 실행하지 않았습니다. 후속으로 static/GI 설정을 바꿀 경우 다시 확인하세요.
- 표준 깊이 검사와 조명을 유지하므로 벽 너머로 보이지 않습니다.
- 밝기 조정은 이 복사본에서 수행하고, 여러 씬과 실제 후처리/Bloom 환경에서 확인하세요.

## 새 몬스터 추가 시 체크리스트
1. 기존 프리팹을 새 경로로 복제하여 새 GUID를 받습니다. 기존 2종을 삭제·재생성하지 않습니다.
2. 루트 AI/HP/Agent/Collider/네트워크 컴포넌트를 먼저 확인합니다. 같은 역할의 컴포넌트를 중복으로 붙이지 않습니다.
3. 구매 원본은 보존하고 새 모델을 Model 아래 넣습니다. 불필요한 Collider/Rigidbody/데모 동작을 제거합니다.
4. 대기와 공격 자세에서 실제 메시 크기·바닥 높이·문 통과를 확인합니다. 모델과 충돌체 크기가 달라지면 판정 의도를 기록합니다.
5. 실제 제공 클립만 매핑하고, 없으면 대체 클립임을 명시합니다. Root Motion과 Agent가 동시에 이동시키지 않게 합니다.
6. 새 상태가 있으면 MonsterAssetAnimation과 MonsterNetworkSync의 상태 매핑을 함께 확장하거나 별도 연결기를 만듭니다. 기존 enum 번호 변경은 네트워크 호환성에 영향을 줍니다.
7. 혈흔을 쓸 경우 MonsterHitFeedback의 bloodPrefab을 연결합니다. 단순 HP 변경이 아니라 정상 TakeHit 경로로 테스트합니다.
8. NetworkObject가 필요한 씬 배치와 런타임 스폰을 구분합니다. 공용 몬스터 프리팹만 인스턴스화했을 때 씬에서 추가된 네트워크 override가 자동으로 따라오는지 확인하세요.
9. 호스트와 클라이언트 각각 공격, 사망, 상태 애니메이션, 혈흔, 플레이어 피해 UI를 확인합니다.
10. .meta를 포함해 공유하고, 변경 파일·검증 결과·남은 문제를 이 문서나 후속 문서에 추가합니다.

## 검증 범위와 남은 작업
확인 완료:
- 기존 2층 몬스터의 NavMesh 이동과 상태별 클립 재생.
- 런타임 일반/치명타 혈흔, 죽은 대상 추가 피격 무시.
- 플레이어 피해 시 UI 활성, 회복 시 피격 재발동 없음.
- 별도 광원 없는 모델과 혈흔/UI의 이미지 렌더 검사.
- 로비 비동기 저장의 빠른 연속 선택·실패 복원·이전 방 응답 무시 모의 검사.
- 새 시작점 배치의 NavMesh 경로/공간 및 저장 후 재확인.

우선 후속 검증:
- 신규 테스트 배치에서 실제 첫 조우부터 사망까지 플레이.
- 두 클라이언트 혈흔/상태 동기화, 지연 및 중복 여부.
- 공격 애니메이션과 실제 피해 프레임 정렬.
- 눈없는애 스캔 반응의 전체 흐름 및 더듬기 대체 클립 적합성.
- 외형 크기와 충돌체 차이, 모든 출입문 통과.
- UI가 부활/관전/층 이동에서 남거나 다른 대원 화면에 표시되지 않는지.
- 실제 서비스에서 고유 아이템 동시 선택 충돌/연결 끊김 처리.

## 변경 공유와 주의
이번 대화에서 구매 에셋 원본, Packages, ProjectSettings는 변경하지 않았고 커밋/푸시도 하지 않았습니다.
Git status/diff가 환경 오류로 실패해 전체 작업 트리 변경을 대조하지 못했습니다. 팀원이 정상 Git 환경에서 다른 담당자의 변경과 함께 확인하세요.
`Docs/INTEGRATION_HANDOFF.md`의 이전 내용을 삭제하지 말고 새 기록을 이어 붙이세요.
씬의 테스트 개체 제거는 `TEST_SmallMonster_NearStart`만 대상으로 하며, 공용 프리팹은 보존하세요.

## 시각 검증 자료
아래는 분리된 미리보기 렌더이며 실제 복도 플레이 화면은 아닙니다.

![어두운 배경에서의 표면 가시성](IntegrationReports/MonsterHandoff/MonsterDarkVisibility.png)
![몬스터 혈흔](IntegrationReports/MonsterHandoff/MonsterBloodPreview.png)
![플레이어 피격 UI](IntegrationReports/MonsterHandoff/PlayerDamagePreview.png)

## 혈흔 가시성 보강 — 2026-11-15
- 모든 몬스터 피격 effectScale 0.28 → 0.48(약 1.7배), 파티클 수명 0.5 → 0.7초.
- URP 혈흔 BaseColor를 (1.6, 0.85, 0.85, 1)로 올려 붉은색 대비/불투명도를 강화. 가산 블렌딩이나 광원은 추가하지 않음.
- 실제 피격점에서 outward 방향으로 0.08m 이동해 외형에 가려지는 현상을 줄임. 풀 3개/정리 1.5초 유지.
- 프리팹 수정으로 시작점 테스트 개체에도 상속됨. 컴파일/콘솔 오류 0. 어두운 배경 렌더로 혈흔 가시성 확인.
