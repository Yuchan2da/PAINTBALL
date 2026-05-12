using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;

/// <summary>
/// [에디터 전용] PauseMenu UI를 씬에 자동 생성하는 도구.
/// Tools > Create Pause Menu UI 메뉴에서 실행하면 된다.
/// 실행 후 이 스크립트는 삭제해도 무방하다.
/// </summary>
public static class PauseMenuUIBuilder
{
    [MenuItem("Tools/Create Pause Menu UI")]
    public static void Build()
    {
        // ── HUD Canvas 탐색 ────────────────────────────────────────────
        var hudObj = Object.FindObjectOfType<GameHUD>();
        if (hudObj == null) { Debug.LogError("[PauseMenuUIBuilder] GameHUD를 찾을 수 없습니다."); return; }

        var hudCanvas = hudObj.GetComponent<Canvas>() ?? hudObj.GetComponentInParent<Canvas>();
        if (hudCanvas == null) { Debug.LogError("[PauseMenuUIBuilder] HUD Canvas를 찾을 수 없습니다."); return; }

        // 기존 PausePanel이 있으면 삭제 후 재생성
        var existing = hudCanvas.transform.Find("PausePanel");
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
            Debug.Log("[PauseMenuUIBuilder] 기존 PausePanel 삭제 후 재생성");
        }

        // ── PausePanel 루트 (전체화면 반투명 오버레이) ──────────────────
        var pausePanel = CreateRT("PausePanel", hudCanvas.transform);
        StretchFull(pausePanel);
        var ppBg = pausePanel.gameObject.AddComponent<Image>();
        ppBg.color = new Color(0f, 0f, 0f, 0.78f);

        // ── 중앙 카드 패널 ─────────────────────────────────────────────
        var card = CreateRT("Card", pausePanel);
        card.anchoredPosition = Vector2.zero;
        card.sizeDelta        = new Vector2(360f, 300f);
        var cardImg = card.gameObject.AddComponent<Image>();
        cardImg.color = new Color(0.06f, 0.05f, 0.12f, 0.97f);
        var ol = card.gameObject.AddComponent<Outline>();
        ol.effectColor    = new Color(0f, 0.9f, 1f, 0.85f);
        ol.effectDistance = new Vector2(2f, 2f);

        // ── 제목 텍스트 ────────────────────────────────────────────────
        var titleRT = CreateRT("TitleText", card);
        titleRT.anchoredPosition = new Vector2(0f, 100f);
        titleRT.sizeDelta        = new Vector2(340f, 55f);
        var titleTMP = titleRT.gameObject.AddComponent<TextMeshProUGUI>();
        titleTMP.text      = "일시 정지";
        titleTMP.fontSize  = 34f;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.color     = new Color(0f, 0.95f, 1f, 1f);

        // ── 구분선 ─────────────────────────────────────────────────────
        var lineRT = CreateRT("Divider", card);
        lineRT.anchoredPosition = new Vector2(0f, 68f);
        lineRT.sizeDelta        = new Vector2(300f, 2f);
        lineRT.gameObject.AddComponent<Image>().color = new Color(0f, 0.9f, 1f, 0.5f);

        // ── 계속 하기 버튼 (시안) ─────────────────────────────────────
        var resumeBtn = CreateButton(
            "ResumeButton", card,
            new Vector2(0f, 15f), new Vector2(280f, 58f),
            "▶  계속 하기",
            new Color(0f, 0.65f, 0.85f, 1f),
            new Color(0f, 0.85f, 1f, 1f),
            new Color(0f, 0.45f, 0.65f, 1f)
        );

        // ── 로비로 나가기 버튼 (레드) ─────────────────────────────────
        var leaveBtn = CreateButton(
            "LeaveButton", card,
            new Vector2(0f, -58f), new Vector2(280f, 58f),
            "🚪  로비로 나가기",
            new Color(0.75f, 0.1f, 0.2f, 1f),
            new Color(1f, 0.2f, 0.3f, 1f),
            new Color(0.5f, 0.05f, 0.1f, 1f)
        );

        // ── 단축키 힌트 ────────────────────────────────────────────────
        var hintRT = CreateRT("HintText", card);
        hintRT.anchoredPosition = new Vector2(0f, -125f);
        hintRT.sizeDelta        = new Vector2(320f, 28f);
        var hintTMP = hintRT.gameObject.AddComponent<TextMeshProUGUI>();
        hintTMP.text      = "ESC  키로 재개";
        hintTMP.fontSize  = 13f;
        hintTMP.alignment = TextAlignmentOptions.Center;
        hintTMP.color     = new Color(0.6f, 0.6f, 0.7f, 1f);

        // ── PauseMenu 컴포넌트를 HUD 오브젝트에 부착 ──────────────────
        var pauseMenu = hudObj.GetComponent<PauseMenu>() ?? hudObj.gameObject.AddComponent<PauseMenu>();
        pauseMenu.pausePanel  = pausePanel.gameObject;
        pauseMenu.resumeButton = resumeBtn;
        pauseMenu.leaveButton  = leaveBtn;

        // 기본 숨김
        pausePanel.gameObject.SetActive(false);

        Undo.RegisterCreatedObjectUndo(pausePanel.gameObject, "Create PausePanel");
        EditorUtility.SetDirty(hudObj.gameObject);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[PauseMenuUIBuilder] ✅ PausePanel 생성 완료! PauseMenu 컴포넌트가 HUD에 연결되었습니다.");
    }

    // ── 헬퍼 메서드 ───────────────────────────────────────────────────

    static RectTransform CreateRT(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static Button CreateButton(
        string name, RectTransform parent,
        Vector2 pos, Vector2 size,
        string label,
        Color normal, Color hover, Color pressed)
    {
        var rt = CreateRT(name, parent);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        rt.gameObject.AddComponent<Image>().color = normal;

        var btn = rt.gameObject.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = normal;
        colors.highlightedColor = hover;
        colors.pressedColor     = pressed;
        btn.colors = colors;

        // 라벨 텍스트
        var textRT = CreateRT("Text", rt);
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = textRT.offsetMax = Vector2.zero;
        var tmp = textRT.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = 22f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = Color.white;

        return btn;
    }
}
