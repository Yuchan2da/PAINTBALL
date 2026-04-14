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

    private Vector3 lastFootprintPos;
    private int paintTriggerMask;

    void Start()
    {
        playerController = GetComponent<PlayerController>();
        characterController = GetComponent<CharacterController>();
        playerShooter = GetComponent<PlayerShooter>();

        lastFootprintPos = transform.position;

        // PaintTrigger 레이어 마스크 캐시
        int layer = LayerMask.NameToLayer("PaintTrigger");
        paintTriggerMask = (layer >= 0) ? (1 << layer) : 0;
    }

    void Update()
    {
        // 조작 잠금 상태(사망)에서는 발자국 생성 안 함
        if (playerController != null && !playerController.inputEnabled) return;

        // ★ 핵심 방어: 공중에서는 절대 감지하지 않음
        if (playerController != null && !playerController.IsGrounded) return;

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
    /// CharacterController 기반으로 정확한 발바닥 위치를 계산한다.
    /// </summary>
    Vector3 CalculateFootPosition()
    {
        if (characterController != null)
        {
            float halfHeight = characterController.height / 2f;
            return transform.position
                + characterController.center
                - new Vector3(0f, halfHeight - 0.02f, 0f);
        }
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

        // 위치
        fp.transform.position = footPos;

        // 방향: 캐릭터의 forward를 바닥에 투영하여 데칼 회전
        Vector3 moveDir = transform.forward;
        moveDir.y = 0f;
        if (moveDir.sqrMagnitude < 0.001f)
            moveDir = Vector3.forward;

        Quaternion flatRotation = Quaternion.LookRotation(moveDir.normalized, Vector3.up);
        // Quad를 눕히기: X축 90도 회전 후 이동 방향 적용
        fp.transform.rotation = flatRotation * Quaternion.Euler(90f, 0f, 0f);

        // 스케일 축소
        fp.transform.localScale = Vector3.one * footprintScale;

        // 팀 컬러 적용
        Color teamCol = (playerShooter != null) ? playerShooter.teamColor : Color.red;
        var fpDecal = fp.GetComponent<FootprintDecal>();
        if (fpDecal != null) fpDecal.SetColor(teamCol);
    }
}
