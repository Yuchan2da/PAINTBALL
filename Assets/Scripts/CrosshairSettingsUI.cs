using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 로비 설정 패널에서 크로스헤어를 커스터마이징하는 UI 컨트롤러.
///
/// [역할]
/// - 슬라이더/토글/색상버튼으로 크로스헤어 설정 편집
/// - CrosshairRenderer 프리뷰에 실시간 반영
/// - 닫기 시 PlayerPrefs에 저장
///
/// [배치]
/// LobbyScene → Canvas → SettingsPanel에 부착.
/// GunCustomizeUI.cs와 동일한 설계 패턴을 따른다.
///
/// [설계 원칙]
/// - Photon 불필요 (크로스헤어는 로컬 전용) → MonoBehaviour 상속
/// - 모든 UI 참조는 SerializeField + Tooltip으로 Inspector 연결
/// - 값 변경 → settings 동기화 → 라벨 갱신 → 프리뷰 갱신 (단방향 흐름)
/// </summary>
public class CrosshairSettingsUI : MonoBehaviour
{
    // ── 프리뷰 ───────────────────────────────────────────────────
    [Header("프리뷰")]
    [Tooltip("설정 변경 시 실시간으로 반영될 CrosshairRenderer")]
    [SerializeField] private CrosshairRenderer previewRenderer;

    // ── 색상 프리셋 ──────────────────────────────────────────────
    [Header("색상 프리셋")]
    [Tooltip("9색 프리셋 버튼 배열 (Inspector에서 순서대로 연결)")]
    [SerializeField] private Button[] colorButtons;

    [Tooltip("각 버튼에 대응하는 색상 (Inspector에서 9색 설정)")]
    [SerializeField] private Color[] colorPresets = new Color[]
    {
        new Color(0f, 1f, 0f, 1f),          // 초록
        new Color(1f, 0.2f, 0.2f, 1f),      // 빨강
        new Color(0.2f, 0.4f, 1f, 1f),      // 파랑
        new Color(1f, 1f, 0f, 1f),          // 노랑
        new Color(1f, 0.4f, 0.7f, 1f),      // 핑크
        new Color(1f, 1f, 1f, 1f),          // 하양
        new Color(0f, 1f, 1f, 1f),          // 시안
        new Color(1f, 0.5f, 0f, 1f),        // 주황
        new Color(0f, 0f, 0f, 1f),          // 검정
    };

    // ── 슬라이더 ─────────────────────────────────────────────────
    [Header("슬라이더")]
    [Tooltip("선 길이 (1~20)")]
    [SerializeField] private Slider lineLengthSlider;
    [Tooltip("선 두께 (1~6)")]
    [SerializeField] private Slider lineThicknessSlider;
    [Tooltip("중심 간격 (0~20)")]
    [SerializeField] private Slider gapSlider;
    [Tooltip("점 크기 (1~6)")]
    [SerializeField] private Slider dotSizeSlider;
    [Tooltip("외곽선 두께 (1~3)")]
    [SerializeField] private Slider outlineSlider;
    [Tooltip("동적 확장 크기 (1~15)")]
    [SerializeField] private Slider dynamicAmountSlider;

    // ── 토글 ─────────────────────────────────────────────────────
    [Header("토글")]
    [Tooltip("조준선 전체 표시 여부")]
    [SerializeField] private Toggle crosshairToggle;
    [Tooltip("십자선(4개 선) 표시 여부")]
    [SerializeField] private Toggle linesToggle;
    [Tooltip("중심 점 표시 여부")]
    [SerializeField] private Toggle dotToggle;
    [Tooltip("외곽선 표시 여부")]
    [SerializeField] private Toggle outlineToggle;
    [Tooltip("T자 모양 (윗선 제거)")]
    [SerializeField] private Toggle tShapeToggle;
    [Tooltip("사격 시 벌어짐")]
    [SerializeField] private Toggle dynamicFireToggle;
    [Tooltip("이동 시 벌어짐")]
    [SerializeField] private Toggle dynamicMoveToggle;

