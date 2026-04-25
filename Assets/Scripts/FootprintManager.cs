using UnityEngine;

/// <summary>
/// 발자국 스텔스 시스템.
/// 플레이어가 바닥의 페인트 데칼(PaintTrigger 레이어) 위를 걸으면
/// 형광 발자국이 남아 이동 궤적이 노출된다.
///
/// [감지 방식: CheckSphere (Raycast 금지)]
/// 바닥 데칼에 얇은 BoxCollider(isTrigger)를 달고 PaintTrigger 레이어로 설정.
/// 발밑에서 CheckSphere로 Trigger 콜라이더 겹침만 확인한다.
/// → Raycast가 Floor/Decal 사이에서 씹히는 문제를 원천 차단.
///
/// [네트워크 호환]
/// 원격 플레이어의 CharacterController는 비활성화되어 IsGrounded를 쓸 수 없으므로,
/// Raycast 기반 접지 체크를 병행한다.
///
/// [최적화]
/// 매 프레임 검사하지 않고, 이전 발자국에서 0.6m 이상 떨어졌을 때만 1회 실행.
///
/// [점프 방어]
/// isGrounded가 true일 때만 작동하여 공중 발자국 생성을 차단.
/// </summary>
public class FootprintManager : MonoBehaviour
{
    [Header("감지 설정")]
    [Tooltip("이전 발자국에서 이 거리(m) 이상 이동해야 다음 감지 수행")]
    public float checkInterval = 0.6f;

    [Tooltip("발밑 CheckSphere 반경")]
    public float checkRadius = 0.3f;

    [Header("발자국 크기")]
    [Tooltip("발자국 데칼 스케일 (기존 PaintDecal 대비)")]
    public float footprintScale = 0.3f;

    // ── 캐시 ──────────────────────────────────────────────────────
    private PlayerController playerController;
    private CharacterController characterController;
    private PlayerShooter playerShooter;
    private MonkeyHealth monkeyHealth;

    private Vector3 lastFootprintPos;
    private int paintTriggerMask;

    void Start()
    {
        playerController = GetComponent<PlayerController>();
        characterController = GetComponent<CharacterController>();
        playerShooter = GetComponent<PlayerShooter>();
        monkeyHealth = GetComponent<MonkeyHealth>();

        lastFootprintPos = transform.position;

        // PaintTrigger 레이어 마스크 캐시
        int layer = LayerMask.NameToLayer("PaintTrigger");
        paintTriggerMask = (layer >= 0) ? (1 << layer) : 0;
    }

    void Update()
    {
        // 사망 상태에서는 발자국 생성 안 함 (원격 플레이어에서도 동작)
        if (monkeyHealth != null && monkeyHealth.IsDead) return;

        // ★ 핵심 방어: 공중에서는 절대 감지하지 않음
        if (!CheckGrounded()) return;

        // 최적화: 0.6m 이상 이동했을 때만 감지 (Y축 무시)
        float distFromLast = Vector3.Distance(
            new Vector3(transform.position.x, 0f, transform.position.z),
            new Vector3(lastFootprintPos.x, 0f, lastFootprintPos.z)
        );

        if (distFromLast < checkInterval) return;

        // === 감지 실행 ===
        Vector3 footPos = CalculateFootPosition();

        // PaintTrigger 레이어의 Trigger 콜라이더와 겹치는지 확인
        bool onPaint = Physics.CheckSphere(
            footPos, checkRadius, paintTriggerMask,
            QueryTriggerInteraction.Collide
        );

        if (onPaint)
        {
            SpawnFootprint(footPos);
        }

        // 위치 기록 갱신 (페인트 위가 아니어도 갱신)
        lastFootprintPos = transform.position;
    }

    /// <summary>
    /// 접지 여부를 확인한다.
    /// 로컬 플레이어: CharacterController.isGrounded (정확)
    /// 원격 플레이어: CC가 비활성화되어 있으므로 Raycast로 체크
    /// </summary>
    bool CheckGrounded()
    {
        // CC가 활성화된 로컬 플레이어
        if (characterController != null && characterController.enabled)
            return characterController.isGrounded;

        // CC가 비활성화된 원격 플레이어: 발밑 Raycast
        float rayLength = 0.3f;
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        return Physics.Raycast(origin, Vector3.down, rayLength);
    }

    /// <summary>
    /// CharacterController 기반으로 정확한 발바닥 위치를 계산한다.
    /// </summary>
    Vector3 CalculateFootPosition()
    {
        if (characterController != null && characterController.enabled)
        {
            float halfHeight = characterController.height / 2f;
            return transform.position
                + characterController.center
                - new Vector3(0f, halfHeight - 0.02f, 0f);
        }
        // 원격 플레이어: CC 없이 발밑 근사치
        return transform.position;
    }

    /// <summary>
    /// 풀에서 발자국 데칼을 꺼내 배치한다.
    /// - 스케일을 작게 줄임
    /// - 캐릭터의 이동 방향으로 회전시킴 (추적 가능하도록)
    /// </summary>
    void SpawnFootprint(Vector3 footPos)
    {
        if (ObjectPoolManager.Instance == null) return;

        GameObject fp = ObjectPoolManager.Instance.GetFootprint();
        if (fp == null) return;

        // 위치: 발바닥 살짝 위
        fp.transform.position = footPos + Vector3.up * 0.02f;

        // 방향: DecalProjector는 로컬 Z축 방향으로 투영
        // 바닥을 향해 아래로 투영 (forward = -Y)
        Vector3 moveDir = transform.forward;
        moveDir.y = 0f;
        if (moveDir.sqrMagnitude < 0.001f)
            moveDir = Vector3.forward;

        // Z축이 아래를 향하도록 회전
        fp.transform.rotation = Quaternion.LookRotation(Vector3.down, moveDir.normalized);

        // DecalProjector 크기는 프리팹에서 설정됨 (0.3m)
        fp.transform.localScale = Vector3.one;

        // 팀 컬러 적용
        Color teamCol = (playerShooter != null) ? playerShooter.teamColor : Color.red;
        var fpDecal = fp.GetComponent<FootprintDecal>();
        if (fpDecal != null) fpDecal.SetColor(teamCol);
    }
}
