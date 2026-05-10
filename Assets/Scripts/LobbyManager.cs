using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using ExitGames.Client.Photon;  // Hashtable

/// <summary>
/// 로비 시스템 매니저.
/// 방 생성/목록/참가/준비/시작을 처리한다.
///
/// [흐름]
/// 1. Main Panel : 닉네임 입력 → 서버 연결 → 방 만들기 or 방 목록에서 참가 or 연습 모드
/// 2. Room Panel : 플레이어 목록 + 준비 토글 + 방장 시작 버튼
/// 3. 전원 준비 + 방장 시작 → 게임 씬 로드 (동시 시작)
///
/// [연습 모드] 혼자서 바로 게임에 들어가는 오프라인 모드.
///
/// [설계 원칙]
/// - 패널 전환은 ShowPanel()로 일원화
/// - Photon 연결은 ConnectToPhoton() → PendingAction으로 비동기 체이닝
/// - 모든 UI 참조는 null-safe 처리
/// - Custom Properties로 Ready 상태 동기화 (네트워크 최적화)
/// </summary>
public class LobbyManager : MonoBehaviourPunCallbacks
{
    // ───── 상수 ───────────────────────────────────────────────
    private const string PROP_IS_READY = "isReady";
    private const string PROP_ROOM_NAME = "roomName";
    private const string PROP_START_TIME = "startTime";
    private const int MIN_PLAYERS_TO_START = 2;

