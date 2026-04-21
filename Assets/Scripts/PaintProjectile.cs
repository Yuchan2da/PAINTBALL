using UnityEngine;
using Photon.Pun;

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

    [Tooltip("탄착 스플래시 파티클 프리팫 (ObjectPoolManager에서 설정)")]
    [HideInInspector] public GameObject hitSplashPrefab;

    private Rigidbody rb;
    private float timer;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // 풀에서 인스턴스가 생성될 때 Projectile 레이어 강제 적용
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
        if (ownerRoot != null && collision.transform.root == ownerRoot)
            return;

        // ── 히트박스 충돌 → 데미지 + 페인트 + 이펙트 ────────────
        if (collision.gameObject.CompareTag("Head") || collision.gameObject.CompareTag("Body"))
        {
            ContactPoint contact = collision.GetContact(0);

            bool isHeadshot = collision.gameObject.CompareTag("Head");
            int damage = isHeadshot ? 20 : 10;

            // 데미지 판정 (Photon 전환 시 이 호출만 RPC로 교체)
            ApplyHitDamage(collision.gameObject, damage, isHeadshot);

            var paintReceiver = collision.gameObject.GetComponentInParent<PaintReceiver>();
            if (paintReceiver != null)
                paintReceiver.PaintAt(contact.point, contact.normal, teamColor);

            // ── 피격 사운드 ──
            if (SFXManager.Instance != null)
                SFXManager.Instance.PlayHit(contact.point, isHeadshot);

            // ── 탄착 스플래시 파티클 ──
            SpawnHitSplash(contact.point, contact.normal);

            ReturnToPool();
            return;
        }

        // ── 모든 표면 (바닥, 벽, 상자, 드럼통 등) → 데칼 + 스플래시 ──
        SpawnDecal(collision);
        ContactPoint cp = collision.GetContact(0);
        SpawnHitSplash(cp.point, cp.normal);
        ReturnToPool();
    }

    /// <summary>
    /// 표면에 페인트 데칼을 배치한다.
    /// DecalProjector를 사용하여 모서리에도 자연스럽게 투영된다.
    /// </summary>
    void SpawnDecal(Collision collision)
    {
        if (ObjectPoolManager.Instance == null) return;

        ContactPoint contact = collision.GetContact(0);
        GameObject decal = ObjectPoolManager.Instance.GetDecal();

        // 위치: 표면에서 살짝 띄워 배치
        decal.transform.position = contact.point + contact.normal * 0.01f;

        // 회전: DecalProjector는 로컬 Z축 방향으로 투영하므로,
        // 표면 노멀의 반대 방향(-normal)을 forward로 설정
        decal.transform.rotation = Quaternion.LookRotation(-contact.normal, Vector3.up);

        // 팀 컬러 적용
        var paintDecal = decal.GetComponent<PaintDecal>();
        if (paintDecal != null)
            paintDecal.SetColor(teamColor);
    }

    /// <summary>
    /// 데미지 판정 래퍼.
    /// [싱글] 로컬 health.TakeDamage() 직접 호출.
    /// [멀티] health.TakeDamageNetwork() → RPC로 Owner에게 전달.
    /// [완 총알은 네트워크 오브젝트가 아님]
    /// 각 클라이언트에서 로컬로 생성/파괴하고, 충돌 감지 시 데미지만 RPC로 보낸다.
    /// 이것이 FPS 게임의 표준 아키텍처.
    /// </summary>
    void ApplyHitDamage(GameObject hitObject, int damage, bool isHeadshot)
    {
        var health = hitObject.GetComponentInParent<MonkeyHealth>();
        if (health != null)
        {
            if (PhotonNetwork.IsConnected)
                health.TakeDamageNetwork(damage, shooterName, isHeadshot);
            else
                health.TakeDamage(damage, shooterName, isHeadshot);
        }
    }

    void ReturnToPool()
    {
        if (ObjectPoolManager.Instance != null)
            ObjectPoolManager.Instance.ReturnProjectile(gameObject);
        else
            gameObject.SetActive(false);
    }

    /// <summary>
    /// 충돌 지점에 페인트 튤는 파티클을 생성한다.
    /// 팀 컬러로 파티클 색상을 동적 변경.
    /// </summary>
    void SpawnHitSplash(Vector3 position, Vector3 normal)
    {
        if (hitSplashPrefab == null) return;

        var go = Instantiate(hitSplashPrefab, position, Quaternion.LookRotation(normal));
        var ps = go.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            var main = ps.main;
            main.startColor = teamColor;
        }
        Destroy(go, 1f);
    }
}
