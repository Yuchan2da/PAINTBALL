using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 오브젝트 풀링 매니저 (싱글톤).
/// 총알(Projectile), 페인트 데칼(Decal), 발자국(Footprint) 세 종류의 풀을 관리한다.
///
/// [왜 한 매니저에서 세 풀을 관리하는가?]
/// 풀마다 싱글톤 매니저를 만들면 코드가 중복되고 씬에 매니저가 난립한다.
/// 풀 종류가 2~3개 수준이면 하나의 매니저에서 관리하는 게 실용적이다.
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

    [Header("발자국 풀")]
    [Tooltip("발자국 프리팹 (데칼 프리팹 재사용 가능). 비워두면 데칼 프리팹 사용")]
    public GameObject footprintPrefab;
    public int footprintPoolSize = 80;

    [Header("탄착 이펙트")]
    [Tooltip("탄착 스플래시 파티클 프리팹")]
    public GameObject hitSplashPrefab;

    // 풀별 Queue 분리 — 서로 다른 프리팹이 섞이는 사고 방지
    private Queue<GameObject> projectilePool = new Queue<GameObject>();
    private Queue<GameObject> decalPool = new Queue<GameObject>();
    private Queue<GameObject> footprintPool = new Queue<GameObject>();

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

        // 발자국 프리팹이 없으면 데칼 프리팹을 재사용
        GameObject fpPrefab = footprintPrefab != null ? footprintPrefab : decalPrefab;
        FillPool(fpPrefab, footprintPool, footprintPoolSize, "Footprints");
    }

    /// <summary>
    /// 지정된 프리팹을 poolSize개만큼 미리 생성해 비활성 상태로 보관한다.
    /// </summary>
    void FillPool(GameObject prefab, Queue<GameObject> pool, int size, string containerName)
    {
        if (prefab == null) return;

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
        var obj = GetFromPool(projectilePool, projectilePrefab);

        // 탄착 스플래시 프리팫 주입
        if (hitSplashPrefab != null)
        {
            var pp = obj.GetComponent<PaintProjectile>();
            if (pp != null) pp.hitSplashPrefab = hitSplashPrefab;
        }

        return obj;
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

    // ── 발자국 풀 접근 ────────────────────────────────────────────

    public GameObject GetFootprint()
    {
        GameObject fpPrefab = footprintPrefab != null ? footprintPrefab : decalPrefab;
        return GetFromPool(footprintPool, fpPrefab);
    }

    public void ReturnFootprint(GameObject obj)
    {
        ReturnToPool(obj, footprintPool);
    }

    // ── 공통 로직 ─────────────────────────────────────────────────

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
