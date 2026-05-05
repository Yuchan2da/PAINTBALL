using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

/// <summary>
/// 로비 대기실에서 페인트 색상을 선택하는 UI 컨트롤러.
///
/// [역할]
/// - 11색 팔레트를 원형 버튼으로 표시
/// - Photon Custom Properties로 선택 동기화 → 같은 색 중복 불가
/// - 잠긴 색상에 선택자 닉네임 표시
/// - 선택 값을 PlayerPrefs + CP에 저장 → 게임 씬에서 PlayerShooter가 읽어 적용
///
/// [배치]
/// RoomPanel 하위에 ColorSelectPanel로 배치.
/// 11개 버튼은 GridLayoutGroup으로 자동 정렬.
/// </summary>
public class ColorSelectUI : MonoBehaviourPunCallbacks
{
    // ====================================================================
    //  팔레트 정의 — 전체 프로젝트의 단일 색상 소스
    // ====================================================================

    /// <summary>페인트 색상 항목. 이름·색상·총기 스킨 인덱스를 묶는다.</summary>
    [System.Serializable]
    public struct PaintColorEntry
    {
        public string name;
        public Color color;
        public int skinIndex; // FPSGunModel MarkerBody 인덱스
    }

    /// <summary>
    /// 11색 팔레트. MarkerBody 에셋의 스킨 순서와 1:1 대응.
    /// 다른 스크립트에서 ColorSelectUI.Palette[i] 로 접근.
    /// </summary>
    public static readonly PaintColorEntry[] Palette = new PaintColorEntry[]
    {
        new PaintColorEntry { name = "검정",  color = new Color(0.15f, 0.15f, 0.15f, 1f), skinIndex = 0 },
        new PaintColorEntry { name = "파랑",  color = new Color(0.20f, 0.40f, 0.90f, 1f), skinIndex = 1 },
        new PaintColorEntry { name = "초록",  color = new Color(0.10f, 0.80f, 0.30f, 1f), skinIndex = 2 },
        new PaintColorEntry { name = "연두",  color = new Color(0.50f, 0.90f, 0.20f, 1f), skinIndex = 3 },
        new PaintColorEntry { name = "네이비", color = new Color(0.10f, 0.15f, 0.55f, 1f), skinIndex = 4 },
        new PaintColorEntry { name = "주황",  color = new Color(1.00f, 0.55f, 0.05f, 1f), skinIndex = 5 },
        new PaintColorEntry { name = "핑크",  color = new Color(1.00f, 0.40f, 0.70f, 1f), skinIndex = 6 },
        new PaintColorEntry { name = "보라",  color = new Color(0.60f, 0.20f, 0.80f, 1f), skinIndex = 7 },
        new PaintColorEntry { name = "빨강",  color = new Color(0.90f, 0.15f, 0.15f, 1f), skinIndex = 8 },
        new PaintColorEntry { name = "하양",  color = new Color(0.90f, 0.90f, 0.90f, 1f), skinIndex = 9 },
        new PaintColorEntry { name = "골드",  color = new Color(1.00f, 0.84f, 0.00f, 1f), skinIndex = 10 },
    };

    // Photon / PlayerPrefs 키
    public const string PROP_PAINT_COLOR = "pc";
    public const string PREF_PAINT_COLOR = "paint_color_index";

    // ====================================================================
    //  Inspector 연결
    // ====================================================================

    [Header("UI 요소")]
    [Tooltip("색상 버튼이 배치될 부모 Transform (GridLayoutGroup)")]
    [SerializeField] private Transform colorGridParent;

    [Tooltip("현재 선택된 색상 이름 표시")]
    [SerializeField] private TMP_Text selectedColorLabel;



    // ====================================================================
    //  내부 상태
    // ====================================================================

    private int selectedIndex = -1;           // 로컬 플레이어가 선택한 인덱스
    private Button[] colorButtons;            // 생성된 버튼 배열
    private Image[] colorButtonImages;        // 버튼 배경 이미지
    private GameObject[] lockOverlays;        // 잠금 오버레이
    private TMP_Text[] lockNameTexts;         // 선택자 닉네임 텍스트
    private Image[] selectionBorders;         // 선택 테두리

