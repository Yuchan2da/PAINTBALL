using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Photon.Pun;

/// <summary>
/// 호스트 전용 방 설정 패널.
/// LobbyManager의 RoomPanel 안에 오버레이로 배치.
/// ◀ 값 ▶ 방식으로 설정값 조절 후 [적용] 버튼으로 Room Properties에 저장.
///
/// [주의] UI 오브젝트는 코드에서 동적 생성한다.
/// LobbyManager에서 settingsButton만 연결하면 된다.
/// </summary>
public class RoomSettingsUI : MonoBehaviour
{
    // ─── 외부 참조 ────────────────────────────────────────────────
    [Header("설정 패널 루트")]
    [Tooltip("설정 패널 전체 (SetActive로 열고 닫음)")]
    public GameObject settingsPanel;

    [Header("설정 버튼 (호스트만 보임)")]
    [Tooltip("RoomPanel 내 ⚙ 방 설정 버튼")]
    public Button settingsButton;

    // ─── 설정 정의 ────────────────────────────────────────────────

    [System.Serializable]
    struct SettingDef
    {
        public string label;
        public string suffix;          // "초", "개", "" 등
        public float min, max, step;
        public float defaultValue;
        public bool isInt;             // int로 표시할지
    }

    static readonly SettingDef[] Defs = new SettingDef[]
    {
        new SettingDef { label = "라운드 시간",   suffix = "초", min = 60,  max = 600, step = 30, defaultValue = 180, isInt = false },
        new SettingDef { label = "최대 체력",     suffix = "",   min = 50,  max = 300, step = 10, defaultValue = 100, isInt = true  },
        new SettingDef { label = "탄창 크기",     suffix = "발", min = 5,   max = 50,  step = 5,  defaultValue = 15,  isInt = true  },
        new SettingDef { label = "헤드샷 데미지", suffix = "",   min = 5,   max = 100, step = 5,  defaultValue = 20,  isInt = true  },
        new SettingDef { label = "바디샷 데미지", suffix = "",   min = 5,   max = 50,  step = 5,  defaultValue = 10,  isInt = true  },
        new SettingDef { label = "패널티 시간",   suffix = "초", min = 3,   max = 15,  step = 1,  defaultValue = 5,   isInt = false },
        new SettingDef { label = "패널티 데미지", suffix = "",   min = 1,   max = 20,  step = 1,  defaultValue = 5,   isInt = true  },
        new SettingDef { label = "수류탄 개수",   suffix = "개", min = 0,   max = 5,   step = 1,  defaultValue = 1,   isInt = true  },
        new SettingDef { label = "수류탄 시작DPS", suffix = "",   min = 5,   max = 30,  step = 5,  defaultValue = 10,  isInt = false },
    };

    // ─── 런타임 ───────────────────────────────────────────────────
    float[] values;
    TMP_Text[] valueTexts;

    void Awake()
    {
        // 기본값 초기화
        values = new float[Defs.Length];
        for (int i = 0; i < Defs.Length; i++)
            values[i] = Defs[i].defaultValue;

        if (settingsPanel != null)
            settingsPanel.SetActive(false);

        if (settingsButton != null)
            settingsButton.onClick.AddListener(TogglePanel);
    }

    void Start()
    {
        // 호스트가 아니면 설정 버튼 숨기기
        UpdateHostVisibility();

        // 패널 내부 UI 동적 생성
        if (settingsPanel != null)
            BuildSettingsUI();
    }

    /// <summary>
    /// 호스트가 변경되었을 때 (방장 이관 등) 버튼 가시성 갱신.
    /// </summary>
    public void UpdateHostVisibility()
    {
        bool isHost = !PhotonNetwork.IsConnected || PhotonNetwork.IsMasterClient;
        if (settingsButton != null)
            settingsButton.gameObject.SetActive(isHost);
    }

    void TogglePanel()
    {
        if (settingsPanel == null) return;

        bool show = !settingsPanel.activeSelf;
        settingsPanel.SetActive(show);

        if (show) LoadCurrentValues();
    }

