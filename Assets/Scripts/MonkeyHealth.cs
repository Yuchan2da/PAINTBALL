using UnityEngine;
using System.Collections;
using Photon.Pun;

/// <summary>
/// 원숭이(적/플레이어) 체력 관리 + 사망 연출 + 리스폰 시스템.
///
/// [사망 흐름]
/// 1. HP ≤ 0 → HandleDeath()
/// 2. 조작 잠금 (PlayerController/PlayerShooter.inputEnabled = false)
/// 3. 사망 애니메이션 트리거 ("Die") 재생
/// 4. 카메라를 등 뒤 3인칭 숄더뷰로 전환 + LocalPlayer 레이어 컬링 켜기
///    (벽 뚫림 방지: SphereCast로 벽 충돌 체크 후 카메라 위치 보정)
/// 5. 3초 대기 (데스캠)
/// 6. Respawn() → 랜덤 위치 텔레포트 + HP/페인트/애니메이션 완전 초기화
///
/// [설계 원칙]
/// - 이 스크립트가 사망~부활의 전체 흐름을 오케스트레이션한다.
/// - PlayerController/Shooter는 inputEnabled만 읽고, 자신의 역할만 수행.
/// - 추후 Photon 적용 시 TakeDamage/Respawn을 RPC로 교체하면 됨.
/// </summary>
public class MonkeyHealth : MonoBehaviourPun
{
    [Header("체력 설정")]
    public int maxHp = 100;

    [Header("데스캠 설정")]
    [Tooltip("사망 후 부활까지 대기 시간(초)")]
    public float deathCamDuration = 3f;

    [Tooltip("데스캠 카메라 오프셋 (캐릭터 로컬 좌표 기준, 등 뒤 위)")]
    public Vector3 deathCamOffset = new Vector3(0f, 1.5f, -3f);

    [Tooltip("카메라 벽 뚫림 방지 SphereCast 반지름")]
    public float cameraCollisionRadius = 0.2f;

    // ── 상태 ──────────────────────────────────────────────────────
    public int CurrentHp { get; private set; }
    public bool IsDead => CurrentHp <= 0;

    // 마지막으로 데미지를 준 플레이어 정보 (킬 판정용)
    private string lastAttackerName;
    private bool lastWasHeadshot;

    // ── 캐시 ──────────────────────────────────────────────────────
    private PlayerController playerController;
    private PlayerShooter playerShooter;
    private PaintReceiver paintReceiver;
    private CharacterController characterController;
    private Camera mainCamera;
    private Animator animator;

    // 카메라 원래 상태 저장 (1인칭 복귀용)
    private Vector3 originalCamLocalPos;
    private Quaternion originalCamLocalRot;
    private int originalCullingMask;

    // 레이어 인덱스 캐시
    private int localPlayerLayerMask;

    // 애니메이터 파라미터 해시
    private static readonly int AnimDie = Animator.StringToHash("Die");
    private static readonly int AnimRespawn = Animator.StringToHash("Respawn");

    void Start()
    {
        CurrentHp = maxHp;

        // 컴포넌트 캐싱
        playerController = GetComponent<PlayerController>();
        playerShooter = GetComponent<PlayerShooter>();
        paintReceiver = GetComponent<PaintReceiver>();
        characterController = GetComponent<CharacterController>();

        // 카메라 캐싱
        if (playerController != null && playerController.cameraTransform != null)
        {
            mainCamera = playerController.cameraTransform.GetComponent<Camera>();
            originalCamLocalPos = playerController.cameraTransform.localPosition;
            originalCamLocalRot = playerController.cameraTransform.localRotation;
            if (mainCamera != null)
                originalCullingMask = mainCamera.cullingMask;
        }

        // 애니메이터 캐싱
        // Player: PlayerController.animator 사용
        // DummyEnemy 등: PlayerController가 없으면 자식에서 Animator를 직접 검색
        if (playerController != null && playerController.animator != null)
            animator = playerController.animator;
        else
            animator = GetComponentInChildren<Animator>();

        // LocalPlayer 레이어 마스크 캐시
        int localPlayerLayer = LayerMask.NameToLayer("LocalPlayer");
        localPlayerLayerMask = (localPlayerLayer >= 0) ? (1 << localPlayerLayer) : 0;
    }

    // [테스트 코드 제거됨] T/Y키 데미지 시뮬레이션 — 멀티플레이 전환으로 삭제.

