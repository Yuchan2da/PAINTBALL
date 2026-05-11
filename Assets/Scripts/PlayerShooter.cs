using UnityEngine;
using System.Collections;
using Photon.Pun;
using Photon.Realtime;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// 1인칭 사격 처리 스크립트.
/// 연사 제한, 장탄수, 재장전 로직 포함.
///
/// [자해 방지 구조]
/// 총알(Projectile 레이어)은 Hitbox 레이어와만 충돌한다.
/// Player의 Head/Body도 Hitbox 레이어이므로, 자기 총알이 자기 히트박스에 맞을 수 있다.
/// → Fire()에서 Physics.IgnoreCollision으로 자기 히트박스와의 물리 충돌을 차단한다.
/// </summary>
public class PlayerShooter : MonoBehaviourPunCallbacks
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

    [Header("1인칭 건 모델")]
    [Tooltip("FPSGunModel 컴포넌트 (Main Camera 자식에 배치)")]
    public FPSGunModel fpsGunModel;

    [Header("팀 색상")]
    [Tooltip("이 플레이어가 발사하는 총알 색상. 멀티플레이에서 Photon으로 동기화 예정")]
    public Color teamColor = Color.red;

    [Header("연사 / 탄창 설정")]
    public float fireCooldown = 0.33f;
    public int maxAmmo = 15;
    [Tooltip("재장전 소요 시간 (초)")]
    public float reloadTime = 2.5f;

    [Header("수류탄")]
    [Tooltip("시작 시 보유 수류탄 개수")]
    public int maxGrenades = 1;
    [Tooltip("투척 힘")]
    public float throwForce = 15f;
    [Tooltip("위쪽 보정 힘")]
    public float throwUpForce = 3f;

    public int CurrentAmmo { get; private set; }
    public int MaxAmmo => maxAmmo;
    public int CurrentGrenades { get; private set; }

    private float lastFireTime;
    private Collider[] ownerHitboxes;
    private PaintReceiver paintReceiver;
    private bool isReloading;

    /// <summary>재장전 중 여부 (HUD에서 참조)</summary>
    public bool IsReloading => isReloading;

    // ── 외부 제어 (사망/리스폰 + 멀티플레이) ──────────────────────
    /// <summary>
    /// false로 설정하면 사격, 재장전이 모두 잠긴다.
    /// 기본값 false: 스폰 직후에는 잠김 상태이며,
    /// GameManager가 Playing 상태로 전환할 때 활성화한다.
    /// </summary>
    [HideInInspector] public bool inputEnabled = false;

    /// <summary>
    /// 로컬 플레이어 여부. Photon 연동 시 photonView.IsMine으로 교체.
    /// </summary>
    [HideInInspector] public bool isLocalPlayer = true;

    // Photon Custom Properties 키
    private const string PROP_TEAM_COLOR = "tc";

    // 플레이어별 고유 색상: ColorSelectUI.Palette에서 통합 관리.
    // 여기서는 팔레트 색상만 캐싱하여 사용한다.
    private static Color[] PlayerColors;

    /// <summary>팔레트 색상 배열 초기화 (최초 1회).</summary>
    private static void EnsurePlayerColors()
    {
        if (PlayerColors != null && PlayerColors.Length == ColorSelectUI.Palette.Length) return;
        PlayerColors = new Color[ColorSelectUI.Palette.Length];
        for (int i = 0; i < ColorSelectUI.Palette.Length; i++)
            PlayerColors[i] = ColorSelectUI.Palette[i].color;
    }

    void Start()
    {
        // Photon IsMine으로 로컬/원격 분리 (OfflineMode에서도 정상 동작)
        if (photonView != null)
            isLocalPlayer = photonView.IsMine;

        // GameSettings에서 무기/수류탄 설정 적용
        maxAmmo = GameSettings.Current.magazineSize;
        maxGrenades = GameSettings.Current.grenadeCount;

        CurrentAmmo = maxAmmo;
        CurrentGrenades = maxGrenades;
        CacheHitboxColliders();
        EnsurePlayerColors();

        // Inspector 미연결 시 자동 탐색
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();

        // ── teamColor: 로비에서 선택한 색상 적용 ──
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && photonView != null)
        {
            if (photonView.IsMine)
            {
                // 로컬: 로비에서 선택한 팔레트 인덱스 읽기
                int paletteIndex = ColorSelectUI.GetPlayerColorIndex(PhotonNetwork.LocalPlayer);

                // 미선택 시 ActorNumber로 폴백
                if (paletteIndex < 0 || paletteIndex >= PlayerColors.Length)
                    paletteIndex = (PhotonNetwork.LocalPlayer.ActorNumber - 1) % PlayerColors.Length;

                teamColor = PlayerColors[paletteIndex];
                SyncTeamColorToNetwork();

                // 1인칭 건 모델 팀색 적용 (skinIndex = paletteIndex, 1:1 매핑)
                if (fpsGunModel != null)
                    fpsGunModel.SetTeamSkin(paletteIndex);
            }
            else
            {
                // 원격: 상대방의 Custom Properties에서 teamColor 읽기
                ReadTeamColorFromNetwork();

                // 원격 플레이어의 건 모델은 표시하지 않음 (투명 캐릭터 게임 특성)
                if (fpsGunModel != null)
                    fpsGunModel.SetVisible(false);
            }
        }
    }

    /// <summary>
    /// 로컬 플레이어의 teamColor를 Photon Custom Properties로 전송.
    /// </summary>
    void SyncTeamColorToNetwork()
    {
        float[] c = { teamColor.r, teamColor.g, teamColor.b, teamColor.a };
        Hashtable props = new Hashtable { { PROP_TEAM_COLOR, c } };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    /// <summary>
    /// 원격 플레이어의 Custom Properties에서 teamColor를 읽어온다.
    /// </summary>
    void ReadTeamColorFromNetwork()
    {
        if (photonView.Owner == null) return;
        object val;
        if (photonView.Owner.CustomProperties.TryGetValue(PROP_TEAM_COLOR, out val))
        {
            float[] c = (float[])val;
            teamColor = new Color(c[0], c[1], c[2], c[3]);
        }
    }

    /// <summary>
    /// 플레이어 프로퍼티 변경 시 호출. 원격 플레이어의 teamColor 갱신.
    /// </summary>
    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (photonView == null || photonView.Owner != targetPlayer) return;
        if (!changedProps.ContainsKey(PROP_TEAM_COLOR)) return;

        float[] c = (float[])changedProps[PROP_TEAM_COLOR];
        teamColor = new Color(c[0], c[1], c[2], c[3]);
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

        // 수류탄 투척 (G키)
        if (Input.GetKeyDown(KeyCode.G) && CurrentGrenades > 0 && !isReloading)
            ThrowGrenade();
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
            pp.teamColor         = teamColor;
            pp.ownerRoot         = transform.root;
            pp.shooterName       = gameObject.name;
            pp.shooterPhotonView = photonView; // 페인트 RPC 전송용
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

        // ── 건 모델 반동 ──
        if (fpsGunModel != null)
            fpsGunModel.PlayRecoil();

        // ── 크로스헤어 확장 ──
        if (GameHUD.Instance != null && GameHUD.Instance.crosshairRenderer != null)
            GameHUD.Instance.crosshairRenderer.OnFire();
    }

    IEnumerator ReloadRoutine()
    {
        isReloading = true;

        // 건 모델 재장전 연출 시작
        if (fpsGunModel != null)
            fpsGunModel.SetReloading(true);

        // 재장전 사운드 (시작 시 재생)
        if (SFXManager.Instance != null)
            SFXManager.Instance.PlayReload(transform.position);

        yield return new WaitForSeconds(reloadTime);

        CurrentAmmo = maxAmmo;
        isReloading = false;

        // 건 모델 재장전 연출 종료
        if (fpsGunModel != null)
            fpsGunModel.SetReloading(false);
    }

    // ── 수류탄 투척 ────────────────────────────────────────────────────

    /// <summary>
    /// 수류탄을 투척한다. 카메라 전방 + 약간 위 방향으로 힘을 가한다.
    /// PhotonNetwork.Instantiate로 모든 클라이언트에서 보이며,
    /// InstantiationData로 팀 색상 + 투척자 ViewID를 전달한다.
    /// </summary>
    void ThrowGrenade()
    {
        CurrentGrenades--;

        Camera cam = playerCamera != null ? playerCamera : Camera.main;
        Vector3 throwDir = cam.transform.forward;
        Vector3 spawnPos = cam.transform.position + throwDir * 1.5f;

        // InstantiationData: [r, g, b, a, ownerViewID]
        object[] data = {
            teamColor.r, teamColor.g, teamColor.b, teamColor.a,
            photonView.ViewID
        };

        GameObject grenade = PhotonNetwork.Instantiate(
            "PaintGrenade", spawnPos, Quaternion.identity, 0, data);

        var rb = grenade.GetComponent<Rigidbody>();
        if (rb != null)
            rb.AddForce(throwDir * throwForce + Vector3.up * throwUpForce,
                        ForceMode.Impulse);

        // HUD 업데이트
        if (GameHUD.Instance != null)
            GameHUD.Instance.UpdateGrenadeCount(CurrentGrenades);
    }

    /// <summary>
    /// 수류탄 보유량을 설정값으로 보충한다. 부활 시 호출.
    /// </summary>
    public void RefillGrenades()
    {
        CurrentGrenades = maxGrenades;
        if (GameHUD.Instance != null)
            GameHUD.Instance.UpdateGrenadeCount(CurrentGrenades);
    }

    // ── 네트워크 페인트 동기화 RPC ────────────────────────────────────

    /// <summary>
    /// 원격 클라이언트에서 수신: 벽/바닥 데칼 생성.
    /// PaintProjectile.SyncDecalOverNetwork()에서 호출된다.
    /// </summary>
    [PunRPC]
    public void RPC_SpawnDecal(Vector3 point, Vector3 normal, float[] color)
    {
        if (ObjectPoolManager.Instance == null) return;

        GameObject decal = ObjectPoolManager.Instance.GetDecal();
        decal.transform.position = point + normal * 0.01f;
        decal.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);

        Color teamCol = new Color(color[0], color[1], color[2], color[3]);
        var paintDecal = decal.GetComponent<PaintDecal>();
        if (paintDecal != null)
            paintDecal.SetColor(teamCol);
    }

    /// <summary>
    /// 원격 클라이언트에서 수신: 캐릭터 UV 페인트 생성.
    /// PaintProjectile.SyncBodyPaintOverNetwork()에서 호출된다.
    /// </summary>
    [PunRPC]
    public void RPC_PaintBody(Vector3 hitPoint, Vector3 hitNormal, float[] color, int targetViewID)
    {
        PhotonView targetPV = PhotonView.Find(targetViewID);
        if (targetPV == null) return;

        var paintReceiver = targetPV.GetComponent<PaintReceiver>();
        if (paintReceiver != null)
        {
            Color teamCol = new Color(color[0], color[1], color[2], color[3]);
            paintReceiver.PaintAt(hitPoint, hitNormal, teamCol);
        }
    }
}
