using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>방 목록의 한 줄.</summary>
public class LobbyRoomListItem : MonoBehaviour
{
    public TMP_Text nameText;
    public TMP_Text hostText;
    public TMP_Text countText;
    public Button joinButton;

    string _id;
    Action<string> _onJoin;

    void Awake()
    {
        joinButton.onClick.AddListener(() => _onJoin?.Invoke(_id));
    }

    public void Setup(string sessionId, string roomName, string host, int players, int maxPlayers, Action<string> onJoin)
    {
        _id = sessionId;
        _onJoin = onJoin;
        nameText.text = string.IsNullOrEmpty(roomName) ? "이름 없는 방" : roomName;
        hostText.text = "방장 " + host;
        countText.text = $"{players} / {maxPlayers}";
        bool full = players >= maxPlayers;
        joinButton.interactable = !full;
        var label = joinButton.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = full ? "가득 참" : "참가";
    }
}