    /// <summary>
    /// 외부에서 호출하는 유일한 데미지 진입점.
    /// killerName: 데미지를 준 플레이어 이름 (킬 판정용)
    /// isHeadshot: 헤드샷 여부 (킬 피드 표시용)
    /// </summary>
    public void TakeDamage(int damage, string killerName = "", bool isHeadshot = false)
    {
        if (IsDead) return; // 이미 죽은 대상 중복 처리 방지

        CurrentHp -= damage;
        CurrentHp = Mathf.Max(CurrentHp, 0); // 음수 방지

        // 마지막 공격자 정보 캐싱 (킬 판정에 사용)
        if (!string.IsNullOrEmpty(killerName))
        {
            lastAttackerName = killerName;
            lastWasHeadshot = isHeadshot;
        }

        Debug.Log($"[피격] {gameObject.name} | 데미지: {damage} | 남은 HP: {CurrentHp}/{maxHp}");

        if (IsDead)
        {
            HandleDeath();
        }
    }

    // ── 네트워크 데미지 ───────────────────────────────────────────

    /// <summary>
    /// 네트워크 환경에서의 데미지 진입점.
    /// 사격자(A)가 호출 → RPC로 피격자(B)의 Owner에게 전달.
    /// [왜 Owner에게만?] HP는 Owner가 관리해야 일관성 보장.
    /// 여러 클라이언트가 동시에 HP를 깎으면 값이 꼬인다.
    /// </summary>
    public void TakeDamageNetwork(int damage, string killerName, bool isHeadshot)
    {
        if (photonView != null && PhotonNetwork.IsConnected)
            photonView.RPC(nameof(RPC_TakeDamage), photonView.Owner, damage, killerName, isHeadshot);
        else
            TakeDamage(damage, killerName, isHeadshot);
    }

    [PunRPC]
    void RPC_TakeDamage(int damage, string killerName, bool isHeadshot)
    {
        TakeDamage(damage, killerName, isHeadshot);
    }

    // ── 사망 처리 ─────────────────────────────────────────────────

    /// <summary>
    /// 사망 연출 시작. 코루틴으로 3초 데스캠 후 리스폰.
    /// </summary>
    void HandleDeath()
    {
        Debug.Log($"[처치] {gameObject.name} 사망! (by {lastAttackerName})");

        // ScoreManager에 킬/데스 기록
        // [네트워크] 연결 시 전 클라이언트에 RPC 브로드캐스트
        if (ScoreManager.Instance != null && !string.IsNullOrEmpty(lastAttackerName))
        {
            if (PhotonNetwork.IsConnected)
                ScoreManager.Instance.RecordKillNetwork(lastAttackerName, gameObject.name, lastWasHeadshot);
            else
                ScoreManager.Instance.RecordKill(lastAttackerName, gameObject.name, lastWasHeadshot);
        }

        // 네트워크: 원격 클라이언트에도 사망 연출 전송
        Debug.Log($"[HandleDeath] RPC조건: IsConnected={PhotonNetwork.IsConnected}, photonView={photonView != null}, IsMine={photonView?.IsMine}");
        if (PhotonNetwork.IsConnected && photonView != null && photonView.IsMine)
        {
            Debug.Log($"[HandleDeath] RPC_RemoteDie 전송! viewID={photonView.ViewID}");
            photonView.RPC(nameof(RPC_RemoteDie), RpcTarget.Others);
        }

        StartCoroutine(DeathRoutine());
    }

    /// <summary>
    /// 원격 클라이언트에서 수신: 사망 연출 (투명화 해제 + 넘어지는 애니메이션).
    /// 데스캠/조작잠금은 로컬에서만 필요하므로 여기서는 시각적 요소만 처리.
    /// </summary>
    [PunRPC]
    void RPC_RemoteDie()
    {
        Debug.Log($"[RPC_RemoteDie] {gameObject.name} 원격 사망 연출 수신! paintReceiver={paintReceiver != null}, animator={animator != null}");

        // animator가 아직 캐싱 안 되었을 수 있음 (Start() 순서 문제)
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        // paintReceiver가 캐싱 안 되었을 수 있음
        if (paintReceiver == null)
            paintReceiver = GetComponent<PaintReceiver>();

        // 1) 정체 노출 (100% 불투명) — 원격에서 캐릭터가 보여야 사망 연출이 보임
        if (paintReceiver != null)
        {
            paintReceiver.SetReveal(1f);
            Debug.Log($"[RPC_RemoteDie] {gameObject.name}: SetReveal(1) 호출 완료");
        }
        else
        {
            // PaintReceiver가 없어도 SkinnedMeshRenderer를 직접 불투명으로 만들기
            Debug.LogWarning($"[RPC_RemoteDie] {gameObject.name}: paintReceiver가 null! 직접 renderer 처리 시도");
            var smr = GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null)
            {
                var block = new MaterialPropertyBlock();
                smr.GetPropertyBlock(block);
                block.SetFloat("_RevealAmount", 1f);
                smr.SetPropertyBlock(block);
            }
        }

