using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 인게임/로비 프리뷰에서 크로스헤어를 렌더링한다.
///
/// [구조]
/// Canvas 위에 Image 10개(선 4 + 점 1 + 외곽선 5)로 구성.
///
/// [동적 확장]
/// - OnFire(): 사격 시 갭이 벌어짐
/// - OnMove(): 이동 시 갭이 벌어짐
/// - Update()에서 매 프레임 Lerp로 복구
///
/// [설계 원칙]
/// - Awake()에서 Image 컴포넌트를 캐싱하여 매 프레임 GetComponent 방지
/// - 로비 프리뷰에서는 SetSettings()로 실시간 반영
/// - 인게임에서는 Start()에서 PlayerPrefs 로드
/// </summary>
public class CrosshairRenderer : MonoBehaviour
{
    // ── Inspector 연결: 선 Image ────────────────────────────────
    [Header("선 Image (상/하/좌/우)")]
    [Tooltip("세로 위쪽 선")]
    public RectTransform lineTop;
    [Tooltip("세로 아래쪽 선")]
    public RectTransform lineBottom;
    [Tooltip("가로 왼쪽 선")]
    public RectTransform lineLeft;
    [Tooltip("가로 오른쪽 선")]
    public RectTransform lineRight;

    // ── Inspector 연결: 점 ──────────────────────────────────────
    [Header("점")]
    [Tooltip("화면 중앙 점")]
    public RectTransform dot;

    // ── Inspector 연결: 외곽선 Image ────────────────────────────
    [Header("외곽선 Image (선과 1:1 대응)")]
    [Tooltip("세로 위쪽 외곽선")]
    public RectTransform outTop;
    [Tooltip("세로 아래쪽 외곽선")]
    public RectTransform outBottom;
    [Tooltip("가로 왼쪽 외곽선")]
    public RectTransform outLeft;
    [Tooltip("가로 오른쪽 외곽선")]
    public RectTransform outRight;
    [Tooltip("점 외곽선")]
    public RectTransform outDot;

    // ── 내부 캐시 ────────────────────────────────────────────────
    private Image[] lineImages;
    private Image[] outlineImages;
    private Image dotImage;
    private Image outDotImage;

    private CrosshairSettings settings;
    private float dynamicOffset;

    // ── 상수 ─────────────────────────────────────────────────────
    /// <summary>연사 시 dynamicOffset 폭주 방지 배수.</summary>
    private const float MAX_DYNAMIC_MULTIPLIER = 3f;

    // ===================================================================
    //  Unity 라이프사이클
    // ===================================================================

    void Awake()
    {
        CacheImageComponents();
    }

    void Start()
    {
        settings = CrosshairSettings.Load();
        ApplyCurrentSettings();
    }

    void Update()
    {
        if (dynamicOffset > 0f)
        {
            dynamicOffset = Mathf.Lerp(dynamicOffset, 0f, Time.deltaTime * 10f);
            if (dynamicOffset < 0.1f) dynamicOffset = 0f;
            RefreshPositions();
        }
    }

    // ===================================================================
    //  초기화
    // ===================================================================

    /// <summary>Image 컴포넌트를 배열로 캐싱. Awake()에서 1회만 실행.</summary>
    private void CacheImageComponents()
    {
        lineImages = new Image[]
        {
            SafeGetImage(lineTop),
            SafeGetImage(lineBottom),
            SafeGetImage(lineLeft),
            SafeGetImage(lineRight)
        };
        outlineImages = new Image[]
        {
            SafeGetImage(outTop),
            SafeGetImage(outBottom),
            SafeGetImage(outLeft),
            SafeGetImage(outRight)
        };
        dotImage    = SafeGetImage(dot);
        outDotImage = SafeGetImage(outDot);
    }

    /// <summary>null-safe GetComponent 헬퍼.</summary>
    private Image SafeGetImage(RectTransform rt)
    {
        return rt != null ? rt.GetComponent<Image>() : null;
    }

    // ===================================================================
    //  설정 적용
    // ===================================================================