    // ====================================================================
    //  초기화
    // ====================================================================

    private bool initialized;

    void Awake()
    {
        CreateColorButtons();
        CreateCloseButton();

        // 이전 세션의 선택 복원
        int savedIndex = PlayerPrefs.GetInt(PREF_PAINT_COLOR, -1);
        if (savedIndex >= 0 && savedIndex < Palette.Length)
            selectedIndex = savedIndex;

        RefreshAllButtons();
        initialized = true;
    }

    void OnEnable()
    {
        // ★ Photon 콜백 등록 (OnPlayerPropertiesUpdate 등 수신에 필수)
        base.OnEnable();

        // Awake 미실행 시(부모가 비활성 상태에서 시작된 경우) 여기서 초기화
        if (!initialized)
        {
            CreateColorButtons();
            CreateCloseButton();

            int savedIndex = PlayerPrefs.GetInt(PREF_PAINT_COLOR, -1);
            if (savedIndex >= 0 && savedIndex < Palette.Length)
                selectedIndex = savedIndex;

            initialized = true;
        }

        RefreshAllButtons();
    }

    void OnDisable()
    {
        // ★ Photon 콜백 해제
        base.OnDisable();
    }

    // ====================================================================
    //  패널 토글
    // ====================================================================

    /// <summary>패널 열기/닫기 토글.</summary>
    public void TogglePanel()
    {
        bool show = !gameObject.activeSelf;
        gameObject.SetActive(show);

        if (show)
            RefreshAllButtons();
    }

    /// <summary>패널 닫기 (닫기 버튼용).</summary>
    public void ClosePanel()
    {
        gameObject.SetActive(false);
    }

    /// <summary>패널 내 우상단 닫기 버튼 생성.</summary>
    private void CreateCloseButton()
    {
        var closeGo = new GameObject("CloseButton");
        closeGo.transform.SetParent(transform, false);

        var rt = closeGo.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-20f, -20f);
        rt.sizeDelta = new Vector2(30f, 30f);

        var img = closeGo.AddComponent<Image>();
        img.color = new Color(0.8f, 0.2f, 0.2f, 0.9f);