    // ───── UI: Main Panel ──────────────────────────────────────
    [Header("Main Panel")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button practiceButton;
    [SerializeField] private Button refreshButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Transform roomListContent;  // ScrollView Content
    [SerializeField] private GameObject roomListItemPrefab;

    // ───── UI: CreateRoom Panel ────────────────────────────────
    [Header("CreateRoom Panel")]
    [SerializeField] private GameObject createRoomPanel;
    [SerializeField] private TMP_InputField roomNameInput;
    // 최대 인원 선택기 (◀ 값 ▶ 버튼 방식, 씬의 MaxPlayersSelector에서 자동 탐색)
    private TMP_Text maxPlayersValueText;
    private int selectedMaxPlayersIndex = 2; // 0→2명, 1→3명, 2→4명
    private readonly int[] maxPlayersOptions = { 2, 3, 4 };
    [SerializeField] private Button confirmCreateButton;
    [SerializeField] private Button cancelCreateButton;

    // ───── UI: Room Panel (대기실) ─────────────────────────────
    [Header("Room Panel")]
    [SerializeField] private GameObject roomPanel;
    [SerializeField] private TMP_Text roomTitleText;
    [SerializeField] private TMP_Text[] playerSlotTexts;   // 최대 4슬롯
    [SerializeField] private TMP_Text[] playerReadyTexts;  // 준비 상태 텍스트
    [SerializeField] private Button readyButton;
    [SerializeField] private TMP_Text readyButtonText;
    [SerializeField] private Button startButton;           // 방장 전용
    [SerializeField] private TMP_Text startButtonText;
    [SerializeField] private Button leaveButton;

    // ───── UI: Settings Panel ──────────────────────────────────
    [Header("Settings Panel")]
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button settingsBackButton;

    // ───── 설정 ───────────────────────────────────────────────
    [Header("설정")]
    [SerializeField] private string gameSceneName = "SampleScene";

    // ───── 비동기 연결 상태 ────────────────────────────────────
    private enum PendingAction { None, CreateRoom, JoinLobby, Practice }
    private PendingAction pendingAction = PendingAction.None;
    private string pendingRoomName;
    private byte pendingMaxPlayers;

    // ───── 내부 상태 ──────────────────────────────────────────
    private bool isConnecting;
    private bool isReady;
    private readonly List<RoomInfo> cachedRoomList = new List<RoomInfo>();

    // ===================================================================
    //  Unity 라이프사이클
    // ===================================================================

    void Start()
    {
        // 커서 표시 (로비에서는 마우스 보여야 함)
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // OfflineMode 초기화 (연습 모드 → 로비 복귀 시 남아있을 수 있음)
        PhotonNetwork.OfflineMode = false;

        // 이전 연결이 남아있으면 끊기
        if (PhotonNetwork.IsConnected)
            PhotonNetwork.Disconnect();

        // 씬 동기화 활성화 (모든 클라이언트에서 필요)
        PhotonNetwork.AutomaticallySyncScene = true;

        // 버튼 이벤트 연결 (null-safe)
        BindButton(createRoomButton, OnCreateRoomClicked);
        BindButton(practiceButton, OnPracticeClicked);
        BindButton(refreshButton, OnRefreshClicked);
        BindButton(confirmCreateButton, OnConfirmCreateClicked);
        BindButton(cancelCreateButton, OnCancelCreateClicked);
        BindButton(readyButton, OnReadyClicked);
        BindButton(startButton, OnStartClicked);
        BindButton(leaveButton, OnLeaveClicked);
        BindButton(settingsButton, () => ShowPanel(settingsPanel));
        BindButton(settingsBackButton, () => {
            var csUI = settingsPanel != null ? settingsPanel.GetComponentInChildren<CrosshairSettingsUI>() : null;
            if (csUI != null) csUI.SaveSettings();
            ShowPanel(mainPanel);
        });

        // 초기 패널 상태
        ShowPanel(mainPanel);
        SetStatus("닉네임을 입력하고 방을 만들거나 참가하세요.");

        // 맥스 플레이어 드롭다운 초기화
        InitMaxPlayersSelector();
    }

    void OnDestroy()
    {
        // 메모리 정리: 방 목록 캐시 클리어
        cachedRoomList.Clear();
    }

    // ===================================================================
    //  초기화 헬퍼
    // ===================================================================

    /// <summary>버튼에 null-safe 리스너 바인딩.</summary>
    private void BindButton(Button btn, UnityEngine.Events.UnityAction action)
    {
        if (btn != null) btn.onClick.AddListener(action);
    }

    /// <summary>
    /// 최대 인원 버튼 셀렉터 초기화.
    /// 씬의 MaxPlayersSelector 오브젝트에서 ◀ / 값 / ▶ 요소를 찾아 연결한다.
    /// </summary>
    private void InitMaxPlayersSelector()
    {
        if (createRoomPanel == null) return;

        var selector = createRoomPanel.transform.Find("MaxPlayersSelector");
        if (selector == null)
        {
            Debug.LogWarning("[Lobby] MaxPlayersSelector not found in CreateRoomPanel");
            return;
        }

        // 중앙 값 텍스트
        var valueTf = selector.Find("ValueText");
        if (valueTf != null)
            maxPlayersValueText = valueTf.GetComponent<TMP_Text>();

        // ◀ 버튼
        var leftTf = selector.Find("LeftBtn");
        if (leftTf != null)
        {
            var btn = leftTf.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(OnMaxPlayersLeft);
        }

        // ▶ 버튼
        var rightTf = selector.Find("RightBtn");
        if (rightTf != null)
        {
            var btn = rightTf.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(OnMaxPlayersRight);
        }

        // 기본값 적용 (4명)
        selectedMaxPlayersIndex = 2;
        RefreshMaxPlayersDisplay();
    }

    private void OnMaxPlayersLeft()
    {
        selectedMaxPlayersIndex = Mathf.Max(0, selectedMaxPlayersIndex - 1);
        RefreshMaxPlayersDisplay();
    }

    private void OnMaxPlayersRight()
    {
        selectedMaxPlayersIndex = Mathf.Min(maxPlayersOptions.Length - 1, selectedMaxPlayersIndex + 1);
        RefreshMaxPlayersDisplay();
    }

    private void RefreshMaxPlayersDisplay()
    {
        if (maxPlayersValueText != null)
            maxPlayersValueText.text = maxPlayersOptions[selectedMaxPlayersIndex] + "명";
    }


    // ===================================================================
    //  패널 전환
    // ===================================================================

    /// <summary>
    /// 지정 패널만 활성화하고 나머지는 비활성화.
    /// 모든 패널 전환의 단일 진입점.
    /// </summary>
    private void ShowPanel(GameObject target)
    {
        if (mainPanel != null) mainPanel.SetActive(target == mainPanel);
        if (createRoomPanel != null) createRoomPanel.SetActive(target == createRoomPanel);
        if (roomPanel != null) roomPanel.SetActive(target == roomPanel);
        if (settingsPanel != null) settingsPanel.SetActive(target == settingsPanel);
    }

    // ===================================================================
    //  Main Panel 버튼
    // ===================================================================

    /// <summary>"방 만들기" 클릭 → CreateRoom 패널 표시</summary>
    private void OnCreateRoomClicked()
    {
        if (!ValidateNickname()) return;
        ShowPanel(createRoomPanel);
    }

    /// <summary>"연습 모드" 클릭 → 오프라인으로 바로 게임 씬 진입</summary>
    private void OnPracticeClicked()
    {
        if (!ValidateNickname()) return;

        PhotonNetwork.NickName = nicknameInput.text.Trim();

        // 이미 Photon에 연결된 상태면 먼저 끊어야 OfflineMode 전환 가능
        if (PhotonNetwork.IsConnected)
        {
            pendingAction = PendingAction.Practice;
            SetStatus("연습 모드 준비 중...");
            PhotonNetwork.Disconnect();
            return;
        }

        StartPracticeMode();
    }

    /// <summary>OfflineMode 활성화 후 게임 씬으로 직접 진입.</summary>
    private void StartPracticeMode()
    {
        PhotonNetwork.OfflineMode = true;
        PhotonNetwork.NickName = nicknameInput != null ? nicknameInput.text.Trim() : "Player";
        SetStatus("연습 모드 시작...");

        // OfflineMode에서는 CreateRoom으로 가상 방 생성 필요
        PhotonNetwork.CreateRoom("Practice");
    }

    /// <summary>"새로고침" 클릭 → 서버 연결/로비 참가하여 방 목록 갱신</summary>
    private void OnRefreshClicked()
    {
        if (!ValidateNickname()) return;
        pendingAction = PendingAction.JoinLobby;
        ConnectToPhoton();
    }

    // ===================================================================
    //  CreateRoom Panel 버튼
    // ===================================================================

    /// <summary>"만들기" 클릭 → Photon 서버 연결 후 방 생성</summary>
    private void OnConfirmCreateClicked()
    {
        string roomName = roomNameInput != null ? roomNameInput.text.Trim() : "";
        if (string.IsNullOrEmpty(roomName))
        {
            SetStatus("방 제목을 입력해주세요!");
            return;
        }

        // 맥스 플레이어 (드롭다운 index 0→2명, 1→3명, 2→4명)
        int maxPlayers = maxPlayersOptions[selectedMaxPlayersIndex];

        // 서버 연결 후 방 생성
        if (!PhotonNetwork.IsConnected)
        {
            pendingRoomName = roomName;
            pendingMaxPlayers = (byte)maxPlayers;
            pendingAction = PendingAction.CreateRoom;
            ConnectToPhoton();
        }
        else
        {
            CreatePhotonRoom(roomName, (byte)maxPlayers);
        }
    }

    /// <summary>"취소" 클릭 → Main Panel 복귀</summary>
    private void OnCancelCreateClicked()
    {
        ShowPanel(mainPanel);
    }

    // ===================================================================
    //  Room Panel 버튼
    // ===================================================================

    /// <summary>"준비" / "준비 해제" 토글</summary>
    private void OnReadyClicked()
    {
        isReady = !isReady;
        SetPlayerReadyProperty(isReady);
        UpdateReadyButtonText();
    }

    /// <summary>
    /// "게임 시작" — 방장 전용.
    /// 전원 준비 + 최소 2명 조건 충족 시 게임 시작.
    /// </summary>
    private void OnStartClicked()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        int playerCount = PhotonNetwork.CurrentRoom.PlayerCount;
        if (playerCount < MIN_PLAYERS_TO_START)
        {
            SetStatus($"최소 {MIN_PLAYERS_TO_START}명이 필요합니다!");
            return;
        }

        if (!AllPlayersReady())
        {
            SetStatus("모든 플레이어가 준비되지 않았습니다!");
            return;
        }

        // 추가 입장 차단
        PhotonNetwork.CurrentRoom.IsOpen = false;
        PhotonNetwork.CurrentRoom.IsVisible = false;

        // 시작 시간 저장 (타이머 동기화용)
        double startTime = PhotonNetwork.Time;
        Hashtable roomProps = new Hashtable { { PROP_START_TIME, startTime } };
        PhotonNetwork.CurrentRoom.SetCustomProperties(roomProps);

        // 동기 씬 로드 (AutomaticallySyncScene으로 전 클라이언트 동시 전환)
        PhotonNetwork.LoadLevel(gameSceneName);
    }

