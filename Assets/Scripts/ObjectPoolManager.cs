using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 총알 오브젝트 풀링 매니저 (싱글톤)
/// 
/// [왜 풀링을 쓰는가?]
/// Instantiate/Destroy는 호출할 때마다 메모리 할당/해제가 발생해서
/// 연사가 잦은 FPS 게임에서는 프레임 드랍(GC Spike)의 주범이 된다.
/// 미리 만들어 두고 껐다/켰다 재활용하면 이 문제를 완전히 피할 수 있다.
/// 
/// [왜 싱글톤인가?]
/// 풀 매니저는 씬에 딱 1개만 존재하면 되고,
/// 어디서든 ObjectPoolManager.Instance로 접근할 수 있어야 편하다.
/// </summary>
public class ObjectPoolManager : MonoBehaviour
{
    public static ObjectPoolManager Instance { get; private set; }

    [Header("풀 설정")]
    [Tooltip("풀링할 총알 프리팹")]
    public GameObject projectilePrefab;  
    
    [Tooltip("시작 시 미리 만들어 둘 총알 개수")]
    public int poolSize = 100;

    // [왜 Queue인가?]
    // 선입선출(FIFO) 구조라서 가장 오래 쉬고 있던 총알부터 꺼내 쓰게 된다.
    // 골고루 돌려쓰므로 특정 오브젝트만 혹사당하는 일이 없다.
    private Queue<GameObject> pool = new Queue<GameObject>();

    void Awake()
    {
        // 싱글톤 세팅: 이미 있으면 중복 제거
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        FillPool();
    }

    /// <summary>
    /// 게임 시작 시 총알을 poolSize개만큼 미리 생성해 비활성 상태로 보관한다.
    /// </summary>
    void FillPool()
    {
        for (int i = 0; i < poolSize; i++)
        {
            GameObject obj = Instantiate(projectilePrefab, transform);
            obj.SetActive(false);
            pool.Enqueue(obj);
        }
    }

    /// <summary>
    /// 풀에서 총알 1개를 꺼내 활성화한 뒤 돌려준다.
    /// </summary>
    public GameObject Get()
    {
        // 풀에 여유분이 있으면 꺼내 쓴다
        if (pool.Count > 0)
        {
            GameObject obj = pool.Dequeue();
            obj.SetActive(true);
            return obj;
        }

        // 풀이 바닥났으면 1개 추가 생성 (안전장치)
        Debug.LogWarning("풀 %.소진! 총알을 추가 생성합니다.");
        GameObject extra = Instantiate(projectilePrefab, transform);
        extra.SetActive(true);
        return extra;
    }

    /// <summary>
    /// 사용이 끝난 총알을 비활성화하고 다시 풀에 넣는다.
    /// </summary>
    public void Return(GameObject obj)
    {
        obj.SetActive(false);
        obj.transform.SetParent(transform);
        pool.Enqueue(obj);
    }
}
