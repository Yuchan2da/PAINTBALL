using UnityEngine;
using System.Collections;
using Photon.Pun;

/// <summary>
/// 1인칭 사격 처리 스크립트.
/// 연사 제한, 장탄수, 재장전 로직 포함.
///
/// [자해 방지 구조]
/// 총알(Projectile 레이어)은 Hitbox 레이어와만 충돌한다.
/// Player의 Head/Body도 Hitbox 레이어이므로, 자기 총알이 자기 히트박스에 맞을 수 있다.
/// → Fire()에서 Physics.IgnoreCollision으로 자기 히트박스와의 물리 충돌을 차단한다.
/// </summary>
public class PlayerShooter : MonoBehaviourPun
{
    [Header("사격 설정")]
    public Transform firePoint;
    public Camera playerCamera;
    public float fireForce = 35f;

    [Header("이펙트")]
    [Tooltip("총구 화염 ParticleSystem (firePoint 자식으로 배치)")]
    public ParticleSystem muzzleFlash;

    [Header("카메라 반동")]
    [Tooltip("발사 시 카메라 반동 각도 (X축 위로)")]
    public float recoilAngle = 2f;

    [Header("팀 색상")]
    [Tooltip("이 플레이어가 발사하는 총알 색상. 멀티플레이에서 Photon으로 동기화 예정")]
    public Color teamColor = Color.red;

    [Header("연사 / 탄창 설정")]
    public float fireCooldown = 0.33f;
    public int maxAmmo = 15;
    [Tooltip("재장전 소요 시간 (초)")]
    public float reloadTime = 2.5f;

    public int CurrentAmmo { get; private set; }
    public int MaxAmmo => maxAmmo;

    private float lastFireTime;
    private Collider[] ownerHitboxes;
    private PaintReceiver paintReceiver;
    private bool isReloading;

    /// <summary>재장전 중 여부 (HUD에서 참조)</summary>
    public bool IsReloading => isReloading;

    // ── 외부 제어 (사망/리스폰 + 멀티플레이) ──────────────────────
    /// <summary>
    /// false로 설정하면 사격, 재장전이 모두 잠긴다.
    /// MonkeyHealth에서 사망/부활 시 토글한다.
    /// </summary>
    [HideInInspector] public bool inputEnabled = true;

    /// <summary>
    /// 로컬 플레이어 여부. Photon 연동 시 photonView.IsMine으로 교체.
    /// </summary>
    [HideInInspector] public bool isLocalPlayer = true;

    void Start()
    {
        // Photon IsMine으로 로컬/원격 분리 (OfflineMode에서도 정상 동작)
        if (photonView != null)
            isLocalPlayer = photonView.IsMine;

        CurrentAmmo = maxAmmo;
        CacheHitboxColliders();

        // Inspector 미연결 시 자동 탐색
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();
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
        if (!isLocalPlayer) return;    // 원격 플레이어 입력 차단
        if (!inputEnabled) return;     // 사망 상태 입력 차단

        if (Input.GetMouseButton(0) && Time.time >= lastFireTime + fireCooldown && CurrentAmmo > 0 && !isReloading)
            Fire();

        if (Input.GetMouseButtonDown(0) && CurrentAmmo <= 0 && !isReloading)
        {
            // 빈 탄창 → 자동 재장전 시작
            StartCoroutine(ReloadRoutine());
        }

        if (Input.GetKeyDown(KeyCode.R) && CurrentAmmo < maxAmmo && !isReloading)
            StartCoroutine(ReloadRoutine());
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

        // ── 총구 화염 파티클 ──
        if (muzzleFlash != null)
            muzzleFlash.Play();

        // ── 발사음 ──
        if (SFXManager.Instance != null)
            SFXManager.Instance.PlayShot(firePoint.position);

        // ── 카메라 반동 ──
        var pc = GetComponent<PlayerController>();
        if (pc != null)
            pc.ApplyRecoil(recoilAngle);

        Debug.Log($"발사! 잔탄: {CurrentAmmo}/{maxAmmo}");
    }

    IEnumerator ReloadRoutine()
    {
        isReloading = true;

        // 재장전 사운드 (시작 시 재생)
        if (SFXManager.Instance != null)
            SFXManager.Instance.PlayReload(transform.position);

        Debug.Log($"재장전 중... ({reloadTime}초)");

        yield return new WaitForSeconds(reloadTime);

        CurrentAmmo = maxAmmo;
        isReloading = false;

        Debug.Log($"재장전 완료! {CurrentAmmo}/{maxAmmo}");
    }

}