    // ── 라벨 (슬라이더 값 표시) ──────────────────────────────────
    [Header("라벨")]
    [SerializeField] private TMP_Text lineLengthLabel;
    [SerializeField] private TMP_Text lineThicknessLabel;
    [SerializeField] private TMP_Text gapLabel;
    [SerializeField] private TMP_Text dotSizeLabel;
    [SerializeField] private TMP_Text outlineLabel;
    [SerializeField] private TMP_Text dynamicAmountLabel;

    // ── 내부 상태 ────────────────────────────────────────────────
    private CrosshairSettings settings;

    /// <summary>LoadSettingsToUI 중 콜백 무시 플래그. 초기화 시 꼬임 방지.</summary>
    private bool isUpdating;

    // ===================================================================
    //  Unity 라이프사이클
    // ===================================================================

    void Start()
    {
        settings = CrosshairSettings.Load();
        LoadSettingsToUI();
        BindListeners();
    }

    // ===================================================================
    //  초기화
    // ===================================================================

    /// <summary>슬라이더/토글에 현재 설정값을 반영하고 라벨을 갱신.</summary>
    private void LoadSettingsToUI()
    {
        // 값 세팅 중 OnSettingChanged 콜백 무시
        isUpdating = true;

        // 슬라이더: 범위 + 정수 단위 + 현재 값
        ConfigureSlider(lineLengthSlider,    1, 20, settings.lineLength);
        ConfigureSlider(lineThicknessSlider, 1, 6,  settings.lineThickness);
        ConfigureSlider(gapSlider,           0, 20, settings.gap);
        ConfigureSlider(dotSizeSlider,       1, 6,  settings.dotSize);
        ConfigureSlider(outlineSlider,       1, 3,  settings.outlineThickness);
        ConfigureSlider(dynamicAmountSlider, 1, 15, settings.dynamicAmount);

        // 토글: 현재 값
        SafeSetToggle(crosshairToggle,   settings.showCrosshair);
        SafeSetToggle(linesToggle,       settings.showLines);
        SafeSetToggle(dotToggle,         settings.showDot);
        SafeSetToggle(outlineToggle,     settings.showOutline);
        SafeSetToggle(tShapeToggle,      settings.tShape);
        SafeSetToggle(dynamicFireToggle, settings.dynamicOnFire);
        SafeSetToggle(dynamicMoveToggle, settings.dynamicOnMove);

        // 모든 UI 값 세팅 완료 후 한 번만 갱신
        isUpdating = false;
        UpdateAllLabels();
        SyncPreview();
    }

    /// <summary>모든 UI 이벤트를 OnSettingChanged에 바인딩.</summary>
    private void BindListeners()
    {
        // 슬라이더
        BindSliderEvent(lineLengthSlider);
        BindSliderEvent(lineThicknessSlider);
        BindSliderEvent(gapSlider);
        BindSliderEvent(dotSizeSlider);
        BindSliderEvent(outlineSlider);
        BindSliderEvent(dynamicAmountSlider);

        // 토글
        BindToggleEvent(crosshairToggle);
        BindToggleEvent(linesToggle);
        BindToggleEvent(dotToggle);
        BindToggleEvent(outlineToggle);
        BindToggleEvent(tShapeToggle);
        BindToggleEvent(dynamicFireToggle);
        BindToggleEvent(dynamicMoveToggle);

        // 색상 프리셋 버튼
        if (colorButtons != null)
        {
            for (int i = 0; i < colorButtons.Length; i++)
            {
                int idx = i; // 클로저 캡처
                if (colorButtons[i] != null)
                {
                    colorButtons[i].onClick.AddListener(() =>
                    {
                        if (idx < colorPresets.Length)
                        {
                            settings.SetColor(colorPresets[idx]);
                            UpdateAllLabels();
                            SyncPreview();
                        }
                    });
                }
            }
        }
    }

    // ===================================================================
    //  값 변경 핸들러
    // ===================================================================

