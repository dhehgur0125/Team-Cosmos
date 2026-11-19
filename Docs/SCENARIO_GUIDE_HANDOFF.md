# 2층 시나리오 안내 시스템

참고: https://claude.ai/artifact/A4QUgc8cYmPmpkvxwicGXX (2026-11-14 설계도). 대사는 해당 흐름에 맞춰 새로 작성. 실제 진행 판정은 현행 F2MissionDirector가 기준.

- Assets/Map/Script/Stage2/Mission/F2MissionGuidance.cs: F2MissionDirector의 partial 확장. 별도 컴포넌트 추가 없이 BuildHud/Update에서 실행.
- 오른쪽 무전 채팅 패널 아래 현재 목표 상시 표시, 단계 변경 시 무전 자막 기본10초, J로 현재 대사 다시 보기. 음성 녹음/합성은 포함하지 않음.
- Inspector의 시나리오 안내 > scenarioLines에서 목표/대사 수정, dialogueSeconds로 시간 설정, showScenarioGuide로 끄기. 배열 순서 0~12는 유지.
- 단계: 카드 수색/카드 사용/절단기 확보/모듈 분리/모듈 운반/커플러 수색/커플러 연결/충전 경계/밸브 수리/공급실 복귀/A계단/클리어/실패.
- 0.2초마다 기존 State와 TeamHas를 읽음. 임무 진행이나 네트워크 판정 변경 없음. 로딩 및 입력 차단 중 숨김/대사 타이머 정지. 중간 접속은 현재 상태 안내부터 표시.
- 검증: 13개 상태 매핑 통과, NotoSansKR SDF 한글 및 텍스트 넘침 없음 확인, 2층 실제 플레이 화면 확인 및 음성 채팅 패널 겹침 수정. 2인 네트워크 실기 검증은 미실시.
- 적용 범위는 현재 2층. 타 층은 각 MissionDirector 상태에 맞는 별도 매핑 필요.

## 중복 목표 패널 제거 — 2026-11-19
- 오른쪽 Current Task 패널 제거. 목표 목록은 기존 왼쪽 상단만 사용.
- 단계별 무전 자막(기본10초)과 J 다시 보기 유지. 무전 패널을 음성 채팅 아래로 올리고, 제목에 J 안내 표시.