        // 2) 사망 애니메이션 — CrossFade로 직접 상태 전환 (Trigger보다 안정적)
        if (animator != null)
        {
            // CrossFade는 상태 이름 해시로 직접 전환 → Trigger 타이밍 문제 없음
            animator.Play("Die", 0, 0f);
            Debug.Log($"[RPC_RemoteDie] {gameObject.name}: animator.Play('Die') 호출 완료");
        }
        else
            Debug.LogWarning($"[RPC_RemoteDie] {gameObject.name}: animator가 null!");

        // 3) HP 0으로 설정 (IsDead 판정용 — 트레일/발자국 생성 중지)
        CurrentHp = 0;
    }

    IEnumerator DeathRoutine()
    {
        // === 1단계: 조작 잠금 ===
        SetInputEnabled(false);

        // === 2단계: 정체 노출! 투명화 완전 해제 ===
        // 죽는 순간 스텔스가 풀리면서 원래 캐릭터 스킨이 100% 보인다
        if (paintReceiver != null)
            paintReceiver.SetReveal(1f);

        // === 3단계: 사망 애니메이션 트리거 ===
        if (animator != null)
            animator.SetTrigger(AnimDie);

        // === 4단계: 3인칭 데스캠 전환 ===
        SwitchToDeathCam();

        // === 5단계: 3초 대기 (데스캠 감상) ===
        yield return new WaitForSeconds(deathCamDuration);

        // === 6단계: 리스폰 ===
        Respawn();
    }

    // ── 데스캠 카메라 ─────────────────────────────────────────────

    /// <summary>
    /// 카메라를 등 뒤 3인칭 숄더뷰로 이동시킨다.
    /// [벽 뚫림 방지] 캐릭터 머리에서 목표 위치로 SphereCast를 쏴서
    /// 벽에 닿으면 충돌 지점 바로 앞까지만 카메라를 당긴다.
    /// </summary>
    void SwitchToDeathCam()
    {
        if (mainCamera == null) return;

        Transform camTransform = mainCamera.transform;

        // 1) LocalPlayer 레이어를 컬링 마스크에 추가 (내 몸이 보이게)
        mainCamera.cullingMask |= localPlayerLayerMask;

        // 2) 월드 기준 카메라 목표 위치 계산
        Vector3 headWorldPos = camTransform.position; // 현재 1인칭 카메라 위치 ≈ 머리
        Vector3 targetWorldPos = transform.TransformPoint(deathCamOffset);

        // 3) 벽 뚫림 방지: 머리 → 목표 방향으로 SphereCast
        Vector3 direction = targetWorldPos - headWorldPos;
        float maxDist = direction.magnitude;
        RaycastHit hit;

        // Default(0)와 Floor(6) 레이어에 대해 벽 충돌 체크
        int wallMask = (1 << 0) | (1 << LayerMask.NameToLayer("Floor"));

        if (Physics.SphereCast(headWorldPos, cameraCollisionRadius, direction.normalized,
            out hit, maxDist, wallMask))
        {
            // 벽에 닿았으면, 충돌 지점에서 살짝 앞으로 당김
            float safeDist = Mathf.Max(hit.distance - cameraCollisionRadius, 0.3f);
            targetWorldPos = headWorldPos + direction.normalized * safeDist;
        }

        // 4) 카메라 이동 & 캐릭터를 바라보게 회전
        camTransform.position = targetWorldPos;
        camTransform.LookAt(headWorldPos);
    }

    /// <summary>
    /// 카메라를 원래 1인칭 위치/회전으로 복원한다.
    /// </summary>
    void RestoreFirstPersonCam()
    {
        if (mainCamera == null) return;

        Transform camTransform = mainCamera.transform;

        // 위치/회전 복원
        camTransform.localPosition = originalCamLocalPos;
        camTransform.localRotation = originalCamLocalRot;

        // LocalPlayer 레이어를 컬링 마스크에서 제거 (내 몸 다시 숨기기)
        mainCamera.cullingMask &= ~localPlayerLayerMask;
    }

    // ── 리스폰 ────────────────────────────────────────────────────

    /// <summary>
    /// 완전한 리스폰 처리.
    /// [순서가 중요한 이유]
    /// 1. 카메라를 먼저 1인칭으로 복원해야 텔레포트 후 시점이 정상.
    /// 2. CC를 끄고 이동한 뒤 켜야 물리 캐싱 텔레포트 버그 방지.
    /// 3. 페인트/애니메이션을 초기화한 뒤 조작을 풀어야 깨끗한 상태로 시작.
    /// </summary>
    void Respawn()
    {
        // [네트워크] 로컬 플레이어만 리스폰 처리
        // 원격 플레이어의 위치는 PhotonTransformView로 자동 동기화
        if (PhotonNetwork.IsConnected && photonView != null && !photonView.IsMine)
            return;

        // 게임 오버 상태면 리스폰하지 않음
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            return;

        Debug.Log($"[리스폰] {gameObject.name} 부활!");

        // 1) 카메라 1인칭 복원
        RestoreFirstPersonCam();

        // 2) CC 텔레포트 (enabled 토글로 물리 캐싱 버그 방지)
        Vector3 spawnPos = Vector3.zero;
        if (SpawnManager.Instance != null)
            spawnPos = SpawnManager.Instance.GetRandomSpawnPoint();
        else
            spawnPos = new Vector3(0f, 1.5f, 0f); // 안전 폴백

        if (characterController != null)
        {
            characterController.enabled = false;
            transform.position = spawnPos;
            characterController.enabled = true;
        }
        else
        {
            transform.position = spawnPos;
        }

        // 3) HP 복구
        CurrentHp = maxHp;

        // 4) 페인트 완전 초기화 (깨끗한 몸으로 부활)
        if (paintReceiver != null)
        {
            paintReceiver.ClearPaintMap();
            paintReceiver.ClearBodyDecals();
        }

        // 5) 애니메이션 리셋 (Respawn 트리거로 Idle 복귀)
        if (animator != null)
        {
            animator.ResetTrigger(AnimDie);
            animator.SetTrigger(AnimRespawn);
        }

        // 6) 조작 활성화 (마지막에 풀어야 안전)
        SetInputEnabled(true);

        // 7) 네트워크: 원격 클라이언트에도 리스폰 연출 전송
        if (PhotonNetwork.IsConnected && photonView != null && photonView.IsMine)
            photonView.RPC(nameof(RPC_RemoteRespawn), RpcTarget.Others);
    }

    /// <summary>
    /// 원격 클라이언트에서 수신: 리스폰 연출 (투명화 복원 + 애니메이션 리셋).
    /// </summary>
    [PunRPC]
    void RPC_RemoteRespawn()
    {
        Debug.Log($"[RPC_RemoteRespawn] {gameObject.name} 원격 리스폰 연출 수신!");

        // fallback
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (paintReceiver == null)
            paintReceiver = GetComponent<PaintReceiver>();

        // HP 복구 (IsDead 해제)
        CurrentHp = maxHp;

        // 페인트 초기화 (깨끗한 몸 + 스텔스 복원)
        if (paintReceiver != null)
        {
            paintReceiver.ClearPaintMap();
            paintReceiver.ClearBodyDecals();
        }

        // 애니메이션 리셋 — 직접 Blend Tree(Idle)로 전환
        if (animator != null)
        {
            animator.ResetTrigger(AnimDie);
            animator.Play("Blend Tree", 0, 0f);
        }
    }

    // ── 유틸리티 ──────────────────────────────────────────────────

    /// <summary>
    /// PlayerController와 PlayerShooter의 입력을 동시에 토글한다.
    /// </summary>
    void SetInputEnabled(bool enabled)
    {
        if (playerController != null) playerController.inputEnabled = enabled;
        if (playerShooter != null) playerShooter.inputEnabled = enabled;
    }
}