        var btn = closeGo.AddComponent<Button>();
        btn.onClick.AddListener(ClosePanel);

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(closeGo.transform, false);
        var textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = "X";
        tmp.fontSize = 18f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
    }

    // ====================================================================
    //  버튼 생성
    // ====================================================================

    /// <summary>11개 색상 버튼을 런타임에 생성한다.</summary>
    private void CreateColorButtons()
    {
        colorButtons = new Button[Palette.Length];
        colorButtonImages = new Image[Palette.Length];
        lockOverlays = new GameObject[Palette.Length];
        lockNameTexts = new TMP_Text[Palette.Length];
        selectionBorders = new Image[Palette.Length];

        for (int i = 0; i < Palette.Length; i++)
        {
            // 버튼 오브젝트
            var btnGo = new GameObject($"ColorBtn_{i}_{Palette[i].name}");
            btnGo.transform.SetParent(colorGridParent, false);

            // 버튼 크기 설정
            var rt = btnGo.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(70f, 70f);

            // 선택 테두리 (바깥 원)
            var borderGo = new GameObject("Border");
            borderGo.transform.SetParent(btnGo.transform, false);
            var borderRt = borderGo.AddComponent<RectTransform>();
            borderRt.anchorMin = Vector2.zero;
            borderRt.anchorMax = Vector2.one;
            borderRt.offsetMin = new Vector2(-4f, -4f);
            borderRt.offsetMax = new Vector2(4f, 4f);
            var borderImg = borderGo.AddComponent<Image>();
            borderImg.color = Color.white;
            borderImg.raycastTarget = false;
            selectionBorders[i] = borderImg;
            borderGo.SetActive(false);

            // 색상 원 (배경)
            var colorBg = new GameObject("ColorBg");
            colorBg.transform.SetParent(btnGo.transform, false);
            var colorRt = colorBg.AddComponent<RectTransform>();
            colorRt.anchorMin = Vector2.zero;
            colorRt.anchorMax = Vector2.one;
            colorRt.offsetMin = Vector2.zero;
            colorRt.offsetMax = Vector2.zero;
            var bgImg = colorBg.AddComponent<Image>();
            bgImg.color = Palette[i].color;
            bgImg.raycastTarget = false;
            colorButtonImages[i] = bgImg;

            // 잠금 오버레이 (반투명 어두운 레이어)
            var lockGo = new GameObject("LockOverlay");
            lockGo.transform.SetParent(btnGo.transform, false);
            var lockRt = lockGo.AddComponent<RectTransform>();
            lockRt.anchorMin = Vector2.zero;
            lockRt.anchorMax = Vector2.one;
            lockRt.offsetMin = Vector2.zero;
            lockRt.offsetMax = Vector2.zero;
            var lockImg = lockGo.AddComponent<Image>();
            lockImg.color = new Color(0f, 0f, 0f, 0.55f);
            lockImg.raycastTarget = false;
            lockOverlays[i] = lockGo;

            // 잠금 아이콘 + 닉네임
            var lockTextGo = new GameObject("LockText");
            lockTextGo.transform.SetParent(lockGo.transform, false);
            var textRt = lockTextGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            var lockText = lockTextGo.AddComponent<TextMeshProUGUI>();
            lockText.text = "🔒";
            lockText.fontSize = 11f;
            lockText.alignment = TextAlignmentOptions.Center;
            lockText.color = Color.white;
            lockText.raycastTarget = false;
            lockNameTexts[i] = lockText;

            lockGo.SetActive(false);

            // 투명 버튼 (클릭 수신)
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0f, 0f, 0f, 0f); // 완전 투명
            var btn = btnGo.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;

            int index = i; // 클로저 캡처
            btn.onClick.AddListener(() => OnColorButtonClicked(index));
            colorButtons[i] = btn;
        }
    }

    // ====================================================================
    //  버튼 클릭 처리
    // ====================================================================

    /// <summary>색상 버튼 클릭 시 호출.</summary>
    private void OnColorButtonClicked(int index)
    {
        if (!PhotonNetwork.InRoom) return;

        // 이미 다른 사람이 사용 중이면 무시
        if (IsColorTakenByOther(index)) return;

        SelectColor(index);
    }

    /// <summary>색상 선택 확정 → CP + PlayerPrefs 저장.</summary>
    private void SelectColor(int index)
    {
        selectedIndex = index;

        // Photon Custom Properties 저장
        if (PhotonNetwork.InRoom)
        {
            Hashtable props = new Hashtable { { PROP_PAINT_COLOR, index } };
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        }

        // PlayerPrefs 저장 (다음 세션 복원용)
        PlayerPrefs.SetInt(PREF_PAINT_COLOR, index);
        PlayerPrefs.Save();

        RefreshAllButtons();
    }

    // ====================================================================
    //  UI 갱신
    // ====================================================================

    /// <summary>모든 버튼의 잠금/선택 상태를 갱신한다.</summary>
    private void RefreshAllButtons()
    {
        if (colorButtons == null) return;

        // 룸 내 모든 플레이어의 선택 상태를 수집
        // key: 팔레트 인덱스, value: 선택한 플레이어 닉네임
        var takenColors = new System.Collections.Generic.Dictionary<int, string>();

        if (PhotonNetwork.InRoom)
        {
            foreach (var player in PhotonNetwork.PlayerList)
            {
                // ★ 로컬 플레이어는 selectedIndex를 직접 사용
                // (SetCustomProperties의 서버 확인 지연으로 CP가 구 값일 수 있음)
                if (player.IsLocal)
                {
                    if (selectedIndex >= 0 && selectedIndex < Palette.Length)
                        takenColors[selectedIndex] = player.NickName;
                    continue;
                }

                if (player.CustomProperties.TryGetValue(PROP_PAINT_COLOR, out object val))
                {
                    int colorIdx = (int)val;
                    if (colorIdx >= 0 && colorIdx < Palette.Length)
                        takenColors[colorIdx] = player.NickName;
                }
            }
        }

        for (int i = 0; i < Palette.Length; i++)
        {
            bool isMySelection = (i == selectedIndex);
            bool isTaken = takenColors.ContainsKey(i);
            bool isTakenByOther = isTaken && !isMySelection;

            // 선택 테두리: 내가 고른 것만 표시
            if (selectionBorders[i] != null)
                selectionBorders[i].gameObject.SetActive(isMySelection);

            // 잠금 오버레이: 다른 사람이 고른 것만 표시
            if (lockOverlays[i] != null)
            {
                lockOverlays[i].SetActive(isTakenByOther);
                if (isTakenByOther && lockNameTexts[i] != null)
                    lockNameTexts[i].text = $"🔒\n{takenColors[i]}";
            }

            // 버튼 interactable: 다른 사람이 고르면 비활성
            if (colorButtons[i] != null)
                colorButtons[i].interactable = !isTakenByOther;
        }

        // 선택 라벨 갱신
        if (selectedColorLabel != null)
        {
            if (selectedIndex >= 0 && selectedIndex < Palette.Length)
                selectedColorLabel.text = $"내 색상: {Palette[selectedIndex].name}";
            else
                selectedColorLabel.text = "색상을 선택하세요";
        }
    }

    // ====================================================================
    //  중복 체크
    // ====================================================================

    /// <summary>해당 색상이 다른 플레이어에 의해 사용 중인지 확인.</summary>
    private bool IsColorTakenByOther(int index)
    {
        if (!PhotonNetwork.InRoom) return false;

        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (player.IsLocal) continue;
            if (player.CustomProperties.TryGetValue(PROP_PAINT_COLOR, out object val))
            {
                if ((int)val == index) return true;
            }
        }
        return false;
    }

    // ====================================================================
    //  Photon 콜백
    // ====================================================================

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (changedProps.ContainsKey(PROP_PAINT_COLOR))
            RefreshAllButtons();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        RefreshAllButtons();
    }

    public override void OnJoinedRoom()
    {
        // 방 입장 시 저장된 색상 복원 시도
        int savedIndex = PlayerPrefs.GetInt(PREF_PAINT_COLOR, -1);
        if (savedIndex >= 0 && savedIndex < Palette.Length && !IsColorTakenByOther(savedIndex))
            SelectColor(savedIndex);
        else
            RefreshAllButtons();
    }

    // ====================================================================
    //  유틸: 외부에서 선택된 인덱스 읽기
    // ====================================================================

    /// <summary>
    /// 특정 플레이어의 선택된 팔레트 인덱스를 CP에서 읽는다.
    /// 미선택 시 -1 반환.
    /// </summary>
    public static int GetPlayerColorIndex(Player player)
    {
        if (player == null) return -1;
        if (player.CustomProperties.TryGetValue(PROP_PAINT_COLOR, out object val))
            return (int)val;
        return -1;
    }

    /// <summary>
    /// 특정 플레이어의 페인트 Color를 반환. 미선택 시 기본 빨강.
    /// </summary>
    public static Color GetPlayerColor(Player player)
    {
        int idx = GetPlayerColorIndex(player);
        if (idx >= 0 && idx < Palette.Length)
            return Palette[idx].color;
        return Palette[8].color; // 기본: 빨강
    }

    /// <summary>
    /// 특정 플레이어의 MarkerBody 스킨 인덱스를 반환. 미선택 시 기본 Red(8).
    /// </summary>
    public static int GetPlayerSkinIndex(Player player)
    {
        int idx = GetPlayerColorIndex(player);
        if (idx >= 0 && idx < Palette.Length)
            return Palette[idx].skinIndex;
        return 8; // 기본: Red
    }
}
