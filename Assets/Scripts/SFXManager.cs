using UnityEngine;

/// <summary>
/// 효과음 통합 재생 매니저 (싱글톤).
///
/// [설계]
/// - AudioSource 풀링으로 동시 재생 지원 (겹쳐도 끊기지 않음)
/// - 3D 사운드: 위치 기반 감쇄 (총소리, 피격음)
/// - 2D 사운드: UI/알림 계열 (킬 효과음 등)
///
/// [사용법]
/// SFXManager.Instance.PlayShot(transform.position);
/// </summary>
public class SFXManager : MonoBehaviour
{
    public static SFXManager Instance { get; private set; }

    [Header("효과음 클립")]
    [Tooltip("발사음")] public AudioClip shotClip;
    [Tooltip("피격음 (몸통)")] public AudioClip hitClip;
    [Tooltip("헤드샷 피격음")] public AudioClip headshotClip;
    [Tooltip("재장전음")] public AudioClip reloadClip;

    [Header("풀링 설정")]
    [Tooltip("동시 재생 가능한 최대 AudioSource 수")]
    public int poolSize = 10;

    [Header("3D 사운드 설정")]
    [Tooltip("소리가 들리기 시작하는 최대 거리")]
    public float maxDistance = 30f;

    private AudioSource[] pool;
    private int poolIndex;

    void Awake()
    {
        // 싱글톤
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // AudioSource 풀 생성
        pool = new AudioSource[poolSize];
        for (int i = 0; i < poolSize; i++)
        {
            var go = new GameObject("SFX_Source_" + i);
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;        // 3D
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1f;
            src.maxDistance = maxDistance;
            pool[i] = src;
        }
    }

    // ── 공개 메서드 ──────────────────────────────────────────────

    /// <summary>발사음 재생 (3D)</summary>
    public void PlayShot(Vector3 position)
    {
        Play3D(shotClip, position);
    }

    /// <summary>피격음 재생 (3D)</summary>
    public void PlayHit(Vector3 position, bool isHeadshot = false)
    {
        var clip = isHeadshot && headshotClip != null ? headshotClip : hitClip;
        Play3D(clip, position);
    }

    /// <summary>재장전음 재생 (3D)</summary>
    public void PlayReload(Vector3 position)
    {
        Play3D(reloadClip, position);
    }

    // ── 내부 구현 ────────────────────────────────────────────────

    /// <summary>
    /// 풀에서 사용 가능한 AudioSource를 꺼내 3D 위치에서 재생.
    /// 라운드 로빈 방식으로 순환 — 모든 소스가 사용 중이면 가장 오래된 것을 덮어쓴다.
    /// </summary>
    void Play3D(AudioClip clip, Vector3 position)
    {
        if (clip == null) return;

        var src = pool[poolIndex];
        poolIndex = (poolIndex + 1) % poolSize;

        src.transform.position = position;
        src.clip = clip;
        src.Play();
    }
}