    // ─── 설정값 로드 ─────────────────────────────────────────────

    void LoadCurrentValues()
    {
        var s = GameSettings.Current;
        values[0] = s.roundDuration;
        values[1] = s.maxHealth;
        values[2] = s.magazineSize;
        values[3] = s.headshotDamage;
        values[4] = s.bodyshotDamage;
        values[5] = s.idlePenaltyDelay;
        values[6] = s.idlePenaltyDamage;
        values[7] = s.grenadeCount;
        values[8] = s.grenadeDPS;

        RefreshAllTexts();
    }

    // ─── 적용 / 닫기 ─────────────────────────────────────────────

    void ApplySettings()
    {
        var s = GameSettings.Current;
        s.roundDuration     = values[0];
        s.maxHealth         = (int)values[1];
        s.magazineSize      = (int)values[2];
        s.headshotDamage    = (int)values[3];
        s.bodyshotDamage    = (int)values[4];
        s.idlePenaltyDelay  = values[5];
        s.idlePenaltyDamage = (int)values[6];
        s.grenadeCount      = (int)values[7];
        s.grenadeDPS        = values[8];

        s.SaveToRoom();

        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    void ClosePanel()
    {
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    // ─── 값 조절 ─────────────────────────────────────────────────

    void ChangeValue(int index, int direction)
    {
        float v = values[index] + direction * Defs[index].step;
        v = Mathf.Clamp(v, Defs[index].min, Defs[index].max);
        values[index] = v;

        if (valueTexts != null && valueTexts[index] != null)
            valueTexts[index].text = FormatValue(index);
    }

    string FormatValue(int index)
    {
        float v = values[index];
        string num = Defs[index].isInt ? ((int)v).ToString() : v.ToString("0");
        return num + Defs[index].suffix;
    }

    void RefreshAllTexts()
    {
        if (valueTexts == null) return;
        for (int i = 0; i < valueTexts.Length; i++)
        {
            if (valueTexts[i] != null)
                valueTexts[i].text = FormatValue(i);
        }
    }

    // ─── UI 동적 생성 ─────────────────────────────────────────────

    /// <summary>
    /// settingsPanel 내부에 9개 설정 행 + 적용/닫기 버튼을 동적으로 생성한다.
    /// </summary>
    void BuildSettingsUI()
    {
        // 기존 자식 제거
        for (int i = settingsPanel.transform.childCount - 1; i >= 0; i--)
            Destroy(settingsPanel.transform.GetChild(i).gameObject);

        valueTexts = new TMP_Text[Defs.Length];

        // 배경 이미지
        var panelImg = settingsPanel.GetComponent<Image>();
        if (panelImg == null) panelImg = settingsPanel.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.12f, 0.95f);

        // VerticalLayoutGroup
        var vlg = settingsPanel.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = settingsPanel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 15, 15);
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        // ContentSizeFitter
        var csf = settingsPanel.GetComponent<ContentSizeFitter>();
        if (csf == null) csf = settingsPanel.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 타이틀
        CreateTitle(settingsPanel.transform, "⚙ 방 설정");

        // 구분선
        CreateDivider(settingsPanel.transform);

        // 설정 행 생성
        for (int i = 0; i < Defs.Length; i++)
        {
            var row = CreateSettingRow(settingsPanel.transform, i);
        }

        // 구분선
        CreateDivider(settingsPanel.transform);

        // 버튼 행 (적용 + 닫기)
        CreateButtonRow(settingsPanel.transform);
    }

    void CreateTitle(Transform parent, string text)
    {
        var obj = new GameObject("Title", typeof(RectTransform));
        obj.transform.SetParent(parent, false);

        var le = obj.AddComponent<LayoutElement>();
        le.preferredHeight = 40f;

        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 22f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
    }

