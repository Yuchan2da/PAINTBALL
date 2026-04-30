using UnityEngine;
using System.Collections;
using Photon.Pun;

/// <summary>
/// 효과음 + BGM 통합 재생 매니저 (싱글톤).
///
/// [설계]
/// - AudioSource 풀링으로 동시 재생 지원 (겹쳐도 끊기지 않음)
/// - 3D 사운드: 위치 기반 감쇄 (총소리, 피격음)
/// - 2D 사운드: UI/알림 계열 (킬 효과음, BGM)
///
/// [사용법]
/// SFXManager.Instance.PlayShot(transform.position);
/// SFXManager.Instance.PlayBGM();
/// SFXManager.Instance.PlayKillAlert(isHeadshot);
/// </summary>
public class SFXManager : MonoBehaviour
{
    public static SFXManager Instance { get; private set; }

    [Header("효과음 클립")]
    [Tooltip("발사음")] public AudioClip shotClip;
    [Tooltip("피격음 (몸통)")] public AudioClip hitClip;
    [Tooltip("헤드샷 피격음")] public AudioClip headshotClip;
    [Tooltip("재장전음")] public AudioClip reloadClip;

    [Header("킬 알림 클립")]
    [Tooltip("일반 킬 알림음")] public AudioClip killClip;
    [Tooltip("헤드샷 킬 알림음")] public AudioClip headshotKillClip;

    [Header("BGM")]
    [Tooltip("인게임 BGM")] public AudioClip bgmClip;
    [Tooltip("BGM 볼륨 (0~1)")] [Range(0f, 1f)]
    public float bgmVolume = 0.3f;

    [Header("풀링 설정")]
    [Tooltip("동시 재생 가능한 최대 AudioSource 수")]
    public int poolSize = 10;

    [Header("3D 사운드 설정")]
    [Tooltip("소리가 들리기 시작하는 최대 거리")]
    public float maxDistance = 30f;

    private AudioSource[] pool;
    private int poolIndex;

    // BGM 전용 AudioSource (풀과 분리)
    private AudioSource bgmSource;

    // 킬 알림 전용 AudioSource (2D, 풀과 분리)
    private AudioSource killAlertSource;

    // BGM 페이드아웃 코루틴 참조
    private Coroutine fadeCoroutine;

    void Awake()
    {
        // 싱글톤
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // AudioSource 풀 생성 (3D 효과음용)
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

        // BGM 전용 AudioSource 생성 (2D, 루프)
        var bgmGo = new GameObject("BGM_Source");
        bgmGo.transform.SetParent(transform);
        bgmSource = bgmGo.AddComponent<AudioSource>();
        bgmSource.playOnAwake = false;
        bgmSource.spatialBlend = 0f;  // 2D
        bgmSource.loop = true;
        bgmSource.volume = bgmVolume;

        // 킬 알림 전용 AudioSource 생성 (2D, 원샷)
        var killGo = new GameObject("KillAlert_Source");
        killGo.transform.SetParent(transform);
        killAlertSource = killGo.AddComponent<AudioSource>();
        killAlertSource.playOnAwake = false;
        killAlertSource.spatialBlend = 0f;  // 2D
    }

    void Start()
    {
        // ScoreManager 킬 이벤트 구독 → 내가 킬했을 때 알림음 재생
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnKillEvent += OnKillEvent;
    }

    void OnDestroy()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnKillEvent -= OnKillEvent;
    }

    // ── 킬 이벤트 핸들러 ─────────────────────────────────────────

    /// <summary>
    /// ScoreManager에서 킬 발생 시 호출.
    /// 로컬 플레이어가 킬러인 경우에만 알림음 재생.
    /// </summary>
    void OnKillEvent(string killerName, string victimName, bool isHeadshot)
    {
        string myName = PhotonNetwork.IsConnected
            ? PhotonNetwork.NickName
            : "Player";

        if (killerName == myName)
            PlayKillAlert(isHeadshot);
    }

    // ── 공개 메서드: 효과음 ──────────────────────────────────────

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

    // ── 공개 메서드: 킬 알림 ─────────────────────────────────────

    /// <summary>
    /// 킬 알림음 재생 (2D, 내 화면에서만).
    /// 헤드샷 킬이면 별도 클립 사용.
    /// </summary>
    public void PlayKillAlert(bool isHeadshot)
    {
        var clip = isHeadshot && headshotKillClip != null ? headshotKillClip : killClip;
        if (clip == null) return;

        killAlertSource.PlayOneShot(clip);
    }

    // ── 공개 메서드: BGM ─────────────────────────────────────────

    /// <summary>BGM 재생 시작. 이미 재생 중이면 무시.</summary>
    public void PlayBGM()
    {
        if (bgmClip == null || bgmSource.isPlaying) return;

        // 이전 페이드아웃이 진행 중이면 중단
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }

        bgmSource.clip = bgmClip;
        bgmSource.volume = bgmVolume;
        bgmSource.Play();
    }

    /// <summary>BGM 즉시 정지.</summary>
    public void StopBGM()
    {
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }

        bgmSource.Stop();
    }

    /// <summary>BGM을 지정 시간에 걸쳐 페이드아웃 후 정지.</summary>
    public void FadeOutBGM(float duration = 2f)
    {
        if (!bgmSource.isPlaying) return;

        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);

        fadeCoroutine = StartCoroutine(FadeOutRoutine(duration));
    }

    IEnumerator FadeOutRoutine(float duration)
    {
        float startVolume = bgmSource.volume;

        while (bgmSource.volume > 0.01f)
        {
            bgmSource.volume -= startVolume * Time.deltaTime / duration;
            yield return null;
        }

        bgmSource.Stop();
        bgmSource.volume = bgmVolume; // 다음 재생을 위해 복원
        fadeCoroutine = null;
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
