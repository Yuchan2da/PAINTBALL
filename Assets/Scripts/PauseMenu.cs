using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;

/// <summary>
/// 인게임 ESC 일시정지 메뉴.
///
/// [동작 조건]
/// - GameManager.Playing 상태에서만 ESC 입력을 받는다.
/// - GameOver / Waiting / Countdown 중에는 ESC 무시.
///
/// [로비 복귀]
/// - Photon 연결 중: LeaveRoom() → GameManager.OnLeftRoom()에서 씬 전환.
/// - 연습 모드: SceneManager로 직접 LobbyScene 로드.
///
/// [커서 처리]
/// - 일시정지 시: 커서 표시 + 자유 이동.
/// - 재개 시: 커서 숨김 + 마우스 잠금.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    public static PauseMenu Instance { get; private set; }

    // ── UI 참조 ──────────────────────────────────────────────────────
    [Header("UI 참조")]
    [Tooltip("일시정지 패널 루트 오브젝트")]
    public GameObject pausePanel;

    [Tooltip("계속 하기 버튼")]
    public Button resumeButton;

    [Tooltip("로비로 나가기 버튼")]
    public Button leaveButton;

    // ── 상태 ─────────────────────────────────────────────────────────
    /// <summary>현재 일시정지 여부. PlayerController가 참조 가능.</summary>
    public bool IsPaused { get; private set; }

    // ── 생명주기 ─────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // 패널 기본 숨김
        if (pausePanel != null)
            pausePanel.SetActive(false);

        // 버튼 이벤트 바인딩
        if (resumeButton != null) resumeButton.onClick.AddListener(Resume);
        if (leaveButton  != null) leaveButton.onClick.AddListener(LeaveToLobby);
    }

    void Update()
    {
        if (!CanOpenPauseMenu()) return;

        if (Input.GetKeyDown(KeyCode.Escape))
            TogglePause();
    }

    // ── 외부 API ─────────────────────────────────────────────────────

    /// <summary>일시정지 상태를 토글한다.</summary>
    public void TogglePause()
    {
        if (IsPaused) Resume();
        else          Pause();
    }

    // ── 내부 동작 ────────────────────────────────────────────────────

    /// <summary>
    /// ESC 메뉴를 열 수 있는 상태인지 확인한다.
    /// Playing 상태에서만 허용.
    /// </summary>
    bool CanOpenPauseMenu()
    {
        if (GameManager.Instance == null) return false;

        var state = GameManager.Instance.CurrentState;
        return state == GameManager.GameState.Playing
            || state == GameManager.GameState.Countdown;
    }

    /// <summary>일시정지: 패널 표시 + 플레이어 입력 차단 + 커서 해제.</summary>
    void Pause()
    {
        IsPaused = true;

        if (pausePanel != null)
            pausePanel.SetActive(true);

        SetLocalPlayerInput(false);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    /// <summary>재개: 패널 숨김 + 플레이어 입력 복구 + 커서 잠금.</summary>
    public void Resume()
    {
        IsPaused = false;

        if (pausePanel != null)
            pausePanel.SetActive(false);

        // GameOver 상태가 아닐 때만 입력 복구
        if (GameManager.Instance != null && GameManager.Instance.IsPlaying)
            SetLocalPlayerInput(true);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    /// <summary>
    /// 게임을 나가 로비로 복귀한다.
    /// Photon 연결 중이면 LeaveRoom() → GameManager.OnLeftRoom()에서 씬 전환.
    /// </summary>
    void LeaveToLobby()
    {
        IsPaused = false;

        if (pausePanel != null)
            pausePanel.SetActive(false);

        if (SFXManager.Instance != null)
            SFXManager.Instance.StopBGM();

        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            // AutomaticallySyncScene 해제 → 상대방 씬이 강제 이동되지 않음
            PhotonNetwork.AutomaticallySyncScene = false;
            PhotonNetwork.LeaveRoom();
            // 이후 GameManager.OnLeftRoom()이 LobbyScene 로드
        }
        else
        {
            // 연습 모드: 직접 씬 전환
            UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
        }
    }

    /// <summary>
    /// 씬에서 로컬 플레이어(IsMine)만 찾아 입력 활성화 여부를 변경한다.
    /// </summary>
    void SetLocalPlayerInput(bool enabled)
    {
        var controllers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        foreach (var pc in controllers)
        {
            // PhotonView가 없거나 로컬 플레이어가 아니면 스킵
            var pv = pc.GetComponent<PhotonView>();
            if (pv != null && !pv.IsMine) continue;

            pc.inputEnabled = enabled;

            var shooter = pc.GetComponent<PlayerShooter>();
            if (shooter != null) shooter.inputEnabled = enabled;
        }
    }
}