    /// <summary>
    /// 슬라이더/토글 값이 변경될 때 호출.
    /// UI → settings 동기화 → 라벨 갱신 → 프리뷰 갱신.
    /// </summary>
    private void OnSettingChanged()
    {
        // LoadSettingsToUI 중에는 콜백 무시 (꼬임 방지)
        if (isUpdating) return;

        // 슬라이더 → settings
        if (lineLengthSlider != null)    settings.lineLength       = lineLengthSlider.value;
        if (lineThicknessSlider != null) settings.lineThickness    = lineThicknessSlider.value;
        if (gapSlider != null)           settings.gap              = gapSlider.value;
        if (dotSizeSlider != null)       settings.dotSize          = dotSizeSlider.value;
        if (outlineSlider != null)       settings.outlineThickness = outlineSlider.value;
        if (dynamicAmountSlider != null) settings.dynamicAmount    = dynamicAmountSlider.value;

        // 토글 → settings
        if (crosshairToggle != null)   settings.showCrosshair = crosshairToggle.isOn;
        if (linesToggle != null)       settings.showLines     = linesToggle.isOn;
        if (dotToggle != null)         settings.showDot       = dotToggle.isOn;
        if (outlineToggle != null)     settings.showOutline   = outlineToggle.isOn;
        if (tShapeToggle != null)      settings.tShape        = tShapeToggle.isOn;
        if (dynamicFireToggle != null) settings.dynamicOnFire = dynamicFireToggle.isOn;
        if (dynamicMoveToggle != null) settings.dynamicOnMove = dynamicMoveToggle.isOn;

        UpdateAllLabels();
        SyncPreview();
    }

    // ===================================================================
    //  라벨 갱신
    // ===================================================================

    /// <summary>슬라이더 옆 라벨에 현재 값 표시.</summary>
    private void UpdateAllLabels()
    {
        SafeSetLabel(lineLengthLabel,    settings.lineLength,       "선 길이");
        SafeSetLabel(lineThicknessLabel, settings.lineThickness,    "선 두께");
        SafeSetLabel(gapLabel,           settings.gap,              "간격");
        SafeSetLabel(dotSizeLabel,       settings.dotSize,          "점 크기");
        SafeSetLabel(outlineLabel,       settings.outlineThickness, "외곽선");
        SafeSetLabel(dynamicAmountLabel, settings.dynamicAmount,    "사격/이동 확장량");
    }

    // ===================================================================
    //  프리뷰 갱신
    // ===================================================================

    /// <summary>CrosshairRenderer 프리뷰에 현재 설정을 실시간 반영.</summary>
    private void SyncPreview()
    {
        if (previewRenderer != null)
            previewRenderer.SetSettings(settings);
    }

    // ===================================================================
    //  공개 API — LobbyManager에서 호출
    // ===================================================================

    /// <summary>현재 설정을 PlayerPrefs에 저장. LobbyManager의 Back 버튼에서 호출.</summary>
    public void SaveSettings()
    {
        if (settings != null)
            settings.Save();
    }

    /// <summary>모든 설정을 기본값으로 초기화.</summary>
    public void ResetToDefault()
    {
        settings = new CrosshairSettings();
        LoadSettingsToUI();
    }

    // ===================================================================
    //  내부 유틸 — DRY 헬퍼
    // ===================================================================

    /// <summary>슬라이더 초기 설정: 범위 + 정수 단위 + 값.</summary>
    private void ConfigureSlider(Slider slider, float min, float max, float value)
    {
        if (slider == null) return;
        slider.minValue     = min;
        slider.maxValue     = max;
        slider.wholeNumbers = true;
        slider.value        = value;
    }

    /// <summary>토글 값 설정 (null-safe).</summary>
    private void SafeSetToggle(Toggle toggle, bool value)
    {
        if (toggle != null) toggle.isOn = value;
    }

    /// <summary>라벨에 "이름: 값" 포맷으로 텍스트 설정 (null-safe).</summary>
    private void SafeSetLabel(TMP_Text label, float value, string name)
    {
        if (label != null) label.text = name + ": " + value.ToString("F0");
    }

    /// <summary>슬라이더 onValueChanged에 OnSettingChanged 바인딩.</summary>
    private void BindSliderEvent(Slider slider)
    {
        if (slider != null)
            slider.onValueChanged.AddListener(_ => OnSettingChanged());
    }

    /// <summary>토글 onValueChanged에 OnSettingChanged 바인딩.</summary>
    private void BindToggleEvent(Toggle toggle)
    {
        if (toggle != null)
            toggle.onValueChanged.AddListener(_ => OnSettingChanged());
    }
}