    /// <summary>
    /// 현재 settings를 기반으로 크로스헤어 전체를 갱신한다.
    /// 로비 프리뷰 변경 시, 게임 시작 시 호출.
    /// </summary>
    public void ApplyCurrentSettings()
    {
        if (settings == null) settings = CrosshairSettings.Load();

        // ── 전체 숨김 ──
        if (!settings.showCrosshair)
        {
            HideAll();
            return;
        }

        Color color = settings.GetColor();

        // ── 선 색상 + 표시 ──
        bool linesVisible = settings.showLines;
        for (int i = 0; i < lineImages.Length; i++)
        {
            if (lineImages[i] != null)
            {
                lineImages[i].color = color;
                lineImages[i].gameObject.SetActive(linesVisible);
            }
        }

        // ── 세로 선 크기 (Top, Bottom) — width=두께, height=길이 ──
        SafeSetSize(lineTop,    settings.lineThickness, settings.lineLength);
        SafeSetSize(lineBottom, settings.lineThickness, settings.lineLength);

        // ── 가로 선 크기 (Left, Right) — width=길이, height=두께 ──
        SafeSetSize(lineLeft,  settings.lineLength, settings.lineThickness);
        SafeSetSize(lineRight, settings.lineLength, settings.lineThickness);

        // ── T자 (윗선 제거) ──
        if (settings.tShape)
        {
            SafeSetActive(lineTop, false);
            SafeSetActive(outTop,  false);
        }
        else if (linesVisible)
        {
            SafeSetActive(lineTop, true);
        }

        // ── 점 ──
        SafeSetActive(dot, settings.showDot);
        SafeSetSize(dot, settings.dotSize, settings.dotSize);
        if (dotImage != null) dotImage.color = color;

        // ── 외곽선 ──
        bool showOut = settings.showOutline;
        for (int i = 0; i < outlineImages.Length; i++)
        {
            if (outlineImages[i] != null)
            {
                // 외곽선은 선이 보이는 때만 표시
                bool outVisible = showOut && linesVisible;
                outlineImages[i].gameObject.SetActive(outVisible);
                outlineImages[i].color = Color.black;
            }
        }

        // T자면 윗 외곽선도 숨김
        if (settings.tShape && outlineImages.Length > 0 && outlineImages[0] != null)
            outlineImages[0].gameObject.SetActive(false);

        SafeSetActive(outDot, showOut && settings.showDot);
        if (outDotImage != null) outDotImage.color = Color.black;

        // 외곽선 크기: 선보다 outlineThickness*2만큼 크게
        if (showOut)
        {
            float pad = settings.outlineThickness * 2f;
            ApplyOutlinePadding(outTop,    lineTop,    pad);
            ApplyOutlinePadding(outBottom, lineBottom, pad);
            ApplyOutlinePadding(outLeft,   lineLeft,   pad);
            ApplyOutlinePadding(outRight,  lineRight,  pad);
            ApplyOutlinePadding(outDot,    dot,        pad);
        }

        RefreshPositions();
    }

    /// <summary>크로스헤어 전체 숨김 (showCrosshair=false).</summary>
    private void HideAll()
    {
        for (int i = 0; i < lineImages.Length; i++)
            if (lineImages[i] != null) lineImages[i].gameObject.SetActive(false);
        for (int i = 0; i < outlineImages.Length; i++)
            if (outlineImages[i] != null) outlineImages[i].gameObject.SetActive(false);
        SafeSetActive(dot, false);
        SafeSetActive(outDot, false);
    }

    // ===================================================================
    //  위치 계산
    // ===================================================================

    /// <summary>선 위치를 gap + dynamicOffset 기반으로 재배치.</summary>
    private void RefreshPositions()
    {
        if (settings == null) return;

        float g    = settings.gap + dynamicOffset;
        float half = settings.lineLength / 2f;

        // 세로 선: 중심에서 gap + 선 길이 절반만큼 떨어짐
        SafeSetPosition(lineTop,    0f,             g + half);
        SafeSetPosition(lineBottom, 0f,            -(g + half));
        // 가로 선
        SafeSetPosition(lineLeft,  -(g + half),     0f);
        SafeSetPosition(lineRight,  g + half,       0f);

        // 외곽선도 동일 위치
        SafeSetPosition(outTop,     0f,             g + half);
        SafeSetPosition(outBottom,  0f,            -(g + half));
        SafeSetPosition(outLeft,   -(g + half),     0f);
        SafeSetPosition(outRight,   g + half,       0f);
    }

    // ===================================================================
    //  외부 호출 API — PlayerShooter, GameHUD에서 사용
    // ===================================================================

    /// <summary>사격 시 호출 — 갭 확장 (clamp로 폭주 방지).</summary>
    public void OnFire()
    {
        if (settings == null || !settings.dynamicOnFire) return;

        dynamicOffset += settings.dynamicAmount;
        dynamicOffset = Mathf.Min(dynamicOffset, settings.dynamicAmount * MAX_DYNAMIC_MULTIPLIER);
    }

    /// <summary>이동 시 호출 — 수평 속도 기반 갭 확장.</summary>
    public void OnMove(float horizontalSpeed)
    {
        if (settings == null || !settings.dynamicOnMove) return;

        dynamicOffset = Mathf.Max(dynamicOffset, horizontalSpeed * 0.5f);
        dynamicOffset = Mathf.Min(dynamicOffset, settings.dynamicAmount * MAX_DYNAMIC_MULTIPLIER);
    }

    /// <summary>동적 오프셋을 즉시 0으로 리셋. 리스폰 시 호출.</summary>
    public void ResetDynamic()
    {
        dynamicOffset = 0f;
        RefreshPositions();
    }

    /// <summary>설정 덮어쓰기 (로비 UI에서 실시간 프리뷰용).</summary>
    public void SetSettings(CrosshairSettings s)
    {
        settings = s;
        ApplyCurrentSettings();
    }

    // ===================================================================
    //  내부 유틸 — null-safe RectTransform 조작
    // ===================================================================

    private void SafeSetSize(RectTransform rt, float width, float height)
    {
        if (rt != null) rt.sizeDelta = new Vector2(width, height);
    }

    private void SafeSetPosition(RectTransform rt, float x, float y)
    {
        if (rt != null) rt.anchoredPosition = new Vector2(x, y);
    }

    private void SafeSetActive(RectTransform rt, bool active)
    {
        if (rt != null) rt.gameObject.SetActive(active);
    }

    private void ApplyOutlinePadding(RectTransform outline, RectTransform source, float padding)
    {
        if (outline == null || source == null) return;
        outline.sizeDelta = source.sizeDelta + Vector2.one * padding;
    }
}
