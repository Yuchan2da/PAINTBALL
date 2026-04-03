using UnityEngine;

/// <summary>
/// 물리 기반 페인트 총알.
/// 
/// [왜 Start() 대신 OnEnable()인가?]
/// 풀링에서는 오브젝트가 파괴되지 않고 껐다 켜진다.
/// Start()는 최초 1회만 호출되지만, OnEnable()은 SetActive(true)될 때마다 호출되므로
/// 매 발사마다 속도 초기화와 수명 타이머를 리셋할 수 있다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PaintProjectile : MonoBehaviour
{
    [Tooltip("충돌 없이 날아갈 수 있는 최대 시간(초)")]
    public float lifeTime = 5f;

    private float timer;
    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void OnEnable()
    {
        // 풀에서 꺼내질 때마다 이전 발사의 잔여 물리력을 깨끗이 제거
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        timer = lifeTime;
    }

    void Update()
    {
        // 수명 카운트다운 — 아무것도 못 맞추고 날아간 총알 회수용
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            ReturnToPool();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        // 바닥(Floor 레이어)에 닿았을 때 → 페인트 자국 남기기
        if (collision.gameObject.layer == LayerMask.NameToLayer("Floor"))
        {
            SpawnDecal(collision);
            ReturnToPool();
            return;
        }

        // 적의 몸통이나 머리에 닿았을 때
        if (collision.gameObject.CompareTag("Body") || collision.gameObject.CompareTag("Head"))
        {
            // Head = 20 데미지, Body = 10 데미지 (기획서 2단 히트박스 규칙)
            int damage = collision.gameObject.CompareTag("Head") ? 20 : 10;

            // [왜 GetComponentInParent?]
            // 콜라이더(Head/Body)는 자식 오브젝트에 있고,
            // MonkeyHealth는 부모(최상위 적 오브젝트)에 붙어있으므로
            // 부모 방향으로 올라가며 컴포넌트를 찾아야 한다.
            MonkeyHealth health = collision.gameObject.GetComponentInParent<MonkeyHealth>();
            if (health != null)
            {
                health.TakeDamage(damage);
            }

            ReturnToPool();
            return;
        }

        // 그 외 벽 등에 닿아도 회수
        ReturnToPool();
    }

    /// <summary>
    /// Destroy 대신 풀로 반환한다.
    /// [왜 메서드를 분리했는가?]
    /// 회수 로직이 한 곳에 모여 있으면, 나중에 회수 시 이펙트(파티클 등)를
    /// 추가하더라도 이 메서드 하나만 수정하면 된다. (단일 책임 원칙)
    /// </summary>
    /// <summary>
    /// 총알이 바닥에 맞은 지점에 페인트 데칼을 배치한다.
    /// [왜 collision 데이터를 쓰는가?]
    /// ContactPoint에서 정확한 충돌 위치와 표면 법선(normal)을 가져올 수 있어서,
    /// 데칼이 바닥 표면에 딱 붙어서 눕도록 회전시킬 수 있다.
    /// </summary>
    void SpawnDecal(Collision collision)
    {
        if (ObjectPoolManager.Instance == null) return;

        ContactPoint contact = collision.GetContact(0);
        GameObject decal = ObjectPoolManager.Instance.GetDecal();

        // 충돌 지점에 배치 (바닥에 살짝 띄워서 z-fighting 방지)
        decal.transform.position = contact.point + contact.normal * 0.01f;

        // [회전 계산]
        // Unity Quad의 앞면(보이는 면)은 로컬 -Z 방향을 향한다.
        // 바닥에 눕히면서 앞면이 위를 향하게 하려면
        // -Z → contact.normal 방향으로 회전시켜야 한다.
        decal.transform.rotation = Quaternion.FromToRotation(-Vector3.forward, contact.normal);

        Debug.Log($"[데칼] 위치: {decal.transform.position} / 법선: {contact.normal}");
    }

    void ReturnToPool()
    {
        if (ObjectPoolManager.Instance != null)
            ObjectPoolManager.Instance.ReturnProjectile(gameObject);
        else
            gameObject.SetActive(false);
    }
}
