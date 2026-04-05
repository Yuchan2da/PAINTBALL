using UnityEngine;

/// <summary>
/// 1인칭 사격 처리 스크립트.
/// 연사 제한, 장탄수, 재장전 로직 포함.
/// </summary>
public class PlayerShooter : MonoBehaviour
{
    [Header("사격 설정")]
    public Transform firePoint;
    public Camera playerCamera;
    public float fireForce = 35f;

    [Header("팀 색상")]
    [Tooltip("이 플레이어가 발사하는 총알 색상 (팀 색상). 멀티플레이에서 Photon으로 동기화 예정")]
    public Color teamColor = Color.red;

    [Header("연사 / 탄창 설정")]
    [Tooltip("연사 간격(초). 0.33 = 초당 약 3발")]
    public float fireCooldown = 0.33f;
    
    [Tooltip("한 탄창의 최대 총알 수")]
    public int maxAmmo = 15;

    // [왜 프로퍼티?] HUD에서 읽을 수 있지만 외부에서 임의로 바꾸는 건 차단 (MonkeyHealth와 동일 원칙)
    public int CurrentAmmo { get; private set; }
    public int MaxAmmo => maxAmmo;
    private float lastFireTime;

    void Start()
    {
        CurrentAmmo = maxAmmo;
    }

    void Update()
    {
        // 좌클릭 — 쿨타임이 지났고 탄이 남아있을 때만 발사
        if (Input.GetMouseButton(0) && Time.time >= lastFireTime + fireCooldown && CurrentAmmo > 0)
        {
            Fire();
        }

        // 탄 비었을 때 클릭하면 알림
        if (Input.GetMouseButtonDown(0) && CurrentAmmo <= 0)
        {
            Debug.Log("탄창이 비었습니다! R키로 재장전하세요.");
        }

        // R키 재장전
        if (Input.GetKeyDown(KeyCode.R) && CurrentAmmo < maxAmmo)
        {
            Reload();
        }

        // [테스트 전용] G키 — 파란 팀 총알에 맞는 상황을 시뮬레이션
        // 멀티플레이 완성 후 삭제
        if (Input.GetKeyDown(KeyCode.G))
        {
            SimulateEnemyHit(Color.blue);
        }
    }

    void Fire()
    {
        lastFireTime = Time.time;
        CurrentAmmo--;

        GameObject bullet = ObjectPoolManager.Instance.GetProjectile();
        bullet.transform.position = firePoint.position;
        bullet.transform.rotation = Quaternion.LookRotation(playerCamera.transform.forward);

        // 총알에 팀 색상 전달 — PaintReceiver가 이 색으로 페인트를 칠함
        PaintProjectile pp = bullet.GetComponent<PaintProjectile>();
        if (pp != null) pp.teamColor = teamColor;

        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.AddForce(playerCamera.transform.forward * fireForce, ForceMode.Impulse);
        }

        Debug.Log($"발사! 잔탄: {CurrentAmmo}/{maxAmmo}");
    }

    void Reload()
    {
        CurrentAmmo = maxAmmo;
        Debug.Log($"재장전 완료! {CurrentAmmo}/{maxAmmo}");
    }

    /// <summary>
    /// [테스트 전용] 적 팀에게 맞은 상황을 시뮬레이션한다.
    /// 이 플레이어의 PaintReceiver를 직접 호출하여 지정 색상으로 칠한다.
    /// 멀티플레이 완성 후 삭제할 것.
    /// </summary>
    void SimulateEnemyHit(Color enemyColor)
    {
        var paintReceiver = GetComponent<PaintReceiver>();
        if (paintReceiver == null)
        {
            Debug.LogWarning("[테스트] Player에 PaintReceiver가 없습니다!");
            return;
        }

        // 캐릭터 중심에서 약간 앞쪽을 타격 지점으로 사용
        Vector3 fakeHitPoint  = transform.position + transform.forward * 0.3f + Vector3.up * 0.5f;
        Vector3 fakeHitNormal = -transform.forward;
        paintReceiver.PaintAt(fakeHitPoint, fakeHitNormal, enemyColor);
        Debug.Log($"[테스트] 파란 팀 피격 시뮬레이션 실행");
    }
}
