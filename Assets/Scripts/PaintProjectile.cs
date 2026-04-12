using UnityEngine;

/// <summary>
/// 물리 기반 페인트 총알.
///
/// [풀링 호환] Start() 대신 OnEnable()에서 상태를 리셋한다.
/// Start()는 최초 1회만 호출되지만, OnEnable()은 SetActive(true)될 때마다
/// 호출되므로 매 발사마다 속도와 수명 타이머를 리셋할 수 있다.
///
/// [레이어 구조]
/// - 총알: Projectile 레이어 → Hitbox 레이어와만 충돌
/// - 히트박스: Hitbox 레이어 → Projectile과만 충돌
/// - CharacterController: Default → Projectile과 충돌하지 않음
/// → 따라서 총알이 CC에 막히거나, CC가 총알을 밟는 문제가 원천 차단됨.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PaintProjectile : MonoBehaviour
{
    [Tooltip("충돌 없이 날아갈 수 있는 최대 시간(초)")]
    public float lifeTime = 5f;

    [HideInInspector] public Color teamColor = Color.red;
    [HideInInspector] public Transform ownerRoot;
    [HideInInspector] public string shooterName;

    private Rigidbody rb;
    private float timer;

    // ── 레이어 ID 캐싱 (StringToHash와 동일 원리 — 매 프레임 문자열 비교 방지) ──
    private static int floorLayer  = -1;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // 레이어 ID는 앱 수명 전체에서 동일하므로 최초 1회만 조회
        if (floorLayer < 0)
            floorLayer = LayerMask.NameToLayer("Floor");

        // 풀에서 인스턴스가 생성될 때 Projectile 레이어 강제 적용
        // [왜?] 프리팹 레이어를 바꿔도 이미 Instantiate된 풀 인스턴스에는
        // 반영되지 않을 수 있으므로, 코드에서 확실히 보장한다.
        int projLayer = LayerMask.NameToLayer("Projectile");
        if (projLayer >= 0) gameObject.layer = projLayer;
    }

    void OnEnable()
    {
        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        timer = lifeTime;
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f) ReturnToPool();
    }

    void OnCollisionEnter(Collision collision)
    {
        // ── 자해 방지 ──────────────────────────────────────────
        // Hitbox 레이어로 격리했기 때문에 대부분 방지되지만,
        // 멀티플레이에서 같은 팀 히트박스와 충돌할 수 있으므로 이중 안전장치.
        if (ownerRoot != null && collision.transform.root == ownerRoot)
            return;

        // ── 바닥 충돌 → 데칼 ────────────────────────────────────
        if (collision.gameObject.layer == floorLayer)
        {
            SpawnDecal(collision);
            ReturnToPool();
            return;
        }

        // ── 히트박스 충돌 → 데미지 + 페인트 ─────────────────────
        if (collision.gameObject.CompareTag("Head") || collision.gameObject.CompareTag("Body"))
        {
            ContactPoint contact = collision.GetContact(0);

            bool isHeadshot = collision.gameObject.CompareTag("Head");
            int damage = isHeadshot ? 20 : 10;
            var health = collision.gameObject.GetComponentInParent<MonkeyHealth>();
            if (health != null) health.TakeDamage(damage, shooterName, isHeadshot);

            var paintReceiver = collision.gameObject.GetComponentInParent<PaintReceiver>();
            if (paintReceiver != null)
                paintReceiver.PaintAt(contact.point, contact.normal, teamColor);

            ReturnToPool();
            return;
        }

        // ── 그 외 (벽 등) → 회수 ────────────────────────────────
        ReturnToPool();
    }

    /// <summary>
    /// 바닥에 페인트 데칼을 배치한다.
    /// </summary>
    void SpawnDecal(Collision collision)
    {
        if (ObjectPoolManager.Instance == null) return;

        ContactPoint contact = collision.GetContact(0);
        GameObject decal = ObjectPoolManager.Instance.GetDecal();

        decal.transform.position = contact.point + contact.normal * 0.01f;
        decal.transform.rotation = Quaternion.FromToRotation(-Vector3.forward, contact.normal);
    }

    void ReturnToPool()
    {
        if (ObjectPoolManager.Instance != null)
            ObjectPoolManager.Instance.ReturnProjectile(gameObject);
        else
            gameObject.SetActive(false);
    }
}
