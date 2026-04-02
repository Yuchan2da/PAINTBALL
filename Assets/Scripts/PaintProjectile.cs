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
        // 바닥(Floor 레이어)에 닿았을 때
        if (collision.gameObject.layer == LayerMask.NameToLayer("Floor"))
        {
            // TODO: 페인트 데칼 생성 (다음 마일스톤)
            ReturnToPool();
            return;
        }

        // 적의 몸통이나 머리에 닿았을 때
        if (collision.gameObject.CompareTag("Body") || collision.gameObject.CompareTag("Head"))
        {
            int damage = collision.gameObject.CompareTag("Head") ? 20 : 10;
            Debug.Log($"명중! 부위: {collision.gameObject.tag} / 데미지: {damage}");
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
    void ReturnToPool()
    {
        if (ObjectPoolManager.Instance != null)
            ObjectPoolManager.Instance.Return(gameObject);
        else
            gameObject.SetActive(false);
    }
}
