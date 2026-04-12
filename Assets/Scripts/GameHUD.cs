using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 인게임 HUD 통합 스크립트.
///
/// [기존 기능] 탄창, 체력 표시 (isDirty 패턴)
/// [추가 기능]
/// - 킬 피드 (우상단, 4초 후 자동 소멸)
/// - 점수판 (Tab 키 토글, 킬/데스 순위)
/// - 라운드 타이머 (상단 중앙)
/// - 카운트다운 / 게임 오버 텍스트
///
/// [설계 원칙]
/// - HUD는 '표시만' 담당. 게임 로직 변경은 각 매니저가 한다. (SRP)
/// - 매 프레임 갱신하지 않고 값이 바뀔 때만 갱신하는 방식(isDirty).
/// - ScoreManager.OnKillEvent를 구독하여 킬 피드 자동 표시.
/// - GameManager.OnStateChanged를 구독하여 화면 전환.
/// </summary>
public class GameHUD : MonoBehaviour
{
    [Header("탄창 HUD")]
    [Tooltip("탄창 수를 표시할 TextMeshPro 컴포넌트")]
    public TMP_Text ammoText;

    [Header("체력 HUD")]
    [Tooltip("체력 수치를 표시할 TextMeshPro 컴포넌트")]
    public TMP_Text hpText;
    [Tooltip("체력 슬라이더 (선택). 비워두면 텍스트만 표시")]
    public Slider hpSlider;

    [Header("연결 대상")]
    [Tooltip("플레이어 오브젝트의 PlayerShooter 컴포넌트")]
    public PlayerShooter playerShooter;
    [Tooltip("플레이어 오브젝트의 MonkeyHealth 컴포넌트")]
    public MonkeyHealth playerHealth;

    [Header("킬 피드")]
    [Tooltip("킬 피드 메시지를 담을 부모 패널 (Vertical Layout Group)")]
    public Transform killFeedPanel;
    [Tooltip("킬 피드용 TMP 프리팹")]
    public GameObject killFeedEntryPrefab;
    [Tooltip("킬 피드 최대 표시 개수")]
    public int maxKillFeedEntries = 5;
    [Tooltip("킬 피드 자동 소멸 시간 (초)")]
    public float killFeedDuration = 4f;

    [Header("점수판")]
    [Tooltip("점수판 패널 (Tab 키로 토글)")]
    public GameObject scoreboardPanel;
    [Tooltip("점수판 항목을 담을 부모 (Vertical Layout Group)")]
    public Transform scoreboardContent;
    [Tooltip("점수판 항목용 TMP 프리팹")]
    public GameObject scoreboardEntryPrefab;

    [Header("라운드 타이머")]
    [Tooltip("남은 시간 표시 텍스트 (상단 중앙)")]
    public TMP_Text timerText;

    [Header("상태 표시")]
    [Tooltip("카운트다운 / 게임 오버 등 큰 텍스트")]
    public TMP_Text stateText;

    // isDirty 패턴: 이전 값과 현재 값이 다를 때만 UI 텍스트를 갱신
    private int lastAmmo   = -1;
    private int lastHp     = -1;
    private float lastTime = -1f;

    // 킬 피드 큐
    private Queue<GameObject> killFeedQueue = new Queue<GameObject>();

