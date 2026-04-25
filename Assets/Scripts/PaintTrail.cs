using UnityEngine;

/// <summary>
/// 이동 시 바닥에 자기 팀 색상 페인트를 남기는 시스템.
///
/// [스텔스 핵심 메카닉]
/// 캐릭터는 투명하지만, 걸을 때마다 바닥에 페인트 자국이 남아
/// 적에게 위치와 이동 경로가 노출된다.
///
/// [네트워크 호환]
/// 원격 플레이어의 CharacterController는 비활성화되어 IsGrounded를 쓸 수 없으므로,
/// Raycast 기반 접지 체크를 병행한다. 각 클라이언트에서 독립적으로 트레일을 생성하므로
/// RPC 없이도 양쪽 모두에서 보인다.
///
/// [최적화]
/// 매 프레임이 아닌, 일정 거리(trailInterval) 이동할 때만 데칼 스폰.
/// 점프 중에는 생성하지 않음.
/// </summary>
public class PaintTrail : MonoBehaviour
{
    [Header("페인트 간격")]
    [Tooltip("이 거리(m)마다 바닥에 페인트 한 방울을 남긴다")]
    public float trailInterval = 0.8f;

    [Header("데칼 크기")]
    [Tooltip("이동 페인트 데칼 스케일 (0.5 = 기본 크기의 절반)")]
    public float trailScale = 0.4f;

    // ── 캐시 ──────────────────────────────────────────────────────
    private PlayerController playerController;
    private PlayerShooter playerShooter;
    private CharacterController characterController;
    private MonkeyHealth monkeyHealth;

    private Vector3 lastTrailPos;

    void Start()
    {
        playerController = GetComponent<PlayerController>();
        playerShooter = GetComponent<PlayerShooter>();
        characterController = GetComponent<CharacterController>();
        monkeyHealth = GetComponent<MonkeyHealth>();

        lastTrailPos = transform.position;
    }

    void Update()
    {
        // 사망 중이면 스킵 (inputEnabled 대신 IsDead — 원격 플레이어에서도 동작)
        if (monkeyHealth != null && monkeyHealth.IsDead) return;

        // ★ 점프 중이면 페인트 안 남김
        if (!CheckGrounded()) return;

        // 거리 기반 체크 (Y축 무시 — 수평 이동만)
        float dist = Vector3.Distance(
            new Vector3(transform.position.x, 0f, transform.position.z),
            new Vector3(lastTrailPos.x, 0f, lastTrailPos.z)
        );

        if (dist < trailInterval) return;

        // === 페인트 스폰 ===
        SpawnTrailDecal();
        lastTrailPos = transform.position;
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
    /// 발밑에 팀 색상 페인트 데칼을 배치한다.
    /// </summary>
    void SpawnTrailDecal()
    {
        if (ObjectPoolManager.Instance == null) return;

        GameObject decal = ObjectPoolManager.Instance.GetDecal();
        if (decal == null) return;

        // 발바닥 위치 계산
        Vector3 footPos;
        if (characterController != null && characterController.enabled)
        {
            float halfHeight = characterController.height / 2f;
            footPos = transform.position
                + characterController.center
                - new Vector3(0f, halfHeight - 0.02f, 0f);
        }
        else
        {
            // 원격 플레이어: CC 없이 발밑 근사치
            footPos = transform.position;
        }

        decal.transform.position = footPos;

        // 이동 방향으로 회전 (발자국 방향 = 진행 방향)
        Vector3 moveDir = transform.forward;
        moveDir.y = 0f;
        if (moveDir.sqrMagnitude < 0.001f)
            moveDir = Vector3.forward;

        Quaternion flatRot = Quaternion.LookRotation(moveDir.normalized, Vector3.up);
        decal.transform.rotation = flatRot * Quaternion.Euler(90f, 0f, 0f);

        // 스케일
        decal.transform.localScale = Vector3.one * trailScale;

        // 팀 컬러 적용 (DecalTintCache의 사전 생성 머티리얼 사용)
        var projector = decal.GetComponent<UnityEngine.Rendering.Universal.DecalProjector>();
        if (projector != null)
        {
            Color color = (playerShooter != null) ? playerShooter.teamColor : Color.red;
            Material tinted = DecalTintCache.GetTintedMaterial(color, projector);
            if (tinted != null)
            {
                projector.material = tinted;
            }
        }
    }
}
