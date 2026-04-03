using UnityEngine;

/// <summary>
/// 바닥에 남는 페인트 자국.
/// 풀에서 꺼내져 배치된 뒤, 일정 시간 후 자동으로 풀에 반환된다.
///
/// [왜 별도 스크립트인가?]
/// 데칼의 수명 관리를 PaintProjectile에서 하면 총알과 데칼의 생명주기가 뒤엉킨다.
/// 데칼이 스스로 자기 수명을 관리하면 책임이 깔끔하게 분리된다. (SRP)
/// </summary>
public class PaintDecal : MonoBehaviour
{
    [Tooltip("데칼이 바닥에 남아있는 시간(초). 기획서 기준 5초")]
    public float lifeTime = 5f;

    private float timer;

    void OnEnable()
    {
        // 풀에서 꺼내질 때마다 수명 타이머 리셋
        timer = lifeTime;
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            ReturnToPool();
        }
    }

    void ReturnToPool()
    {
        if (ObjectPoolManager.Instance != null)
            ObjectPoolManager.Instance.ReturnDecal(gameObject);
        else
            gameObject.SetActive(false);
    }
}