    /// <summary>"나가기" — 방 퇴장 → Main Panel</summary>
    private void OnLeaveClicked()
    {
        isReady = false;
        PhotonNetwork.LeaveRoom();
    }

    // ===================================================================
    //  Photon 연결
    // ===================================================================

    /// <summary>
    /// Photon 마스터 서버에 연결한다.
    /// 이미 연결되어 있으면 로비 참가로 스킵.
    /// </summary>
    private void ConnectToPhoton()
    {
        if (isConnecting) return;
        if (!ValidateNickname()) return;

        isConnecting = true;
        PhotonNetwork.NickName = nicknameInput.text.Trim();

        if (PhotonNetwork.IsConnected)
        {
            isConnecting = false;
            // 이미 연결됨 → 로비 참가 or 대기중인 액션 실행
            if (pendingAction == PendingAction.CreateRoom)
            {
                CreatePhotonRoom(pendingRoomName, pendingMaxPlayers);
                pendingAction = PendingAction.None;
            }
            else if (!PhotonNetwork.InLobby)
            {
                PhotonNetwork.JoinLobby();
            }
            else
            {
                OnJoinedLobby();
            }
            return;
        }

        SetStatus("서버 연결 중...");
        PhotonNetwork.ConnectUsingSettings();
    }

    // ===================================================================
    //  Photon 콜백
    // ===================================================================

