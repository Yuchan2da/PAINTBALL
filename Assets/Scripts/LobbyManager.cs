using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// 로비 씬 매니저.
/// Photon 마스터 서버에 접속한 뒤, 방 참가/생성을 처리한다.
/// [흐름]
/// 1. ConnectToPhoton() → 마스터 서버 연결
/// 2. OnConnectedToMaster() → 로비 참가
/// 3. OnJoinedLobby() → 자동으로 방 참가 시도
/// 4. OnJoinedRoom() → 게임 씬 로드
/// </summary>
public class LobbyManager : MonoBehaviourPunCallbacks
{
    [Header("UI 연결")]
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private Button connectButton;
    [SerializeField] private TMP_Text statusText;

    [Header("방 설정")]
    [SerializeField] private byte maxPlayersPerRoom = 4;
    [SerializeField] private string gameSceneName = "SampleScene";

    private bool isConnecting = false;

    void Start()
    {
        // 이전 연결이 남아있으면 끊기
        if (PhotonNetwork.IsConnected)
            PhotonNetwork.Disconnect();

        connectButton.onClick.AddListener(OnConnectClicked);
        SetStatusText("닉네임을 입력하고 게임 시작을 눌러주세요.");

        // 커서 표시 (로비에서는 마우스 보여야 함)
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>
    /// "게임 시작" 버튼 클릭 시 호출.
    /// </summary>
    void OnConnectClicked()
    {
        string nickname = nicknameInput.text.Trim();
        if (string.IsNullOrEmpty(nickname))
        {
            SetStatusText("닉네임을 입력해주세요!");
            return;
        }

        if (isConnecting) return;

        isConnecting = true;
        connectButton.interactable = false;

        // Photon 닉네임 설정
        PhotonNetwork.NickName = nickname;

        SetStatusText("서버 연결 중...");
        PhotonNetwork.ConnectUsingSettings();
    }

    // ───── Photon 콜백 ─────────────────────────────────────────

    public override void OnConnectedToMaster()
    {
        SetStatusText("마스터 서버 연결 완료! 로비 참가 중...");
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        SetStatusText("로비 참가 완료! 방 찾는 중...");

        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = maxPlayersPerRoom,
            IsVisible = true,
            IsOpen = true
        };

        PhotonNetwork.JoinOrCreateRoom("FFA_Room", roomOptions, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        SetStatusText("방 참가 완료! 게임 로딩 중...");

        PhotonNetwork.AutomaticallySyncScene = true;

        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.LoadLevel(gameSceneName);
        }
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        SetStatusText("방 참가 실패. 재시도 중...");

        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = maxPlayersPerRoom
        };
        PhotonNetwork.CreateRoom(null, roomOptions);
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        SetStatusText("방 생성 실패: " + message);
        isConnecting = false;
        connectButton.interactable = true;
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        SetStatusText("연결 끊김: " + cause);
        isConnecting = false;
        connectButton.interactable = true;
    }

    // ───── 유틸 ─────────────────────────────────────────────────

    private void SetStatusText(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
        Debug.Log("[Lobby] " + msg);
    }
}
