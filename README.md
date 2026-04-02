# 🐵 페인트 총알 FPS (가제)

투명한 원숭이들이 페인트탄으로 서로 맞추고, 맞으면 스킨이 드러나는 멀티플레이 FPS입니다.  
동아리 프로젝트로 개발 중이에요.

## 개발 환경
- **엔진:** Unity 6.3.11f1 (URP)
- **네트워크:** Photon PUN 2 (예정)
- **주요 에셋:** PSPSPS Monkey (Suriyun)

## 지금까지 만든 것
- CharacterController로 1인칭 이동/점프/달리기 구현
- 마우스로 상하좌우 시점 회전 (각도 제한 포함)
- Rigidbody 기반 포물선 페인트탄 발사 (AddForce)
- 오브젝트 풀링으로 총알 100개 재활용 (Instantiate/Destroy 제거)
- 연사 속도 제한(초당 3발), 15발 탄창, R키 재장전
- Pspsps Monkey 에셋 연동 + Animator 파라미터(Speed, isGrounded, Jump) 연결
- 1인칭 카메라에서 내 캐릭터 안 보이게 Culling Mask 처리

## 앞으로 할 것
- [ ] 5초 정지 패널티 (페인트 누수 기믹)
- [ ] 데미지 시스템 + 피격 시 스킨 노출 셰이더
- [ ] Photon 멀티플레이 연동
- [ ] UI (체력, 탄창, 타이머)

## 개발 일지
개발 과정은 일지로 기록 중입니다.