    public override void OnConnectedToMaster()
    {
        isConnecting = false;
        SetStatus("서버 연결 완료!");

        if (pendingAction == PendingAction.CreateRoom)
        {
            CreatePhotonRoom(pendingRoomName, pendingMaxPlayers);
            pendingAction = PendingAction.None;
        }
        else
        {
            // 방 목록을 받으려면 로비 참가 필요
            PhotonNetwork.JoinLobby();
        }
    }

    public override void OnJoinedLobby()
    {
        isConnecting = false;
        SetStatus($"로비 참가 완료! 방 {cachedRoomList.Count}개");
        ShowPanel(mainPanel);
        RefreshRoomListUI();
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        UpdateCachedRoomList(roomList);
        RefreshRoomListUI();
    }

    public override void OnJoinedRoom()
    {
        // 연습 모드(OfflineMode)면 대기실 건너뛰고 바로 게임 씬 진입
        if (PhotonNetwork.OfflineMode)
        {
            SetStatus("연습 모드 시작!");
            UnityEngine.SceneManagement.SceneManager.LoadScene(gameSceneName);
            return;
        }

        SetStatus($"방 '{PhotonNetwork.CurrentRoom.Name}' 입장!");
        isReady = false;
        SetPlayerReadyProperty(false);

        ShowPanel(roomPanel);
        UpdateRoomPanel();
    }

    public override void OnLeftRoom()
    {
        ShowPanel(mainPanel);
        SetStatus("방에서 나왔습니다.");

        // 로비 재참가 (OfflineMode가 아닐 때만)
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.InLobby)
            PhotonNetwork.JoinLobby();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        UpdateRoomPanel();
        SetStatus($"{newPlayer.NickName}님이 입장했습니다.");
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        UpdateRoomPanel();
        SetStatus($"{otherPlayer.NickName}님이 퇴장했습니다.");
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (changedProps.ContainsKey(PROP_IS_READY))
            UpdateRoomPanel();
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        // 방장이 바뀌면 로컬 Ready 상태와 버튼 가시성 갱신
        if (PhotonNetwork.LocalPlayer.IsMasterClient)
        {
            isReady = false; // 방장은 Ready가 아닌 Start 사용
        }
        UpdateRoomPanel();
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        SetStatus("방 참가 실패: " + message);
        ShowPanel(mainPanel);
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        SetStatus("방 생성 실패: " + message);
        ShowPanel(mainPanel);
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        isConnecting = false;
        cachedRoomList.Clear();

        // 연습 모드 대기 중이었으면 → OfflineMode 진입
        if (pendingAction == PendingAction.Practice)
        {
            pendingAction = PendingAction.None;
            StartPracticeMode();
            return;
        }

