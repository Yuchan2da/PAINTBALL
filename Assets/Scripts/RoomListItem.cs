
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 방 목록의 개별 항목 UI.
/// 방 이름, 인원수, 참가 버튼을 표시한다.
/// LobbyManager.RefreshRoomListUI()에서 동적 생성됨.
/// </summary>
public class RoomListItem : MonoBehaviour
{
    [SerializeField] private TMP_Text roomNameText;
    [SerializeField] private TMP_Text playerCountText;
    [SerializeField] private Button joinButton;

    /// <summary>
    /// 방 정보를 세팅하고 참가 버튼에 콜백을 연결한다.
    /// </summary>
    public void Setup(string roomName, int currentPlayers, int maxPlayers, System.Action onJoin)
    {
        if (roomNameText != null)
            roomNameText.text = roomName;

        if (playerCountText != null)
            playerCountText.text = $"{currentPlayers}/{maxPlayers}";

        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(() => onJoin?.Invoke());

            // 방이 꽉 찼으면 버튼 비활성화
            joinButton.interactable = currentPlayers < maxPlayers;
        }
    }
}
