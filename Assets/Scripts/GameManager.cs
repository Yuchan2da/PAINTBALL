using UnityEngine;
using System;
using Photon.Pun;
using ExitGames.Client.Photon;

/// <summary>
/// 게임 흐름 관리 싱글톤 (상태 머신 기반).
///
/// [게임 상태]
/// Waiting     → 대기 중 (플레이어 접속 대기, 로컬에선 즉시 넘어감)
/// Countdown   → 카운트다운 (3초)
/// Playing     → 플레이 중 (라운드 타이머 동작)
/// GameOver    → 게임 종료 (조작 잠금 + 결과 화면)
///
/// [설계 원칙]
/// - 이 스크립트가 게임 전체 라이프사이클을 오케스트레이션한다.
/// - MonkeyHealth, PlayerController 등은 GameManager의 상태를 읽고 자신의 역할만 수행.
/// - 추후 Photon 적용 시 서버가 상태 전이를 통제하고 클라이언트가 따르는 구조로 전환.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // ── 상수 ────────────────────────────────────────────────────────
    private const string PROP_START_TIME = "startTime";

    // ── 게임 상태 enum ──────────────────────────────────────────────
    public enum GameState
    {
        Waiting,    // 대기
        Countdown,  // 카운트다운
        Playing,    // 플레이 중
        GameOver    // 종료
    }

    // ── 설정 ────────────────────────────────────────────────────────
    [Header("라운드 설정")]
    [Tooltip("라운드 플레이 시간 (초)")]
    public float roundDuration = 180f; // 3분

    [Tooltip("카운트다운 시간 (초)")]
    public float countdownDuration = 3f;

    [Tooltip("게임 오버 후 결과 화면 표시 시간 (초)")]
    public float gameOverDisplayDuration = 5f;

    // ── 상태 ────────────────────────────────────────────────────────
    public GameState CurrentState { get; private set; } = GameState.Waiting;

    /// <summary>
    /// 현재 남은 라운드 시간 (초). Playing 상태에서만 감소.
    /// </summary>
    public float RemainingTime { get; private set; }

    /// <summary>
    /// 카운트다운 남은 시간 (초). Countdown 상태에서만 감소.
    /// </summary>
    public float CountdownTime { get; private set; }

    // ── 이벤트 ──────────────────────────────────────────────────────
    /// <summary>
    /// 게임 상태가 바뀔 때 발행. UI가 구독하여 화면을 전환한다.
    /// </summary>
    public event Action<GameState> OnStateChanged;

    /// <summary>
    /// 게임 종료 시 1등 플레이어 이름과 함께 발행.
    /// </summary>
    public event Action<string> OnGameOver;

    // ── 내부 ────────────────────────────────────────────────────────
    private float gameOverTimer;

    /// <summary>
    /// Photon 서버 시간 기준 플레이 시작 시각.
    /// 모든 클라이언트가 동일한 남은 시간을 계산하는 데 사용.
    /// </summary>
    private double networkStartTime = -1;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        // Photon 네트워크: Room에 저장된 startTime이 있으면 동기화 시작
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode
            && PhotonNetwork.CurrentRoom != null)
        {
            object startTimeObj;
            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(PROP_START_TIME, out startTimeObj))
            {
                networkStartTime = (double)startTimeObj;
            }
        }

        // 게임 시작 (카운트다운 → 플레이)
        StartGame();
    }

    void Update()
    {
        switch (CurrentState)
        {
            case GameState.Countdown:
                HandleCountdown();
                break;
            case GameState.Playing:
                HandlePlaying();
                break;
            case GameState.GameOver:
                HandleGameOver();
                break;
        }
    }

    // ── 게임 시작 ────────────────────────────────────────────────────

    /// <summary>
    /// 게임을 시작한다. Waiting → Countdown 전이.
    /// </summary>
    public void StartGame()
    {
        // 플레이어 등록 (로컬에서는 씬의 MonkeyHealth를 자동 검색)
        RegisterAllPlayers();

        // 점수 초기화
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.ResetAllScores();

        // 카운트다운 시작
        CountdownTime = countdownDuration;
        SetState(GameState.Countdown);

        // 카운트다운 중에는 조작 잠금
        SetAllPlayersInput(false);
    }

    // ── 상태별 핸들러 ────────────────────────────────────────────────

    void HandleCountdown()
    {
        CountdownTime -= Time.deltaTime;

        if (CountdownTime <= 0f)
        {
            CountdownTime = 0f;
            RemainingTime = roundDuration;
            SetState(GameState.Playing);

            // 플레이 시작 → 조작 활성화
            SetAllPlayersInput(true);
        }
    }

    void HandlePlaying()
    {
        // Photon 네트워크 동기화: 서버 시간 기준으로 남은 시간 계산
        if (networkStartTime > 0 && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            // 카운트다운 시간도 포함하여 계산
            double elapsed = PhotonNetwork.Time - networkStartTime - countdownDuration;
            RemainingTime = Mathf.Max(0f, roundDuration - (float)elapsed);
        }
        else
        {
            // 오프라인/연습 모드: 로컬 타이머
            RemainingTime -= Time.deltaTime;
        }

        if (RemainingTime <= 0f)
        {
            RemainingTime = 0f;
            EndRound();
        }
    }

    void HandleGameOver()
    {
        gameOverTimer -= Time.deltaTime;

        if (gameOverTimer <= 0f)
        {
            // 멀티플레이: 로비로 복귀
            if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            {
                PhotonNetwork.LeaveRoom();
                UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
            }
            else
            {
                // 연습 모드: 자동으로 새 라운드 시작
                StartGame();
            }
        }
    }

    // ── 라운드 종료 ──────────────────────────────────────────────────

    void EndRound()
    {
        SetState(GameState.GameOver);

        // 조작 잠금
        SetAllPlayersInput(false);

        // 1등 판정
        string winnerName = "None";
        if (ScoreManager.Instance != null)
        {
            var ranking = ScoreManager.Instance.GetRanking();
            if (ranking.Count > 0)
                winnerName = ranking[0].playerName;
        }

        Debug.Log("[GameManager] 라운드 종료! 1등: " + winnerName);

        OnGameOver?.Invoke(winnerName);

        // 결과 표시 타이머
        gameOverTimer = gameOverDisplayDuration;
    }

    // ── 상태 전이 ────────────────────────────────────────────────────

    void SetState(GameState newState)
    {
        if (CurrentState == newState) return;

        CurrentState = newState;
        Debug.Log("[GameManager] 상태 전이 → " + newState);
        OnStateChanged?.Invoke(newState);
    }

    // ── 유틸리티 ─────────────────────────────────────────────────────

    /// <summary>
    /// 씬의 모든 MonkeyHealth를 찾아 ScoreManager에 등록한다.
    /// </summary>
    void RegisterAllPlayers()
    {
        if (ScoreManager.Instance == null) return;

        var allHealth = FindObjectsByType<MonkeyHealth>(FindObjectsSortMode.None);
        foreach (var health in allHealth)
        {
            ScoreManager.Instance.RegisterPlayer(health.gameObject.name);
        }
    }

    /// <summary>
    /// 씬의 모든 플레이어(PlayerController 보유)의 입력을 토글한다.
    /// </summary>
    void SetAllPlayersInput(bool enabled)
    {
        var controllers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        foreach (var pc in controllers)
        {
            pc.inputEnabled = enabled;
            var shooter = pc.GetComponent<PlayerShooter>();
            if (shooter != null) shooter.inputEnabled = enabled;
        }
    }

    // ── 외부 접근 ────────────────────────────────────────────────────

    /// <summary>
    /// 라운드 남은 시간을 "M:SS" 형태 문자열로 반환.
    /// </summary>
    public string GetFormattedTime()
    {
        int minutes = Mathf.FloorToInt(RemainingTime / 60f);
        int seconds = Mathf.FloorToInt(RemainingTime % 60f);
        return minutes + ":" + seconds.ToString("00");
    }

    /// <summary>
    /// 현재 게임이 플레이 중인지 여부.
    /// MonkeyHealth 등에서 리스폰 가능 여부 판단에 사용.
    /// </summary>
    public bool IsPlaying => CurrentState == GameState.Playing;
}