        pendingAction = PendingAction.None;
        SetStatus("연결 끊김: " + cause);
        ShowPanel(mainPanel);
    }

    // ===================================================================
    //  내부 로직
    // ===================================================================

    /// <summary>Photon 방 생성 (옵션 설정 포함).</summary>
    private void CreatePhotonRoom(string roomName, byte maxPlayers)
    {
        RoomOptions options = new RoomOptions
        {
            MaxPlayers = maxPlayers,
            IsVisible = true,
            IsOpen = true,
            CustomRoomPropertiesForLobby = new string[] { PROP_ROOM_NAME },
            CustomRoomProperties = new Hashtable { { PROP_ROOM_NAME, roomName } }
        };
        PhotonNetwork.CreateRoom(roomName, options);
        SetStatus($"방 '{roomName}' 생성 중...");
    }

    /// <summary>닉네임 입력 검증.</summary>
    private bool ValidateNickname()
    {
        string nick = nicknameInput != null ? nicknameInput.text.Trim() : "";
        if (string.IsNullOrEmpty(nick))
        {
            SetStatus("닉네임을 입력해주세요!");
            return false;
        }
        return true;
    }

    /// <summary>로컬 플레이어의 Ready 프로퍼티 설정.</summary>
    private void SetPlayerReadyProperty(bool ready)
    {
        Hashtable props = new Hashtable { { PROP_IS_READY, ready } };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    /// <summary>방장을 제외한 모든 플레이어가 Ready인지 확인.</summary>
    private bool AllPlayersReady()
    {
        foreach (Player p in PhotonNetwork.PlayerList)
        {
            if (p.IsMasterClient) continue; // 방장은 Start 버튼 사용

            object readyObj;
            if (!p.CustomProperties.TryGetValue(PROP_IS_READY, out readyObj))
                return false; // 프로퍼티 없으면 미준비

            if (!(bool)readyObj) return false;
        }
        return true;
    }

    /// <summary>
    /// 방 목록 캐시를 Photon의 delta 업데이트로 갱신.
    /// 삭제/비공개/만석 방은 제거하고 열린 방만 유지.
    /// </summary>
    private void UpdateCachedRoomList(List<RoomInfo> deltaList)
    {
        foreach (RoomInfo info in deltaList)
        {
            // 기존 항목 제거 (업데이트 또는 삭제)
            int index = cachedRoomList.FindIndex(r => r.Name == info.Name);
            if (index >= 0)
                cachedRoomList.RemoveAt(index);

            // 유효한 방만 다시 추가
            if (!info.RemovedFromList && info.IsVisible && info.IsOpen)
                cachedRoomList.Add(info);
        }
    }

    // ===================================================================
    //  UI 갱신
    // ===================================================================

    /// <summary>방 목록 ScrollView UI를 캐시 기반으로 재구성.</summary>
    private void RefreshRoomListUI()
    {
        if (roomListContent == null || roomListItemPrefab == null) return;

        // 기존 항목 삭제
        for (int i = roomListContent.childCount - 1; i >= 0; i--)
            Destroy(roomListContent.GetChild(i).gameObject);

        // 방 목록 생성
        foreach (RoomInfo room in cachedRoomList)
        {
            GameObject item = Instantiate(roomListItemPrefab, roomListContent);
            RoomListItem listItem = item.GetComponent<RoomListItem>();
            if (listItem != null)
            {
                // 클로저 캡처를 위해 로컬 변수 사용
                string roomName = room.Name;
                listItem.Setup(roomName, room.PlayerCount, room.MaxPlayers, () =>
                {
                    if (!PhotonNetwork.IsConnected)
                    {
                        SetStatus("서버에 연결되어 있지 않습니다. 새로고침을 눌러주세요.");
                        return;
                    }
                    PhotonNetwork.JoinRoom(roomName);
                    SetStatus($"방 '{roomName}' 참가 중...");
                });
            }
        }

        if (cachedRoomList.Count == 0)
            SetStatus("현재 열린 방이 없습니다. 방을 만들어보세요!");
    }

    /// <summary>Room Panel의 플레이어 슬롯과 버튼 상태를 갱신.</summary>
    private void UpdateRoomPanel()
    {
        if (!PhotonNetwork.InRoom) return;

        // 방 제목
        if (roomTitleText != null)
            roomTitleText.text = PhotonNetwork.CurrentRoom.Name;

        // 플레이어 슬롯 갱신 (방 최대인원에 맞춰 초과 슬롯 숨김)
        Player[] players = PhotonNetwork.PlayerList;
        int maxPlayers = PhotonNetwork.CurrentRoom.MaxPlayers;

        for (int i = 0; i < playerSlotTexts.Length; i++)
        {
            // 방 최대인원 초과 슬롯은 완전 숨김
            if (i >= maxPlayers)
            {
                HidePlayerSlot(i);
                continue;
            }

            if (i < players.Length)
            {
                UpdatePlayerSlot(i, players[i]);
            }
            else
            {
                ClearPlayerSlot(i);
            }
        }

        // 버튼 상태 갱신
        UpdateReadyButtonText();
        UpdateStartButton();

        // 준비 버튼: 방장이 아닌 경우에만 표시
        if (readyButton != null)
            readyButton.gameObject.SetActive(!PhotonNetwork.IsMasterClient);
    }

    /// <summary>개별 플레이어 슬롯 UI 갱신.</summary>
    private void UpdatePlayerSlot(int index, Player player)
    {
        if (index >= playerSlotTexts.Length) return;

        string nick = player.NickName;
        if (player.IsMasterClient) nick += " ★";

        playerSlotTexts[index].text = nick;
        playerSlotTexts[index].gameObject.SetActive(true);

        if (index >= playerReadyTexts.Length || playerReadyTexts[index] == null) return;

        if (player.IsMasterClient)
        {
            playerReadyTexts[index].text = "방장";
            playerReadyTexts[index].color = new Color(1f, 0.8f, 0.2f); // 금색
        }
        else
        {
            bool ready = false;
            object readyObj;
            if (player.CustomProperties.TryGetValue(PROP_IS_READY, out readyObj))
                ready = (bool)readyObj;

            playerReadyTexts[index].text = ready ? "✅ 준비됨" : "❌ 대기중";
            playerReadyTexts[index].color = ready
                ? new Color(0.2f, 0.9f, 0.3f)
                : new Color(0.9f, 0.3f, 0.3f);
        }
        playerReadyTexts[index].gameObject.SetActive(true);
    }

    /// <summary>빈 플레이어 슬롯 초기화.</summary>
    private void ClearPlayerSlot(int index)
    {
        if (index >= playerSlotTexts.Length) return;

        playerSlotTexts[index].text = "빈 슬롯";
        playerSlotTexts[index].gameObject.SetActive(true);

        if (index < playerReadyTexts.Length && playerReadyTexts[index] != null)
        {
            playerReadyTexts[index].text = "";
            playerReadyTexts[index].gameObject.SetActive(false);
        }
    }

    /// <summary>방 최대인원 초과 슬롯을 완전히 숨긴다.</summary>
    private void HidePlayerSlot(int index)
    {
        if (index < playerSlotTexts.Length && playerSlotTexts[index] != null)
            playerSlotTexts[index].gameObject.SetActive(false);

        if (index < playerReadyTexts.Length && playerReadyTexts[index] != null)
            playerReadyTexts[index].gameObject.SetActive(false);
    }

    /// <summary>시작 버튼 상태 갱신 (방장 전용).</summary>
    private void UpdateStartButton()
    {
        if (startButton == null) return;

        bool isMaster = PhotonNetwork.IsMasterClient;
        startButton.gameObject.SetActive(isMaster);

        if (isMaster)
        {
            bool canStart = AllPlayersReady()
                && PhotonNetwork.CurrentRoom.PlayerCount >= MIN_PLAYERS_TO_START;
            startButton.interactable = canStart;

            if (startButtonText != null)
                startButtonText.text = canStart ? "게임 시작!" : "대기중...";
        }
    }

    /// <summary>준비 버튼 텍스트 토글.</summary>
    private void UpdateReadyButtonText()
    {
        if (readyButtonText != null)
            readyButtonText.text = isReady ? "준비 해제" : "준비";
    }

    /// <summary>상태 텍스트 갱신 + 디버그 로그.</summary>
    private void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
        Debug.Log("[Lobby] " + msg);
    }
}