    void Start()
    {
        // HP 슬라이더 초기 범위 설정
        if (hpSlider != null && playerHealth != null)
        {
            hpSlider.minValue = 0;
            hpSlider.maxValue = playerHealth.maxHp;
            hpSlider.value    = playerHealth.maxHp;
        }

        // 점수판 기본 숨김
        if (scoreboardPanel != null)
            scoreboardPanel.SetActive(false);

        // 상태 텍스트 기본 숨김
        if (stateText != null)
            stateText.gameObject.SetActive(false);

        // ── 이벤트 구독 ──────────────────────────────────────────
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnKillEvent += OnKillEvent;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnStateChanged += OnGameStateChanged;
            GameManager.Instance.OnGameOver += OnGameOver;

            // ★ 핵심: GameManager.Start()가 먼저 실행됐을 수 있으므로
            //   현재 상태를 직접 동기화 (이벤트를 놓쳐도 안전)
            SyncToCurrentState(GameManager.Instance.CurrentState);
        }
        else
        {
            // GameManager가 없으면 기본 HUD만 표시
            SetHudVisible(true);
        }
    }

    void OnDestroy()
    {
        // 이벤트 구독 해제 (메모리 누수 방지)
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnKillEvent -= OnKillEvent;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnStateChanged -= OnGameStateChanged;
            GameManager.Instance.OnGameOver -= OnGameOver;
        }
    }

    void Update()
    {
        RefreshAmmo();
        RefreshHp();
        RefreshTimer();
        HandleScoreboardToggle();
    }

    // ── 탄창 갱신 ─────────────────────────────────────────────────

    void RefreshAmmo()
    {
        if (playerShooter == null || ammoText == null) return;

        int current = playerShooter.CurrentAmmo;
        if (current == lastAmmo) return; // 변화 없으면 스킵

        lastAmmo = current;
        ammoText.text = current + " / " + playerShooter.MaxAmmo;

        // 탄창이 5발 이하면 빨간색으로 경고
        ammoText.color = current <= 5 ? Color.red : Color.white;
    }

    // ── 체력 갱신 ─────────────────────────────────────────────────

    void RefreshHp()
    {
        if (playerHealth == null) return;

        int current = playerHealth.CurrentHp;
        if (current == lastHp) return; // 변화 없으면 스킵

        lastHp = current;

        if (hpText != null)
        {
            hpText.text = "HP  " + current + " / " + playerHealth.maxHp;

            float ratio = (float)current / playerHealth.maxHp;
            hpText.color = ratio > 0.5f ? Color.green
                         : ratio > 0.25f ? Color.yellow
                         : Color.red;
        }

        if (hpSlider != null)
            hpSlider.value = current;
    }

    // ── 라운드 타이머 갱신 ────────────────────────────────────────

    void RefreshTimer()
    {
        if (timerText == null || GameManager.Instance == null) return;
        if (GameManager.Instance.CurrentState != GameManager.GameState.Playing) return;

        // 1초 단위로만 갱신 (소수점 변환 GC 방지)
        float current = Mathf.Ceil(GameManager.Instance.RemainingTime);
        if (Mathf.Approximately(current, lastTime)) return;

        lastTime = current;
        timerText.text = GameManager.Instance.GetFormattedTime();

        // 30초 이하면 빨간색 경고
        timerText.color = current <= 30f ? Color.red : Color.white;
    }

    // ── 킬 피드 ──────────────────────────────────────────────────

    void OnKillEvent(string killer, string victim, bool isHeadshot)
    {
        if (killFeedPanel == null || killFeedEntryPrefab == null) return;

        // 프리팹 인스턴스 생성 (프리팹이 비활성이므로 복사 후 활성화 필수)
        GameObject entry = Instantiate(killFeedEntryPrefab, killFeedPanel);
        entry.SetActive(true);
        var tmp = entry.GetComponent<TMP_Text>();
        if (tmp != null)
        {
            string headshot = isHeadshot ? " [HEADSHOT]" : "";
            tmp.text = killer + " >> " + victim + headshot;
        }

        killFeedQueue.Enqueue(entry);

        // 최대 개수 초과 시 가장 오래된 항목 제거
        while (killFeedQueue.Count > maxKillFeedEntries)
        {
            var old = killFeedQueue.Dequeue();
            if (old != null) Destroy(old);
        }

        // 일정 시간 후 자동 소멸
        Destroy(entry, killFeedDuration);
    }

    // ── 점수판 (Tab 토글) ────────────────────────────────────────

    void HandleScoreboardToggle()
    {
        if (scoreboardPanel == null) return;

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            bool show = !scoreboardPanel.activeSelf;
            scoreboardPanel.SetActive(show);
            if (show) RefreshScoreboard();
        }
    }

    void RefreshScoreboard()
    {
        if (scoreboardContent == null || scoreboardEntryPrefab == null) return;
        if (ScoreManager.Instance == null) return;

        // 기존 항목 제거
        foreach (Transform child in scoreboardContent)
        {
            Destroy(child.gameObject);
        }

        // 순위 데이터 가져오기
        var ranking = ScoreManager.Instance.GetRanking();

        for (int i = 0; i < ranking.Count; i++)
        {
            var score = ranking[i];
            GameObject entry = Instantiate(scoreboardEntryPrefab, scoreboardContent);
            entry.SetActive(true);
            var tmp = entry.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                string rank = "#" + (i + 1);
                tmp.text = rank + "  " + score.playerName
                         + "  |  K: " + score.kills
                         + "  D: " + score.deaths;
            }
        }
    }

    // ── 게임 상태 변경 이벤트 핸들러 ─────────────────────────────

    /// <summary>
    /// 이벤트로 호출됨. SyncToCurrentState에 위임.
    /// </summary>
    void OnGameStateChanged(GameManager.GameState newState)
    {
        SyncToCurrentState(newState);
    }

    /// <summary>
    /// 주어진 게임 상태에 맞게 HUD 전체를 동기화.
    /// Start()에서 직접 호출하거나, 이벤트 핸들러에서 호출.
    /// </summary>
    void SyncToCurrentState(GameManager.GameState state)
    {
        switch (state)
        {
            case GameManager.GameState.Waiting:
                SetHudVisible(false);
                HideStateText();
                if (timerText != null) timerText.gameObject.SetActive(false);
                break;

            case GameManager.GameState.Countdown:
                SetHudVisible(true);          // HP, Ammo 표시
                ShowStateText("READY...");
                if (timerText != null) timerText.gameObject.SetActive(false);

                // 이전 라운드 UI 정리
                if (scoreboardPanel != null) scoreboardPanel.SetActive(false);
                ClearKillFeed();
                ForceRefresh();
                break;

            case GameManager.GameState.Playing:
                SetHudVisible(true);          // HP, Ammo 표시
                HideStateText();
                if (timerText != null) timerText.gameObject.SetActive(true);
                ForceRefresh();
                break;

            case GameManager.GameState.GameOver:
                SetHudVisible(false);         // HP, Ammo 숨김
                if (timerText != null) timerText.gameObject.SetActive(false);
                // OnGameOver에서 결과 텍스트 표시
                break;
        }
    }

    void OnGameOver(string winnerName)
    {
        ShowStateText("GAME OVER!\n#1: " + winnerName);

        // 점수판 자동 표시
        if (scoreboardPanel != null)
        {
            scoreboardPanel.SetActive(true);
            RefreshScoreboard();
        }
    }

    // ── 상태 텍스트 유틸리티 ─────────────────────────────────────

    void ShowStateText(string message)
    {
        if (stateText == null) return;
        stateText.gameObject.SetActive(true);
        stateText.text = message;
    }

    void HideStateText()
    {
        if (stateText == null) return;
        stateText.gameObject.SetActive(false);
    }

    // ── 강제 갱신 (Start, 리스폰 등에서 호출용) ──────────────────

    /// <summary>
    /// isDirty 캐시를 초기화해서 다음 Update에서 즉시 UI를 갱신하도록 한다.
    /// </summary>
    public void ForceRefresh()
    {
        lastAmmo = -1;
        lastHp   = -1;
        lastTime = -1f;
    }

    /// <summary>
    /// 킬 피드 큐의 모든 항목을 파괴한다. 라운드 전환 시 호출.
    /// </summary>
    void ClearKillFeed()
    {
        while (killFeedQueue.Count > 0)
        {
            var entry = killFeedQueue.Dequeue();
            if (entry != null) Destroy(entry);
        }
    }

    /// <summary>
    /// HP, Ammo 등 인게임 HUD 요소의 가시성을 일괄 제어.
    /// Countdown/Playing에서 true, Waiting/GameOver에서 false.
    /// </summary>
    void SetHudVisible(bool visible)
    {
        if (ammoText != null)
            ammoText.gameObject.SetActive(visible && playerShooter != null);

        if (hpText != null)
            hpText.gameObject.SetActive(visible && playerHealth != null);

        if (hpSlider != null)
            hpSlider.gameObject.SetActive(visible && playerHealth != null);
    }
}