    void CreateDivider(Transform parent)
    {
        var obj = new GameObject("Divider", typeof(RectTransform));
        obj.transform.SetParent(parent, false);

        var le = obj.AddComponent<LayoutElement>();
        le.preferredHeight = 2f;

        var img = obj.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.15f);
    }

    GameObject CreateSettingRow(Transform parent, int index)
    {
        var row = new GameObject($"Setting_{index}", typeof(RectTransform));
        row.transform.SetParent(parent, false);

        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 35f;

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlHeight = true;
        hlg.childControlWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth = false;

        // 라벨 (왼쪽)
        var labelObj = new GameObject("Label", typeof(RectTransform));
        labelObj.transform.SetParent(row.transform, false);
        var labelLE = labelObj.AddComponent<LayoutElement>();
        labelLE.preferredWidth = 130f;
        labelLE.flexibleWidth = 0f;
        var labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
        labelTmp.text = Defs[index].label;
        labelTmp.fontSize = 16f;
        labelTmp.color = new Color(0.85f, 0.85f, 0.85f);
        labelTmp.alignment = TextAlignmentOptions.MidlineLeft;

        // ◀ 버튼
        CreateArrowButton(row.transform, "◀", index, -1);

        // 값 표시
        var valObj = new GameObject("Value", typeof(RectTransform));
        valObj.transform.SetParent(row.transform, false);
        var valLE = valObj.AddComponent<LayoutElement>();
        valLE.preferredWidth = 80f;
        valLE.flexibleWidth = 0f;
        var valTmp = valObj.AddComponent<TextMeshProUGUI>();
        valTmp.text = FormatValue(index);
        valTmp.fontSize = 18f;
        valTmp.fontStyle = FontStyles.Bold;
        valTmp.color = Color.white;
        valTmp.alignment = TextAlignmentOptions.Center;
        valueTexts[index] = valTmp;

        // ▶ 버튼
        CreateArrowButton(row.transform, "▶", index, 1);

        return row;
    }

    void CreateArrowButton(Transform parent, string label, int settingIndex, int direction)
    {
        var obj = new GameObject(label, typeof(RectTransform));
        obj.transform.SetParent(parent, false);

        var le = obj.AddComponent<LayoutElement>();
        le.preferredWidth = 35f;
        le.flexibleWidth = 0f;

        var img = obj.AddComponent<Image>();
        img.color = new Color(0.3f, 0.3f, 0.35f, 0.8f);

        var btn = obj.AddComponent<Button>();
        btn.targetGraphic = img;

        // 버튼 색상 설정
        var colors = btn.colors;
        colors.normalColor = new Color(0.3f, 0.3f, 0.35f, 0.8f);
        colors.highlightedColor = new Color(0.4f, 0.4f, 0.45f, 0.9f);
        colors.pressedColor = new Color(0.5f, 0.5f, 0.55f, 1f);
        btn.colors = colors;

        int idx = settingIndex;
        int dir = direction;
        btn.onClick.AddListener(() => ChangeValue(idx, dir));

        // 라벨 텍스트
        var txtObj = new GameObject("Text", typeof(RectTransform));
        txtObj.transform.SetParent(obj.transform, false);
        var rt = txtObj.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var tmp = txtObj.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 20f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
    }

    void CreateButtonRow(Transform parent)
    {
        var row = new GameObject("Buttons", typeof(RectTransform));
        row.transform.SetParent(parent, false);

        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 40f;

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 20f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlHeight = true;
        hlg.childControlWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth = true;

        // 적용 버튼
        CreateActionButton(row.transform, "적용", new Color(0.2f, 0.6f, 0.2f, 0.9f), ApplySettings);

        // 닫기 버튼
        CreateActionButton(row.transform, "닫기", new Color(0.5f, 0.2f, 0.2f, 0.9f), ClosePanel);
    }

    void CreateActionButton(Transform parent, string label, Color bgColor, UnityEngine.Events.UnityAction action)
    {
        var obj = new GameObject(label, typeof(RectTransform));
        obj.transform.SetParent(parent, false);

        var img = obj.AddComponent<Image>();
        img.color = bgColor;

        var btn = obj.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(action);

        // 라벨
        var txtObj = new GameObject("Text", typeof(RectTransform));
        txtObj.transform.SetParent(obj.transform, false);
        var rt = txtObj.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var tmp = txtObj.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 18f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
    }
}
