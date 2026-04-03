using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 오브젝트 풀링 매니저 (싱글톤).
/// 총알(Projectile)과 페인트 데칼(Decal) 두 종류의 풀을 관리한다.
///
/// [왜 한 매니저에서 두 풀을 관리하는가?]
/// 풀마다 싱글톤 매니저를 만들면 코드가 중복되고 씬에 매니저가 난립한다.
/// 풀 종류가 2~3개 수준이면 하나의 매니저에서 관리하는 게 실용적이다.
/// (만약 10개 이상으로 늘어나면 그때 제네릭 풀로 리팩터링해도 늦지 않다)
/// </summary>
public class ObjectPoolManager : MonoBehaviour
{
    public static ObjectPoolManager Instance { get; private set; }

    [Header("총알 풀")]
    public GameObject projectilePrefab;
    public int projectilePoolSize = 100;

    [Header("데칼 풀")]
    public GameObject decalPrefab;
    public int decalPoolSize = 150;

    // 풀별 Queue 분리 — 서로 다른 프리팹이 섞이는 사고 방지
    private Queue<GameObject> projectilePool = new Queue<GameObject>();
    private Queue<GameObject> decalPool = new Queue<GameObject>();

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        FillPool(projectilePrefab, projectilePool, projectilePoolSize, "Projectiles");
        FillPool(decalPrefab, decalPool, decalPoolSize, "Decals");
    }

    /// <summary>
    /// 지정된 프리팹을 poolSize개만큼 미리 생성해 비활성 상태로 보관한다.
    /// [왜 containerName으로 자식 오브젝트를 묶는가?]
    /// Hierarchy 창에서 총알 100개, 데칼 150개가 뒤섞이면 디버깅이 불가능해진다.
    /// </summary>
    void FillPool(GameObject prefab, Queue<GameObject> pool, int size, string containerName)
    {
        if (prefab == null) return;

        // Hierarchy 정리용 부모 오브젝트 생성
        Transform container = new GameObject(containerName).transform;
        container.SetParent(transform);

        for (int i = 0; i < size; i++)
        {
            GameObject obj = Instantiate(prefab, container);
            obj.SetActive(false);
            pool.Enqueue(obj);
        }
    }

    // ── 총알 풀 접근 ──────────────────────────────────────────────

    public GameObject GetProjectile()
    {
        return GetFromPool(projectilePool, projectilePrefab);
    }

    public void ReturnProjectile(GameObject obj)
    {
        ReturnToPool(obj, projectilePool);
    }

    // ── 데칼 풀 접근 ──────────────────────────────────────────────

    public GameObject GetDecal()
    {
        return GetFromPool(decalPool, decalPrefab);
    }

    public void ReturnDecal(GameObject obj)
    {
        ReturnToPool(obj, decalPool);
    }

    // ── 공통 로직 ─────────────────────────────────────────────────
    // [왜 공통 메서드로 분리?] Get/Return 로직이 풀마다 동일하므로
    // 중복 코드를 제거한다. (DRY 원칙)

    GameObject GetFromPool(Queue<GameObject> pool, GameObject prefab)
    {
        if (pool.Count > 0)
        {
            GameObject obj = pool.Dequeue();
            obj.SetActive(true);
            return obj;
        }

        Debug.LogWarning($"풀 소진! {prefab.name}을(를) 추가 생성합니다.");
        GameObject extra = Instantiate(prefab, transform);
        extra.SetActive(true);
        return extra;
    }

    void ReturnToPool(GameObject obj, Queue<GameObject> pool)
    {
        obj.SetActive(false);
        pool.Enqueue(obj);
    }
}
