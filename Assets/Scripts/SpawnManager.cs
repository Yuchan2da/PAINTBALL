using UnityEngine;

/// <summary>
/// 리스폰 위치 관리 (싱글톤).
///
/// [설계]
/// - spawnPoints 배열에 맵 곳곳의 스폰 위치 Transform을 등록한다.
/// - 배열이 비어 있으면, 현재 바닥(Floor) 범위 내에서 랜덤 좌표를 생성한다.
///   → 맵이 아직 없는 프로토타입 단계에서도 안전하게 동작.
/// - 추후 맵 완성 시 Inspector에서 스폰 포인트만 등록하면 자동 전환.
/// </summary>
public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance { get; private set; }

    [Header("스폰 포인트")]
    [Tooltip("맵에 배치된 스폰 위치들. 비어 있으면 바닥 범위 내 랜덤 좌표 사용")]
    public Transform[] spawnPoints;

    [Header("임시 랜덤 스폰 범위 (스폰 포인트가 없을 때)")]
    [Tooltip("랜덤 스폰 X/Z 범위 (바닥 중심 기준 ±)")]
    public float randomRange = 6f;
    [Tooltip("스폰 높이 (FloorTop + CC절반)")]
    public float spawnY = 1.5f;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// 무작위 스폰 위치를 반환한다.
    /// [우선순위] spawnPoints 배열 → 없으면 바닥 범위 내 랜덤 좌표.
    /// </summary>
    public Vector3 GetRandomSpawnPoint()
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            Transform chosen = spawnPoints[Random.Range(0, spawnPoints.Length)];
            return chosen.position;
        }

        // 스폰 포인트가 없으면 바닥 범위 내 랜덤 좌표
        float x = Random.Range(-randomRange, randomRange);
        float z = Random.Range(-randomRange, randomRange);
        return new Vector3(x, spawnY, z);
    }
}
