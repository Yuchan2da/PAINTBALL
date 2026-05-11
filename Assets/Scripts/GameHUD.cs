using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
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
    public static GameHUD Instance { get; private set; }

    [Header("탄창 HUD (레거시 — 비활성 폴백용)")]
    [Tooltip("탄창 수를 표시할 TextMeshPro 컴포넌트 (레거시)")]
    public TMP_Text ammoText;

    [Header("탄창 HUD — 총알 아이콘")]
    [Tooltip("총알 아이콘들의 부모 컨테이너 (HorizontalLayoutGroup)")]
    public Transform ammoContainer;
    [Tooltip("재장전 중 표시 텍스트")]
    public TMP_Text reloadText;

    [Header("체력 HUD (레거시 — 비활성 폴백용)")]
    [Tooltip("체력 수치를 표시할 TextMeshPro 컴포넌트 (레거시)")]
    public TMP_Text hpText;
    [Tooltip("체력 슬라이더 (레거시)")]
    public Slider hpSlider;

    [Header("체력 HUD — HP 바")]
    [Tooltip("메인 HP 바 (Image.fillAmount 사용)")]
    public Image hpBarFill;
    [Tooltip("데미지 잔상 바 (천천히 따라옴)")]
    public Image hpBarDamage;
    [Tooltip("HP 바 위 숫자 표시")]
    public TMP_Text hpBarLabel;

    [Header("연결 대상 (런타임 자동 등록)")]
    [Tooltip("로컬 플레이어의 PlayerShooter. RegisterLocalPlayer()로 자동 설정됨")]
    public PlayerShooter playerShooter;
    [Tooltip("로컬 플레이어의 MonkeyHealth. RegisterLocalPlayer()로 자동 설정됨")]
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

    [Header("크로스헤어")]
    [Tooltip("인게임 크로스헤어 렌더러")]
    public CrosshairRenderer crosshairRenderer;

    [Header("수류탄 HUD")]
    [Tooltip("수류탄 잔여 개수 텍스트")]
    public TMP_Text grenadeCountText;
    [Tooltip("수류탄 아이콘 오브젝트")]
    public GameObject grenadeIcon;

    // isDirty 패턴: 이전 값과 현재 값이 다를 때만 UI 텍스트를 갱신
    private int lastAmmo   = -1;
    private int lastHp     = -1;
    private float lastTime = -1f;

    // 총알 아이콘 캐시
    private Image[] bulletIcons;

    // HP 바 데미지 잔상 추적
    private float damageBarTarget = 1f;

    // 킬 피드 큐
    private Queue<GameObject> killFeedQueue = new Queue<GameObject>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── 수류탄 HUD ──────────────────────────────────────────────────────

    /// <summary>
    /// 수류탄 잔여 개수 UI를 갱신한다.
    /// </summary>
    public void UpdateGrenadeCount(int count)
    {
        if (grenadeCountText != null)
            grenadeCountText.text = count.ToString();
        if (grenadeIcon != null)
            grenadeIcon.SetActive(count > 0);
    }

    // ─── GameOver 표시 상태 추적 ────────────────────────────────
    private bool gameOverShown;

    void Start()
    {
        // HP 슬라이더 초기 범위 설정 (레거시)
        if (hpSlider != null && playerHealth != null)
        {
            hpSlider.minValue = 0;
            hpSlider.maxValue = playerHealth.maxHp;
            hpSlider.value    = playerHealth.maxHp;
        }

        // 총알 아이콘 캐싱
        CacheBulletIcons();

        // 점수판 기본 숨김
        if (scoreboardPanel != null)
            scoreboardPanel.SetActive(false);

        // 상태 텍스트 기본 숨김
        if (stateText != null)
            stateText.gameObject.SetActive(false);

        // ── 이벤트 구독 (코루틴으로 안전하게) ─────────────────────
        StartCoroutine(SubscribeToManagers());
    }

    /// <summary>
    /// GameManager와 ScoreManager가 준비될 때까지 기다려서 이벤트를 구독한다.
    /// Start() 실행 순서에 따라 아직 Instance가 null일 수 있으므로
    /// 최대 1초까지 매 프레임 재시도한다.
    /// </summary>
    IEnumerator SubscribeToManagers()
    {
        // ScoreManager 구독
        float waited = 0f;
        while (ScoreManager.Instance == null && waited < 1f)
        {
            yield return null;
            waited += Time.deltaTime;
        }
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnKillEvent += OnKillEvent;

        // GameManager 구독 (가장 중요!)
        waited = 0f;
        while (GameManager.Instance == null && waited < 1f)
        {
            yield return null;
            waited += Time.deltaTime;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnStateChanged += OnGameStateChanged;
            GameManager.Instance.OnGameOver += OnGameOver;

            // ★ 핵심: GameManager.Start()가 먼저 실행됐을 수 있으므로
            //   현재 상태를 직접 동기화 (이벤트를 놓쳐도 안전)
            SyncToCurrentState(GameManager.Instance.CurrentState);
            Debug.Log("[GameHUD] GameManager 이벤트 구독 성공. 현재 상태: " + GameManager.Instance.CurrentState);
        }
        else
        {
            Debug.LogWarning("[GameHUD] GameManager.Instance를 찾지 못함! 기본 HUD만 표시.");
            SetHudVisible(true);
        }
    }

    /// <summary>
    /// 로컬 플레이어가 런타임에 스폰될 때 자기 자신을 HUD에 등록한다.
    /// Photon에서 PhotonNetwork.Instantiate() 후 로컬 플레이어가 호출.
    /// </summary>
    public void RegisterLocalPlayer(PlayerShooter shooter, MonkeyHealth health)
    {
        playerShooter = shooter;
        playerHealth = health;

        // HP 슬라이더 초기화 (레거시)
        if (hpSlider != null && playerHealth != null)
        {
            hpSlider.minValue = 0;
            hpSlider.maxValue = playerHealth.maxHp;
            hpSlider.value    = playerHealth.maxHp;
        }

        // 총알 아이콘 캐싱 (Start보다 늦게 호출될 수 있음)
        if (bulletIcons == null || bulletIcons.Length == 0)
            CacheBulletIcons();

        // HP 바 초기화
        if (hpBarFill != null) hpBarFill.fillAmount = 1f;
        if (hpBarDamage != null) hpBarDamage.fillAmount = 1f;
        damageBarTarget = 1f;

        ForceRefresh();
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
        RefreshCrosshairMovement();

        // ── GameOver 안전장치 ─────────────────────────────────────
        // 이벤트를 놓쳤거나 구독이 늦어서 GameOver 결과가 안 보이는 경우 보정
        if (!gameOverShown
            && GameManager.Instance != null
            && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
        {
            Debug.Log("[GameHUD] GameOver 안전장치 발동 — 결과 화면 강제 표시");
            SyncToCurrentState(GameManager.GameState.GameOver);
            ForceShowGameOverResult();
            gameOverShown = true;
        }
    }

    // ── 총알 아이콘 캐싱 ───────────────────────────────────────────

    void CacheBulletIcons()
    {
        if (ammoContainer == null) return;

        int count = ammoContainer.childCount;
        bulletIcons = new Image[count];
        for (int i = 0; i < count; i++)
        {
            bulletIcons[i] = ammoContainer.GetChild(i).GetComponent<Image>();
        }
    }

    // ── 탄창 갱신 (총알 아이콘) ────────────────────────────────────

    void RefreshAmmo()
    {
        if (playerShooter == null) return;

        // 재장전 중 표시
        if (playerShooter.IsReloading)
        {
            if (lastAmmo != -2)
            {
                lastAmmo = -2;
                // 총알 아이콘 전부 반투명
                if (bulletIcons != null)
                {
                    for (int i = 0; i < bulletIcons.Length; i++)
                    {
                        if (bulletIcons[i] != null)
                            bulletIcons[i].color = new Color(0.3f, 0.85f, 1f, 0.15f);
                    }
                }
                if (reloadText != null) reloadText.gameObject.SetActive(true);
            }
            return;
        }

        int current = playerShooter.CurrentAmmo;
        if (current == lastAmmo) return;

        lastAmmo = current;
        if (reloadText != null) reloadText.gameObject.SetActive(false);

        // 총알 아이콘 표시/숨김
        if (bulletIcons != null)
        {
            for (int i = 0; i < bulletIcons.Length; i++)
            {
                if (bulletIcons[i] == null) continue;

                if (i < current)
                {
                    // 남은 총알: 밝게
                    bulletIcons[i].color = current <= 5
                        ? new Color(1f, 0.3f, 0.2f, 0.95f) // 빨간색 경고
                        : new Color(0.3f, 0.85f, 1f, 0.95f); // 하늘색
                }
                else
                {
                    // 소모된 총알: 거의 투명
                    bulletIcons[i].color = new Color(0.3f, 0.3f, 0.3f, 0.15f);
                }
            }
        }

        // 레거시 텍스트 폴백
        if (ammoText != null && ammoText.gameObject.activeInHierarchy)
        {
            ammoText.text = current + " / " + playerShooter.MaxAmmo;
            ammoText.color = current <= 5 ? Color.red : Color.white;
        }
    }

    // ── 체력 갱신 (HP 바) ──────────────────────────────────────────

    void RefreshHp()
    {
        if (playerHealth == null) return;

        int current = playerHealth.CurrentHp;
        float ratio = (float)current / playerHealth.maxHp;

        // ── HP 바 갱신 ──
        if (hpBarFill != null)
        {
            hpBarFill.fillAmount = ratio;

            // 초록 → 노랑 → 빨강 그라데이션
            if (ratio > 0.5f)
                hpBarFill.color = Color.Lerp(Color.yellow, new Color(0.2f, 0.9f, 0.3f), (ratio - 0.5f) * 2f);
            else
                hpBarFill.color = Color.Lerp(Color.red, Color.yellow, ratio * 2f);
        }

        // ── 데미지 잔상 바 (빨간 바가 천천히 따라옴) ──
        if (hpBarDamage != null)
        {
            if (ratio < damageBarTarget)
                damageBarTarget = ratio; // 타겟 갱신 (HP 감소 시)

            // 아직 잔상이 메인보다 크면 천천히 줄임
            if (hpBarDamage.fillAmount > ratio)
                hpBarDamage.fillAmount = Mathf.Lerp(hpBarDamage.fillAmount, ratio, Time.deltaTime * 3f);
            else
                hpBarDamage.fillAmount = ratio;
        }

        // ── HP 라벨 ──
        if (hpBarLabel != null && current != lastHp)
        {
            hpBarLabel.text = current + " / " + playerHealth.maxHp;
        }

        // 레거시 텍스트 폴백
        if (current != lastHp && hpText != null && hpText.gameObject.activeInHierarchy)
        {
            hpText.text = "HP  " + current + " / " + playerHealth.maxHp;
            hpText.color = ratio > 0.5f ? Color.green
                         : ratio > 0.25f ? Color.yellow
                         : Color.red;
        }

        if (hpSlider != null)
            hpSlider.value = current;

        lastHp = current;
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
            // 로컬 플레이어 이름 판별
            string localName = Photon.Pun.PhotonNetwork.IsConnected
                ? Photon.Pun.PhotonNetwork.NickName : "Player";
            bool isMyKill = killer == localName;

            if (isHeadshot)
            {
                // 헤드샷 킬: 빨간 강조
                tmp.text = killer + " >> " + victim + " <color=#FF4444>[HEADSHOT]</color>";
                tmp.color = new Color(1f, 0.7f, 0.7f); // 연한 빨강 기본
            }
            else if (isMyKill)
            {
                // 내 킬: 노란색 강조
                tmp.text = "<color=#FFD700>" + killer + "</color> >> " + victim;
                tmp.color = Color.white;
            }
            else
            {
                tmp.text = killer + " >> " + victim;
                tmp.color = new Color(0.85f, 0.85f, 0.85f); // 기본 연한 회색
            }
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

        // 로컬 플레이어 이름
        string localName = Photon.Pun.PhotonNetwork.IsConnected
            ? Photon.Pun.PhotonNetwork.NickName : "Player";

        // 순위 데이터 가져오기
        var ranking = ScoreManager.Instance.GetRanking();

        for (int i = 0; i < ranking.Count; i++)
        {
            var score = ranking[i];
            GameObject entry = Instantiate(scoreboardEntryPrefab, scoreboardContent);
            entry.SetActive(true);

            bool isMe = score.playerName == localName;

            var tmp = entry.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                string rank = "#" + (i + 1);
                string colorTag = BuildColorTag(score.playerName);

                tmp.text = string.Format("  {0,-4} {1} {2,-18} {3,4}    {4,4}",
                    rank, colorTag, score.playerName, score.kills, score.deaths);

                // 자기 행은 밝은 노란색
                tmp.color = isMe ? new Color(1f, 0.85f, 0.3f) : Color.white;
            }

            // 자기 행 배경 강조
            if (isMe)
            {
                var img = entry.GetComponent<Image>();
                if (img != null)
                    img.color = new Color(1f, 0.85f, 0.3f, 0.12f);
            }
        }
    }

    /// <summary>
    /// 플레이어 이름으로 페인트 색상 태그(TMP Rich Text)를 생성한다.
    /// Photon CustomProperties에서 실제 팀 색상("tc")을 읽어온다.
    /// </summary>
    string BuildColorTag(string playerName)
    {
        Color paintColor = GetPlayerTeamColor(playerName);
        string hex = ColorUtility.ToHtmlStringRGB(paintColor);
        return $"<color=#{hex}>\u25a0</color>";
    }

    /// <summary>
    /// 플레이어 이름으로 실제 팀 색상을 가져온다.
    /// 우선순위: tc(팀컬러 RGBA) → pc(팔레트 인덱스) → 회색 폴백.
    /// </summary>
    Color GetPlayerTeamColor(string playerName)
    {
        var player = FindPhotonPlayer(playerName);
        if (player == null) return Color.gray;

        // 1순위: PlayerShooter가 동기화하는 실제 RGBA
        if (player.CustomProperties.TryGetValue("tc", out object tcVal) && tcVal is float[] c)
            return new Color(c[0], c[1], c[2], c[3]);

        // 2순위: 로비에서 선택한 팔레트 인덱스
        return ColorSelectUI.GetPlayerColor(player);
    }

    /// <summary>
    /// 닉네임으로 Photon Player를 찾는다.
    /// </summary>
    Photon.Realtime.Player FindPhotonPlayer(string nickName)
    {
        if (!Photon.Pun.PhotonNetwork.InRoom) return null;

        foreach (var p in Photon.Pun.PhotonNetwork.PlayerList)
        {
            if (p.NickName == nickName)
                return p;
        }
        return null;
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
                // 카운트다운 중에는 크로스헤어 숨김 (Playing에서 표시)
                if (crosshairRenderer != null)
                    crosshairRenderer.gameObject.SetActive(false);
                ShowStateText("READY...");
                if (timerText != null) timerText.gameObject.SetActive(false);

                // 이전 라운드 UI 정리
                if (scoreboardPanel != null) scoreboardPanel.SetActive(false);
                ClearKillFeed();
                gameOverShown = false; // 다음 라운드용 리셋

                // GameOver에서 옮긴 RectTransform 위치 원복
                ResetUiPositions();

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

                // ★ 피격 오버레이 즉시 제거 (결과 UI가 가려지는 것 방지)
                if (HitScreenEffect.Instance != null)
                    HitScreenEffect.Instance.Clear();

                // OnGameOver에서 결과 텍스트 표시
                break;
        }
    }

    void OnGameOver(string winnerName)
    {
        Debug.Log($"[GameHUD] OnGameOver 호출됨. 승자: {winnerName}");
        gameOverShown = true;

        ShowGameOverUI(winnerName);
    }

    /// <summary>
    /// GameOver 결과 UI를 실제로 표시하는 핵심 메서드.
    /// OnGameOver 이벤트 또는 Update() 안전장치에서 호출.
    /// </summary>
    void ShowGameOverUI(string winnerName)
    {
        // ★ HitScreenEffect 오버레이 제거 —
        //   런타임에 생성된 전체화면 Image가 Canvas 최상위에 있으므로
        //   이것이 StateText/ScoreboardPanel 위에 렌더링되어 UI를 가린다.
        if (HitScreenEffect.Instance != null)
            HitScreenEffect.Instance.Clear();

        ShowStateText("GAME OVER!\n#1: " + winnerName);

        // 승리 텍스트를 화면 상단으로 이동 (스코어보드와 겹침 방지)
        if (stateText != null)
        {
            var rt = stateText.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.5f, 0.85f);
                rt.anchorMax = new Vector2(0.5f, 0.85f);
                rt.anchoredPosition = Vector2.zero;
            }

            // 텍스트 크기 강조
            stateText.fontSize = 48f;
            stateText.fontStyle = FontStyles.Bold;

            // ★ StateText를 Canvas 맨 앞으로 이동 (HitVignetteOverlay 위)
            stateText.transform.SetAsLastSibling();
        }

        // 점수판 자동 표시
        if (scoreboardPanel != null)
        {
            scoreboardPanel.SetActive(true);
            RefreshScoreboard();

            // 스코어보드를 화면 중앙~하단으로 이동
            var sbRt = scoreboardPanel.GetComponent<RectTransform>();
            if (sbRt != null)
            {
                sbRt.anchorMin = new Vector2(0.5f, 0.35f);
                sbRt.anchorMax = new Vector2(0.5f, 0.35f);
                sbRt.anchoredPosition = Vector2.zero;
            }

            // ★ ScoreboardPanel도 Canvas 맨 앞으로 (StateText 바로 아래)
            scoreboardPanel.transform.SetAsLastSibling();
            // StateText를 다시 최상위로 (ScoreboardPanel보다 위)
            if (stateText != null) stateText.transform.SetAsLastSibling();
        }
        else
        {
            Debug.LogWarning("[GameHUD] scoreboardPanel이 null! Inspector에서 연결을 확인하세요.");
        }
    }

    /// <summary>
    /// Update() 안전장치에서 호출. 이벤트를 놓쳤을 때 ScoreManager에서 직접 1등을 조회.
    /// </summary>
    void ForceShowGameOverResult()
    {
        string winnerName = "Unknown";
        if (ScoreManager.Instance != null)
        {
            var ranking = ScoreManager.Instance.GetRanking();
            if (ranking.Count > 0)
                winnerName = ranking[0].playerName;
        }
        ShowGameOverUI(winnerName);
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

    /// <summary>
    /// GameOver에서 옮긴 UI 위치를 화면 중앙으로 원복.
    /// </summary>
    void ResetUiPositions()
    {
        if (stateText != null)
        {
            var rt = stateText.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
            }
        }

        if (scoreboardPanel != null)
        {
            var sbRt = scoreboardPanel.GetComponent<RectTransform>();
            if (sbRt != null)
            {
                sbRt.anchorMin = new Vector2(0.5f, 0.5f);
                sbRt.anchorMax = new Vector2(0.5f, 0.5f);
                sbRt.anchoredPosition = Vector2.zero;
            }
        }
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

        // 리스폰 시 크로스헤어 동적 오프셋 리셋
        if (crosshairRenderer != null)
            crosshairRenderer.ResetDynamic();
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
        // 레거시 텍스트 (항상 비활성 — 새 HUD 사용)
        if (ammoText != null)
            ammoText.gameObject.SetActive(false);

        if (hpText != null)
            hpText.gameObject.SetActive(false);

        if (hpSlider != null)
            hpSlider.gameObject.SetActive(false);

        // ── 새 HUD: visible이면 무조건 보이고, 내용은 Update에서 채움 ──
        if (ammoContainer != null)
            ammoContainer.gameObject.SetActive(visible);

        if (reloadText != null && !visible)
            reloadText.gameObject.SetActive(false);

        // HP 바 컨테이너 (HpBarFill의 최상위 부모 = HpBarContainer)
        if (hpBarFill != null)
        {
            // HpBarFill → HpBarContainer(parent.parent) 안전 접근
            Transform container = hpBarFill.transform.parent;
            if (container != null) container = container.parent;
            if (container != null)
                container.gameObject.SetActive(visible);
        }

        // HP 바 리셋 (부활 시)
        if (visible && playerHealth != null)
        {
            float ratio = (float)playerHealth.CurrentHp / playerHealth.maxHp;
            if (hpBarFill != null) hpBarFill.fillAmount = ratio;
            if (hpBarDamage != null) hpBarDamage.fillAmount = ratio;
            damageBarTarget = ratio;
            lastHp = -1;
            lastAmmo = -1;
        }

        // 크로스헤어 가시성
        if (crosshairRenderer != null)
            crosshairRenderer.gameObject.SetActive(visible);
    }

    // ── 크로스헤어 이동 확장 ──────────────────────────────────────

    /// <summary>
    /// 매 프레임 로컬 플레이어의 수평 속도를 크로스헤어에 전달.
    /// CharacterController.velocity에서 Y축을 제거한 수평 속도를 사용.
    /// </summary>
    void RefreshCrosshairMovement()
    {
        if (crosshairRenderer == null || playerShooter == null) return;

        var cc = playerShooter.GetComponentInParent<CharacterController>();
        if (cc == null) return;

        Vector3 vel = cc.velocity;
        float horizontalSpeed = new Vector3(vel.x, 0f, vel.z).magnitude;
        crosshairRenderer.OnMove(horizontalSpeed);
    }
}
