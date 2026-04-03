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

    [Header("연사 / 탄창 설정")]
    [Tooltip("연사 간격(초). 0.33 = 초당 약 3발")]
    public float fireCooldown = 0.33f;
    
    [Tooltip("한 탄창의 최대 총알 수")]
    public int maxAmmo = 15;

    private int currentAmmo;
    private float lastFireTime;

    void Start()
    {
        currentAmmo = maxAmmo;
    }

    void Update()
    {
        // 좌클릭 — 쿨타임이 지났고 탄이 남아있을 때만 발사
        if (Input.GetMouseButton(0) && Time.time >= lastFireTime + fireCooldown && currentAmmo > 0)
        {
            Fire();
        }

        // 탄 비었을 때 클릭하면 알림
        if (Input.GetMouseButtonDown(0) && currentAmmo <= 0)
        {
            Debug.Log("탄창이 비었습니다! R키로 재장전하세요.");
        }

        // R키 재장전
        if (Input.GetKeyDown(KeyCode.R) && currentAmmo < maxAmmo)
        {
            Reload();
        }
    }

    void Fire()
    {
        lastFireTime = Time.time;
        currentAmmo--;

        GameObject bullet = ObjectPoolManager.Instance.GetProjectile();
        bullet.transform.position = firePoint.position;
        bullet.transform.rotation = Quaternion.LookRotation(playerCamera.transform.forward);

        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.AddForce(playerCamera.transform.forward * fireForce, ForceMode.Impulse);
        }

        Debug.Log($"발사! 잔탄: {currentAmmo}/{maxAmmo}");
    }

    void Reload()
    {
        currentAmmo = maxAmmo;
        Debug.Log($"재장전 완료! {currentAmmo}/{maxAmmo}");
    }
}
