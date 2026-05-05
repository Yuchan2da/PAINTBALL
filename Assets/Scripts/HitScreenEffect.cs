using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// 피격 시 화면에 페인트가 튀는 듯한 비네팅 이펙트를 표시한다.
///
/// [동작 방식]
/// - 런타임 초기화 시 Canvas를 찾아 전체 화면 비네팅 Image를 자동 생성
/// - 피격 시 Play(attackerColor)를 호출하면:
///   1. 이미지 색상을 공격자 팀컬러로 설정
///   2. 알파를 즉시 올림 (연속 피격 시 누적, 최대 0.6)
///   3. fadeTime에 걸쳐 서서히 알파 0으로 페이드아웃
/// - HP 20% 이하 시 약한 빨간 비네팅 상시 표시 (위기 경고)
///
/// [배치]
/// GameHUD가 존재하는 Canvas에 자동 부착. Inspector 연결 불필요.
/// </summary>
public class HitScreenEffect : MonoBehaviour
{
    public static HitScreenEffect Instance { get; private set; }

    [Header("피격 이펙트")]
    [Tooltip("피격 시 즉시 올리는 알파 값")]
    [SerializeField] private float hitAlpha = 0.35f;

    [Tooltip("알파 최대치 (연속 피격 누적 제한)")]
    [SerializeField] private float maxAlpha = 0.6f;

    [Tooltip("페이드아웃 소요 시간 (초)")]
    [SerializeField] private float fadeTime = 0.5f;

    [Header("저체력 경고")]
    [Tooltip("저체력 경고 시 상시 유지되는 알파 값")]
    [SerializeField] private float lowHpAlpha = 0.15f;

    [Tooltip("저체력 경고 색상")]
    [SerializeField] private Color lowHpColor = new Color(0.8f, 0.1f, 0.1f, 1f);

    // ── 내부 상태 ──────────────────────────────────────────────
    private Image hitImage;
    private float currentAlpha;
    private Coroutine fadeCoroutine;
    private bool isLowHp;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        CreateHitImage();
    }

    // ===================================================================
    //  공개 API
    // ===================================================================

    /// <summary>
    /// 피격 이펙트 재생. 공격자의 팀 컬러로 화면이 물든다.
    /// 연속 피격 시 알파가 누적되어 점점 진해진다.
    /// </summary>
    public void Play(Color attackerColor)
    {
        if (hitImage == null) return;

        // 색상 갱신 (가장 최근 피격 색상 사용)
        hitImage.color = new Color(attackerColor.r, attackerColor.g, attackerColor.b, currentAlpha);

        // 알파 누적 (최대치 제한)
        currentAlpha = Mathf.Min(currentAlpha + hitAlpha, maxAlpha);
        SetImageAlpha(currentAlpha);

        // 기존 페이드 중이면 취소하고 새로 시작
        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeOut());
    }

    /// <summary>
    /// 저체력 경고 상태 설정. HP 비율 기반으로 호출.
    /// </summary>
    public void SetLowHpWarning(bool lowHp)
    {
        isLowHp = lowHp;

        // 저체력이고 페이드 중이 아니면 경고 비네팅 표시
        if (isLowHp && fadeCoroutine == null && hitImage != null)
        {
            currentAlpha = lowHpAlpha;
            hitImage.color = new Color(lowHpColor.r, lowHpColor.g, lowHpColor.b, currentAlpha);
        }
    }

    /// <summary>
    /// 이펙트를 즉시 제거한다. 리스폰 시 호출.
    /// </summary>
    public void Clear()
    {
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }
        currentAlpha = 0f;
        isLowHp = false;
        SetImageAlpha(0f);
    }

    // ===================================================================
    //  비네팅 Image 자동 생성
    // ===================================================================

    /// <summary>
    /// Canvas를 찾아 전체 화면 비네팅 Image를 런타임에 생성한다.
    /// 비네팅 텍스처(Assets/Textures/HitVignette)를 로드하고,
    /// 없으면 단색 Image로 폴백한다.
    /// </summary>
    private void CreateHitImage()
    {
        // 이 스크립트가 붙은 Canvas 또는 씬의 Canvas 사용
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        // Image 오브젝트 생성
        var go = new GameObject("HitVignetteOverlay");
        go.transform.SetParent(canvas.transform, false);

        // 맨 앞에 배치 (다른 UI 위)
        go.transform.SetAsLastSibling();

        // RectTransform: 전체 화면
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // Image 컴포넌트
        hitImage = go.AddComponent<Image>();

        // 비네팅 스프라이트 로드 시도
        var sprite = Resources.Load<Sprite>("HitVignette");
        if (sprite == null)
        {
            // Resources 폴더에 없으면 에셋 경로에서 직접 로드
            sprite = LoadVignetteSprite();
        }

        if (sprite != null)
        {
            hitImage.sprite = sprite;
            hitImage.type = Image.Type.Sliced;
        }

        hitImage.color = new Color(1f, 0f, 0f, 0f);
        hitImage.raycastTarget = false; // 클릭 방해 금지

        SetImageAlpha(0f);
    }

    /// <summary>
    /// Assets/Textures/HitVignette 스프라이트 로드 시도.
    /// 실패 시 null 반환 (단색 Image로 폴백).
    /// </summary>
    private Sprite LoadVignetteSprite()
    {
#if UNITY_EDITOR
        var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/HitVignette.png");
        return tex;
#else
        return null;
#endif
    }

    // ===================================================================
    //  페이드 코루틴
    // ===================================================================

    private IEnumerator FadeOut()
    {
        float startAlpha = currentAlpha;

        // 저체력이면 lowHpAlpha까지만 내림, 아니면 0까지
        float targetAlpha = isLowHp ? lowHpAlpha : 0f;

        float elapsed = 0f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeTime;
            currentAlpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            SetImageAlpha(currentAlpha);
            yield return null;
        }

        currentAlpha = targetAlpha;
        SetImageAlpha(currentAlpha);

        // 저체력 경고 전환: 페이드 완료 후 경고 색으로 교체
        if (isLowHp && hitImage != null)
            hitImage.color = new Color(lowHpColor.r, lowHpColor.g, lowHpColor.b, currentAlpha);

        fadeCoroutine = null;
    }

    // ===================================================================
    //  내부 유틸
    // ===================================================================

    private void SetImageAlpha(float alpha)
    {
        if (hitImage == null) return;
        Color c = hitImage.color;
        c.a = alpha;
        hitImage.color = c;
    }
}
