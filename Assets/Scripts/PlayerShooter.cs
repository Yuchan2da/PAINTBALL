using UnityEngine;

/// <summary>
/// 1인칭 사격 처리 스크립트.
/// 연사 제한, 장탄수, 재장전 로직 포함.
///
/// [자해 방지 구조]
/// 총알(Projectile 레이어)은 Hitbox 레이어와만 충돌한다.
/// Player의 Head/Body도 Hitbox 레이어이므로, 자기 총알이 자기 히트박스에 맞을 수 있다.
/// → Fire()에서 Physics.IgnoreCollision으로 자기 히트박스와의 물리 충돌을 차단한다.
/// </summary>
public class PlayerShooter : MonoBehaviour
{
    [Header("사격 설정")]
    public Transform firePoint;
    public Camera playerCamera;
    public float fireForce = 35f;

    [Header("팀 색상")]
    [Tooltip("이 플레이어가 발사하는 총알 색상. 멀티플레이에서 Photon으로 동기화 예정")]
    public Color teamColor = Color.red;

    [Header("연사 / 탄창 설정")]
    public float fireCooldown = 0.33f;
    public int maxAmmo = 15;

    public int CurrentAmmo { get; private set; }
    public int MaxAmmo => maxAmmo;

    private float lastFireTime;
    private Collider[] ownerHitboxes;   // 자기 Head/Body 콜라이더 캐시
    private PaintReceiver paintReceiver; // 테스트용 캐시

    // ── 외부 제어 (사망/리스폰) ────────────────────────────────────
    /// <summary>
    /// false로 설정하면 사격, 재장전, 테스트 입력이 모두 잠긴다.
    /// MonkeyHealth에서 사망/부활 시 토글한다.
    /// </summary>
    [HideInInspector] public bool inputEnabled = true;

    void Start()
    {
        CurrentAmmo = maxAmmo;
        CacheHitboxColliders();
        paintReceiver = GetComponent<PaintReceiver>();
    }

    /// <summary>
    /// 자기 히트박스 콜라이더를 전부 캐싱한다 (Head, Body, ArmR, ArmL, LegR, LegL).
    /// [왜 GetComponentsInChildren?] 히트박스가 몇 개든 자동으로 수집.
    /// Start()에서 1회만 실행하므로 성능 부담 없음.
    /// </summary>
    void CacheHitboxColliders()
    {
        int hitboxLayer = LayerMask.NameToLayer("Hitbox");
        var allColliders = GetComponentsInChildren<Collider>();
        // Hitbox 레이어에 속하는 콜라이더만 필터링
        var list = new System.Collections.Generic.List<Collider>();
        for (int i = 0; i < allColliders.Length; i++)
        {
            if (allColliders[i].gameObject.layer == hitboxLayer)
                list.Add(allColliders[i]);
        }
        ownerHitboxes = list.ToArray();
    }

    void Update()
    {
        if (!inputEnabled) return; // 사망 상태에서는 모든 입력 차단

        if (Input.GetMouseButton(0) && Time.time >= lastFireTime + fireCooldown && CurrentAmmo > 0)
            Fire();

        if (Input.GetMouseButtonDown(0) && CurrentAmmo <= 0)
            Debug.Log("탄창이 비었습니다! R키로 재장전하세요.");

        if (Input.GetKeyDown(KeyCode.R) && CurrentAmmo < maxAmmo)
            Reload();

        // [테스트 전용] G키 — 파란 팀 피격 시뮬레이션. 멀티플레이 완성 후 삭제.
        if (Input.GetKeyDown(KeyCode.G))
            SimulateEnemyHit(Color.blue);
    }

    void Fire()
    {
        lastFireTime = Time.time;
        CurrentAmmo--;

        GameObject bullet = ObjectPoolManager.Instance.GetProjectile();
        bullet.transform.position = firePoint.position;
        bullet.transform.rotation = Quaternion.LookRotation(playerCamera.transform.forward);

        // ── 총알 초기화 ──
        var pp = bullet.GetComponent<PaintProjectile>();
        if (pp != null)
        {
            pp.teamColor    = teamColor;
            pp.ownerRoot    = transform.root;
            pp.shooterName  = gameObject.name;
        }

        // ── 자기 히트박스와 물리 충돌 무시 ──
        // [왜 Physics.IgnoreCollision?]
        // 같은 Hitbox 레이어이므로 물리 엔진이 충돌을 시도한다.
        // 총알이 자기 몸을 뚫고 나가도록, 발사 시 충돌 자체를 꺼준다.
        var bulletCol = bullet.GetComponent<Collider>();
        if (bulletCol != null)
        {
            for (int i = 0; i < ownerHitboxes.Length; i++)
            {
                if (ownerHitboxes[i] != null)
                    Physics.IgnoreCollision(bulletCol, ownerHitboxes[i]);
            }
        }

        var rb = bullet.GetComponent<Rigidbody>();
        if (rb != null)
            rb.AddForce(playerCamera.transform.forward * fireForce, ForceMode.Impulse);

        Debug.Log($"발사! 잔탄: {CurrentAmmo}/{maxAmmo}");
    }

    void Reload()
    {
        CurrentAmmo = maxAmmo;
        Debug.Log($"재장전 완료! {CurrentAmmo}/{maxAmmo}");
    }

    /// <summary>
    /// [테스트 전용] 적 팀에게 맞은 상황을 시뮬레이션한다.
    /// Raycast를 우회하고 랜덤 UV에 직접 페인트한다.
    /// [왜 PaintAtRandomUV?]
    /// SimulateEnemyHit은 실제 총알 충돌이 아니므로,
    /// 월드→UV 변환 Raycast가 정확하지 않을 수 있다.
    /// 테스트 목적이므로 UV에 직접 칠하는 게 더 확실하고 빠르다.
    /// 멀티플레이 완성 후 삭제할 것.
    /// </summary>
    void SimulateEnemyHit(Color enemyColor)
    {
        if (paintReceiver == null)
        {
            Debug.LogWarning("[테스트] Player에 PaintReceiver가 없습니다!");
            return;
        }

        paintReceiver.PaintAtRandomUV(enemyColor);
        Debug.Log("[테스트] 파란 팀 피격 시뮬레이션 — 랜덤 UV 페인트");
    }
}
