using UnityEngine;
using Photon.Pun;

/// <summary>
/// 게임 씬에서 Photon 네트워크 플레이어를 스폰하는 매니저.
/// 
/// [역할]
/// 1. 게임 씬 로드 시 로컬 플레이어를 네트워크 인스턴스로 생성
/// 2. SpawnManager의 스폰 포인트를 활용
/// 3. 로비로 복귀 처리
/// </summary>
public class NetworkManager : MonoBehaviourPunCallbacks
{
    [Header("스폰 설정")]
    [SerializeField] private string playerPrefabName = "Player";

    void Start()
    {
        // Photon 연결 상태 확인 (OfflineMode = 연습 모드도 허용)
        if (!PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            Debug.LogWarning("[NetworkManager] Photon 미연결! 로비로 이동합니다.");
            UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
            return;
        }

        SpawnPlayer();
    }

    /// <summary>
    /// 네트워크 플레이어를 스폰하고 로컬 시스템에 등록한다.
    /// [스폰 후 처리]
    /// 1. 닉네임 적용 → 킬피드/점수판에서 식별
    /// 2. GameHUD 등록 → HP/탄창 UI 연결
    /// 3. ScoreManager 등록 → 킬/데스 추적 시작
    /// </summary>
    void SpawnPlayer()
    {
        // SpawnManager에서 랜덤 스폰 위치
        Vector3 spawnPos = (SpawnManager.Instance != null)
            ? SpawnManager.Instance.GetRandomSpawnPoint()
            : Vector3.zero;

        // Photon 네트워크로 플레이어 생성 (Resources 폴더에서 로드)
        GameObject player = PhotonNetwork.Instantiate(
            playerPrefabName, spawnPos, Quaternion.identity
        );

        // ── 닉네임 적용 (킬피드/점수판에서 이 이름으로 표시) ──
        player.name = PhotonNetwork.NickName;

        // ── HUD에 로컬 플레이어 등록 (HP/탄창 UI 연결) ──
        if (GameHUD.Instance != null)
        {
            var shooter = player.GetComponent<PlayerShooter>();
            var health  = player.GetComponent<MonkeyHealth>();
            GameHUD.Instance.RegisterLocalPlayer(shooter, health);
        }

        // ── ScoreManager에 등록 (킬/데스 추적 시작) ──
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.RegisterPlayer(PhotonNetwork.NickName);
    }

    /// <summary>
    /// 연결이 끊기면 로비로 복귀.
    /// 단, GameOver에서 LeaveRoom()에 의한 정상 종료 시에는
    /// GameManager.OnLeftRoom()이 씬 전환을 담당하므로 여기서는 무시한다.
    /// </summary>
    public override void OnDisconnected(Photon.Realtime.DisconnectCause cause)
    {
        // GameOver 정상 흐름이면 GameManager.OnLeftRoom()에서 처리
        if (GameManager.Instance != null
            && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
        {
            Debug.Log($"[NetworkManager] GameOver 정상 종료 중 Disconnect({cause}) — 무시");
            return;
        }

        Debug.LogWarning($"[NetworkManager] 연결 끊김: {cause}. 로비로 이동.");
        PhotonNetwork.OfflineMode = false;
        UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
    }
}
