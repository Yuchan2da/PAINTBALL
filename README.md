# 🐵 페인트 총알 FPS (가제)

투명한 원숭이들이 페인트탄으로 서로 맞추고, 맞은 부분에 팀 색깔이 칠해지는 멀티플레이 FPS입니다.  
동아리 프로젝트로 개발 중이에요.

## 개발 환경
- **엔진:** Unity 6.3.11f1 (URP)
- **네트워크:** Photon PUN 2 (예정)
- **주요 에셋:** PSPSPS Monkey (Suriyun)

## 지금까지 만든 것
- CharacterController로 1인칭 이동/점프/달리기 구현
- 마우스로 상하좌우 시점 회전 (각도 제한 포함)
- Rigidbody 기반 포물선 페인트탄 발사 (AddForce)
- 오브젝트 풀링으로 총알/데칼 재활용 (Instantiate/Destroy 제거)
- 연사 속도 제한(초당 3발), 15발 탄창, R키 재장전
- Pspsps Monkey 에셋 연동 + Animator 파라미터(Speed, isGrounded, Jump) 연결
- 1인칭 카메라에서 내 캐릭터 안 보이게 Culling Mask 처리
- 5초 정지 패널티 (실제 이동 거리 기준, 제자리 점프 꼼수 차단 + 발밑 데칼 생성)
- 머리/몸통 2단 히트박스 + HP 데미지 시스템 (Head 20, Body 10)
- 바닥 충돌 시 페인트 데칼 생성, 5초 후 자동 소멸
- 탄약/체력 HUD 실시간 표시 (부족 시 색상 경고)
- UV 텍스처 페인팅 — 총알이 맞은 지점에 팀 색깔이 직접 칠해지는 시스템
  - BakeMesh + MeshCollider로 정확한 UV 좌표 계산
  - RenderTexture에 Graphics.Blit으로 페인트 누적
  - 캐릭터마다 독립적인 페인트맵 (MaterialPropertyBlock 사용)

## 앞으로 할 것
- [ ] 사망 처리 및 리스폰 로직
- [ ] 결과 화면 UI (승패, 점수)
- [ ] 경기장 맵 제작
- [ ] Photon 멀티플레이 연동

## 개발 일지
개발 과정은 일지로 기록 중입니다.